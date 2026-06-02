using System.Diagnostics;
using System.Text.Json;
using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Concrete implementation of IIntegrationHealthService.
///
/// Provider-agnostic: iterates over whatever IntegrationProvider records exist
/// in the database, resolves matching IIntegrationProvider implementations
/// from DI (if any), and assembles status DTOs.
///
/// For providers that are planned/disabled or have no registered implementation,
/// returns metadata with appropriate status — never attempts connection.
/// </summary>
public class IntegrationHealthService : IIntegrationHealthService
{
    private readonly ApplicationDbContext _db;
    private readonly IEnumerable<IIntegrationProvider> _providers;
    private readonly AdminLogWriter _logWriter;
    private readonly ILogger<IntegrationHealthService> _logger;
    private readonly IConfiguration _configuration;

    public IntegrationHealthService(
        ApplicationDbContext db,
        IEnumerable<IIntegrationProvider> providers,
        AdminLogWriter logWriter,
        ILogger<IntegrationHealthService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _providers = providers;
        _logWriter = logWriter;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<IntegrationHealthSummaryDto> GetHealthSummaryAsync(CancellationToken ct = default)
    {
        var dbProviders = await _db.IntegrationProviders
            .Include(p => p.ConnectionStatus)
            .Include(p => p.Settings)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        var smtpSettings = await _db.SmtpSettings
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        var result = new IntegrationHealthSummaryDto();

        foreach (var dbProvider in dbProviders)
        {
            var implementation = _providers.FirstOrDefault(
                p => p.Code.Equals(dbProvider.Code, StringComparison.OrdinalIgnoreCase));

            var configSection = _configuration.GetSection($"Integrations:{dbProvider.Code}");
            var configHasSettings = !string.IsNullOrEmpty(configSection["Server"]) || !string.IsNullOrEmpty(configSection["ApiBaseUrl"]);

            var capabilities = ParseCapabilities(dbProvider.Capabilities);
            var hasSettings = configHasSettings || (dbProvider.Settings != null &&
                (!string.IsNullOrEmpty(dbProvider.Settings.Server) || !string.IsNullOrEmpty(dbProvider.Settings.ApiBaseUrl)));
            
            // SMTP custom hasSettings check
            if (dbProvider.Code.Equals("SMTP", StringComparison.OrdinalIgnoreCase))
            {
                hasSettings = smtpSettings != null && !string.IsNullOrEmpty(smtpSettings.Server);
            }

            // ALPLAPROD uses nested Plants config, not a flat Server key
            if (dbProvider.Code.Equals("ALPLAPROD", StringComparison.OrdinalIgnoreCase))
            {
                var alplaProdSec = _configuration.GetSection("Integrations:AlplaProd");
                hasSettings = !string.IsNullOrEmpty(alplaProdSec["Plants:VIANA1:Server"])
                           || !string.IsNullOrEmpty(alplaProdSec["Plants:VIANA2:Server"])
                           || !string.IsNullOrEmpty(alplaProdSec["Plants:VIANA3:Server"]);
            }

            var hasImpl = implementation != null;
            var isEnabled = dbProvider.IsEnabled;

            // Determine current status dynamically
            var currentStatus = DetermineDisplayStatus(dbProvider, isEnabled, smtpSettings, _configuration);

            var dto = new IntegrationProviderStatusDto
            {
                Code = dbProvider.Code,
                Name = dbProvider.Name,
                ProviderType = dbProvider.ProviderType,
                ConnectionType = dbProvider.ConnectionType,
                Description = dbProvider.Description,
                Environment = dbProvider.Environment,
                IsEnabled = isEnabled,
                IsPlanned = dbProvider.IsPlanned,
                DisplayOrder = dbProvider.DisplayOrder,
                Capabilities = capabilities,
                CurrentStatus = currentStatus,
                HasConnectionSettings = hasSettings,
                HasImplementation = hasImpl,
                // Can test only if: enabled + not planned + has settings + has implementation
                CanTestConnection = isEnabled && !dbProvider.IsPlanned && hasSettings && hasImpl
            };

            // Populate connection status fields if available
            if (dbProvider.ConnectionStatus != null)
            {
                var cs = dbProvider.ConnectionStatus;
                dto.LastSuccessUtc = cs.LastSuccessUtc;
                dto.LastFailureUtc = cs.LastFailureUtc;
                dto.LastCheckedAtUtc = cs.LastCheckedAtUtc;
                dto.LastResponseTimeMs = cs.LastResponseTimeMs;
                dto.LastErrorMessage = cs.LastErrorMessage;
                dto.ConsecutiveFailures = cs.ConsecutiveFailures;
                dto.LastTestedByEmail = cs.LastTestedByEmail;
            }

            result.Providers.Add(dto);
        }

        return result;
    }

    public async Task<IntegrationConnectionTestResultDto> TestProviderConnectionAsync(
        string providerCode, string? companyKey = null, CancellationToken ct = default)
    {
        var dbProvider = await _db.IntegrationProviders
            .Include(p => p.ConnectionStatus)
            .FirstOrDefaultAsync(p => p.Code == providerCode, ct);

        if (dbProvider == null)
        {
            return new IntegrationConnectionTestResultDto
            {
                ProviderCode = providerCode,
                Success = false,
                Message = $"Provider '{providerCode}' not found."
            };
        }

        if (dbProvider.IsPlanned)
        {
            return new IntegrationConnectionTestResultDto
            {
                ProviderCode = providerCode,
                Success = false,
                Message = "This provider is planned for a future phase. Connection testing is not available yet."
            };
        }

        // Use DB status strictly, bypassing for Primavera to let its custom validation handle the disabled message
        bool isEnabled = providerCode.Equals("PRIMAVERA", StringComparison.OrdinalIgnoreCase) || dbProvider.IsEnabled;

        if (!isEnabled)
        {
            return new IntegrationConnectionTestResultDto
            {
                ProviderCode = providerCode,
                Success = false,
                Message = "This provider is currently disabled."
            };
        }

        var implementation = _providers.FirstOrDefault(
            p => p.Code.Equals(providerCode, StringComparison.OrdinalIgnoreCase));

        if (implementation == null)
        {
            return new IntegrationConnectionTestResultDto
            {
                ProviderCode = providerCode,
                Success = false,
                Message = "No concrete implementation is registered for this provider."
            };
        }

        // Log test start
        await _logWriter.WriteAsync("Information", IntegrationLogEventTypes.LogSource,
            IntegrationLogEventTypes.ConnectionTestStarted,
            $"Connection test started for provider: {providerCode}{(string.IsNullOrEmpty(companyKey) ? "" : $" (Company: {companyKey})")}");

        var sw = Stopwatch.StartNew();
        IntegrationConnectionTestResult testResult;

        try
        {
            if (providerCode.Equals("PRIMAVERA", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(companyKey))
            {
                if (System.Enum.TryParse<AlplaPortal.Application.Interfaces.Integration.PrimaveraCompany>(companyKey, true, out var company))
                {
                    var primaveraProvider = (PrimaveraIntegrationProvider)implementation;
                    testResult = await primaveraProvider.TestCompanyConnectionAsync(company, ct);
                }
                else
                {
                    throw new ArgumentException($"Empresa '{companyKey}' inválida para o Primavera.");
                }
            }
            else
            {
                testResult = await implementation.TestConnectionAsync(ct);
            }
            sw.Stop();
            testResult.ResponseTimeMs ??= (int)sw.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Connection test failed for provider {ProviderCode}", providerCode);
            testResult = new IntegrationConnectionTestResult
            {
                Success = false,
                Message = ex.Message,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
        }

        // Update connection status in DB
        var status = dbProvider.ConnectionStatus;
        if (status == null)
        {
            status = new IntegrationConnectionStatus
            {
                IntegrationProviderId = dbProvider.Id
            };
            _db.IntegrationConnectionStatuses.Add(status);
        }

        status.LastCheckedAtUtc = DateTime.UtcNow;
        status.LastResponseTimeMs = testResult.ResponseTimeMs;

        if (testResult.Success)
        {
            status.CurrentStatus = IntegrationStatusCodes.Healthy;
            status.LastSuccessUtc = DateTime.UtcNow;
            status.ConsecutiveFailures = 0;
            status.LastErrorMessage = null;
        }
        else
        {
            status.CurrentStatus = IntegrationStatusCodes.Unhealthy;
            status.LastFailureUtc = DateTime.UtcNow;
            status.ConsecutiveFailures++;
            status.LastErrorMessage = testResult.Message;
        }

        await _db.SaveChangesAsync(ct);

        // Log result
        var eventType = testResult.Success
            ? IntegrationLogEventTypes.ConnectionTestOk
            : IntegrationLogEventTypes.ConnectionTestFailed;

        await _logWriter.WriteAsync(
            testResult.Success ? "Information" : "Warning",
            IntegrationLogEventTypes.LogSource,
            eventType,
            $"Connection test {(testResult.Success ? "succeeded" : "failed")} for provider: {providerCode}",
            exceptionDetail: testResult.Success ? null : testResult.Message,
            payload: JsonSerializer.Serialize(new
            {
                providerCode,
                success = testResult.Success,
                responseTimeMs = testResult.ResponseTimeMs,
                message = testResult.Message
            }));

        return new IntegrationConnectionTestResultDto
        {
            ProviderCode = providerCode,
            Success = testResult.Success,
            Message = testResult.Message,
            ResponseTimeMs = testResult.ResponseTimeMs
        };
    }

    /// <summary>
    /// Determines the display status for a provider based on its configuration state.
    /// </summary>
    public static string DetermineDisplayStatus(IntegrationProvider provider, bool isEnabled, SmtpSettings? smtpSettings, IConfiguration configuration)
    {
        if (provider.IsPlanned) 
            return IntegrationStatusCodes.Planned;

        if (!isEnabled) 
            return IntegrationStatusCodes.Inactive;

        // Check if configured
        bool isConfigured = false;
        if (provider.Code.Equals("PRIMAVERA", StringComparison.OrdinalIgnoreCase))
        {
            var s = provider.Settings;
            var configSec = configuration.GetSection("Integrations:Primavera");
            var server = s?.Server ?? configSec["Server"];
            var authMode = s?.AuthenticationMode ?? configSec["AuthenticationMode"] ?? "SQL";

            if (!string.IsNullOrEmpty(server))
            {
                bool allCompaniesValid = true;
                bool atLeastOneCompany = false;

                PrimaveraAdditionalConfig? parsed = null;
                if (s != null && !string.IsNullOrWhiteSpace(s.AdditionalConfig))
                {
                    try
                    {
                        parsed = JsonSerializer.Deserialize<PrimaveraAdditionalConfig>(
                            s.AdditionalConfig,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch
                    {
                        // ignore malformed JSON
                    }
                }

                foreach (var company in System.Enum.GetValues<AlplaPortal.Application.Interfaces.Integration.PrimaveraCompany>())
                {
                    var companyKey = company.ToString();
                    bool compEnabled = true;
                    
                    // Fallback to configuration settings
                    var companySection = configuration.GetSection($"Integrations:Primavera:Companies:{companyKey}");
                    var dbName = companySection["DatabaseName"];
                    var username = companySection["Username"];
                    var hasPassword = !string.IsNullOrEmpty(companySection["Password"]);
                    if (companySection["Enabled"] != null)
                    {
                        compEnabled = companySection.GetValue<bool>("Enabled");
                    }

                    // DB override
                    if (parsed?.Companies != null && parsed.Companies.TryGetValue(companyKey, out var compSettings))
                    {
                        dbName = compSettings.DatabaseName ?? dbName;
                        compEnabled = compSettings.Enabled;
                        username = compSettings.Username ?? username;
                        hasPassword = !string.IsNullOrEmpty(compSettings.EncryptedPassword) || hasPassword;
                    }

                    if (compEnabled)
                    {
                        atLeastOneCompany = true;
                        if (string.IsNullOrEmpty(dbName))
                        {
                            allCompaniesValid = false;
                            break;
                        }

                        if (authMode.Equals("SQL", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrEmpty(username) || !hasPassword)
                            {
                                allCompaniesValid = false;
                                break;
                            }
                        }
                    }
                }

                isConfigured = allCompaniesValid && atLeastOneCompany;
            }
        }
        else if (provider.Code.Equals("INNUX", StringComparison.OrdinalIgnoreCase))
        {
            var s = provider.Settings;
            var configSec = configuration.GetSection("Integrations:Innux");
            var server = s?.Server ?? configSec["Server"];
            var db = s?.DatabaseName ?? configSec["DatabaseName"];
            var authMode = s?.AuthenticationMode ?? configSec["AuthenticationMode"] ?? "SQL";
            var username = s?.Username ?? configSec["Username"];
            var hasPassword = (s != null && !string.IsNullOrEmpty(s.EncryptedPassword)) || !string.IsNullOrEmpty(configSec["Password"]);

            isConfigured = !string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(db) &&
                           (authMode.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase) || 
                            (!string.IsNullOrEmpty(username) && hasPassword));
        }
        else if (provider.Code.Equals("OPENAI", StringComparison.OrdinalIgnoreCase))
        {
            var s = provider.Settings;
            var configSec = configuration.GetSection("Integrations:OpenAi");
            var hasApiKey = (s != null && !string.IsNullOrEmpty(s.ApiKeyEncrypted)) || 
                             !string.IsNullOrEmpty(configSec["ApiKey"]) || 
                             !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

            isConfigured = hasApiKey;
        }
        else if (provider.Code.Equals("SMTP", StringComparison.OrdinalIgnoreCase))
        {
            var smtp = smtpSettings;
            var configSec = configuration.GetSection("SmtpSettings");
            var server = smtp?.Server ?? configSec["Server"];
            var portVal = smtp?.Port ?? (configSec["Port"] != null ? int.Parse(configSec["Port"]!) : 0);
            var senderEmail = smtp?.SenderEmail ?? configSec["SenderEmail"];

            isConfigured = !string.IsNullOrEmpty(server) && portVal > 0 && !string.IsNullOrEmpty(senderEmail);
        }
        else if (provider.Code.Equals("ALPLAPROD", StringComparison.OrdinalIgnoreCase))
        {
            var configSec = configuration.GetSection("Integrations:AlplaProd");
            var authMode = configSec["AuthenticationMode"]?.ToUpperInvariant() ?? "SQL";
            var username = configSec["Username"];
            var hasPassword = !string.IsNullOrEmpty(configSec["Password"]);

            // At least one plant must have Server + DatabaseName configured
            bool hasAnyPlant = false;
            foreach (var plantName in new[] { "VIANA1", "VIANA2", "VIANA3" })
            {
                var plantSec = configSec.GetSection($"Plants:{plantName}");
                var plantEnabled = plantSec["Enabled"];
                if (bool.TryParse(plantEnabled, out var pe) && !pe)
                    continue;

                var plantServer = plantSec["Server"];
                var plantDb = plantSec["DatabaseName"];
                if (!string.IsNullOrEmpty(plantServer) && !string.IsNullOrEmpty(plantDb))
                {
                    hasAnyPlant = true;
                    break;
                }
            }

            isConfigured = hasAnyPlant &&
                           (authMode == "WINDOWS" || (!string.IsNullOrEmpty(username) && hasPassword));
        }

        if (!isConfigured) 
            return IntegrationStatusCodes.NotConfigured;

        // If configured, check last test connection status
        var status = provider.ConnectionStatus;
        if (status == null || status.LastCheckedAtUtc == null)
        {
            return IntegrationStatusCodes.PendingTest;
        }

        return status.CurrentStatus;
    }

    private static List<string> ParseCapabilities(string? capabilitiesJson)
    {
        if (string.IsNullOrWhiteSpace(capabilitiesJson))
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(capabilitiesJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private class PrimaveraAdditionalConfig
    {
        public Dictionary<string, PrimaveraCompanyConfig>? Companies { get; set; }
    }

    private class PrimaveraCompanyConfig
    {
        public string? DatabaseName { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Username { get; set; }
        public string? EncryptedPassword { get; set; }
        public int SecretVersion { get; set; }
    }
}
