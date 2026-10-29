using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;
using C = AlplaPortal.Domain.Constants.PoGroupCancellationConstants;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// D4 replacement-P.O. block and D15 number reuse through the REAL endpoints (InMemory): RegisterPo refuses while the
/// prior ECF confirmation is pending or rejected and accepts after confirmation; the P.O. document upload is refused
/// on the same condition; a sibling group is never blocked; the same number as the cancelled P.O. is warned, then
/// accepted with the Buyer's explanation and audited; the ordinary P.O. correction flow (WAITING_PO_CORRECTION group
/// without any cancellation) is unchanged. The UPDLOCK path is relational-only (PoGroupCancellationRelationalTests).
/// </summary>
public class PoCancellationRegisterPoGuardTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_pocancel_" + Guid.NewGuid());
    public PoCancellationRegisterPoGuardTests() => Directory.CreateDirectory(_scratchDir);
    public void Dispose() { try { Directory.Delete(_scratchDir, true); } catch { } }

    private static RequestsController Buyer(ApplicationDbContext ctx, Guid buyerId)
    {
        var options = new PostPaymentCompletionOptions();
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>(); orchestrator.Setup(o => o.EmitAsync(It.IsAny<Domain.Events.WorkflowEvent>())).Returns(Task.CompletedTask);
        var ctl = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, S.AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(options));
        ctl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, buyerId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return ctl;
    }

    private AttachmentsController Attachments(ApplicationDbContext ctx, Guid actorId)
    {
        var env = new Mock<IWebHostEnvironment>(); env.Setup(e => e.ContentRootPath).Returns(_scratchDir);
        var config = new Mock<IConfiguration>(); config.Setup(c => c[It.IsAny<string>()]).Returns(_scratchDir);
        var security = Options.Create(new SecurityOptions { Upload = new UploadOptions { AllowedExtensions = new List<string> { ".pdf" }, BlockedExtensions = new List<string>(), MaxFileSizeBytes = 10_000_000 } });
        var ctl = new AttachmentsController(ctx, env.Object, security, config.Object);
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")) } };
        return ctl;
    }

    private static IFormFile Pdf(string name = "po.pdf") { var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 test"); return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name) { Headers = new HeaderDictionary(), ContentType = "application/pdf" }; }

    // v2.249.4 — no server-held extraction evidence in these seeds → the document is UNCERTAIN and the Buyer's explicit confirmation + justification is required (audited).
    private static RegisterPoActionDto Dto(Guid groupId, string number = "ECF10 2026/500") => new() { PoGroupId = groupId, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, PurchaseOrderNumber = number, DocumentTypeConfirmed = true, Comment = "ZZTEST tipo de documento confirmado manualmente (sem extração registada no teste)" };

    /// <summary>Cancel group A (restart) then approve a new batch #2 whose group C covers line 1 again (WAITING_PO, P.O. file attached).</summary>
    private static async Task<(S.Seed Seed, Guid CancellationId, Guid GroupC)> RestartedAsync(ApplicationDbContext ctx, string? reuseNumber = null)
    {
        var s = await S.SeedAsync(ctx, requestStatus: "PO_ISSUED");
        var (orch, _) = S.OrchestratorMock();
        var r = await S.Service(ctx, orch.Object).CancelAsync(S.Cancel(s, S.Buyer(s.Owner)));
        Assert.True(r.Succeeded, r.Detail);
        var cancellationId = ((PoGroupCancellationResultDto)r.Payload!).CancellationId;
        ctx.ChangeTracker.Clear();
        var batch2 = Guid.NewGuid(); var groupC = Guid.NewGuid();
        ctx.ApprovalBatches.Add(new ApprovalBatch { Id = batch2, RequestId = s.RequestId, BatchNumber = 2, Status = "APPROVED", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Owner });
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = groupC, RequestId = s.RequestId, SupplierId = S.SupplierA, SupplierNameSnapshot = "PRODUCTIVE SYSTEMS", CurrencyCode = "RND", PlantId = S.Plant, TotalAmount = 24000m, Status = "WAITING_PO", ApprovalBatchId = batch2, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Owner });
        ctx.ApprovalBatchItems.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch2, RequestLineItemId = s.Line1, SelectedQuotationItemId = s.Qi1Alt, CreatedAtUtc = DateTime.UtcNow });
        var line = await ctx.RequestLineItems.SingleAsync(l => l.Id == s.Line1); line.RequestPoGroupId = groupC; line.QuotationLifecycleStatus = "QUOTATION_APPROVED"; line.SelectedQuotationItemId = s.Qi1Alt;
        ctx.RequestAttachments.Add(new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, RequestPoGroupId = groupC, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = "po-c.pdf", FileExtension = ".pdf", StorageReference = "po-c.pdf", UploadedByUserId = s.Owner, UploadedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return (s, cancellationId, groupC);
    }

    private static string Code(IActionResult r) => (string)Assert.IsType<ProblemDetails>(((ObjectResult)r).Value).Extensions["code"]!;

    [Fact]
    public async Task Replacement_PO_is_blocked_while_pending_or_rejected_and_allowed_after_Finance_confirmation()
    {
        await using var ctx = S.NewCtx();
        var (s, cancellationId, groupC) = await RestartedAsync(ctx);
        var (orch, _) = S.OrchestratorMock(); var svc = S.Service(ctx, orch.Object);

        var blocked = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(groupC));
        var pd = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(blocked).Value);
        Assert.Equal(C.ErrorCodes.PriorEcfConfirmationPending, pd.Extensions["code"]); Assert.Contains("ECF10 2026/364", pd.Detail);
        ctx.ChangeTracker.Clear();
        Assert.Equal("WAITING_PO", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupC)).Status);   // nothing written
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO" && h.Comment!.Contains(groupC.ToString().Substring(0, 8))));

        Assert.True((await svc.FinanceReviewAsync(new FinanceReviewCommand(cancellationId, S.Finance(s.Finance1), false, "ECF ainda ativa no Primavera; anular primeiro.", null))).Succeeded);
        ctx.ChangeTracker.Clear();
        Assert.Equal(C.ErrorCodes.PriorEcfConfirmationPending, Code(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(groupC))));

        Assert.True((await svc.FinanceReviewAsync(new FinanceReviewCommand(cancellationId, S.Finance(s.Finance2), true, null, null))).Succeeded);
        ctx.ChangeTracker.Clear();
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(groupC)));
        ctx.ChangeTracker.Clear();
        var c = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupC);
        Assert.Equal("PO_ISSUED", c.Status); Assert.Equal("ECF10 2026/500", c.PurchaseOrderNumber);
        Assert.Equal("CANCELLED", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).Status);
    }

    [Fact]
    public async Task Sibling_group_registers_normally_while_the_restarted_line_is_blocked()
    {
        await using var ctx = S.NewCtx();
        var (s, _, _) = await RestartedAsync(ctx);
        ctx.RequestAttachments.Add(new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, RequestPoGroupId = s.GroupB, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = "po-b.pdf", FileExtension = ".pdf", StorageReference = "po-b.pdf", UploadedByUserId = s.Owner, UploadedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, "ECF11 2026/777")));
        Assert.Equal("PO_ISSUED", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).Status);
    }

    [Fact]
    public async Task PO_document_upload_is_refused_for_a_blocked_group()
    {
        await using var ctx = S.NewCtx();
        var (s, _, groupC) = await RestartedAsync(ctx);
        var result = await Attachments(ctx, s.Owner).Upload(s.RequestId, new List<IFormFile> { Pdf() }, null, "PO", groupC);
        var pd = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(result).Value);
        Assert.Equal(C.ErrorCodes.PriorEcfConfirmationPending, pd.Extensions["code"]);
        var ok = await Attachments(ctx, s.Owner).Upload(s.RequestId, new List<IFormFile> { Pdf("po-b.pdf") }, null, "PO", s.GroupB);
        Assert.IsType<OkObjectResult>(ok);
        var evidence = await Attachments(ctx, s.Owner).Upload(s.RequestId, new List<IFormFile> { Pdf("ecf-anulada.pdf") }, null, "PO_CANCELLATION", s.GroupA);
        Assert.IsType<OkObjectResult>(evidence);   // evidence of the external annulment may be attached to the cancelled group
    }

    [Fact]
    public async Task Same_number_as_the_cancelled_PO_is_warned_then_accepted_with_an_explanation_and_audited()
    {
        await using var ctx = S.NewCtx();
        var (s, cancellationId, groupC) = await RestartedAsync(ctx);
        var (orch, _) = S.OrchestratorMock(); var svc = S.Service(ctx, orch.Object);
        Assert.True((await svc.FinanceReviewAsync(new FinanceReviewCommand(cancellationId, S.Finance(s.Finance1), true, null, null))).Succeeded);
        ctx.ChangeTracker.Clear();

        var warned = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(groupC, " ecf10 2026/364 "));
        var pd = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(warned).Value);
        Assert.Equal("PREVIOUS_PO_NUMBER_REUSED", pd.Title); Assert.Equal(C.ErrorCodes.PreviousPoNumberReused, pd.Extensions["code"]); Assert.Equal(cancellationId, pd.Extensions["cancellationId"]);
        ctx.ChangeTracker.Clear();
        Assert.Equal("WAITING_PO", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupC)).Status);

        var tooShort = Dto(groupC, "ECF10 2026/364"); tooShort.PreviousPoNumberReuseConfirmed = true; tooShort.PreviousPoNumberReuseComment = "curto";
        Assert.Equal(C.ErrorCodes.PreviousPoNumberReused, Code(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, tooShort)));

        var confirmed = Dto(groupC, "ECF10 2026/364"); confirmed.PreviousPoNumberReuseConfirmed = true; confirmed.PreviousPoNumberReuseComment = "O Primavera reemitiu a ECF com o mesmo número após a anulação.";
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, confirmed));
        ctx.ChangeTracker.Clear();
        Assert.Equal("ECF10 2026/364", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupC)).PurchaseOrderNumber);
        var audit = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == C.HistoryActions.PoNumberReused);
        Assert.Contains("reemitiu a ECF", audit.Comment); Assert.Contains(cancellationId.ToString(), audit.Comment);
        // a different number needs no confirmation at all
        await using var ctx2 = S.NewCtx();
        var (s2, cid2, groupC2) = await RestartedAsync(ctx2);
        var (orch2, _) = S.OrchestratorMock(); Assert.True((await S.Service(ctx2, orch2.Object).FinanceReviewAsync(new FinanceReviewCommand(cid2, S.Finance(s2.Finance1), true, null, null))).Succeeded); ctx2.ChangeTracker.Clear();
        Assert.IsType<OkObjectResult>(await Buyer(ctx2, s2.Owner).RegisterPo(s2.RequestId, Dto(groupC2, "ECF10 2026/999")));
    }

    [Fact]
    public async Task Ordinary_PO_correction_without_any_cancellation_is_unchanged()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx, groupAStatus: "WAITING_PO_CORRECTION", requestStatus: "WAITING_PO_CORRECTION", legacyReturnReason: "P.O com valor errado");
        var ok = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF10 2026/364-B"));
        Assert.IsType<OkObjectResult>(ok);
        ctx.ChangeTracker.Clear();
        Assert.Equal("PO_ISSUED", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).Status);
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REREGISTER_PO"));
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == C.HistoryActions.PoNumberReused));
    }

    [Fact]
    public async Task Released_lines_can_enter_a_new_batch_but_held_lines_cannot()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx, requestStatus: "PO_ISSUED");
        var (orch, _) = S.OrchestratorMock();
        Assert.True((await S.Service(ctx, orch.Object).CancelAsync(S.Cancel(s, S.Buyer(s.Owner)))).Succeeded);
        ctx.ChangeTracker.Clear();
        // the CreateBatch "held" rule, as the controller evaluates it: released items of APPROVED batches are excluded
        var held = await ctx.ApprovalBatchItems.AsNoTracking()
            .Where(bi => bi.ApprovalBatch.RequestId == s.RequestId && bi.ApprovalBatch.Status == "APPROVED" && bi.ReleasedAtUtc == null)
            .Select(bi => bi.RequestLineItemId).ToListAsync();
        Assert.DoesNotContain(s.Line1, held); Assert.Contains(s.Line2, held);
        Assert.Equal("QUOTATION_PENDING", (await ctx.RequestLineItems.AsNoTracking().SingleAsync(l => l.Id == s.Line1)).QuotationLifecycleStatus);
    }
}
