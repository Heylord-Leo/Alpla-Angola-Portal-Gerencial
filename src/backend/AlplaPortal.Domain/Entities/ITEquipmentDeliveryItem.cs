namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Represents one equipment item inside a grouped delivery term.
/// Links to the equipment record and (after confirmation) to the assignment record.
/// </summary>
public class ITEquipmentDeliveryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DeliveryTermId { get; set; }
    public ITEquipmentDeliveryTerm? DeliveryTerm { get; set; }

    public Guid EquipmentId { get; set; }
    public ITEquipment? Equipment { get; set; }

    /// <summary>Set after the delivery term is confirmed/generated. Links to the assignment created during confirmation.</summary>
    public Guid? AssignmentId { get; set; }
    public ITEquipmentAssignment? Assignment { get; set; }

    /// <summary>Item-level status (PENDING, DELIVERED, RETURNED, REPLACED, LOST, RETIRED).</summary>
    public string ItemStatus { get; set; } = "PENDING";

    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReturnedAt { get; set; }

    /// <summary>Return condition code (GOOD, DAMAGED, NEEDS_REPAIR). Only set when the item is returned.</summary>
    public string? ReturnCondition { get; set; }

    public string? Notes { get; set; }
}
