namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Represents an I.T equipment asset tracked in the inventory.
/// </summary>
public class ITEquipment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Primary business identifier — always unique and required.</summary>
    public string AssetTag { get; set; } = string.Empty;

    public string? Hostname { get; set; }
    public string? Plant { get; set; }

    /// <summary>Internal stable code (LAPTOP, DESKTOP, MONITOR, PRINTER, NVR, UNKNOWN).</summary>
    public string EquipmentType { get; set; } = "UNKNOWN";

    /// <summary>Internal stable status code (AVAILABLE, IN_USE, etc.).</summary>
    public string StatusCode { get; set; } = "AVAILABLE";

    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? MacAddress { get; set; }
    public string? Processor { get; set; }
    public string? MemoryRam { get; set; }
    public string? Color { get; set; }
    public bool BiometricMfaEnabled { get; set; }
    public string? IdCard { get; set; }
    public string? DevicePhotoUrl { get; set; }

    // Current owner snapshot (denormalized for quick list display)
    public string? CurrentOwnerName { get; set; }
    public Guid? CurrentOwnerUserId { get; set; }
    public User? CurrentOwnerUser { get; set; }
    public string? CurrentOwnerEmployeeId { get; set; }

    public string? Notes { get; set; }

    /// <summary>How the equipment entered the system (IMPORTED_LEGACY, MANUAL_PURCHASE, MANUAL_REGISTRATION).</summary>
    public string SourceType { get; set; } = "MANUAL_REGISTRATION";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }

    // Navigation properties
    public ICollection<ITEquipmentAssignment> Assignments { get; set; } = new List<ITEquipmentAssignment>();
    public ICollection<ITEquipmentMovementLog> MovementLogs { get; set; } = new List<ITEquipmentMovementLog>();
    public ITEquipmentAcquisition? Acquisition { get; set; }
    public ICollection<ITEquipmentDocument> Documents { get; set; } = new List<ITEquipmentDocument>();
}
