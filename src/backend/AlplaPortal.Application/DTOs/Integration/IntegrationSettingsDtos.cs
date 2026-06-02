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

    // SMTP-specific connection settings
    public int? Port { get; set; }
    public bool? EnableSsl { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }

    // Secret presence indicators — NEVER the actual secrets
    public bool HasPassword { get; set; }
    public bool HasApiKey { get; set; }
    public int SecretVersion { get; set; }

    // Last connection test status
    public string? LastTestStatus { get; set; }
    public DateTime? LastTestAt { get; set; }
    public string? LastTestMessage { get; set; }
    public int? LastTestResponseTimeMs { get; set; }

    // Company-specific settings for Primavera
    public List<PrimaveraCompanySettingsDto>? PrimaveraCompanies { get; set; }

    // Plant-specific settings for AlplaPROD
    public List<AlplaProdPlantSettingsDto>? AlplaProdPlants { get; set; }

    // Audit
    public string? UpdatedByUserName { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class PrimaveraCompanySettingsDto
{
    public string CompanyKey { get; set; } = string.Empty;
    public string? DatabaseName { get; set; }
    public bool Enabled { get; set; }
    public string? Username { get; set; }
    public bool HasPassword { get; set; }
    public int SecretVersion { get; set; }
}

/// <summary>
/// Response DTO for AlplaPROD per-plant settings — NEVER includes secrets.
/// </summary>
public class AlplaProdPlantSettingsDto
{
    public string PlantKey { get; set; } = string.Empty;
    public string? Server { get; set; }
    public string? DatabaseName { get; set; }
    public bool Enabled { get; set; }
    public string? Username { get; set; }
    /// <summary>Whether a per-plant password override is configured (true) or falls back to global (false).</summary>
    public bool HasPassword { get; set; }
    /// <summary>Indicates if credentials come from per-plant config or global AlplaPROD fallback.</summary>
    public bool UsesGlobalCredentials { get; set; }
    public int SecretVersion { get; set; }
    /// <summary>Read-only pipeline model from configuration.</summary>
    public string? PipelineModel { get; set; }
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

    // SMTP-specific connection settings
    public int? Port { get; set; }
    public bool? EnableSsl { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }
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

public class UpdatePrimaveraCompanyDto
{
    public string CompanyKey { get; set; } = string.Empty;
    public string? DatabaseName { get; set; }
    public bool Enabled { get; set; }
    public string? Username { get; set; }
}

public class ReplacePrimaveraCompanySecretDto
{
    public string CompanyKey { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for updating non-secret AlplaPROD per-plant settings.
/// Uses plantKey terminology for domain clarity.
/// </summary>
public class UpdateAlplaProdPlantDto
{
    public string PlantKey { get; set; } = string.Empty;
    public string? Server { get; set; }
    public string? DatabaseName { get; set; }
    public bool Enabled { get; set; }
    public string? Username { get; set; }
}

/// <summary>
/// Request DTO for replacing the per-plant AlplaPROD password.
/// If set, overrides the global AlplaPROD credentials for this plant.
/// </summary>
public class ReplaceAlplaProdPlantSecretDto
{
    public string PlantKey { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
