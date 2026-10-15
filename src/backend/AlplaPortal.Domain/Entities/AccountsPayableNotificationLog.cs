namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Audit log for Accounts Payable notification emails.
/// Provides duplicate prevention and traceability for AP notifications.
/// Dedup key: (RequestId, EventCode, RecipientEmail) where Success=true and Skipped=false.
/// </summary>
public class AccountsPayableNotificationLog
{
    public int Id { get; set; }

    /// <summary>The request that triggered this notification.</summary>
    public Guid RequestId { get; set; }

    /// <summary>The company associated with the request.</summary>
    public int CompanyId { get; set; }

    /// <summary>Workflow event code (PAYMENT_SCHEDULED or PAYMENT_COMPLETED).</summary>
    public string EventCode { get; set; } = string.Empty;

    /// <summary>Primary recipient email address (To:).</summary>
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>CC email addresses used (snapshot at send time).</summary>
    public string? CcEmails { get; set; }

    /// <summary>Email subject line sent.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Timestamp when the send was attempted.</summary>
    public DateTime SentAtUtc { get; set; }

    /// <summary>Whether the email was sent successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Error details if sending failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>True if this entry was skipped due to duplicate detection.</summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// Workflow correlation (the history row of the transition) for events whose dedup is per ACTION rather than
    /// per request: PO_REGISTERED (a request can have several P.O. groups and legitimate corrections). Null for
    /// payment scheduling/completion rows, whose dedup stays per (RequestId, EventCode, RecipientEmail), and for
    /// every pre-existing row.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
