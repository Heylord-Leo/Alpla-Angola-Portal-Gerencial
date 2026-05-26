namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Immutable audit trail for leave record status transitions.
/// Follows the same pattern as RequestStatusHistory.
/// </summary>
public class LeaveStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LeaveRecordId { get; set; }
    public LeaveRecord LeaveRecord { get; set; } = null!;

    public Guid ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;

    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string? Comment { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
