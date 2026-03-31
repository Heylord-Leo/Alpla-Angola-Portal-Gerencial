namespace AlplaPortal.Domain.Entities;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Plant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PortalCode { get; set; } // Internal/Local code
    public string? PrimaveraCode { get; set; } // ERP/Future integration code
    public string? TaxId { get; set; } // NIF/VAT
    public bool IsActive { get; set; } = true;
}

public class CostCenter
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Plant this Cost Center belongs to. Required — every CC maps to exactly one plant.</summary>
    public int PlantId { get; set; }
    public Plant Plant { get; set; } = null!;
}
