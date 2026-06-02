using System.Diagnostics;
using System.Text;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// IIntegrationProvider implementation for AlplaPROD 1.0 production databases.
///
/// Provides health check connectivity testing for the admin health dashboard.
/// Tests ALL enabled plant connections using a lightweight read-only query,
/// then aggregates results into a single clear diagnostic message.
///
/// This provider:
///   - Is strictly read-only (SELECT @@SERVERNAME, DB_NAME(), SYSTEM_USER, GETDATE() only)
///   - Tests every enabled plant, not just the first
///   - Reports Portuguese validation messages (matching Primavera/Innux pattern)
///   - Does NOT expose connection strings, passwords, or business data
///
/// Business-domain operations (timeline, transfers, etc.) are defined in
/// separate service interfaces in Phase 2.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §4.6
/// </summary>
public class AlplaProdIntegrationProvider : IIntegrationProvider
{
    private readonly AlplaProdConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AlplaProdIntegrationProvider> _logger;

    private const string ConfigSection = "Integrations:AlplaProd";

    /// <summary>Read-only diagnostic query — no data mutation, no schema access.</summary>
    private const string DiagnosticQuery = @"
        SELECT
            @@SERVERNAME    AS ServerName,
            DB_NAME()       AS DatabaseName,
            SYSTEM_USER     AS SqlUser,
            GETDATE()       AS TestDate;";

    public string Code => "ALPLAPROD";
    public string ProviderType => "PRODUCTION";
    public string ConnectionType => "SQL";

    public AlplaProdIntegrationProvider(
        AlplaProdConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<AlplaProdIntegrationProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Tests connectivity to ALL enabled AlplaPROD plant databases.
    ///
    /// Validation sequence (Portuguese messages, matching Primavera pattern):
    ///   1. Provider enabled check
    ///   2. At least one plant configured
    ///   3. Credential pre-flight check (SQL Auth only)
    ///   4. Per-plant connection test with diagnostic query
    ///   5. Aggregated result: plant, configured server/database, SQL-returned values, success/failure
    /// </summary>
    public async Task<IntegrationConnectionTestResult> TestConnectionAsync(
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // ─── Step 1: Provider enabled ───

            if (!_connectionFactory.IsGloballyEnabled())
            {
                return new IntegrationConnectionTestResult
                {
                    Success = false,
                    Message = "A integração AlplaPROD está desativada.",
                    ResponseTimeMs = (int)sw.ElapsedMilliseconds
                };
            }

            // ─── Step 2: At least one plant configured ───

            var configuredPlants = _connectionFactory.GetConfiguredPlants();
            if (configuredPlants.Count == 0)
            {
                return new IntegrationConnectionTestResult
                {
                    Success = false,
                    Message = "Nenhuma planta AlplaPROD configurada (Server + DatabaseName).",
                    ResponseTimeMs = (int)sw.ElapsedMilliseconds
                };
            }

            // ─── Step 3: Credential pre-flight (SQL Auth only) ───

            var authMode = _configuration[$"{ConfigSection}:AuthenticationMode"]
                ?.ToUpperInvariant() ?? "SQL";

            if (authMode != "WINDOWS")
            {
                var username = _configuration[$"{ConfigSection}:Username"];
                if (string.IsNullOrWhiteSpace(username))
                {
                    return new IntegrationConnectionTestResult
                    {
                        Success = false,
                        Message = "Utilizador do AlplaPROD não configurado.",
                        ResponseTimeMs = (int)sw.ElapsedMilliseconds
                    };
                }

                var password = _configuration[$"{ConfigSection}:Password"];
                if (string.IsNullOrWhiteSpace(password))
                {
                    return new IntegrationConnectionTestResult
                    {
                        Success = false,
                        Message = "Senha do AlplaPROD não configurada.",
                        ResponseTimeMs = (int)sw.ElapsedMilliseconds
                    };
                }
            }

            // ─── Step 4: Test ALL enabled plants ───

            var plantResults = new List<PlantTestResult>();
            var allSuccess = true;

            foreach (var plant in configuredPlants)
            {
                var plantKey = plant.ToString();
                var configuredServer = _connectionFactory.GetPlantServer(plant) ?? "(não configurado)";
                var configuredDb = _connectionFactory.GetPlantDatabaseName(plant) ?? "(não configurado)";

                try
                {
                    await using var connection = await _connectionFactory.CreateConnectionAsync(plant, ct);
                    await using var command = connection.CreateCommand();

                    command.CommandText = DiagnosticQuery;
                    command.CommandTimeout = 15;

                    await using var reader = await command.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        plantResults.Add(new PlantTestResult
                        {
                            Plant = plantKey,
                            ConfiguredServer = configuredServer,
                            ConfiguredDatabase = configuredDb,
                            SqlServerName = reader.GetString(0),
                            SqlDatabaseName = reader.GetString(1),
                            SqlUser = reader.GetString(2),
                            TestDate = reader.GetDateTime(3),
                            Success = true,
                            ErrorMessage = null
                        });

                        _logger.LogInformation(
                            "AlplaPROD connection test OK: {Plant} → {ServerName}/{DbName} (user: {SqlUser})",
                            plantKey, reader.GetString(0), reader.GetString(1), reader.GetString(2));
                    }
                    else
                    {
                        allSuccess = false;
                        plantResults.Add(new PlantTestResult
                        {
                            Plant = plantKey,
                            ConfiguredServer = configuredServer,
                            ConfiguredDatabase = configuredDb,
                            Success = false,
                            ErrorMessage = "Consulta de diagnóstico não retornou resultado."
                        });
                    }
                }
                catch (Exception ex)
                {
                    allSuccess = false;
                    plantResults.Add(new PlantTestResult
                    {
                        Plant = plantKey,
                        ConfiguredServer = configuredServer,
                        ConfiguredDatabase = configuredDb,
                        Success = false,
                        ErrorMessage = ex.Message
                    });

                    _logger.LogWarning(ex,
                        "AlplaPROD connection test failed for {Plant}: {Message}",
                        plantKey, ex.Message);
                }
            }

            sw.Stop();

            // ─── Step 5: Aggregate result ───

            var message = BuildAggregatedMessage(plantResults, configuredPlants.Count);

            return new IntegrationConnectionTestResult
            {
                Success = allSuccess,
                Message = message,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();

            _logger.LogWarning(ex,
                "AlplaPROD connection test failed unexpectedly: {Message}", ex.Message);

            return new IntegrationConnectionTestResult
            {
                Success = false,
                Message = $"Falha inesperada: {ex.Message}",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    // ── Private helpers ──

    /// <summary>
    /// Builds a clear aggregated diagnostic message from all per-plant results.
    /// Does NOT expose connection strings or passwords.
    /// </summary>
    private static string BuildAggregatedMessage(List<PlantTestResult> results, int totalConfigured)
    {
        var successCount = results.Count(r => r.Success);
        var failCount = results.Count - successCount;
        var sb = new StringBuilder();

        if (successCount == totalConfigured && failCount == 0)
        {
            sb.Append($"Conexão OK — {successCount}/{totalConfigured} planta(s) ativa(s). ");
        }
        else if (successCount > 0)
        {
            sb.Append($"Parcial — {successCount}/{totalConfigured} planta(s) OK, {failCount} com falha. ");
        }
        else
        {
            sb.Append($"Falha — 0/{totalConfigured} planta(s) conectada(s). ");
        }

        foreach (var r in results)
        {
            if (r.Success)
            {
                sb.Append($"| {r.Plant}: {r.SqlServerName}/{r.SqlDatabaseName} (user: {r.SqlUser}, data: {r.TestDate:yyyy-MM-dd HH:mm:ss}) ✓ ");
            }
            else
            {
                sb.Append($"| {r.Plant}: {r.ConfiguredServer}/{r.ConfiguredDatabase} — FALHA: {r.ErrorMessage} ");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Internal DTO for per-plant test results.</summary>
    private class PlantTestResult
    {
        public string Plant { get; set; } = string.Empty;
        public string ConfiguredServer { get; set; } = string.Empty;
        public string ConfiguredDatabase { get; set; } = string.Empty;
        public string? SqlServerName { get; set; }
        public string? SqlDatabaseName { get; set; }
        public string? SqlUser { get; set; }
        public DateTime? TestDate { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
