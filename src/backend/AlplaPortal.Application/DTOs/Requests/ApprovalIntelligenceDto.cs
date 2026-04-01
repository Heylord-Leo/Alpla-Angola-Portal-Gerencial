using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

public class ApprovalIntelligenceDto
{
    public Guid RequestId { get; set; }
    public List<ItemIntelligenceDto> Items { get; set; } = new();
    public DepartmentIntelligenceDto DepartmentContext { get; set; } = new();
    public List<DecisionAlertDto> OverallAlerts { get; set; } = new();
}

public class ItemIntelligenceDto
{
    public Guid LineItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal CurrentUnitPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    
    // Historical
    public decimal? LastPaidPrice { get; set; }
    public decimal? AverageHistoricalPrice { get; set; }
    public string? LastSupplierName { get; set; }
    public int TotalPurchaseCount { get; set; }
    public decimal? VariationVsLastPercentage { get; set; }
    public decimal? VariationVsAvgPercentage { get; set; }
    public bool HasHistory { get; set; }
    public string MatchType { get; set; } = "DESCRIPTION"; 
}

public class DepartmentIntelligenceDto
{
    public decimal MonthAccumulatedTotal { get; set; }
    public decimal YearAccumulatedTotal { get; set; }
    public int MonthApprovedCount { get; set; }
    public decimal CurrentRequestSharePercentage { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class DecisionAlertDto
{
    public string Type { get; set; } = string.Empty; // e.g., "PRC_HIGH_AVG", "DUP_RECENT"
    public string Level { get; set; } = "INFO"; // INFO, WARNING, CRITICAL
    public string Message { get; set; } = string.Empty;
    public Guid? RelatedItemId { get; set; }
}
