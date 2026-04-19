namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Primary aggregate root for the Contracts module.
/// Represents a legal agreement with a supplier/counterparty.
/// Status lifecycle: DRAFT → [UNDER_REVIEW] → ACTIVE → SUSPENDED/EXPIRED/TERMINATED.
/// </summary>
public class Contract
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Auto-generated at draft creation: CTR-YYYY-NNNN</summary>
    public string ContractNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Classification
    public int ContractTypeId { get; set; }
    public ContractType ContractType { get; set; } = null!;

    // Status lifecycle (string code — matches LeaveRecord pattern)
    public string StatusCode { get; set; } = "DRAFT";

    // Counterparty
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Free-text fallback when no Supplier record is matched.</summary>
    public string? CounterpartyName { get; set; }

    // Organizational scope
    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Null = company-wide contract (applies to all plants).</summary>
    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

    // Dates
    public DateTime? SignedAtUtc { get; set; }
    public DateTime EffectiveDateUtc { get; set; }
    public DateTime ExpirationDateUtc { get; set; }
    public DateTime? TerminatedAtUtc { get; set; }

    // Renewal
    public bool AutoRenew { get; set; }
    public int? RenewalNoticeDays { get; set; }

    // Financial
    public decimal? TotalContractValue { get; set; }
    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public string? PaymentTerms { get; set; }

    // Legal
    public string? GoverningLaw { get; set; }
    public string? TerminationClauses { get; set; }

    // OCR Tracking
    public string? OcrExtractionBatchId { get; set; }
    public bool OcrValidatedByUser { get; set; }

    // Audit
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    // Navigation collections
    public ICollection<ContractDocument> Documents { get; set; } = new List<ContractDocument>();
    public ICollection<ContractHistory> Histories { get; set; } = new List<ContractHistory>();
    public ICollection<ContractPaymentObligation> PaymentObligations { get; set; } = new List<ContractPaymentObligation>();
    public ICollection<ContractAlert> Alerts { get; set; } = new List<ContractAlert>();
    public ICollection<Request> LinkedRequests { get; set; } = new List<Request>();
}
