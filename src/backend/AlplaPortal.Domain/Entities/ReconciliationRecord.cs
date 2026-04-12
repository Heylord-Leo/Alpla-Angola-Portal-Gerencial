namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Links requester items to OCR-extracted items with match analysis.
/// Generated automatically after OCR extraction, then updated by the buyer
/// during the review step. Non-blocking — buyer can save quotation
/// without completing reconciliation review.
/// </summary>
public class ReconciliationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    /// <summary>Groups reconciliation to a specific OCR extraction run.</summary>
    public Guid ExtractionBatchId { get; set; }

    /// <summary>FK to the requester's original line item. Null = extra supplier item with no requester match.</summary>
    public Guid? RequesterItemId { get; set; }
    public RequestLineItem? RequesterItem { get; set; }

    /// <summary>FK to the OCR-extracted item. Null = missing requested item not found in document.</summary>
    public Guid? OcrExtractedItemId { get; set; }
    public OcrExtractedItem? OcrExtractedItem { get; set; }

    /// <summary>FK to the final QuotationItem, linked after buyer saves. Optional and filled later.</summary>
    public Guid? QuotationItemId { get; set; }
    public QuotationItem? QuotationItem { get; set; }

    /// <summary>
    /// System-generated match result.
    /// Values: EXACT_MATCH, PROBABLE_MATCH, REVIEW_REQUIRED, EXTRA_SUPPLIER_ITEM, MISSING_REQUESTED_ITEM
    /// </summary>
    public string MatchStatus { get; set; } = "REVIEW_REQUIRED";

    /// <summary>Match confidence score (0.0–1.0). Higher = more confident.</summary>
    public decimal? MatchConfidence { get; set; }

    /// <summary>
    /// How the match was determined.
    /// Values: CATALOG_CODE, DESCRIPTION_EXACT, DESCRIPTION_FUZZY, UNIT_QTY_HINT, MANUAL, NONE
    /// </summary>
    public string? MatchStrategy { get; set; }

    /// <summary>Quantity difference (supplier qty - requester qty). Null if either side is missing.</summary>
    public decimal? QuantityDivergence { get; set; }

    /// <summary>Whether the units differ between requester and supplier items.</summary>
    public bool UnitDivergence { get; set; }

    /// <summary>
    /// Buyer review status for this reconciliation record.
    /// Values: PENDING, CONFIRMED, REJECTED, ADJUSTED
    /// </summary>
    public string BuyerReviewStatus { get; set; } = "PENDING";

    /// <summary>Optional buyer justification for divergences or decisions.</summary>
    public string? BuyerJustification { get; set; }

    /// <summary>FK to the user who reviewed this record.</summary>
    public Guid? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
