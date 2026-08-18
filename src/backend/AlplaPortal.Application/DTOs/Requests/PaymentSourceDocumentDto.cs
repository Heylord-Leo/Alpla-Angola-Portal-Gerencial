using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// One document that originates a PAYMENT request, as the UI sees it.
///
/// <para>Deliberately excludes the audit-only trail (who voided, when, the void reason beyond its
/// text): the screen needs to render and validate a document, not to reproduce its history. The
/// override audit lives in <c>DocumentClassificationOverrides</c> and is read through the request
/// timeline.</para>
/// </summary>
public class PaymentSourceDocumentDto
{
    public Guid Id { get; set; }
    public int SequenceNumber { get; set; }

    // ── Attachment ──
    public Guid AttachmentId { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentStorageReference { get; set; }

    // ── Commercial counterpart ──
    public int? SupplierId { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public string? SupplierTaxIdSnapshot { get; set; }
    public int? PlantId { get; set; }
    public string? PlantName { get; set; }

    // ── Document identity ──
    public string? SourceDocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentSeries { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Currency { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? GrossAmount { get; set; }

    /// <summary>Sum of this document's active items — the number the gross must agree with.</summary>
    public decimal ItemsTotal { get; set; }

    // ── How the classification was reached ──
    public string? OcrSuggestion { get; set; }
    public decimal? OcrConfidence { get; set; }
    public string? OcrTitleFound { get; set; }
    public string? OcrEvidenceJson { get; set; }
    public string? OcrConflictingEvidenceJson { get; set; }
    public string? ClassificationSource { get; set; }
    public string? ClassificationSuggestionSource { get; set; }
    public bool ClassificationConflictAcknowledged { get; set; }
    public string? ClassificationJustification { get; set; }

    public bool ClassificationReviewedByFinance { get; set; }
    public Guid? ClassificationReviewedByUserId { get; set; }
    public string? ClassificationReviewedByName { get; set; }
    public DateTime? ClassificationReviewedAtUtc { get; set; }

    // ── Derived obligations, so the UI never re-derives them ──
    public bool RequiresOperationInvoice { get; set; }
    public bool RequiresAdvanceRegularization { get; set; }
    public bool RequiresFinanceClassificationReview { get; set; }

    // ── Lifecycle ──
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }

    /// <summary>Items belonging to this document.</summary>
    public List<PaymentSourceDocumentItemDto> Items { get; set; } = new();

    /// <summary>
    /// The supplier on this document is an internal ALPLA legal entity, which can never be the
    /// payable counterparty of a payment request.
    /// </summary>
    ///
    /// <remarks>
    /// Only ever true for a document saved before the rule existed — persistence refuses it now.
    /// Surfaced so the screen can state the problem plainly instead of leaving the user to guess
    /// why the request will not submit. Historical rows are shown, never rewritten.
    /// </remarks>
    public bool SupplierIsInternalCompany { get; set; }
    public string? SupplierInternalCompanyName { get; set; }

    /// <summary>Why this document is not yet submittable. Empty when it is.</summary>
    public List<string> ValidationMessages { get; set; } = new();
    public bool IsValid => ValidationMessages.Count == 0;

    /// <summary>Must be echoed on update — a stale edit is reported, never merged.</summary>
    public byte[]? RowVersion { get; set; }
}

/// <summary>An item as it appears inside its source document's card.</summary>
public class PaymentSourceDocumentItemDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? UnitId { get; set; }
    public string? UnitCode { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? DiscountAmount { get; set; }
    public int? IvaRateId { get; set; }
    public decimal TotalAmount { get; set; }
    public int? PlantId { get; set; }
    public int? SupplierId { get; set; }
    public Guid? RequestPoGroupId { get; set; }
}

/// <summary>Create or replace a source document. Used for both, so the two cannot drift apart.</summary>
public class SavePaymentSourceDocumentDto
{
    /// <summary>The uploaded file. Required on create; on update it replaces the document.</summary>
    public Guid? AttachmentId { get; set; }

    public int? SupplierId { get; set; }
    public string? SupplierTaxIdSnapshot { get; set; }
    public int? PlantId { get; set; }

    public string? SourceDocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentSeries { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Currency { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? GrossAmount { get; set; }

    // ── Classification evidence ──
    public string? OcrSuggestion { get; set; }
    public decimal? OcrConfidence { get; set; }
    public string? OcrTitleFound { get; set; }
    public string? OcrEvidenceJson { get; set; }
    public string? OcrConflictingEvidenceJson { get; set; }
    public string? ClassificationSource { get; set; }
    public string? ClassificationSuggestionSource { get; set; }
    public bool? ClassificationConflictAcknowledged { get; set; }
    public string? ClassificationJustification { get; set; }

    /// <summary>
    /// Required on update. Absent or stale is reported as a conflict — never merged, because a
    /// classification decision is a judgement about a specific reading.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Client-supplied token making a retried CREATE a no-op instead of a second document. Only
    /// creation is keyed: an ordinary field edit must stay repeatable.
    /// </summary>
    public string? CreationIdempotencyKey { get; set; }

    // ── Duplicate hierarchy LEVEL 4 (v2.229.10) ──

    /// <summary>
    /// The user saw the ambiguous-duplicate refusal (same supplier reference, insufficient content
    /// evidence to prove duplicate or distinct) and explicitly chose to proceed. Never implied.
    /// </summary>
    public bool? DuplicateOverrideAcknowledged { get; set; }

    /// <summary>Mandatory written reason (≥ 20 chars) accompanying the acknowledgement. Audited.</summary>
    public string? DuplicateOverrideReason { get; set; }
}

/// <summary>Everything the payment screen needs about a request's origin documents.</summary>
public class PaymentSourceDocumentsSummaryDto
{
    public Guid RequestId { get; set; }

    /// <summary>Non-voided documents, in sequence order.</summary>
    public List<PaymentSourceDocumentDto> Documents { get; set; } = new();

    /// <summary>Voided documents, kept visible because their decisions were audited.</summary>
    public List<PaymentSourceDocumentDto> VoidedDocuments { get; set; } = new();

    /// <summary>Sum of the active documents' gross amounts. The authoritative request total.</summary>
    public decimal RequestTotal { get; set; }
    public string? Currency { get; set; }

    /// <summary>
    /// This request uses the multi-source-document model. TRUE with zero documents means a new draft
    /// awaiting its first one — the collection must render. FALSE with zero documents means a
    /// historical request that keeps the legacy single-document layout.
    /// </summary>
    public bool UsesMultiDocumentModel { get; set; }

    /// <summary>True while the request is DRAFT or returned for adjustment.</summary>
    public bool CanEditDocuments { get; set; }
    public string? EditBlockedReason { get; set; }

    /// <summary>Request-level problems (no documents at all, mixed currencies).</summary>
    public List<string> RequestValidationMessages { get; set; } = new();

    /// <summary>
    /// Non-blocking: the same supplier appears with different document types. Backend metadata for
    /// the Phase 3 UI; submission is never refused because of it.
    /// </summary>
    public string? MixedTypeNotice { get; set; }

    public bool CanSubmit { get; set; }
}

/// <summary>Void a document that a previously submitted request has had returned for adjustment.</summary>
public class VoidPaymentSourceDocumentDto
{
    public string? Reason { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Asks whether a file may be attached, before the browser spends the user's time reading it.
///
/// <para>The hash is computed client-side and sent for checking, so the file itself never has to be
/// uploaded to find out that it is already here. Hashes are deliberately NOT exposed on the ordinary
/// summary — a client that can enumerate them can fingerprint documents it was never shown.</para>
/// </summary>
public class CheckSourceDocumentDuplicateDto
{
    public string? ContentHash { get; set; }
    public string? CandidateFileName { get; set; }

    /// <summary>Set when replacing an attachment: that document cannot conflict with itself.</summary>
    public Guid? ReplacingDocumentId { get; set; }
}

public class SourceDocumentDuplicateResultDto
{
    public bool IsDuplicate { get; set; }

    /// <summary>NONE, SAME_REQUEST or OTHER_REQUEST.</summary>
    public string DuplicateScope { get; set; } = "NONE";

    /// <summary>NONE, BLOCK or WARN.</summary>
    public string Outcome { get; set; } = "NONE";

    // Identifying detail for the SAME_REQUEST case, where the user is entitled to see it: it is
    // their own request, already on their screen.
    public Guid? ConflictingDocumentId { get; set; }
    public int? ConflictingSequenceNumber { get; set; }
    public string? ConflictingDocumentNumber { get; set; }
    public string? SupplierName { get; set; }

    // OTHER_REQUEST detail, populated only when the user may see that request. A duplicate is
    // confirmed either way; what is withheld is whose it is.
    public string? RequestNumber { get; set; }
    public Guid? RequestId { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
}
