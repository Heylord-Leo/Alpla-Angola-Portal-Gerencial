using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.Models.Configuration;

namespace AlplaPortal.Application.Interfaces.Extraction;

/// <summary>
/// Service responsible for resolving the effective document extraction settings
/// with precedence: Database -> appsettings.json -> Safe Defaults.
/// </summary>
public interface IDocumentExtractionSettingsService
{
    /// <summary>
    /// Gets the effective settings used by the extraction engine.
    /// </summary>
    Task<DocumentExtractionOptions> GetEffectiveSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the current operational settings for administrative purposes.
    /// </summary>
    Task<DocumentExtractionSettingsDto> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates the persisted operational settings.
    /// </summary>
    Task UpdateSettingsAsync(DocumentExtractionSettingsDto dto, CancellationToken ct = default);

    /// <summary>
    /// Tests the connection to the currently active document extraction provider.
    /// </summary>
    Task<ConnectionTestResultDto> TestConnectionAsync(CancellationToken ct = default);
}
