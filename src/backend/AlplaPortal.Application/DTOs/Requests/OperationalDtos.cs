using System;
using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

public class UpdateLineItemCostCenterDto
{
    public int? CostCenterId { get; set; }
}

public class UpdateItemReceivingDto
{
    [Range(0, double.MaxValue)]
    public decimal ReceivedQuantity { get; set; }
    
    [MaxLength(1000)]
    public string? DivergenceNotes { get; set; }
}

public class UpdateLineItemStatusDto
{
    public int? LineItemStatusId { get; set; }
    
    [MaxLength(50)]
    public string? StatusCode { get; set; }
    
    [MaxLength(500)]
    public string? Comment { get; set; }
}

public class UpdateLineItemCurrencyDto
{
    public int? CurrencyId { get; set; }
}
