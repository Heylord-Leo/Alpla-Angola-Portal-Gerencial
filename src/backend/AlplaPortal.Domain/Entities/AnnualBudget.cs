using System;

namespace AlplaPortal.Domain.Entities;

public class AnnualBudget
{
    public int Id { get; set; }
    public int Year { get; set; }
    public decimal TotalAmount { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    // Relações
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public int PlantId { get; set; }
    public Plant? Plant { get; set; }

    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int? CostCenterId { get; set; }
    public CostCenter? CostCenter { get; set; }
    
    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }
}
