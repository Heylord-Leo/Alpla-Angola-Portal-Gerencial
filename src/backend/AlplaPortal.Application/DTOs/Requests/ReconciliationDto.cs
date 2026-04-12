using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Requests;

// ──────────────────────────────────────────────────────────────────
// Response DTOs
// ──────────────────────────────────────────────────────────────────

public class OcrExtractedItemDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public string RawDescription { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? RawUnit { get; set; }
    public int? ResolvedUnitId { get; set; }
    public string? ResolvedUnitCode { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? LineTotal { get; set; }
}

public class ReconciliationRecordDto
{
    public Guid Id { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public decimal? MatchConfidence { get; set; }
    public string? MatchStrategy { get; set; }
    public decimal? QuantityDivergence { get; set; }
    public bool UnitDivergence { get; set; }
    public string BuyerReviewStatus { get; set; } = "PENDING";
    public string? BuyerJustification { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    // Inline snapshots so the UI doesn't need separate queries
    public Guid? RequesterItemId { get; set; }
    public string? RequesterDescription { get; set; }
    public decimal? RequesterQuantity { get; set; }
    public string? RequesterUnitCode { get; set; }
    public int? RequesterCatalogId { get; set; }
    public string? RequesterCatalogCode { get; set; }

    public Guid? OcrExtractedItemId { get; set; }
    public string? OcrDescription { get; set; }
    public decimal? OcrQuantity { get; set; }
    public string? OcrRawUnit { get; set; }
    public decimal? OcrUnitPrice { get; set; }
    public decimal? OcrLineTotal { get; set; }
}

public class ReconciliationBatchDto
{
    public Guid ExtractionBatchId { get; set; }
    public DateTime ExtractedAtUtc { get; set; }
    public string? ProviderName { get; set; }
    public decimal QualityScore { get; set; }
    public Guid? AttachmentId { get; set; }
    public int OcrItemCount { get; set; }
    public List<ReconciliationRecordDto> Records { get; set; } = new();
    public ReconciliationSummaryDto Summary { get; set; } = new();
}

public class ReconciliationSummaryDto
{
    public int TotalRecords { get; set; }
    public int ExactMatches { get; set; }
    public int ProbableMatches { get; set; }
    public int ReviewRequired { get; set; }
    public int ExtraSupplierItems { get; set; }
    public int MissingRequestedItems { get; set; }
    public int BuyerConfirmed { get; set; }
    public int BuyerPending { get; set; }
    public int BuyerRejected { get; set; }
}

// ──────────────────────────────────────────────────────────────────
// Request DTO (buyer review submission)
// ──────────────────────────────────────────────────────────────────

public class ReconciliationReviewItemDto
{
    /// <summary>ID of the ReconciliationRecord being reviewed.</summary>
    public Guid RecordId { get; set; }

    /// <summary>CONFIRMED, REJECTED, ADJUSTED</summary>
    public string ReviewStatus { get; set; } = string.Empty;

    /// <summary>Optional buyer justification/notes.</summary>
    public string? Justification { get; set; }
}

public class ReconciliationReviewRequestDto
{
    public List<ReconciliationReviewItemDto> Reviews { get; set; } = new();
}
