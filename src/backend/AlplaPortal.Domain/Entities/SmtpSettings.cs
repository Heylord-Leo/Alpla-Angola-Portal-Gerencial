namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Persisted global settings for SMTP email delivery.
/// These settings override appsettings.json when present in the database.
/// All fields are nullable to allow for field-level fallback to configuration.
/// </summary>
public class SmtpSettings
{
    public int Id { get; set; }

    public string? Server { get; set; }
    public int? Port { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }
    public bool? EnableSsl { get; set; }

    /// <summary>
    /// AES-encrypted SMTP password. Never exposed via API.
    /// </summary>
    public string? EncryptedPassword { get; set; }

    // ─── Email Environment Identification ───────────────────────
    // These settings control how the system identifies the environment
    // in outgoing emails. Non-production environments apply defaults
    // automatically; these fields allow admin customization.

    /// <summary>
    /// When true, a prefix (e.g. "[TEST - IGNORE]") is prepended to the email subject.
    /// In non-production environments, the prefix is applied by default even if this is false.
    /// </summary>
    public bool EnableSubjectPrefix { get; set; }

    /// <summary>
    /// Custom subject prefix text. Falls back to "[{ENV} - IGNORE]" when null.
    /// </summary>
    public string? SubjectPrefixText { get; set; }

    /// <summary>
    /// When true, a styled warning banner is injected at the top of the email body.
    /// In non-production environments, the banner is applied by default even if this is false.
    /// </summary>
    public bool EnableBodyWarningBanner { get; set; }

    /// <summary>
    /// Custom warning banner text. Falls back to a standard environment warning when null.
    /// </summary>
    public string? WarningBannerText { get; set; }

    /// <summary>
    /// When true, all outgoing emails are redirected to TestRecipientEmail.
    /// Only applicable in non-production environments.
    /// </summary>
    public bool RedirectAllToTestRecipient { get; set; }

    /// <summary>
    /// The email address to redirect all outgoing emails to when RedirectAllToTestRecipient is enabled.
    /// </summary>
    public string? TestRecipientEmail { get; set; }

    /// <summary>
    /// When true and redirection is active, the original recipient is shown in the email body.
    /// </summary>
    public bool ShowOriginalRecipientsInBody { get; set; }

    /// <summary>
    /// When true, allows sending to real recipients even in non-production environments.
    /// Acts as an explicit safety override — defaults to false.
    /// </summary>
    public bool AllowRealRecipientsInNonProduction { get; set; }

    // Audit
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
