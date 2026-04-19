namespace AlplaPortal.Application.DTOs.Contracts;

// ─── List / Summary ───

public class ContractListItemDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public string ContractTypeName { get; set; } = string.Empty;
    public string ContractTypeCode { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public string? CounterpartyName { get; set; }
    public string? CompanyName { get; set; }
    public string? PlantName { get; set; }
    public string? DepartmentName { get; set; }
    public DateTime EffectiveDateUtc { get; set; }
    public DateTime ExpirationDateUtc { get; set; }
    public decimal? TotalContractValue { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int ObligationCount { get; set; }
    public int PendingObligationCount { get; set; }
}

public class ContractListResponseDto
{
    public List<ContractListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public ContractSummaryDto Summary { get; set; } = new();
}

public class ContractSummaryDto
{
    public int TotalContracts { get; set; }
    public int ActiveContracts { get; set; }
    public int ExpiringIn30Days { get; set; }
    public int DraftContracts { get; set; }
    public int SuspendedContracts { get; set; }
}

// ─── Detail ───

public class ContractDetailDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string StatusCode { get; set; } = string.Empty;

    // Classification
    public int ContractTypeId { get; set; }
    public string ContractTypeName { get; set; } = string.Empty;
    public string ContractTypeCode { get; set; } = string.Empty;

    // Counterparty
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? CounterpartyName { get; set; }

    // Org scope
    public int DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? PlantId { get; set; }
    public string? PlantName { get; set; }

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
    public string? CurrencyCode { get; set; }
    public string? PaymentTerms { get; set; }

    // Legal
    public string? GoverningLaw { get; set; }
    public string? TerminationClauses { get; set; }

    // OCR
    public bool OcrValidatedByUser { get; set; }

    // Audit
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }

    // Collections
    public List<ContractDocumentDto> Documents { get; set; } = new();
    public List<ObligationDto> Obligations { get; set; } = new();
    public List<ContractHistoryDto> Histories { get; set; } = new();
    public List<ContractAlertDto> Alerts { get; set; } = new();
}

// ─── Sub-entities ───

public class ContractDocumentDto
{
    public Guid Id { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string? Description { get; set; }
    public int VersionNumber { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string UploadedByUserName { get; set; } = string.Empty;
}

public class ContractHistoryDto
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? FromStatusCode { get; set; }
    public string? ToStatusCode { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string ActorUserName { get; set; } = string.Empty;
}

public class ContractAlertDto
{
    public Guid Id { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public DateTime TriggerDateUtc { get; set; }
    public bool IsDismissed { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    // Contract info for the alerts page
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? ContractTitle { get; set; }
    public string? ContractStatusCode { get; set; }
}

public class ObligationDto
{
    public Guid Id { get; set; }
    public int SequenceNumber { get; set; }
    public string? Description { get; set; }
    public decimal ExpectedAmount { get; set; }
    public int? CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime DueDateUtc { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    // Linked request info (looked up from Request table)
    public Guid? LinkedRequestId { get; set; }
    public string? LinkedRequestNumber { get; set; }
    public string? LinkedRequestStatusCode { get; set; }
}

// ─── Create / Update ───

public class CreateContractDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ContractTypeId { get; set; }
    public int? SupplierId { get; set; }
    public string? CounterpartyName { get; set; }
    public int DepartmentId { get; set; }
    public int CompanyId { get; set; }
    public int? PlantId { get; set; }
    public DateTime? SignedAtUtc { get; set; }
    public DateTime EffectiveDateUtc { get; set; }
    public DateTime ExpirationDateUtc { get; set; }
    public bool AutoRenew { get; set; }
    public int? RenewalNoticeDays { get; set; }
    public decimal? TotalContractValue { get; set; }
    public int? CurrencyId { get; set; }
    public string? PaymentTerms { get; set; }
    public string? GoverningLaw { get; set; }
    public string? TerminationClauses { get; set; }
}

public class UpdateContractDto : CreateContractDto
{
}

public class CreateObligationDto
{
    public string? Description { get; set; }
    public decimal ExpectedAmount { get; set; }
    public int? CurrencyId { get; set; }
    public DateTime DueDateUtc { get; set; }
}

public class UpdateObligationDto : CreateObligationDto
{
}

// ─── Actions ───

public class StatusTransitionDto
{
    public string? Comment { get; set; }
}

public class GenerateRequestResultDto
{
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
