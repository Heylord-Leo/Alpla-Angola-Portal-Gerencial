using AlplaPortal.Application.DTOs.Integration;

namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Service contract for aggregating integration provider health across all registered providers.
///
/// Resolves providers from the database, invokes available IIntegrationProvider implementations,
/// and assembles a unified health summary for the admin integration UI.
///
/// This service is provider-agnostic — it iterates over whatever providers are registered
/// without knowledge of specific business domains (employees, materials, etc.).
/// </summary>
public interface IIntegrationHealthService
{
    /// <summary>
    /// Returns health status for all registered integration providers.
    /// Planned/disabled providers are included with appropriate status codes.
    /// </summary>
    Task<IntegrationHealthSummaryDto> GetHealthSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Triggers a manual connection test for a specific provider.
    /// Only succeeds if the provider is enabled, configured, and has a registered implementation.
    /// </summary>
    Task<IntegrationConnectionTestResultDto> TestProviderConnectionAsync(string providerCode, CancellationToken ct = default);
}
