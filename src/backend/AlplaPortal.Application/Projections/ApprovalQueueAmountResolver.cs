namespace AlplaPortal.Application.Projections;

/// <summary>
/// Centralized, pure resolver for the single authoritative "actionable amount" shown on each
/// Approval Center queue card (and summed into the queue total). It exists so the card amount, the
/// queue-total KPI and every sort/filter use one monetary rule instead of scattered per-status
/// guesses. No DB / no EF — the caller projects the raw ingredients and this resolves them.
///
/// Key principle: a missing/unresolved amount is returned as <c>null</c>, never silently coerced to
/// 0. A genuine zero (e.g. a zero-value payment) is returned as <c>0m</c> and stays distinguishable.
/// </summary>
public static class ApprovalQueueAmountResolver
{
    /// <summary>Rounding tolerance (currency minor unit) below which snapshot/sum differences are
    /// treated as rounding rather than a real inconsistency.</summary>
    private const decimal MonetaryTolerance = 0.01m;

    public static class Sources
    {
        /// <summary>PAYMENT request — the approved payment amount (Request.EstimatedTotalAmount).</summary>
        public const string PaymentAmount = "PAYMENT_AMOUNT";
        /// <summary>QUOTATION — immutable approved snapshot on the active batch.</summary>
        public const string BatchSnapshot = "BATCH_SNAPSHOT";
        /// <summary>QUOTATION — sum of the active batch items' selected-quotation line totals.</summary>
        public const string BatchItemSum = "BATCH_ITEM_SUM";
        /// <summary>QUOTATION — request-level approved snapshot (whole-request final approval).</summary>
        public const string RequestSnapshot = "REQUEST_SNAPSHOT";
        /// <summary>QUOTATION — legacy single selected-quotation document total (no batch).</summary>
        public const string SelectedQuotation = "SELECTED_QUOTATION";
        /// <summary>Other request types — the request estimate.</summary>
        public const string Estimate = "ESTIMATE";
        /// <summary>No authoritative amount could be resolved — render a "not defined" state, not 0.</summary>
        public const string Unresolved = "UNRESOLVED";
    }

    public readonly record struct Resolution(decimal? Amount, string Source, bool HasInconsistency, int? LotNumber);

    /// <param name="requestTypeCode">e.g. "QUOTATION" / "PAYMENT".</param>
    /// <param name="hasActiveBatch">Whether a batch matching the current approval stage exists.</param>
    /// <param name="activeBatchNumber">That batch's number (for the lot label), if any.</param>
    /// <param name="activeBatchSnapshot">That batch's ApprovedTotalAmount (null before final approval).</param>
    /// <param name="activeBatchItemSum">Sum of that batch's items' selected-quotation line totals
    /// (null when there is no active batch; 0 when the batch has items summing to zero).</param>
    /// <param name="activeBatchItemCount">Number of items in that batch.</param>
    /// <param name="hasSelectedQuotation">Whether the request has a legacy SelectedQuotationId.</param>
    /// <param name="selectedQuotationTotal">That quotation's document total, if resolvable.</param>
    /// <param name="requestApprovedTotal">Request-level ApprovedTotalAmount snapshot, if any.</param>
    /// <param name="requestEstimate">Request.EstimatedTotalAmount (the payment amount for PAYMENT).</param>
    public static Resolution Resolve(
        string? requestTypeCode,
        bool hasActiveBatch,
        int? activeBatchNumber,
        decimal? activeBatchSnapshot,
        decimal? activeBatchItemSum,
        int activeBatchItemCount,
        bool hasSelectedQuotation,
        decimal? selectedQuotationTotal,
        decimal? requestApprovedTotal,
        decimal requestEstimate)
    {
        // PAYMENT: the approved payment amount is authoritative (genuine zero preserved).
        if (string.Equals(requestTypeCode, "PAYMENT", StringComparison.OrdinalIgnoreCase))
            return new Resolution(requestEstimate, Sources.PaymentAmount, false, null);

        if (string.Equals(requestTypeCode, "QUOTATION", StringComparison.OrdinalIgnoreCase))
        {
            // The current actionable lot dominates — never the request estimate, never the full
            // quotation document total when only part of it belongs to this batch.
            if (hasActiveBatch)
            {
                if (activeBatchSnapshot.HasValue && activeBatchSnapshot.Value > 0)
                {
                    var inconsistent = activeBatchItemSum.HasValue
                        && Math.Abs(activeBatchSnapshot.Value - activeBatchItemSum.Value) > MonetaryTolerance;
                    return new Resolution(activeBatchSnapshot.Value, Sources.BatchSnapshot, inconsistent, activeBatchNumber);
                }

                if (activeBatchItemCount > 0 && activeBatchItemSum.HasValue)
                    return new Resolution(activeBatchItemSum.Value, Sources.BatchItemSum, false, activeBatchNumber);

                // Batch exists but its winners cannot be valued — unresolved, not zero.
                return new Resolution(null, Sources.Unresolved, false, activeBatchNumber);
            }

            // No active batch: whole-request snapshot, then legacy single-quotation, else unresolved.
            if (requestApprovedTotal.HasValue && requestApprovedTotal.Value > 0)
                return new Resolution(requestApprovedTotal.Value, Sources.RequestSnapshot, false, null);

            if (hasSelectedQuotation && selectedQuotationTotal.HasValue)
                return new Resolution(selectedQuotationTotal.Value, Sources.SelectedQuotation, false, null);

            return new Resolution(null, Sources.Unresolved, false, null);
        }

        // Any other request type: fall back to the request estimate (genuine zero preserved).
        return new Resolution(requestEstimate, Sources.Estimate, false, null);
    }

    /// <summary>
    /// The queue-total rule, shared so the "Valor total em fila" KPI and any parity check use the
    /// exact same summation the cards imply: sum the resolved actionable amounts, excluding
    /// unresolved (null) ones. Genuine zeros contribute 0 and do not change the total. The frontend
    /// must mirror this (sum of ActionableAmount where != null) so cards and summary can never
    /// diverge.
    /// </summary>
    public static decimal SumActionable(IEnumerable<decimal?> amounts)
        => amounts.Where(a => a.HasValue).Sum(a => a!.Value);
}
