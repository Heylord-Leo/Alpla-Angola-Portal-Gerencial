using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Shared factory that resolves SQL connections to AlplaPROD production databases.
///
/// Unlike PrimaveraConnectionFactory (single server, multiple databases) and
/// InnuxConnectionFactory (single database), AlplaPROD has:
///   - Two physical servers: AOVIA1VMS006 and AOVIA2VMS006
///   - Three databases: AlplaPROD_aovia1, AlplaPROD_aovia2, AlplaPROD_aovia3
///   - Shared read-only credentials across all plants
///
/// Settings cascade:
///   1. Database-backed IntegrationProviderSettings for code "ALPLAPROD"
///      → Server (global fallback), Username, EncryptedPassword
///   2. IConfiguration fallback: Integrations:AlplaProd section
///   3. Safe disabled state if neither is available
///
/// Per-plant overrides for Server and DatabaseName are read from:
///   Integrations:AlplaProd:Plants:{VIANA1|VIANA2|VIANA3}
///
/// IMPORTANT: This factory provides READ-ONLY connections only.
/// AlplaPROD databases must never be modified by the Portal.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §3
/// </summary>
public class AlplaProdConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly IntegrationConfigResolver _configResolver;
    private readonly ILogger<AlplaProdConnectionFactory> _logger;

    private const string ConfigSection = "Integrations:AlplaProd";
    private const string ProviderCode = "ALPLAPROD";

    public AlplaProdConnectionFactory(
        IConfiguration configuration,
        IntegrationConfigResolver configResolver,
        ILogger<AlplaProdConnectionFactory> logger)
    {
        _configuration = configuration;
        _configResolver = configResolver;
        _logger = logger;
    }

    /// <summary>
    /// Creates and opens a read-only SQL connection to the specified AlplaPROD plant database.
    /// Uses the settings cascade: DB → IConfiguration → disabled.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when AlplaPROD is not enabled, the plant is not configured,
    /// or required credentials are missing.
    /// </exception>
    public async Task<SqlConnection> CreateConnectionAsync(
        AlplaProdPlant plant, CancellationToken ct = default)
    {
        var plantKey = plant.ToString();
        var plantSection = _configuration.GetSection($"{ConfigSection}:Plants:{plantKey}");

        // ─── Validate global provider state ───
        //
        // AlplaProd uses a multi-server pattern: Server is per-plant, not at the
        // global section level. The IntegrationConfigResolver may return Source=NONE
        // because there's no top-level "Server" key. In that case, fall back to the
        // IConfiguration Enabled flag directly (same as IsGloballyEnabled()).

        var resolved = await _configResolver.ResolveSqlSettingsAsync(
            ProviderCode, ConfigSection, ct);

        var isEnabled = resolved.IsEnabled;

        // If the resolver found no config (NONE), check IConfiguration directly
        if (resolved.Source == "NONE")
        {
            isEnabled = IsGloballyEnabled();
        }

        if (!isEnabled)
        {
            throw new InvalidOperationException(
                "A integração AlplaPROD está desativada.");
        }

        // ─── Resolve per-plant server and database ───

        var plantEnabled = plantSection["Enabled"];
        if (bool.TryParse(plantEnabled, out var pe) && !pe)
        {
            throw new InvalidOperationException(
                $"A planta {plantKey} está desativada para esta integração.");
        }

        // Server: plant-level override takes precedence, then global resolved
        var server = plantSection["Server"];
        if (string.IsNullOrWhiteSpace(server))
        {
            server = resolved.Server;
        }

        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException(
                "Servidor do AlplaPROD não configurado.");
        }

        var databaseName = plantSection["DatabaseName"];
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                $"Base de dados da planta {plantKey} não configurada.");
        }

        // ─── Resolve credentials (shared across plants) ───

        var username = resolved.Username;
        var password = resolved.DecryptedPassword;

        // Fallback to IConfiguration if resolver returned no credentials
        if (string.IsNullOrWhiteSpace(username))
        {
            username = _configuration[$"{ConfigSection}:Username"];
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            password = _configuration[$"{ConfigSection}:Password"];
        }

        var authMode = resolved.AuthenticationMode;
        if (string.IsNullOrWhiteSpace(authMode) || authMode == "NONE")
        {
            var configAuthMode = _configuration[$"{ConfigSection}:AuthenticationMode"];
            authMode = !string.IsNullOrWhiteSpace(configAuthMode)
                ? configAuthMode.ToUpperInvariant()
                : "SQL";
        }

        // ─── Build connection string ───

        var connectionString = BuildConnectionString(
            server, databaseName, authMode, username, password, resolved.TimeoutSeconds);

        _logger.LogDebug(
            "AlplaProdConnectionFactory: opening connection to {Plant} ({Database}) on {Server}, source {Source}",
            plantKey, databaseName, server, resolved.Source);

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
    /// Checks whether a specific plant has a valid configuration (server + database name).
    /// Does not open a connection.
    /// </summary>
    public bool IsPlantConfigured(AlplaProdPlant plant)
    {
        var plantKey = plant.ToString();
        var plantSection = _configuration.GetSection($"{ConfigSection}:Plants:{plantKey}");

        var server = plantSection["Server"];
        var databaseName = plantSection["DatabaseName"];

        // Check if plant is explicitly disabled
        var plantEnabled = plantSection["Enabled"];
        if (bool.TryParse(plantEnabled, out var pe) && !pe)
            return false;

        return !string.IsNullOrWhiteSpace(server)
            && !string.IsNullOrWhiteSpace(databaseName);
    }

    /// <summary>
    /// Returns all plants that have valid configuration.
    /// Does not open connections.
    /// </summary>
    public IReadOnlyList<AlplaProdPlant> GetConfiguredPlants()
    {
        var result = new List<AlplaProdPlant>();

        foreach (var plant in Enum.GetValues<AlplaProdPlant>())
        {
            if (IsPlantConfigured(plant))
                result.Add(plant);
        }

        return result;
    }

    /// <summary>
    /// Returns the configured pipeline model for a plant from configuration.
    /// Defaults to STANDARD if not specified.
    /// </summary>
    public AlplaProdPipelineModel GetPlantPipelineModel(AlplaProdPlant plant)
    {
        var plantKey = plant.ToString();
        var pipelineModelRaw = _configuration[$"{ConfigSection}:Plants:{plantKey}:PipelineModel"];

        if (Enum.TryParse<AlplaProdPipelineModel>(pipelineModelRaw, ignoreCase: true, out var model))
            return model;

        return AlplaProdPipelineModel.STANDARD;
    }

    /// <summary>
    /// Returns the configured server for a plant for diagnostics/logging.
    /// Does not open a connection.
    /// </summary>
    public string? GetPlantServer(AlplaProdPlant plant)
    {
        var plantKey = plant.ToString();
        return _configuration[$"{ConfigSection}:Plants:{plantKey}:Server"];
    }

    /// <summary>
    /// Returns the configured database name for a plant for diagnostics/logging.
    /// Does not open a connection.
    /// </summary>
    public string? GetPlantDatabaseName(AlplaProdPlant plant)
    {
        var plantKey = plant.ToString();
        return _configuration[$"{ConfigSection}:Plants:{plantKey}:DatabaseName"];
    }

    /// <summary>
    /// Returns whether the global AlplaPROD integration is enabled in configuration.
    /// Does not open a connection.
    /// </summary>
    public bool IsGloballyEnabled()
    {
        var enabledRaw = _configuration[$"{ConfigSection}:Enabled"];
        return bool.TryParse(enabledRaw, out var e) && e;
    }

    // ── Private helpers ──

    private static string BuildConnectionString(
        string server,
        string databaseName,
        string authMode,
        string? username,
        string? password,
        int timeoutSeconds)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = databaseName,
            ConnectTimeout = timeoutSeconds > 0 ? timeoutSeconds : 30,
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            ApplicationName = "AlplaPortal_Operations"
        };

        if (authMode == "WINDOWS")
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException(
                    "Utilizador do AlplaPROD não configurado.");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "Senha do AlplaPROD não configurada.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = username;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }
}
