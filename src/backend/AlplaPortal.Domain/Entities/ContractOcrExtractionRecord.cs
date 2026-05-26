namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Audit record for a single OCR extraction run on a contract document.
/// One record is created per extraction attempt (re-runs produce new records).
/// The latest record is referenced by Contract.OcrExtractionBatchId.
///
/// Status lifecycle: PENDING → PROCESSING → COMPLETED | FAILED
/// Re-runs: previous records are preserved; only Contract.OcrExtractionBatchId is updated.
/// </summary>
public class ContractOcrExtractionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── Source links ──────────────────────────────────────────────────
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;

    /// <summary>The document that was processed. Preserved even if the document is later replaced.</summary>
    public Guid ContractDocumentId { get; set; }
    public ContractDocument ContractDocument { get; set; } = null!;

    // ── Trigger ───────────────────────────────────────────────────────
    public Guid TriggeredByUserId { get; set; }
    public User TriggeredByUser { get; set; } = null!;

    public DateTime TriggeredAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }

    // ── Status ────────────────────────────────────────────────────────
    /// <summary>PENDING | PROCESSING | COMPLETED | FAILED</summary>
    public string Status { get; set; } = "PENDING";

    public string? ErrorMessage { get; set; }

    // ── Provider metadata ─────────────────────────────────────────────
    public string ProviderName { get; set; } = "OPENAI";
    public string? RoutingStrategy { get; set; }

    public int ChunkCount { get; set; }
    public int TotalTokensUsed { get; set; }

    /// <summary>Overall quality score reported by the extraction provider (0.0–1.0).</summary>
    public decimal? QualityScore { get; set; }

    public bool IsPartial { get; set; }
    public bool ConflictsDetected { get; set; }
    public bool NativeTextDetected { get; set; }

    /// <summary>
    /// Full raw JSON response from the LLM, stored verbatim for debugging and reprocessing.
    /// Not exposed via API. Trimmed to max 64 KB if the provider returns oversized output.
    /// </summary>
    public string? RawJsonResult { get; set; }

    // ── Navigation ────────────────────────────────────────────────────
    public ICollection<ContractOcrExtractedField> ExtractedFields { get; set; } = new List<ContractOcrExtractedField>();
}
