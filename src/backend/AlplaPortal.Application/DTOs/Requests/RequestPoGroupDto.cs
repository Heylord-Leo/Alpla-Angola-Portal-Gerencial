using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

public class RequestPoGroupDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    
    public int? SupplierId { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public string? SupplierNifSnapshot { get; set; }
    
    public int? CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    
    public decimal TotalAmount { get; set; }
    
    public string? PaymentConditionCode { get; set; }
    public decimal? AdvancePaymentPercent { get; set; }
    
    public string Status { get; set; } = string.Empty;
    public string? PurchaseOrderNumber { get; set; }
    
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    
    // Summary of linked children for easy UI access without heavy payloads if needed
    public int LineItemCount { get; set; }

    public int AttachmentCount { get; set; }
    
    public List<RequestPaymentDto> Payments { get; set; } = new();
}

public class RequestPaymentDto
{
    public int Id { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal PlannedAmount { get; set; }
    public decimal? ActualPaidAmount { get; set; }
    public DateTime? ScheduledDateUtc { get; set; }
    public DateTime? PaidDateUtc { get; set; }
    public string? CurrencyCode { get; set; }
    public bool HasDivergence { get; set; }
}


public class RequestPoGroupSummaryDto
{
    public Guid Id { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PurchaseOrderNumber { get; set; }
}
