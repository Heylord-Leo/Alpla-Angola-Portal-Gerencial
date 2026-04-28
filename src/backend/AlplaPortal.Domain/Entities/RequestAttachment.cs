namespace AlplaPortal.Domain.Entities;

public class RequestAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public decimal FileSizeMBytes { get; set; }
    public string? FileHash { get; set; }
    
    public string AttachmentTypeCode { get; set; } = string.Empty; // e.g., PROFORMA, PO, PAYMENT_PROOF, PAYMENT_SCHEDULE
    
    // Constants for Type Codes
    public const string TYPE_PROFORMA = "PROFORMA";
    public const string TYPE_PO = "PO";
    public const string TYPE_PAYMENT_PROOF = "PAYMENT_PROOF";
    public const string TYPE_PAYMENT_SCHEDULE = "PAYMENT_SCHEDULE";
    public const string TYPE_RECEIPT = "RECEIPT";

    public string StorageReference { get; set; } = string.Empty; // Path pointer

    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public DateTime UploadedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
}
