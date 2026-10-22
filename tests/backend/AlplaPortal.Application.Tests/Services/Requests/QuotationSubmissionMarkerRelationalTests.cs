using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Data.Migrations;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// RELATIONAL (SQL Server LocalDB sandbox <c>Portal-Gerencial-IntegrationTests</c>) proof of the §7 durable
/// submission marker: real rowversion arbitration between a Buyer edit/delete and a concurrent submission,
/// transactional rollback of a failed submission (no marker left behind), FK/delete behaviour (the marker and its
/// batch pointer survive candidate/batch-item removal and whole-batch deletion), and the migration's backfill SQL
/// (selected + candidate references of ANY batch status, legacy line-level awards, idempotent). InMemory cannot
/// exercise rowversion or transactions, hence this suite. Skips (returns) when LocalDB is unavailable.
/// </summary>
[Collection("IntegrationTests")]
public class QuotationSubmissionMarkerRelationalTests
{
    /// <summary>Model-drift-aware bootstrap: recreate the sandbox when it predates the Quotation marker/RowVersion columns.</summary>
    static QuotationSubmissionMarkerRelationalTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var marker = ctx.Database.SqlQueryRaw<int>("SELECT CAST(ISNULL(COL_LENGTH('dbo.Quotations', 'FirstSubmittedForApprovalAtUtc'), 0) AS int) AS [Value]").AsEnumerable().First();
                var rowVersion = ctx.Database.SqlQueryRaw<int>("SELECT CAST(ISNULL(COL_LENGTH('dbo.Quotations', 'RowVersion'), 0) AS int) AS [Value]").AsEnumerable().First();
                var source = ctx.Database.SqlQueryRaw<int>("SELECT CAST(ISNULL(COL_LENGTH('dbo.Quotations', 'FirstSubmittedSource'), 0) AS int) AS [Value]").AsEnumerable().First();
                if (marker == 0 || rowVersion == 0 || source == 0)
                {
                    Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                    ctx.Database.ExecuteSqlRaw("ALTER DATABASE CURRENT SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                    ctx.Database.EnsureDeleted();
                }
            }
            ctx.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QuotationSubmissionMarkerRelationalTests] sandbox bootstrap: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();
    private static ApplicationDbContext NewCtx() => new(Options());

    /// <summary>Context whose FIRST SaveChanges runs <paramref name="beforeSave"/> (a competing commit) right before its own write is sent.</summary>
    private static ApplicationDbContext NewCtxWithRace(Func<Task> beforeSave)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(IntegrationTestDatabase.GetConnectionString()).AddInterceptors(new RaceInterceptor(beforeSave)).Options);

    private sealed class RaceInterceptor : SaveChangesInterceptor
    {
        private readonly Func<Task> _beforeSave; private int _fired;
        public RaceInterceptor(Func<Task> beforeSave) => _beforeSave = beforeSave;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0) await _beforeSave();
            return result;
        }
    }

    private sealed record Seed(Guid RequestId, Guid BuyerId, int SupplierId, Guid LineA, Guid LineB,
        Guid Q1, Guid Q1Item, Guid Q2, Guid Q2Item, Guid Q3, Guid Q3Item, Guid Q4, Guid Q4Item, Guid Q5, Guid Q5Item,
        Guid Batch1, Guid Batch2, Guid AttachmentQ5, Guid AttachmentQ5Alt);

    /// <summary>Request with five quotations and two batches. References: Q1 candidate of #1 (REJECTED); Q2 selected-only
    /// of #2 (CANCELLED); Q3 candidate of #1 AND #2; Q4 legacy line-level award only; Q5 never submitted (editable).</summary>
    /// <summary>Removes this suite's rows from previous runs (tests in a class run sequentially, so nothing live is touched).</summary>
    private static async Task CleanupPreviousRunsAsync(ApplicationDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE c FROM ApprovalBatchItemCandidates c INNER JOIN ApprovalBatchItems abi ON abi.Id=c.ApprovalBatchItemId INNER JOIN ApprovalBatches b ON b.Id=abi.ApprovalBatchId INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE 'ZZT-MK-%';" +
            "DELETE abi FROM ApprovalBatchItems abi INNER JOIN ApprovalBatches b ON b.Id=abi.ApprovalBatchId INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE 'ZZT-MK-%';" +
            "DELETE b FROM ApprovalBatches b INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE 'ZZT-MK-%';" +
            "UPDATE li SET SelectedQuotationItemId=NULL FROM RequestLineItems li INNER JOIN Requests r ON r.Id=li.RequestId WHERE r.RequestNumber LIKE 'ZZT-MK-%';" +
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id=qi.QuotationId INNER JOIN Requests r ON r.Id=q.RequestId WHERE r.RequestNumber LIKE 'ZZT-MK-%';" +
            "DELETE q FROM Quotations q INNER JOIN Requests r ON r.Id=q.RequestId WHERE r.RequestNumber LIKE 'ZZT-MK-%';" +
            "DELETE a FROM RequestAttachments a INNER JOIN Requests r ON r.Id=a.RequestId WHERE r.RequestNumber LIKE 'ZZT-MK-%';" +
            "DELETE li FROM RequestLineItems li INNER JOIN Requests r ON r.Id=li.RequestId WHERE r.RequestNumber LIKE 'ZZT-MK-%';" +
            "DELETE h FROM RequestStatusHistories h INNER JOIN Requests r ON r.Id=h.RequestId WHERE r.RequestNumber LIKE 'ZZT-MK-%';" +
            "DELETE FROM Requests WHERE RequestNumber LIKE 'ZZT-MK-%';" +
            "DELETE FROM Suppliers WHERE Name LIKE 'ZZ Fornecedor Marker %' AND NOT EXISTS (SELECT 1 FROM Quotations q WHERE q.SupplierId=Suppliers.Id);" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-marker-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId=Users.Id);");
    }

    private static async Task<Seed?> SeedAsync(string batch1Status = "REJECTED", string batch2Status = "CANCELLED")
    {
        await using var ctx = NewCtx();
        await CleanupPreviousRunsAsync(ctx);
        var statusId = await ctx.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.WaitingQuotation).Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == RequestConstants.Types.Quotation).Select(t => t.Id).FirstOrDefaultAsync();
        if (statusId == 0 || typeId == 0) return null;
        var buyer = new User { Id = Guid.NewGuid(), FullName = "ZZ Buyer Marker", Email = $"zztest-marker-{Guid.NewGuid():N}@test.local", IsActive = true };
        ctx.Users.Add(buyer);
        var supplier = new Supplier { Name = "ZZ Fornecedor Marker " + Guid.NewGuid().ToString("N")[..8], TaxId = "54" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString() /* IX_Suppliers_TaxId is unique */, PortalCode = "ZZ" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), IsActive = true, RegistrationStatus = "ACTIVE" };
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();

        var request = new Request { Id = Guid.NewGuid(), Title = "ZZTEST_MARKER", RequestNumber = "ZZT-MK-" + Guid.NewGuid().ToString("N")[..8], StatusId = statusId, RequestTypeId = typeId, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1, RequesterId = buyer.Id, BuyerId = buyer.Id, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(request);
        var lineA = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1, Description = "Peça A", Quantity = 1, UnitPrice = 100m, TotalAmount = 100m, PlantId = 1, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow.AddDays(-3) };
        var lineB = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 2, Description = "Peça B", Quantity = 1, UnitPrice = 100m, TotalAmount = 100m, PlantId = 1, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestLineItems.AddRange(lineA, lineB);

        Quotation Q(string doc, DateTime created) => new() { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, SupplierNameSnapshot = supplier.Name, DocumentNumber = doc, Currency = "AOA", SourceType = "MANUAL", TotalAmount = 100m, CreatedAtUtc = created, CreatedByUserId = buyer.Id };
        QuotationItem I(Quotation q, Guid line) => new() { Id = Guid.NewGuid(), QuotationId = q.Id, LineNumber = 1, Description = "Peça", Quantity = 1, UnitPrice = 100m, GrossSubtotal = 100m, LineTotal = 100m, MappedRequestLineItemId = line, ReconciliationStatus = "MAPPED" };
        var q1 = Q("Q1", DateTime.UtcNow.AddDays(-10)); var q1i = I(q1, lineA.Id);
        var q2 = Q("Q2", DateTime.UtcNow.AddDays(-9)); var q2i = I(q2, lineA.Id);
        var q3 = Q("Q3", DateTime.UtcNow.AddDays(-8)); var q3i = I(q3, lineA.Id);
        var q4 = Q("Q4", DateTime.UtcNow.AddDays(-7)); var q4i = I(q4, lineB.Id);
        var q5 = Q("Q5", DateTime.UtcNow.AddDays(-6)); var q5i = I(q5, lineB.Id);
        var attachment = new RequestAttachment { Id = Guid.NewGuid(), RequestId = request.Id, AttachmentTypeCode = RequestAttachment.TYPE_QUOTATION, FileName = "q5.pdf", StorageReference = "q5.pdf", UploadedByUserId = buyer.Id, UploadedAtUtc = DateTime.UtcNow };
        q5.ProformaAttachmentId = attachment.Id;
        var attachmentAlt = new RequestAttachment { Id = Guid.NewGuid(), RequestId = request.Id, AttachmentTypeCode = RequestAttachment.TYPE_QUOTATION, FileName = "q5-alt.pdf", StorageReference = "q5-alt.pdf", UploadedByUserId = buyer.Id, UploadedAtUtc = DateTime.UtcNow };
        ctx.RequestAttachments.AddRange(attachment, attachmentAlt);
        ctx.Quotations.AddRange(q1, q2, q3, q4, q5); ctx.QuotationItems.AddRange(q1i, q2i, q3i, q4i, q5i);

        var batch1 = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1, Status = batch1Status, CreatedAtUtc = DateTime.UtcNow.AddDays(-5), CreatedByUserId = buyer.Id };
        var batch2 = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 2, Status = batch2Status, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedByUserId = buyer.Id };
        var b1i = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch1.Id, RequestLineItemId = lineA.Id, CreatedAtUtc = batch1.CreatedAtUtc };
        var b2i = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch2.Id, RequestLineItemId = lineA.Id, CreatedAtUtc = batch2.CreatedAtUtc, SelectedQuotationItemId = q2i.Id };
        ctx.ApprovalBatches.AddRange(batch1, batch2); ctx.ApprovalBatchItems.AddRange(b1i, b2i);
        ApprovalBatchItemCandidate C(ApprovalBatchItem bi, Quotation q, QuotationItem qi) => new() { Id = Guid.NewGuid(), ApprovalBatchItemId = bi.Id, QuotationItemId = qi.Id, QuotationId = q.Id, SupplierId = supplier.Id, SupplierNameSnapshot = supplier.Name, QuotedDescription = "Peça", QuotedQuantity = 1, UnitPrice = 100m, GrossSubtotal = 100m, LineTotal = 100m, Currency = "AOA", CreatedAtUtc = bi.CreatedAtUtc };
        ctx.ApprovalBatchItemCandidates.AddRange(C(b1i, q1, q1i), C(b1i, q3, q3i), C(b2i, q3, q3i));
        await ctx.SaveChangesAsync();
        // Legacy request-level award on an EXISTING line (second write unit: avoids the line↔quotation-item insert cycle and
        // exercises the stamper's Modified-RequestLineItem path).
        lineB.SelectedQuotationItemId = q4i.Id;
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, buyer.Id, supplier.Id, lineA.Id, lineB.Id, q1.Id, q1i.Id, q2.Id, q2i.Id, q3.Id, q3i.Id, q4.Id, q4i.Id, q5.Id, q5i.Id, batch1.Id, batch2.Id, attachment.Id, attachmentAlt.Id);
    }

    private static RequestsController Ctl(ApplicationDbContext ctx, Guid actorId) => CtlWithMocks(ctx, actorId).Controller;

    private static (RequestsController Controller, Mock<IWorkflowNotificationOrchestrator> Orchestrator, Mock<INotificationService> Notifications) CtlWithMocks(ApplicationDbContext ctx, Guid actorId)
    {
        var statusSync = new Mock<IRequestStatusSyncService>(); statusSync.Setup(x => x.ComputeDisplayWorkflowStateAsync(It.IsAny<Guid>())).ReturnsAsync(string.Empty);
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>(); var notifications = new Mock<INotificationService>();
        var controller = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance), NullLogger<RequestsController>.Instance,
            notifications.Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            statusSync.Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object, new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Microsoft.Extensions.Options.Options.Create(new PostPaymentCompletionOptions()));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() } };
        return (controller, orchestrator, notifications);
    }

    private static SaveQuotationRequestDto EditDto(Seed s, Guid line, decimal unit, Guid? attachment = null) => new()
    {
        SupplierId = s.SupplierId, Currency = "AOA", ProformaAttachmentId = attachment ?? s.AttachmentQ5,
        Items = new List<SaveQuotationItemDto> { new() { LineNumber = 1, Description = "Peça", Quantity = 1, UnitPrice = unit, MappedRequestLineItemId = line, ReconciliationStatus = "MAPPED" } }
    };

    /// <summary>A competing submission of Q5 (never submitted before) committed in its own context/transaction.</summary>
    private static async Task CommitSubmissionOfQ5(Seed s)
    {
        await using var other = NewCtx();
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = s.RequestId, BatchNumber = 3, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.BuyerId };
        var bi = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = s.LineB, CreatedAtUtc = DateTime.UtcNow };
        other.ApprovalBatches.Add(batch); other.ApprovalBatchItems.Add(bi);
        other.ApprovalBatchItemCandidates.Add(new ApprovalBatchItemCandidate { Id = Guid.NewGuid(), ApprovalBatchItemId = bi.Id, QuotationItemId = s.Q5Item, QuotationId = s.Q5, SupplierId = s.SupplierId, SupplierNameSnapshot = "ZZ", QuotedDescription = "Peça", QuotedQuantity = 1, UnitPrice = 100m, GrossSubtotal = 100m, LineTotal = 100m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow });
        await other.SaveChangesAsync();
    }

    // ───────────────────────────── FK / delete behaviour ─────────────────────────────

    [Fact]
    public async Task No_foreign_key_on_FirstSubmittedBatchId_and_marker_survives_reference_removal_and_batch_deletion()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var ctx = NewCtx();
        var fkCount = ctx.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_key_columns fkc JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id WHERE fkc.parent_object_id = OBJECT_ID('dbo.Quotations') AND c.name = 'FirstSubmittedBatchId'").AsEnumerable().First();
        Assert.Equal(0, fkCount);

        var stamped = await ctx.Quotations.AsNoTracking().Where(q => q.RequestId == s.RequestId).ToDictionaryAsync(q => q.Id);
        Assert.Equal(s.Batch1, stamped[s.Q1].FirstSubmittedBatchId); Assert.Equal(s.Batch2, stamped[s.Q2].FirstSubmittedBatchId); Assert.Equal(s.Batch1, stamped[s.Q3].FirstSubmittedBatchId);
        Assert.NotNull(stamped[s.Q4].FirstSubmittedForApprovalAtUtc); Assert.Null(stamped[s.Q4].FirstSubmittedBatchId);
        Assert.Null(stamped[s.Q5].FirstSubmittedForApprovalAtUtc);

        // Rework-recomposition shape on batch #1: drop the candidate rows and the batch item, keep the batch.
        var b1 = await ctx.ApprovalBatches.Include(b => b.Items).ThenInclude(i => i.Candidates).SingleAsync(b => b.Id == s.Batch1);
        foreach (var bi in b1.Items.ToList()) { ctx.ApprovalBatchItemCandidates.RemoveRange(bi.Candidates); ctx.ApprovalBatchItems.Remove(bi); }
        await ctx.SaveChangesAsync();
        // Whole-batch deletion on batch #2 (cascades to its item and candidates).
        ctx.ApprovalBatches.Remove(await ctx.ApprovalBatches.SingleAsync(b => b.Id == s.Batch2));
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        Assert.False(await ctx.ApprovalBatchItemCandidates.AnyAsync(c => c.Quotation.RequestId == s.RequestId));
        Assert.False(await ctx.ApprovalBatches.AnyAsync(b => b.Id == s.Batch2));
        var after = await ctx.Quotations.Include(q => q.Items).Where(q => q.RequestId == s.RequestId).ToDictionaryAsync(q => q.Id);
        foreach (var id in new[] { s.Q1, s.Q2, s.Q3 })
        {
            Assert.Equal(stamped[id].FirstSubmittedForApprovalAtUtc, after[id].FirstSubmittedForApprovalAtUtc);
            Assert.Equal(stamped[id].FirstSubmittedBatchId, after[id].FirstSubmittedBatchId);   // pointer kept even when the batch is gone
            var st = await QuotationSubmissionLock.EvaluateAsync(ctx, after[id]);
            Assert.True(st.IsSubmitted); Assert.Equal(QuotationSubmissionLock.EvidenceDurableMarker, st.Evidence);
        }
        Assert.Equal(1, (await QuotationSubmissionLock.EvaluateAsync(ctx, after[s.Q1])).BatchNumber);       // batch #1 still exists → named
        Assert.Null((await QuotationSubmissionLock.EvaluateAsync(ctx, after[s.Q2])).BatchNumber);           // batch #2 deleted → "removido"
        Assert.False((await QuotationSubmissionLock.EvaluateAsync(ctx, after[s.Q5])).IsSubmitted);
    }

    // ───────────────────────────── concurrency ─────────────────────────────

    [Fact]
    public async Task Stale_edit_is_refused_409_when_a_submission_commits_while_the_update_is_saving()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var racing = NewCtxWithRace(() => CommitSubmissionOfQ5(s));
        var (ctl, orchestrator, notifications) = CtlWithMocks(racing, s.BuyerId);
        // The stale payload also swaps the proforma document — the side effect that soft-deletes the old attachment must not land either.
        var result = await ctl.UpdateQuotation(s.RequestId, s.Q5, EditDto(s, s.LineB, 123m, attachment: s.AttachmentQ5Alt));
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(QuotationSubmissionLock.ConcurrencyReasonCode, Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<AlplaPortal.Domain.Events.WorkflowEvent>()), Times.Never); notifications.VerifyNoOtherCalls();

        await using var verify = NewCtx();
        var q5 = await verify.Quotations.Include(q => q.Items).SingleAsync(q => q.Id == s.Q5);
        Assert.Equal(100m, q5.Items.Single().UnitPrice);   // the stale edit never landed
        Assert.Equal(s.AttachmentQ5, q5.ProformaAttachmentId);                                            // document link unchanged
        Assert.False((await verify.RequestAttachments.SingleAsync(a => a.Id == s.AttachmentQ5)).IsDeleted);  // old proforma not soft-deleted
        Assert.NotNull(q5.FirstSubmittedForApprovalAtUtc); Assert.Equal(QuotationSubmissionSources.Submission, q5.FirstSubmittedSource);   // the submission did land
        Assert.False(await verify.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "QUOTATION_UPDATED"));
        Assert.False(await verify.EmailOutbox.AnyAsync(e => e.RequestId == s.RequestId));
        Assert.True((await QuotationSubmissionLock.EvaluateAsync(verify, q5)).IsSubmitted);
    }

    [Fact]
    public async Task Stale_delete_is_refused_409_when_a_submission_commits_while_the_delete_is_saving()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var racing = NewCtxWithRace(() => CommitSubmissionOfQ5(s));
        var (ctl, orchestrator, notifications) = CtlWithMocks(racing, s.BuyerId);
        var result = await ctl.DeleteQuotation(s.RequestId, s.Q5);
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(QuotationSubmissionLock.ConcurrencyReasonCode, Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<AlplaPortal.Domain.Events.WorkflowEvent>()), Times.Never); notifications.VerifyNoOtherCalls();

        await using var verify = NewCtx();
        Assert.True(await verify.Quotations.AnyAsync(q => q.Id == s.Q5));
        Assert.True(await verify.QuotationItems.AnyAsync(i => i.Id == s.Q5Item));
        Assert.False((await verify.RequestAttachments.SingleAsync(a => a.Id == s.AttachmentQ5)).IsDeleted);
        Assert.False(await verify.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "QUOTATION_DELETED"));
    }

    [Fact]
    public async Task Submission_fails_and_rolls_back_when_the_quotation_was_edited_after_it_was_read()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var submitting = NewCtx();
        // CreateBatch shape: quotation items loaded WITH their quotation (tracked) before composing the batch.
        var qi = await submitting.QuotationItems.Include(x => x.Quotation).SingleAsync(x => x.Id == s.Q5Item);

        await using (var editing = NewCtx())
        {
            var q = await editing.Quotations.SingleAsync(x => x.Id == s.Q5);
            q.TotalAmount = 150m; await editing.SaveChangesAsync();   // Buyer edit committed in between
        }

        var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = s.RequestId, BatchNumber = 3, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.BuyerId };
        var bi = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = s.LineB, CreatedAtUtc = DateTime.UtcNow };
        submitting.ApprovalBatches.Add(batch); submitting.ApprovalBatchItems.Add(bi);
        submitting.ApprovalBatchItemCandidates.Add(new ApprovalBatchItemCandidate { Id = Guid.NewGuid(), ApprovalBatchItemId = bi.Id, QuotationItemId = qi.Id, QuotationId = qi.QuotationId, SupplierId = s.SupplierId, SupplierNameSnapshot = "ZZ", QuotedDescription = "Peça", QuotedQuantity = 1, UnitPrice = qi.UnitPrice, GrossSubtotal = 100m, LineTotal = 100m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => submitting.SaveChangesAsync());

        await using var verify = NewCtx();
        Assert.False(await verify.ApprovalBatches.AnyAsync(b => b.Id == batch.Id));                 // nothing of the submission persisted
        Assert.False(await verify.ApprovalBatchItemCandidates.AnyAsync(c => c.QuotationItemId == s.Q5Item));
        var q5 = await verify.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.Q5);
        Assert.Null(q5.FirstSubmittedForApprovalAtUtc); Assert.Equal(150m, q5.TotalAmount);       // no marker, edit kept
    }

    [Fact]
    public async Task Failed_submission_save_leaves_no_marker_behind()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var ctx = NewCtx();
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = s.RequestId, BatchNumber = 3, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.BuyerId };
        var bi = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = Guid.NewGuid() /* FK violation → the write unit fails */, CreatedAtUtc = DateTime.UtcNow };
        ctx.ApprovalBatches.Add(batch); ctx.ApprovalBatchItems.Add(bi);
        ctx.ApprovalBatchItemCandidates.Add(new ApprovalBatchItemCandidate { Id = Guid.NewGuid(), ApprovalBatchItemId = bi.Id, QuotationItemId = s.Q5Item, QuotationId = s.Q5, SupplierId = s.SupplierId, SupplierNameSnapshot = "ZZ", QuotedDescription = "Peça", QuotedQuantity = 1, UnitPrice = 100m, GrossSubtotal = 100m, LineTotal = 100m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());

        await using var verify = NewCtx();
        var q5 = await verify.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.Q5);
        Assert.Null(q5.FirstSubmittedForApprovalAtUtc); Assert.Null(q5.FirstSubmittedBatchId);
        Assert.False(await verify.ApprovalBatches.AnyAsync(b => b.Id == batch.Id));
    }

    [Fact]
    public async Task Never_submitted_sibling_is_still_editable_and_deletable_on_sql_server()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var ctx = NewCtx();
        var ctl = Ctl(ctx, s.BuyerId);
        var upd = await ctl.UpdateQuotation(s.RequestId, s.Q5, EditDto(s, s.LineB, 77m));
        Assert.True(upd.Result is OkObjectResult || upd.Value != null, $"update refused: {(upd.Result as ObjectResult)?.Value}");
        ctx.ChangeTracker.Clear();
        Assert.Equal(77m, (await ctx.QuotationItems.AsNoTracking().SingleAsync(i => i.QuotationId == s.Q5)).UnitPrice);
        Assert.IsType<NoContentResult>(await ctl.DeleteQuotation(s.RequestId, s.Q5));
        ctx.ChangeTracker.Clear();
        Assert.False(await ctx.Quotations.AnyAsync(q => q.Id == s.Q5));
        foreach (var locked in new[] { s.Q1, s.Q2, s.Q3, s.Q4 })
            Assert.IsType<ConflictObjectResult>(await ctl.DeleteQuotation(s.RequestId, locked));
    }

    // ───────────────────────────── migration backfill SQL ─────────────────────────────

    [Fact]
    public async Task Backfill_sql_marks_quotations_from_references_of_any_batch_status_and_legacy_awards_and_is_idempotent()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var ctx = NewCtx();
        var ids = new[] { s.Q1, s.Q2, s.Q3, s.Q4, s.Q5 };
        // Simulate rows that predate the marker: the stamper set them on seed — clear them bypassing EF (the guard only protects tracked saves).
        await ctx.Database.ExecuteSqlRawAsync("UPDATE Quotations SET FirstSubmittedForApprovalAtUtc = NULL, FirstSubmittedBatchId = NULL WHERE RequestId = {0}", s.RequestId);
        Assert.All(await ctx.Quotations.AsNoTracking().Where(q => ids.Contains(q.Id)).ToListAsync(), q => Assert.Null(q.FirstSubmittedForApprovalAtUtc));

        await ctx.Database.ExecuteSqlRawAsync(AddQuotationSubmissionMarker.BackfillSql);
        var first = await ctx.Quotations.AsNoTracking().Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id);
        var batches = await ctx.ApprovalBatches.AsNoTracking().Where(b => b.RequestId == s.RequestId).ToDictionaryAsync(b => b.Id);
        var lineB = await ctx.RequestLineItems.AsNoTracking().SingleAsync(li => li.Id == s.LineB);

        Assert.Equal(s.Batch1, first[s.Q1].FirstSubmittedBatchId); Assert.Equal(batches[s.Batch1].CreatedAtUtc, first[s.Q1].FirstSubmittedForApprovalAtUtc); Assert.Equal(QuotationSubmissionSources.BackfillBatch, first[s.Q1].FirstSubmittedSource);   // candidate, REJECTED
        Assert.Equal(s.Batch2, first[s.Q2].FirstSubmittedBatchId); Assert.Equal(batches[s.Batch2].CreatedAtUtc, first[s.Q2].FirstSubmittedForApprovalAtUtc); Assert.Equal(QuotationSubmissionSources.BackfillBatch, first[s.Q2].FirstSubmittedSource);   // selected-only, CANCELLED
        Assert.Equal(s.Batch1, first[s.Q3].FirstSubmittedBatchId); Assert.Equal(batches[s.Batch1].CreatedAtUtc, first[s.Q3].FirstSubmittedForApprovalAtUtc); Assert.Equal(QuotationSubmissionSources.BackfillBatch, first[s.Q3].FirstSubmittedSource);   // both → earliest, pointer consistent with timestamp
        Assert.Null(first[s.Q4].FirstSubmittedBatchId); Assert.Equal(lineB.UpdatedAtUtc ?? first[s.Q4].CreatedAtUtc, first[s.Q4].FirstSubmittedForApprovalAtUtc); Assert.Equal(QuotationSubmissionSources.BackfillLineAwardEstimated, first[s.Q4].FirstSubmittedSource);   // legacy award → estimated
        Assert.Null(first[s.Q5].FirstSubmittedForApprovalAtUtc); Assert.Null(first[s.Q5].FirstSubmittedBatchId); Assert.Null(first[s.Q5].FirstSubmittedSource);   // never submitted

        // Estimated marker is presented as approximate once the award pointer is gone (marker-only evidence).
        await ctx.Database.ExecuteSqlRawAsync("UPDATE RequestLineItems SET SelectedQuotationItemId = NULL WHERE Id = {0}", s.LineB);
        var q4 = await ctx.Quotations.Include(q => q.Items).SingleAsync(q => q.Id == s.Q4);
        var q4State = await QuotationSubmissionLock.EvaluateAsync(ctx, q4);
        Assert.True(q4State.IsSubmitted); Assert.Equal(QuotationSubmissionLock.EvidenceDurableMarker, q4State.Evidence); Assert.Contains("data de referência aproximada", q4State.Reason);
        var q1 = await ctx.Quotations.Include(q => q.Items).SingleAsync(q => q.Id == s.Q1);
        Assert.DoesNotContain("aproximada", (await QuotationSubmissionLock.EvaluateAsync(ctx, q1)).Reason);   // batch-backed rows are not "approximate"

        // Backfilled batch evidence, once its reference is gone, is worded as a batch record (creation date), not as an exact submission instant.
        await ctx.Database.ExecuteSqlRawAsync("UPDATE bi SET SelectedQuotationItemId = NULL FROM ApprovalBatchItems bi WHERE bi.ApprovalBatchId = {0}", s.Batch2);
        var q2 = await ctx.Quotations.Include(q => q.Items).SingleAsync(q => q.Id == s.Q2);
        var q2State = await QuotationSubmissionLock.EvaluateAsync(ctx, q2);
        Assert.True(q2State.IsSubmitted); Assert.Equal(QuotationSubmissionLock.EvidenceDurableMarker, q2State.Evidence); Assert.Equal(2, q2State.BatchNumber);
        Assert.Contains("registo de submissão", q2State.Reason); Assert.Contains("criado em", q2State.Reason); Assert.DoesNotContain("submetida para aprovação em", q2State.Reason);

        await ctx.Database.ExecuteSqlRawAsync(AddQuotationSubmissionMarker.BackfillSql);   // idempotent
        var second = await ctx.Quotations.AsNoTracking().Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id);
        foreach (var id in ids) { Assert.Equal(first[id].FirstSubmittedForApprovalAtUtc, second[id].FirstSubmittedForApprovalAtUtc); Assert.Equal(first[id].FirstSubmittedBatchId, second[id].FirstSubmittedBatchId); }
    }
}
