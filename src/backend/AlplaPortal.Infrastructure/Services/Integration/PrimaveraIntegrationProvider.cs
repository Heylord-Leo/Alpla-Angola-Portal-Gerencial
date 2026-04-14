using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Primavera integration provider — connection health / diagnostics only.
///
/// Phase 2D: uses PrimaveraConnectionFactory for multi-database support.
/// Health check uses Option A: tests the first configured company (default target).
///
/// The health test documents which company/database it is testing against
/// so there is no ambiguity in the reported status.
///
/// Read-only: no writes, no EF context, no business-domain queries.
/// </summary>
public class PrimaveraIntegrationProvider : IIntegrationProvider
{
    private readonly PrimaveraConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PrimaveraIntegrationProvider> _logger;

    public string Code => "PRIMAVERA";
    public string ProviderType => "ERP";
    public string ConnectionType => "SQL";

    public PrimaveraIntegrationProvider(
        PrimaveraConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<PrimaveraIntegrationProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IntegrationConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var section = _configuration.GetSection("Integrations:Primavera");

        // ─── Validate provider is enabled ───

        var enabledRaw = section["Enabled"];
        if (!bool.TryParse(enabledRaw, out var isEnabled) || !isEnabled)
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "Primavera provider is not enabled in configuration.",
                ResponseTimeMs = 0
            };
        }

        // ─── Determine health check target (Option A: first configured company) ───

        var defaultCompany = _connectionFactory.GetDefaultCompany();
        if (defaultCompany == null)
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "No Primavera companies are configured. Add at least one company under Integrations:Primavera:Companies.",
                ResponseTimeMs = 0
            };
        }

        var targetCompany = defaultCompany.Value;
        var targetDatabase = _connectionFactory.GetDatabaseName(targetCompany) ?? "unknown";

        // ─── Diagnostic logging ───

        var server = section["Server"];
        var instanceName = section["InstanceName"];
        var authMode = section["AuthenticationMode"]?.ToUpperInvariant() ?? "SQL";
        var timeoutSeconds = section["TimeoutSeconds"];
        var hasUsername = !string.IsNullOrWhiteSpace(section["Username"]);
        var hasPassword = !string.IsNullOrWhiteSpace(section["Password"]);
        var dataSource = string.IsNullOrWhiteSpace(instanceName) ? server : $"{server}\\{instanceName}";
        var configuredCompanies = _connectionFactory.GetConfiguredCompanies();

        _logger.LogWarning(
            "DIAGNOSTIC - PRIMAVERA CONFIG: AuthMode=[{AuthMode}], Server=[{Server}], Instance=[{Instance}], " +
            "HasUser=[{HasUser}], HasPass=[{HasPass}], FinalDataSource=[{DataSource}], " +
            "HealthTarget=[{HealthTarget}], HealthDB=[{HealthDB}], ConfiguredCompanies=[{Companies}]",
            authMode, server, instanceName, hasUsername, hasPassword, dataSource,
            targetCompany, targetDatabase, string.Join(", ", configuredCompanies));

        // ─── Execute connection test against default company ───

        var sw = Stopwatch.StartNew();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(targetCompany, ct);

            // Diagnostic query: confirms server identity + target database.
            // Read-only, no business tables, no writes.
            await using var command = new SqlCommand(
                "SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName", connection);
            command.CommandTimeout = int.TryParse(timeoutSeconds, out var t) ? t : 15;

            await using var reader = await command.ExecuteReaderAsync(ct);

            string? serverName = null;
            string? dbName = null;

            if (await reader.ReadAsync(ct))
            {
                serverName = reader["ServerName"]?.ToString();
                dbName = reader["DatabaseName"]?.ToString();
            }

            sw.Stop();

            _logger.LogInformation(
                "Primavera connection test succeeded. Company: {Company}, Server: {ServerName}, Database: {DatabaseName}, ResponseTime: {ElapsedMs}ms",
                targetCompany, serverName, dbName, sw.ElapsedMilliseconds);

            return new IntegrationConnectionTestResult
            {
                Success = true,
                Message = $"Connected successfully. Company: {targetCompany}, Server: {serverName}, Database: {dbName}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (InvalidOperationException ex)
        {
            sw.Stop();

            _logger.LogWarning(
                "Primavera connection test failed (configuration). Company: {Company}, Error: {ErrorMessage}",
                targetCompany, ex.Message);

            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = $"Configuration error: {ex.Message}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (SqlException ex)
        {
            sw.Stop();

            _logger.LogWarning(ex,
                "Primavera connection test failed. Company: {Company}, Database: {Database}, Error: {ErrorMessage}",
                targetCompany, targetDatabase, ex.Message);

            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = $"SQL connection failed ({targetCompany}): {ex.Message}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();

            _logger.LogError(ex,
                "Primavera connection test encountered an unexpected error. Company: {Company}, Database: {Database}",
                targetCompany, targetDatabase);

            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = $"Connection error ({targetCompany}): {ex.Message}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
    }
}
