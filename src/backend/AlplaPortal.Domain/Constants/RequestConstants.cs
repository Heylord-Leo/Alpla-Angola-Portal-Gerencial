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
        public const string FinalApproved = "FINAL_APPROVED";
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
    }
}
