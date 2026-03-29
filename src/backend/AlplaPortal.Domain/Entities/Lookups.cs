namespace AlplaPortal.Domain.Entities;

public class NeedLevel
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class RequestStatus
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    
    // Workflow tracking properties
    public int DisplayOrder { get; set; }
    public string BadgeColor { get; set; } = "default";
    
    // Future Note: This table prepares for a Workflow Engine. 
    // Allowed transitions between these status IDs can be mapped later via a separate transition rules table.
}

public class RequestType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // QUOTATION, PAYMENT
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Currency
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // AOA, USD, EUR
    public string Symbol { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class CapexOpexClassification
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // CAPEX, OPEX
    public bool IsActive { get; set; } = true;
}

public class Unit
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool AllowsDecimalQuantity { get; set; } = true;
}

/// <summary>
/// Master data for item-level status. Buyer-controlled in future phases.
/// Initial status is auto-assigned by the backend based on parent Request type.
/// </summary>
public class LineItemStatus
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public string BadgeColor { get; set; } = "default";
    public bool IsActive { get; set; } = true;
}
