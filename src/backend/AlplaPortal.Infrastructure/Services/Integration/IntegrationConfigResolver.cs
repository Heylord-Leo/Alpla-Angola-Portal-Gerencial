using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Shared cascade resolver for integration provider settings.
///
/// Resolution order:
///   1. Database-backed IntegrationProviderSettings (encrypted secrets decrypted at runtime).
///   2. IConfiguration / appsettings / env vars (fallback for local development).
///   3. Safe disabled/not-configured state if neither source is available.
///
/// This resolver is consumed by PrimaveraConnectionFactory, InnuxConnectionFactory,
/// DocumentExtractionSettingsService, and the test-connection flow.
///
/// Security guarantees:
///   - Decrypted secrets are NEVER logged.
///   - Decrypted secrets are NEVER included in exceptions.
///   - Decrypted secrets are NEVER returned to the frontend.
///   - IsReadOnly flag is exposed for caller enforcement.
/// </summary>
public class IntegrationConfigResolver
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IntegrationConfigResolver> _logger;

    private string EncryptionKey => _configuration["AppConfig:EncryptionKey"] ?? string.Empty;

    public IntegrationConfigResolver(
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger<IntegrationConfigResolver> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Resolved settings for a SQL-based integration provider.
    /// Contains server, database, authentication, and optional decrypted password.
    /// </summary>
    public record ResolvedSqlSettings
    {
        public string Source { get; init; } = "NONE";
        public bool IsEnabled { get; init; }
        public bool IsReadOnly { get; init; }
        public string? Server { get; init; }
        public string? InstanceName { get; init; }
        public string? DatabaseName { get; init; }
        public string AuthenticationMode { get; init; } = "SQL";
        public string? Username { get; init; }
        public string? DecryptedPassword { get; init; }
        public int TimeoutSeconds { get; init; } = 15;
        public string? AdditionalConfig { get; init; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Server);
    }

    /// <summary>
    /// Resolved settings for an API-based integration provider.
    /// Contains API base URL, optional decrypted API key, and configuration.
    /// </summary>
    public record ResolvedApiSettings
    {
        public string Source { get; init; } = "NONE";
        public bool IsEnabled { get; init; }
        public bool IsReadOnly { get; init; }
        public string? ApiBaseUrl { get; init; }
        public string? DecryptedApiKey { get; init; }
        public int TimeoutSeconds { get; init; } = 30;
        public string? AdditionalConfig { get; init; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(DecryptedApiKey);
    }

    /// <summary>
    /// Resolves SQL connection settings for a provider using the cascade:
    /// DB → IConfiguration → disabled.
    /// </summary>
    public async Task<ResolvedSqlSettings> ResolveSqlSettingsAsync(
        string providerCode,
        string configSectionPath,
        CancellationToken ct = default)
    {
        // ── Layer 1: Database ──
        var dbSettings = await GetDbSettingsAsync(providerCode, ct);

        if (dbSettings != null && HasSqlConfiguration(dbSettings))
        {
            _logger.LogDebug(
                "IntegrationConfigResolver: using DB-backed settings for {Provider}",
                providerCode);

            string? decryptedPassword = null;
            if (!string.IsNullOrEmpty(dbSettings.EncryptedPassword))
            {
                try
                {
                    decryptedPassword = AesEncryptionHelper.Decrypt(dbSettings.EncryptedPassword, EncryptionKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to decrypt password for provider {Provider}. Falling back to IConfiguration.",
                        providerCode);
                    // Fall through to config fallback
                    return await ResolveSqlFromConfigAsync(configSectionPath, providerCode);
                }
            }

            var provider = await GetProviderAsync(providerCode, ct);

            return new ResolvedSqlSettings
            {
                Source = "DATABASE",
                IsEnabled = provider?.IsEnabled ?? false,
                IsReadOnly = dbSettings.IsReadOnly,
                Server = dbSettings.Server,
                InstanceName = dbSettings.InstanceName,
                DatabaseName = dbSettings.DatabaseName,
                AuthenticationMode = dbSettings.AuthenticationMode ?? "SQL",
                Username = dbSettings.Username,
                DecryptedPassword = decryptedPassword,
                TimeoutSeconds = dbSettings.TimeoutSeconds ?? 15,
                AdditionalConfig = dbSettings.AdditionalConfig
            };
        }

        // ── Layer 2: IConfiguration fallback ──
        _logger.LogDebug(
            "IntegrationConfigResolver: no DB settings for {Provider}, falling back to IConfiguration",
            providerCode);

        return await ResolveSqlFromConfigAsync(configSectionPath, providerCode);
    }

    /// <summary>
    /// Resolves API settings (like OpenAI API key) using the cascade:
    /// DB → IConfiguration/env var → disabled.
    /// </summary>
    public async Task<ResolvedApiSettings> ResolveApiSettingsAsync(
        string providerCode,
        string? envVarName = null,
        CancellationToken ct = default)
    {
        // ── Layer 1: Database ──
        var dbSettings = await GetDbSettingsAsync(providerCode, ct);

        if (dbSettings != null && !string.IsNullOrEmpty(dbSettings.ApiKeyEncrypted))
        {
            _logger.LogDebug(
                "IntegrationConfigResolver: using DB-backed API key for {Provider}",
                providerCode);

            string? decryptedApiKey = null;
            try
            {
                decryptedApiKey = AesEncryptionHelper.Decrypt(dbSettings.ApiKeyEncrypted, EncryptionKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to decrypt API key for provider {Provider}. Falling back to environment variable.",
                    providerCode);
                // Fall through to env var fallback
            }

            if (!string.IsNullOrEmpty(decryptedApiKey))
            {
                var provider = await GetProviderAsync(providerCode, ct);
                return new ResolvedApiSettings
                {
                    Source = "DATABASE",
                    IsEnabled = provider?.IsEnabled ?? false,
                    IsReadOnly = dbSettings.IsReadOnly,
                    ApiBaseUrl = dbSettings.ApiBaseUrl,
                    DecryptedApiKey = decryptedApiKey,
                    TimeoutSeconds = dbSettings.TimeoutSeconds ?? 30,
                    AdditionalConfig = dbSettings.AdditionalConfig
                };
            }
        }

        // ── Layer 2: Environment variable / IConfiguration fallback ──
        if (!string.IsNullOrWhiteSpace(envVarName))
        {
            var envValue = _configuration[envVarName];
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                _logger.LogDebug(
                    "IntegrationConfigResolver: using env var {EnvVar} for {Provider}",
                    envVarName, providerCode);

                var provider = await GetProviderAsync(providerCode, ct);
                return new ResolvedApiSettings
                {
                    Source = "CONFIGURATION",
                    IsEnabled = provider?.IsEnabled ?? true, // Env var implies intentional setup
                    IsReadOnly = false,
                    DecryptedApiKey = envValue,
                    TimeoutSeconds = 30
                };
            }
        }

        // ── Layer 3: Not configured ──
        _logger.LogDebug(
            "IntegrationConfigResolver: no API key found for {Provider} (DB or env var)",
            providerCode);

        return new ResolvedApiSettings
        {
            Source = "NONE",
            IsEnabled = false,
            IsReadOnly = false
        };
    }

    // ── Private helpers ──

    private async Task<IntegrationProviderSettings?> GetDbSettingsAsync(string providerCode, CancellationToken ct)
    {
        return await _db.IntegrationProviderSettings
            .Include(s => s.Provider)
            .FirstOrDefaultAsync(s => s.Provider.Code == providerCode, ct);
    }

    private async Task<IntegrationProvider?> GetProviderAsync(string providerCode, CancellationToken ct)
    {
        return await _db.IntegrationProviders
            .FirstOrDefaultAsync(p => p.Code == providerCode, ct);
    }

    private static bool HasSqlConfiguration(IntegrationProviderSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.Server) ||
               (settings.Provider?.Code == "ALPLAPROD" && !string.IsNullOrWhiteSpace(settings.AdditionalConfig));
    }

    private Task<ResolvedSqlSettings> ResolveSqlFromConfigAsync(string configSectionPath, string providerCode)
    {
        var section = _configuration.GetSection(configSectionPath);
        var enabledRaw = section["Enabled"];
        var isEnabled = bool.TryParse(enabledRaw, out var e) && e;

        var server = section["Server"];
        if (string.IsNullOrWhiteSpace(server))
        {
            // Layer 3: Not configured at all
            return Task.FromResult(new ResolvedSqlSettings
            {
                Source = "NONE",
                IsEnabled = false,
                IsReadOnly = false
            });
        }

        return Task.FromResult(new ResolvedSqlSettings
        {
            Source = "CONFIGURATION",
            IsEnabled = isEnabled,
            IsReadOnly = false,
            Server = server,
            InstanceName = section["InstanceName"],
            DatabaseName = section["DatabaseName"],
            AuthenticationMode = section["AuthenticationMode"]?.ToUpperInvariant() ?? "SQL",
            Username = section["Username"],
            DecryptedPassword = section["Password"],
            TimeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var t) && t > 0 ? t : 15,
            AdditionalConfig = null
        });
    }
}
