namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Configurable leave/absence type.
/// Managed by HR/Admin in the HR Administration area.
/// Seed data provides the initial set of common types.
/// </summary>
public class LeaveType
{
    public int Id { get; set; }

    /// <summary>Stable internal code (VACATION, SICK_LEAVE, etc.).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Portuguese display label.</summary>
    public string DisplayNamePt { get; set; } = string.Empty;

    /// <summary>CSS color for calendar rendering (hex).</summary>
    public string? Color { get; set; }

    /// <summary>Whether this type counts against annual leave balance (Phase 2 entitlement).</summary>
    public bool CountsAgainstBalance { get; set; }

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}
