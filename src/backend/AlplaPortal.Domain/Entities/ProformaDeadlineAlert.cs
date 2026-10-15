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

    /// <summary>
    /// LEGACY (direct SmtpClient era): true = the message was accepted by the SMTP server at send
    /// time. Since alerts go through EmailOutbox this flag is always false; delivery evidence is the
    /// linked outbox row (<see cref="OutboxEntryId"/>). Never read it as "delivered" for new rows.
    /// </summary>
    public bool EmailSent { get; set; }
    public bool InAppSent { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Latest EmailOutbox row queued for this alert level (null for legacy direct-send rows).</summary>
    public Guid? OutboxEntryId { get; set; }
    public EmailOutboxEntry? OutboxEntry { get; set; }

    /// <summary>How many outbox rows were queued for this alert level (re-queued after DEAD_LETTER/EXPIRED).</summary>
    public int QueuedCount { get; set; }
    public DateTime? LastQueuedAtUtc { get; set; }

    public DateTime SentAtUtc { get; set; }
}
