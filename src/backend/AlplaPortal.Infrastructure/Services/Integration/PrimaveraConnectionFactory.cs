using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Shared, domain-neutral factory that resolves SQL connections for any configured
/// Primavera business company/database target.
///
/// Settings cascade (Phase 3):
///   1. Database-backed IntegrationProviderSettings for code "PRIMAVERA"
///      → Server, InstanceName, AuthMode, Username, EncryptedPassword, AdditionalConfig (company DBs)
///   2. IConfiguration fallback: Integrations:Primavera section
///   3. Safe disabled state if neither is available
///
/// AdditionalConfig JSON schema for company databases:
///   { "Companies": { "ALPLAPLASTICO": { "DatabaseName": "..." }, "ALPLASOPRO": { "DatabaseName": "..." } } }
///
/// This factory is intentionally domain-neutral — it provides connections only.
/// Employee queries, material queries, supplier queries, etc. belong in their
/// respective domain services that consume this factory.
/// </summary>
public class PrimaveraConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly IntegrationConfigResolver _configResolver;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PrimaveraConnectionFactory> _logger;

    public PrimaveraConnectionFactory(
        IConfiguration configuration,
        IntegrationConfigResolver configResolver,
        ApplicationDbContext db,
        ILogger<PrimaveraConnectionFactory> logger)
    {
        _configuration = configuration;
        _configResolver = configResolver;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Creates and opens a SQL connection to the specified Primavera company database.
    /// Uses the settings cascade: DB → IConfiguration → disabled.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Primavera is not enabled, credentials are missing,
    /// or the requested company is not configured.
    /// </exception>
    public async Task<SqlConnection> CreateConnectionAsync(
        PrimaveraCompany company, CancellationToken ct = default)
    {
        var resolved = await _configResolver.ResolveSqlSettingsAsync(
            "PRIMAVERA", "Integrations:Primavera", ct);

        // ─── Validate provider is enabled and configured ───

        if (!resolved.IsEnabled)
        {
            throw new InvalidOperationException("Primavera integration is not enabled.");
        }

        if (!resolved.IsConfigured)
        {
            throw new InvalidOperationException("Primavera server is not configured.");
        }

        // ─── Resolve company settings ───

        var companyKey = company.ToString();
        var companySettings = ResolveCompanySettings(resolved, companyKey);

        if (!companySettings.Enabled)
        {
            throw new InvalidOperationException($"Primavera company '{companyKey}' is disabled.");
        }

        var databaseName = companySettings.DatabaseName;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                $"Primavera company '{companyKey}' database name is not configured. " +
                $"Source: {resolved.Source}.");
        }

        // ─── Build connection string ───

        var connectionString = BuildConnectionString(resolved, companySettings);

        _logger.LogDebug(
            "PrimaveraConnectionFactory: opening connection for company {Company}, database {Database}, server {Server}, source {Source}",
            companyKey, databaseName, resolved.Server, resolved.Source);

        var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public record PrimaveraCompanySettings
    {
        public string? DatabaseName { get; init; }
        public bool Enabled { get; init; } = true;
        public string? Username { get; init; }
        public bool HasPassword { get; init; }
    }

    public async Task<(bool IsProviderEnabled, bool IsConfigured, string? Server, string? AuthMode, PrimaveraCompanySettings Settings)> GetCompanySettingsAsync(
        PrimaveraCompany company, CancellationToken ct = default)
    {
        var resolved = await _configResolver.ResolveSqlSettingsAsync(
            "PRIMAVERA", "Integrations:Primavera", ct);

        var companyKey = company.ToString();
        var companySettings = ResolveCompanySettings(resolved, companyKey);

        return (
            resolved.IsEnabled,
            resolved.IsConfigured,
            resolved.Server,
            resolved.AuthenticationMode,
            new PrimaveraCompanySettings
            {
                DatabaseName = companySettings.DatabaseName,
                Enabled = companySettings.Enabled,
                Username = companySettings.Username,
                HasPassword = !string.IsNullOrWhiteSpace(companySettings.DecryptedPassword)
            }
        );
    }

    private string EncryptionKey => _configuration["AppConfig:EncryptionKey"] ?? string.Empty;

    private ResolvedCompanySettings ResolveCompanySettings(
        IntegrationConfigResolver.ResolvedSqlSettings resolved, string companyKey)
    {
        // 1. Try database-backed AdditionalConfig
        if (resolved.Source == "DATABASE" && !string.IsNullOrWhiteSpace(resolved.AdditionalConfig))
        {
            try
            {
                var additionalConfig = JsonSerializer.Deserialize<PrimaveraAdditionalConfig>(
                    resolved.AdditionalConfig,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (additionalConfig?.Companies != null &&
                    additionalConfig.Companies.TryGetValue(companyKey, out var companyConfig))
                {
                    string? decryptedPassword = null;
                    if (!string.IsNullOrWhiteSpace(companyConfig.EncryptedPassword))
                    {
                        try
                        {
                            decryptedPassword = AesEncryptionHelper.Decrypt(
                                companyConfig.EncryptedPassword, EncryptionKey);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to decrypt Primavera company password for {Company}", companyKey);
                        }
                    }

                    return new ResolvedCompanySettings
                    {
                        DatabaseName = companyConfig.DatabaseName,
                        Enabled = companyConfig.Enabled,
                        Username = companyConfig.Username,
                        DecryptedPassword = decryptedPassword
                    };
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse AdditionalConfig JSON for Primavera. Falling back to IConfiguration.");
            }
        }

        // 2. Fallback: IConfiguration
        var companySection = _configuration.GetSection($"Integrations:Primavera:Companies:{companyKey}");
        return new ResolvedCompanySettings
        {
            DatabaseName = companySection["DatabaseName"],
            Enabled = companySection.GetValue<bool?>("Enabled") ?? true,
            Username = companySection["Username"],
            DecryptedPassword = companySection["Password"]
        };
    }

    /// <summary>
    /// Returns the list of PrimaveraCompany values that have a DatabaseName configured.
    /// Checks both DB-backed AdditionalConfig and IConfiguration.
    /// </summary>
    public IReadOnlyList<PrimaveraCompany> GetConfiguredCompanies()
    {
        var result = new List<PrimaveraCompany>();

        // Try DB-backed AdditionalConfig first
        var dbCompanies = GetCompaniesFromDbConfig();
        if (dbCompanies != null && dbCompanies.Count > 0)
        {
            foreach (var company in Enum.GetValues<PrimaveraCompany>())
            {
                if (dbCompanies.TryGetValue(company.ToString(), out var config) &&
                    !string.IsNullOrWhiteSpace(config.DatabaseName))
                {
                    result.Add(company);
                }
            }
            if (result.Count > 0) return result;
        }

        // Fallback: IConfiguration
        var section = _configuration.GetSection("Integrations:Primavera:Companies");
        foreach (var company in Enum.GetValues<PrimaveraCompany>())
        {
            var dbName = section[$"{company}:DatabaseName"];
            if (!string.IsNullOrWhiteSpace(dbName))
            {
                result.Add(company);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the configured database name for a specific company, or null if not configured.
    /// Checks DB-backed AdditionalConfig first, then IConfiguration fallback.
    /// </summary>
    public string? GetDatabaseName(PrimaveraCompany company)
    {
        var companyKey = company.ToString();

        // Try DB-backed config
        var dbCompanies = GetCompaniesFromDbConfig();
        if (dbCompanies != null &&
            dbCompanies.TryGetValue(companyKey, out var config) &&
            !string.IsNullOrWhiteSpace(config.DatabaseName))
        {
            return config.DatabaseName;
        }

        // Fallback to IConfiguration
        return _configuration[$"Integrations:Primavera:Companies:{companyKey}:DatabaseName"];
    }

    /// <summary>
    /// Returns the first configured company for use as the default health check target.
    /// Returns null if no companies are configured.
    /// </summary>
    public PrimaveraCompany? GetDefaultCompany()
    {
        var configured = GetConfiguredCompanies();
        return configured.Count > 0 ? configured[0] : null;
    }

    // ── Private helpers ──

    /// <summary>
    /// Resolves a company database name from either DB AdditionalConfig JSON or IConfiguration.
    /// </summary>
    private string? ResolveCompanyDatabase(IntegrationConfigResolver.ResolvedSqlSettings resolved, string companyKey)
    {
        // If settings came from DB, parse company mappings from AdditionalConfig JSON
        if (resolved.Source == "DATABASE" && !string.IsNullOrWhiteSpace(resolved.AdditionalConfig))
        {
            try
            {
                var additionalConfig = JsonSerializer.Deserialize<PrimaveraAdditionalConfig>(
                    resolved.AdditionalConfig,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (additionalConfig?.Companies != null &&
                    additionalConfig.Companies.TryGetValue(companyKey, out var companyConfig) &&
                    !string.IsNullOrWhiteSpace(companyConfig.DatabaseName))
                {
                    return companyConfig.DatabaseName;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse AdditionalConfig JSON for Primavera. Falling back to IConfiguration.");
            }
        }

        // Fallback: IConfiguration
        return _configuration[$"Integrations:Primavera:Companies:{companyKey}:DatabaseName"];
    }

    /// <summary>
    /// Parses DB-backed company config from AdditionalConfig.
    /// Returns null if no DB settings or parsing fails.
    /// </summary>
    private Dictionary<string, PrimaveraCompanyConfig>? GetCompaniesFromDbConfig()
    {
        try
        {
            var settings = _db.IntegrationProviderSettings
                .Include(s => s.Provider)
                .FirstOrDefault(s => s.Provider.Code == "PRIMAVERA");

            if (settings == null || string.IsNullOrWhiteSpace(settings.AdditionalConfig))
                return null;

            var additionalConfig = JsonSerializer.Deserialize<PrimaveraAdditionalConfig>(
                settings.AdditionalConfig,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return additionalConfig?.Companies;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a SQL Server connection string from resolved settings.
    /// </summary>
    private static string BuildConnectionString(
        IntegrationConfigResolver.ResolvedSqlSettings resolved, ResolvedCompanySettings companySettings)
    {
        var dataSource = string.IsNullOrWhiteSpace(resolved.InstanceName)
            ? resolved.Server!
            : $"{resolved.Server}\\{resolved.InstanceName}";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = companySettings.DatabaseName,
            ConnectTimeout = resolved.TimeoutSeconds,
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            ApplicationName = "AlplaPortal_Integration_Primavera"
        };

        if (resolved.AuthenticationMode == "WINDOWS")
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            // Use company-specific credentials, fallback to provider-level
            var username = !string.IsNullOrWhiteSpace(companySettings.Username)
                ? companySettings.Username
                : resolved.Username;

            var password = !string.IsNullOrWhiteSpace(companySettings.DecryptedPassword)
                ? companySettings.DecryptedPassword
                : resolved.DecryptedPassword;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "Primavera SQL Authentication credentials are not fully configured. Required: Username, Password.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = username;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    // ── JSON models for AdditionalConfig ──

    private record PrimaveraAdditionalConfig
    {
        public Dictionary<string, PrimaveraCompanyConfig>? Companies { get; init; }
    }

    private record PrimaveraCompanyConfig
    {
        public string? DatabaseName { get; init; }
        public bool Enabled { get; init; } = true;
        public string? Username { get; init; }
        public string? EncryptedPassword { get; init; }
        public int SecretVersion { get; set; }
    }

    private record ResolvedCompanySettings
    {
        public string? DatabaseName { get; init; }
        public bool Enabled { get; init; } = true;
        public string? Username { get; init; }
        public string? DecryptedPassword { get; init; }
    }
}
