namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Represents an I.T equipment asset tracked in the inventory.
/// </summary>
public class ITEquipment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Auto-generated official Asset Code (e.g. APA-AOVIA1-IT-LAP-000001). Unique, immutable after creation.</summary>
    public string AssetTag { get; set; } = string.Empty;

    /// <summary>Optional manual/legacy patrimony code for backward compatibility.</summary>
    public string? LegacyAssetCode { get; set; }

    /// <summary>Sequence number used in AssetTag generation. Scoped per Company+Plant+EquipmentType.</summary>
    public int SequenceNumber { get; set; }

    // ── FK to master data for Asset Code generation ──
    public int? CompanyId { get; set; }
    public Company? CompanyRef { get; set; }
    public int? PlantId { get; set; }
    public Plant? PlantRef { get; set; }

    /// <summary>Denormalized snapshot of Company.Code at creation time (e.g. "APA"). Immutable.</summary>
    public string? CompanyCode { get; set; }
    /// <summary>Denormalized snapshot of Plant.Code at creation time (e.g. "AOVIA1"). Immutable.</summary>
    public string? PlantCode { get; set; }
    /// <summary>Denormalized snapshot of ITEquipmentType.ShortCode at creation time (e.g. "LAP"). Immutable.</summary>
    public string? EquipmentTypeShortCode { get; set; }

    /// <summary>QR Code URL pointing to the asset detail page.</summary>
    public string? QrCodeUrl { get; set; }

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
    public string? WifiMacAddress { get; set; }
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

    /// <summary>Date when the equipment was manufactured. Used for lifecycle/replacement planning.</summary>
    public DateTime? ManufactureDate { get; set; }

    /// <summary>How the equipment entered the system (IMPORTED_LEGACY, MANUAL_PURCHASE, MANUAL_REGISTRATION).</summary>
    public string SourceType { get; set; } = "MANUAL_REGISTRATION";

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, the equipment registration is incomplete because the mandatory purchase document
    /// has not been uploaded yet. Equipment with this flag cannot be assigned or added to delivery terms.
    /// Automatically cleared when a PURCHASE_DOCUMENT is uploaded via ITEquipmentDocumentsController.
    /// </summary>
    public bool PurchaseDocumentPending { get; set; } = false;

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
