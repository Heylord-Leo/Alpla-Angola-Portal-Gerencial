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
/// Reads connection settings from Integrations:Innux section:
/// - Server, InstanceName, DatabaseName
/// - AuthenticationMode (SQL / WINDOWS)
/// - Username, Password (for SQL auth)
/// - TimeoutSeconds
///
/// Reusable by any future Innux domain service (employees, attendance,
/// terminals, etc.) — this factory provides connections only.
/// </summary>
public class InnuxConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<InnuxConnectionFactory> _logger;

    public InnuxConnectionFactory(
        IConfiguration configuration,
        ILogger<InnuxConnectionFactory> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Creates and opens a SQL connection to the Innux database.
    /// Validates configuration completeness before attempting connection.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Innux is not enabled, credentials are missing,
    /// or required configuration is incomplete.
    /// </exception>
    public async Task<SqlConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        var section = _configuration.GetSection("Integrations:Innux");

        // ─── Validate provider-level configuration ───

        var enabledRaw = section["Enabled"];
        if (!bool.TryParse(enabledRaw, out var isEnabled) || !isEnabled)
        {
            throw new InvalidOperationException("Innux integration is not enabled.");
        }

        var server = section["Server"];
        var databaseName = section["DatabaseName"];

        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException("Innux server is not configured.");
        }

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Innux database name is not configured.");
        }

        // ─── Build connection string ───

        var connectionString = BuildConnectionString(section);

        _logger.LogDebug(
            "InnuxConnectionFactory: opening connection to database {Database}, server {Server}",
            databaseName, server);

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
    /// Does not open a connection.
    /// </summary>
    public string? GetDatabaseName()
    {
        return _configuration["Integrations:Innux:DatabaseName"];
    }

    /// <summary>
    /// Returns the configured server/instance data source for diagnostics/logging.
    /// Does not open a connection.
    /// </summary>
    public string? GetDataSource()
    {
        var section = _configuration.GetSection("Integrations:Innux");
        var server = section["Server"];
        var instanceName = section["InstanceName"];

        if (string.IsNullOrWhiteSpace(server)) return null;

        return string.IsNullOrWhiteSpace(instanceName)
            ? server
            : $"{server}\\{instanceName}";
    }

    /// <summary>
    /// Builds a SQL Server connection string from the Integrations:Innux section.
    /// Strictly adheres to explicit authentication rules.
    /// </summary>
    internal static string BuildConnectionString(IConfigurationSection section)
    {
        var server = section["Server"] ?? string.Empty;
        var instanceName = section["InstanceName"];
        var databaseName = section["DatabaseName"] ?? string.Empty;
        var authMode = section["AuthenticationMode"]?.ToUpperInvariant() ?? "SQL";
        var timeoutSeconds = GetTimeoutSeconds(section);

        var dataSource = string.IsNullOrWhiteSpace(instanceName)
            ? server
            : $"{server}\\{instanceName}";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = databaseName,
            ConnectTimeout = timeoutSeconds,
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
            var username = section["Username"];
            var password = section["Password"];

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "Innux SQL Authentication credentials are not fully configured. Required: Username, Password.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = username;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    internal static int GetTimeoutSeconds(IConfigurationSection section)
    {
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0)
            return timeout;
        return 15;
    }
}
