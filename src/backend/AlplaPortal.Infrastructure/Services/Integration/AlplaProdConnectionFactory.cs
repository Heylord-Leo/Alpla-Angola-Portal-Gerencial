using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AlplaProdConnectionFactory> _logger;

    private const string ConfigSection = "Integrations:AlplaProd";
    private const string ProviderCode = "ALPLAPROD";

    public AlplaProdConnectionFactory(
        IConfiguration configuration,
        IntegrationConfigResolver configResolver,
        ApplicationDbContext db,
        ILogger<AlplaProdConnectionFactory> logger)
    {
        _configuration = configuration;
        _configResolver = configResolver;
        _db = db;
        _logger = logger;
    }

    private class AlplaProdAdditionalConfig
    {
        public Dictionary<string, AlplaProdPlantConfig>? Plants { get; set; }
    }

    private class AlplaProdPlantConfig
    {
        public string? Server { get; set; }
        public string? DatabaseName { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Username { get; set; }
        public string? EncryptedPassword { get; set; }
        public string? PipelineModel { get; set; }
    }

    private async Task<AlplaProdAdditionalConfig> GetParsedConfigAsync(CancellationToken ct)
    {
        var resolved = await _configResolver.ResolveSqlSettingsAsync(ProviderCode, ConfigSection, ct);
        if (string.IsNullOrWhiteSpace(resolved.AdditionalConfig))
        {
            return new AlplaProdAdditionalConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<AlplaProdAdditionalConfig>(
                resolved.AdditionalConfig,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
                ?? new AlplaProdAdditionalConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse AlplaPROD AdditionalConfig JSON.");
            return new AlplaProdAdditionalConfig();
        }
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
        var parsedConfig = await GetParsedConfigAsync(ct);
        var dbPlantConfig = parsedConfig.Plants != null && parsedConfig.Plants.TryGetValue(plantKey, out var p) ? p : null;

        var isEnabled = await IsGloballyEnabledAsync(ct);

        if (!isEnabled)
        {
            throw new InvalidOperationException(
                "A integração AlplaPROD está desativada.");
        }

        var plantEnabled = dbPlantConfig?.Enabled ?? (bool.TryParse(plantSection["Enabled"], out var pe) ? pe : true);
        if (!plantEnabled)
        {
            throw new InvalidOperationException(
                $"A planta {plantKey} está desativada para esta integração.");
        }

        var server = dbPlantConfig?.Server;
        if (string.IsNullOrWhiteSpace(server))
            server = plantSection["Server"];

        var resolved = await _configResolver.ResolveSqlSettingsAsync(ProviderCode, ConfigSection, ct);
        if (string.IsNullOrWhiteSpace(server))
            server = resolved.Server;

        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException(
                "Servidor do AlplaPROD não configurado.");
        }

        var databaseName = dbPlantConfig?.DatabaseName;
        if (string.IsNullOrWhiteSpace(databaseName))
            databaseName = plantSection["DatabaseName"];

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                $"Base de dados da planta {plantKey} não configurada.");
        }

        // Credentials: try plant-specific override first, then global resolved, then IConfiguration
        var username = dbPlantConfig?.Username;
        if (string.IsNullOrWhiteSpace(username))
            username = resolved.Username;
        if (string.IsNullOrWhiteSpace(username))
            username = _configuration[$"{ConfigSection}:Username"];

        var password = dbPlantConfig?.EncryptedPassword;
        if (!string.IsNullOrWhiteSpace(password))
        {
            // The password at plant-level is already encrypted in the DB, decrypt it
            try
            {
                var encryptionKey = _configuration["AppConfig:EncryptionKey"] ?? string.Empty;
                password = AlplaPortal.Infrastructure.Security.AesEncryptionHelper.Decrypt(password, encryptionKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt plant {PlantKey} password override.", plantKey);
                password = null;
            }
        }
        if (string.IsNullOrWhiteSpace(password))
            password = resolved.DecryptedPassword;
        if (string.IsNullOrWhiteSpace(password))
            password = _configuration[$"{ConfigSection}:Password"];

        var authMode = resolved.AuthenticationMode;
        if (string.IsNullOrWhiteSpace(authMode) || authMode == "NONE")
        {
            var configAuthMode = _configuration[$"{ConfigSection}:AuthenticationMode"];
            authMode = !string.IsNullOrWhiteSpace(configAuthMode)
                ? configAuthMode.ToUpperInvariant()
                : "SQL";
        }

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
    public async Task<bool> IsPlantConfiguredAsync(AlplaProdPlant plant, CancellationToken ct = default)
    {
        var plantKey = plant.ToString();
        var parsedConfig = await GetParsedConfigAsync(ct);
        var dbPlantConfig = parsedConfig.Plants != null && parsedConfig.Plants.TryGetValue(plantKey, out var p) ? p : null;

        var plantSection = _configuration.GetSection($"{ConfigSection}:Plants:{plantKey}");
        var server = dbPlantConfig?.Server ?? plantSection["Server"];
        var databaseName = dbPlantConfig?.DatabaseName ?? plantSection["DatabaseName"];
        var enabled = dbPlantConfig?.Enabled ?? (bool.TryParse(plantSection["Enabled"], out var pe) ? pe : true);

        if (!enabled)
            return false;

        return !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(databaseName);
    }

    public async Task<IReadOnlyList<AlplaProdPlant>> GetConfiguredPlantsAsync(CancellationToken ct = default)
    {
        var result = new List<AlplaProdPlant>();
        foreach (var plant in Enum.GetValues<AlplaProdPlant>())
        {
            if (await IsPlantConfiguredAsync(plant, ct))
                result.Add(plant);
        }
        return result;
    }

    public async Task<AlplaProdPipelineModel> GetPlantPipelineModelAsync(AlplaProdPlant plant, CancellationToken ct = default)
    {
        var plantKey = plant.ToString();
        var parsedConfig = await GetParsedConfigAsync(ct);
        var dbPlantConfig = parsedConfig.Plants != null && parsedConfig.Plants.TryGetValue(plantKey, out var p) ? p : null;

        var pipelineModelRaw = dbPlantConfig?.PipelineModel ?? _configuration[$"{ConfigSection}:Plants:{plantKey}:PipelineModel"];

        if (Enum.TryParse<AlplaProdPipelineModel>(pipelineModelRaw, ignoreCase: true, out var model))
            return model;

        return AlplaProdPipelineModel.STANDARD;
    }

    public async Task<string?> GetPlantServerAsync(AlplaProdPlant plant, CancellationToken ct = default)
    {
        var plantKey = plant.ToString();
        var parsedConfig = await GetParsedConfigAsync(ct);
        var dbPlantConfig = parsedConfig.Plants != null && parsedConfig.Plants.TryGetValue(plantKey, out var p) ? p : null;
        
        return dbPlantConfig?.Server ?? _configuration[$"{ConfigSection}:Plants:{plantKey}:Server"];
    }

    public async Task<string?> GetPlantDatabaseNameAsync(AlplaProdPlant plant, CancellationToken ct = default)
    {
        var plantKey = plant.ToString();
        var parsedConfig = await GetParsedConfigAsync(ct);
        var dbPlantConfig = parsedConfig.Plants != null && parsedConfig.Plants.TryGetValue(plantKey, out var p) ? p : null;
        
        return dbPlantConfig?.DatabaseName ?? _configuration[$"{ConfigSection}:Plants:{plantKey}:DatabaseName"];
    }

    /// <summary>
    /// Returns whether the global AlplaPROD integration is enabled in configuration.
    /// Does not open a connection.
    /// </summary>
    public async Task<bool> IsGloballyEnabledAsync(CancellationToken ct = default)
    {
        var provider = await _db.IntegrationProviders.FirstOrDefaultAsync(p => p.Code == ProviderCode, ct);
        if (provider != null)
            return provider.IsEnabled;

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
