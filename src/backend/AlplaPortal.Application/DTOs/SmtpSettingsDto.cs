namespace AlplaPortal.Application.DTOs;

/// <summary>
/// DTO for reading and writing SMTP configuration.
/// The password is write-only: never returned on GET, only accepted on PUT.
/// </summary>
public class SmtpSettingsDto
{
    public string? Server { get; set; }
    public int? Port { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// Read-only. Indicates whether a password is configured in the database.
    /// </summary>
    public bool HasPassword { get; set; }

    /// <summary>
    /// Write-only. Set to a non-empty value to update the password.
    /// Null or empty means "keep existing password".
    /// </summary>
    public string? Password { get; set; }
}
