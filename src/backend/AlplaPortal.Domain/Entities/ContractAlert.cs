namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Proactive notification records for contract lifecycle events.
/// Phase 1: EXPIRY_WARNING (90/60/30 days) and RENEWAL_NOTICE only.
/// </summary>
public class ContractAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;

    /// <summary>EXPIRY_WARNING, RENEWAL_NOTICE</summary>
    public string AlertType { get; set; } = string.Empty;

    public DateTime TriggerDateUtc { get; set; }
    public bool IsDismissed { get; set; }
    public Guid? DismissedByUserId { get; set; }
    public DateTime? DismissedAtUtc { get; set; }

    /// <summary>Human-readable message in Portuguese.</summary>
    public string? Message { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
