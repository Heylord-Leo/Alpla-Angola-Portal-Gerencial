namespace AlplaPortal.Domain.Entities;

/// <summary>
/// DB-persisted technical connection/configuration settings for an integration provider.
/// Overrides appsettings.json when values are present (settings cascade pattern).
///
/// This entity is STRICTLY connection-oriented. It must NOT contain:
/// - business/domain-specific settings (employee rules, material mappings)
/// - sync scheduling rules
/// - domain-specific field mappings
///
/// Future domain-specific configuration should live in separate, dedicated entities.
/// </summary>
public class IntegrationProviderSettings
{
    public int Id { get; set; }

    /// <summary>FK to the owning integration provider.</summary>
    public int IntegrationProviderId { get; set; }
    public IntegrationProvider Provider { get; set; } = null!;

    // ─── SQL-based connection settings ───

    /// <summary>Server hostname or IP address.</summary>
    public string? Server { get; set; }

    /// <summary>Database name on the target server.</summary>
    public string? DatabaseName { get; set; }

    /// <summary>SQL Server instance name (e.g., SQLALPLA, SQLINNUX).</summary>
    public string? InstanceName { get; set; }

    /// <summary>Authentication mode: SQL, WINDOWS, API_KEY, OAUTH.</summary>
    public string? AuthenticationMode { get; set; }

    /// <summary>Username for SQL or API authentication.</summary>
    public string? Username { get; set; }

    /// <summary>Encrypted password — persisted via AesEncryptionHelper. Never exposed to frontend.</summary>
    public string? EncryptedPassword { get; set; }

    // ─── API-based connection settings ───

    /// <summary>Base URL for REST/SOAP API providers.</summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>Encrypted API key — persisted via AesEncryptionHelper. Never exposed to frontend.</summary>
    public string? ApiKeyEncrypted { get; set; }

    // ─── Shared technical settings ───

    /// <summary>Connection timeout in seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// JSON blob for provider-specific technical configuration that does not warrant
    /// a dedicated column. Use sparingly — prefer explicit columns for common settings.
    /// </summary>
    public string? AdditionalConfig { get; set; }

    // Audit
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
