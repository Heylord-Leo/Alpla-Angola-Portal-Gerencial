namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Document attached to an equipment record and/or its acquisition.
/// Follows the same physical file storage pattern as RequestAttachment.
/// </summary>
public class ITEquipmentDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquipmentId { get; set; }
    public ITEquipment? Equipment { get; set; }

    /// <summary>Nullable link to the acquisition record if this document is purchase-related.</summary>
    public Guid? AcquisitionId { get; set; }
    public ITEquipmentAcquisition? Acquisition { get; set; }

    /// <summary>Nullable link to the assignment record if this document is assignment-related (e.g. responsibility agreement).</summary>
    public Guid? AssignmentId { get; set; }
    public ITEquipmentAssignment? Assignment { get; set; }

    /// <summary>Document type code (PAYMENT_PROOF, INVOICE, PROFORMA, ASSIGNMENT_AGREEMENT, etc.).</summary>
    public string DocumentType { get; set; } = "OTHER";

    public string FileName { get; set; } = string.Empty;

    /// <summary>Physical file name on disk (GUID + extension, stored in data/attachments/it-equipment/).</summary>
    public string StorageReference { get; set; } = string.Empty;

    /// <summary>SHA256 hash of file content for dedup detection.</summary>
    public string? FileHash { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public Guid? UploadedByUserId { get; set; }
    public User? UploadedByUser { get; set; }

    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
}
