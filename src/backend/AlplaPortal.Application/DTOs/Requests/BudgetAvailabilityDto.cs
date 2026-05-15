using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

public class BudgetAvailabilityDto
{
    public bool HasBudgetConfig { get; set; }
    public string MatchLevel { get; set; } = "NONE"; // NONE, COST_CENTER, DEPARTMENT_GENERAL, MULTI_CC
    public int FiscalYear { get; set; }
    public string CurrencyCode { get; set; } = "AOA";
    public string? DepartmentName { get; set; }
    public string? PlantName { get; set; }
    public string? CompanyName { get; set; }
    public string? CostCenterName { get; set; }
    public string? InfoMessage { get; set; }
    
    public string Status { get; set; } = "OK"; // OK, WARNING, CRITICAL, EXCEEDED
    public decimal CurrentRequestAmount { get; set; }
    public decimal AnnualBudget { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal AvailableBefore { get; set; }
    public decimal AvailableAfter { get; set; }
    public decimal UtilizationPercent { get; set; }
    
    public List<BudgetCostCenterBreakdownDto> CostCenterBreakdown { get; set; } = new();
}

public class BudgetCostCenterBreakdownDto
{
    public int? CostCenterId { get; set; }
    public string CostCenterName { get; set; } = string.Empty;
    public bool HasBudgetLine { get; set; }
    public decimal AnnualBudget { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal RequestAmountInCC { get; set; }
    public decimal AvailableAfter { get; set; }
    public string Status { get; set; } = "OK";
    public decimal UtilizationPercent { get; set; }
}
