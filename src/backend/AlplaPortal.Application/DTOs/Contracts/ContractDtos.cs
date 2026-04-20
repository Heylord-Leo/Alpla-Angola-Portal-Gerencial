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

    /// <summary>
    /// True when this contract (in DRAFT status) was previously returned by a technical or final approver.
    /// Derived from ContractHistories — no schema migration required.
    /// Used by the Contracts list to visually differentiate returned drafts from new ones.
    /// </summary>
    public bool WasReturnedFromApproval { get; set; }
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
    /// <summary>Legacy free-text payment conditions. Kept for backwards compatibility.</summary>
    public string? PaymentTerms { get; set; }

    // ── Payment Rule (DEC-117) ───────────────────────────────────────────────────────
    public string? PaymentTermTypeCode { get; set; }
    public string? ReferenceEventTypeCode { get; set; }
    public int? PaymentTermDays { get; set; }
    public int? PaymentFixedDay { get; set; }
    public bool AllowsManualDueDateOverride { get; set; }
    public int? GracePeriodDays { get; set; }
    public bool HasLatePenalty { get; set; }
    public string? LatePenaltyTypeCode { get; set; }
    public decimal? LatePenaltyValue { get; set; }
    public bool HasLateInterest { get; set; }
    public string? LateInterestTypeCode { get; set; }
    public decimal? LateInterestValue { get; set; }
    /// <summary>Auto-generated for structured types; manually authored for CUSTOM_TEXT.</summary>
    public string? PaymentRuleSummary { get; set; }
    public string? FinancialNotes { get; set; }
    public string? PenaltyNotes { get; set; }

    // Legal
    public string? GoverningLaw { get; set; }
    public string? TerminationClauses { get; set; }

    // OCR
    public bool OcrValidatedByUser { get; set; }

    // ── Two-step approval participants (DEC-118) ─────────────────────────────────────
    public Guid? TechnicalApproverId { get; set; }
    public string? TechnicalApproverName { get; set; }
    public Guid? FinalApproverId { get; set; }
    public string? FinalApproverName { get; set; }

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

    // ── Due date tracking (DEC-117) ──────────────────────────────────────────────────
    public DateTime? ReferenceDateUtc { get; set; }
    public DateTime? CalculatedDueDateUtc { get; set; }
    /// <summary>Effective due date — auto-computed or manually set.</summary>
    public DateTime DueDateUtc { get; set; }
    /// <summary>AUTO_FROM_CONTRACT | MANUAL_OVERRIDE | null (pre-feature records)</summary>
    public string? DueDateSourceCode { get; set; }
    public DateTime? GraceDateUtc { get; set; }
    public DateTime? PenaltyStartDateUtc { get; set; }

    // ── Operational ──────────────────────────────────────────────────────────────────
    public DateTime? InvoiceReceivedDateUtc { get; set; }
    public DateTime? ServiceAcceptanceDateUtc { get; set; }
    public string? BillingReference { get; set; }
    public string? ObligationNotes { get; set; }

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

    // ── Payment Rule (DEC-117) ───────────────────────────────────────────────────────
    public string? PaymentTermTypeCode { get; set; }
    public string? ReferenceEventTypeCode { get; set; }
    public int? PaymentTermDays { get; set; }
    public int? PaymentFixedDay { get; set; }
    public bool AllowsManualDueDateOverride { get; set; }
    public int? GracePeriodDays { get; set; }
    public bool HasLatePenalty { get; set; }
    public string? LatePenaltyTypeCode { get; set; }
    public decimal? LatePenaltyValue { get; set; }
    public bool HasLateInterest { get; set; }
    public string? LateInterestTypeCode { get; set; }
    public decimal? LateInterestValue { get; set; }
    /// <summary>
    /// Manually authored text for CUSTOM_TEXT rule type.
    /// Auto-generated by the backend for structured rule types (do not send for those).
    /// </summary>
    public string? PaymentRuleSummary { get; set; }
    public string? FinancialNotes { get; set; }
    public string? PenaltyNotes { get; set; }

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

    // ── Due date inputs ──────────────────────────────────────────────────────────────
    /// <summary>
    /// The due date to use. Required only when the contract rule is MANUAL, ADVANCE_PAYMENT,
    /// CUSTOM_TEXT, or when auto-calculation was not possible due to missing reference data.
    /// When auto-calculation is successful, this field is ignored in favour of the calculated result
    /// UNLESS the contract allows manual override (AllowsManualDueDateOverride = true).
    /// </summary>
    public DateTime? DueDateUtc { get; set; }

    /// <summary>
    /// Manual reference date — required when the contract's ReferenceEventTypeCode = MANUAL_REFERENCE_DATE.
    /// </summary>
    public DateTime? ManualReferenceDateUtc { get; set; }

    /// <summary>
    /// Invoice received date — required when the contract's ReferenceEventTypeCode = INVOICE_RECEIVED_DATE.
    /// </summary>
    public DateTime? InvoiceReceivedDateUtc { get; set; }

    public DateTime? ServiceAcceptanceDateUtc { get; set; }
    public string? BillingReference { get; set; }
    public string? ObligationNotes { get; set; }
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

// ─── Lookups ───

public class PaymentTermTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Whether due date is auto-calculated (false = user must enter manually).</summary>
    public bool IsAutoCalculated { get; set; }
    /// <summary>Whether a reference event field is relevant for this term type.</summary>
    public bool RequiresReferenceEvent { get; set; }
    /// <summary>Whether PaymentTermDays field is relevant for this term type.</summary>
    public bool RequiresDays { get; set; }
    /// <summary>Whether PaymentFixedDay field is relevant for this term type.</summary>
    public bool RequiresFixedDay { get; set; }
}

public class ReferenceEventTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Whether the user must provide the reference date manually at obligation level.</summary>
    public bool RequiresUserInput { get; set; }
}

// ─── Contract Approval Queue (DEC-118) ───

/// <summary>Response DTO for GET /api/v1/contracts/pending-approvals.</summary>
public class PendingContractApprovalsResponseDto
{
    /// <summary>Contracts awaiting Stage 1 technical review.</summary>
    public List<ContractApprovalItemDto> TechnicalApprovals { get; set; } = new();
    /// <summary>Contracts awaiting Stage 2 final approval.</summary>
    public List<ContractApprovalItemDto> FinalApprovals { get; set; } = new();
}

/// <summary>Lean projection used in the contract approval queue list.</summary>
public class ContractApprovalItemDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public string ContractTypeName { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public string? SupplierPortalCode { get; set; }
    public string? CounterpartyName { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? PlantName { get; set; }
    public decimal? TotalContractValue { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime EffectiveDateUtc { get; set; }
    public DateTime ExpirationDateUtc { get; set; }
    public string? PaymentRuleSummary { get; set; }
    public Guid? TechnicalApproverId { get; set; }
    public string? TechnicalApproverName { get; set; }
    public Guid? FinalApproverId { get; set; }
    public string? FinalApproverName { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
