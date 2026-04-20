namespace AlplaPortal.Domain.Constants;

public static class ContractConstants
{
    public static class Statuses
    {
        public const string Draft = "DRAFT";

        /// <summary>Legacy status — pre-DEC-118 contracts submitted for review.
        /// These are not affected by the two-step approval endpoints.
        /// Admin can force-activate via POST /{id}/activate with a mandatory comment.</summary>
        public const string UnderReview = "UNDER_REVIEW";

        /// <summary>Stage 1: awaiting technical validation by the department area approver (DEC-118).</summary>
        public const string UnderTechnicalReview = "UNDER_TECHNICAL_REVIEW";

        /// <summary>Stage 2: awaiting final legal/formal approval by the company final approver (DEC-118).</summary>
        public const string UnderFinalReview = "UNDER_FINAL_REVIEW";

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

        // ── Two-step approval events (DEC-118) ───────────────────────────────────────
        /// <summary>Contract submitted from DRAFT → UNDER_TECHNICAL_REVIEW.</summary>
        public const string SubmittedForTechnicalReview = "SUBMITTED_FOR_TECHNICAL_REVIEW";

        /// <summary>Technical approver approved Stage 1 → UNDER_FINAL_REVIEW.</summary>
        public const string TechnicalApproved = "TECHNICAL_APPROVED";

        /// <summary>Technical approver returned the contract → DRAFT (mandatory comment).</summary>
        public const string TechnicalReturned = "TECHNICAL_RETURNED";

        /// <summary>Final approver approved Stage 2 → ACTIVE.</summary>
        public const string FinalApproved = "FINAL_APPROVED";

        /// <summary>Final approver returned the contract → DRAFT (mandatory comment).</summary>
        public const string FinalReturned = "FINAL_RETURNED";

        /// <summary>Administrator force-activated a contract from any in-review state.
        /// Always requires a comment. This is a named, auditable event — not a silent bypass.</summary>
        public const string AdminForceActivated = "ADMIN_FORCE_ACTIVATED";
    }

    public static class AlertTypes
    {
        public const string ExpiryWarning = "EXPIRY_WARNING";
        public const string RenewalNotice = "RENEWAL_NOTICE";
    }

    /// <summary>
    /// Defines how the payment due date is calculated for obligations of this contract.
    /// </summary>
    public static class PaymentTermTypes
    {
        /// <summary>Due date = reference date + PaymentTermDays. Requires a valid reference event.</summary>
        public const string FixedDaysAfterReference = "FIXED_DAYS_AFTER_REFERENCE";

        /// <summary>
        /// Due date = a specific fixed day within a month.
        /// Interpretation: if reference date is on/before the fixed day in the current month → due date = fixed day this month.
        /// If reference date is after the fixed day → due date = fixed day next month.
        /// Example: fixed day = 10, reference = April 3 → due date = April 10. Reference = April 12 → due date = May 10.
        /// </summary>
        public const string FixedDayOfMonth = "FIXED_DAY_OF_MONTH";

        /// <summary>Due date = PaymentFixedDay of the month following the reference date month.</summary>
        public const string NextMonthFixedDay = "NEXT_MONTH_FIXED_DAY";

        /// <summary>Due date = reference date (immediate payment on the reference event).</summary>
        public const string OnReceipt = "ON_RECEIPT";

        /// <summary>
        /// Payment is made in advance. No auto-calculation is performed.
        /// The user must inform the due date manually at obligation level.
        /// The UI must clearly communicate this to avoid confusion.
        /// </summary>
        public const string AdvancePayment = "ADVANCE_PAYMENT";

        /// <summary>No auto-calculation. User must manually enter due date for each obligation.</summary>
        public const string Manual = "MANUAL";

        /// <summary>
        /// The contract contains a complex or non-standard rule that cannot be modeled by the current engine.
        /// PaymentRuleSummary must be filled manually. Due dates must be set manually per obligation.
        /// </summary>
        public const string CustomText = "CUSTOM_TEXT";
    }

    /// <summary>
    /// Defines the business event used as the starting reference for due date calculation.
    /// Only the first four are surfaced in the UI (v1). Others are defined for future use.
    /// </summary>
    public static class ReferenceEventTypes
    {
        /// <summary>Reference date = the date the contract was signed (Contract.SignedAtUtc). Surfaced in UI: Yes.</summary>
        public const string ContractSignDate = "CONTRACT_SIGN_DATE";

        /// <summary>Reference date = the date the obligation record is created. Surfaced in UI: Yes.</summary>
        public const string ObligationCreationDate = "OBLIGATION_CREATION_DATE";

        /// <summary>Reference date = the date the invoice was received (must be provided by user). Surfaced in UI: Yes.</summary>
        public const string InvoiceReceivedDate = "INVOICE_RECEIVED_DATE";

        /// <summary>Reference date = provided manually by the user per obligation. Surfaced in UI: Yes.</summary>
        public const string ManualReferenceDate = "MANUAL_REFERENCE_DATE";

        /// <summary>Reference date = service acceptance date. Defined for future use.</summary>
        public const string ServiceAcceptanceDate = "SERVICE_ACCEPTANCE_DATE";

        /// <summary>Reference date = delivery date. Defined for future use.</summary>
        public const string DeliveryDate = "DELIVERY_DATE";
    }

    /// <summary>
    /// Tracks whether the effective due date on an obligation was auto-calculated or manually overridden.
    /// </summary>
    public static class DueDateSources
    {
        /// <summary>Due date was computed automatically from the contract's payment rule.</summary>
        public const string AutoFromContract = "AUTO_FROM_CONTRACT";

        /// <summary>Due date was manually set or overridden by the user.</summary>
        public const string ManualOverride = "MANUAL_OVERRIDE";
    }

    /// <summary>
    /// Types of late-payment penalty or interest applied when a payment is overdue.
    /// Monetary calculation is out of scope for this phase — these codes only support tracking.
    /// </summary>
    public static class LatePenaltyTypes
    {
        /// <summary>Penalty expressed as a percentage of the outstanding amount.</summary>
        public const string Percentage = "PERCENTAGE";

        /// <summary>Penalty expressed as a fixed monetary amount.</summary>
        public const string FixedAmount = "FIXED_AMOUNT";
    }
}

