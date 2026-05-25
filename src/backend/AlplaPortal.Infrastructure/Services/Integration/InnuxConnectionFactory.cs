using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Shared factory that resolves SQL connections to the configured Innux database.
///
/// Unlike PrimaveraConnectionFactory, Innux has a single database target —
/// no company routing, no multi-database strategy.
///
/// Settings cascade (Phase 3):
///   1. Database-backed IntegrationProviderSettings for code "INNUX"
///      → Server, InstanceName, DatabaseName, AuthMode, Username, EncryptedPassword
///   2. IConfiguration fallback: Integrations:Innux section
///   3. Safe disabled state if neither is available
///
/// Reusable by any Innux domain service (employees, attendance,
/// terminals, etc.) — this factory provides connections only.
/// </summary>
public class InnuxConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly IntegrationConfigResolver _configResolver;
    private readonly ILogger<InnuxConnectionFactory> _logger;

    public InnuxConnectionFactory(
        IConfiguration configuration,
        IntegrationConfigResolver configResolver,
        ILogger<InnuxConnectionFactory> logger)
    {
        _configuration = configuration;
        _configResolver = configResolver;
        _logger = logger;
    }

    /// <summary>
    /// Creates and opens a SQL connection to the Innux database.
    /// Uses the settings cascade: DB → IConfiguration → disabled.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Innux is not enabled, credentials are missing,
    /// or required configuration is incomplete.
    /// </exception>
    public async Task<SqlConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        var resolved = await _configResolver.ResolveSqlSettingsAsync(
            "INNUX", "Integrations:Innux", ct);

        // ─── Validate provider-level configuration ───

        if (!resolved.IsEnabled)
        {
            throw new InvalidOperationException("Innux integration is not enabled.");
        }

        if (!resolved.IsConfigured)
        {
            throw new InvalidOperationException("Innux server is not configured.");
        }

        // For Innux, DatabaseName comes from the resolved settings or config section
        var databaseName = resolved.DatabaseName;
        if (resolved.Source == "CONFIGURATION" && string.IsNullOrWhiteSpace(databaseName))
        {
            databaseName = _configuration["Integrations:Innux:DatabaseName"];
        }

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Innux database name is not configured.");
        }

        // ─── Build connection string ───

        var connectionString = BuildConnectionString(resolved, databaseName);

        _logger.LogDebug(
            "InnuxConnectionFactory: opening connection to database {Database}, server {Server}, source {Source}",
            databaseName, resolved.Server, resolved.Source);

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
    /// Returns the configured database name for diagnostics/logging.
    /// Checks DB-backed settings first, then IConfiguration fallback.
    /// Does not open a connection.
    /// </summary>
    public string? GetDatabaseName()
    {
        // Synchronous — acceptable for diagnostics
        var resolved = _configResolver.ResolveSqlSettingsAsync(
            "INNUX", "Integrations:Innux").GetAwaiter().GetResult();

        if (resolved.Source == "DATABASE" && !string.IsNullOrWhiteSpace(resolved.DatabaseName))
            return resolved.DatabaseName;

        return _configuration["Integrations:Innux:DatabaseName"];
    }

    /// <summary>
    /// Returns the configured server/instance data source for diagnostics/logging.
    /// Checks DB-backed settings first, then IConfiguration fallback.
    /// Does not open a connection.
    /// </summary>
    public string? GetDataSource()
    {
        // Synchronous — acceptable for diagnostics
        var resolved = _configResolver.ResolveSqlSettingsAsync(
            "INNUX", "Integrations:Innux").GetAwaiter().GetResult();

        if (resolved.Source == "DATABASE" && resolved.IsConfigured)
        {
            return string.IsNullOrWhiteSpace(resolved.InstanceName)
                ? resolved.Server
                : $"{resolved.Server}\\{resolved.InstanceName}";
        }

        var section = _configuration.GetSection("Integrations:Innux");
        var server = section["Server"];
        var instanceName = section["InstanceName"];

        if (string.IsNullOrWhiteSpace(server)) return null;

        return string.IsNullOrWhiteSpace(instanceName)
            ? server
            : $"{server}\\{instanceName}";
    }

    /// <summary>
    /// Builds a SQL Server connection string from resolved settings.
    /// </summary>
    private static string BuildConnectionString(
        IntegrationConfigResolver.ResolvedSqlSettings resolved, string databaseName)
    {
        var server = resolved.Server ?? string.Empty;
        var instanceName = resolved.InstanceName;
        var authMode = resolved.AuthenticationMode;

        var dataSource = string.IsNullOrWhiteSpace(instanceName)
            ? server
            : $"{server}\\{instanceName}";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = databaseName,
            ConnectTimeout = resolved.TimeoutSeconds,
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            ApplicationName = "AlplaPortal_Integration_Innux"
        };

        if (authMode == "WINDOWS")
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(resolved.Username) || string.IsNullOrWhiteSpace(resolved.DecryptedPassword))
            {
                throw new InvalidOperationException(
                    "Innux SQL Authentication credentials are not fully configured. Required: Username, Password.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = resolved.Username;
            builder.Password = resolved.DecryptedPassword;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Returns timeout seconds from resolved settings.
    /// Used by InnuxIntegrationProvider for command timeouts.
    /// </summary>
    internal static int GetTimeoutSeconds(IConfigurationSection section)
    {
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0)
            return timeout;
        return 15;
    }
}
