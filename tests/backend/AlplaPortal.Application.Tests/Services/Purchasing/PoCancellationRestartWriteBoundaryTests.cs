using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Approvals;
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
using Proj = AlplaPortal.Domain.Services.BuyerQueueProjectionBuilder;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// v2.249.2 — write-side boundaries of the restart-quotation flow, exercised on the REAL cancellation service output
/// (PoCancellationTestSeed: group A PO_ISSUED → cancelled "voltar a cotar", line 1 released with its historical
/// candidates, group B untouched, Finance confirmation PENDING):
///  • the coverage projection agrees with CreateBatch (released line pending, nothing to submit);
///  • fresh approvals are NOT blocked by the pending Finance confirmation — a NEW quotation enters a new batch, while the
///    historical candidates are refused until explicitly re-authorized (DEC-147 / D3);
///  • the item-level window never lets the submitted/approved quotation evidence be edited or deleted (§7 lock);
///  • a mixed payload (released line + committed line) fails as a whole and persists nothing;
///  • a PROFORMA document linked to a submitted quotation is never removed, a fresh one still can be.
/// The replacement-P.O. block itself is covered by PoCancellationRegisterPoGuardTests.
/// </summary>
public class PoCancellationRestartWriteBoundaryTests : IDisposable
{
    private readonly string _scratchDir;
    public PoCancellationRestartWriteBoundaryTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_restart_boundary_" + Guid.NewGuid());
        Directory.CreateDirectory(_scratchDir);
    }
    public void Dispose() { try { Directory.Delete(_scratchDir, true); } catch { /* best-effort */ } }

    // ── harness ──

    private static ControllerContext Http(Guid actorId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() }
        };
    }

    private static RequestsController Requests(ApplicationDbContext ctx, Guid actorId)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new ApprovalRoutingResultDto());
        var statusSync = new Mock<IRequestStatusSyncService>();
        statusSync.Setup(s => s.ComputeDisplayWorkflowStateAsync(It.IsAny<Guid>())).ReturnsAsync(string.Empty);
        var c = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            statusSync.Object,
            routing.Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new QuotationItemEligibilityService(ctx),
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(new PostPaymentCompletionOptions()));
        c.ControllerContext = Http(actorId, RoleConstants.Buyer);
        return c;
    }

    private static ApprovalBatchController Batches(ApplicationDbContext ctx, Guid actorId)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto { Managers = { new AreaManagerDto { UserId = Guid.NewGuid(), FullName = "ZZ Manager" } } });
        routing.Setup(r => r.IsAreaManagerAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(false);
        var c = new ApprovalBatchController(
            ctx,
            NullLogger<ApprovalBatchController>.Instance,
            new Mock<IRequestStatusSyncService>().Object,
            new GroupBuilderService(ctx),
            routing.Object,
            new QuotationItemEligibilityService(ctx),
            new BatchExtraItemDecisionService(ctx),
            new AdjustmentCycleService(ctx),
            new Mock<IWorkflowNotificationOrchestrator>().Object);
        c.ControllerContext = Http(actorId, RoleConstants.Buyer);
        return c;
    }

    private AttachmentsController Attachments(ApplicationDbContext ctx, Guid actorId)
    {
        var env = new Mock<IWebHostEnvironment>(); env.Setup(e => e.ContentRootPath).Returns(_scratchDir);
        var cfg = new Mock<IConfiguration>(); cfg.Setup(c => c[It.IsAny<string>()]).Returns(_scratchDir);
        var sec = Options.Create(new SecurityOptions { Upload = new UploadOptions { AllowedExtensions = new List<string> { ".pdf" }, BlockedExtensions = new List<string>(), MaxFileSizeBytes = 10_000_000 } });
        var c = new AttachmentsController(ctx, env.Object, sec, cfg.Object);
        c.ControllerContext = Http(actorId, RoleConstants.Buyer);
        return c;
    }

    /// <summary>Seed + REAL cancellation of group A ("voltar a cotar", ECF declared cancelled → Finance confirmation PENDING).</summary>
    private static async Task<S.Seed> SeedAndRestartAsync(ApplicationDbContext ctx)
    {
        var s = await S.SeedAsync(ctx);
        var (orch, _) = S.OrchestratorMock();
        var result = await S.Service(ctx, orch.Object).CancelAsync(S.Cancel(s, S.Buyer(s.Owner)));
        Assert.True(result.Succeeded, "seed cancellation must succeed");
        ctx.ChangeTracker.Clear();
        var c = await ctx.RequestPoGroupCancellations.AsNoTracking().SingleAsync(x => x.RequestPoGroupId == s.GroupA);
        Assert.Equal(PoGroupCancellationConstants.FinanceConfirmationStatuses.Pending, c.FinanceConfirmationStatus);
        return s;
    }

    private static SaveQuotationRequestDto NewQuotationDto(params (Guid lineId, int lineNumber, string description)[] lines) => new()
    {
        SupplierId = S.SupplierB, SupplierNameSnapshot = "OUTRO FORNECEDOR", Currency = "AOA", SourceType = "MANUAL",
        Items = lines.Select(l => new SaveQuotationItemDto { LineNumber = l.lineNumber, Description = l.description, Quantity = 1, UnitPrice = 20_000m, MappedRequestLineItemId = l.lineId, ReconciliationStatus = "MAPPED" }).ToList(),
    };

    private static async Task<Request> LoadForProjectionAsync(ApplicationDbContext ctx, Guid requestId) =>
        await ctx.Requests.AsNoTracking()
            .Include(r => r.RequestType).Include(r => r.Status).Include(r => r.NeedLevel)
            .Include(r => r.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(r => r.ApprovalBatches).ThenInclude(b => b.Items).ThenInclude(bi => bi.Candidates)
            .Include(r => r.PoGroups)
            .Include(r => r.Quotations).ThenInclude(q => q.Items)
            .Include(r => r.QuotationReuseAuthorizations)
            .Include(r => r.Attachments)
            .SingleAsync(r => r.Id == requestId);

    // ── 1. projection agrees with the batch endpoint ──
    [Fact]
    public async Task After_a_real_restart_the_projection_is_PartialCoverage_with_AddQuotation_and_nothing_to_submit()
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);
        var r = await LoadForProjectionAsync(ctx, s.RequestId);
        var input = BuyerQueueProjectionInputFactory.FromRequest(r);
        var p = Proj.Build(input, s.Owner, DateTime.UtcNow);
        var buckets = Proj.ClassifyItemCoverage(input);
        Assert.Equal(BuyerQueueConstants.CoverageBuckets.PendingQuotation, buckets[s.Line1]); // old candidates Qi1/Qi1Alt are reuse-blocked
        Assert.Equal(BuyerQueueConstants.CoverageBuckets.Approved, buckets[s.Line2]);
        Assert.Equal(BuyerQueueConstants.OperationalStates.PartialCoverage, p.OperationalState);
        Assert.Contains(p.NextBuyerActions, a => a.Code == BuyerQueueConstants.ActionCodes.AddQuotation && a.Actionable);
        Assert.DoesNotContain(p.NextBuyerActions, a => a.Code == BuyerQueueConstants.ActionCodes.SubmitBatch);
        Assert.Contains(s.Qi1, QuotationReuseEligibilityPolicy.ReuseBlockedQuotationItemIds(r));
        Assert.Contains(s.Qi1Alt, QuotationReuseEligibilityPolicy.ReuseBlockedQuotationItemIds(r));
        Assert.DoesNotContain(s.Qi2, QuotationReuseEligibilityPolicy.ReuseBlockedQuotationItemIds(r)); // still held by group B's item
    }

    // ── 2. fresh approvals while the Finance confirmation is pending; historical candidates refused ──
    [Fact]
    public async Task CreateBatch_refuses_the_historical_candidate_and_accepts_a_new_quotation_while_confirmation_is_pending()
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);

        var oldCandidate = new CreateApprovalBatchDto { Items = new List<BatchItemDto> { new() { RequestLineItemId = s.Line1, Candidates = new List<BatchCandidateInputDto> { new() { QuotationItemId = s.Qi1 } } } } };
        var refused = Assert.IsAssignableFrom<ObjectResult>(await Batches(ctx, s.Owner).CreateBatch(s.RequestId, oldCandidate));
        Assert.Equal(409, refused.StatusCode);
        Assert.Equal("QUOTATION_REUSE_NOT_AUTHORIZED", ((ProblemDetails)refused.Value!).Extensions["code"]?.ToString());
        Assert.Equal(1, await ctx.ApprovalBatches.CountAsync(b => b.RequestId == s.RequestId));

        // A NEW quotation for the released line (the Buyer's normal way out) → new batch, no Finance gate.
        var saved = await Requests(ctx, s.Owner).SaveQuotation(s.RequestId, null, NewQuotationDto((s.Line1, 1, "Belt — nova proposta")));
        Assert.IsType<OkObjectResult>(saved.Result);
        ctx.ChangeTracker.Clear();
        var newItem = await ctx.QuotationItems.AsNoTracking().Where(qi => qi.MappedRequestLineItemId == s.Line1 && qi.QuotationId != s.Q1).Select(qi => qi.Id).SingleAsync();
        var fresh = new CreateApprovalBatchDto { Items = new List<BatchItemDto> { new() { RequestLineItemId = s.Line1, Candidates = new List<BatchCandidateInputDto> { new() { QuotationItemId = newItem } } } } };
        var ok = Assert.IsType<OkObjectResult>(await Batches(ctx, s.Owner).CreateBatch(s.RequestId, fresh));
        var batch = Assert.IsType<ApprovalBatchDto>(ok.Value);
        ctx.ChangeTracker.Clear();
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, (await ctx.ApprovalBatches.AsNoTracking().SingleAsync(b => b.Id == batch.Id)).Status);
        Assert.Equal(2, await ctx.ApprovalBatches.CountAsync(b => b.RequestId == s.RequestId));
        // Sibling group, its approved line and the pending confirmation are untouched.
        Assert.Equal("WAITING_PO", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).Status);
        Assert.Equal(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, (await ctx.RequestLineItems.AsNoTracking().SingleAsync(l => l.Id == s.Line2)).QuotationLifecycleStatus);
        Assert.Equal(PoGroupCancellationConstants.FinanceConfirmationStatuses.Pending, (await ctx.RequestPoGroupCancellations.AsNoTracking().SingleAsync(x => x.RequestPoGroupId == s.GroupA)).FinanceConfirmationStatus);
    }

    // ── 3. the submitted/approved quotation evidence is never editable or deletable on the item window ──
    [Fact]
    public async Task Submitted_quotation_of_the_released_line_cannot_be_edited_or_deleted()
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);
        var c = Requests(ctx, s.Owner);

        var put = Assert.IsType<ConflictObjectResult>((await c.UpdateQuotation(s.RequestId, s.Q1, NewQuotationDto((s.Line1, 1, "Belt editado")))).Result);
        Assert.Equal(QuotationSubmissionLock.ReasonCode, ((ProblemDetails)put.Value!).Extensions["code"]?.ToString());
        var del = Assert.IsType<ConflictObjectResult>(await c.DeleteQuotation(s.RequestId, s.Q1));
        Assert.Equal(QuotationSubmissionLock.ReasonCode, ((ProblemDetails)del.Value!).Extensions["code"]?.ToString());

        ctx.ChangeTracker.Clear();
        var q1 = await ctx.Quotations.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.Id == s.Q1);
        Assert.Equal(2, q1.Items.Count);
        Assert.Equal(24133.06m, q1.Items.Single(i => i.Id == s.Qi1).UnitPrice);
    }

    // ── 4. mixed payload: released line + committed line → refused as a whole, nothing persisted ──
    [Fact]
    public async Task Mixed_payload_with_a_committed_line_is_refused_and_nothing_is_persisted()
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);
        var c = Requests(ctx, s.Owner);
        var before = await ctx.Quotations.CountAsync(q => q.RequestId == s.RequestId);

        var mixedSave = Assert.IsType<ConflictObjectResult>((await c.SaveQuotation(s.RequestId, null, NewQuotationDto((s.Line1, 1, "Belt"), (s.Line2, 2, "Rolamento")))).Result);
        Assert.Equal("Item já comprometido", ((ProblemDetails)mixedSave.Value!).Title);
        Assert.Equal(before, await ctx.Quotations.CountAsync(q => q.RequestId == s.RequestId));

        // A valid new quotation for the released line, then an UPDATE that tries to add the committed line → refused, untouched.
        Assert.IsType<OkObjectResult>((await c.SaveQuotation(s.RequestId, null, NewQuotationDto((s.Line1, 1, "Belt")))).Result);
        ctx.ChangeTracker.Clear();
        var newQ = await ctx.Quotations.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.RequestId == s.RequestId && q.Id != s.Q1 && q.Id != s.Q2);
        var mixedUpdate = Assert.IsType<ConflictObjectResult>((await Requests(ctx, s.Owner).UpdateQuotation(s.RequestId, newQ.Id, NewQuotationDto((s.Line1, 1, "Belt"), (s.Line2, 2, "Rolamento")))).Result);
        Assert.Equal("Item já comprometido", ((ProblemDetails)mixedUpdate.Value!).Title);
        ctx.ChangeTracker.Clear();
        var after = await ctx.Quotations.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.Id == newQ.Id);
        Assert.Equal(s.Line1, Assert.Single(after.Items).MappedRequestLineItemId);
        Assert.Equal(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, (await ctx.RequestLineItems.AsNoTracking().SingleAsync(l => l.Id == s.Line2)).QuotationLifecycleStatus);
    }

    // ── 5. proforma evidence of a submitted quotation is never removed; a fresh unsubmitted one can be ──
    [Fact]
    public async Task Proforma_of_a_submitted_quotation_cannot_be_deleted_but_a_fresh_unsubmitted_one_can()
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);

        var evidence = new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, AttachmentTypeCode = AttachmentConstants.Types.Proforma, FileName = "proforma-q1.pdf", FileExtension = ".pdf", StorageReference = "x", UploadedByUserId = s.Owner, UploadedAtUtc = DateTime.UtcNow.AddDays(-9) };
        ctx.RequestAttachments.Add(evidence);
        var q1 = await ctx.Quotations.SingleAsync(q => q.Id == s.Q1);
        q1.ProformaAttachmentId = evidence.Id;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var refused = Assert.IsType<ConflictObjectResult>(await Attachments(ctx, s.Owner).Delete(evidence.Id));
        Assert.Equal(QuotationSubmissionLock.ReasonCode, ((ProblemDetails)refused.Value!).Extensions["code"]?.ToString());
        ctx.ChangeTracker.Clear();
        Assert.False((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == evidence.Id)).IsDeleted);

        // Fresh, unsubmitted quotation for the released line with its own document → deletable on the item window.
        var fresh = new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, AttachmentTypeCode = AttachmentConstants.Types.Proforma, FileName = "proforma-new.pdf", FileExtension = ".pdf", StorageReference = "y", UploadedByUserId = s.Owner, UploadedAtUtc = DateTime.UtcNow };
        ctx.RequestAttachments.Add(fresh);
        var newQ = new Quotation { Id = Guid.NewGuid(), RequestId = s.RequestId, SupplierId = S.SupplierB, SupplierNameSnapshot = "OUTRO FORNECEDOR", Currency = "AOA", TotalAmount = 20_000m, SourceType = "MANUAL", CreatedByUserId = s.Owner, CreatedAtUtc = DateTime.UtcNow, ProformaAttachmentId = fresh.Id };
        ctx.Quotations.Add(newQ);
        ctx.QuotationItems.Add(new QuotationItem { Id = Guid.NewGuid(), QuotationId = newQ.Id, Description = "Belt", MappedRequestLineItemId = s.Line1, ReconciliationStatus = "MAPPED", Quantity = 1, UnitPrice = 20_000m, LineTotal = 20_000m });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var ok = await Attachments(ctx, s.Owner).Delete(fresh.Id);
        var status = (ok as ObjectResult)?.StatusCode ?? (ok as StatusCodeResult)?.StatusCode ?? 200;
        Assert.True(status < 400, $"fresh document delete refused with {status}: {(ok as ObjectResult)?.Value}");
        ctx.ChangeTracker.Clear();
        Assert.True((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == fresh.Id)).IsDeleted);
    }
}
