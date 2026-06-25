namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Represents a queued email notification in the outbox pattern.
/// Emails are written to this table during workflow event processing and
/// sent asynchronously by the <see cref="EmailOutboxProcessor"/> background service.
///
/// <para><b>Lifecycle:</b> PENDING → SENT | FAILED → (retry) → SENT | DEAD_LETTER</para>
///
/// <para><b>Dedup Key:</b> (CorrelationId, RecipientEmail) — prevents duplicate emails
/// for the same workflow event when the processor retries after partial failures.</para>
/// </summary>
public class EmailOutboxEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Recipient ──────────────────────────────────────────────

    /// <summary>Primary recipient email address (To:).</summary>
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>Display name of the recipient.</summary>
    public string? RecipientName { get; set; }

    // ─── Email Content ──────────────────────────────────────────

    /// <summary>Email subject line.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Email headline (used in the branded template).</summary>
    public string Headline { get; set; } = string.Empty;

    /// <summary>HTML body content of the email.</summary>
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>Optional CTA button URL.</summary>
    public string? ActionUrl { get; set; }

    /// <summary>Optional CTA button label.</summary>
    public string? ActionLabel { get; set; }

    /// <summary>Optional CC email addresses (semicolon-separated).</summary>
    public string? CcEmails { get; set; }

    // ─── Processing State ───────────────────────────────────────

    /// <summary>
    /// Current processing status.
    /// Valid values: PENDING, PROCESSING, SENT, FAILED, DEAD_LETTER.
    /// </summary>
    public string Status { get; set; } = "PENDING";

    /// <summary>Number of send attempts made so far.</summary>
    public int RetryCount { get; set; }

    /// <summary>Maximum number of retry attempts before moving to DEAD_LETTER.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Error message from the last failed attempt.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Earliest time the processor should attempt sending this entry.
    /// Used for exponential backoff scheduling.
    /// </summary>
    public DateTime? NextRetryAtUtc { get; set; }

    // ─── Correlation & Traceability ─────────────────────────────

    /// <summary>The request that triggered this notification.</summary>
    public Guid? RequestId { get; set; }

    /// <summary>The request number for display/logging (e.g., "REQ-25/06/2026-042").</summary>
    public string? RequestNumber { get; set; }

    /// <summary>Workflow event code that originated this email (e.g., "QUOTATION_AWAITING_BUYER").</summary>
    public string? EventCode { get; set; }

    /// <summary>
    /// Unique correlation ID for deduplication.
    /// Combined with RecipientEmail, prevents duplicate sends for the same event.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    // ─── Timestamps ─────────────────────────────────────────────

    /// <summary>When this outbox entry was created (queued).</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the email was successfully sent or moved to DEAD_LETTER.</summary>
    public DateTime? ProcessedAtUtc { get; set; }
}
