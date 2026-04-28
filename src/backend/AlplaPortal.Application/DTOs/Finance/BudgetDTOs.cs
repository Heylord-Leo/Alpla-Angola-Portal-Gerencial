namespace AlplaPortal.Application.DTOs.Finance;

public class BudgetDepartmentOverviewDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public decimal TotalBudget { get; set; }
    public decimal CommittedSpend { get; set; }
    public decimal PaidSpend { get; set; }
    public decimal UsagePercentage => TotalBudget > 0 ? (CommittedSpend / TotalBudget) * 100 : 0;
    public string CurrencyCode { get; set; } = string.Empty;
    public int CurrencyId { get; set; }
}

public class BudgetOverviewDto
{
    public int Year { get; set; }
    public List<BudgetDepartmentOverviewDto> Departments { get; set; } = new();
    public decimal TotalGlobalBudget => Departments.Sum(d => d.TotalBudget);
    public decimal TotalGlobalCommitted => Departments.Sum(d => d.CommittedSpend);
    public decimal GlobalUsagePercentage => TotalGlobalBudget > 0 ? (TotalGlobalCommitted / TotalGlobalBudget) * 100 : 0;
}

public class BudgetCostCenterDetailDto
{
    public int CostCenterId { get; set; }
    public string CostCenterName { get; set; } = string.Empty;
    public decimal CommittedSpend { get; set; }
    public decimal PaidSpend { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public class BudgetMonthlyDataDto
{
    public int Month { get; set; }
    public string MonthLabel { get; set; } = string.Empty;
    public List<BudgetMonthlyCostCenterDto> CostCenters { get; set; } = new();
}

public class BudgetMonthlyCostCenterDto
{
    public int CostCenterId { get; set; }
    public string CostCenterName { get; set; } = string.Empty;
    public decimal CommittedAmount { get; set; }
    public decimal PaidAmount { get; set; }
}

public class AnnualBudgetConfigDto
{
    public int Id { get; set; }
    public int Year { get; set; }

    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }

    public int PlantId { get; set; }
    public string? PlantName { get; set; }

    public int DepartmentId { get; set; }
    public string? DepartmentName { get; set; }

    public int? CostCenterId { get; set; }
    public string? CostCenterName { get; set; }

    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }

    public decimal TotalAmount { get; set; }
    public bool IsActive { get; set; }
}
