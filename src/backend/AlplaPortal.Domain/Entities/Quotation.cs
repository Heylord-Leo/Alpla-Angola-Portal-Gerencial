using System;
using System.Collections.Generic;

namespace AlplaPortal.Domain.Entities;

public class Quotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string? DocumentNumber { get; set; }
    public DateTime? DocumentDate { get; set; }

    /// <summary>
    /// IDENTITY of the document the supplier issued for this quotation — see
    /// RequestConstants.SourceDocumentTypes. No default; the Buyer must select explicitly.
    /// The WINNING quotation's value is propagated to the PO group by GroupBuilderService.
    /// </summary>
    public string? DocumentType { get; set; }

    /// <summary>How the classification was reached: USER_SELECTED, OCR_CONFIRMED, FINANCE_REVIEW.</summary>
    public string? DocumentTypeSource { get; set; }

    /// <summary>What document extraction proposed, if anything. Never auto-applied.</summary>
    public string? DocumentTypeOcrSuggestion { get; set; }

    /// <summary>Extraction confidence for the suggestion (0.0–1.0).</summary>
    public decimal? DocumentTypeOcrConfidence { get; set; }

    /// <summary>Serialized evidence behind the suggestion.</summary>
    public string? DocumentTypeEvidenceJson { get; set; }

    /// <summary>The Buyer was shown a conflict with the extracted evidence and proceeded anyway.</summary>
    public bool ClassificationConflictAcknowledged { get; set; }

    /// <summary>Mandatory written reason when a high-risk conflict was overridden.</summary>
    public string? ClassificationJustification { get; set; }
    public string Currency { get; set; } = string.Empty;

    // Financial Totals
    public decimal TotalGrossAmount { get; set; }
    public decimal TotalDiscountAmount { get; set; }
    public decimal TotalTaxableBase { get; set; }
    public decimal TotalIvaAmount { get; set; }
    public decimal DiscountAmount { get; set; } // Added manually at quotation level
    public decimal TotalAmount { get; set; } // Final Total (Taxable + IVA)

    public bool IsSelected { get; set; } = false; // Winning Quotation Selection

    public string SourceType { get; set; } = string.Empty; // OCR, MANUAL
    public string? SourceFileName { get; set; }

    public Guid? ProformaAttachmentId { get; set; }
    public RequestAttachment? ProformaAttachment { get; set; }

    /// <summary>Adjustment V2 (Phase 4) — revision provenance. When set, this quotation is a commercial
    /// REVISION of the referenced quotation (created during a batch rework). The original quotation
    /// stays immutable/auditable; this one is a NEW identity carrying revised commercial values. Null
    /// for every ordinary quotation (no historical backfill). NOT Phase 6 candidate-review.</summary>
    public Guid? RevisesQuotationId { get; set; }
    public Quotation? RevisesQuotation { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    /// <summary>§7 durable "ever submitted for approval" marker. Written atomically with the FIRST persisted
    /// submission of any line of this quotation (candidate, batch winner or legacy line-level award) by
    /// <c>QuotationSubmissionStamper</c> inside SaveChanges; never overwritten or cleared, and independent of the
    /// batch references that rework recomposition / extra-item reversal / batch deletion may remove later.
    /// Null = never submitted (ordinary edit/delete allowed) — or submitted before the marker existed AND all
    /// references already removed before the migration backfill (documented boundary).</summary>
    public DateTime? FirstSubmittedForApprovalAtUtc { get; set; }
    /// <summary>Identity of the batch of the first submission. Historical pointer WITHOUT a foreign key on
    /// purpose: deleting or recomposing a batch must never clear or block this value. Null for legacy
    /// request-level awards (no batch) or when the parent batch item was not tracked at stamp time.</summary>
    public Guid? FirstSubmittedBatchId { get; set; }
    /// <summary>How the marker was established — see <see cref="QuotationSubmissionSources"/>. SUBMISSION = the
    /// actual instant (stamped live); BACKFILL_BATCH = persisted batch evidence dated by the batch's CreatedAtUtc (a
    /// lower bound: rework edits and extra-item inclusions add quotations to an EXISTING batch later);
    /// BACKFILL_LINE_AWARD_ESTIMATED = approximate date (line UpdatedAtUtc or quotation CreatedAtUtc). Written once
    /// with the marker, never changed.</summary>
    public string? FirstSubmittedSource { get; set; }
    /// <summary>Optimistic concurrency between a Buyer edit/delete and a submission committing at the same time:
    /// the stamper updates the quotation row, so a stale PUT/DELETE fails with DbUpdateConcurrencyException (409).</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
}

/// <summary>Values of <see cref="Quotation.FirstSubmittedSource"/>.</summary>
public static class QuotationSubmissionSources
{
    /// <summary>Stamped live by the submission's own SaveChanges — exact instant.</summary>
    public const string Submission = "SUBMISSION";
    /// <summary>Migration backfill from a still-existing batch reference — PROVEN submission, dated by the batch's
    /// CreatedAtUtc. Equals the submission instant only for quotations present when the batch was created; quotations
    /// added by a rework edit or an extra-item inclusion joined that batch later, so the date is persisted batch
    /// evidence (lower bound), not the exact first-submission time.</summary>
    public const string BackfillBatch = "BACKFILL_BATCH";
    /// <summary>Migration backfill from a legacy line-level award only — PROVEN submission, ESTIMATED timestamp.</summary>
    public const string BackfillLineAwardEstimated = "BACKFILL_LINE_AWARD_ESTIMATED";
}
