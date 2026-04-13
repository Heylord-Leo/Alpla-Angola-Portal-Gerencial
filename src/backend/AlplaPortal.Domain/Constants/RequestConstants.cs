namespace AlplaPortal.Domain.Constants;

public static class RequestConstants
{
    public static class Types
    {
        public const string Quotation = "QUOTATION";
        public const string Payment = "PAYMENT";
    }

    public static class Statuses
    {
        public const string Draft = "DRAFT";
        public const string WaitingQuotation = "WAITING_QUOTATION";
        public const string WaitingAreaApproval = "WAITING_AREA_APPROVAL";
        public const string WaitingFinalApproval = "WAITING_FINAL_APPROVAL";
        public const string FinalApproved = "APPROVED";
        public const string Rejected = "REJECTED";
        public const string Cancelled = "CANCELLED";
        public const string QuotationCompleted = "QUOTATION_COMPLETED";
        public const string PoIssued = "PO_ISSUED";
        public const string PaymentRequestSent = "PAYMENT_REQUEST_SENT";
        public const string PaymentScheduled = "PAYMENT_SCHEDULED";
        public const string Paid = "PAID";
        public const string AreaAdjustment = "AREA_ADJUSTMENT";
        public const string FinalAdjustment = "FINAL_ADJUSTMENT";
        public const string PaymentCompleted = "PAYMENT_COMPLETED";
        public const string InFollowup = "IN_FOLLOWUP";
        public const string WaitingCostCenter = "WAITING_COST_CENTER";
        public const string WaitingPoCorrection = "WAITING_PO_CORRECTION";
    }

    /// <summary>
    /// Financial Integrity Gate configuration.
    /// Controls the tolerance used to compare OCR-extracted totals against
    /// system-calculated quotation totals at the quotation completion stage.
    /// Tolerance = max(AbsoluteFloor, OcrTotal × RelativeThreshold).
    /// </summary>
    public static class FinancialIntegrity
    {
        /// <summary>
        /// Absolute minimum tolerance in currency units.
        /// Prevents blocking for sub-unit rounding noise.
        /// </summary>
        public const decimal AbsoluteFloor = 1.00m;

        /// <summary>
        /// Relative tolerance as a fraction of the OCR original total.
        /// 0.001 = 0.1% — catches real mismatches while tolerating
        /// rounding differences between JavaScript and C# engines.
        /// </summary>
        public const decimal RelativeThreshold = 0.001m;

        /// <summary>
        /// Calculates the effective tolerance for a given OCR baseline amount.
        /// Returns the greater of AbsoluteFloor or the relative threshold.
        /// </summary>
        public static decimal CalculateTolerance(decimal ocrOriginalTotal)
        {
            var relativeTolerance = Math.Abs(ocrOriginalTotal) * RelativeThreshold;
            return Math.Max(AbsoluteFloor, relativeTolerance);
        }
    }
}
