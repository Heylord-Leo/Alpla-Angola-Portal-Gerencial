namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Canonical event codes for workflow status transitions.
/// Used by <see cref="Events.WorkflowEvent"/> and the notification orchestrator
/// to map business transitions to notification actions.
/// </summary>
public static class WorkflowEventCodes
{
    // --- Approval Flow ---
    public const string RequestSubmitted = "REQUEST_SUBMITTED";
    public const string SubmissionConfirmed = "SUBMISSION_CONFIRMED";
    public const string AreaApproved = "AREA_APPROVED";
    public const string AreaRejected = "AREA_REJECTED";
    public const string FinalApproved = "FINAL_APPROVED";
    public const string FinalRejected = "FINAL_REJECTED";
    public const string AreaAdjustment = "AREA_ADJUSTMENT";
    public const string FinalAdjustment = "FINAL_ADJUSTMENT";

    // --- Operational Flow ---
    public const string PoRegistered = "PO_REGISTERED";
    public const string PaymentScheduled = "PAYMENT_SCHEDULED";
    public const string PaymentCompleted = "PAYMENT_COMPLETED";
    public const string FinanceReturned = "FINANCE_RETURNED";

    // --- Lifecycle ---
    public const string RequestCancelled = "REQUEST_CANCELLED";
    public const string RequestFinalized = "REQUEST_FINALIZED";

    // --- Quotation (migrated from inline) ---
    public const string QuotationCompleted = "QUOTATION_COMPLETED";
}
