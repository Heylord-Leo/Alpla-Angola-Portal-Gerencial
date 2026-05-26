namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Audit record for Proforma deadline alerts sent to approvers.
/// Dedup key: (RequestId, AlertLevel, RecipientUserId) — globally unique,
/// so each recipient receives at most one alert per level per request.
/// If the request moves to another approval stage (and the responsible
/// approver changes), the new recipient can still receive the alert.
/// </summary>
public class ProformaDeadlineAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    /// <summary>WARNING_3D, WARNING_1D, CRITICAL_0D, EXPIRED</summary>
    public string AlertLevel { get; set; } = string.Empty;

    public Guid RecipientUserId { get; set; }
    public User? RecipientUser { get; set; }

    public bool EmailSent { get; set; }
    public bool InAppSent { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime SentAtUtc { get; set; }
}
