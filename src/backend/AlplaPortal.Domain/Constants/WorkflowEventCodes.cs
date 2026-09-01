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

    // --- Adjustment V2 (Phase 3): batch-level structured adjustment cycle notifications ---
    // Distinct from the legacy request-level AREA_ADJUSTMENT/FINAL_ADJUSTMENT codes above.
    // Phase 3 emits ONLY these two (Buyer-facing); requester/resubmit/cancel events land in later phases.
    public const string BatchAreaAdjustment = "BATCH_AREA_ADJUSTMENT_REQUESTED";
    public const string BatchFinalAdjustment = "BATCH_FINAL_ADJUSTMENT_REQUESTED";
    // Phase 4: Buyer resolved the adjustment and resubmitted the lot to Area approval.
    public const string BatchResubmitted = "BATCH_RESUBMITTED_TO_AREA";

    // --- Operational Flow ---
    public const string PoRegistered = "PO_REGISTERED";
    public const string PaymentScheduled = "PAYMENT_SCHEDULED";
    public const string PaymentCompleted = "PAYMENT_COMPLETED";
    public const string FinanceReturned = "FINANCE_RETURNED";
    public const string PoCorrectionCompleted = "PO_CORRECTION_COMPLETED";
    public const string PaymentScheduleCancelled = "PAYMENT_SCHEDULE_CANCELLED";

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

    // --- Post-Payment Completion Workflow ---
    // Release 1 declares the codes so history/notification wiring has a single source of truth.
    // NO handler is registered and NO event is emitted for any of them until Releases 3–4;
    // while PostPaymentCompletion.Enabled is false they are unreachable by design.
    public const string FinalInvoiceObligationActivated = "FINAL_INVOICE_OBLIGATION_ACTIVATED";
    public const string FinalInvoiceUploaded = "FINAL_INVOICE_UPLOADED";
    public const string FinalInvoiceValidationRequired = "FINAL_INVOICE_VALIDATION_REQUIRED";
    public const string FinalInvoiceValidated = "FINAL_INVOICE_VALIDATED";
    public const string FinalInvoiceRejected = "FINAL_INVOICE_REJECTED";
    public const string FinalInvoiceReplacementRequested = "FINAL_INVOICE_REPLACEMENT_REQUESTED";
    public const string FinalInvoiceDivergenceAccepted = "FINAL_INVOICE_DIVERGENCE_ACCEPTED";
    public const string OperationalReceiptCompleted = "OPERATIONAL_RECEIPT_COMPLETED";
    public const string ReceiptCompletedInvoicePending = "RECEIPT_COMPLETED_INVOICE_PENDING";
    public const string InvoiceValidatedReceiptPending = "INVOICE_VALIDATED_RECEIPT_PENDING";
    public const string FiscalReceiptUnlocked = "FISCAL_RECEIPT_UNLOCKED";
    public const string FiscalReceiptUploaded = "FISCAL_RECEIPT_UPLOADED";
    public const string GroupCompleted = "GROUP_COMPLETED";
    public const string LegacyDocumentClassified = "LEGACY_DOCUMENT_CLASSIFIED";
}
