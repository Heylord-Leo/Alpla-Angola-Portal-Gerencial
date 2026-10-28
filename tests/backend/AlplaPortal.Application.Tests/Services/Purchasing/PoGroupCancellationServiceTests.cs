using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;
using C = AlplaPortal.Domain.Constants.PoGroupCancellationConstants;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// Phase 1 P.O. group cancellation — service behaviour on InMemory EF: restart / close outcomes, preserved evidence,
/// sibling isolation, the no-P.O. case, legacy Finance returns, refusals, D1 ownership, D4 ECF declaration and Finance
/// confirmation (D12 authorization matrix), declaration updates, D13 resend, D8 request cancellation, eligibility read
/// model, Finance queue. Transactions are ignored by the provider — atomicity and locks are proven relationally.
/// </summary>
public class PoGroupCancellationServiceTests
{
    private static async Task<(ApplicationDbContext Ctx, S.Seed Seed, PoGroupCancellationService Svc, System.Collections.Generic.List<Domain.Events.WorkflowEvent> Events)> ArrangeAsync(
        string groupAStatus = "PO_ISSUED", string? poNumber = "ECF10 2026/364", bool poAttachment = true, string groupBStatus = "WAITING_PO", string requestStatus = "PO_PARTIALLY_UPLOADED",
        string requestType = "QUOTATION", string? line1Status = "WAITING_QUOTATION", int financeCount = 2, string? legacyReturnReason = null)
    {
        var ctx = S.NewCtx();
        var seed = await S.SeedAsync(ctx, groupAStatus, poNumber, poAttachment, groupBStatus, requestStatus, requestType, line1Status, legacyReturnReason: legacyReturnReason);
        var (orch, events) = S.OrchestratorMock(financeCount);
        return (ctx, seed, S.Service(ctx, orch.Object), events);
    }

    private static T Payload<T>(PoGroupCancellationOperationResult r) { Assert.True(r.Succeeded, $"{r.Status} {r.Code}: {r.Detail}"); return Assert.IsType<T>(r.Payload); }

    // ═══ Restart quotation with a registered P.O. (REQ-483 shape) ═══

    [Fact]
    public async Task Restart_with_registered_PO_releases_line_voids_PO_keeps_evidence_and_requires_Finance_confirmation()
    {
        var (ctx, s, svc, events) = await ArrangeAsync();
        var r = Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner))));
        Assert.Equal("RESTART_QUOTATION", r.Kind); Assert.True(r.FinanceConfirmationRequired); Assert.Equal(1, r.ReleasedLineCount); Assert.Equal(0, r.ClosedLineCount);
        Assert.Equal(1, r.VoidedPoAttachments); Assert.Equal(2, r.FinanceRecipientsResolvedCount); Assert.Null(r.NotificationWarning); Assert.False(r.RequestCancelled);
        ctx.ChangeTracker.Clear();

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA);
        Assert.Equal("CANCELLED", group.Status); Assert.Equal("ECF10 2026/364", group.PurchaseOrderNumber);   // number kept on the historical group
        var line = await ctx.RequestLineItems.AsNoTracking().SingleAsync(l => l.Id == s.Line1);
        Assert.Equal(RequestConstants.QuotationLifecycleStatuses.QuotationPending, line.QuotationLifecycleStatus); Assert.Null(line.RequestPoGroupId); Assert.Null(line.SelectedQuotationItemId); Assert.Equal(1, line.LineItemStatusId);
        var bi = await ctx.ApprovalBatchItems.AsNoTracking().SingleAsync(x => x.Id == s.BatchItem1);
        Assert.NotNull(bi.ReleasedAtUtc); Assert.Equal("RESTART_QUOTATION", bi.ReleaseKind); Assert.Equal(s.Owner, bi.ReleasedByUserId); Assert.Equal(s.Qi1, bi.SelectedQuotationItemId);   // winner stamp preserved
        Assert.Equal("APPROVED", (await ctx.ApprovalBatches.AsNoTracking().SingleAsync(b => b.Id == s.Batch)).Status);
        Assert.Equal(24633.06m, (await ctx.Requests.AsNoTracking().SingleAsync(x => x.Id == s.RequestId)).ApprovedTotalAmount);   // historical total untouched
        var att = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.PoAttachmentA);
        Assert.False(att.IsDeleted); Assert.NotNull(att.VoidedAtUtc); Assert.Contains("comprador declara que a ECF ECF10 2026/364 JÁ foi anulada", att.VoidReason);
        Assert.Equal(1, await ctx.Quotations.CountAsync(q => q.Id == s.Q1));   // quotation preserved

        var c = await ctx.RequestPoGroupCancellations.AsNoTracking().Include(x => x.Items).Include(x => x.EcfEvents).SingleAsync(x => x.RequestPoGroupId == s.GroupA);
        Assert.Equal("PO_ISSUED", c.PreviousGroupStatus); Assert.Equal("ECF10 2026/364", c.PurchaseOrderNumberSnapshot); Assert.Equal(24133.06m, c.TotalAmountSnapshot); Assert.Equal("RND", c.CurrencyCodeSnapshot); Assert.Equal(1, c.ApprovalBatchNumberSnapshot);
        Assert.True(c.FinanceConfirmationRequired); Assert.Equal("DECLARED_CANCELLED", c.EcfStatus); Assert.Equal("ECF10 2026/364", c.EcfReference); Assert.Equal(s.Owner, c.EcfDeclaredByUserId);
        Assert.Equal("PENDING", c.FinanceConfirmationStatus); Assert.Null(c.FinanceConfirmedByUserId);
        var item = Assert.Single(c.Items); Assert.Equal(s.Line1, item.RequestLineItemId); Assert.Equal("RELEASED", item.Outcome); Assert.Equal("WAITING_QUOTATION", item.PreviousLineItemStatusCode); Assert.Equal(s.Qi1, item.PreviousSelectedQuotationItemId);
        var ev = Assert.Single(c.EcfEvents); Assert.Equal("BUYER_DECLARED", ev.EventType); Assert.Equal("BUYER", ev.ActorRoleSnapshot);
        Assert.Equal(2, c.FinanceRecipientsResolvedCount); Assert.True(c.BuyerRecipientResolved); Assert.NotNull(c.LastNotificationCorrelationId);

        var actions = (await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == s.RequestId).OrderBy(h => h.CreatedAtUtc).Select(h => h.ActionTaken).ToListAsync());
        Assert.Contains(C.HistoryActions.GroupCancelled, actions); Assert.Contains(C.HistoryActions.ItemsReleased, actions); Assert.DoesNotContain("CANCELLED", actions);
        var main = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.ActionTaken == C.HistoryActions.GroupCancelled);
        Assert.Contains("Confirmação de Finanças: pendente", main.Comment); Assert.Contains("declaração do comprador", main.Comment);
        Assert.Equal(main.Id, c.LastNotificationCorrelationId);
        Assert.True(await ctx.RequestFieldChangeHistories.AnyAsync(h => h.LineItemId == s.Line1 && h.FieldName == "QuotationLifecycleStatus"));

        // request scalar: line 1 pending again, sibling group B still WAITING_PO and nothing crossed the P.O. gate → WAITING_QUOTATION
        Assert.Equal("WAITING_QUOTATION", r.RequestStatusCode);
        var e = Assert.Single(events);
        Assert.Equal(WorkflowEventCodes.PoGroupCancelled, e.EventCode); Assert.Equal(c.Id, e.PoCancellationId); Assert.Equal("DECLARED_CANCELLED", e.EcfStatus); Assert.True(e.FinanceConfirmationRequired);
        Assert.Equal("ECF10 2026/364", e.PreviousPoNumber); Assert.Equal(main.Id, e.CorrelationId); Assert.Equal(S.Plant, e.PlantId); Assert.Equal(S.Company, e.CompanyId); Assert.Equal(s.Requester, e.RequesterId);
    }

    [Fact]
    public async Task Sibling_group_and_its_line_are_untouched()
    {
        var (ctx, s, svc, _) = await ArrangeAsync(groupBStatus: "PO_ISSUED", requestStatus: "PO_ISSUED");
        ctx.RequestPayments.Add(new RequestPayment { Id = 7, RequestId = s.RequestId, RequestPoGroupId = s.GroupB, PaymentType = "FINAL_BALANCE", PaymentStatus = "SCHEDULED", PlannedAmount = 500m, CurrencyCode = "AOA", CreatedByUserId = s.Owner, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var before = await Snapshot(ctx, s);
        Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner))));
        ctx.ChangeTracker.Clear();
        Assert.Equal(before, await Snapshot(ctx, s));
        // request scalar: sibling crossed the P.O. gate → group phase; the only eligible live group is PO_ISSUED
        Assert.Equal("PO_ISSUED", (await ctx.Requests.AsNoTracking().Include(r => r.Status).SingleAsync(r => r.Id == s.RequestId)).Status.Code);
    }

    private static async Task<string> Snapshot(ApplicationDbContext ctx, S.Seed s)
    {
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupB);
        var l = await ctx.RequestLineItems.AsNoTracking().SingleAsync(x => x.Id == s.Line2);
        var bi = await ctx.ApprovalBatchItems.AsNoTracking().SingleAsync(x => x.Id == s.BatchItem2);
        var p = await ctx.RequestPayments.AsNoTracking().Where(x => x.RequestPoGroupId == s.GroupB).Select(x => x.PaymentStatus).ToListAsync();
        return $"{g.Status}|{g.PurchaseOrderNumber}|{l.QuotationLifecycleStatus}|{l.RequestPoGroupId}|{l.SelectedQuotationItemId}|{l.LineItemStatusId}|{bi.ReleasedAtUtc}|{string.Join(",", p)}";
    }

    [Fact]
    public async Task Close_items_closes_the_line_and_cancels_the_request_only_when_nothing_active_remains()
    {
        // sibling still live → request continues
        var (ctx, s, svc, _) = await ArrangeAsync();
        var r = Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), kind: "CANCEL_ITEMS")));
        Assert.False(r.RequestCancelled); Assert.Equal(1, r.ClosedLineCount);
        ctx.ChangeTracker.Clear();
        var line = await ctx.RequestLineItems.AsNoTracking().SingleAsync(l => l.Id == s.Line1);
        Assert.Equal(RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval, line.QuotationLifecycleStatus); Assert.Equal(7, line.LineItemStatusId);
        Assert.Equal(s.GroupA, line.RequestPoGroupId); Assert.Equal(s.Qi1, line.SelectedQuotationItemId);   // evidence pointers kept
        Assert.False((await ctx.Requests.AsNoTracking().SingleAsync(x => x.Id == s.RequestId)).IsCancelled);
        Assert.Contains(C.HistoryActions.ItemsClosed, await ctx.RequestStatusHistories.Select(h => h.ActionTaken).ToListAsync());

        // sibling already cancelled → D8: request CANCELLED
        var (ctx2, s2, svc2, _) = await ArrangeAsync(groupBStatus: "CANCELLED");
        var l2 = await ctx2.RequestLineItems.SingleAsync(l => l.Id == s2.Line2); l2.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval; await ctx2.SaveChangesAsync(); ctx2.ChangeTracker.Clear();
        var r2 = Payload<PoGroupCancellationResultDto>(await svc2.CancelAsync(S.Cancel(s2, S.Buyer(s2.Owner), kind: "CANCEL_ITEMS")));
        Assert.True(r2.RequestCancelled); Assert.Equal("CANCELLED", r2.RequestStatusCode);
        ctx2.ChangeTracker.Clear();
        var req = await ctx2.Requests.AsNoTracking().Include(x => x.Status).SingleAsync(x => x.Id == s2.RequestId);
        Assert.True(req.IsCancelled); Assert.Equal("CANCELLED", req.Status.Code);
        Assert.True(await ctx2.RequestStatusHistories.AnyAsync(h => h.RequestId == s2.RequestId && h.ActionTaken == "CANCELLED"));
    }

    [Fact]
    public async Task Planned_advance_is_cancelled_and_kept_as_audit()
    {
        var (ctx, s, svc, _) = await ArrangeAsync(groupAStatus: "ADVANCE_PAYMENT_REQUIRED");
        ctx.RequestPayments.Add(new RequestPayment { Id = 3, RequestId = s.RequestId, RequestPoGroupId = s.GroupA, PaymentType = "ADVANCE", PaymentStatus = "PLANNED", PlannedAmount = 1000m, CurrencyCode = "RND", CreatedByUserId = s.Owner, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var r = Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner))));
        Assert.Equal(1, r.CancelledPlannedPayments);
        Assert.Equal("CANCELLED", (await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.Id == 3)).PaymentStatus);
    }

    // ═══ No P.O. ever → no ECF, no Finance confirmation ═══

    [Fact]
    public async Task Without_any_PO_no_declaration_is_needed_and_no_confirmation_is_required()
    {
        var (ctx, s, svc, events) = await ArrangeAsync(groupAStatus: "WAITING_PO", poNumber: null, poAttachment: false, requestStatus: "PO_REQUESTED");
        var r = Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), ecfStatus: null, ecfReference: null)));
        Assert.False(r.FinanceConfirmationRequired); Assert.Equal(0, r.VoidedPoAttachments);
        var c = await ctx.RequestPoGroupCancellations.AsNoTracking().Include(x => x.EcfEvents).SingleAsync();
        Assert.Equal("NOT_APPLICABLE", c.EcfStatus); Assert.Equal("NOT_REQUIRED", c.FinanceConfirmationStatus); Assert.Empty(c.EcfEvents);
        Assert.False(events.Single().FinanceConfirmationRequired);
        var elig = await svc.EvaluateEligibilityAsync(s.GroupB, S.Buyer(s.Owner));
        Assert.False(elig.FinanceConfirmationWouldBeRequired);
        // Finance review on it is not applicable
        var rv = await svc.FinanceReviewAsync(new FinanceReviewCommand(c.Id, S.Finance(s.Finance1), true, null, null));
        Assert.Equal(409, rv.Status); Assert.Equal(C.ErrorCodes.FinanceConfirmationNotRequired, rv.Code);
    }

    [Fact]
    public async Task A_voided_PO_document_or_REGISTER_PO_history_alone_still_requires_confirmation()
    {
        var (ctx, s, svc, _) = await ArrangeAsync(groupAStatus: "WAITING_PO", poNumber: null, poAttachment: true);
        Assert.True((await svc.EvaluateEligibilityAsync(s.GroupA, S.Buyer(s.Owner))).FinanceConfirmationWouldBeRequired);
        var missing = await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), ecfStatus: null, ecfReference: null));
        Assert.Equal(400, missing.Status); Assert.Equal(C.ErrorCodes.EcfDeclarationRequired, missing.Code);
    }

    // ═══ Legacy Finance return (REQ-483: returned before the kind existed) ═══

    [Fact]
    public async Task Legacy_return_is_shown_unclassified_with_its_original_reason_and_cancellation_proceeds_without_a_second_return()
    {
        var (ctx, s, svc, _) = await ArrangeAsync(groupAStatus: "WAITING_PO_CORRECTION", requestStatus: "WAITING_PO_CORRECTION", legacyReturnReason: "PEDIDO CANCELADO");
        var last = await svc.GetLastFinanceReturnAsync(s.GroupA);
        Assert.NotNull(last); Assert.Null(last!.Kind); Assert.Equal("HISTORY", last.Source); Assert.Equal("PEDIDO CANCELADO", last.Notes); Assert.Equal("Finance Um", last.ReturnedByName);
        Assert.Equal("Tipo não classificado (devolução anterior à versão)", last.KindLabel);
        var r = Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner))));
        Assert.True(r.FinanceConfirmationRequired);
        var c = await ctx.RequestPoGroupCancellations.AsNoTracking().SingleAsync();
        Assert.Equal("WAITING_PO_CORRECTION", c.PreviousGroupStatus); Assert.NotNull(c.OriginFinanceReturnHistoryId);
        Assert.Null((await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).LastFinanceReturnKind);   // never backfilled
    }

    // ═══ Refusals ═══

    [Theory]
    [InlineData("PAYMENT_SCHEDULED", C.ErrorCodes.PaymentScheduledActive)]
    [InlineData("PAYMENT_COMPLETED", C.ErrorCodes.PaymentOrReceiptEvidence)]
    [InlineData("PENDING", C.ErrorCodes.GroupStatusNotCancellable)]
    [InlineData("CANCELLED", C.ErrorCodes.GroupAlreadyCancelled)]
    public async Task Group_status_refusals(string status, string code)
    {
        var (ctx, s, svc, _) = await ArrangeAsync(groupAStatus: status);
        var r = await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner)));
        Assert.Equal(409, r.Status); Assert.Equal(code, r.Code);
        Assert.Equal(status, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).Status);
        Assert.Equal(0, await ctx.RequestPoGroupCancellations.CountAsync());
    }

    [Theory]
    [InlineData("RECEIVED", C.ErrorCodes.PaymentOrReceiptEvidence)]
    [InlineData("PARTIALLY_RECEIVED", C.ErrorCodes.PaymentOrReceiptEvidence)]
    [InlineData("ORDERED", C.ErrorCodes.OrderedLineRequiresInvestigation)]
    public async Task Line_status_refusals_leave_everything_untouched(string lineStatus, string code)
    {
        var (ctx, s, svc, _) = await ArrangeAsync(line1Status: lineStatus);
        var r = await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner)));
        Assert.Equal(409, r.Status); Assert.Equal(code, r.Code);
        var line = await ctx.RequestLineItems.AsNoTracking().Include(l => l.LineItemStatus).SingleAsync(l => l.Id == s.Line1);
        Assert.Equal(lineStatus, line.LineItemStatus!.Code); Assert.Equal(s.GroupA, line.RequestPoGroupId);
        if (lineStatus == "ORDERED") Assert.Contains("requer investigação", r.Detail);
    }

    [Fact]
    public async Task Payment_type_request_is_refused()
    {
        var (_, s, svc, _) = await ArrangeAsync(requestType: "PAYMENT");
        var r = await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner)));
        Assert.Equal(409, r.Status); Assert.Equal(C.ErrorCodes.RequestTypeNotSupported, r.Code);
    }

    [Fact]
    public async Task Input_validation()
    {
        var (_, s, svc, _) = await ArrangeAsync();
        Assert.Equal(C.ErrorCodes.KindInvalid, (await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), kind: "DELETE"))).Code);
        Assert.Equal(C.ErrorCodes.ReasonCodeInvalid, (await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), reasonCode: "X"))).Code);
        Assert.Equal(C.ErrorCodes.JustificationRequired, (await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), reason: "curto"))).Code);
        Assert.Equal(C.ErrorCodes.EcfDeclarationRequired, (await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), ecfStatus: "NOT_APPLICABLE"))).Code);
        Assert.Equal(C.ErrorCodes.EcfReferenceRequired, (await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), ecfReference: " "))).Code);
        Assert.Equal(C.ErrorCodes.CancellationConcurrentChange, (await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), rowVersion: new byte[] { 1, 2, 3 }))).Code);
    }

    // ═══ D1 ownership ═══

    [Fact]
    public async Task Only_the_responsible_Buyer_or_SysAdmin_may_cancel()
    {
        var (ctx, s, svc, _) = await ArrangeAsync();
        var other = await svc.CancelAsync(S.Cancel(s, S.Buyer(s.OtherBuyer)));
        Assert.Equal(403, other.Status); Assert.Equal(C.ErrorCodes.NotResponsibleBuyer, other.Code); Assert.Contains("Assumir pedido de outro comprador", other.Detail);
        var finance = await svc.CancelAsync(S.Cancel(s, S.Finance(s.Finance1)));
        Assert.Equal(403, finance.Status);
        var elig = await svc.EvaluateEligibilityAsync(s.GroupA, S.Buyer(s.OtherBuyer));
        Assert.False(elig.CanCancel); Assert.Equal(C.ErrorCodes.NotResponsibleBuyer, elig.BlockCode);
        Assert.True((await svc.EvaluateEligibilityAsync(s.GroupA, S.Buyer(s.Owner))).CanCancel);
        Assert.True((await svc.EvaluateEligibilityAsync(s.GroupA, S.Admin(s.Admin))).CanCancel);
        // the group's P.O. responsible (not the request owner) may act too
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupA); g.PoResponsibleBuyerId = s.OtherBuyer; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.True((await svc.EvaluateEligibilityAsync(s.GroupA, S.Buyer(s.OtherBuyer))).CanCancel);
        Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Admin(s.Admin))));
    }

    [Fact]
    public async Task Out_of_scope_Buyer_gets_404()
    {
        var (ctx, s, svc, _) = await ArrangeAsync();
        var scope = await ctx.UserPlantScopes.SingleAsync(x => x.UserId == s.Owner); ctx.UserPlantScopes.Remove(scope); ctx.UserPlantScopes.Add(new UserPlantScope { UserId = s.Owner, PlantId = S.OtherPlant }); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.Equal(404, (await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner)))).Status);
    }

    // ═══ D12 Finance confirmation / rejection ═══

    private static async Task<(ApplicationDbContext, S.Seed, PoGroupCancellationService, System.Collections.Generic.List<Domain.Events.WorkflowEvent>, Guid)> CancelledAsync(string ecf = "DECLARED_CANCELLED")
    {
        var (ctx, s, svc, events) = await ArrangeAsync();
        var r = Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), ecfStatus: ecf, ecfReference: ecf == "DECLARED_CANCELLED" ? "ECF10 2026/364" : null)));
        ctx.ChangeTracker.Clear(); events.Clear();
        return (ctx, s, svc, events, r.CancellationId);
    }

    [Fact]
    public async Task Finance_in_plant_confirms_and_only_the_Buyer_is_notified()
    {
        var (ctx, s, svc, events, id) = await CancelledAsync();
        var dto = Payload<PoGroupCancellationDto>(await svc.FinanceReviewAsync(new FinanceReviewCommand(id, S.Finance(s.Finance1), true, "Verificado no Primavera.", null)));
        Assert.Equal("CONFIRMED", dto.FinanceConfirmationStatus); Assert.Equal("Finance Um", dto.FinanceConfirmedByName); Assert.NotNull(dto.FinanceConfirmedAtUtc);
        Assert.Equal("Comprador Dono", dto.EcfDeclaredByName);   // the declaration stays the Buyer's
        Assert.Contains(dto.Events, e => e.EventType == "FINANCE_CONFIRMED" && e.ActorRoleSnapshot == "FINANCE");
        Assert.False(dto.Actions.CanUpdateDeclaration); Assert.False(dto.Actions.CanFinanceReview);
        var e = Assert.Single(events); Assert.Equal(WorkflowEventCodes.PoCancellationConfirmed, e.EventCode); Assert.Equal(s.Owner, e.BuyerId);
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == C.HistoryActions.FinanceConfirmed));
        var again = await svc.FinanceReviewAsync(new FinanceReviewCommand(id, S.Finance(s.Finance2), true, null, null));
        Assert.Equal(409, again.Status); Assert.Equal(C.ErrorCodes.FinanceConfirmationAlreadyDone, again.Code);
    }

    [Fact]
    public async Task Finance_rejection_needs_a_note_keeps_the_group_cancelled_and_the_Buyer_can_update_the_declaration()
    {
        var (ctx, s, svc, events, id) = await CancelledAsync();
        var noNote = await svc.FinanceReviewAsync(new FinanceReviewCommand(id, S.Finance(s.Finance1), false, "curto", null));
        Assert.Equal(400, noNote.Status); Assert.Equal(C.ErrorCodes.JustificationRequired, noNote.Code);
        var dto = Payload<PoGroupCancellationDto>(await svc.FinanceReviewAsync(new FinanceReviewCommand(id, S.Finance(s.Finance1), false, "A ECF 2026/364 continua ativa no Primavera.", null)));
        Assert.Equal("REJECTED", dto.FinanceConfirmationStatus); Assert.Null(dto.FinanceConfirmedAtUtc); Assert.Equal("Finance Um", dto.FinanceReviewedByName);
        Assert.Equal("CANCELLED", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).Status);
        Assert.Equal(WorkflowEventCodes.PoCancellationDeclarationRejected, Assert.Single(events).EventCode);
        Assert.False(dto.Actions.CanUpdateDeclaration);   // actions are per CALLER: Finance may not touch the Buyer's declaration
        Assert.True((await svc.GetAsync(s.GroupA, S.Buyer(s.Owner)))!.Actions.CanUpdateDeclaration);
        events.Clear();
        var upd = Payload<PoGroupCancellationDto>(await svc.UpdateEcfDeclarationAsync(new UpdateEcfDeclarationCommand(id, S.Buyer(s.Owner), "DECLARED_CANCELLED", "ECF10 2026/364-A", "Anulada hoje.", null, null)));
        Assert.Equal("PENDING", upd.FinanceConfirmationStatus); Assert.Equal("ECF10 2026/364-A", upd.EcfReference);
        Assert.Contains(upd.Events, e => e.EventType == "BUYER_UPDATED");
        Assert.Equal("ECF_DECLARATION_UPDATED", Assert.Single(events).ActionTaken);   // Finance re-notified under a new correlation
        Payload<PoGroupCancellationDto>(await svc.FinanceReviewAsync(new FinanceReviewCommand(id, S.Finance(s.Finance2), true, null, null)));
        var locked = await svc.UpdateEcfDeclarationAsync(new UpdateEcfDeclarationCommand(id, S.Buyer(s.Owner), "UNKNOWN", null, null, null, null));
        Assert.Equal(409, locked.Status); Assert.Equal(C.ErrorCodes.DeclarationLocked, locked.Code);
    }

    [Fact]
    public async Task D12_authorization_matrix()
    {
        var (ctx, s, svc, _, id) = await CancelledAsync();
        async Task Expect(PoGroupCancellationActor actor, string code) { var r = await svc.FinanceReviewAsync(new FinanceReviewCommand(id, actor, true, null, null)); Assert.Equal(403, r.Status); Assert.Equal(code, r.Code); }
        await Expect(S.Buyer(s.Owner), C.ErrorCodes.FinanceScopeRequired);                 // Buyer
        await Expect(S.Admin(s.Admin), C.ErrorCodes.FinanceScopeRequired);                 // SysAdmin alone — never
        Assert.Equal(404, (await svc.FinanceReviewAsync(new FinanceReviewCommand(id, S.Finance(s.FinanceOtherPlant), true, null, null))).Status);   // other plant: out of request scope → 404, nothing disclosed
        await Expect(S.Finance(s.FinanceInactive), C.ErrorCodes.FinanceScopeRequired);     // inactive
        await Expect(S.Finance(s.OtherBuyer), C.ErrorCodes.FinanceScopeRequired);          // claims the role but has no Finance assignment in the DB
        // the declarer with a Finance role may not confirm their own declaration
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = s.Owner, RoleId = S.R_FINANCE }); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        await Expect(S.Finance(s.Owner), C.ErrorCodes.SelfConfirmationForbidden);
        // a SysAdmin who ALSO holds Finance with plant scope acts in the Finance capacity
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = s.Admin, RoleId = S.R_FINANCE }); ctx.UserPlantScopes.Add(new UserPlantScope { UserId = s.Admin, PlantId = S.Plant }); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.True((await svc.FinanceReviewAsync(new FinanceReviewCommand(id, S.AdminAndFinance(s.Admin), true, null, null))).Succeeded);
    }

    [Fact]
    public async Task Finance_queue_lists_open_cancellations_with_per_caller_review_capability()
    {
        var (ctx, s, svc, _, id) = await CancelledAsync("PENDING_EXTERNAL");
        var list = await svc.ListForFinanceAsync(S.Finance(s.Finance1), null, null, null);
        Assert.Equal(1, list.PendingCount); var row = Assert.Single(list.Items);
        Assert.Equal(id, row.CancellationId); Assert.True(row.CanReview); Assert.Equal("PENDING_EXTERNAL", row.EcfStatus); Assert.Equal("Comprador Dono", row.EcfDeclaredByName); Assert.Equal("PRODUCTIVE SYSTEMS", row.SupplierName);
        var other = await svc.ListForFinanceAsync(S.Finance(s.FinanceOtherPlant), null, null, null);
        Assert.Empty(other.Items);   // request scope: plant 9 user does not see plant 1 requests
        var admin = await svc.ListForFinanceAsync(S.Admin(s.Admin), null, null, null);
        Assert.False(Assert.Single(admin.Items).CanReview); Assert.Contains("administrador", Assert.Single(admin.Items).ReviewBlockReason);
        Assert.Empty((await svc.ListForFinanceAsync(S.Finance(s.Finance1), "CONFIRMED", null, null)).Items);
        Assert.Single((await svc.ListForFinanceAsync(S.Finance(s.Finance1), "ALL", S.Plant, "2026/364")).Items);
    }

    // ═══ D13 resend ═══

    [Fact]
    public async Task Resend_is_rate_limited_uses_a_new_correlation_and_records_the_recipient_outcome()
    {
        var (ctx, s, svc, events, id) = await CancelledAsync();
        var soon = await svc.ResendNotificationAsync(id, S.Buyer(s.Owner));
        Assert.Equal(409, soon.Status); Assert.Equal(C.ErrorCodes.ResendTooSoon, soon.Code);
        var c = await ctx.RequestPoGroupCancellations.SingleAsync(x => x.Id == id); var firstCorrelation = c.LastNotificationCorrelationId; c.LastNotifiedAtUtc = DateTime.UtcNow.AddMinutes(-10); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.Equal(403, (await svc.ResendNotificationAsync(id, S.Buyer(s.OtherBuyer))).Status);
        var dto = Payload<PoGroupCancellationDto>(await svc.ResendNotificationAsync(id, S.Buyer(s.Owner)));
        Assert.Equal(1, dto.NotificationResendCount); Assert.NotEqual(firstCorrelation, dto.LastNotificationCorrelationId);
        var e = Assert.Single(events); Assert.Equal("NOTIFICATION_RESENT", e.ActionTaken); Assert.Equal(dto.LastNotificationCorrelationId, e.CorrelationId);
        Assert.Contains(dto.Events, x => x.EventType == "NOTIFICATION_RESENT");
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == C.HistoryActions.NotificationResent));
    }

    [Fact]
    public async Task No_Finance_recipient_is_visible_on_the_result_and_read_model()
    {
        var (ctx, s, svc, _) = await ArrangeAsync(financeCount: 0);
        var r = Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner))));
        Assert.Equal(0, r.FinanceRecipientsResolvedCount); Assert.Contains("Cancelamentos a confirmar", r.NotificationWarning); Assert.Contains("Reenviar notificação", r.NotificationWarning);
        var dto = await svc.GetAsync(s.GroupA, S.Buyer(s.Owner));
        Assert.NotNull(dto!.NotificationWarning); Assert.True(dto.Actions.CanResendNotification);
    }

    [Fact]
    public async Task Delivery_state_of_the_last_emission_is_exposed_and_a_dead_letter_produces_an_actionable_warning()
    {
        var (ctx, s, svc, _) = await ArrangeAsync();
        var r = Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner))));
        Assert.Null(r.NotificationWarning); Assert.Equal(2, r.FinanceRecipientsResolvedCount);
        var c = await ctx.RequestPoGroupCancellations.AsNoTracking().SingleAsync(x => x.Id == r.CancellationId);
        Assert.Equal("QUEUED", c.FinanceEmailPolicyOutcome); Assert.Equal(2, c.FinanceEmailsQueuedCount); Assert.True(c.BuyerEmailQueued);
        // the outbox worker's later facts (simulated): one SENT, one DEAD_LETTER on the last correlation
        ctx.EmailOutbox.AddRange(
            new EmailOutboxEntry { RecipientEmail = "fin1@test.local", Subject = "x", Status = "SENT", RequestId = s.RequestId, CorrelationId = c.LastNotificationCorrelationId, EventCode = WorkflowEventCodes.PoGroupCancelled, RetryCount = 1 },
            new EmailOutboxEntry { RecipientEmail = "fin2@test.local", Subject = "x", Status = "DEAD_LETTER", RequestId = s.RequestId, CorrelationId = c.LastNotificationCorrelationId, EventCode = WorkflowEventCodes.PoGroupCancelled, RetryCount = 3, LastError = "SMTP 554" });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var dto = (await svc.GetAsync(s.GroupA, S.Buyer(s.Owner)))!;
        Assert.Equal(2, dto.Delivery.Count); Assert.Contains(dto.Delivery, d => d.RecipientEmail == "fin2@test.local" && d.Status == "DEAD_LETTER" && d.LastError == "SMTP 554");
        Assert.Contains("fin2@test.local (DEAD_LETTER)", dto.NotificationWarning); Assert.Contains("Reenviar notificação", dto.NotificationWarning); Assert.Contains("Logs do Sistema", dto.NotificationWarning);
        Assert.True(dto.Actions.CanResendNotification);
        var finance = Assert.Single((await svc.ListForFinanceAsync(S.Finance(s.Finance1), null, null, null)).Items);
        Assert.Equal("QUOTED".Replace("QUOTED", "QUEUED"), finance.FinanceEmailPolicyOutcome);
    }

    // ═══ Multiple cycles + replacement block (D4) ═══

    [Fact]
    public async Task Replacement_block_follows_the_line_across_cycles_until_each_prior_cancellation_is_confirmed()
    {
        var (ctx, s, svc, _, first) = await CancelledAsync();
        var guard = new PriorEcfConfirmationGuard(ctx);
        // new batch #2 approved for line 1 → new group C
        var batch2 = Guid.NewGuid(); var groupC = Guid.NewGuid();
        ctx.ApprovalBatches.Add(new ApprovalBatch { Id = batch2, RequestId = s.RequestId, BatchNumber = 2, Status = "APPROVED", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Owner });
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = groupC, RequestId = s.RequestId, SupplierId = S.SupplierA, SupplierNameSnapshot = "PRODUCTIVE SYSTEMS", CurrencyCode = "RND", PlantId = S.Plant, TotalAmount = 24000m, Status = "WAITING_PO", ApprovalBatchId = batch2, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Owner });
        ctx.ApprovalBatchItems.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch2, RequestLineItemId = s.Line1, SelectedQuotationItemId = s.Qi1Alt, CreatedAtUtc = DateTime.UtcNow });
        var line = await ctx.RequestLineItems.SingleAsync(l => l.Id == s.Line1); line.RequestPoGroupId = groupC; line.QuotationLifecycleStatus = "QUOTATION_APPROVED"; line.SelectedQuotationItemId = s.Qi1Alt;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        var block = await guard.EvaluateForGroupAsync(groupC, lockRows: false);
        Assert.True(block.Blocked); var b = Assert.Single(block.Cancellations); Assert.Equal(first, b.CancellationId); Assert.Equal("ECF10 2026/364", b.PreviousPoNumber); Assert.Equal(new[] { 1 }, b.LineNumbers);
        Assert.Contains("ECF10 2026/364", block.Message);
        // sibling group B (line 2) is never blocked
        Assert.False((await guard.EvaluateForGroupAsync(s.GroupB, lockRows: false)).Blocked);

        // rejection keeps the block; confirmation lifts it
        Payload<PoGroupCancellationDto>(await svc.FinanceReviewAsync(new FinanceReviewCommand(first, S.Finance(s.Finance1), false, "Ainda ativa no Primavera, por favor anular.", null)));
        Assert.True((await guard.EvaluateForGroupAsync(groupC, lockRows: false)).Blocked);
        Payload<PoGroupCancellationDto>(await svc.FinanceReviewAsync(new FinanceReviewCommand(first, S.Finance(s.Finance1), true, null, null)));
        Assert.False((await guard.EvaluateForGroupAsync(groupC, lockRows: false)).Blocked);

        // second cycle: register a P.O. on C then cancel C again → a NEW block from the second cancellation only
        var gC = await ctx.RequestPoGroups.SingleAsync(g => g.Id == groupC); gC.Status = "PO_ISSUED"; gC.PurchaseOrderNumber = "ECF10 2026/400"; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var second = Payload<PoGroupCancellationResultDto>(await svc.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), groupId: groupC, ecfStatus: "PENDING_EXTERNAL", ecfReference: null)));
        var block2 = await guard.EvaluateForGroupAsync(groupC, lockRows: false);   // the line has no group now → evaluate by line
        var byLine = await guard.EvaluateForLineItemsAsync(new[] { s.Line1 }, lockRows: false);
        Assert.False(block2.Blocked); Assert.True(byLine.Blocked); Assert.Equal(second.CancellationId, Assert.Single(byLine.Cancellations).CancellationId);
        Assert.Equal(2, await ctx.RequestPoGroupCancellationItems.CountAsync(i => i.RequestLineItemId == s.Line1));
    }

    [Fact]
    public async Task Released_items_become_reuse_authorization_required_for_the_eligibility_service()
    {
        var (ctx, s, svc, _, _) = await CancelledAsync();
        var map = await new QuotationItemEligibilityService(ctx).GetEligibilityMapAsync(s.RequestId);
        Assert.Equal(QuotationItemEligibilityReasons.ReuseAuthorizationRequired, map[s.Qi1].ReasonCode);      // former winner
        Assert.Equal(QuotationItemEligibilityReasons.ReuseAuthorizationRequired, map[s.Qi1Alt].ReasonCode);   // former candidate
        Assert.Equal(s.Batch, map[s.Qi1].SourceCancelledBatchId);
        Assert.Equal(QuotationItemEligibilityReasons.EligibleNormal, map[s.Qi2].ReasonCode);                  // sibling line's item untouched
    }

    [Fact]
    public async Task Read_model_separates_declaration_and_confirmation_and_exposes_actions_per_caller()
    {
        var (ctx, s, svc, _, id) = await CancelledAsync();
        var buyerView = await svc.GetAsync(s.GroupA, S.Buyer(s.Owner));
        Assert.NotNull(buyerView); Assert.True(buyerView!.Actions.CanUpdateDeclaration); Assert.True(buyerView.Actions.CanResendNotification); Assert.False(buyerView.Actions.CanFinanceReview);
        Assert.Contains("administrador", buyerView.Actions.FinanceReviewBlockReason ?? "", StringComparison.OrdinalIgnoreCase);
        var financeView = await svc.GetAsync(s.GroupA, S.Finance(s.Finance1));
        Assert.True(financeView!.Actions.CanFinanceReview); Assert.False(financeView.Actions.CanUpdateDeclaration);
        Assert.Equal("Comprador Dono", financeView.EcfDeclaredByName); Assert.Null(financeView.FinanceConfirmedByName);
        Assert.Null(await svc.GetAsync(s.GroupB, S.Buyer(s.Owner)));
    }
}
