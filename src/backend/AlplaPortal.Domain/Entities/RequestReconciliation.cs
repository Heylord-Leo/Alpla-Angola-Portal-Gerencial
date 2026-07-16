namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Tracks reconciliation attempts for a request's Buy-to-Pay lifecycle.
/// 1:N relationship — a single request may require multiple reconciliation iterations
/// (e.g., supplier issues, partial delivery, invoice differences).
/// Only one reconciliation should be active (IN_PROGRESS) at a time per request.
/// </summary>
public class RequestReconciliation
{
    public int Id { get; set; }

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public Guid? RequestPoGroupId { get; set; }
    public RequestPoGroup? RequestPoGroup { get; set; }

    // ── Sequencing ──
    /// <summary>
    /// Sequential version number (1, 2, 3...) for multiple reconciliation attempts.
    /// </summary>
    public int ReconciliationSequence { get; set; } = 1;

    /// <summary>
    /// Lifecycle status: DRAFT, IN_PROGRESS, COMPLETED, CANCELLED, SUPERSEDED.
    /// </summary>
    public string ReconciliationStatus { get; set; } = ReconciliationStatuses.Draft;

    // ── Invoice & Delivery Amounts ──
    public decimal? FinalInvoiceAmount { get; set; }
    public decimal? FinalAcceptedAmount { get; set; }
    public decimal? DeliveredAcceptedAmount { get; set; }
    public string? CurrencyCode { get; set; }

    // ── Difference Tracking ──
    /// <summary>
    /// Calculated: FinalInvoiceAmount - sum(ActualPaidAmount from RequestPayments).
    /// </summary>
    public decimal? DifferenceAmount { get; set; }
    public string? DifferenceReason { get; set; }
    /// <summary>
    /// Balance still owed after reconciliation.
    /// </summary>
    public decimal? RemainingBalanceAmount { get; set; }

    // ── Credit Note ──
    public bool CreditNoteRequired { get; set; }
    public string? CreditNoteNumber { get; set; }
    public Guid? CreditNoteAttachmentId { get; set; }
    public RequestAttachment? CreditNoteAttachment { get; set; }

    // ── Debit Note ──
    public bool DebitNoteRequired { get; set; }
    public string? DebitNoteNumber { get; set; }
    public Guid? DebitNoteAttachmentId { get; set; }
    public RequestAttachment? DebitNoteAttachment { get; set; }

    // ── Supplier Refund ──
    public bool RefundRequired { get; set; }
    public decimal? RefundAmount { get; set; }
    /// <summary>
    /// Refund status: PENDING, RECEIVED, WAIVED.
    /// </summary>
    public string? RefundStatus { get; set; }

    // ── Compensation ──
    public bool CompensationFuturePayment { get; set; }
    public string? CompensationNotes { get; set; }

    // ── Decision & Outcome ──
    /// <summary>
    /// Reconciliation outcome code: NO_DIFFERENCE, BALANCE_DUE, PARTIAL_DELIVERY,
    /// INVOICE_HIGHER, INVOICE_LOWER, SUPPLIER_ISSUE.
    /// </summary>
    public string? ReconciliationDecision { get; set; }
    public string? ReconciliationNotes { get; set; }

    // ── Lifecycle Actors ──
    public Guid? StartedByUserId { get; set; }
    public User? StartedByUser { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public User? CompletedByUser { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    // ── Audit ──
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // ── Constants ──
    public static class ReconciliationStatuses
    {
        public const string Draft = "DRAFT";
        public const string InProgress = "IN_PROGRESS";
        public const string Completed = "COMPLETED";
        public const string Cancelled = "CANCELLED";
        public const string Superseded = "SUPERSEDED";
    }

    public static class ReconciliationDecisions
    {
        public const string NoDifference = "NO_DIFFERENCE";
        public const string BalanceDue = "BALANCE_DUE";
        public const string PartialDelivery = "PARTIAL_DELIVERY";
        public const string InvoiceHigher = "INVOICE_HIGHER";
        public const string InvoiceLower = "INVOICE_LOWER";
        public const string SupplierIssue = "SUPPLIER_ISSUE";
    }

    public static class RefundStatuses
    {
        public const string Pending = "PENDING";
        public const string Received = "RECEIVED";
        public const string Waived = "WAIVED";
    }
}
