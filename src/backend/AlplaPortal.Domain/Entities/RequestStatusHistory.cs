namespace AlplaPortal.Domain.Entities;

// Immutable log of actions
public class RequestStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public Guid ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;

    public string ActionTaken { get; set; } = string.Empty; // e.g. "Submitted", "Approved"
    
    public int? PreviousStatusId { get; set; }
    public RequestStatus? PreviousStatus { get; set; }

    public int NewStatusId { get; set; }
    public RequestStatus NewStatus { get; set; } = null!;

    public string? Comment { get; set; }

    /// <summary>
    /// Stable business identity of the event that produced this row, used to deduplicate
    /// retried transitions (e.g. FI_VAL:{GroupId}:{AttachmentId}, GC:{GroupId}:{FiscalReceiptAttachmentId},
    /// RC:{RequestId}:{CompletionCycleId}). Built exclusively by
    /// <see cref="Services.PostPaymentIdempotencyKeys"/> — never a date and never a per-attempt GUID.
    /// Nullable: every pre-existing row and every event without a defined key stays null,
    /// which is why the uniqueness index is filtered on IS NOT NULL.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
