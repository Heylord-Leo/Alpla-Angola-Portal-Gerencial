using AlplaPortal.Application.DTOs.MonthlyChanges;
using AlplaPortal.Application.Interfaces.MonthlyChanges;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.MonthlyChanges;

/// <summary>
/// V1 Processing Run Orchestrator.
///
/// Coordinates the lifecycle of a processing run:
///   Create (DRAFT) → Execute (SYNCING → NEEDS_REVIEW or FAILED)
///
/// State machine:
///   DRAFT → SYNCING → NEEDS_REVIEW → READY_FOR_EXPORT → EXPORTED → CLOSED
///   SYNCING → FAILED (on error, allows manual retry back to DRAFT)
///
/// The orchestrator is the single entry point for state transitions.
/// It calls SyncService and DetectionEngine in sequence,
/// then updates statistics on the run.
/// </summary>
public class MonthlyChangesOrchestrator : IMonthlyChangesOrchestrator
{
    private readonly ApplicationDbContext _context;
    private readonly IMonthlyChangesSyncService _syncService;
    private readonly IOccurrenceDetectionEngine _detectionEngine;
    private readonly ILogger<MonthlyChangesOrchestrator> _logger;

    public MonthlyChangesOrchestrator(
        ApplicationDbContext context,
        IMonthlyChangesSyncService syncService,
        IOccurrenceDetectionEngine detectionEngine,
        ILogger<MonthlyChangesOrchestrator> logger)
    {
        _context = context;
        _syncService = syncService;
        _detectionEngine = detectionEngine;
        _logger = logger;
    }

    // ─── Create ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ProcessingRunDetailDto> CreateRunAsync(CreateProcessingRunRequest request, Guid userId)
    {
        // Validate: no duplicate DRAFT/SYNCING/NEEDS_REVIEW run for same entity+month
        var existing = await _context.MCProcessingRuns
            .Where(r => r.EntityId == request.EntityId
                     && r.Year == request.Year
                     && r.Month == request.Month
                     && r.StatusCode != "CLOSED" && r.StatusCode != "FAILED" && r.StatusCode != "EXPORTED")
            .AnyAsync();

        if (existing)
            throw new InvalidOperationException(
                $"An active processing run already exists for Entity {request.EntityId}, {request.Year}-{request.Month:D2}. Close or complete it before creating a new one.");

        var run = new MCProcessingRun
        {
            Id = Guid.NewGuid(),
            EntityId = request.EntityId,
            EntityName = MCStatusLabels.EntityName(request.EntityId),
            Year = request.Year,
            Month = request.Month,
            StatusCode = "DRAFT",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        _context.MCProcessingRuns.Add(run);
        await LogEventAsync(run.Id, "RUN_CREATED", $"Run created for Entity {request.EntityId}, {request.Year}-{request.Month:D2}", userId);
        await _context.SaveChangesAsync();

        _logger.LogInformation("MC Orchestrator: Created run {RunId} for Entity={EntityId}, {Year}-{Month:D2}",
            run.Id, request.EntityId, request.Year, request.Month);

        return await BuildDetailDto(run.Id);
    }

    // ─── Execute ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ProcessingRunDetailDto> ExecuteRunAsync(Guid runId, Guid userId)
    {
        var run = await _context.MCProcessingRuns.FindAsync(runId)
            ?? throw new KeyNotFoundException($"Processing run {runId} not found.");

        if (run.StatusCode != "DRAFT" && run.StatusCode != "FAILED")
            throw new InvalidOperationException(
                $"Run {runId} is in status '{run.StatusCode}'. Only DRAFT or FAILED runs can be executed.");

        // ─── Transition to SYNCING ───
        run.StatusCode = "SYNCING";
        run.SyncedAtUtc = null;
        run.DetectionCompletedAtUtc = null;
        run.ErrorMessage = null;
        await _context.SaveChangesAsync();
        await LogEventAsync(runId, "STATUS_CHANGED", "Status → SYNCING", userId);

        try
        {
            // ─── Step 1: Sync ───
            _logger.LogInformation("MC Orchestrator: Executing sync for run {RunId}", runId);
            var syncedCount = await _syncService.SyncAttendanceDataAsync(runId, run.EntityId, run.Year, run.Month);

            run.SyncedRowCount = syncedCount;
            run.SyncedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await LogEventAsync(runId, "SYNC_COMPLETED", $"Synced {syncedCount} snapshot rows", userId);

            if (syncedCount == 0)
            {
                run.StatusCode = "NEEDS_REVIEW";
                run.OccurrenceCount = 0;
                run.AnomalyCount = 0;
                run.UnresolvedCount = 0;
                await _context.SaveChangesAsync();
                await LogEventAsync(runId, "STATUS_CHANGED", "Status → NEEDS_REVIEW (no data to process)", userId);
                return await BuildDetailDto(runId);
            }

            // ─── Step 2: Detect ───
            _logger.LogInformation("MC Orchestrator: Executing detection for run {RunId}", runId);
            var (occurrenceCount, anomalyCount) = await _detectionEngine.DetectOccurrencesAsync(runId, run.EntityId);

            run.OccurrenceCount = occurrenceCount;
            run.AnomalyCount = anomalyCount;
            run.DetectionCompletedAtUtc = DateTime.UtcNow;

            // ─── Compute unresolved count (items still needing review) ───
            run.UnresolvedCount = await _context.MCMonthlyChangeItems
                .Where(i => i.ProcessingRunId == runId
                         && (i.StatusCode == "AUTO_CODED" || i.StatusCode == "NEEDS_REVIEW"))
                .CountAsync();

            // ─── Transition to NEEDS_REVIEW ───
            run.StatusCode = "NEEDS_REVIEW";
            await _context.SaveChangesAsync();
            await LogEventAsync(runId, "DETECTION_COMPLETED",
                $"Detection completed: {occurrenceCount} occurrences, {anomalyCount} anomalies, {run.UnresolvedCount} unresolved", userId);
            await LogEventAsync(runId, "STATUS_CHANGED", "Status → NEEDS_REVIEW", userId);

            _logger.LogInformation(
                "MC Orchestrator: Run {RunId} completed — {Synced} synced, {Occurrences} occurrences, {Anomalies} anomalies",
                runId, syncedCount, occurrenceCount, anomalyCount);

            return await BuildDetailDto(runId);
        }
        catch (Exception ex)
        {
            // ─── Transition to FAILED ───
            _logger.LogError(ex, "MC Orchestrator: Run {RunId} failed", runId);
            run.StatusCode = "FAILED";
            run.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            await _context.SaveChangesAsync();
            await LogEventAsync(runId, "RUN_FAILED", run.ErrorMessage, userId);

            return await BuildDetailDto(runId);
        }
    }

    // ─── Review Actions ──────────────────────────────────────────────────

    public async Task<MonthlyChangeItemDto> ApproveItemAsync(Guid runId, Guid itemId, Guid userId)
    {
        var run = await _context.MCProcessingRuns.FindAsync(runId)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (run.StatusCode != "NEEDS_REVIEW")
            throw new InvalidOperationException($"Run {runId} is not in review state.");

        var item = await _context.MCMonthlyChangeItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ProcessingRunId == runId)
            ?? throw new KeyNotFoundException($"Item {itemId} not found.");

        if (item.StatusCode != "AUTO_CODED")
            throw new InvalidOperationException($"Item is in {item.StatusCode} state. Only AUTO_CODED items can be directly approved.");

        item.StatusCode = "APPROVED";
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await RecalculateUnresolvedCountAsync(runId);

        await LogEventAsync(runId, "ITEM_APPROVED", $"Item {itemId} ({item.EmployeeCode}) approved.", userId);

        return MapToItemDto(item);
    }

    public async Task<MonthlyChangeItemDto> ExcludeItemAsync(Guid runId, Guid itemId, ExcludeItemRequest request, Guid userId)
    {
        var run = await _context.MCProcessingRuns.FindAsync(runId)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (run.StatusCode != "NEEDS_REVIEW")
            throw new InvalidOperationException($"Run {runId} is not in review state.");

        var item = await _context.MCMonthlyChangeItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ProcessingRunId == runId)
            ?? throw new KeyNotFoundException($"Item {itemId} not found.");

        if (item.StatusCode != "AUTO_CODED" && item.StatusCode != "APPROVED" && item.StatusCode != "NEEDS_REVIEW")
            throw new InvalidOperationException($"Item is in {item.StatusCode} state. Cannot exclude.");

        item.StatusCode = "EXCLUDED";
        item.ExclusionReason = request.Reason;
        item.ExcludedBy = userId.ToString();
        item.ExcludedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await RecalculateUnresolvedCountAsync(runId);

        await LogEventAsync(runId, "ITEM_EXCLUDED", $"Item {itemId} ({item.EmployeeCode}) excluded. Reason: {request.Reason}", userId);

        return MapToItemDto(item);
    }

    public async Task<MonthlyChangeItemDto> ReincludeItemAsync(Guid runId, Guid itemId, Guid userId)
    {
        var run = await _context.MCProcessingRuns.FindAsync(runId)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (run.StatusCode != "NEEDS_REVIEW")
            throw new InvalidOperationException($"Run {runId} is not in review state.");

        var item = await _context.MCMonthlyChangeItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ProcessingRunId == runId)
            ?? throw new KeyNotFoundException($"Item {itemId} not found.");

        if (item.StatusCode != "EXCLUDED")
            throw new InvalidOperationException($"Item is not excluded.");

        item.StatusCode = item.IsAnomaly ? "NEEDS_REVIEW" : "AUTO_CODED";
        item.ExclusionReason = null;
        item.ExcludedBy = null;
        item.ExcludedAtUtc = null;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await RecalculateUnresolvedCountAsync(runId);

        await LogEventAsync(runId, "ITEM_REINCLUDED", $"Item {itemId} ({item.EmployeeCode}) re-included to {item.StatusCode}.", userId);

        return MapToItemDto(item);
    }

    public async Task<MonthlyChangeItemDto> ResolveAnomalyAsync(Guid runId, Guid itemId, ResolveAnomalyRequest request, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(request.ResolutionNote))
            throw new ArgumentException("Resolution note is required when resolving an anomaly.");

        var run = await _context.MCProcessingRuns.FindAsync(runId)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (run.StatusCode != "NEEDS_REVIEW")
            throw new InvalidOperationException($"Run {runId} is not in review state.");

        var item = await _context.MCMonthlyChangeItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ProcessingRunId == runId)
            ?? throw new KeyNotFoundException($"Item {itemId} not found.");

        if (!item.IsAnomaly || item.StatusCode != "NEEDS_REVIEW")
            throw new InvalidOperationException($"Item is not a NEEDS_REVIEW anomaly.");

        item.ResolutionNote = request.ResolutionNote;
        item.ResolvedBy = userId.ToString();
        item.ResolvedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = DateTime.UtcNow;

        if (request.Action.ToUpper() == "EXCLUDE")
        {
            item.StatusCode = "EXCLUDED";
            item.ExclusionReason = request.ResolutionNote;
            item.ExcludedBy = userId.ToString();
            item.ExcludedAtUtc = DateTime.UtcNow;
        }
        else if (request.Action.ToUpper() == "APPROVE")
        {
            item.StatusCode = "APPROVED";
        }
        else
        {
            throw new ArgumentException("Invalid action. Must be APPROVE or EXCLUDE.");
        }

        await _context.SaveChangesAsync();
        await RecalculateUnresolvedCountAsync(runId);

        await LogEventAsync(runId, "ANOMALY_RESOLVED", $"Anomaly {itemId} ({item.EmployeeCode}) resolved as {item.StatusCode}. Note: {request.ResolutionNote}", userId);

        return MapToItemDto(item);
    }

    public async Task<ProcessingRunDetailDto> MarkRunReadyForExportAsync(Guid runId, Guid userId)
    {
        var run = await _context.MCProcessingRuns.FindAsync(runId)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (run.StatusCode != "NEEDS_REVIEW")
            throw new InvalidOperationException($"Run {runId} is not in NEEDS_REVIEW state.");

        // Dynamically recalculate state distribution to ensure safety
        var unresolvedCount = await _context.MCMonthlyChangeItems
            .Where(i => i.ProcessingRunId == runId && (i.StatusCode == "AUTO_CODED" || i.StatusCode == "NEEDS_REVIEW"))
            .CountAsync();

        if (unresolvedCount > 0)
            throw new InvalidOperationException($"Run cannot be marked ready. There are {unresolvedCount} unresolved items remaining.");

        run.StatusCode = "READY_FOR_EXPORT";
        run.UnresolvedCount = 0;
        run.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await LogEventAsync(runId, "STATUS_CHANGED", "Status → READY_FOR_EXPORT", userId);

        return await BuildDetailDto(runId);
    }

    public async Task<ProcessingRunDetailDto> RevertRunToReviewAsync(Guid runId, Guid userId)
    {
        var run = await _context.MCProcessingRuns.FindAsync(runId)
            ?? throw new KeyNotFoundException($"Run {runId} not found.");

        if (run.StatusCode != "READY_FOR_EXPORT")
            throw new InvalidOperationException($"Run {runId} is not in READY_FOR_EXPORT state.");

        run.StatusCode = "NEEDS_REVIEW";
        run.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await LogEventAsync(runId, "STATUS_CHANGED", "Status → NEEDS_REVIEW (Reverted)", userId);

        return await BuildDetailDto(runId);
    }

    // ─── Queries ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IEnumerable<ProcessingRunSummaryDto>> ListRunsAsync(int? entityId = null)
    {
        var query = _context.MCProcessingRuns
            .Include(r => r.CreatedByUser)
            .AsNoTracking()
            .AsQueryable();

        if (entityId.HasValue)
            query = query.Where(r => r.EntityId == entityId.Value);

        var runs = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        return runs.Select(MapToSummaryDto);
    }

    /// <inheritdoc />
    public async Task<ProcessingRunDetailDto?> GetRunDetailAsync(Guid runId)
    {
        var run = await _context.MCProcessingRuns
            .Include(r => r.CreatedByUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId);

        if (run == null) return null;

        return await BuildDetailDto(runId, run);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MonthlyChangeItemDto>> ListItemsAsync(Guid runId, string? statusCode = null)
    {
        var query = _context.MCMonthlyChangeItems
            .Where(i => i.ProcessingRunId == runId)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(statusCode))
            query = query.Where(i => i.StatusCode == statusCode);

        var items = await query
            .OrderBy(i => i.EmployeeCode)
            .ThenBy(i => i.Date)
            .ToListAsync();

        return items.Select(MapToItemDto);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AnomalyItemDto>> ListAnomaliesAsync(Guid runId)
    {
        var items = await _context.MCMonthlyChangeItems
            .Where(i => i.ProcessingRunId == runId && i.IsAnomaly)
            .AsNoTracking()
            .OrderBy(i => i.EmployeeCode)
            .ThenBy(i => i.Date)
            .ToListAsync();

        return items.Select(i => new AnomalyItemDto
        {
            Id = i.Id,
            EmployeeCode = i.EmployeeCode,
            EmployeeName = i.EmployeeName,
            Date = i.Date,
            OccurrenceType = i.OccurrenceType,
            AnomalyReason = i.AnomalyReason,
            StatusCode = i.StatusCode,
            StatusLabel = MCStatusLabels.ItemLabel(i.StatusCode),
            ResolutionNote = i.OverrideReason,
            ResolvedBy = null, // V1: no user join for override
            ResolvedAtUtc = i.OverrideAtUtc
        });
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ProcessingLogDto>> GetLogsAsync(Guid runId)
    {
        var logs = await _context.MCProcessingLogs
            .Where(l => l.ProcessingRunId == runId)
            .AsNoTracking()
            .OrderBy(l => l.OccurredAtUtc)
            .ToListAsync();

        return logs.Select(l => new ProcessingLogDto
        {
            EventType = l.EventType,
            Message = l.Message,
            Actor = l.Actor,
            OccurredAtUtc = l.OccurredAtUtc
        });
    }

    // ─── Internal Helpers ────────────────────────────────────────────────

    private async Task RecalculateUnresolvedCountAsync(Guid runId)
    {
        var run = await _context.MCProcessingRuns.FindAsync(runId);
        if (run == null) return;

        run.UnresolvedCount = await _context.MCMonthlyChangeItems
            .Where(i => i.ProcessingRunId == runId && (i.StatusCode == "AUTO_CODED" || i.StatusCode == "NEEDS_REVIEW"))
            .CountAsync();
    }

    private async Task LogEventAsync(Guid runId, string eventType, string message, Guid? userId = null)
    {
        var log = new MCProcessingLog
        {
            Id = Guid.NewGuid(),
            ProcessingRunId = runId,
            EventType = eventType,
            Message = message,
            Actor = userId?.ToString(),
            OccurredAtUtc = DateTime.UtcNow
        };
        _context.MCProcessingLogs.Add(log);
        // SaveChanges called by the caller
    }

    private async Task<ProcessingRunDetailDto> BuildDetailDto(Guid runId, MCProcessingRun? run = null)
    {
        run ??= await _context.MCProcessingRuns
            .Include(r => r.CreatedByUser)
            .AsNoTracking()
            .FirstAsync(r => r.Id == runId);

        var summary = MapToSummaryDto(run);

        // Item status breakdown
        var statusCounts = await _context.MCMonthlyChangeItems
            .Where(i => i.ProcessingRunId == runId)
            .GroupBy(i => i.StatusCode)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        return new ProcessingRunDetailDto
        {
            Id = summary.Id,
            EntityId = summary.EntityId,
            EntityName = summary.EntityName,
            Year = summary.Year,
            Month = summary.Month,
            StatusCode = summary.StatusCode,
            StatusLabel = summary.StatusLabel,
            SyncedRowCount = summary.SyncedRowCount,
            OccurrenceCount = summary.OccurrenceCount,
            AnomalyCount = summary.AnomalyCount,
            UnresolvedCount = summary.UnresolvedCount,
            CreatedAtUtc = summary.CreatedAtUtc,
            CreatedByEmail = summary.CreatedByEmail,
            SyncedAtUtc = summary.SyncedAtUtc,
            DetectionCompletedAtUtc = summary.DetectionCompletedAtUtc,
            ErrorMessage = summary.ErrorMessage,
            ApprovedCount = statusCounts.FirstOrDefault(s => s.Status == "APPROVED")?.Count ?? 0,
            AdjustedCount = statusCounts.FirstOrDefault(s => s.Status == "ADJUSTED")?.Count ?? 0,
            ExcludedCount = statusCounts.FirstOrDefault(s => s.Status == "EXCLUDED")?.Count ?? 0,
            AutoCodedCount = statusCounts.FirstOrDefault(s => s.Status == "AUTO_CODED")?.Count ?? 0,
            NeedsReviewCount = statusCounts.FirstOrDefault(s => s.Status == "NEEDS_REVIEW")?.Count ?? 0
        };
    }

    private static ProcessingRunSummaryDto MapToSummaryDto(MCProcessingRun run)
    {
        return new ProcessingRunSummaryDto
        {
            Id = run.Id,
            EntityId = run.EntityId,
            EntityName = MCStatusLabels.EntityName(run.EntityId),
            Year = run.Year,
            Month = run.Month,
            StatusCode = run.StatusCode,
            StatusLabel = MCStatusLabels.RunLabel(run.StatusCode),
            SyncedRowCount = run.SyncedRowCount,
            OccurrenceCount = run.OccurrenceCount,
            AnomalyCount = run.AnomalyCount,
            UnresolvedCount = run.UnresolvedCount,
            CreatedAtUtc = run.CreatedAtUtc,
            CreatedByEmail = run.CreatedByUser?.Email,
            SyncedAtUtc = run.SyncedAtUtc,
            DetectionCompletedAtUtc = run.DetectionCompletedAtUtc,
            ErrorMessage = run.ErrorMessage
        };
    }

    private static MonthlyChangeItemDto MapToItemDto(MCMonthlyChangeItem item)
    {
        return new MonthlyChangeItemDto
        {
            Id = item.Id,
            EmployeeCode = item.EmployeeCode,
            EmployeeName = item.EmployeeName,
            Date = item.Date,
            OccurrenceType = item.OccurrenceType,
            DetectionRule = item.DetectionRule,
            DurationMinutes = item.DurationMinutes,
            ScheduleCode = item.ScheduleCode,
            StatusCode = item.StatusCode,
            StatusLabel = MCStatusLabels.ItemLabel(item.StatusCode),
            PrimaveraCode = item.PrimaveraCode,
            PrimaveraCodeDescription = item.PrimaveraCodeDescription,
            Hours = item.Hours,
            CostCenter = item.CostCenter,
            IsManualOverride = item.IsManualOverride,
            IsAnomaly = item.IsAnomaly,
            AnomalyReason = item.AnomalyReason,
            CreatedAtUtc = item.CreatedAtUtc
        };
    }
}
