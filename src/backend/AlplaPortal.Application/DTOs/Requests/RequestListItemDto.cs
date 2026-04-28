namespace AlplaPortal.Application.DTOs.Requests;

public class RequestListItemDto
{
    public Guid Id { get; set; }
    public string? RequestNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    
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

    // Context for Area Approval
    public string? CostCenterCode { get; set; }
    public string? CostCenterName { get; set; }
    
    public DateTime? CompletedAtUtc { get; set; }
    
    /// <summary>
    /// Date when payment was completed (transition to PAYMENT_COMPLETED status).
    /// Available for WAITING_RECEIPT and COMPLETED requests so the list can show both
    /// the original deadline and the actual payment date.
    /// </summary>
    public DateTime? PaymentCompletedAtUtc { get; set; }
}
