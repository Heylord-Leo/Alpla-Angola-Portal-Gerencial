using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Shared, domain-neutral factory that resolves SQL connections for any configured
/// Primavera business company/database target.
///
/// Reads shared connection settings from Integrations:Primavera (Server, Instance, Auth, Timeout)
/// and per-company DatabaseName from Integrations:Primavera:Companies:{companyKey}:DatabaseName.
///
/// This factory is intentionally domain-neutral — it provides connections only.
/// Employee queries, material queries, supplier queries, etc. belong in their
/// respective domain services that consume this factory.
///
/// Reusable by any future Primavera domain service (employees, materials, suppliers,
/// departments, cost centers, etc.).
/// </summary>
public class PrimaveraConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PrimaveraConnectionFactory> _logger;

    public PrimaveraConnectionFactory(
        IConfiguration configuration,
        ILogger<PrimaveraConnectionFactory> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Creates and opens a SQL connection to the specified Primavera company database.
    /// Validates configuration completeness before attempting connection.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Primavera is not enabled, credentials are missing,
    /// or the requested company is not configured.
    /// </exception>
    public async Task<SqlConnection> CreateConnectionAsync(
        PrimaveraCompany company, CancellationToken ct = default)
    {
        var section = _configuration.GetSection("Integrations:Primavera");

        // ─── Validate provider-level configuration ───

        if (!section.GetValue<bool>("Enabled"))
        {
            throw new InvalidOperationException("Primavera integration is not enabled.");
        }

        var server = section.GetValue<string>("Server");
        var instanceName = section.GetValue<string>("InstanceName");
        var authMode = section["AuthenticationMode"]?.ToUpperInvariant() ?? "SQL";

        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException("Primavera server is not configured.");
        }

        // ─── Validate company-level configuration ───

        var companyKey = company.ToString();
        var companySection = section.GetSection($"Companies:{companyKey}");
        var databaseName = companySection.GetValue<string>("DatabaseName");
        var companyEnabled = companySection.GetValue<bool?>("Enabled") ?? true;

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                $"Primavera company '{companyKey}' is not configured. Missing DatabaseName in Integrations:Primavera:Companies:{companyKey}.");
        }

        if (!companyEnabled)
        {
            throw new InvalidOperationException(
                $"Primavera company '{companyKey}' is disabled in configuration.");
        }

        // ─── Build connection string ───

        var connectionString = BuildConnectionString(section, databaseName);

        _logger.LogDebug(
            "PrimaveraConnectionFactory: opening connection for company {Company}, database {Database}, server {Server}",
            companyKey, databaseName, server);

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
    /// </summary>
    public IReadOnlyList<PrimaveraCompany> GetConfiguredCompanies()
    {
        var section = _configuration.GetSection("Integrations:Primavera:Companies");
        var result = new List<PrimaveraCompany>();

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
    /// For diagnostics/logging — does not open a connection.
    /// </summary>
    public string? GetDatabaseName(PrimaveraCompany company)
    {
        return _configuration[$"Integrations:Primavera:Companies:{company}:DatabaseName"];
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

    /// <summary>
    /// Builds a SQL Server connection string using shared Primavera settings
    /// and the specified database name.
    /// </summary>
    private static string BuildConnectionString(IConfigurationSection section, string databaseName)
    {
        var server = section.GetValue<string>("Server") ?? string.Empty;
        var instanceName = section.GetValue<string>("InstanceName");
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
            ApplicationName = "AlplaPortal_Integration_Primavera"
        };

        if (authMode == "WINDOWS")
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            var username = section.GetValue<string>("Username");
            var password = section.GetValue<string>("Password");

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

    private static int GetTimeoutSeconds(IConfigurationSection section)
    {
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0)
            return timeout;
        return 15;
    }
}
