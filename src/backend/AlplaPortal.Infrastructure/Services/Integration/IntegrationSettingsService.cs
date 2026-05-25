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

        return new IntegrationSettingsDto
        {
            Code = provider.Code,
            Name = provider.Name,
            ProviderType = provider.ProviderType,
            ConnectionType = provider.ConnectionType,
            Description = provider.Description,
            Environment = provider.Environment,
            IsEnabled = provider.IsEnabled,
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
            LastTestStatus = status?.CurrentStatus,
            LastTestAt = status?.LastCheckedAtUtc,
            LastTestMessage = status?.LastErrorMessage,
            LastTestResponseTimeMs = status?.LastResponseTimeMs,

            // Audit
            UpdatedByUserName = updatedByName,
            UpdatedAt = settings?.UpdatedAtUtc
        };
    }
}
