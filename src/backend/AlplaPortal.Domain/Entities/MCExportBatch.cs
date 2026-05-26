namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Immutable record of a generated Primavera-compatible Excel export.
/// Created when HR triggers export generation on a READY_FOR_EXPORT run.
///
/// Lifecycle: GENERATED → DOWNLOADED → IMPORTED → ARCHIVED
///
/// The Excel file IS the operational contract of V1:
/// if the generated file is correct, the middleware is functionally correct.
///
/// ConfigSnapshotJson captures the exact code mappings and thresholds active
/// at export time, enabling full traceability of which rules produced each export.
/// </summary>
public class MCExportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Parent ───

    public Guid ProcessingRunId { get; set; }
    public MCProcessingRun? ProcessingRun { get; set; }

    // ─── Lifecycle ───

    /// <summary>Status codes: GENERATED, DOWNLOADED, IMPORTED, ARCHIVED.</summary>
    public string StatusCode { get; set; } = "GENERATED";

    // ─── File ───

    /// <summary>Generated filename, e.g. "ExportRH_AlplaPLASTICO_2026-03.xlsx".</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Excel file content stored as binary. Nullable to allow deferred storage.</summary>
    public byte[]? FileData { get; set; }

    public int RowCount { get; set; }

    // ─── Generation Audit ───

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid GeneratedByUserId { get; set; }
    public string GeneratedByEmail { get; set; } = string.Empty;

    // ─── Download Tracking ───

    public DateTime? DownloadedAtUtc { get; set; }

    // ─── Import Confirmation ───

    public DateTime? ImportConfirmedAtUtc { get; set; }
    public string? ImportConfirmedBy { get; set; }
    public string? Notes { get; set; }

    // ─── Configuration Audit Snapshot (Amendment §5) ───

    /// <summary>
    /// Serialized JSON snapshot of all active MCPrimaveraCodeMapping and MCDetectionThreshold
    /// rules at export generation time. Enables answering "which rules produced this export?"
    /// </summary>
    public string? ConfigSnapshotJson { get; set; }

    /// <summary>
    /// SHA256 hash of ConfigSnapshotJson for quick comparison between exports.
    /// Enables answering "did config change between March and April exports?"
    /// </summary>
    public string? ConfigSnapshotHash { get; set; }

    // ─── Navigation ───

    public ICollection<MCExportRow> Rows { get; set; } = new List<MCExportRow>();
}
