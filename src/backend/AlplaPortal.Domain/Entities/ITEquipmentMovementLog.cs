namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Audit trail for every equipment lifecycle action.
/// Every status change, assignment, return, repair, loss, etc. creates an entry.
/// </summary>
public class ITEquipmentMovementLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquipmentId { get; set; }
    public ITEquipment? Equipment { get; set; }

    /// <summary>Action type code (CREATED, IMPORTED, ASSIGNED, RETURNED, etc.).</summary>
    public string MovementType { get; set; } = string.Empty;

    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? PreviousOwnerName { get; set; }
    public string? NewOwnerName { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
