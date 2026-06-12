namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Lookup table for IT equipment memory/RAM options (4 GB, 8 GB, 16 GB, etc.).
/// Display names are saved into the ITEquipment.MemoryRam text column.
/// </summary>
public class ITEquipmentMemoryOption
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name (e.g. "8 GB", "16 GB"). Unique.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Numeric value in GB for sorting/filtering. Null for "N/A".</summary>
    public int? ValueInGb { get; set; }

    /// <summary>Whether this option is available for selection in forms.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Sort order for display in dropdowns (lower = first).</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
