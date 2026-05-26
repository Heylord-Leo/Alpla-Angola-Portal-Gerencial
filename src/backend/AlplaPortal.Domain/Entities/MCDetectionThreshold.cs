namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Admin-configurable detection threshold for the OccurrenceDetectionEngine (Layer 1).
/// Currently supports lateness thresholds; extensible for future types.
///
/// Example:
///   ThresholdType=Lateness, ScheduleCode=TN, ThresholdMinutes=15
///   → Arrivals within 15 minutes of scheduled start are not flagged as late.
///
/// Tier 2 rule governance: admin-managed, audited. Changes take effect
/// on the next processing run. Cannot retroactively affect closed runs.
/// </summary>
public class MCDetectionThreshold
{
    public int Id { get; set; }

    /// <summary>Type of threshold. V1 supports: "Lateness".</summary>
    public string ThresholdType { get; set; } = "Lateness";

    /// <summary>Restrict to a specific schedule code. Null = default for all schedules.</summary>
    public string? ScheduleCode { get; set; }

    /// <summary>Restrict to a specific Innux entity. Null = all entities.</summary>
    public int? EntityId { get; set; }

    /// <summary>Threshold value in minutes.</summary>
    public int ThresholdMinutes { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    // ─── Audit ───

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
