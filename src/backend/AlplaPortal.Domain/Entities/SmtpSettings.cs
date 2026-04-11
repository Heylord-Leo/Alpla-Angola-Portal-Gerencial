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

    // Audit
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
