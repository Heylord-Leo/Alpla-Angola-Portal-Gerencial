namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Defines an equipment type that can be managed dynamically through the admin UI.
/// Replaces the hard-coded EquipmentType constants for dropdown/filter purposes.
/// </summary>
public class ITEquipmentType
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Internal stable code (e.g. LAPTOP, MONITOR, MOUSE). Unique.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name in Portuguese (e.g. "Laptop", "Monitor", "Rato").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Short 3-letter code used exclusively in Asset Code generation (e.g. NBK, DSK, MON). Unique.</summary>
    public string ShortCode { get; set; } = string.Empty;

    /// <summary>Whether this type is available for selection in forms and filters.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Sort order for display in dropdowns (lower = first).</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
