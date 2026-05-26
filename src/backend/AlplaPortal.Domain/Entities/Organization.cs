namespace AlplaPortal.Domain.Entities;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid? ResponsibleUserId { get; set; }
    public User? ResponsibleUser { get; set; }
}

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    
    public Guid? FinalApproverUserId { get; set; }
    public User? FinalApproverUser { get; set; }
}

public class Plant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PortalCode { get; set; } // Internal/Local code
    public string? PrimaveraCode { get; set; } // ERP/Future integration code
    public string? TaxId { get; set; } // NIF/VAT
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Record origin for operational traceability.
    /// Values: "MANUAL", "SYNCED_PRIMAVERA"
    /// </summary>
    public string Origin { get; set; } = "MANUAL";

    /// <summary>
    /// Source Primavera company/database this record was synchronized from.
    /// Null for manually created records.
    /// </summary>
    public string? SourceCompany { get; set; }

    /// <summary>
    /// Timestamp of the last synchronization that touched this record.
    /// Null for manually created records.
    /// </summary>
    public DateTime? LastSyncedAtUtc { get; set; }

    // ─── Registration Lifecycle (Ficha de Fornecedor) ───

    /// <summary>
    /// Supplier registration status for the Ficha de Fornecedor workflow.
    /// Values: DRAFT, PENDING_COMPLETION, PENDING_APPROVAL, ADJUSTMENT_REQUESTED, ACTIVE, SUSPENDED, BLOCKED
    /// Existing records default to ACTIVE via migration.
    /// </summary>
    public string RegistrationStatus { get; set; } = "DRAFT";

    // ─── Approval Workflow (Phase 2 — DAF/DG two-step) ───

    /// <summary>
    /// User assigned as DAF approver (Stage 1). Mapped to "Area Approver" role.
    /// Resolved at submission time. Null until submitted for approval.
    /// </summary>
    public Guid? DafApproverId { get; set; }
    public User? DafApprover { get; set; }
    public DateTime? DafApprovedAtUtc { get; set; }

    /// <summary>
    /// User assigned as DG approver (Stage 2). Mapped to "Final Approver" role.
    /// Resolved at submission time. Null until submitted for approval.
    /// </summary>
    public Guid? DgApproverId { get; set; }
    public User? DgApprover { get; set; }
    public DateTime? DgApprovedAtUtc { get; set; }

    /// <summary>User who submitted the supplier for approval.</summary>
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }

    /// <summary>Comment from DAF or DG when requesting adjustment.</summary>
    public string? AdjustmentComment { get; set; }

    // ─── Address ───
    public string? Address { get; set; }

    // ─── Contact Person 1 ───
    public string? ContactName1 { get; set; }
    public string? ContactRole1 { get; set; }
    public string? ContactPhone1 { get; set; }
    public string? ContactEmail1 { get; set; }

    // ─── Contact Person 2 ───
    public string? ContactName2 { get; set; }
    public string? ContactRole2 { get; set; }
    public string? ContactPhone2 { get; set; }
    public string? ContactEmail2 { get; set; }

    // ─── Banking ───
    public string? BankAccountNumber { get; set; }
    public string? BankIban { get; set; }
    public string? BankSwift { get; set; }

    // ─── Commercial Terms (free-text for Phase 1) ───
    public string? PaymentTerms { get; set; }
    public string? PaymentMethod { get; set; }

    // ─── Audit Trail ───
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedByUserId { get; set; }
    public string? Notes { get; set; }

    // ─── Navigation: Required Documents ───
    public ICollection<SupplierDocument> Documents { get; set; } = new List<SupplierDocument>();

    // ─── Navigation: Audit Trail ───
    public ICollection<SupplierStatusHistory> StatusHistories { get; set; } = new List<SupplierStatusHistory>();
}

/// <summary>
/// Stores metadata for required supplier registration documents.
/// Physical storage: data/attachments/suppliers/{supplierId}/
/// Document types: CONTRIBUINTE, CERTIDAO_COMERCIAL, DIARIO_REPUBLICA, ALVARA
/// </summary>
public class SupplierDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    /// <summary>CONTRIBUINTE, CERTIDAO_COMERCIAL, DIARIO_REPUBLICA, ALVARA</summary>
    public string DocumentType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string? FileHash { get; set; }

    public DateTime UploadedAtUtc { get; set; }
    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

public class CostCenter
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Plant this Cost Center belongs to. Required — every CC maps to exactly one plant.</summary>
    public int PlantId { get; set; }
    public Plant Plant { get; set; } = null!;
}
