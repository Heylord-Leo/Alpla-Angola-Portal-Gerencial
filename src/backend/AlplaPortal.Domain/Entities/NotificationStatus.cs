using System;

namespace AlplaPortal.Domain.Entities;

public class NotificationStatus
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Category { get; set; } = string.Empty;
    public int LastReadCount { get; set; }
    public bool IsRead { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
