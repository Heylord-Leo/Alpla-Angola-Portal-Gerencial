using System;

namespace AlplaPortal.Domain.Entities;

public class IvaRate
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal RatePercent { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}
