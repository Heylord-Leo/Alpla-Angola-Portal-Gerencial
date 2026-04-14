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

        var result = new IntegrationHealthSummaryDto();

        foreach (var dbProvider in dbProviders)
        {
            var implementation = _providers.FirstOrDefault(
                p => p.Code.Equals(dbProvider.Code, StringComparison.OrdinalIgnoreCase));

            var configSection = _configuration.GetSection($"Integrations:{dbProvider.Code}");
            var configHasSettings = !string.IsNullOrEmpty(configSection["Server"]) || !string.IsNullOrEmpty(configSection["ApiBaseUrl"]);
            var configEnabled = configSection.GetValue<bool>("Enabled");

            var capabilities = ParseCapabilities(dbProvider.Capabilities);
            var hasSettings = configHasSettings || (dbProvider.Settings != null &&
                (!string.IsNullOrEmpty(dbProvider.Settings.Server) || !string.IsNullOrEmpty(dbProvider.Settings.ApiBaseUrl)));
            var hasImpl = implementation != null;
            var isEnabled = dbProvider.IsEnabled || configEnabled;

            // Determine current status
            var currentStatus = DetermineDisplayStatus(dbProvider, hasSettings, hasImpl, isEnabled);

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
        string providerCode, CancellationToken ct = default)
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

        var configSection = _configuration.GetSection($"Integrations:{dbProvider.Code}");
        var configEnabled = configSection.GetValue<bool>("Enabled");
        var isEnabled = dbProvider.IsEnabled || configEnabled;

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
            $"Connection test started for provider: {providerCode}");

        var sw = Stopwatch.StartNew();
        IntegrationConnectionTestResult testResult;

        try
        {
            testResult = await implementation.TestConnectionAsync(ct);
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
    private static string DetermineDisplayStatus(IntegrationProvider provider, bool hasSettings, bool hasImplementation, bool isEnabled)
    {
        if (provider.IsPlanned) return IntegrationStatusCodes.Planned;
        if (!isEnabled) return IntegrationStatusCodes.NotConfigured;
        if (!hasSettings) return IntegrationStatusCodes.NotConfigured;

        // If we have a persisted connection status, use it
        if (provider.ConnectionStatus != null)
            return provider.ConnectionStatus.CurrentStatus;

        // Has settings but never tested
        return IntegrationStatusCodes.NotConfigured;
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
}
