using System;

namespace AlplaPortal.Domain.Entities;

public class AnnualBudget
{
    public int Id { get; set; }
    public int Year { get; set; }
    public decimal TotalAmount { get; set; }
    
    // Relações
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }
    
    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }
}
