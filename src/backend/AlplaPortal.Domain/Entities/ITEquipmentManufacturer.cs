namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Lookup table for IT equipment manufacturers (HP, Dell, Lenovo, etc.).
/// Provides dropdown values for the equipment form. Display names are saved
/// into the ITEquipment.Manufacturer text column for backward compatibility.
/// </summary>
public class ITEquipmentManufacturer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name (e.g. "HP", "Dell", "Lenovo"). Unique.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this manufacturer is available for selection in forms.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Sort order for display in dropdowns (lower = first).</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public ICollection<ITEquipmentModel> Models { get; set; } = new List<ITEquipmentModel>();
}
