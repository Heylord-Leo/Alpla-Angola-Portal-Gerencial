using System;
using System.Collections.Generic;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// One document that originates a PAYMENT request.
///
/// <para><b>PAYMENT only.</b> Quotation Management keeps one document per quotation, carried on
/// <see cref="Quotation.DocumentType"/>, and nothing here reaches it.</para>
///
/// <para>A payment request may pay several invoices at once — the same supplier billing two plants,
/// a monthly service split per site. Each document keeps its own identity, OCR reading,
/// classification decision, dates, values and items, because each derives its own post-payment
/// obligations. Aggregating them into one request-level type would make a request that pays a
/// proforma AND an invoice inexpressible.</para>
///
/// <para>This is <b>not</b> an <see cref="OperationInvoice"/>. A source document initiates payment;
/// an operation invoice arrives later to discharge an obligation the source document created. They
/// have different lifecycles, permissions and audits, and merging them is how "final invoice"
/// became ambiguous in the first place.</para>
/// </summary>
public class PaymentSourceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request? Request { get; set; }

    /// <summary>The file itself. Unique — one attachment is one source document.</summary>
    public Guid AttachmentId { get; set; }
    public RequestAttachment? Attachment { get; set; }

    // ── Identity of the commercial counterpart ──
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public string? SupplierTaxIdSnapshot { get; set; }

    /// <summary>
    /// The plant this document is for. Part of the PO-group key, which is why two invoices from one
    /// supplier for two plants produce two groups.
    /// </summary>
    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

    // ── Document identity (Release 2 taxonomy) ──
    /// <summary>What the supplier issued — see RequestConstants.SourceDocumentTypes.</summary>
    public string? SourceDocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentSeries { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Currency { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? GrossAmount { get; set; }

    // ── How the classification was reached (Release 2 evidence model, per document) ──
    public string? OcrSuggestion { get; set; }
    public decimal? OcrConfidence { get; set; }
    public string? OcrEvidenceJson { get; set; }
    public string? OcrConflictingEvidenceJson { get; set; }
    public string? OcrTitleFound { get; set; }

    /// <summary>USER_SELECTED, OCR_CONFIRMED or FINANCE_REVIEW.</summary>
    public string? ClassificationSource { get; set; }
    /// <summary>OCR or FALLBACK — how the suggestion was reached.</summary>
    public string? ClassificationSuggestionSource { get; set; }
    /// <summary>The user was shown a contradiction with the reading and proceeded anyway.</summary>
    public bool ClassificationConflictAcknowledged { get; set; }
    /// <summary>Mandatory written reason when a high-risk contradiction was overridden.</summary>
    public string? ClassificationJustification { get; set; }

    /// <summary>Finance reviewed this document's classification, where the type requires it.</summary>
    public bool ClassificationReviewedByFinance { get; set; }
    public Guid? ClassificationReviewedByUserId { get; set; }
    public DateTime? ClassificationReviewedAtUtc { get; set; }

    // ── Position and lifecycle ──
    /// <summary>1..N within the request — stable "Documento 2" labelling that survives a removal.</summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// Removed after submission. Never hard-deleted at that point: its classification decision and
    /// any justification were already audited, and an audit must survive the object it describes.
    /// Before submission the row is deleted outright — nothing downstream exists yet.
    /// </summary>
    public bool IsVoided { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The items this document pays for. Their <c>RequestPoGroupId</c> is what distributes the
    /// document across PO groups — no separate allocation table exists, because a stored allocation
    /// amount could disagree with the sum of the lines it claims to represent.
    /// </summary>
    public ICollection<RequestLineItem> LineItems { get; set; } = new List<RequestLineItem>();
}
