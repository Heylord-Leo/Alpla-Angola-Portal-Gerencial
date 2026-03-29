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

    public DateTime CreatedAtUtc { get; set; }
}
