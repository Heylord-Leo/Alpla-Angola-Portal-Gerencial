using AlplaPortal.Application.DTOs.MonthlyChanges;

namespace AlplaPortal.Application.Interfaces.MonthlyChanges;

/// <summary>
/// Orchestrates the end-to-end processing pipeline for a monthly changes run.
/// Coordinates: create → sync → detect → (future: code → export).
///
/// State machine enforcement:
///   DRAFT → SYNCING → NEEDS_REVIEW → READY_FOR_EXPORT → EXPORTED → CLOSED
///   SYNCING → FAILED (on error, allows retry → DRAFT)
/// </summary>
public interface IMonthlyChangesOrchestrator
{
    /// <summary>Creates a new DRAFT processing run for the given entity+month.</summary>
    Task<ProcessingRunDetailDto> CreateRunAsync(CreateProcessingRunRequest request, Guid userId);

    /// <summary>Executes a DRAFT run: sync → detect → update stats. Transitions through SYNCING → NEEDS_REVIEW or FAILED.</summary>
    Task<ProcessingRunDetailDto> ExecuteRunAsync(Guid runId, Guid userId);

    /// <summary>Returns a summary list of all processing runs, newest first.</summary>
    Task<IEnumerable<ProcessingRunSummaryDto>> ListRunsAsync(int? entityId = null);

    /// <summary>Returns full detail for a specific run, including item counts by status.</summary>
    Task<ProcessingRunDetailDto?> GetRunDetailAsync(Guid runId);

    // ─── Review Actions ──────────────────────────────────────────────────

    /// <summary>Approves an AUTO_CODED item for export.</summary>
    Task<MonthlyChangeItemDto> ApproveItemAsync(Guid runId, Guid itemId, Guid userId);

    /// <summary>Excludes an item (AUTO_CODED, APPROVED, or NEEDS_REVIEW) from export.</summary>
    Task<MonthlyChangeItemDto> ExcludeItemAsync(Guid runId, Guid itemId, ExcludeItemRequest request, Guid userId);

    /// <summary>Re-includes an EXCLUDED item (back to AUTO_CODED or NEEDS_REVIEW).</summary>
    Task<MonthlyChangeItemDto> ReincludeItemAsync(Guid runId, Guid itemId, Guid userId);

    /// <summary>Resolves an anomaly item (NEEDS_REVIEW) by approving or excluding it.</summary>
    Task<MonthlyChangeItemDto> ResolveAnomalyAsync(Guid runId, Guid itemId, ResolveAnomalyRequest request, Guid userId);

    /// <summary>Marks a NEEDS_REVIEW run as READY_FOR_EXPORT, validating no unresolved items remain.</summary>
    Task<ProcessingRunDetailDto> MarkRunReadyForExportAsync(Guid runId, Guid userId);

    /// <summary>Reverts a READY_FOR_EXPORT run back to NEEDS_REVIEW.</summary>
    Task<ProcessingRunDetailDto> RevertRunToReviewAsync(Guid runId, Guid userId);

    // ─── Queries ─────────────────────────────────────────────────────────

    /// <summary>Returns detected items for a run, optionally filtered by status.</summary>
    Task<IEnumerable<MonthlyChangeItemDto>> ListItemsAsync(Guid runId, string? statusCode = null);

    /// <summary>Returns only anomaly items for a run.</summary>
    Task<IEnumerable<AnomalyItemDto>> ListAnomaliesAsync(Guid runId);

    /// <summary>Returns processing logs for a run.</summary>
    Task<IEnumerable<ProcessingLogDto>> GetLogsAsync(Guid runId);
}

/// <summary>
/// Syncs Innux processed attendance data (Alteracoes) into MCAttendanceSnapshot.
/// One sync = one ProcessingRun = one entity + one month.
///
/// The sync creates immutable snapshot rows — never updates existing ones.
/// Uses direct SQL against Innux (via InnuxConnectionFactory).
/// </summary>
public interface IMonthlyChangesSyncService
{
    /// <summary>
    /// Queries Innux Alteracoes for the given entity+month and persists
    /// MCAttendanceSnapshot rows under the specified processing run.
    /// Returns the count of rows synced.
    /// </summary>
    Task<int> SyncAttendanceDataAsync(Guid processingRunId, int entityId, int year, int month, CancellationToken ct = default);
}

/// <summary>
/// Reads MCAttendanceSnapshot rows for a processing run and detects occurrences,
/// creating MCMonthlyChangeItem entries.
///
/// Detection rules (V1):
///   - Unjustified absence: AbsenceMinutes > 0 AND JustifiedAbsenceMinutes = 0
///   - Justified absence: JustifiedAbsenceMinutes > 0
///   - Lateness: FirstEntry later than scheduled start + threshold
///   - Anomaly: AnomalyDescription present in Innux data
///
/// Does NOT assign Primavera codes (that's CodingService, blocked until code catalog available).
/// Items start as AUTO_CODED or NEEDS_REVIEW depending on detection confidence.
/// </summary>
public interface IOccurrenceDetectionEngine
{
    /// <summary>
    /// Processes all snapshots for a run, creating MCMonthlyChangeItem entries.
    /// Returns (occurrenceCount, anomalyCount).
    /// </summary>
    Task<(int OccurrenceCount, int AnomalyCount)> DetectOccurrencesAsync(Guid processingRunId, int entityId, CancellationToken ct = default);
}
