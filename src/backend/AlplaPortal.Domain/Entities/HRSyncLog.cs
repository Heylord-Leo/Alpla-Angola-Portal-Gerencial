namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Tracks each Innux → HREmployee sync operation for operational visibility.
/// Visible in the HR Overview and Employee admin area.
/// </summary>
public class HRSyncLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Portal user who triggered the sync (null for automated syncs).</summary>
    public Guid? TriggeredByUserId { get; set; }
    public User? TriggeredByUser { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public int EmployeesCreated { get; set; }
    public int EmployeesUpdated { get; set; }
    public int EmployeesDeactivated { get; set; }
    public int TotalProcessed { get; set; }
    public int Errors { get; set; }

    /// <summary>RUNNING, COMPLETED, FAILED</summary>
    public string Status { get; set; } = "RUNNING";

    public string? ErrorDetails { get; set; }
}
