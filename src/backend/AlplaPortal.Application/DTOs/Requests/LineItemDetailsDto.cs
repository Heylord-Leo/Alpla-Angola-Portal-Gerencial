namespace AlplaPortal.Application.DTOs.Requests;

public class LineItemDetailsDto
{
    // Item Fields
    public Guid? LineItemId { get; set; }
    public int LineNumber { get; set; }
    public string ItemDescription { get; set; } = string.Empty;
    public string? ItemPriority { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Parent Request Fields
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTitle { get; set; } = string.Empty;
    public string? RequestDescription { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public string? RequesterEmail { get; set; }
    public DateTime? NeedByDateUtc { get; set; }
    
    // Request State/Type
    public string RequestStatusName { get; set; } = string.Empty;
    public string RequestStatusCode { get; set; } = string.Empty;
    public string RequestStatusBadgeColor { get; set; } = string.Empty;
    public string RequestTypeCode { get; set; } = string.Empty;
    public string RequestTypeName { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public Guid? BuyerId { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerEmail { get; set; }
    public Guid? AreaApproverId { get; set; }
    public string? AreaApproverName { get; set; }
    public string? AreaApproverEmail { get; set; }
    public Guid? FinalApproverId { get; set; }
    public string? FinalApproverName { get; set; }
    public string? FinalApproverEmail { get; set; }

    // Proforma Info (Group-level) - supports multiple files
    public Guid? ProformaId { get; set; }         // kept for backward compatibility (first/latest)
    public string? ProformaFileName { get; set; } // kept for backward compatibility (first/latest)
    public List<ProformaAttachmentDto>? ProformaAttachments { get; set; }
    public List<ProformaAttachmentDto>? SupportingAttachments { get; set; }

    // Request Organization Lookups
    public string? DepartmentName { get; set; }
    public int? PlantId { get; set; }
    public string? PlantName { get; set; }
    public int? RequestPlantId { get; set; }
    public string? RequestPlantName { get; set; }
    public string? PrimaveraCode { get; set; } // This comes from Request if exists, or Supplier in future - let's verify if DTO meant from request or item

    // Item Lookups
    public string? LineItemStatusCode { get; set; }
    public string? LineItemStatusName { get; set; }
    public string? LineItemStatusBadgeColor { get; set; }
    public string? UnitCode { get; set; }

    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierCode { get; set; }

    // Request-level Supplier (Important for transition checks)
    public int? RequestSupplierId { get; set; }
    public string? RequestSupplierName { get; set; }
    public string? RequestSupplierCode { get; set; }

    public int? CostCenterId { get; set; }
    public string? CostCenterName { get; set; }
    public string? CostCenterCode { get; set; }

    public int? IvaRateId { get; set; }
    public string? IvaRateCode { get; set; }
    public string? IvaRateName { get; set; }
    public decimal? IvaRatePercent { get; set; }

    // Currency Fields
    public int? CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }

    public int? RequestCurrencyId { get; set; }
    public string? RequestCurrencyCode { get; set; }

    // Adjustment Metadata
    public string? LatestAdjustmentMessage { get; set; }
    public string? LatestAdjustmentActor { get; set; }
    public string? LatestAdjustmentRole { get; set; }
    public DateTime? LatestAdjustmentDateUtc { get; set; }
    public List<SavedQuotationDto>? Quotations { get; set; }
}
