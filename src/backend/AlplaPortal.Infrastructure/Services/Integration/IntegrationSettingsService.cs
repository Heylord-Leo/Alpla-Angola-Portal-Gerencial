using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Manages CRUD operations on IntegrationProviderSettings.
/// Secrets are encrypted via AesEncryptionHelper and NEVER returned in DTOs.
/// All mutations are audit-logged via AdminLogWriter.
/// </summary>
public class IntegrationSettingsService : IIntegrationSettingsService
{
    private readonly ApplicationDbContext _db;
    private readonly AdminLogWriter _logWriter;
    private readonly ILogger<IntegrationSettingsService> _logger;
    private readonly IConfiguration _configuration;

    private string EncryptionKey => _configuration["AppConfig:EncryptionKey"] ?? string.Empty;

    public IntegrationSettingsService(
        ApplicationDbContext db,
        AdminLogWriter logWriter,
        ILogger<IntegrationSettingsService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _logWriter = logWriter;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<List<IntegrationSettingsDto>> GetAllAsync(CancellationToken ct = default)
    {
        var providers = await _db.IntegrationProviders
            .Include(p => p.Settings)
            .Include(p => p.ConnectionStatus)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        var result = new List<IntegrationSettingsDto>();

        foreach (var provider in providers)
        {
            result.Add(MapToDto(provider));
        }

        return result;
    }

    public async Task<IntegrationSettingsDto?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var provider = await _db.IntegrationProviders
            .Include(p => p.Settings)
            .Include(p => p.ConnectionStatus)
            .FirstOrDefaultAsync(p => p.Code == code, ct);

        return provider == null ? null : MapToDto(provider);
    }

    public async Task UpdateSettingsAsync(string code, UpdateIntegrationSettingsDto dto, int userId, CancellationToken ct = default)
    {
        var provider = await _db.IntegrationProviders
            .Include(p => p.Settings)
            .FirstOrDefaultAsync(p => p.Code == code, ct);

        if (provider == null)
            throw new ArgumentException($"Provider '{code}' not found.");

        if (code == "SMTP")
        {
            if (string.IsNullOrWhiteSpace(dto.Server))
                throw new ArgumentException("O endereço do servidor SMTP é obrigatório.");

            if (dto.Port is null or <= 0 or > 65535)
                throw new ArgumentException("A porta SMTP deve ser um valor entre 1 e 65535.");

            if (string.IsNullOrWhiteSpace(dto.SenderEmail))
                throw new ArgumentException("O e-mail do remetente é obrigatório.");

            var smtpSettings = await _db.SmtpSettings.OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
            if (smtpSettings == null)
            {
                smtpSettings = new SmtpSettings { CreatedAtUtc = DateTime.UtcNow };
                _db.SmtpSettings.Add(smtpSettings);
            }

            smtpSettings.Server = dto.Server;
            smtpSettings.Port = dto.Port;
            smtpSettings.SenderEmail = dto.SenderEmail;
            smtpSettings.SenderName = dto.SenderName;
            smtpSettings.EnableSsl = dto.EnableSsl ?? true;
            smtpSettings.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("SMTP integration settings updated by user {UserId}", userId);

            await _logWriter.WriteAsync(
                "Information",
                "IntegrationSettings",
                "SETTINGS_UPDATED",
                $"SMTP integration settings updated",
                payload: System.Text.Json.JsonSerializer.Serialize(new
                {
                    providerCode = "SMTP",
                    server = dto.Server,
                    port = dto.Port,
                    senderEmail = dto.SenderEmail,
                    senderName = dto.SenderName,
                    enableSsl = dto.EnableSsl
                }));
            return;
        }

        if (provider.Settings?.IsReadOnly == true)
            throw new InvalidOperationException($"Provider '{code}' is read-only and cannot be modified.");

        var settings = provider.Settings;
        if (settings == null)
        {
            settings = new IntegrationProviderSettings
            {
                IntegrationProviderId = provider.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.IntegrationProviderSettings.Add(settings);
        }

        // Update non-secret fields
        settings.Server = dto.Server;
        settings.DatabaseName = dto.DatabaseName;
        settings.InstanceName = dto.InstanceName;
        settings.AuthenticationMode = dto.AuthenticationMode;
        settings.Username = dto.Username;
        settings.ApiBaseUrl = dto.ApiBaseUrl;
        settings.TimeoutSeconds = dto.TimeoutSeconds;
        settings.AdditionalConfig = dto.AdditionalConfig;
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Integration settings updated for provider {Code} by user {UserId}", code, userId);

        await _logWriter.WriteAsync(
            "Information",
            "IntegrationSettings",
            "SETTINGS_UPDATED",
            $"Integration settings updated for provider: {code}",
            payload: System.Text.Json.JsonSerializer.Serialize(new
            {
                providerCode = code,
                server = dto.Server,
                databaseName = dto.DatabaseName,
                instanceName = dto.InstanceName,
                authMode = dto.AuthenticationMode,
                username = dto.Username,
                apiBaseUrl = dto.ApiBaseUrl,
                timeoutSeconds = dto.TimeoutSeconds
                // NOTE: No secrets in log payload
            }));
    }

    public async Task ReplaceSecretAsync(string code, ReplaceIntegrationSecretDto dto, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.NewSecretValue))
            throw new ArgumentException("O novo valor do segredo não pode ser vazio.");

        if (dto.SecretType != "PASSWORD" && dto.SecretType != "API_KEY")
            throw new ArgumentException("SecretType deve ser 'PASSWORD' ou 'API_KEY'.");

        var provider = await _db.IntegrationProviders
            .Include(p => p.Settings)
            .FirstOrDefaultAsync(p => p.Code == code, ct);

        if (provider == null)
            throw new ArgumentException($"Provider '{code}' not found.");

        if (code == "SMTP")
        {
            if (dto.SecretType != "PASSWORD")
                throw new ArgumentException("SecretType para SMTP deve ser 'PASSWORD'.");

            var smtpSettings = await _db.SmtpSettings.OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
            if (smtpSettings == null)
            {
                smtpSettings = new SmtpSettings { CreatedAtUtc = DateTime.UtcNow };
                _db.SmtpSettings.Add(smtpSettings);
            }

            smtpSettings.EncryptedPassword = AesEncryptionHelper.Encrypt(dto.NewSecretValue, EncryptionKey);
            smtpSettings.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("SMTP secret updated (encrypted) by user {UserId}", userId);

            await _logWriter.WriteAsync(
                "Warning",
                "IntegrationSettings",
                "SECRET_ROTATED",
                "Secret rotated for provider: SMTP, type: PASSWORD");
            return;
        }

        if (provider.Settings?.IsReadOnly == true)
            throw new InvalidOperationException($"Provider '{code}' is read-only. Secret rotation is not permitted.");

        var settings = provider.Settings;
        if (settings == null)
        {
            settings = new IntegrationProviderSettings
            {
                IntegrationProviderId = provider.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.IntegrationProviderSettings.Add(settings);
        }

        var encrypted = AesEncryptionHelper.Encrypt(dto.NewSecretValue, EncryptionKey);

        if (dto.SecretType == "PASSWORD")
        {
            settings.EncryptedPassword = encrypted;
        }
        else // API_KEY
        {
            settings.ApiKeyEncrypted = encrypted;
        }

        settings.SecretVersion++;
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Secret rotated for provider {Code}, type {SecretType}, version {Version}",
            code, dto.SecretType, settings.SecretVersion);

        await _logWriter.WriteAsync(
            "Warning",
            "IntegrationSettings",
            "SECRET_ROTATED",
            $"Secret rotated for provider: {code}, type: {dto.SecretType}, new version: {settings.SecretVersion}");
    }

    public async Task SetEnabledAsync(string code, bool enabled, int userId, CancellationToken ct = default)
    {
        var provider = await _db.IntegrationProviders
            .FirstOrDefaultAsync(p => p.Code == code, ct);

        if (provider == null)
            throw new ArgumentException($"Provider '{code}' not found.");

        provider.IsEnabled = enabled;
        provider.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Provider {Code} {Action} by user {UserId}",
            code, enabled ? "enabled" : "disabled", userId);

        await _logWriter.WriteAsync(
            "Information",
            "IntegrationSettings",
            enabled ? "PROVIDER_ENABLED" : "PROVIDER_DISABLED",
            $"Provider {code} {(enabled ? "enabled" : "disabled")}");
    }

    /// <summary>
    /// Maps a provider entity + settings + connection status to a masked DTO.
    /// EncryptedPassword and ApiKeyEncrypted are NEVER included.
    /// </summary>
    private IntegrationSettingsDto MapToDto(IntegrationProvider provider)
    {
        var settings = provider.Settings;
        var status = provider.ConnectionStatus;

        // Resolve updated-by user name
        string? updatedByName = null;
        if (settings?.UpdatedByUserId != null)
        {
            updatedByName = _db.Users
                .Where(u => u.Id == settings.UpdatedByUserId)
                .Select(u => u.FullName)
                .FirstOrDefault();
        }

        var isEnabled = provider.IsEnabled;
        var smtpSettings = _db.SmtpSettings.OrderByDescending(s => s.Id).FirstOrDefault();

        var dynamicStatus = IntegrationHealthService.DetermineDisplayStatus(provider, isEnabled, smtpSettings, _configuration);

        var dto = new IntegrationSettingsDto
        {
            Code = provider.Code,
            Name = provider.Name,
            ProviderType = provider.ProviderType,
            ConnectionType = provider.ConnectionType,
            Description = provider.Description,
            Environment = provider.Environment,
            IsEnabled = isEnabled,
            IsPlanned = provider.IsPlanned,
            IsReadOnly = settings?.IsReadOnly ?? false,

            // Connection settings (non-secret)
            Server = settings?.Server,
            DatabaseName = settings?.DatabaseName,
            InstanceName = settings?.InstanceName,
            AuthenticationMode = settings?.AuthenticationMode,
            Username = settings?.Username,
            ApiBaseUrl = settings?.ApiBaseUrl,
            TimeoutSeconds = settings?.TimeoutSeconds,
            AdditionalConfig = settings?.AdditionalConfig,

            // Secret presence only — NEVER the actual value
            HasPassword = !string.IsNullOrEmpty(settings?.EncryptedPassword),
            HasApiKey = !string.IsNullOrEmpty(settings?.ApiKeyEncrypted),
            SecretVersion = settings?.SecretVersion ?? 0,

            // Connection test status
            LastTestStatus = dynamicStatus,
            LastTestAt = status?.LastCheckedAtUtc,
            LastTestMessage = status?.LastErrorMessage,
            LastTestResponseTimeMs = status?.LastResponseTimeMs,

            // Audit
            UpdatedByUserName = updatedByName,
            UpdatedAt = settings?.UpdatedAtUtc
        };

        if (provider.Code == "SMTP")
        {
            var smtp = _db.SmtpSettings.OrderByDescending(s => s.Id).FirstOrDefault();
            if (smtp != null)
            {
                dto.Server = smtp.Server;
                dto.Port = smtp.Port;
                dto.EnableSsl = smtp.EnableSsl;
                dto.SenderEmail = smtp.SenderEmail;
                dto.SenderName = smtp.SenderName;
                dto.Username = smtp.SenderEmail; // SMTP Username is the sender email
                dto.HasPassword = !string.IsNullOrEmpty(smtp.EncryptedPassword);
                dto.UpdatedAt = smtp.UpdatedAtUtc;
                dto.IsReadOnly = false;
            }
        }

        if (provider.Code == "PRIMAVERA")
        {
            dto.PrimaveraCompanies = new List<PrimaveraCompanySettingsDto>();
            foreach (var company in System.Enum.GetValues<AlplaPortal.Application.Interfaces.Integration.PrimaveraCompany>())
            {
                var companyKey = company.ToString();
                var companyDto = new PrimaveraCompanySettingsDto
                {
                    CompanyKey = companyKey,
                    Enabled = true, // default fallback
                    SecretVersion = 0
                };

                // Fallback database name from configuration
                var companySection = _configuration.GetSection($"Integrations:Primavera:Companies:{companyKey}");
                companyDto.DatabaseName = companySection["DatabaseName"];
                companyDto.Username = companySection["Username"];
                companyDto.HasPassword = !string.IsNullOrEmpty(companySection["Password"]);

                // Override with DB AdditionalConfig if present
                if (settings != null && !string.IsNullOrWhiteSpace(settings.AdditionalConfig))
                {
                    try
                    {
                        var parsed = System.Text.Json.JsonSerializer.Deserialize<PrimaveraAdditionalConfig>(
                            settings.AdditionalConfig,
                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                        if (parsed?.Companies != null && parsed.Companies.TryGetValue(companyKey, out var compSettings))
                        {
                            companyDto.DatabaseName = compSettings.DatabaseName ?? companyDto.DatabaseName;
                            companyDto.Enabled = compSettings.Enabled;
                            companyDto.Username = compSettings.Username ?? companyDto.Username;
                            companyDto.HasPassword = !string.IsNullOrEmpty(compSettings.EncryptedPassword) || companyDto.HasPassword;
                            companyDto.SecretVersion = compSettings.SecretVersion;
                        }
                    }
                    catch
                    {
                        // ignore malformed JSON in DB
                    }
                }

                dto.PrimaveraCompanies.Add(companyDto);
            }
        }

        return dto;
    }

    public async Task UpdatePrimaveraCompanyAsync(UpdatePrimaveraCompanyDto dto, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.CompanyKey))
            throw new ArgumentException("O código da empresa é obrigatório.");

        if (!System.Enum.TryParse<AlplaPortal.Application.Interfaces.Integration.PrimaveraCompany>(dto.CompanyKey, true, out var company))
            throw new ArgumentException($"Empresa '{dto.CompanyKey}' inválida.");

        var provider = await _db.IntegrationProviders
            .Include(p => p.Settings)
            .FirstOrDefaultAsync(p => p.Code == "PRIMAVERA", ct);

        if (provider == null)
            throw new ArgumentException("Provedor PRIMAVERA não encontrado.");

        var settings = provider.Settings;
        if (settings == null)
        {
            settings = new IntegrationProviderSettings
            {
                IntegrationProviderId = provider.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.IntegrationProviderSettings.Add(settings);
        }

        // Parse existing config
        var config = new PrimaveraAdditionalConfig { Companies = new Dictionary<string, PrimaveraCompanyConfig>() };
        if (!string.IsNullOrWhiteSpace(settings.AdditionalConfig))
        {
            try
            {
                config = System.Text.Json.JsonSerializer.Deserialize<PrimaveraAdditionalConfig>(
                    settings.AdditionalConfig,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? config;
            }
            catch
            {
                // overwrite if malformed
            }
        }

        config.Companies ??= new Dictionary<string, PrimaveraCompanyConfig>();

        var companyKey = company.ToString();
        if (!config.Companies.TryGetValue(companyKey, out var compSettings))
        {
            compSettings = new PrimaveraCompanyConfig { Enabled = true };
            config.Companies[companyKey] = compSettings;
        }

        // Update properties
        compSettings.DatabaseName = dto.DatabaseName;
        compSettings.Enabled = dto.Enabled;
        compSettings.Username = dto.Username;

        settings.AdditionalConfig = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Primavera company settings updated for {Company} by user {UserId}", companyKey, userId);

        await _logWriter.WriteAsync(
            "Information",
            "IntegrationSettings",
            "SETTINGS_UPDATED",
            $"Primavera company settings updated for: {companyKey}",
            payload: System.Text.Json.JsonSerializer.Serialize(new
            {
                companyKey,
                databaseName = dto.DatabaseName,
                enabled = dto.Enabled,
                username = dto.Username
            }));
    }

    public async Task ReplacePrimaveraCompanySecretAsync(ReplacePrimaveraCompanySecretDto dto, int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.CompanyKey))
            throw new ArgumentException("O código da empresa é obrigatório.");

        if (!System.Enum.TryParse<AlplaPortal.Application.Interfaces.Integration.PrimaveraCompany>(dto.CompanyKey, true, out var company))
            throw new ArgumentException($"Empresa '{dto.CompanyKey}' inválida.");

        if (string.IsNullOrWhiteSpace(dto.NewPassword))
            throw new ArgumentException("A nova senha não pode ser vazia.");

        var provider = await _db.IntegrationProviders
            .Include(p => p.Settings)
            .FirstOrDefaultAsync(p => p.Code == "PRIMAVERA", ct);

        if (provider == null)
            throw new ArgumentException("Provedor PRIMAVERA não encontrado.");

        var settings = provider.Settings;
        if (settings == null)
        {
            settings = new IntegrationProviderSettings
            {
                IntegrationProviderId = provider.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.IntegrationProviderSettings.Add(settings);
        }

        // Parse existing config
        var config = new PrimaveraAdditionalConfig { Companies = new Dictionary<string, PrimaveraCompanyConfig>() };
        if (!string.IsNullOrWhiteSpace(settings.AdditionalConfig))
        {
            try
            {
                config = System.Text.Json.JsonSerializer.Deserialize<PrimaveraAdditionalConfig>(
                    settings.AdditionalConfig,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? config;
            }
            catch
            {
                // overwrite if malformed
            }
        }

        config.Companies ??= new Dictionary<string, PrimaveraCompanyConfig>();

        var companyKey = company.ToString();
        if (!config.Companies.TryGetValue(companyKey, out var compSettings))
        {
            compSettings = new PrimaveraCompanyConfig { Enabled = true };
            config.Companies[companyKey] = compSettings;
        }

        // Encrypt and save secret
        compSettings.EncryptedPassword = AesEncryptionHelper.Encrypt(dto.NewPassword, EncryptionKey);
        compSettings.SecretVersion++;

        settings.AdditionalConfig = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Primavera company password rotated for {Company} by user {UserId}", companyKey, userId);

        await _logWriter.WriteAsync(
            "Warning",
            "IntegrationSettings",
            "SECRET_ROTATED",
            $"Secret rotated for Primavera company: {companyKey}");
    }

    private class PrimaveraAdditionalConfig
    {
        public Dictionary<string, PrimaveraCompanyConfig>? Companies { get; set; }
    }

    private class PrimaveraCompanyConfig
    {
        public string? DatabaseName { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Username { get; set; }
        public string? EncryptedPassword { get; set; }
        public int SecretVersion { get; set; }
    }
}
