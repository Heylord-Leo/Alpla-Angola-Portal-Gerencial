namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Tracks who is using or used a specific equipment.
/// Only one ACTIVE assignment should exist per equipment at any time.
/// </summary>
public class ITEquipmentAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquipmentId { get; set; }
    public ITEquipment? Equipment { get; set; }

    /// <summary>Nullable FK — only set when the user exists in the portal.</summary>
    public Guid? AssignedToUserId { get; set; }
    public User? AssignedToUser { get; set; }

    /// <summary>Free-text name — always stored even if linked to a portal user.</summary>
    public string AssignedToName { get; set; } = string.Empty;

    /// <summary>Email of the person receiving the equipment — used for agreement email dispatch.</summary>
    public string? AssignedToEmail { get; set; }

    public string? AssignedToDepartment { get; set; }
    public string? AssignedToPlant { get; set; }

    public DateTime AssignedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedReturnDate { get; set; }
    public DateTime? ReturnedDate { get; set; }

    /// <summary>Internal code (ACTIVE, RETURNED, LOST, REPLACED, CANCELLED).</summary>
    public string AssignmentStatus { get; set; } = "ACTIVE";

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
