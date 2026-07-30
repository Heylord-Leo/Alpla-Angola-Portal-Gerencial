using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Entities;

public class RequestPoGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string? SupplierNameSnapshot { get; set; }
    public string? SupplierNifSnapshot { get; set; }

    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public string? CurrencyCode { get; set; }

    public decimal TotalAmount { get; set; }

    public string? PaymentConditionCode { get; set; }
    public decimal? AdvancePaymentPercent { get; set; }

    public string Status { get; set; } = "PENDING";

    /// <summary>
    /// The approval batch that generated this PO group.
    /// Null for legacy groups created before the batch model, and for PAYMENT requests.
    /// </summary>
    public Guid? ApprovalBatchId { get; set; }
    public ApprovalBatch? ApprovalBatch { get; set; }

    public string? PurchaseOrderNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    // ── Concurrency (Post-Payment Completion Workflow — Release 1 foundation) ──
    /// <summary>
    /// SQL Server rowversion. Guards the parallel post-payment dimensions: Operational Receipt,
    /// Final Invoice and Fiscal Receipt can be written by different users at the same time.
    /// Written by the database only — never assigned by application code.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // ── Post-Payment Completion Dimensions (Release 1: schema foundation only) ──
    // All three dimensions are INDEPENDENT fields, not sequential statuses. No code writes
    // them while PostPaymentCompletion.Enabled is false; they are activated in Releases 3–4.

    // Dimension 1: Operational Receipt
    /// <summary>Stamped when every item of this group has been operationally received.</summary>
    public DateTime? OperationalReceiptCompletedAtUtc { get; set; }
    public Guid? OperationalReceiptCompletedByUserId { get; set; }

    // Dimension 2: Final Invoice
    /// <summary>
    /// Billing document that originated this group: PROFORMA | FINAL_INVOICE | null.
    /// Null means the group was never classified — see <see cref="FinalInvoiceStatus"/>.
    /// </summary>
    public string? BillingDocumentType { get; set; }

    /// <summary>
    /// Final Invoice obligation state. Defaults to UNCLASSIFIED: a group whose billing document
    /// type is unknown must never be silently treated as "no invoice required" (rule R12).
    /// </summary>
    public string FinalInvoiceStatus { get; set; } = RequestConstants.FinalInvoiceStatuses.Unclassified;

    public Guid? FinalInvoiceAttachmentId { get; set; }
    public DateTime? FinalInvoiceUploadedAtUtc { get; set; }
    public Guid? FinalInvoiceUploadedByUserId { get; set; }
    public DateTime? FinalInvoiceValidatedAtUtc { get; set; }
    public Guid? FinalInvoiceValidatedByUserId { get; set; }
    public string? FinalInvoiceRejectionReason { get; set; }

    // Dimension 3: Fiscal Receipt — terminal document, and the stable group-completion identity
    /// <summary>
    /// The Fiscal Receipt attachment that closes this group. Also the deduplication identity of
    /// GROUP_COMPLETED (GC:{GroupId}:{FiscalReceiptAttachmentId}) — a group can never complete
    /// without it, which is what makes that history key stable across retries.
    /// </summary>
    public Guid? FiscalReceiptAttachmentId { get; set; }
    public DateTime? FiscalReceiptUploadedAtUtc { get; set; }
    public Guid? FiscalReceiptUploadedByUserId { get; set; }

    // ── Completion stamp ──
    /// <summary>UTC moment this group was marked COMPLETED by the completion service.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    // Navigation properties
    public ICollection<RequestLineItem> LineItems { get; set; } = new List<RequestLineItem>();
    public ICollection<RequestAttachment> PoAttachments { get; set; } = new List<RequestAttachment>();
    public ICollection<RequestPayment> Payments { get; set; } = new List<RequestPayment>();
    public ICollection<RequestReconciliation> Reconciliations { get; set; } = new List<RequestReconciliation>();
}
