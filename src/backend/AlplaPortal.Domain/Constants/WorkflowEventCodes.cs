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
    public const string PoCorrectionCompleted = "PO_CORRECTION_COMPLETED";

    // --- Lifecycle ---
    public const string RequestCancelled = "REQUEST_CANCELLED";
    public const string RequestFinalized = "REQUEST_FINALIZED";

    // --- Quotation (migrated from inline) ---
    public const string QuotationCompleted = "QUOTATION_COMPLETED";
    public const string QuotationItemAwarded = "QUOTATION_ITEM_AWARDED";
    public const string QuotationResubmitted = "QUOTATION_RESUBMITTED";

    // --- Quotation Buyer Notifications ---
    public const string QuotationAwaitingBuyer = "QUOTATION_AWAITING_BUYER";
    public const string BuyerAssigned = "BUYER_ASSIGNED";

    // --- Buy-to-Pay (Advance Payment / Reconciliation) ---
    public const string PaymentConditionDefined = "PAYMENT_CONDITION_DEFINED";
    public const string AdvancePaymentRequired = "ADVANCE_PAYMENT_REQUIRED";
    public const string AdvancePaymentScheduled = "ADVANCE_PAYMENT_SCHEDULED";
    public const string AdvancePaymentCompleted = "ADVANCE_PAYMENT_COMPLETED";
    public const string WaitingSupplierDelivery = "WAITING_SUPPLIER_DELIVERY";
    public const string ReceivingPending = "RECEIVING_PENDING";
    public const string DeliveryConfirmed = "DELIVERY_CONFIRMED";
    public const string ReconciliationStarted = "RECONCILIATION_STARTED";
    public const string ReconciliationCompleted = "RECONCILIATION_COMPLETED";
    public const string FinalBalanceRequired = "FINAL_BALANCE_REQUIRED";
    public const string FinalBalanceScheduled = "FINAL_BALANCE_SCHEDULED";
    public const string FinalBalanceCompleted = "FINAL_BALANCE_COMPLETED";
    public const string CreditDebitNoteRequired = "CREDIT_DEBIT_NOTE_REQUIRED";
}
