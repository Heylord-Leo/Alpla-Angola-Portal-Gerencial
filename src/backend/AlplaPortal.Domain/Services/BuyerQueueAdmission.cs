using System;
using System.Linq;
using System.Linq.Expressions;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.249.2 — the ONE SQL admission rule for "requests the Buyer may still have quotation work on", shared by every
/// surface that hydrates requests for <see cref="BuyerQueueProjectionBuilder"/>: the Buyer queue (list + summary), the
/// Dashboard V2 Buyer section / workload, the personal "Minha Operação" actions, the canonical near-deadline alerts and
/// the operational pipeline. Before this release each site carried its own copy of the buyer-active status list, so a
/// request whose scalar is already past the P.O. gate because a SIBLING group was issued — but which has a line
/// RELEASED to quotation by a P.O.-group cancellation (REQ-28/10/2026-628) — reached the Workspace only by URL.
///
/// Admission is a BOUND, never a classification: the pure projection still decides the operational state and the
/// hidden-by-default states are dropped after projection exactly as before. Legacy pre-batch requests never carry a
/// QUOTATION_PENDING line past the gate (the release writes it), so they are not admitted.
/// </summary>
public static class BuyerQueueAdmission
{
    /// <summary>Request-level statuses where the Buyer still owns the quotation phase (unchanged list).</summary>
    public static readonly string[] BuyerActiveRequestStatusCodes =
    {
        RequestConstants.Statuses.Draft, RequestConstants.Statuses.WaitingQuotation,
        RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.AreaAdjustment,
        RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.FinalAdjustment,
    };

    /// <summary>
    /// EF-translatable: buyer-active scalar OR open quotation work on a batch-model request (a non-deleted line in
    /// <c>QUOTATION_PENDING</c> while the request has at least one approval batch). Apply with a separate <c>Where</c>.
    /// </summary>
    public static readonly Expression<Func<Request, bool>> IsBuyerActiveOrHasOpenQuotationWork = r =>
        BuyerActiveRequestStatusCodes.Contains(r.Status.Code)
        || (r.ApprovalBatches.Any()
            && r.LineItems.Any(li => !li.IsDeleted
                && li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending));

    /// <summary>
    /// Buyer queue variant: the above OR a P.O. group returned by Finance (WAITING_PO_CORRECTION, cross-type —
    /// v2.242.0). Kept as one expression because EF cannot OR two <c>Where</c> calls.
    /// </summary>
    public static readonly Expression<Func<Request, bool>> IsBuyerActiveOrHasOpenQuotationWorkOrPoCorrection = r =>
        BuyerActiveRequestStatusCodes.Contains(r.Status.Code)
        || r.PoGroups.Any(g => g.Status == RequestConstants.PoGroupStatuses.WaitingPoCorrection)
        || (r.ApprovalBatches.Any()
            && r.LineItems.Any(li => !li.IsDeleted
                && li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending));

    /// <summary>In-memory twin of <see cref="IsBuyerActiveOrHasOpenQuotationWork"/> for loaded aggregates (tests, diagnostics).</summary>
    public static bool HasOpenQuotationWork(Request r) =>
        r.ApprovalBatches.Count > 0
        && r.LineItems.Any(li => !li.IsDeleted && li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending);
}
