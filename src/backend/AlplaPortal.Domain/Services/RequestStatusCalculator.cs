using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Diagnostic issue codes a status calculation can surface without persisting anything.
/// Callers (persistence wrappers) decide how to log/react.
/// </summary>
public enum RequestStatusIssueCode
{
    /// <summary>
    /// One or more RequestPoGroup rows belonging to an APPROVED ApprovalBatch are still PENDING
    /// after the quotation approval lifecycle completed. PENDING should have been converted to
    /// WAITING_PO at final approval — this indicates incomplete activation or a partially
    /// committed transaction. The calculator preserves the current status rather than guessing.
    /// </summary>
    UnexpectedPendingPoGroups
}

/// <summary>
/// Result of an aggregate Request.Status calculation. Never null StatusCode.
/// IssueCode/AffectedPoGroupIds are populated only when the calculation detected a
/// data inconsistency it deliberately did not resolve on its own.
/// </summary>
public sealed record RequestStatusCalculationResult(
    string StatusCode,
    RequestStatusIssueCode? IssueCode = null,
    IReadOnlyList<Guid>? AffectedPoGroupIds = null)
{
    public static RequestStatusCalculationResult Clean(string statusCode) => new(statusCode);
}

/// <summary>
/// Pure, static, side-effect-free calculator for the legacy Request.StatusId compatibility field.
/// No ILogger, no persistence, no lazy loading — operates only on request.ApprovalBatches,
/// request.LineItems, and request.PoGroups exactly as loaded by the caller.
///
/// Phase 1 (batch/item authority): while any ApprovalBatch is in an in-progress approval state,
/// or any active line item is pending quotation / awaiting not-quoted resolution, Request.Status
/// is derived from batches and items — unchanged from the original single-status workflow.
///
/// Phase 2 (PO-group authority): once every batch is settled (APPROVED or REJECTED), at least one
/// batch is APPROVED, and no active line item remains pending, Request.Status is derived from the
/// RequestPoGroups belonging to APPROVED batches (or with no batch at all, for the legacy/PAYMENT
/// path). Groups from REJECTED batches and CANCELLED groups never participate.
/// </summary>
public static class RequestStatusCalculator
{
    // Lower number = earlier in the operational lifecycle = "furthest behind" when aggregating
    // across multiple active RequestPoGroups. Only reached once PENDING/WAITING_PO/
    // WAITING_PO_CORRECTION groups have already been handled by the earlier, more specific checks.
    private static readonly Dictionary<string, int> GroupStatusPriority = new()
    {
        [RequestConstants.PoGroupStatuses.AdvancePaymentRequired] = 24,
        [RequestConstants.PoGroupStatuses.AdvancePaymentScheduled] = 25,
        [RequestConstants.PoGroupStatuses.AdvancePaymentCompleted] = 26,
        [RequestConstants.PoGroupStatuses.WaitingSupplierDelivery] = 27,
        [RequestConstants.PoGroupStatuses.PoIssued] = 30,
        [RequestConstants.PoGroupStatuses.PaymentRequestSent] = 40,
        [RequestConstants.PoGroupStatuses.PaymentScheduled] = 50,
        [RequestConstants.PoGroupStatuses.PaymentCompleted] = 60,
        [RequestConstants.PoGroupStatuses.WaitingReceipt] = 70,
        [RequestConstants.PoGroupStatuses.WaitingReconciliation] = 80,
        [RequestConstants.PoGroupStatuses.InFollowup] = 90,
        // Phase 4C: the fiscal-receipt antechamber sits between every operational stage and
        // COMPLETED. 95 — NOT 80, which WAITING_RECONCILIATION already holds (the collision the
        // Release 1 audit warned about): a group waiting only for its Recibo Fiscal is further
        // along than any receiving/reconciliation stage, and only completion beats it.
        [RequestConstants.PoGroupStatuses.WaitingFiscalReceipt] = 95,
        [RequestConstants.PoGroupStatuses.Completed] = 100,
    };

    private static readonly string[] NotYetIssued =
    {
        RequestConstants.PoGroupStatuses.WaitingPo,
        RequestConstants.PoGroupStatuses.WaitingPoCorrection
    };

    public static RequestStatusCalculationResult DetermineAggregateRequestStatus(Request request)
    {
        var currentStatusCode = request.Status?.Code ?? RequestConstants.Statuses.WaitingQuotation;
        var activeItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        var allGroups = request.PoGroups.ToList();

        // v2.230.0 — two multi-group corrections (REQ-23/07/2026-140 regression class):
        //
        // (a) Batches that are CANCELLED, or SUPERSEDED (still in an approval state but every
        //     item already processed by another active operational unit — see
        //     SupersededBatchPolicy), no longer represent live approval work and are excluded
        //     from Phase 1 entirely. An abandoned AREA_ADJUSTMENT batch must not keep pulling
        //     the request back to WAITING_AREA_APPROVAL forever.
        //
        // (b) PO-gate floor: once ANY active group crossed the PO gate (PO_ISSUED, the
        //     advance-payment track, payment/receiving states…), Phase 1 may no longer return a
        //     pre-PO approval status at all — the persisted aggregate is computed from the
        //     groups (Phase 2), and in-flight approval of REMAINING items surfaces through the
        //     display projection (MIXED_PROCESSING), never by regressing the scalar.
        var batches = SupersededBatchPolicy.ConsideredBatches(request.ApprovalBatches, activeItems, allGroups);
        var poGateCrossed = SupersededBatchPolicy.AnyActiveGroupCrossedPoGate(allGroups);

        // "Came from the batch workflow" keys off the ORIGINAL batch list: a request whose only
        // batch is superseded/cancelled still ran the batch workflow, so its all-WAITING_PO
        // groups must report PO_REQUESTED (active Buyer stage), not preserve a stale approval
        // status. Only genuinely batchless flows (PAYMENT single-group) keep inline semantics.
        var cameFromBatchWorkflow = request.ApprovalBatches.Count > 0;

        if (poGateCrossed)
            return DeterminePoGroupPhaseStatus(request, currentStatusCode, batches, cameFromBatchWorkflow);

        if (batches.Count == 0)
        {
            // No ApprovalBatch model applies to this request at all — either a QUOTATION
            // request that hasn't started the batch workflow yet (nothing to aggregate,
            // preserve status), or a batchless group workflow (e.g. the single auto-created
            // PO group on a PAYMENT-type request) that already has PoGroups to aggregate over.
            // In the latter case, skip Phase 1 entirely and go straight to Phase 2 below —
            // every such group has ApprovalBatchId == null, so it is eligible regardless.
            if (!request.PoGroups.Any())
                return RequestStatusCalculationResult.Clean(currentStatusCode);
        }
        else
        {
            var hasPendingItems = activeItems.Any(li =>
                string.IsNullOrWhiteSpace(li.QuotationLifecycleStatus) ||
                li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending);

            var hasNotQuotedProposed = activeItems.Any(li =>
                li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed);

            // Priority: pending/not-quoted-proposed items keep the request in WAITING_QUOTATION
            // so the buyer can continue inserting quotations for them.
            if (hasPendingItems || hasNotQuotedProposed)
                return RequestStatusCalculationResult.Clean(RequestConstants.Statuses.WaitingQuotation);

            var hasFinalApproval = batches.Any(b =>
                b.Status == RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval ||
                b.Status == RequestConstants.ApprovalBatchStatuses.FinalAdjustment);
            if (hasFinalApproval)
                return RequestStatusCalculationResult.Clean(RequestConstants.Statuses.WaitingFinalApproval);

            var hasAreaApproval = batches.Any(b =>
                b.Status == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval ||
                b.Status == RequestConstants.ApprovalBatchStatuses.AreaAdjustment);
            if (hasAreaApproval)
                return RequestStatusCalculationResult.Clean(RequestConstants.Statuses.WaitingAreaApproval);

            var hasApprovedBatch = batches.Any(b => b.Status == RequestConstants.ApprovalBatchStatuses.Approved);
            var allSettled = batches.All(b =>
                b.Status == RequestConstants.ApprovalBatchStatuses.Approved ||
                b.Status == RequestConstants.ApprovalBatchStatuses.Rejected);

            // All batches settled but none approved (e.g. every batch rejected) — nothing was
            // actually approved, so do not report QUOTATION_COMPLETED. Preserve the current status;
            // whether an all-rejected quotation request should auto-close is a separate product
            // decision, not something this calculator guesses at.
            if (!allSettled || !hasApprovedBatch)
                return RequestStatusCalculationResult.Clean(currentStatusCode);

            // No PO groups exist at all for this request even though batches are fully
            // settled/approved (legacy path, or the PO-group workflow never ran) — nothing to
            // aggregate over; the settled-batches/no-pending-items state itself means
            // quotation selection is done.
            if (request.PoGroups.Count == 0)
                return RequestStatusCalculationResult.Clean(RequestConstants.Statuses.QuotationCompleted);
        }

        // ── Phase 2: PO-group authority ──
        // Reached either because batches.Count == 0 with PoGroups already present (batchless
        // group workflow — e.g. a PAYMENT-type request's single auto-created group), or because
        // Phase 1 fully settled with at least one approved batch and at least one PO group.
        return DeterminePoGroupPhaseStatus(request, currentStatusCode, batches, cameFromBatchWorkflow);
    }

    private static RequestStatusCalculationResult DeterminePoGroupPhaseStatus(
        Request request,
        string currentStatusCode,
        IReadOnlyList<ApprovalBatch> batches,
        bool cameFromBatchPhase)
    {
        var approvedBatchIds = batches
            .Where(b => b.Status == RequestConstants.ApprovalBatchStatuses.Approved)
            .Select(b => b.Id)
            .ToHashSet();

        var eligibleGroups = request.PoGroups
            .Where(g =>
                g.Status != RequestConstants.PoGroupStatuses.Cancelled &&
                (g.ApprovalBatchId == null || approvedBatchIds.Contains(g.ApprovalBatchId.Value)))
            .ToList();

        // Conservative: an eligible group that is still PENDING after the approval lifecycle
        // completed indicates incomplete activation. Do not exclude it and let healthy siblings
        // drive the result — that would make the stuck group invisible to the Buyer while the
        // request keeps moving. Preserve the current status entirely and surface the issue.
        var pendingUnexpected = eligibleGroups
            .Where(g => g.Status == RequestConstants.PoGroupStatuses.Pending)
            .ToList();
        if (pendingUnexpected.Count > 0)
        {
            return new RequestStatusCalculationResult(
                currentStatusCode,
                RequestStatusIssueCode.UnexpectedPendingPoGroups,
                pendingUnexpected.Select(g => g.Id).ToList());
        }

        // All eligible groups were CANCELLED (or none matched an approved batch) — nothing
        // active to aggregate over. Preserve the current status rather than guessing.
        if (eligibleGroups.Count == 0)
            return RequestStatusCalculationResult.Clean(currentStatusCode);

        if (eligibleGroups.All(g => g.Status == RequestConstants.PoGroupStatuses.WaitingPo))
        {
            // v2.229.1 (REQ-17/08/2026-232): zero of N required P.O.s registered is an ACTIVE
            // Buyer stage, not a finished one — reporting QUOTATION_COMPLETED here presented a
            // request waiting for its first P.O. as done and hid it from the Buyer's status
            // filters. The batch-driven path now reports PO_REQUESTED ("Aguardando P.O.");
            // QUOTATION_COMPLETED keeps only its zero-PO-group meaning above. A batchless group
            // workflow (e.g. a PAYMENT-type request's single group) reaching WAITING_PO is
            // handled inline by RegisterPo, not by this aggregator — preserve its status.
            return cameFromBatchPhase
                ? RequestStatusCalculationResult.Clean(RequestConstants.Statuses.PoRequested)
                : RequestStatusCalculationResult.Clean(currentStatusCode);
        }

        if (eligibleGroups.All(g => NotYetIssued.Contains(g.Status)) &&
            eligibleGroups.Any(g => g.Status == RequestConstants.PoGroupStatuses.WaitingPoCorrection))
            return RequestStatusCalculationResult.Clean(RequestConstants.Statuses.WaitingPoCorrection);

        if (eligibleGroups.Any(g => NotYetIssued.Contains(g.Status)) &&
            eligibleGroups.Any(g => !NotYetIssued.Contains(g.Status)))
            return RequestStatusCalculationResult.Clean(RequestConstants.Statuses.PoPartiallyUploaded);

        // Every eligible group has progressed at least to PO_ISSUED (or an advance-payment
        // sub-state). The parent request reflects whichever group is furthest behind —
        // PoGroupStatuses codes at this point are string-identical to their RequestConstants.Statuses
        // counterparts, so no separate mapping table is needed.
        var furthestBehind = eligibleGroups
            .OrderBy(g => GroupStatusPriority.TryGetValue(g.Status, out var p) ? p : int.MaxValue)
            .First();

        return RequestStatusCalculationResult.Clean(furthestBehind.Status);
    }
}
