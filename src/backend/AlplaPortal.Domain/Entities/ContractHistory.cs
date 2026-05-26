namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Immutable audit trail for contract lifecycle events.
/// Analogous to RequestStatusHistory.
/// </summary>
public class ContractHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;

    /// <summary>CREATED, STATUS_CHANGED, DOCUMENT_UPLOADED, OBLIGATION_ADDED, OCR_EXTRACTED, FIELD_UPDATED, REQUEST_LINKED, RENEWED</summary>
    public string EventType { get; set; } = string.Empty;

    public string? FromStatusCode { get; set; }
    public string? ToStatusCode { get; set; }

    /// <summary>Human-readable comment in Portuguese (DEC-011).</summary>
    public string Comment { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;
}
