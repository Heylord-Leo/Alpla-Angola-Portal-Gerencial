using System;
using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

public class UpdateLineItemSupplierDto
{
    public int? SupplierId { get; set; }
    
    [MaxLength(200)]
    public string? SupplierName { get; set; }
    
    public decimal? UnitPrice { get; set; }
    
    public int? CurrencyId { get; set; }
}
