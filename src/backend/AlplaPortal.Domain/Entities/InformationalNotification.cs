using System;

namespace AlplaPortal.Domain.Entities;

public class InformationalNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // WARNING, INFO, etc.
    public string TargetPath { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public bool IsDismissed { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Dedup anchor — typically the RequestStatusHistory.Id that generated this notification.
    /// Combined with UserId, prevents duplicate notifications on controller retries.
    /// Null for legacy notifications created before the orchestrator was introduced.
    /// </summary>
    public Guid? EventCorrelationId { get; set; }

    /// <summary>
    /// Notification category for filtering/grouping (e.g., "APPROVAL", "PAYMENT").
    /// See <see cref="Constants.NotificationCategories"/>.
    /// </summary>
    public string? Category { get; set; }
}
