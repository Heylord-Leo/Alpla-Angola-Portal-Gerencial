using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Concrete IIntegrationProvider implementation: Innux (Time and Attendance).
///
/// Provider-level health / diagnostics only.
/// - Tests SQL connectivity to the configured Innux SQL Server instance
/// - Returns server identity and database name for diagnostic confirmation
/// - Delegates connection-string resolution to InnuxConnectionFactory
/// - Read-only: no writes, no EF context, no business-domain queries
///
/// Connection logic is centralized in InnuxConnectionFactory so that both
/// this provider and domain services (InnuxEmployeeService, etc.) share
/// a single source of truth for connection configuration.
/// </summary>
public class InnuxIntegrationProvider : IIntegrationProvider
{
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InnuxIntegrationProvider> _logger;

    public string Code => "INNUX";
    
    // Explicitly aligned with functional category requirements
    public string ProviderType => "TIME_ATTENDANCE";
    public string ConnectionType => "SQL";

    public InnuxIntegrationProvider(
        InnuxConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<InnuxIntegrationProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IntegrationConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var section = _configuration.GetSection("Integrations:Innux");

        // ─── Validate required configuration ───

        var server = section["Server"];
        var databaseName = section["DatabaseName"];
        var enabledRaw = section["Enabled"];

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(databaseName))
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "Innux connection settings are incomplete. Required: Server, DatabaseName.",
                ResponseTimeMs = 0
            };
        }

        if (!bool.TryParse(enabledRaw, out var isEnabled) || !isEnabled)
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "Innux provider is not enabled in configuration.",
                ResponseTimeMs = 0
            };
        }

        // ─── Execute connection test using shared factory ───

        var sw = Stopwatch.StartNew();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(ct);

            // Diagnostic query: confirms server identity + target database.
            // Read-only, no business tables, no writes.
            await using var command = new SqlCommand(
                "SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName", connection);
            command.CommandTimeout = InnuxConnectionFactory.GetTimeoutSeconds(section);

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
                "Innux connection test succeeded. Server: {ServerName}, Database: {DatabaseName}, ResponseTime: {ElapsedMs}ms",
                serverName, dbName, sw.ElapsedMilliseconds);

            return new IntegrationConnectionTestResult
            {
                Success = true,
                Message = $"Connected successfully. Server: {serverName}, Database: {dbName}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (SqlException ex)
        {
            sw.Stop();

            _logger.LogWarning(ex,
                "Innux connection test failed. Server: {Server}, Database: {Database}, Error: {ErrorMessage}",
                server, databaseName, ex.Message);

            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = $"SQL connection failed: {ex.Message}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (InvalidOperationException ex)
        {
            sw.Stop();

            _logger.LogWarning(ex,
                "Innux connection test failed due to configuration error: {ErrorMessage}",
                ex.Message);

            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = $"Configuration error: {ex.Message}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();

            _logger.LogError(ex,
                "Innux connection test encountered an unexpected error. Server: {Server}, Database: {Database}",
                server, databaseName);

            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = $"Connection error: {ex.Message}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
    }
}
