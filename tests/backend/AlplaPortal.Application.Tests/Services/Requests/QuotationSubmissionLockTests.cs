using System;
using System.Collections.Generic;
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
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// §7 — ordinary Edit/Delete only for quotations never submitted for approval. Lock = any line referenced as a
/// selected item or as a candidate of ANY batch, whatever the batch status (rejected / cancelled / adjustment do not
/// unlock). Judged per quotation: a sibling quotation never submitted stays editable. Enforced in PUT and DELETE before
/// any mutation or notification; exposed on the request detail and the Workspace projection.
/// </summary>
public class QuotationSubmissionLockTests
{
    private const int S_WAITING_QUOTATION = 1, S_AREA_ADJUSTMENT = 2;
    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Seed { public Guid RequestId, SubmittedQuotation, FreeQuotation, SubmittedItem, SubmittedItem2, FreeItem, LineItemId, Buyer, Requester, SubmittedAttachment, FreeAttachment; }

    /// <summary>Request with TWO quotations: "Submitted" (two lines) and "Free" (one line). <paramref name="reference"/>: NONE | CANDIDATE | SELECTED | BOTH.</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string reference = "CANDIDATE", string batchStatus = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, int requestStatusId = S_WAITING_QUOTATION)
    {
        var s = new Seed { RequestId = Guid.NewGuid(), SubmittedQuotation = Guid.NewGuid(), FreeQuotation = Guid.NewGuid(), SubmittedItem = Guid.NewGuid(), SubmittedItem2 = Guid.NewGuid(), FreeItem = Guid.NewGuid(), LineItemId = Guid.NewGuid(), Buyer = Guid.NewGuid(), Requester = Guid.NewGuid(), SubmittedAttachment = Guid.NewGuid(), FreeAttachment = Guid.NewGuid() };
        if (!await ctx.RequestTypes.AnyAsync())
        {
            ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
            ctx.RequestStatuses.AddRange(new RequestStatus { Id = S_WAITING_QUOTATION, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Aguardando Cotação" },
                                         new RequestStatus { Id = S_AREA_ADJUSTMENT, Code = RequestConstants.Statuses.AreaAdjustment, Name = "Reajuste de Área" });
            ctx.Companies.Add(new Company { Id = 1, Name = "Alpla", IsActive = true });
            ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 3", CompanyId = 1 });
            ctx.Set<Department>().Add(new Department { Id = 5, Name = "Manutenção", IsActive = true });
            ctx.Suppliers.Add(new Supplier { Id = 10, Name = "Socimorcasal Angola", TaxId = "5010", RegistrationStatus = "ACTIVE" });
        }
        ctx.Users.AddRange(new User { Id = s.Buyer, FullName = "Samuel Mpoco", Email = "samuel@test.local", IsActive = true },
                           new User { Id = s.Requester, FullName = "Requisitante", Email = "req@test.local", IsActive = true });
        ctx.Requests.Add(new Request { Id = s.RequestId, RequestNumber = "REQ-05/10/2026-517", Title = "Peças", RequestTypeId = 1, StatusId = requestStatusId, RequesterId = s.Requester, BuyerId = s.Buyer, DepartmentId = 5, CompanyId = 1, PlantId = 1, EstimatedTotalAmount = 0m, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), UpdatedAtUtc = DateTime.UtcNow });
        ctx.RequestLineItems.Add(new RequestLineItem { Id = s.LineItemId, RequestId = s.RequestId, LineNumber = 1, Description = "Peça A", Quantity = 1, CreatedAtUtc = DateTime.UtcNow });
        ctx.RequestAttachments.AddRange(
            new RequestAttachment { Id = s.SubmittedAttachment, RequestId = s.RequestId, AttachmentTypeCode = RequestAttachment.TYPE_QUOTATION, FileName = "a.pdf", StorageReference = "a.pdf", UploadedByUserId = s.Buyer, UploadedAtUtc = DateTime.UtcNow },
            new RequestAttachment { Id = s.FreeAttachment, RequestId = s.RequestId, AttachmentTypeCode = RequestAttachment.TYPE_QUOTATION, FileName = "b.pdf", StorageReference = "b.pdf", UploadedByUserId = s.Buyer, UploadedAtUtc = DateTime.UtcNow });
        ctx.Quotations.AddRange(
            new Quotation { Id = s.SubmittedQuotation, RequestId = s.RequestId, SupplierId = 10, SupplierNameSnapshot = "Socimorcasal Angola", DocumentNumber = "OR 1", Currency = "AOA", TotalAmount = 100m, SourceType = "MANUAL", ProformaAttachmentId = s.SubmittedAttachment, CreatedAtUtc = DateTime.UtcNow.AddHours(-2), CreatedByUserId = s.Buyer,
                Items = new List<QuotationItem> { new() { Id = s.SubmittedItem, Description = "Peça A", LineNumber = 1, Quantity = 1, UnitPrice = 60m, LineTotal = 60m, MappedRequestLineItemId = s.LineItemId, ReconciliationStatus = "MAPPED" }, new() { Id = s.SubmittedItem2, Description = "Extra", LineNumber = 2, Quantity = 1, UnitPrice = 40m, LineTotal = 40m, ReconciliationStatus = "EXTRA_ITEM", ReconciliationJustification = "item adicional do fornecedor" } } },
            new Quotation { Id = s.FreeQuotation, RequestId = s.RequestId, SupplierId = 10, SupplierNameSnapshot = "Socimorcasal Angola", DocumentNumber = "OR 2", Currency = "AOA", TotalAmount = 50m, SourceType = "MANUAL", ProformaAttachmentId = s.FreeAttachment, CreatedAtUtc = DateTime.UtcNow.AddHours(-1), CreatedByUserId = s.Buyer,
                Items = new List<QuotationItem> { new() { Id = s.FreeItem, Description = "Peça A", LineNumber = 1, Quantity = 1, UnitPrice = 50m, LineTotal = 50m, MappedRequestLineItemId = s.LineItemId, ReconciliationStatus = "MAPPED" } } });
        if (reference != "NONE")
        {
            var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = s.RequestId, BatchNumber = 1, Status = batchStatus, CreatedAtUtc = DateTime.UtcNow.AddHours(-1), CreatedByUserId = s.Buyer };
            var bi = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = s.LineItemId, CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
                SelectedQuotationItemId = reference is "SELECTED" or "BOTH" ? s.SubmittedItem : null };
            ctx.ApprovalBatches.Add(batch); ctx.Set<ApprovalBatchItem>().Add(bi);
            if (reference is "CANDIDATE" or "BOTH")
                ctx.ApprovalBatchItemCandidates.Add(new ApprovalBatchItemCandidate { Id = Guid.NewGuid(), ApprovalBatchItemId = bi.Id, QuotationItemId = s.SubmittedItem, QuotationId = s.SubmittedQuotation, SupplierId = 10, SupplierNameSnapshot = "Socimorcasal Angola", QuotedDescription = "Peça A", QuotedQuantity = 1, UnitPrice = 60m, GrossSubtotal = 60m, LineTotal = 60m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow.AddHours(-1) });
        }
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return s;
    }

    private static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    private static (RequestsController Ctl, Mock<IWorkflowNotificationOrchestrator> Orchestrator, Mock<INotificationService> Notif) Ctl(ApplicationDbContext ctx, Guid userId)
    {
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>(); orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Returns(Task.CompletedTask);
        var notif = new Mock<INotificationService>();
        var eligibility = new Mock<IQuotationItemEligibilityService>(); eligibility.Setup(x => x.GetEligibilityMapAsync(It.IsAny<Guid>())).ReturnsAsync(new Dictionary<Guid, QuotationItemEligibility>());
        var extraItems = new Mock<IBatchExtraItemDecisionService>(); extraItems.Setup(x => x.GetInformationalLinesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new BatchInformationalLinesDto());
        var statusSync = new Mock<IRequestStatusSyncService>(); statusSync.Setup(x => x.ComputeDisplayWorkflowStateAsync(It.IsAny<Guid>())).ReturnsAsync(string.Empty);
        var controller = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, AdminLog().Object, NullLogger<RequestsController>.Instance,
            notif.Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            statusSync.Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, eligibility.Object, extraItems.Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() } };
        return (controller, orchestrator, notif);
    }

    private static BuyerWorkspaceController WsCtl(ApplicationDbContext ctx, Guid userId)
    {
        var c = new BuyerWorkspaceController(ctx);
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")) } };
        return c;
    }

    private static SaveQuotationRequestDto EditDto(Seed s, Guid quotationId, Guid attachmentId) => new()
    {
        SupplierId = 10, Currency = "AOA", ProformaAttachmentId = attachmentId,
        Items = new List<SaveQuotationItemDto> { new() { LineNumber = 1, Description = "Peça A", Quantity = 1, UnitPrice = 55m, MappedRequestLineItemId = s.LineItemId, ReconciliationStatus = "MAPPED" } }
    };

    private sealed record Snap(string? SubmittedDoc, decimal SubmittedTotal, int SubmittedLines, bool SubmittedExists, bool SubmittedAttachmentDeleted, int Histories, int Outbox);
    private static async Task<Snap> SnapAsync(ApplicationDbContext ctx, Seed s)
    {
        ctx.ChangeTracker.Clear();
        var q = await ctx.Quotations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.SubmittedQuotation);
        var att = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.SubmittedAttachment);
        return new Snap(q?.DocumentNumber, q?.TotalAmount ?? -1, await ctx.QuotationItems.CountAsync(i => i.QuotationId == s.SubmittedQuotation), q != null, att.IsDeleted,
            await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId), await ctx.EmailOutbox.CountAsync(e => e.RequestId == s.RequestId));
    }

    [Theory]
    [InlineData("CANDIDATE", "WAITING_AREA_APPROVAL", S_WAITING_QUOTATION)]   // REQ-517 shape: scalar still in the quotation window, batch already underway
    [InlineData("SELECTED", "WAITING_AREA_APPROVAL", S_WAITING_QUOTATION)]    // legacy buyer-selected winner, no candidate rows
    [InlineData("BOTH", "WAITING_FINAL_APPROVAL", S_WAITING_QUOTATION)]
    [InlineData("CANDIDATE", "REJECTED", S_WAITING_QUOTATION)]                // rejected batch does not unlock
    [InlineData("CANDIDATE", "CANCELLED", S_WAITING_QUOTATION)]               // cancelled batch does not unlock
    [InlineData("CANDIDATE", "AREA_ADJUSTMENT", S_AREA_ADJUSTMENT)]           // adjustment state does not unlock in-place edit (revision flow instead)
    [InlineData("CANDIDATE", "APPROVED", S_WAITING_QUOTATION)]
    public async Task Submitted_quotation_is_locked_for_put_and_delete_without_any_mutation_or_notification(string reference, string batchStatus, int requestStatusId)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, reference, batchStatus, requestStatusId);
        var before = await SnapAsync(ctx, s);
        var (ctl, orchestrator, notif) = Ctl(ctx, s.Buyer);

        var put = Assert.IsType<ConflictObjectResult>((await ctl.UpdateQuotation(s.RequestId, s.SubmittedQuotation, EditDto(s, s.SubmittedQuotation, Guid.NewGuid() /* stale form tries to swap the document too */))).Result);
        var putPd = Assert.IsType<ProblemDetails>(put.Value);
        Assert.Equal(QuotationSubmissionLock.ReasonCode, putPd.Extensions["code"]); Assert.Equal(1, putPd.Extensions["batchNumber"]);
        Assert.Contains("Lote #1", putPd.Detail); Assert.Contains(batchStatus, putPd.Detail);

        var del = Assert.IsType<ConflictObjectResult>(await ctl.DeleteQuotation(s.RequestId, s.SubmittedQuotation));
        Assert.Equal(QuotationSubmissionLock.ReasonCode, Assert.IsType<ProblemDetails>(del.Value).Extensions["code"]);

        Assert.Equal(before, await SnapAsync(ctx, s));   // header, lines, document link, history and outbox untouched
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Never);
        notif.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Never_submitted_sibling_quotation_stays_editable_and_deletable_while_the_other_is_in_approval()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "CANDIDATE");
        var (ctl, _, _) = Ctl(ctx, s.Buyer);
        var upd = await ctl.UpdateQuotation(s.RequestId, s.FreeQuotation, EditDto(s, s.FreeQuotation, s.FreeAttachment));
        Assert.True(upd.Result is OkObjectResult || upd.Value != null, $"update refused: {(upd.Result as ObjectResult)?.Value}");
        ctx.ChangeTracker.Clear();
        Assert.Equal(55m, (await ctx.QuotationItems.AsNoTracking().SingleAsync(i => i.QuotationId == s.FreeQuotation)).UnitPrice);
        Assert.IsType<NoContentResult>(await ctl.DeleteQuotation(s.RequestId, s.FreeQuotation));
        ctx.ChangeTracker.Clear();
        Assert.False(await ctx.Quotations.AnyAsync(q => q.Id == s.FreeQuotation));
        Assert.True(await ctx.Quotations.AnyAsync(q => q.Id == s.SubmittedQuotation));   // the submitted one is untouched
    }

    [Fact]
    public async Task Never_submitted_quotation_without_any_batch_is_editable_and_deletable()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "NONE");
        var (ctl, _, _) = Ctl(ctx, s.Buyer);
        var upd = await ctl.UpdateQuotation(s.RequestId, s.SubmittedQuotation, EditDto(s, s.SubmittedQuotation, s.SubmittedAttachment));
        Assert.True(upd.Result is OkObjectResult || upd.Value != null);
        Assert.IsType<NoContentResult>(await ctl.DeleteQuotation(s.RequestId, s.SubmittedQuotation));
    }

    [Fact]
    public async Task Detail_and_workspace_projections_expose_the_same_server_decision_per_quotation()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "CANDIDATE", RequestConstants.ApprovalBatchStatuses.Rejected);
        var (ctl, _, _) = Ctl(ctx, s.Buyer);
        var detail = Assert.IsType<RequestDetailsDto>(Assert.IsType<OkObjectResult>((await ctl.GetRequest(s.RequestId)).Result).Value);
        var dSub = detail.Quotations.Single(q => q.Id == s.SubmittedQuotation); var dFree = detail.Quotations.Single(q => q.Id == s.FreeQuotation);
        Assert.True(dSub.IsSubmittedForApproval); Assert.Equal(1, dSub.SubmittedBatchNumber); Assert.Contains("REJECTED", dSub.SubmissionLockReason);
        Assert.False(dFree.IsSubmittedForApproval); Assert.Null(dFree.SubmissionLockReason);

        var wsRes = await WsCtl(ctx, s.Buyer).GetWorkspace(s.RequestId);
        var ws = wsRes.Value ?? (BuyerWorkspaceDto)((OkObjectResult)wsRes.Result!).Value!;
        Assert.True(ws.Quotations.Single(q => q.Id == s.SubmittedQuotation).IsSubmittedForApproval);
        Assert.Equal(1, ws.Quotations.Single(q => q.Id == s.SubmittedQuotation).SubmittedBatchNumber);
        Assert.False(ws.Quotations.Single(q => q.Id == s.FreeQuotation).IsSubmittedForApproval);
    }

    [Fact]
    public void Pure_decision_prefers_live_references_then_line_award_then_durable_marker()
    {
        var refs = new[] { new QuotationSubmissionLock.BatchReference(3, "CANCELLED"), new QuotationSubmissionLock.BatchReference(1, "REJECTED"), new QuotationSubmissionLock.BatchReference(2, "APPROVED") };
        var st = QuotationSubmissionLock.Evaluate(refs, false, null);
        Assert.True(st.IsSubmitted); Assert.Equal(1, st.BatchNumber); Assert.Equal("REJECTED", st.BatchStatus); Assert.Contains("Lote #1", st.Reason); Assert.Equal(QuotationSubmissionLock.EvidenceBatchReference, st.Evidence);

        var award = QuotationSubmissionLock.Evaluate(Array.Empty<QuotationSubmissionLock.BatchReference>(), true, null);
        Assert.True(award.IsSubmitted); Assert.Equal(QuotationSubmissionLock.EvidenceLineAward, award.Evidence); Assert.Contains("adjudicada", award.Reason);

        var at = new DateTime(2026, 10, 5, 9, 30, 0, DateTimeKind.Utc);
        var markerKnownBatch = QuotationSubmissionLock.Evaluate(Array.Empty<QuotationSubmissionLock.BatchReference>(), false, new QuotationSubmissionLock.Marker(at, 2, "REJECTED"));
        Assert.True(markerKnownBatch.IsSubmitted); Assert.Equal(QuotationSubmissionLock.EvidenceDurableMarker, markerKnownBatch.Evidence); Assert.Equal(2, markerKnownBatch.BatchNumber);
        Assert.Contains("05/10/2026 09:30 UTC", markerKnownBatch.Reason); Assert.Contains("Lote #2, REJECTED", markerKnownBatch.Reason); Assert.Contains("removidas ou recompostas", markerKnownBatch.Reason);

        var markerGoneBatch = QuotationSubmissionLock.Evaluate(Array.Empty<QuotationSubmissionLock.BatchReference>(), false, new QuotationSubmissionLock.Marker(at, null, null));
        Assert.True(markerGoneBatch.IsSubmitted); Assert.Null(markerGoneBatch.BatchNumber); Assert.Contains("já removido ou recomposto", markerGoneBatch.Reason);

        // Estimated timestamp (legacy award backfill): proven submission, approximate date, never shown as an exact instant.
        var estimated = QuotationSubmissionLock.Evaluate(Array.Empty<QuotationSubmissionLock.BatchReference>(), false, new QuotationSubmissionLock.Marker(at, null, null, QuotationSubmissionSources.BackfillLineAwardEstimated));
        Assert.True(estimated.IsSubmitted); Assert.Equal(QuotationSubmissionLock.EvidenceDurableMarker, estimated.Evidence);
        Assert.Contains("data de referência aproximada 05/10/2026", estimated.Reason); Assert.DoesNotContain("09:30", estimated.Reason); Assert.DoesNotContain("submetida para aprovação em", estimated.Reason);

        // Backfilled batch evidence: worded as a batch record with the batch creation date, never as the exact submission instant.
        var batchEvidence = QuotationSubmissionLock.Evaluate(Array.Empty<QuotationSubmissionLock.BatchReference>(), false, new QuotationSubmissionLock.Marker(at, 2, "CANCELLED", QuotationSubmissionSources.BackfillBatch));
        Assert.True(batchEvidence.IsSubmitted); Assert.Equal(2, batchEvidence.BatchNumber);
        Assert.Contains("registo de submissão", batchEvidence.Reason); Assert.Contains("Lote #2, CANCELLED, criado em 05/10/2026", batchEvidence.Reason);
        Assert.DoesNotContain("09:30", batchEvidence.Reason); Assert.DoesNotContain("submetida para aprovação em", batchEvidence.Reason);
        var batchEvidenceGone = QuotationSubmissionLock.Evaluate(Array.Empty<QuotationSubmissionLock.BatchReference>(), false, new QuotationSubmissionLock.Marker(at, null, null, QuotationSubmissionSources.BackfillBatch));
        Assert.Contains("lote de aprovação criado em 05/10/2026, já removido ou recomposto", batchEvidenceGone.Reason);

        // Live stamp (SUBMISSION) keeps the exact instant.
        var live = QuotationSubmissionLock.Evaluate(Array.Empty<QuotationSubmissionLock.BatchReference>(), false, new QuotationSubmissionLock.Marker(at, 1, "APPROVED", QuotationSubmissionSources.Submission));
        Assert.Contains("submetida para aprovação em 05/10/2026 09:30 UTC", live.Reason);

        Assert.False(QuotationSubmissionLock.Evaluate(Array.Empty<QuotationSubmissionLock.BatchReference>(), false, null).IsSubmitted);
    }

    // ───────────────────────────── durable marker (stamped inside SaveChanges) ─────────────────────────────

    [Theory]
    [InlineData("CANDIDATE")]
    [InlineData("SELECTED")]
    public async Task Seeding_a_submission_stamps_the_marker_in_the_same_save_and_leaves_the_sibling_untouched(string reference)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, reference);
        var submitted = await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.SubmittedQuotation);
        var free = await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.FreeQuotation);
        var batchId = await ctx.ApprovalBatches.Select(b => b.Id).SingleAsync();
        Assert.NotNull(submitted.FirstSubmittedForApprovalAtUtc); Assert.Equal(batchId, submitted.FirstSubmittedBatchId); Assert.Equal(QuotationSubmissionSources.Submission, submitted.FirstSubmittedSource);
        Assert.Null(free.FirstSubmittedForApprovalAtUtc); Assert.Null(free.FirstSubmittedBatchId); Assert.Null(free.FirstSubmittedSource);
    }

    [Fact]
    public async Task Legacy_award_removal_or_reassignment_never_unlocks_a_marked_quotation_and_stamps_the_new_target()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "NONE");
        var li = await ctx.RequestLineItems.SingleAsync(x => x.Id == s.LineItemId);
        li.SelectedQuotationItemId = s.SubmittedItem; await ctx.SaveChangesAsync();
        var stamped = await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.SubmittedQuotation);
        Assert.NotNull(stamped.FirstSubmittedForApprovalAtUtc); Assert.Equal(QuotationSubmissionSources.Submission, stamped.FirstSubmittedSource);

        li.SelectedQuotationItemId = null; await ctx.SaveChangesAsync();   // award removed
        ctx.ChangeTracker.Clear();
        var afterRemoval = await ctx.Quotations.Include(q => q.Items).SingleAsync(q => q.Id == s.SubmittedQuotation);
        Assert.Equal(stamped.FirstSubmittedForApprovalAtUtc, afterRemoval.FirstSubmittedForApprovalAtUtc);
        var st = await QuotationSubmissionLock.EvaluateAsync(ctx, afterRemoval);
        Assert.True(st.IsSubmitted); Assert.Equal(QuotationSubmissionLock.EvidenceDurableMarker, st.Evidence); Assert.DoesNotContain("aproximada", st.Reason);   // live stamp = exact instant

        var li2 = await ctx.RequestLineItems.SingleAsync(x => x.Id == s.LineItemId);
        li2.SelectedQuotationItemId = s.FreeItem; await ctx.SaveChangesAsync();   // award moved to the sibling
        ctx.ChangeTracker.Clear();
        Assert.NotNull((await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.FreeQuotation)).FirstSubmittedForApprovalAtUtc);   // new target stamped
        Assert.Equal(stamped.FirstSubmittedForApprovalAtUtc, (await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.SubmittedQuotation)).FirstSubmittedForApprovalAtUtc);   // old one unchanged

        var (ctl, _, _) = Ctl(ctx, s.Buyer);
        Assert.IsType<ConflictObjectResult>((await ctl.UpdateQuotation(s.RequestId, s.SubmittedQuotation, EditDto(s, s.SubmittedQuotation, s.SubmittedAttachment))).Result);
        Assert.IsType<ConflictObjectResult>(await ctl.DeleteQuotation(s.RequestId, s.SubmittedQuotation));
        Assert.IsType<ConflictObjectResult>(await ctl.DeleteQuotation(s.RequestId, s.FreeQuotation));
    }

    [Fact]
    public async Task Legacy_line_level_award_stamps_the_marker_without_a_batch_and_locks_the_quotation()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "NONE");
        var li = await ctx.RequestLineItems.SingleAsync(x => x.Id == s.LineItemId);
        li.SelectedQuotationItemId = s.SubmittedItem;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var q = await ctx.Quotations.Include(x => x.Items).SingleAsync(x => x.Id == s.SubmittedQuotation);
        Assert.NotNull(q.FirstSubmittedForApprovalAtUtc); Assert.Null(q.FirstSubmittedBatchId);
        var st = await QuotationSubmissionLock.EvaluateAsync(ctx, q);
        Assert.True(st.IsSubmitted); Assert.Equal(QuotationSubmissionLock.EvidenceLineAward, st.Evidence);
        var (ctl, _, _) = Ctl(ctx, s.Buyer);
        Assert.IsType<ConflictObjectResult>((await ctl.UpdateQuotation(s.RequestId, s.SubmittedQuotation, EditDto(s, s.SubmittedQuotation, s.SubmittedAttachment))).Result);
        Assert.IsType<ConflictObjectResult>(await ctl.DeleteQuotation(s.RequestId, s.SubmittedQuotation));
    }

    [Fact]
    public async Task Repeated_submission_keeps_the_first_marker()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "CANDIDATE");
        var first = await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.SubmittedQuotation);
        var batch2 = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = s.RequestId, BatchNumber = 2, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Buyer };
        var bi2 = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch2.Id, RequestLineItemId = s.LineItemId, CreatedAtUtc = DateTime.UtcNow };
        ctx.ApprovalBatches.Add(batch2); ctx.Set<ApprovalBatchItem>().Add(bi2);
        ctx.ApprovalBatchItemCandidates.Add(new ApprovalBatchItemCandidate { Id = Guid.NewGuid(), ApprovalBatchItemId = bi2.Id, QuotationItemId = s.SubmittedItem2, QuotationId = s.SubmittedQuotation, SupplierId = 10, SupplierNameSnapshot = "Socimorcasal Angola", QuotedDescription = "Extra", QuotedQuantity = 1, UnitPrice = 40m, GrossSubtotal = 40m, LineTotal = 40m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var after = await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == s.SubmittedQuotation);
        Assert.Equal(first.FirstSubmittedForApprovalAtUtc, after.FirstSubmittedForApprovalAtUtc);
        Assert.Equal(first.FirstSubmittedBatchId, after.FirstSubmittedBatchId);
        Assert.NotEqual(batch2.Id, after.FirstSubmittedBatchId);
    }

    [Fact]
    public async Task Marker_is_never_cleared_or_moved_by_a_later_save()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "CANDIDATE");
        var q = await ctx.Quotations.SingleAsync(x => x.Id == s.SubmittedQuotation);
        var (at, batchId, source) = (q.FirstSubmittedForApprovalAtUtc, q.FirstSubmittedBatchId, q.FirstSubmittedSource);
        q.FirstSubmittedForApprovalAtUtc = null; q.FirstSubmittedBatchId = Guid.NewGuid(); q.FirstSubmittedSource = "TAMPERED"; q.DocumentNumber = "OR 1-B";
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var after = await ctx.Quotations.AsNoTracking().SingleAsync(x => x.Id == s.SubmittedQuotation);
        Assert.Equal(at, after.FirstSubmittedForApprovalAtUtc); Assert.Equal(batchId, after.FirstSubmittedBatchId); Assert.Equal(source, after.FirstSubmittedSource); Assert.Equal("OR 1-B", after.DocumentNumber);

        // Replacing with another NON-NULL value is refused too (sync SaveChanges path).
        var q2 = await ctx.Quotations.SingleAsync(x => x.Id == s.SubmittedQuotation);
        q2.FirstSubmittedForApprovalAtUtc = DateTime.UtcNow.AddYears(1); q2.FirstSubmittedBatchId = Guid.NewGuid();
        ctx.SaveChanges(); ctx.ChangeTracker.Clear();
        var after2 = await ctx.Quotations.AsNoTracking().SingleAsync(x => x.Id == s.SubmittedQuotation);
        Assert.Equal(at, after2.FirstSubmittedForApprovalAtUtc); Assert.Equal(batchId, after2.FirstSubmittedBatchId);
    }

    [Theory]
    [InlineData(false)]   // candidate + batch item removed (rework recomposition shape), batch row kept
    [InlineData(true)]    // whole batch removed (cascade) — marker points to a batch that no longer exists
    public async Task Marker_alone_keeps_the_lock_after_the_references_are_removed(bool removeWholeBatch)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "BOTH");
        var batch = await ctx.ApprovalBatches.Include(b => b.Items).ThenInclude(i => i.Candidates).SingleAsync();
        if (removeWholeBatch) ctx.ApprovalBatches.Remove(batch);
        else { foreach (var bi in batch.Items.ToList()) { ctx.ApprovalBatchItemCandidates.RemoveRange(bi.Candidates); ctx.Set<ApprovalBatchItem>().Remove(bi); } }
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.False(await ctx.ApprovalBatchItemCandidates.AnyAsync()); Assert.False(await ctx.Set<ApprovalBatchItem>().AnyAsync());

        var q = await ctx.Quotations.Include(x => x.Items).SingleAsync(x => x.Id == s.SubmittedQuotation);
        Assert.NotNull(q.FirstSubmittedForApprovalAtUtc); Assert.Equal(batch.Id, q.FirstSubmittedBatchId);   // pointer survives, never nulled
        var st = await QuotationSubmissionLock.EvaluateAsync(ctx, q);
        Assert.True(st.IsSubmitted); Assert.Equal(QuotationSubmissionLock.EvidenceDurableMarker, st.Evidence);
        if (removeWholeBatch) { Assert.Null(st.BatchNumber); Assert.Contains("já removido ou recomposto", st.Reason); }
        else { Assert.Equal(1, st.BatchNumber); Assert.Contains("Lote #1", st.Reason); }

        var before = await SnapAsync(ctx, s);
        var (ctl, orchestrator, notif) = Ctl(ctx, s.Buyer);
        var put = Assert.IsType<ConflictObjectResult>((await ctl.UpdateQuotation(s.RequestId, s.SubmittedQuotation, EditDto(s, s.SubmittedQuotation, s.SubmittedAttachment))).Result);
        Assert.Equal(QuotationSubmissionLock.EvidenceDurableMarker, Assert.IsType<ProblemDetails>(put.Value).Extensions["evidence"]);
        Assert.IsType<ConflictObjectResult>(await ctl.DeleteQuotation(s.RequestId, s.SubmittedQuotation));
        Assert.Equal(before, await SnapAsync(ctx, s));
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Never); notif.VerifyNoOtherCalls();

        // Projections agree with the guards.
        var detail = Assert.IsType<RequestDetailsDto>(Assert.IsType<OkObjectResult>((await ctl.GetRequest(s.RequestId)).Result).Value);
        Assert.True(detail.Quotations.Single(x => x.Id == s.SubmittedQuotation).IsSubmittedForApproval);
        Assert.False(detail.Quotations.Single(x => x.Id == s.FreeQuotation).IsSubmittedForApproval);
        var wsRes = await WsCtl(ctx, s.Buyer).GetWorkspace(s.RequestId);
        var ws = wsRes.Value ?? (BuyerWorkspaceDto)((OkObjectResult)wsRes.Result!).Value!;
        Assert.True(ws.Quotations.Single(x => x.Id == s.SubmittedQuotation).IsSubmittedForApproval);
        Assert.False(ws.Quotations.Single(x => x.Id == s.FreeQuotation).IsSubmittedForApproval);
    }
}
