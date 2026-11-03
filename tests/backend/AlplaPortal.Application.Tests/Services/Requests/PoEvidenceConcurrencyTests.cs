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
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.249.5 boundary cases (docs/PO_EVIDENCE_AND_QUOTATION_LIFECYCLE_v2.249.5.md §9) on the InMemory provider — two contexts
/// over ONE store stand for two sessions; the SQL Server lock itself is proven in <c>PoEvidenceConcurrencyRelationalTests</c>.
///  • register-po re-reads the chosen file inside its transaction (PoDocumentRowLock) and refuses a file deleted meanwhile;
///  • a file deleted before the call is never registered, whether named by the client or picked as the newest live file;
///  • a temporary upload made AFTER the last registration stays deletable although the group has older registration history
///    (pointer-era group), and the legacy heuristic's 1-second tolerance / timestamp tie behave as documented;
///  • the legacy whole-quotation selection follows the D3 reuse eligibility (unauthorised refused, authorised and mixed allowed).
/// </summary>
public class PoEvidenceConcurrencyTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_poconc_" + Guid.NewGuid());
    public PoEvidenceConcurrencyTests() => Directory.CreateDirectory(_scratchDir);
    public void Dispose() { try { Directory.Delete(_scratchDir, true); } catch { } }

    private static DbContextOptions<ApplicationDbContext> SharedOptions(string store) => new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(store).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;

    private static RequestsController Requests(ApplicationDbContext ctx, Guid userId, string role = RoleConstants.Buyer, IQuotationItemEligibilityService? eligibility = null)
    {
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>(); orchestrator.Setup(o => o.EmitAsync(It.IsAny<Domain.Events.WorkflowEvent>())).Returns(Task.CompletedTask);
        var ctl = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, S.AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, eligibility ?? new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()));
        ctl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, role) }, "Test")),
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
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")) } };
        return ctl;
    }

    private static RegisterPoActionDto Dto(Guid groupId, string number, Guid? attachmentId = null) => new()
    {
        PoGroupId = groupId, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, PurchaseOrderNumber = number,
        PoAttachmentId = attachmentId, DocumentTypeConfirmed = true, Comment = "ZZTEST tipo de documento confirmado manualmente (sem extração registada no teste)"
    };

    private static async Task<Guid> AddFileAsync(ApplicationDbContext ctx, S.Seed s, Guid groupId, DateTime uploadedAt, string name = "po-temp.pdf")
    {
        var id = Guid.NewGuid();
        ctx.RequestAttachments.Add(new RequestAttachment { Id = id, RequestId = s.RequestId, RequestPoGroupId = groupId, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = name, FileExtension = ".pdf", StorageReference = name, FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), UploadedByUserId = s.Owner, UploadedAtUtc = uploadedAt });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return id;
    }

    private static ProblemDetails Problem(IActionResult r) => Assert.IsType<ProblemDetails>(((ObjectResult)r).Value);
    private static string Code(IActionResult r) => (string)Problem(r).Extensions["code"]!;

    // ── 1. register-po versus a delete committed by another session ──

    [Fact]
    public async Task LockAndReload_sees_a_soft_delete_committed_by_another_context()
    {
        var store = Guid.NewGuid().ToString();
        await using var ctxA = new ApplicationDbContext(SharedOptions(store));
        await using var ctxB = new ApplicationDbContext(SharedOptions(store));
        var s = await S.SeedAsync(ctxA, groupBStatus: "WAITING_PO");
        var temp = await AddFileAsync(ctxA, s, s.GroupB, DateTime.UtcNow);

        // Session A loads the file (as register-po does BEFORE its transaction) …
        var loadedByA = await ctxA.RequestAttachments.SingleAsync(a => a.Id == temp);
        Assert.False(loadedByA.IsDeleted);
        // … session B soft-deletes and commits …
        var rowB = await ctxB.RequestAttachments.SingleAsync(a => a.Id == temp); rowB.IsDeleted = true; await ctxB.SaveChangesAsync();
        // … session A's in-transaction re-read (PoDocumentRowLock) sees the committed state on the SAME tracked instance.
        Assert.True(await PoDocumentRowLock.LockAndReloadAsync(ctxA, loadedByA));
        Assert.True(loadedByA.IsDeleted);
    }

    [Fact]
    public async Task RegisterPo_refuses_a_file_named_by_the_client_that_was_deleted_before_the_call_and_writes_nothing()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx, groupBStatus: "WAITING_PO");
        var temp = await AddFileAsync(ctx, s, s.GroupB, DateTime.UtcNow);
        var row = await ctx.RequestAttachments.SingleAsync(a => a.Id == temp); row.IsDeleted = true; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        var refused = await Requests(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, "ECF10 2026/777", temp));
        Assert.Equal(400, Problem(refused).Status);   // HasGroupAttachmentAsync: no live P.O. file on the group
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB);
        Assert.Equal("WAITING_PO", group.Status); Assert.Null(group.CurrentPoAttachmentId); Assert.Null(group.PurchaseOrderNumber);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO" && h.Comment!.Contains(s.GroupB.ToString().Substring(0, 8))));
    }

    [Fact]
    public async Task RegisterPo_with_a_live_sibling_never_picks_a_deleted_file_named_by_the_client()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx, groupBStatus: "WAITING_PO");
        var deleted = await AddFileAsync(ctx, s, s.GroupB, DateTime.UtcNow.AddMinutes(-2), "po-deleted.pdf");
        var live = await AddFileAsync(ctx, s, s.GroupB, DateTime.UtcNow.AddMinutes(-1), "po-live.pdf");
        var row = await ctx.RequestAttachments.SingleAsync(a => a.Id == deleted); row.IsDeleted = true; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        var refused = await Requests(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, "ECF10 2026/778", deleted));
        Assert.Equal("PO_ATTACHMENT_INVALID", Code(refused));
        Assert.Null((await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).CurrentPoAttachmentId);

        // Without a client-named file the newest LIVE file is the document, never the deleted one.
        var ok = await Requests(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, "ECF10 2026/778"));
        Assert.IsType<OkObjectResult>(ok);
        Assert.Equal(live, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).CurrentPoAttachmentId);
    }

    [Fact]
    public async Task Delete_of_a_file_already_soft_deleted_by_another_session_is_idempotent_and_writes_nothing()
    {
        var store = Guid.NewGuid().ToString();
        await using var ctxA = new ApplicationDbContext(SharedOptions(store));
        await using var ctxB = new ApplicationDbContext(SharedOptions(store));
        var s = await S.SeedAsync(ctxA, groupBStatus: "WAITING_PO");
        var temp = await AddFileAsync(ctxA, s, s.GroupB, DateTime.UtcNow);
        var rowB = await ctxB.RequestAttachments.SingleAsync(a => a.Id == temp); rowB.IsDeleted = true; await ctxB.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await Attachments(ctxA, s.Owner).Delete(temp));
        await using var check = new ApplicationDbContext(SharedOptions(store));
        Assert.Equal(0, await check.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "DOCUMENTO REMOVIDO"));
    }

    [Fact]
    public async Task Register_then_delete_the_registered_file_is_refused_and_the_pointer_stays()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx, groupBStatus: "WAITING_PO");
        var file = await AddFileAsync(ctx, s, s.GroupB, DateTime.UtcNow);
        Assert.IsType<OkObjectResult>(await Requests(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, "ECF10 2026/779", file)));
        ctx.ChangeTracker.Clear();
        // Finance return: the delete status window is open again — the evidence rule alone must keep the registered file.
        var gb = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB); gb.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        var refused = await Attachments(ctx, s.Owner).Delete(file);
        Assert.Equal(PoEvidenceProtection.ErrorCode, Code(refused));
        Assert.Equal(PoEvidenceProtection.Reasons.Current, Problem(refused).Extensions["reasonCode"]);
        var row = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == file);
        Assert.False(row.IsDeleted);
        Assert.Equal(file, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).CurrentPoAttachmentId);
    }

    // ── 2. a temporary upload after the last registration, with older registration history ──

    [Fact]
    public async Task Temporary_upload_after_the_last_registration_stays_deletable_in_a_pointer_era_group_with_registration_history()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx, groupBStatus: "WAITING_PO");
        var first = await AddFileAsync(ctx, s, s.GroupB, DateTime.UtcNow.AddMinutes(-10), "po-first.pdf");
        Assert.IsType<OkObjectResult>(await Requests(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, "ECF10 2026/780", first)));
        ctx.ChangeTracker.Clear();
        // Finance returns the group; the Buyer starts a correction and uploads a new file, then abandons it.
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB); g.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var temp = await AddFileAsync(ctx, s, s.GroupB, DateTime.UtcNow, "po-temp.pdf");
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO"));   // older history exists

        Assert.IsType<NoContentResult>(await Attachments(ctx, s.Owner).Delete(temp));
        Assert.True((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == temp)).IsDeleted);
        // The registered file is untouched and still refused.
        Assert.False((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == first)).IsDeleted);
        ctx.ChangeTracker.Clear();
        Assert.Equal(PoEvidenceProtection.ErrorCode, Code(await Attachments(ctx, s.Owner).Delete(first)));
    }

    [Theory]
    [InlineData(0, true)]      // exact timestamp tie → protected
    [InlineData(1, true)]      // upload 1 s after the registration → protected (tolerance)
    [InlineData(2, false)]     // upload more than 1 s after the LAST registration → temporary upload, deletable
    [InlineData(-5, true)]     // upload before the registration → protected (the file was on the group when registered)
    public void Legacy_heuristic_ties_and_tolerance(int uploadSecondsAfterRegistration, bool expectedProtected)
    {
        var group = Guid.NewGuid();
        var registeredAt = new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc);
        var file = new PoEvidenceProtection.FileFacts(Guid.NewGuid(), group, registeredAt.AddSeconds(uploadSecondsAfterRegistration), null, null);
        var verdict = PoEvidenceProtection.Evaluate(file, groupCurrentPointer: null, new[] { new PoEvidenceProtection.Registration(registeredAt, group) });
        Assert.Equal(expectedProtected, verdict.IsProtected);
        if (expectedProtected) Assert.Equal(PoEvidenceProtection.Reasons.RegisteredAfterUpload, verdict.ReasonCode);
    }

    [Fact]
    public void Pointer_era_group_never_uses_the_timestamp_heuristic_for_a_live_unpointed_file()
    {
        var group = Guid.NewGuid(); var pointer = Guid.NewGuid();
        var registeredAt = new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc);
        // Same tie that protects a legacy file: with a pointer on the group it is a temporary upload.
        var file = new PoEvidenceProtection.FileFacts(Guid.NewGuid(), group, registeredAt, null, null);
        Assert.False(PoEvidenceProtection.Evaluate(file, pointer, new[] { new PoEvidenceProtection.Registration(registeredAt, group) }).IsProtected);
        // The pointer itself, a superseded and a voided file stay protected.
        Assert.Equal(PoEvidenceProtection.Reasons.Current, PoEvidenceProtection.Evaluate(file with { Id = pointer }, pointer, Array.Empty<PoEvidenceProtection.Registration>()).ReasonCode);
        Assert.Equal(PoEvidenceProtection.Reasons.Superseded, PoEvidenceProtection.Evaluate(file with { SupersededAtUtc = registeredAt }, pointer, Array.Empty<PoEvidenceProtection.Registration>()).ReasonCode);
        Assert.Equal(PoEvidenceProtection.Reasons.Voided, PoEvidenceProtection.Evaluate(file with { VoidedAtUtc = registeredAt }, pointer, Array.Empty<PoEvidenceProtection.Registration>()).ReasonCode);
        // A legacy file with NO group of its own still follows the heuristic even when some group has a pointer.
        var orphan = new PoEvidenceProtection.FileFacts(Guid.NewGuid(), null, registeredAt, null, null);
        Assert.True(PoEvidenceProtection.Evaluate(orphan, null, new[] { new PoEvidenceProtection.Registration(registeredAt, null) }).IsProtected);
    }

    // ── 3. legacy whole-quotation selection follows the D3 reuse eligibility ──

    private static async Task<S.Seed> CancelledBatchSeedAsync(ApplicationDbContext ctx)
    {
        var s = await S.SeedAsync(ctx, requestStatus: RequestConstants.Statuses.WaitingAreaApproval, groupAStatus: "CANCELLED", groupBStatus: "CANCELLED", groupAPoNumber: null, groupAPoAttachment: false);
        var batch = await ctx.ApprovalBatches.SingleAsync(b => b.Id == s.Batch); batch.Status = RequestConstants.ApprovalBatchStatuses.Cancelled;
        foreach (var li in await ctx.RequestLineItems.Where(l => l.RequestId == s.RequestId).ToListAsync()) { li.SelectedQuotationItemId = null; li.RequestPoGroupId = null; li.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationPending; }
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return s;
    }

    [Fact]
    public async Task Select_from_the_card_is_refused_when_every_item_of_the_quotation_is_reuse_blocked()
    {
        await using var ctx = S.NewCtx();
        var s = await CancelledBatchSeedAsync(ctx);
        var ctl = Requests(ctx, s.Admin, RoleConstants.SystemAdministrator, new QuotationItemEligibilityService(ctx));
        var refused = await ctl.SelectQuotation(s.RequestId, s.Q1);
        Assert.Equal(409, Problem(refused).Status);
        Assert.Equal("QUOTATION_REUSE_NOT_AUTHORIZED", Code(refused));
        Assert.False((await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.Q1)).IsSelected);
        Assert.Null((await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).SelectedQuotationId);
    }

    [Fact]
    public async Task Select_from_the_card_is_allowed_once_the_reuse_is_explicitly_authorised()
    {
        await using var ctx = S.NewCtx();
        var s = await CancelledBatchSeedAsync(ctx);
        ctx.QuotationReuseAuthorizations.Add(new QuotationReuseAuthorization { Id = Guid.NewGuid(), RequestId = s.RequestId, QuotationId = s.Q1, QuotationItemId = s.Qi1, SourceApprovalBatchId = s.Batch, AuthorizedByUserId = s.Owner, AuthorizedAtUtc = DateTime.UtcNow, IsActive = true, Reason = "ZZTEST reuse", CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var ctl = Requests(ctx, s.Admin, RoleConstants.SystemAdministrator, new QuotationItemEligibilityService(ctx));
        Assert.IsType<NoContentResult>(await ctl.SelectQuotation(s.RequestId, s.Q1));
        Assert.True((await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.Q1)).IsSelected);
        // The sibling quotation (candidate of the same cancelled batch, no authorization) is still refused.
        ctx.ChangeTracker.Clear();
        Assert.Equal("QUOTATION_REUSE_NOT_AUTHORIZED", Code(await Requests(ctx, s.Admin, RoleConstants.SystemAdministrator, new QuotationItemEligibilityService(ctx)).SelectQuotation(s.RequestId, s.Q2)));
    }

    [Fact]
    public async Task Select_from_the_card_is_allowed_for_a_quotation_mixing_historical_and_current_items()
    {
        await using var ctx = S.NewCtx();
        var s = await CancelledBatchSeedAsync(ctx);
        // A new line quoted by the same supplier after the cancellation: never in a batch → eligible, so the quotation is selectable.
        var line3 = Guid.NewGuid();
        ctx.RequestLineItems.Add(new RequestLineItem { Id = line3, RequestId = s.RequestId, LineNumber = 3, Description = "Novo item", Quantity = 1, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationPending, LineItemStatusId = 1, CreatedAtUtc = DateTime.UtcNow });
        ctx.QuotationItems.Add(new QuotationItem { Id = Guid.NewGuid(), QuotationId = s.Q1, Description = "Novo item", MappedRequestLineItemId = line3, ReconciliationStatus = "MAPPED", Quantity = 1, UnitPrice = 10m, LineTotal = 10m });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var ctl = Requests(ctx, s.Admin, RoleConstants.SystemAdministrator, new QuotationItemEligibilityService(ctx));
        Assert.IsType<NoContentResult>(await ctl.SelectQuotation(s.RequestId, s.Q1));
        Assert.True((await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.Q1)).IsSelected);
    }
}
