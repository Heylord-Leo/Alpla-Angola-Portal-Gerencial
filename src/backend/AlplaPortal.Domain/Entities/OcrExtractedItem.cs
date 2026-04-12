namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Immutable snapshot of a line item extracted by OCR from a supplier document.
/// These records are never modified after creation — they represent what the
/// document said at extraction time. Each OCR run creates a new batch
/// (grouped by ExtractionBatchId) so prior extractions are preserved for audit.
/// </summary>
public class OcrExtractedItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    /// <summary>Groups items from the same OCR extraction run.</summary>
    public Guid ExtractionBatchId { get; set; }

    /// <summary>Optional FK to the PROFORMA attachment that was processed.</summary>
    public Guid? AttachmentId { get; set; }
    public RequestAttachment? Attachment { get; set; }

    /// <summary>Line ordering within the extraction.</summary>
    public int LineNumber { get; set; }

    /// <summary>Exact description text as extracted by OCR.</summary>
    public string RawDescription { get; set; } = string.Empty;

    public decimal? Quantity { get; set; }

    /// <summary>Raw unit string before resolution (e.g., "Unid.", "CX", "KG").</summary>
    public string? RawUnit { get; set; }

    /// <summary>Resolved FK to Units table, if the raw unit was successfully mapped.</summary>
    public int? ResolvedUnitId { get; set; }
    public Unit? ResolvedUnit { get; set; }

    public decimal? UnitPrice { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? LineTotal { get; set; }

    /// <summary>Overall extraction quality score (0–100).</summary>
    public decimal QualityScore { get; set; }

    /// <summary>OCR provider used (e.g., "OPENAI", "LOCAL_OCR").</summary>
    public string? ProviderName { get; set; }

    public DateTime ExtractedAtUtc { get; set; } = DateTime.UtcNow;
}
