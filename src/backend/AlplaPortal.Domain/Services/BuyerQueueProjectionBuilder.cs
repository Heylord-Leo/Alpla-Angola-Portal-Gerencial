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
    public sealed record QuotationItemInput(Guid Id, Guid? MappedRequestLineItemId, string? ReconciliationStatus);
    public sealed record BatchItemInput(Guid RequestLineItemId, Guid? SelectedQuotationItemId, IReadOnlyList<Guid> CandidateQuotationItemIds);
    public sealed record BatchInput(Guid Id, int BatchNumber, string Status, IReadOnlyList<BatchItemInput> Items);
    public sealed record ItemInput(Guid Id, bool IsDeleted, string? QuotationLifecycleStatus, string? LineItemStatusCode, bool HasSupplier);
    public sealed record RequestInput(
        Guid RequestId, string RequestNumber, string? Title,
        string RequestTypeCode, string RequestStatusCode, bool IsCancelled,
        Guid? BuyerId, string? NeedLevelCode, DateTime? NeedByDateUtc, DateTime CreatedAtUtc,
        bool RequestHasSupplier, bool HasProformaOrQuotationAttachment,
        IReadOnlyList<ItemInput> Items, IReadOnlyList<BatchInput> Batches,
        IReadOnlyList<QuotationItemInput> QuotationItems,
        IReadOnlyCollection<Guid> SupersededBatchIds);

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
        var pastBuyerPhase = r.RequestTypeCode == RequestConstants.Types.Quotation
            && !BuyerActiveRequestStatuses.Contains(r.RequestStatusCode);

        if (notBuyerPhase) state = BuyerQueueConstants.OperationalStates.NoBuyerAction;
        else if (terminal || pastBuyerPhase) state = BuyerQueueConstants.OperationalStates.CompletedForBuyer;
        else if (anyAdjustment) state = BuyerQueueConstants.OperationalStates.AdjustmentRequired;
        else if (anyInApproval) state = BuyerQueueConstants.OperationalStates.AwaitingApproval;
        else if (notQProposed > 0) state = BuyerQueueConstants.OperationalStates.AwaitingRequesterDecision;
        else if (pending == 0 && ready > 0) state = BuyerQueueConstants.OperationalStates.ReadyForApproval;
        else if (pending > 0 && (ready > 0 || approved > 0 || inBatch > 0 || closed > 0 || notQAccepted > 0))
            state = BuyerQueueConstants.OperationalStates.PartialCoverage;
        else if (pending > 0) state = BuyerQueueConstants.OperationalStates.NeedsQuotation;
        else state = BuyerQueueConstants.OperationalStates.CompletedForBuyer;

        var nextActions = NextActions(state, ready, pending);

        // ── Attention signals ──
        var signals = new List<BuyerAttentionSignal>();
        if (anyAdjustment) signals.Add(new(BuyerQueueConstants.AttentionCodes.AdjustmentRequired, BuyerQueueConstants.AttentionSeverities.Blocking));
        if (deadline == BuyerQueueConstants.DeadlineConditions.Overdue) signals.Add(new(BuyerQueueConstants.AttentionCodes.Overdue, BuyerQueueConstants.AttentionSeverities.UrgentDeadline));
        else if (deadline == BuyerQueueConstants.DeadlineConditions.DueToday) signals.Add(new(BuyerQueueConstants.AttentionCodes.DueToday, BuyerQueueConstants.AttentionSeverities.UrgentDeadline));
        var hasSuperseded = r.SupersededBatchIds.Count > 0;
        if (hasSuperseded) signals.Add(new(BuyerQueueConstants.AttentionCodes.SupersededBatch, BuyerQueueConstants.AttentionSeverities.Warning));
        var actionable = nextActions.Any(a => a.Actionable);
        if (ownership == BuyerQueueConstants.OwnershipStates.Unassigned && actionable
            && deadline is BuyerQueueConstants.DeadlineConditions.Overdue or BuyerQueueConstants.DeadlineConditions.DueToday or BuyerQueueConstants.DeadlineConditions.Approaching)
            signals.Add(new(BuyerQueueConstants.AttentionCodes.UnassignedNearDeadline, BuyerQueueConstants.AttentionSeverities.Warning));

        // ── Priority band (only for buyer-active states; completed/no-action never Band 1) ──
        var isBuyerActive = state is not (BuyerQueueConstants.OperationalStates.CompletedForBuyer or BuyerQueueConstants.OperationalStates.NoBuyerAction);
        var band = isBuyerActive && (state == BuyerQueueConstants.OperationalStates.AdjustmentRequired
                    || deadline == BuyerQueueConstants.DeadlineConditions.Overdue)
            ? BuyerQueueConstants.PriorityBands.ExceptionOrOverdue
            : BuyerQueueConstants.PriorityBands.Standard;

        var requiresAttention = isBuyerActive &&
            (state == BuyerQueueConstants.OperationalStates.AdjustmentRequired
             || deadline == BuyerQueueConstants.DeadlineConditions.Overdue
             || hasSuperseded);

        return new BuyerQueueProjection(
            state, BuyerQueueConstants.OperationalStates.Label(state),
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
            .SelectMany(bi => (bi.SelectedQuotationItemId.HasValue ? new[] { bi.SelectedQuotationItemId.Value } : Array.Empty<Guid>())
                .Concat(bi.CandidateQuotationItemIds))
            .ToHashSet();

        bool HasSelectableCandidate(Guid itemId) => r.QuotationItems.Any(qi =>
            qi.MappedRequestLineItemId == itemId
            && (qi.ReconciliationStatus == RequestConstants.ReconciliationStatuses.Mapped
                || qi.ReconciliationStatus == RequestConstants.ReconciliationStatuses.Substitute)
            && !heldQuotationItemIds.Contains(qi.Id));

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
            RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed => BuyerQueueConstants.CoverageBuckets.NotQuotedProposed,
            RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted => BuyerQueueConstants.CoverageBuckets.NotQuotedAccepted,
            // null or QUOTATION_PENDING: eligible pool.
            _ => hasSelectableCandidate(it.Id)
                ? BuyerQueueConstants.CoverageBuckets.QuotedReadyForBatch
                : BuyerQueueConstants.CoverageBuckets.PendingQuotation,
        };
    }

    private static IReadOnlyList<BuyerNextAction> NextActions(string state, int ready, int pending)
    {
        switch (state)
        {
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
            case BuyerQueueConstants.OperationalStates.AwaitingApproval:
                return new[] { new BuyerNextAction(BuyerQueueConstants.ActionCodes.None, "Aguardando aprovação", false) };
            case BuyerQueueConstants.OperationalStates.AwaitingRequesterDecision:
                return new[] { new BuyerNextAction(BuyerQueueConstants.ActionCodes.None, "Aguardando decisão do requisitante", false) };
            default:
                return Array.Empty<BuyerNextAction>();
        }
    }
}
