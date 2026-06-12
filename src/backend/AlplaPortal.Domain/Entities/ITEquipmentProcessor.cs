namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Lookup table for IT equipment processors (Intel Core i5, AMD Ryzen 7, etc.).
/// Display names are saved into the ITEquipment.Processor text column.
/// </summary>
public class ITEquipmentProcessor
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name (e.g. "Intel Core i5", "AMD Ryzen 7"). Unique.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this processor is available for selection in forms.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Sort order for display in dropdowns (lower = first).</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
