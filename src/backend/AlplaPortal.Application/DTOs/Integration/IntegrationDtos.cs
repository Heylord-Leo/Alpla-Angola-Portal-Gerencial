using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Summary of health status across all registered integration providers.
/// Returned by the integration health API endpoint.
/// </summary>
public class IntegrationHealthSummaryDto
{
    [JsonPropertyName("providers")]
    public List<IntegrationProviderStatusDto> Providers { get; set; } = new();

    [JsonPropertyName("checkedAtUtc")]
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Health/status representation for a single integration provider.
/// Contains provider metadata + connection diagnostic state.
/// Never exposes credentials or sensitive connection details.
/// </summary>
public class IntegrationProviderStatusDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("providerType")]
    public string ProviderType { get; set; } = string.Empty;

    [JsonPropertyName("connectionType")]
    public string ConnectionType { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("isPlanned")]
    public bool IsPlanned { get; set; }

    [JsonPropertyName("displayOrder")]
    public int DisplayOrder { get; set; }

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    // ─── Connection status (from IntegrationConnectionStatus) ───

    /// <summary>Machine-readable status code from IntegrationStatusCodes.</summary>
    [JsonPropertyName("currentStatus")]
    public string CurrentStatus { get; set; } = string.Empty;

    [JsonPropertyName("lastSuccessUtc")]
    public DateTime? LastSuccessUtc { get; set; }

    [JsonPropertyName("lastFailureUtc")]
    public DateTime? LastFailureUtc { get; set; }

    [JsonPropertyName("lastCheckedAtUtc")]
    public DateTime? LastCheckedAtUtc { get; set; }

    [JsonPropertyName("lastResponseTimeMs")]
    public int? LastResponseTimeMs { get; set; }

    [JsonPropertyName("lastErrorMessage")]
    public string? LastErrorMessage { get; set; }

    [JsonPropertyName("consecutiveFailures")]
    public int ConsecutiveFailures { get; set; }

    [JsonPropertyName("lastTestedByEmail")]
    public string? LastTestedByEmail { get; set; }

    // ─── Configuration indicators (no secrets exposed) ───

    /// <summary>Whether connection settings exist for this provider (does not expose values).</summary>
    [JsonPropertyName("hasConnectionSettings")]
    public bool HasConnectionSettings { get; set; }

    /// <summary>Whether a concrete IIntegrationProvider implementation is registered.</summary>
    [JsonPropertyName("hasImplementation")]
    public bool HasImplementation { get; set; }

    /// <summary>Whether manual connection test can be triggered (enabled + configured + implemented).</summary>
    [JsonPropertyName("canTestConnection")]
    public bool CanTestConnection { get; set; }
}

/// <summary>
/// Result of a manual connection test for a specific provider.
/// Returned by the test-connection endpoint.
/// </summary>
public class IntegrationConnectionTestResultDto
{
    [JsonPropertyName("providerCode")]
    public string ProviderCode { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("responseTimeMs")]
    public int? ResponseTimeMs { get; set; }

    [JsonPropertyName("testedAtUtc")]
    public DateTime TestedAtUtc { get; set; } = DateTime.UtcNow;
}
