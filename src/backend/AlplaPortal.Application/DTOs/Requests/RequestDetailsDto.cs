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
    public string? CurrencyCode { get; set; }

    public DateTime RequestedDateUtc { get; set; }
    public DateTime? NeedByDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    
    public bool IsCancelled { get; set; }
    public Guid? SelectedQuotationId { get; set; }

    public List<RequestLineItemDto> LineItems { get; set; } = new();
    public List<RequestAttachmentDto> Attachments { get; set; } = new();
    public List<RequestStatusHistoryDto> StatusHistory { get; set; } = new();
    public List<SavedQuotationDto> Quotations { get; set; } = new();
}

public class RequestLineItemDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public string ItemPriority { get; set; } = "MEDIUM";
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
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
}

public class RequestStatusHistoryDto
{
    public Guid Id { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
    public string NewStatusName { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string ActorName { get; set; } = string.Empty;
}
