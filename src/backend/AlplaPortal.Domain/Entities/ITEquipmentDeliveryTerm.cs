namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Represents a grouped responsibility term for one employee receiving multiple IT equipment items.
/// One term = one PDF = one signature for multiple equipment items.
/// </summary>
public class ITEquipmentDeliveryTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Business identifier (TER-YYYY-NNNNN). Unique.</summary>
    public string TermNumber { get; set; } = string.Empty;

    // ── Employee Information ──

    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeEmail { get; set; }

    /// <summary>Nullable FK — only set when the employee exists as a portal user.</summary>
    public Guid? EmployeeUserId { get; set; }
    public User? EmployeeUser { get; set; }

    public string? EmployeeDepartment { get; set; }
    public string? EmployeePosition { get; set; }
    public string? EmployeePlant { get; set; }

    // ── Master Data References (nullable — old records may only have text fields) ──

    /// <summary>FK to the company from Master Data. Nullable for backward compatibility.</summary>
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>FK to the plant from Master Data. Nullable for backward compatibility.</summary>
    public int? EmployeePlantId { get; set; }
    public Plant? EmployeePlantRef { get; set; }

    /// <summary>FK to the department from Master Data. Nullable for backward compatibility.</summary>
    public int? EmployeeDepartmentId { get; set; }
    public Department? EmployeeDepartmentRef { get; set; }

    // ── Term Information ──

    public DateTime DeliveryDate { get; set; }

    /// <summary>Internal status code (DRAFT, GENERATED, SENT, SIGNED, PARTIALLY_RETURNED, CLOSED, CANCELLED).</summary>
    public string Status { get; set; } = "DRAFT";

    /// <summary>FK to the generated PDF document.</summary>
    public Guid? GeneratedDocumentId { get; set; }
    public ITEquipmentDocument? GeneratedDocument { get; set; }

    /// <summary>FK to the signed (uploaded) PDF document.</summary>
    public Guid? SignedDocumentId { get; set; }
    public ITEquipmentDocument? SignedDocument { get; set; }

    public string? Notes { get; set; }

    // ── Audit ──

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }

    // ── Navigation ──

    public ICollection<ITEquipmentDeliveryItem> Items { get; set; } = new List<ITEquipmentDeliveryItem>();
}
