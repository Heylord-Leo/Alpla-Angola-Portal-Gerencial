using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure, static policy answering two questions the multi-group workflow needs everywhere
/// (aggregate status calculation, the workflow projection, UI diagnostics):
///
/// 1. Has an operational unit crossed the PO gate? Once a RequestPoGroup has a registered
///    P.O. (or entered the advance-payment track), its lifecycle is monotonic — approval-side
///    states may never drag the parent request back behind it (the REQ-23/07/2026-140
///    regression: an abandoned AREA_ADJUSTMENT batch pulled a PO_ISSUED request back to
///    WAITING_AREA_APPROVAL and a re-approval overwrote it with APPROVED).
///
/// 2. Is a batch superseded? A batch still sitting in an in-approval state whose items were
///    ALL already processed by another operational unit (an active PO group that does not
///    belong to this batch) no longer represents live work: it must not drive aggregation,
///    responsibilities or next actions. It is never hidden from history and is surfaced as a
///    diagnostics warning instead. Detection only — nothing here mutates the batch.
/// </summary>
public static class SupersededBatchPolicy
{
    /// <summary>
    /// Group statuses at or beyond the PO gate: the group's P.O. is registered, or the group
    /// entered the advance-payment/delivery track that precedes formal issuance. PENDING,
    /// WAITING_PO and WAITING_PO_CORRECTION are the only active pre-gate statuses; CANCELLED
    /// is out of the lifecycle entirely.
    /// </summary>
    private static readonly HashSet<string> PoGateCrossedStatuses = new(StringComparer.Ordinal)
    {
        RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
        RequestConstants.PoGroupStatuses.AdvancePaymentScheduled,
        RequestConstants.PoGroupStatuses.AdvancePaymentCompleted,
        RequestConstants.PoGroupStatuses.WaitingSupplierDelivery,
        RequestConstants.PoGroupStatuses.PoIssued,
        RequestConstants.PoGroupStatuses.PaymentRequestSent,
        RequestConstants.PoGroupStatuses.PaymentScheduled,
        RequestConstants.PoGroupStatuses.PaymentCompleted,
        RequestConstants.PoGroupStatuses.WaitingReceipt,
        RequestConstants.PoGroupStatuses.WaitingReconciliation,
        RequestConstants.PoGroupStatuses.WaitingFiscalReceipt,
        RequestConstants.PoGroupStatuses.InFollowup,
        RequestConstants.PoGroupStatuses.Completed,
    };

    /// <summary>Batch statuses that still represent an in-flight approval.</summary>
    private static readonly HashSet<string> InApprovalBatchStatuses = new(StringComparer.Ordinal)
    {
        RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
        RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
        RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
        RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
    };

    public static bool HasCrossedPoGate(RequestPoGroup group) =>
        PoGateCrossedStatuses.Contains(group.Status);

    /// <summary>Any active (non-cancelled) group of the request already crossed the PO gate.</summary>
    public static bool AnyActiveGroupCrossedPoGate(IEnumerable<RequestPoGroup> groups) =>
        groups.Any(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled && HasCrossedPoGate(g));

    public static bool IsInApproval(ApprovalBatch batch) => InApprovalBatchStatuses.Contains(batch.Status);

    /// <summary>
    /// A batch is superseded when it is still in an approval state AND it has items AND every
    /// one of its items maps to an active (non-deleted) line item that is already attached to
    /// an active (non-cancelled) PO group NOT created by this batch. Requires batch.Items to
    /// be loaded; a batch with no loaded/existing items is never classified superseded
    /// (fail-open to "live" — misclassifying live work as stale is the dangerous direction).
    /// </summary>
    public static bool IsSuperseded(
        ApprovalBatch batch,
        IReadOnlyCollection<RequestLineItem> lineItems,
        IReadOnlyCollection<RequestPoGroup> poGroups)
    {
        if (!IsInApproval(batch)) return false;
        if (batch.Items == null || batch.Items.Count == 0) return false;

        var activeGroupsById = poGroups
            .Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled)
            .ToDictionary(g => g.Id);

        foreach (var batchItem in batch.Items)
        {
            var lineItem = lineItems.FirstOrDefault(li => li.Id == batchItem.RequestLineItemId && !li.IsDeleted);
            if (lineItem == null) return false;
            if (lineItem.RequestPoGroupId == null) return false;
            if (!activeGroupsById.TryGetValue(lineItem.RequestPoGroupId.Value, out var coveringGroup)) return false;
            if (coveringGroup.ApprovalBatchId == batch.Id) return false;
        }

        return true;
    }

    /// <summary>
    /// The batches that still participate in workflow aggregation / active-unit projection:
    /// excludes CANCELLED batches and superseded ones. REJECTED and APPROVED batches remain —
    /// they are settled and the calculator's Phase-1/Phase-2 handling depends on seeing them.
    /// </summary>
    public static List<ApprovalBatch> ConsideredBatches(
        IEnumerable<ApprovalBatch> batches,
        IReadOnlyCollection<RequestLineItem> lineItems,
        IReadOnlyCollection<RequestPoGroup> poGroups) =>
        batches
            .Where(b => b.Status != RequestConstants.ApprovalBatchStatuses.Cancelled)
            .Where(b => !IsSuperseded(b, lineItems, poGroups))
            .ToList();
}
