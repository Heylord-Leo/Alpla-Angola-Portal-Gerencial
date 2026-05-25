namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Response DTO for integration provider settings — returned by GET endpoints.
/// NEVER includes EncryptedPassword or ApiKeyEncrypted.
/// </summary>
public class IntegrationSettingsDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Environment { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsPlanned { get; set; }
    public bool IsReadOnly { get; set; }

    // Connection settings (non-secret)
    public string? Server { get; set; }
    public string? DatabaseName { get; set; }
    public string? InstanceName { get; set; }
    public string? AuthenticationMode { get; set; }
    public string? Username { get; set; }
    public string? ApiBaseUrl { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? AdditionalConfig { get; set; }

    // Secret presence indicators — NEVER the actual secrets
    public bool HasPassword { get; set; }
    public bool HasApiKey { get; set; }
    public int SecretVersion { get; set; }

    // Last connection test status
    public string? LastTestStatus { get; set; }
    public DateTime? LastTestAt { get; set; }
    public string? LastTestMessage { get; set; }
    public int? LastTestResponseTimeMs { get; set; }

    // Audit
    public string? UpdatedByUserName { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Request DTO for updating non-secret integration settings.
/// </summary>
public class UpdateIntegrationSettingsDto
{
    public string? Server { get; set; }
    public string? DatabaseName { get; set; }
    public string? InstanceName { get; set; }
    public string? AuthenticationMode { get; set; }
    public string? Username { get; set; }
    public string? ApiBaseUrl { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? AdditionalConfig { get; set; }
}

/// <summary>
/// Request DTO for replacing an encrypted secret (password or API key).
/// </summary>
public class ReplaceIntegrationSecretDto
{
    /// <summary>PASSWORD or API_KEY</summary>
    public string SecretType { get; set; } = string.Empty;

    /// <summary>The new plaintext value — will be AES-encrypted before persistence.</summary>
    public string NewSecretValue { get; set; } = string.Empty;
}
