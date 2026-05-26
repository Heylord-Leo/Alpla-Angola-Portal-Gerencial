namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Aggregate root for the HR Monthly Changes workflow.
/// One run = one calendar month for one Innux entity (AlplaPLASTICO or AlplaSOPRO).
///
/// Lifecycle: DRAFT → SYNCING → NEEDS_REVIEW → READY_FOR_EXPORT → EXPORTED → CLOSED
///            SYNCING → FAILED → DRAFT (retry)
///
/// This entity orchestrates the Innux → Portal → Primavera export pipeline:
///   1. Sync Innux data into local snapshots
///   2. Detect occurrences (Layer 1 — classification)
///   3. Assign Primavera codes (Layer 2 — coding)
///   4. HR reviews and adjusts
///   5. Generate Primavera-compatible Excel
///   6. HR confirms manual import into Primavera
/// </summary>
public class MCProcessingRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Scope ───

    /// <summary>Innux IDEntidade (1 = AlplaPLASTICO, 6 = AlplaSOPRO).</summary>
    public int EntityId { get; set; }

    /// <summary>Human-readable entity name for display and audit.</summary>
    public string EntityName { get; set; } = string.Empty;

    public int Year { get; set; }
    public int Month { get; set; }

    // ─── Lifecycle ───

    /// <summary>
    /// Status codes: DRAFT, SYNCING, NEEDS_REVIEW, READY_FOR_EXPORT, EXPORTED, CLOSED, FAILED.
    /// </summary>
    public string StatusCode { get; set; } = "DRAFT";

    public string? ErrorMessage { get; set; }

    // ─── Processing Metrics ───

    public DateTime? SyncedAtUtc { get; set; }
    public int SyncedRowCount { get; set; }
    public DateTime? DetectionCompletedAtUtc { get; set; }
    public int OccurrenceCount { get; set; }
    public int AnomalyCount { get; set; }

    /// <summary>Items in AUTO_CODED or NEEDS_REVIEW that still require HR action.</summary>
    public int UnresolvedCount { get; set; }

    // ─── Audit ───

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    // ─── Navigation ───

    public ICollection<MCAttendanceSnapshot> Snapshots { get; set; } = new List<MCAttendanceSnapshot>();
    public ICollection<MCMonthlyChangeItem> Items { get; set; } = new List<MCMonthlyChangeItem>();
    public ICollection<MCExportBatch> Exports { get; set; } = new List<MCExportBatch>();
    public ICollection<MCProcessingLog> Logs { get; set; } = new List<MCProcessingLog>();
}
