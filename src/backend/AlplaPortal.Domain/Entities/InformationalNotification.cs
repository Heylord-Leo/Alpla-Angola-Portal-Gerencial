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
}
