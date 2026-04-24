using AlplaPortal.Application.Interfaces.MonthlyChanges;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.MonthlyChanges;

/// <summary>
/// V1 Occurrence Detection Engine.
///
/// Reads MCAttendanceSnapshot rows for a processing run and produces
/// MCMonthlyChangeItem entries based on deterministic rules.
///
/// Detection rules (V1):
///   1. UNJUSTIFIED_ABSENCE — AbsenceMinutes > 0 AND JustifiedAbsenceMinutes = 0 AND NOT rest day
///   2. JUSTIFIED_ABSENCE  — JustifiedAbsenceMinutes > 0
///   3. LATENESS            — First entry after schedule start + threshold (configurable via MCDetectionThreshold)
///   4. ANOMALY             — AnomalyDescription is not null/empty
///
/// State assignment:
///   - Absence (unjustified/justified): AUTO_CODED — clear detection, but NO Primavera code yet (V1)
///   - Lateness: AUTO_CODED if within defined threshold range, NEEDS_REVIEW if anomalous
///   - Anomaly: NEEDS_REVIEW — requires human triage
///
/// Primavera code assignment is NOT done here (blocked until code catalog is provided).
/// Items with AUTO_CODED status are NOT directly exportable per Amendment §1.
/// </summary>
public class OccurrenceDetectionEngine : IOccurrenceDetectionEngine
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OccurrenceDetectionEngine> _logger;

    /// <summary>Default lateness threshold in minutes when no MCDetectionThreshold is configured.</summary>
    private const int DefaultLatenessThresholdMinutes = 15;

    public OccurrenceDetectionEngine(
        ApplicationDbContext context,
        ILogger<OccurrenceDetectionEngine> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(int OccurrenceCount, int AnomalyCount)> DetectOccurrencesAsync(
        Guid processingRunId, int entityId, CancellationToken ct = default)
    {
        _logger.LogInformation("MC Detection: Starting for RunId={RunId}, Entity={EntityId}", processingRunId, entityId);

        // ─── Clear any previous items (idempotent retry) ───
        var clearedCount = await _context.MCMonthlyChangeItems
            .Where(i => i.ProcessingRunId == processingRunId)
            .ExecuteDeleteAsync(ct);

        if (clearedCount > 0)
            _logger.LogWarning("MC Detection: Cleared {Count} existing items for run {RunId} (retry scenario)", clearedCount, processingRunId);

        // ─── Load snapshots ───
        var snapshots = await _context.MCAttendanceSnapshots
            .Where(s => s.ProcessingRunId == processingRunId)
            .AsNoTracking()
            .OrderBy(s => s.EmployeeCode)
            .ThenBy(s => s.Date)
            .ToListAsync(ct);

        if (snapshots.Count == 0)
        {
            _logger.LogWarning("MC Detection: No snapshots found for run {RunId}", processingRunId);
            return (0, 0);
        }

        // ─── Load thresholds for this entity ───
        var thresholds = await LoadThresholdsAsync(entityId, ct);

        // ─── Detect ───
        var items = new List<MCMonthlyChangeItem>();
        int anomalyCount = 0;

        foreach (var snap in snapshots)
        {
            var detected = DetectForSnapshot(snap, processingRunId, thresholds);
            foreach (var item in detected)
            {
                items.Add(item);
                if (item.IsAnomaly) anomalyCount++;
            }
        }

        // ─── Persist in batches ───
        const int batchSize = 500;
        for (int i = 0; i < items.Count; i += batchSize)
        {
            var batch = items.Skip(i).Take(batchSize);
            _context.MCMonthlyChangeItems.AddRange(batch);
            await _context.SaveChangesAsync(ct);
        }

        // ─── Count by occurrence type for diagnostics ───
        var typeCounts = items.GroupBy(i => i.OccurrenceType)
                              .ToDictionary(g => g.Key, g => g.Count());
        
        var typeCountsStr = string.Join(", ", typeCounts.Select(kv => $"{kv.Key}: {kv.Value}"));
        int snapshotsWithNoItems = snapshots.Count - items.Select(i => i.SnapshotId).Distinct().Count();

        _logger.LogInformation(
            "MC Detection: Completed Run {RunId}. Snapshots synced: {SnapCount}. Snapshots producing no items: {NoOpCount}.",
            processingRunId, snapshots.Count, snapshotsWithNoItems);
        
        _logger.LogInformation(
            "MC Detection: Generated {OccurrenceCount} items ({TypeCounts}). Anomalies: {AnomalyCount}.",
            items.Count, typeCountsStr, anomalyCount);

        return (items.Count, anomalyCount);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Detection Logic
    // ──────────────────────────────────────────────────────────────────────

    private List<MCMonthlyChangeItem> DetectForSnapshot(
        MCAttendanceSnapshot snap, Guid processingRunId, Dictionary<string, int> thresholds)
    {
        var results = new List<MCMonthlyChangeItem>();

        // Skip rest days — no occurrence detection needed
        if (snap.IsRestDay)
            return results;

        bool hasLateness = false;
        int latenessMinutes = 0;

        // Rule 1: Lateness
        // Requires schedule start and first entry
        if (!string.IsNullOrWhiteSpace(snap.FirstEntry) && !string.IsNullOrWhiteSpace(snap.ScheduleStartTime))
        {
            latenessMinutes = ComputeLatenessMinutes(snap.ScheduleStartTime, snap.FirstEntry);
            var threshold = GetThresholdForSchedule(snap.ScheduleCode, thresholds);

            if (latenessMinutes > threshold)
            {
                hasLateness = true;
                results.Add(CreateItem(snap, processingRunId,
                    occurrenceType: "LATENESS",
                    detectionRule: $"FirstEntry ({snap.FirstEntry}) > ScheduleStart ({snap.ScheduleStartTime}) + {threshold}min threshold",
                    durationMinutes: latenessMinutes,
                    statusCode: "AUTO_CODED",
                    isAnomaly: false));
            }
        }

        // Rule 2: Unjustified absence
        // If absence minutes > 0, we log it. 
        // To avoid overlapping duplicate items for the exact same minutes, we skip UNJUSTIFIED_ABSENCE 
        // if the absence is entirely explained by the lateness we just logged.
        if (snap.AbsenceMinutes > 0)
        {
            if (!hasLateness || snap.AbsenceMinutes > latenessMinutes)
            {
                results.Add(CreateItem(snap, processingRunId,
                    occurrenceType: "UNJUSTIFIED_ABSENCE",
                    detectionRule: "AbsenceMinutes > 0",
                    durationMinutes: snap.AbsenceMinutes,
                    statusCode: "AUTO_CODED",
                    isAnomaly: false));
            }
        }

        // Rule 3: Justified absence
        if (snap.JustifiedAbsenceMinutes > 0)
        {
            results.Add(CreateItem(snap, processingRunId,
                occurrenceType: "JUSTIFIED_ABSENCE",
                detectionRule: "JustifiedAbsenceMinutes > 0",
                durationMinutes: snap.JustifiedAbsenceMinutes,
                statusCode: "AUTO_CODED",
                isAnomaly: false));
        }

        // Rule 4: Anomaly
        if (!string.IsNullOrWhiteSpace(snap.AnomalyDescription))
        {
            if (results.Count == 0)
            {
                // If no other occurrence was generated, generate a standalone anomaly occurrence
                results.Add(CreateItem(snap, processingRunId,
                    occurrenceType: "ANOMALY",
                    detectionRule: $"Innux TipoAnomalia: {snap.AnomalyDescription}",
                    durationMinutes: 0,
                    statusCode: "NEEDS_REVIEW",
                    isAnomaly: true,
                    anomalyReason: snap.AnomalyDescription));
            }
            else
            {
                // Mark existing items as anomalous so they require human review
                foreach (var item in results)
                {
                    item.IsAnomaly = true;
                    item.AnomalyReason = snap.AnomalyDescription;
                    item.StatusCode = "NEEDS_REVIEW"; // Anomalies always need review
                }
            }
        }

        return results;
    }

    private static MCMonthlyChangeItem CreateItem(
        MCAttendanceSnapshot snap, Guid processingRunId,
        string occurrenceType, string detectionRule, int durationMinutes,
        string statusCode, bool isAnomaly,
        string? anomalyReason = null)
    {
        return new MCMonthlyChangeItem
        {
            Id = Guid.NewGuid(),
            ProcessingRunId = processingRunId,
            SnapshotId = snap.Id,
            EmployeeCode = snap.EmployeeCode,
            EmployeeName = snap.EmployeeName,
            Date = snap.Date,
            OccurrenceType = occurrenceType,
            DetectionRule = detectionRule,
            DurationMinutes = durationMinutes,
            ScheduleCode = snap.ScheduleCode,
            StatusCode = statusCode,
            // PrimaveraCode left empty — CodingService not implemented yet
            PrimaveraCode = null,
            PrimaveraCodeDescription = null,
            Hours = null,
            CostCenter = null, // Nullable per Amendment §4
            IsManualOverride = false,
            IsAnomaly = isAnomaly,
            AnomalyReason = anomalyReason,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Threshold / Lateness Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads MCDetectionThreshold entries for the given entity.
    /// Returns a dictionary keyed by ScheduleCode → ThresholdMinutes.
    /// If no thresholds are configured, falls back to DefaultLatenessThresholdMinutes.
    /// </summary>
    private async Task<Dictionary<string, int>> LoadThresholdsAsync(int entityId, CancellationToken ct)
    {
        var thresholds = await _context.MCDetectionThresholds
            .Where(t => t.EntityId == entityId && t.IsActive)
            .AsNoTracking()
            .ToListAsync(ct);

        return thresholds.ToDictionary(
            t => t.ScheduleCode ?? "__DEFAULT__",
            t => t.ThresholdMinutes);
    }

    private int GetThresholdForSchedule(string? scheduleCode, Dictionary<string, int> thresholds)
    {
        if (scheduleCode != null && thresholds.TryGetValue(scheduleCode, out var specific))
            return specific;

        if (thresholds.TryGetValue("__DEFAULT__", out var defaultThreshold))
            return defaultThreshold;

        return DefaultLatenessThresholdMinutes;
    }

    /// <summary>
    /// Computes lateness in minutes between schedule start and actual first entry.
    /// Both inputs are "HH:mm" strings. Returns 0 if employee arrived on time or early.
    /// </summary>
    private static int ComputeLatenessMinutes(string scheduleStart, string firstEntry)
    {
        if (!TimeSpan.TryParse(scheduleStart, out var scheduledTime))
            return 0;
        if (!TimeSpan.TryParse(firstEntry, out var actualTime))
            return 0;

        var diff = actualTime - scheduledTime;
        return diff.TotalMinutes > 0 ? (int)diff.TotalMinutes : 0;
    }
}
