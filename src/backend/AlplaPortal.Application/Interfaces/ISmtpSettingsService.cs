using AlplaPortal.Application.DTOs;
using AlplaPortal.Application.DTOs.Extraction;

namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// Service responsible for managing SMTP settings with
/// precedence: Database → appsettings.json → Safe Defaults.
/// </summary>
public interface ISmtpSettingsService
{
    /// <summary>
    /// Gets the current settings for administrative display (password masked).
    /// </summary>
    Task<SmtpSettingsDto> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates the persisted SMTP settings.
    /// </summary>
    Task UpdateSettingsAsync(SmtpSettingsDto dto, CancellationToken ct = default);

    /// <summary>
    /// Tests the SMTP connection with the current effective settings.
    /// </summary>
    Task<ConnectionTestResultDto> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the effective SMTP credentials for use by EmailService.
    /// Returns decrypted password.
    /// </summary>
    Task<SmtpEffectiveSettings> GetEffectiveSettingsAsync(CancellationToken ct = default);
}

/// <summary>
/// Internal DTO with decrypted password for EmailService consumption.
/// Never exposed via API.
/// </summary>
public class SmtpEffectiveSettings
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
}
