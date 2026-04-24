namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Admin-configurable rule mapping an occurrence type (with optional conditions)
/// to a Primavera import code. Used by the OccurrenceCodingService (Layer 2).
///
/// Examples:
///   OccurrenceType=Absence, MinDuration=null → PrimaveraCode=FLTINJ, DefaultHoursFormula=duration_hours
///   OccurrenceType=Lateness, MinDuration=15  → PrimaveraCode=FLT, DefaultHoursFormula=0.25
///
/// Priority: Lower value = higher priority. When multiple rules match,
/// the one with the lowest Priority value wins.
///
/// Tier 2 rule governance: admin-managed, audited. Changes take effect
/// on the next processing run. Cannot retroactively affect closed runs.
/// </summary>
public class MCPrimaveraCodeMapping
{
    public int Id { get; set; }

    /// <summary>Occurrence type this rule applies to: Absence, JustifiedAbsence, Lateness, Anomaly.</summary>
    public string OccurrenceType { get; set; } = string.Empty;

    /// <summary>Minimum duration (inclusive) in minutes. Null = no minimum.</summary>
    public int? MinDurationMinutes { get; set; }

    /// <summary>Maximum duration (inclusive) in minutes. Null = no maximum.</summary>
    public int? MaxDurationMinutes { get; set; }

    /// <summary>The Primavera code to assign (e.g., "FLTINJ", "FLT").</summary>
    public string PrimaveraCode { get; set; } = string.Empty;

    /// <summary>Human-readable description of the Primavera code.</summary>
    public string PrimaveraCodeDescription { get; set; } = string.Empty;

    /// <summary>
    /// How to compute Hours for the export row.
    /// Possible values: "duration_hours" (converts DurationMinutes to decimal hours)
    /// or a fixed decimal string (e.g., "0.25", "8.00").
    /// </summary>
    public string? DefaultHoursFormula { get; set; }

    /// <summary>Restrict to a specific Innux entity. Null = all entities.</summary>
    public int? EntityId { get; set; }

    /// <summary>Restrict to a specific schedule code. Null = all schedules.</summary>
    public string? ScheduleCode { get; set; }

    /// <summary>Lower value = higher priority. Used when multiple rules match.</summary>
    public int Priority { get; set; } = 100;

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    // ─── Audit ───

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
