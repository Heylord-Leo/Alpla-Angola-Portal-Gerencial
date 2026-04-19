namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Stores metadata for files uploaded to a contract.
/// Physical storage: data/attachments/contracts/{contractId}/
/// </summary>
public class ContractDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;

    /// <summary>ORIGINAL, AMENDMENT, ADDENDUM, ANNEX, OTHER</summary>
    public string DocumentType { get; set; } = "ORIGINAL";

    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }

    /// <summary>SHA-256 hash for deduplication.</summary>
    public string? FileHash { get; set; }

    public string? Description { get; set; }
    public int VersionNumber { get; set; } = 1;

    public DateTime UploadedAtUtc { get; set; }
    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;
}
