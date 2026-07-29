using System.Text.Json.Serialization;
using AlplaPortal.Application.Serialization;

namespace AlplaPortal.Application.DTOs.Requests;

public class RequestDetailsDto
{
    public Guid Id { get; set; }
    public string? RequestNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    
    public int StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public int StatusDisplayOrder { get; set; }
    public string StatusBadgeColor { get; set; } = string.Empty;

    /// <summary>
    /// Computed workflow state for QUOTATION requests (not persisted).
    /// For PAYMENT requests, mirrors StatusCode.
    /// Values: QUOTATION_IN_PROGRESS, PARTIALLY_IN_APPROVAL, QUOTATION_IN_APPROVAL,
    /// PARTIALLY_APPROVED, PARTIALLY_PO_ISSUED, MIXED_PROCESSING,
    /// COMPLETED_WITH_CLOSURES, FULLY_COMPLETED, or legacy status code.
    /// </summary>
    public string? DisplayWorkflowState { get; set; }

    public int RequestTypeId { get; set; }
    public string RequestTypeName { get; set; } = string.Empty;
    public string RequestTypeCode { get; set; } = string.Empty;

    public int? NeedLevelId { get; set; }
    public string? NeedLevelName { get; set; }

    public int? CurrencyId { get; set; }
    public int? CapexOpexClassificationId { get; set; }

    public Guid RequesterId { get; set; }
    public string RequesterName { get; set; } = string.Empty;

    public Guid? BuyerId { get; set; }
    public string? BuyerName { get; set; }

    public Guid? AreaApproverId { get; set; }
    public string? AreaApproverName { get; set; }

    /// <summary>
    /// Phase B (DepartmentManager routing): while the request awaits area approval and
    /// nobody has decided yet (AreaApproverId null), the names of the managers eligible
    /// to decide — feeds the "Pendente — N responsáveis elegíveis" display.
    /// Null for other statuses / already-decided requests.
    /// </summary>
    public List<string>? EligibleAreaManagerNames { get; set; }

    public Guid? FinalApproverId { get; set; }
    public string? FinalApproverName { get; set; }

    public int DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public int? PlantId { get; set; }
    public string? PlantName { get; set; }

    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierPortalCode { get; set; }

    public decimal EstimatedTotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? CurrencyCode { get; set; }

    // B2P: Payment Condition
    public string? PaymentConditionCode { get; set; }
    public decimal? AdvancePaymentPercent { get; set; }
    public string? PaymentConditionSource { get; set; }

    public DateTime RequestedDateUtc { get; set; }
    public DateTime? NeedByDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    
    public bool IsCancelled { get; set; }
    public Guid? SelectedQuotationId { get; set; }

    public List<RequestLineItemDto> LineItems { get; set; } = new();
    public List<RequestPoGroupDto> PoGroups { get; set; } = new();
    public List<RequestAttachmentDto> Attachments { get; set; } = new();
    public List<RequestStatusHistoryDto> StatusHistory { get; set; } = new();
    public List<SavedQuotationDto> Quotations { get; set; } = new();
    public List<RequestApprovalBatchDto> ApprovalBatches { get; set; } = new();
}

public class RequestApprovalBatchDto
{
    public Guid Id { get; set; }
    public int BatchNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Budget justification recorded by the Area Approver when the batch was approved with a critical/over-budget cost center. Shown to the Final Approver.</summary>
    public string? BudgetJustification { get; set; }
    /// <summary>Snapshot of the batch total at final approval (null until then).</summary>
    public decimal? ApprovedTotalAmount { get; set; }

    public Guid CreatedByUserId { get; set; }
    /// <summary>Batch creator (the Buyer). Enriched post-query.</summary>
    public string? CreatedByUserName { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    /// <summary>Last actor on the batch — after area approval, the Area Approver. Enriched post-query.</summary>
    public string? UpdatedByUserName { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public List<RequestApprovalBatchItemDto> Items { get; set; } = new();

    /// <summary>Genuine EXTRA_ITEM lines the Buyer explicitly decided not to include in this batch. Read-only, excluded from totals.</summary>
    public List<BatchInformationalItemDto> ExcludedExtraItems { get; set; } = new();

    /// <summary>IGNORED-status lines from the batch's contributing quotation(s) — a complete, valid, already-justified terminal state. Read-only, excluded from totals.</summary>
    public List<BatchInformationalItemDto> IgnoredLines { get; set; } = new();

    /// <summary>Genuine EXTRA_ITEM lines with no recorded buyer decision — only possible for batches created before this rule existed. Blocks Area Approval progression when non-empty.</summary>
    public List<BatchInformationalItemDto> UnresolvedLegacyLines { get; set; } = new();

    /// <summary>Normalized, lot-aware view model for the Final Approval screen: authoritative item
    /// line totals (from the selected quotation item, never the RequestLineItem estimate), lot total,
    /// supplier resolution and included-vs-ignored separation. Computed server-side; null when the
    /// batch has no items to value.</summary>
    public FinalApprovalLotViewDto? LotView { get; set; }
}

public class RequestApprovalBatchItemDto
{
    public Guid Id { get; set; }
    public Guid RequestLineItemId { get; set; }
    public Guid SelectedQuotationItemId { get; set; }
}

public class RequestLineItemDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public string ItemPriority { get; set; } = "MEDIUM";
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? SupplierName { get; set; }
    public string? Notes { get; set; }
    // Item Status — read-only, backend-controlled
    public string? LineItemStatusCode { get; set; }
    public string? LineItemStatusName { get; set; }
    public string? LineItemStatusBadgeColor { get; set; }

    // Catalog linkage — null for manual/free-text items
    public int? ItemCatalogId { get; set; }
    public string? ItemCatalogCode { get; set; }

    // Quotation lifecycle (partial/batch approval flow) — read-only, backend-controlled
    public string? QuotationLifecycleStatus { get; set; }
    public string? NotQuotedJustification { get; set; }
    public string? NotQuotedProposedByName { get; set; }
    public DateTime? NotQuotedProposedAtUtc { get; set; }

    // Receiving Fields
    public decimal ReceivedQuantity { get; set; }
    public string? DivergenceNotes { get; set; }

    public int? PlantId { get; set; }
    public string? PlantName { get; set; }

    public int? CostCenterId { get; set; }
    public string? CostCenterName { get; set; }
    public string? CostCenterCode { get; set; }

    public int? IvaRateId { get; set; }
    public string? IvaRateCode { get; set; }
    public string? IvaRateName { get; set; }
    public decimal? IvaRatePercent { get; set; }

    public int? SupplierId { get; set; }
    public int? CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime? DueDate { get; set; }
    
    public Guid? RequestPoGroupId { get; set; }
    public Guid? SelectedQuotationItemId { get; set; }

    /// <summary>How this line was created. Null = standard requester/create flow. See LineItemCreationOrigins.</summary>
    public string? CreationOrigin { get; set; }

    public List<RequestLineItemAllocationDto> Allocations { get; set; } = new();
}

public class RequestLineItemAllocationDto
{
    public Guid Id { get; set; }
    public int PlantId { get; set; }
    public string? PlantName { get; set; }
    public int? CostCenterId { get; set; }
    public string? CostCenterName { get; set; }
    public string? CostCenterCode { get; set; }
    public decimal Percentage { get; set; }
    public int AllocationOrder { get; set; }
}

public class RequestAttachmentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public decimal FileSizeMBytes { get; set; }
    public string AttachmentTypeCode { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; }
    public string UploadedByName { get; set; } = string.Empty;

    // ── Void (correction workflows, e.g. Finance cancel-schedule) ──
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }
}

public class RequestStatusHistoryDto
{
    public Guid Id { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
    public string NewStatusName { get; set; } = string.Empty;
    public string? Comment { get; set; }
    [JsonConverter(typeof(UtcDateTimeJsonConverter))]
    public DateTime CreatedAtUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public List<RequestFieldChangeHistoryDto> FieldChanges { get; set; } = new();
}

public class RequestFieldChangeHistoryDto
{
    public Guid Id { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string FieldDisplayName { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string StatusCodeAtChange { get; set; } = string.Empty;
    public Guid? LineItemId { get; set; }
    [JsonConverter(typeof(UtcDateTimeJsonConverter))]
    public DateTime CreatedAtUtc { get; set; }
    public string ActorName { get; set; } = string.Empty;
}

