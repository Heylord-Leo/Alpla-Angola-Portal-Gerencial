using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

public class SaveQuotationRequestDto
{
    public int SupplierId { get; set; }
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal TotalGrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalIvaAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? SourceType { get; set; } // OCR, MANUAL
    public string? SourceFileName { get; set; }
    public Guid? ProformaAttachmentId { get; set; }

    public List<SaveQuotationItemDto> Items { get; set; } = new();
}

public class SaveQuotationItemDto
{
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? UnitId { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? IvaRateId { get; set; }
}

public class SavedQuotationDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public int? SupplierId { get; set; }
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal TotalGrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalIvaAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalTaxableBase { get; set; }
    public decimal TotalDiscountAmount { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? SourceFileName { get; set; }
    public Guid? ProformaAttachmentId { get; set; }
    public bool IsSelected { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int ItemCount { get; set; }
    
    public List<SavedQuotationItemDto> Items { get; set; } = new();
}

public class SavedQuotationItemDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public string? UnitCode { get; set; }
    public decimal UnitPrice { get; set; }

    public int? IvaRateId { get; set; }
    public decimal IvaRatePercent { get; set; }

    public decimal DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }

    public decimal GrossSubtotal { get; set; }
    public decimal IvaAmount { get; set; }
    public decimal LineTotal { get; set; }

    // Receiving Fields
    public decimal ReceivedQuantity { get; set; }
    public string? DivergenceNotes { get; set; }
    public string? LineItemStatusCode { get; set; }
    public string? LineItemStatusName { get; set; }
    public string? LineItemStatusBadgeColor { get; set; }
}
