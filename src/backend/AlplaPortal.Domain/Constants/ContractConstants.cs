namespace AlplaPortal.Domain.Constants;

public static class ContractConstants
{
    public static class Statuses
    {
        public const string Draft = "DRAFT";
        public const string UnderReview = "UNDER_REVIEW";
        public const string Active = "ACTIVE";
        public const string Suspended = "SUSPENDED";
        public const string Expired = "EXPIRED";
        public const string Terminated = "TERMINATED";
    }

    public static class ObligationStatuses
    {
        public const string Pending = "PENDING";
        public const string RequestCreated = "REQUEST_CREATED";
        public const string Paid = "PAID";
        public const string Cancelled = "CANCELLED";
    }

    public static class DocumentTypes
    {
        public const string Original = "ORIGINAL";
        public const string Amendment = "AMENDMENT";
        public const string Addendum = "ADDENDUM";
        public const string Annex = "ANNEX";
        public const string Other = "OTHER";
    }

    public static class HistoryEventTypes
    {
        public const string Created = "CREATED";
        public const string StatusChanged = "STATUS_CHANGED";
        public const string DocumentUploaded = "DOCUMENT_UPLOADED";
        public const string ObligationAdded = "OBLIGATION_ADDED";
        public const string OcrExtracted = "OCR_EXTRACTED";
        public const string FieldUpdated = "FIELD_UPDATED";
        public const string RequestLinked = "REQUEST_LINKED";
        public const string Renewed = "RENEWED";
    }

    public static class AlertTypes
    {
        public const string ExpiryWarning = "EXPIRY_WARNING";
        public const string RenewalNotice = "RENEWAL_NOTICE";
    }
}
