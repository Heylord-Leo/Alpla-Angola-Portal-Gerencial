namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Lookup table for IT equipment models (HP ProBook 440 G10, Dell Latitude 5440, etc.).
/// Linked to a manufacturer and optionally to an equipment type code.
/// Display names are saved into the ITEquipment.Model text column.
/// </summary>
public class ITEquipmentModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the manufacturer that produces this model.</summary>
    public Guid ManufacturerId { get; set; }
    public ITEquipmentManufacturer Manufacturer { get; set; } = null!;

    /// <summary>
    /// Optional equipment type code (e.g. "LAPTOP", "MONITOR") to allow
    /// filtering models by type. Stored as the type code string, not a hard FK.
    /// </summary>
    public string? EquipmentTypeCode { get; set; }

    /// <summary>Display name (e.g. "ProBook 440 G10"). Unique per manufacturer.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this model is available for selection in forms.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Sort order for display in dropdowns (lower = first).</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
