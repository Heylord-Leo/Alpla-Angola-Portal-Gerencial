using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Service interface for managing integration provider settings.
/// Handles CRUD operations on IntegrationProviderSettings with
/// AES encryption for secrets and audit logging.
/// </summary>
public interface IIntegrationSettingsService
{
    /// <summary>Get all providers with their masked settings.</summary>
    Task<List<IntegrationSettingsDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get a single provider's masked settings by code.</summary>
    Task<IntegrationSettingsDto?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Update non-secret settings for a provider.</summary>
    Task UpdateSettingsAsync(string code, UpdateIntegrationSettingsDto dto, int userId, CancellationToken ct = default);

    /// <summary>Replace an encrypted secret (password or API key).</summary>
    Task ReplaceSecretAsync(string code, ReplaceIntegrationSecretDto dto, int userId, CancellationToken ct = default);

    /// <summary>Enable or disable a provider.</summary>
    Task SetEnabledAsync(string code, bool enabled, int userId, CancellationToken ct = default);

    /// <summary>Update Primavera company-specific settings.</summary>
    Task UpdatePrimaveraCompanyAsync(UpdatePrimaveraCompanyDto dto, int userId, CancellationToken ct = default);

    /// <summary>Replace Primavera company-specific password.</summary>
    Task ReplacePrimaveraCompanySecretAsync(ReplacePrimaveraCompanySecretDto dto, int userId, CancellationToken ct = default);
}
