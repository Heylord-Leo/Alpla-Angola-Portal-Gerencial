namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Lookup entity for contract classification.
/// Phase 1 seeds: SERVICE, LEASE, SUPPLY, MAINTENANCE.
/// </summary>
public class ContractType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}
