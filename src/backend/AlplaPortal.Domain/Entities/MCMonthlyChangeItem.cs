namespace AlplaPortal.Domain.Entities;

/// <summary>
/// The central domain entity of the HR Monthly Changes Middleware.
/// Represents one coded occurrence line ready for Primavera export.
///
/// Lifecycle: AUTO_CODED → APPROVED / ADJUSTED / EXCLUDED → EXPORTED (locked)
///            NEEDS_REVIEW → APPROVED / ADJUSTED / EXCLUDED → EXPORTED (locked)
///
/// Key rule: AUTO_CODED items are NOT directly exportable — they must be
/// explicitly approved (individually or via bulk-approve) before export generation.
/// Only APPROVED and ADJUSTED items are collected into export batches.
///
/// Each item traces back to exactly one MCAttendanceSnapshot row,
/// providing full audit from Innux source → detection rule → code assignment → export.
/// </summary>
public class MCMonthlyChangeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Parent References ───

    public Guid ProcessingRunId { get; set; }
    public MCProcessingRun? ProcessingRun { get; set; }

    public Guid SnapshotId { get; set; }
    public MCAttendanceSnapshot? Snapshot { get; set; }

    // ─── Employee Context (denormalized for query performance) ───

    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime Date { get; set; }

    // ─── Layer 1: Detection ───

    /// <summary>
    /// Classified occurrence type: Absence, JustifiedAbsence, Lateness, Anomaly.
    /// Set by the OccurrenceDetectionEngine.
    /// </summary>
    public string OccurrenceType { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable explanation of why this was detected.
    /// E.g., "AbsenceMinutes=480, JustifiedAbsenceMinutes=0" or "Entry 08:22 > threshold 08:15 for schedule TN".
    /// </summary>
    public string DetectionRule { get; set; } = string.Empty;

    /// <summary>Duration of the occurrence in minutes.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Schedule code active on this day.</summary>
    public string? ScheduleCode { get; set; }

    // ─── Lifecycle ───

    /// <summary>
    /// Status codes: AUTO_CODED, NEEDS_REVIEW, APPROVED, ADJUSTED, EXCLUDED, EXPORTED.
    /// </summary>
    public string StatusCode { get; set; } = "AUTO_CODED";

    // ─── Layer 2: Coding ───

    /// <summary>Primavera occurrence code assigned by OccurrenceCodingService or HR override.</summary>
    public string? PrimaveraCode { get; set; }

    /// <summary>Human-readable description of the Primavera code.</summary>
    public string? PrimaveraCodeDescription { get; set; }

    /// <summary>Hours value for the export row. Nullable in V1 — required for export.</summary>
    public decimal? Hours { get; set; }

    /// <summary>Cost center for the export row. Explicitly optional in V1.</summary>
    public string? CostCenter { get; set; }

    // ─── Override Tracking ───

    public bool IsManualOverride { get; set; }

    /// <summary>Preserved pre-override code for audit trail.</summary>
    public string? OriginalPrimaveraCode { get; set; }

    /// <summary>Preserved pre-override hours for audit trail.</summary>
    public decimal? OriginalHours { get; set; }

    public string? OverrideBy { get; set; }
    public DateTime? OverrideAtUtc { get; set; }
    public string? OverrideReason { get; set; }

    // ─── Anomaly ───

    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
    public string? ResolutionNote { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }

    // ─── Exclusion ───

    public string? ExclusionReason { get; set; }
    public string? ExcludedBy { get; set; }
    public DateTime? ExcludedAtUtc { get; set; }

    // ─── Audit ───

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
