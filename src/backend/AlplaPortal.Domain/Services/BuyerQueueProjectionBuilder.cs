using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure, static, side-effect-free canonical Buyer-queue projection (Finance
/// FinanceObligationProjectionBuilder doctrine). Turns one loaded QUOTATION Request into the Buyer's
/// operational interpretation — operational state, next action(s), coverage, priority, deadline,
/// attention signals, ownership and cancel capability — so the frontend never re-derives the Buyer
/// workflow. It reuses persisted state only; it is NOT a second workflow engine and never mutates.
///
/// Coverage/eligibility rules are the exact server counterparts of buyerItemStatus.ts /
/// batchEligibility.ts; batch-state → Buyer-action mapping is consistent with
/// RequestWorkflowProjectionBuilder (AREA/FINAL_ADJUSTMENT → Comprador). See
/// docs/BUYER_QUEUE_CANONICAL_MODEL.md.
///
/// MAINTENANCE TRIGGER: changes to Buyer operational state, coverage, priority, or cancel eligibility
/// MUST be validated against the Buyer DEV Regression Harness (ZZTEST-BUY-*) —
/// docs/BUYER_DEV_REGRESSION_HARNESS.md.
/// </summary>
public static class BuyerQueueProjectionBuilder
{
    // Batch statuses that HOLD their items/quotation candidates (mirror batchEligibility.ts).
    private static readonly string[] ActiveOrApprovedBatchStatuses =
    {
        RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
        RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
        RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
        RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
        RequestConstants.ApprovalBatchStatuses.Approved,
    };

    private static readonly string[] AdjustmentBatchStatuses =
        { RequestConstants.ApprovalBatchStatuses.AreaAdjustment, RequestConstants.ApprovalBatchStatuses.FinalAdjustment };

    private static readonly string[] InApprovalBatchStatuses =
        { RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval };

    // Request-level statuses where the Buyer still owns the quotation phase.
    private static readonly string[] BuyerActiveRequestStatuses =
    {
        RequestConstants.Statuses.Draft, RequestConstants.Statuses.WaitingQuotation,
        RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.AreaAdjustment,
        RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.FinalAdjustment,
    };

    // ── Inputs (populated by the controller from loaded entities) ──
    // v2.249.2 — IsReuseBlocked: the item was a candidate/winner of a CANCELLED batch item or of a RELEASED item of an
    // APPROVED batch and has no active reuse authorization (QuotationReuseEligibilityPolicy, DEC-147 / D3). Such an item
    // is NOT a selectable candidate — the batch endpoint refuses it — so the line it maps to stays "Pendente de cotação".
    public sealed record QuotationItemInput(Guid Id, Guid? MappedRequestLineItemId, string? ReconciliationStatus, bool IsReuseBlocked = false);
    public sealed record BatchItemInput(Guid RequestLineItemId, Guid? SelectedQuotationItemId, IReadOnlyList<Guid> CandidateQuotationItemIds,
        // Phase 1 P.O. cancellation: a released item of an APPROVED batch no longer holds its line or candidates.
        bool IsReleased = false);
    public sealed record BatchInput(Guid Id, int BatchNumber, string Status, IReadOnlyList<BatchItemInput> Items);
    public sealed record ItemInput(Guid Id, bool IsDeleted, string? QuotationLifecycleStatus, string? LineItemStatusCode, bool HasSupplier);
    public sealed record RequestInput(
        Guid RequestId, string RequestNumber, string? Title,
        string RequestTypeCode, string RequestStatusCode, bool IsCancelled,
        Guid? BuyerId, string? NeedLevelCode, DateTime? NeedByDateUtc, DateTime CreatedAtUtc,
        bool RequestHasSupplier, bool HasProformaOrQuotationAttachment,
        IReadOnlyList<ItemInput> Items, IReadOnlyList<BatchInput> Batches,
        IReadOnlyList<QuotationItemInput> QuotationItems,
        IReadOnlyCollection<Guid> SupersededBatchIds,
        // v2.242.0 — true when any PO group of this request is WAITING_PO_CORRECTION (returned by
        // Finance). Group-derived; drives the PO_CORRECTION operational state.
        bool HasPoCorrection = false,
        // v2.249.2 — stored Finance return kinds of the WAITING_PO_CORRECTION groups (null entry = legacy, unclassified);
        // drives the PO_CORRECTION label and the order/wording of its next actions. Never inferred from reason text.
        IReadOnlyList<string?>? PoCorrectionReturnKinds = null,
        // v2.249.2 — any PO group in WAITING_PO (first registration pending) → PO_REGISTRATION_PENDING instead of
        // COMPLETED_FOR_BUYER when nothing else is open for the Buyer.
        bool HasPoRegistrationPending = false);

    // ── Outputs ──
    public sealed record BuyerNextAction(string Code, string Label, bool Actionable);
    public sealed record BuyerAttentionSignal(string Code, string Severity);
    public sealed record BuyerQueueProjection(
        string OperationalState, string OperationalStateLabel,
        IReadOnlyList<BuyerNextAction> NextBuyerActions,
        string CoverageStatus,
        int ActiveItemCount, int CoveredCount, int PendingCount, int QuotationCount, int ActiveBatchCount,
        IReadOnlyDictionary<string, int> CoverageCounts,
        string PriorityBand, string DeadlineCondition,
        IReadOnlyList<BuyerAttentionSignal> AttentionSignals,
        string OwnershipState, bool RequiresAttention,
        bool CanCancel, string? CancelBlockReason,
        int NeedLevelRank, int DeadlineRank);

    public static int NeedLevelRank(string? code) => code switch
    {
        RequestConstants.NeedLevels.Critico => 0,
        RequestConstants.NeedLevels.Urgente => 1,
        RequestConstants.NeedLevels.Normal => 2,
        RequestConstants.NeedLevels.Baixo => 3,
        _ => 4,
    };

    private static int DeadlineRank(string condition) => condition switch
    {
        BuyerQueueConstants.DeadlineConditions.Overdue => 0,
        BuyerQueueConstants.DeadlineConditions.DueToday => 1,
        BuyerQueueConstants.DeadlineConditions.Approaching => 2,
        BuyerQueueConstants.DeadlineConditions.WithinDeadline => 3,
        _ => 4,
    };

    public static string DeadlineConditionFor(DateTime? needByDateUtc, DateTime today)
    {
        if (!needByDateUtc.HasValue) return BuyerQueueConstants.DeadlineConditions.None;
        var due = needByDateUtc.Value.Date; var t = today.Date;
        if (due < t) return BuyerQueueConstants.DeadlineConditions.Overdue;
        if (due == t) return BuyerQueueConstants.DeadlineConditions.DueToday;
        if (due <= t.AddDays(BuyerQueueConstants.ApproachingDeadlineDays)) return BuyerQueueConstants.DeadlineConditions.Approaching;
        return BuyerQueueConstants.DeadlineConditions.WithinDeadline;
    }

    public static BuyerQueueProjection Build(RequestInput r, Guid currentUserId, DateTime today)
    {
        var ownership = r.BuyerId == null ? BuyerQueueConstants.OwnershipStates.Unassigned
            : r.BuyerId == currentUserId ? BuyerQueueConstants.OwnershipStates.Mine
            : BuyerQueueConstants.OwnershipStates.Other;

        var deadline = DeadlineConditionFor(r.NeedByDateUtc, today);

        // Cancel capability (BUYER mode) — reuse the single evaluator.
        var anyProcessed = r.Items.Any(i => !i.IsDeleted &&
            (i.HasSupplier || (i.LineItemStatusCode != null
                && !RequestCancellationEvaluator.LineItemQuotationOpenStatuses.Contains(i.LineItemStatusCode))));
        var cancel = RequestCancellationEvaluator.Evaluate(new RequestCancellationEvaluator.Input(
            r.RequestTypeCode, r.RequestStatusCode, r.IsCancelled, ActorIsBuyerMode: true,
            r.RequestHasSupplier, r.HasProformaOrQuotationAttachment, anyProcessed, HasPaymentOperationalAttachment: false));

        // ── Coverage buckets ──
        var counts = new Dictionary<string, int>
        {
            [BuyerQueueConstants.CoverageBuckets.CancelledDeleted] = 0,
            [BuyerQueueConstants.CoverageBuckets.Approved] = 0,
            [BuyerQueueConstants.CoverageBuckets.InActiveBatch] = 0,
            [BuyerQueueConstants.CoverageBuckets.ClosedNotQuoted] = 0,
            [BuyerQueueConstants.CoverageBuckets.NotQuotedProposed] = 0,
            [BuyerQueueConstants.CoverageBuckets.NotQuotedAccepted] = 0,
            [BuyerQueueConstants.CoverageBuckets.QuotedReadyForBatch] = 0,
            [BuyerQueueConstants.CoverageBuckets.PendingQuotation] = 0,
        };

        var activeBatches = r.Batches
            .Where(b => ActiveOrApprovedBatchStatuses.Contains(b.Status) && !r.SupersededBatchIds.Contains(b.Id))
            .ToList();

        // Per-item coverage buckets come from the single shared classifier (also used by the Buyer
        // Workspace) so the queue counts and the Workspace item states can never diverge.
        var itemBuckets = ClassifyItemCoverage(r);
        foreach (var bucket in itemBuckets.Values) counts[bucket]++;

        var cancelledDeleted = counts[BuyerQueueConstants.CoverageBuckets.CancelledDeleted];
        var approved = counts[BuyerQueueConstants.CoverageBuckets.Approved];
        var inBatch = counts[BuyerQueueConstants.CoverageBuckets.InActiveBatch];
        var closed = counts[BuyerQueueConstants.CoverageBuckets.ClosedNotQuoted];
        var notQProposed = counts[BuyerQueueConstants.CoverageBuckets.NotQuotedProposed];
        var notQAccepted = counts[BuyerQueueConstants.CoverageBuckets.NotQuotedAccepted];
        var ready = counts[BuyerQueueConstants.CoverageBuckets.QuotedReadyForBatch];
        var pending = counts[BuyerQueueConstants.CoverageBuckets.PendingQuotation];

        var activeItemCount = r.Items.Count - cancelledDeleted;
        var covered = approved + inBatch + closed + notQAccepted + ready; // "treated" (not pending, not awaiting decision)
        var quotationCount = r.QuotationItems.Select(q => q.Id).Count() == 0 ? 0 : r.QuotationItems.Count; // items; caller may override with doc count
        var activeBatchCount = activeBatches.Count(b => b.Status != RequestConstants.ApprovalBatchStatuses.Approved);

        var anyAdjustment = activeBatches.Any(b => AdjustmentBatchStatuses.Contains(b.Status));
        var anyInApproval = activeBatches.Any(b => InApprovalBatchStatuses.Contains(b.Status));

        // ── Coverage status ──
        string coverageStatus;
        if (activeItemCount == 0) coverageStatus = BuyerQueueConstants.CoverageStatuses.FullyCovered;
        else if (notQProposed > 0) coverageStatus = BuyerQueueConstants.CoverageStatuses.AwaitingDecision;
        else if (pending == 0) coverageStatus = BuyerQueueConstants.CoverageStatuses.FullyCovered;
        else if (pending == activeItemCount) coverageStatus = BuyerQueueConstants.CoverageStatuses.NotCovered;
        else coverageStatus = BuyerQueueConstants.CoverageStatuses.PartiallyCovered;

        // ── Operational state (precedence) ──
        string state;
        var notBuyerPhase = r.RequestTypeCode != RequestConstants.Types.Quotation;
        var terminal = r.IsCancelled || r.RequestStatusCode is "CANCELLED" or "REJECTED" or "COMPLETED";
        // v2.249.1 — OPEN quotation work: lines still pending (or quoted and ready) that no active/approved
        // batch holds. Mirrors QuotationWindowPolicy (write side): such lines exist after a PARTIAL submission
        // (another batch in approval) and after a P.O.-group cancellation "voltar a cotar" while sibling
        // groups stay operational. They keep the Buyer phase alive regardless of the request scalar, which is
        // never regressed below the P.O. gate (RequestStatusCalculator). Only batch-model requests qualify:
        // legacy pre-batch requests with null lifecycles stay "completed for Buyer" exactly as before.
        // A line still HELD by an active/approved batch (non-released batch item) is never open work, whatever
        // its lifecycle says — the batch endpoints own it until it is approved, rejected, cancelled or released.
        var heldLineIds = activeBatches
            .SelectMany(b => b.Items)
            .Where(bi => !bi.IsReleased)
            .Select(bi => bi.RequestLineItemId)
            .ToHashSet();
        var openPending = itemBuckets.Count(kv => kv.Value == BuyerQueueConstants.CoverageBuckets.PendingQuotation && !heldLineIds.Contains(kv.Key));
        var openReady = itemBuckets.Count(kv => kv.Value == BuyerQueueConstants.CoverageBuckets.QuotedReadyForBatch && !heldLineIds.Contains(kv.Key));
        var openWork = r.RequestTypeCode == RequestConstants.Types.Quotation
            && r.Batches.Count > 0
            && (openPending > 0 || openReady > 0);
        var pastBuyerPhase = r.RequestTypeCode == RequestConstants.Types.Quotation
            && !BuyerActiveRequestStatuses.Contains(r.RequestStatusCode)
            && !openWork;

        // v2.242.0 — a Finance-returned PO group is actionable Buyer work that WINS over every other
        // classification, including notBuyerPhase (a PAYMENT request carries RequestTypeCode != QUOTATION
        // yet a WAITING_PO_CORRECTION group is genuine Buyer/Purchasing work) and past-buyer-phase /
        // terminal. It occurs only after the quotation phase, so it cannot conflict with the
        // active-quotation states evaluated below. This is why it is evaluated FIRST.
        // v2.249.2 — the quotation phase is over but a group still awaits its FIRST P.O.: say so instead of "Concluído".
        var completedOrRegistration = !terminal && r.HasPoRegistrationPending
            ? BuyerQueueConstants.OperationalStates.PoRegistrationPending
            : BuyerQueueConstants.OperationalStates.CompletedForBuyer;
        if (r.HasPoCorrection) state = BuyerQueueConstants.OperationalStates.PoCorrection;
        else if (notBuyerPhase) state = BuyerQueueConstants.OperationalStates.NoBuyerAction;
        else if (terminal || pastBuyerPhase) state = completedOrRegistration;
        else if (anyAdjustment) state = BuyerQueueConstants.OperationalStates.AdjustmentRequired;
        // v2.249.1 — a batch in approval is informational ONLY when nothing else is open for the Buyer: with
        // pending/ready lines outside that batch the request is PARTIAL_COVERAGE / READY_FOR_APPROVAL and
        // keeps ADD_QUOTATION / SUBMIT_BATCH (the batch itself stays locked; the write endpoints refuse its lines).
        else if (anyInApproval && !openWork) state = BuyerQueueConstants.OperationalStates.AwaitingApproval;
        else if (notQProposed > 0) state = BuyerQueueConstants.OperationalStates.AwaitingRequesterDecision;
        else if (pending == 0 && ready > 0) state = BuyerQueueConstants.OperationalStates.ReadyForApproval;
        else if (pending > 0 && (ready > 0 || approved > 0 || inBatch > 0 || closed > 0 || notQAccepted > 0))
            state = BuyerQueueConstants.OperationalStates.PartialCoverage;
        else if (pending > 0) state = BuyerQueueConstants.OperationalStates.NeedsQuotation;
        else state = completedOrRegistration;

        var nextActions = NextActions(state, ready, pending, openWork, r.PoCorrectionReturnKinds);
        var stateLabel = state == BuyerQueueConstants.OperationalStates.PoCorrection
            ? BuyerQueueConstants.OperationalStates.PoCorrectionLabel(r.PoCorrectionReturnKinds)
            : BuyerQueueConstants.OperationalStates.Label(state);

        // ── Attention signals ──
        var signals = new List<BuyerAttentionSignal>();
        if (anyAdjustment) signals.Add(new(BuyerQueueConstants.AttentionCodes.AdjustmentRequired, BuyerQueueConstants.AttentionSeverities.Blocking));
        if (r.HasPoCorrection) signals.Add(new(BuyerQueueConstants.AttentionCodes.PoCorrection, BuyerQueueConstants.AttentionSeverities.Blocking));
        if (deadline == BuyerQueueConstants.DeadlineConditions.Overdue) signals.Add(new(BuyerQueueConstants.AttentionCodes.Overdue, BuyerQueueConstants.AttentionSeverities.UrgentDeadline));
        else if (deadline == BuyerQueueConstants.DeadlineConditions.DueToday) signals.Add(new(BuyerQueueConstants.AttentionCodes.DueToday, BuyerQueueConstants.AttentionSeverities.UrgentDeadline));
        var hasSuperseded = r.SupersededBatchIds.Count > 0;
        if (hasSuperseded) signals.Add(new(BuyerQueueConstants.AttentionCodes.SupersededBatch, BuyerQueueConstants.AttentionSeverities.Warning));
        var actionable = nextActions.Any(a => a.Actionable);
        if (ownership == BuyerQueueConstants.OwnershipStates.Unassigned && actionable
            && deadline is BuyerQueueConstants.DeadlineConditions.Overdue or BuyerQueueConstants.DeadlineConditions.DueToday or BuyerQueueConstants.DeadlineConditions.Approaching)
            signals.Add(new(BuyerQueueConstants.AttentionCodes.UnassignedNearDeadline, BuyerQueueConstants.AttentionSeverities.Warning));

        // ── Priority band (only for buyer-active states; completed/no-action never Band 1) ──
        var isBuyerActive = state is not (BuyerQueueConstants.OperationalStates.CompletedForBuyer
            or BuyerQueueConstants.OperationalStates.NoBuyerAction
            or BuyerQueueConstants.OperationalStates.PoRegistrationPending);
        var band = isBuyerActive && (state == BuyerQueueConstants.OperationalStates.AdjustmentRequired
                    || state == BuyerQueueConstants.OperationalStates.PoCorrection
                    || deadline == BuyerQueueConstants.DeadlineConditions.Overdue)
            ? BuyerQueueConstants.PriorityBands.ExceptionOrOverdue
            : BuyerQueueConstants.PriorityBands.Standard;

        var requiresAttention = isBuyerActive &&
            (state == BuyerQueueConstants.OperationalStates.AdjustmentRequired
             || state == BuyerQueueConstants.OperationalStates.PoCorrection
             || deadline == BuyerQueueConstants.DeadlineConditions.Overdue
             || hasSuperseded);

        return new BuyerQueueProjection(
            state, stateLabel,
            nextActions, coverageStatus,
            activeItemCount, covered, pending, quotationCount, activeBatchCount,
            counts, band, deadline, signals, ownership, requiresAttention,
            cancel.CanCancel, cancel.BlockReason,
            NeedLevelRank(r.NeedLevelCode), DeadlineRank(deadline));
    }

    /// <summary>
    /// Per-item coverage classification (itemId → CoverageBucket) — the single shared source of the
    /// coverage taxonomy for both the queue counts and the Buyer Workspace item list. Pure.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> ClassifyItemCoverage(RequestInput r)
    {
        var activeBatches = r.Batches
            .Where(b => ActiveOrApprovedBatchStatuses.Contains(b.Status) && !r.SupersededBatchIds.Contains(b.Id))
            .ToList();
        var heldQuotationItemIds = activeBatches
            .SelectMany(b => b.Items)
            .Where(bi => !bi.IsReleased)
            .SelectMany(bi => (bi.SelectedQuotationItemId.HasValue ? new[] { bi.SelectedQuotationItemId.Value } : Array.Empty<Guid>())
                .Concat(bi.CandidateQuotationItemIds))
            .ToHashSet();

        // v2.249.2 — a reuse-blocked candidate (released/cancelled use without an active authorization) is NOT selectable:
        // CreateBatch refuses it (REUSE_AUTHORIZATION_REQUIRED), so counting it as "ready" produced a false "Pronto para
        // lote" / "Enviar itens para aprovação" with an empty submission modal (TEST, REQ-28/10/2026-628).
        bool HasSelectableCandidate(Guid itemId) => r.QuotationItems.Any(qi =>
            qi.MappedRequestLineItemId == itemId
            && (qi.ReconciliationStatus == RequestConstants.ReconciliationStatuses.Mapped
                || qi.ReconciliationStatus == RequestConstants.ReconciliationStatuses.Substitute)
            && !heldQuotationItemIds.Contains(qi.Id)
            && !qi.IsReuseBlocked);

        var result = new Dictionary<Guid, string>(r.Items.Count);
        foreach (var it in r.Items) result[it.Id] = Bucket(it, HasSelectableCandidate);
        return result;
    }

    private static string Bucket(ItemInput it, Func<Guid, bool> hasSelectableCandidate)
    {
        if (it.IsDeleted || it.LineItemStatusCode is "CANCELLED" or "DELETED")
            return BuyerQueueConstants.CoverageBuckets.CancelledDeleted;
        return it.QuotationLifecycleStatus switch
        {
            RequestConstants.QuotationLifecycleStatuses.QuotationApproved => BuyerQueueConstants.CoverageBuckets.Approved,
            RequestConstants.QuotationLifecycleStatuses.BatchAssigned => BuyerQueueConstants.CoverageBuckets.InActiveBatch,
            RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted => BuyerQueueConstants.CoverageBuckets.ClosedNotQuoted,
            // Phase 1 P.O. cancellation (CANCEL_ITEMS): closed after approval — treated like a cancelled line.
            RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval => BuyerQueueConstants.CoverageBuckets.CancelledDeleted,
            RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed => BuyerQueueConstants.CoverageBuckets.NotQuotedProposed,
            RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted => BuyerQueueConstants.CoverageBuckets.NotQuotedAccepted,
            // null or QUOTATION_PENDING: eligible pool.
            _ => hasSelectableCandidate(it.Id)
                ? BuyerQueueConstants.CoverageBuckets.QuotedReadyForBatch
                : BuyerQueueConstants.CoverageBuckets.PendingQuotation,
        };
    }

    private static IReadOnlyList<BuyerNextAction> NextActions(string state, int ready, int pending, bool openWork, IReadOnlyList<string?>? poCorrectionReturnKinds)
    {
        switch (state)
        {
            case BuyerQueueConstants.OperationalStates.PoRegistrationPending:
                // Informational: the first registration is done on the request page (Para Minha Ação "Registrar P.O.");
                // not actionable here so the personal actions / alerts / workload counters are unchanged.
                return new[] { new BuyerNextAction(BuyerQueueConstants.ActionCodes.RegisterPo, "Registar a P.O. na página do pedido", false) };
            case BuyerQueueConstants.OperationalStates.NeedsQuotation:
                return new[] { new BuyerNextAction(BuyerQueueConstants.ActionCodes.AddQuotation, "Adicionar cotação", true) };
            case BuyerQueueConstants.OperationalStates.PartialCoverage:
                var list = new List<BuyerNextAction> { new(BuyerQueueConstants.ActionCodes.AddQuotation, "Completar cotações", true) };
                if (ready > 0) list.Add(new(BuyerQueueConstants.ActionCodes.SubmitBatch, "Enviar itens cobertos para aprovação", true));
                return list;
            case BuyerQueueConstants.OperationalStates.ReadyForApproval:
                return new[] { new BuyerNextAction(BuyerQueueConstants.ActionCodes.SubmitBatch, "Enviar itens para aprovação", true) };
            case BuyerQueueConstants.OperationalStates.AdjustmentRequired:
                return new[] { new BuyerNextAction(BuyerQueueConstants.ActionCodes.ResolveAdjustment, "Revisar e reenviar lote", true) };
            case BuyerQueueConstants.OperationalStates.PoCorrection:
                // v2.249.2 — wording and order follow the STORED return kind(s). Both capabilities stay available whatever
                // the kind (the kind is advisory): correction-only keeps the historical order; cancellation-requested leads
                // with the cancellation and names Finance's advisory; legacy/unclassified or mixed stay neutral.
                var kinds = poCorrectionReturnKinds ?? Array.Empty<string?>();
                var anyCorrectionKind = kinds.Any(k => k == PoGroupCancellationConstants.FinanceReturnKinds.PoCorrection);
                var anyCancellationKind = kinds.Any(k => k == PoGroupCancellationConstants.FinanceReturnKinds.OrderCancellationRequested);
                var poCorrection = new List<BuyerNextAction>();
                if (anyCancellationKind && !anyCorrectionKind)
                {
                    poCorrection.Add(new(BuyerQueueConstants.ActionCodes.CancelPoGroup, "Cancelar P.O. (cancelamento solicitado por Finanças)", true));
                    poCorrection.Add(new(BuyerQueueConstants.ActionCodes.CorrectPo, "Corrigir P.O. (se a devolução for discutível)", true));
                }
                else if (anyCorrectionKind && !anyCancellationKind)
                {
                    poCorrection.Add(new(BuyerQueueConstants.ActionCodes.CorrectPo, "Corrigir P.O.", true));
                    // Phase 1: the Buyer may also cancel the returned group (restart quotation / close items) instead of correcting.
                    poCorrection.Add(new(BuyerQueueConstants.ActionCodes.CancelPoGroup, "Cancelar P.O.", true));
                }
                else
                {
                    poCorrection.Add(new(BuyerQueueConstants.ActionCodes.CorrectPo, "Rever a P.O. devolvida (corrigir ou cancelar)", true));
                    poCorrection.Add(new(BuyerQueueConstants.ActionCodes.CancelPoGroup, "Cancelar P.O.", true));
                }
                // v2.249.1 — lines released by an earlier cancellation (or never quoted) stay actionable while a
                // sibling group is in correction: the quotation entries are not hidden by the correction state.
                if (openWork && pending > 0) poCorrection.Add(new(BuyerQueueConstants.ActionCodes.AddQuotation, "Completar cotações", true));
                if (openWork && ready > 0) poCorrection.Add(new(BuyerQueueConstants.ActionCodes.SubmitBatch, "Enviar itens cobertos para aprovação", true));
                return poCorrection;
            case BuyerQueueConstants.OperationalStates.AwaitingApproval:
                return new[] { new BuyerNextAction(BuyerQueueConstants.ActionCodes.None, "Aguardando aprovação", false) };
            case BuyerQueueConstants.OperationalStates.AwaitingRequesterDecision:
                return new[] { new BuyerNextAction(BuyerQueueConstants.ActionCodes.None, "Aguardando decisão do requisitante", false) };
            default:
                return Array.Empty<BuyerNextAction>();
        }
    }
}
