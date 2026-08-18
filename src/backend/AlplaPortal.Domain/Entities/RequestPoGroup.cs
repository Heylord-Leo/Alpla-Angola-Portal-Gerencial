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

    /// <summary>
    /// The plant this group serves. Part of the grouping key from Release 3: one supplier billing
    /// two plants produces two groups, because the obligations are tracked and received separately.
    /// Null on groups created before the multi-document feature.
    /// </summary>
    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

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

    // Dimension 2: Operation Invoice (the Factura documenting the actual supply)
    /// <summary>
    /// IDENTITY of the document that originated this group — see
    /// RequestConstants.SourceDocumentTypes. Null means never classified.
    /// Obligations are derived from it by DocumentObligationResolver, never stored in this field.
    /// </summary>
    public string? SourceDocumentType { get; set; }

    /// <summary>
    /// Operation-invoice obligation state. Defaults to UNCLASSIFIED: a group whose document
    /// identity is unknown must never be silently treated as "no invoice required" (rule R12).
    /// </summary>
    public string OperationInvoiceStatus { get; set; } = RequestConstants.OperationInvoiceStatuses.Unclassified;

    // ── Derived obligations (recomputed from SourceDocumentType; persisted for querying) ──
    /// <summary>A Factura for the actual supply is still owed.</summary>
    public bool RequiresOperationInvoice { get; set; }

    /// <summary>
    /// A separate payment receipt is owed. False for a Factura-Recibo, which already documents
    /// payment — demanding a further receipt there would be legally wrong.
    /// </summary>
    public bool RequiresSeparateFiscalReceipt { get; set; }

    /// <summary>
    /// Advance regularization is owed (Factura de Adiantamento): operation invoice, Credit Note and
    /// payment evidence. Discharged through the existing Buy-to-Pay RequestReconciliation flow.
    /// </summary>
    public bool RequiresAdvanceRegularization { get; set; }

    /// <summary>Finance must confirm or correct the classification before completion.</summary>
    public bool RequiresFinanceClassificationReview { get; set; }

    // ── Cumulative coverage (Release 3) ──
    //
    // The single-document columns that used to live here (FinalInvoiceAttachmentId and friends) are
    // gone. A group may be covered by MANY operation invoices, and one invoice may cover MANY
    // groups, so a "primary attachment" pointer beside a 1:N set would invite exactly the bug this
    // model exists to prevent. Coverage lives in OperationInvoiceAllocation.

    /// <summary>
    /// The commercial value this group must eventually be invoiced for. Captured ONCE when the
    /// obligation is activated, not re-derived: recomputing it later would drift whenever a
    /// quotation or line item is edited, silently moving the finish line.
    ///
    /// <para>Always the value of what was ORDERED — never what was paid. That is what makes an
    /// advance behave correctly without a special rule.</para>
    /// </summary>
    public decimal? ExpectedOperationInvoiceTotal { get; set; }
    public string? ExpectedOperationInvoiceCurrency { get; set; }

    /// <summary>Set only when Finance established the expected total by hand, with a reason.</summary>
    public Guid? ExpectedTotalSetByUserId { get; set; }
    public DateTime? ExpectedTotalSetAtUtc { get; set; }
    public string? ExpectedTotalJustification { get; set; }

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
