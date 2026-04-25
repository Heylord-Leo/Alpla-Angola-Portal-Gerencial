namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Immutable audit trail for supplier registration lifecycle events.
/// Follows the ContractHistory pattern — event-type based with optional from/to status codes.
/// </summary>
public class SupplierStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    /// <summary>
    /// Event type code — see <see cref="Constants.SupplierConstants.HistoryEventTypes"/>.
    /// CREATED, FIELD_UPDATED, STATUS_CHANGED, DOCUMENT_UPLOADED, DOCUMENT_REMOVED,
    /// SUBMITTED_FOR_APPROVAL, DAF_APPROVED, DG_APPROVED, ADJUSTMENT_REQUESTED,
    /// ACTIVATED, SUSPENDED, BLOCKED, REACTIVATED.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Status code before the transition. Null for non-status events like DOCUMENT_UPLOADED.</summary>
    public string? FromStatusCode { get; set; }

    /// <summary>Status code after the transition. Null for non-status events.</summary>
    public string? ToStatusCode { get; set; }

    /// <summary>
    /// Human-readable comment or justification.
    /// Mandatory for ADJUSTMENT_REQUESTED events. Optional for others.
    /// </summary>
    public string Comment { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public Guid ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;
}
