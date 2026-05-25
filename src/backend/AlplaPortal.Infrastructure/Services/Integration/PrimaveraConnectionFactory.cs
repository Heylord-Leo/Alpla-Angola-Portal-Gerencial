using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Infrastructure.Data;
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

        // ─── Resolve company database name ───

        var companyKey = company.ToString();
        var databaseName = ResolveCompanyDatabase(resolved, companyKey);

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                $"Primavera company '{companyKey}' is not configured. " +
                $"Source: {resolved.Source}. " +
                (resolved.Source == "DATABASE"
                    ? "Add company mapping in AdditionalConfig JSON."
                    : $"Missing DatabaseName in Integrations:Primavera:Companies:{companyKey}."));
        }

        // Check company-level enabled flag (config fallback only)
        if (resolved.Source == "CONFIGURATION")
        {
            var companySection = _configuration.GetSection($"Integrations:Primavera:Companies:{companyKey}");
            var companyEnabled = companySection.GetValue<bool?>("Enabled") ?? true;
            if (!companyEnabled)
            {
                throw new InvalidOperationException(
                    $"Primavera company '{companyKey}' is disabled in configuration.");
            }
        }

        // ─── Build connection string ───

        var connectionString = BuildConnectionString(resolved, databaseName);

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
    private static string BuildConnectionString(IntegrationConfigResolver.ResolvedSqlSettings resolved, string databaseName)
    {
        var dataSource = string.IsNullOrWhiteSpace(resolved.InstanceName)
            ? resolved.Server!
            : $"{resolved.Server}\\{resolved.InstanceName}";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = databaseName,
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
            if (string.IsNullOrWhiteSpace(resolved.Username) || string.IsNullOrWhiteSpace(resolved.DecryptedPassword))
            {
                throw new InvalidOperationException(
                    "Primavera SQL Authentication credentials are not fully configured. Required: Username, Password.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = resolved.Username;
            builder.Password = resolved.DecryptedPassword;
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
    }
}
