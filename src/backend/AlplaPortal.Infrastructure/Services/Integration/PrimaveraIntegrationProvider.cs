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

        return await TestCompanyConnectionAsync(defaultCompany.Value, ct);
    }

    public async Task<IntegrationConnectionTestResult> TestCompanyConnectionAsync(PrimaveraCompany company, CancellationToken ct = default)
    {
        var targetCompany = company;
        var targetDatabase = _connectionFactory.GetDatabaseName(targetCompany) ?? "unknown";

        // ─── Sequential validations in Portuguese ───
        var (isProviderEnabled, isConfigured, server, authMode, companySettings) = 
            await _connectionFactory.GetCompanySettingsAsync(targetCompany, ct);

        // 1. If provider PRIMAVERA IsEnabled=false:
        if (!isProviderEnabled)
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "A integração Primavera está desativada. Ative a integração antes de testar a conexão.",
                ResponseTimeMs = 0
            };
        }

        // 2. If company Enabled=false:
        if (!companySettings.Enabled)
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "A empresa selecionada está desativada para esta integração.",
                ResponseTimeMs = 0
            };
        }

        // 3. If Server is missing:
        if (string.IsNullOrWhiteSpace(server))
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "Servidor do Primavera não configurado.",
                ResponseTimeMs = 0
            };
        }

        // 4. If company DatabaseName is missing:
        if (string.IsNullOrWhiteSpace(companySettings.DatabaseName))
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "Base de dados da empresa não configurada.",
                ResponseTimeMs = 0
            };
        }

        // 5. If SQL authentication and company Username is missing:
        bool isSqlAuth = authMode?.Equals("SQL", StringComparison.OrdinalIgnoreCase) ?? true;
        if (isSqlAuth && string.IsNullOrWhiteSpace(companySettings.Username))
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "Utilizador da empresa não configurado.",
                ResponseTimeMs = 0
            };
        }

        // 6. If SQL authentication and company password is missing:
        if (isSqlAuth && !companySettings.HasPassword)
        {
            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = "Senha da empresa não configurada. Utilize 'Substituir Senha' antes de testar a conexão.",
                ResponseTimeMs = 0
            };
        }

        // ─── Execute connection test against company database ───

        var sw = Stopwatch.StartNew();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(targetCompany, ct);

            // Diagnostic query: confirms server identity + target database.
            // Read-only, no business tables, no writes.
            await using var command = new SqlCommand(
                "SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName", connection);
            command.CommandTimeout = 15;

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
