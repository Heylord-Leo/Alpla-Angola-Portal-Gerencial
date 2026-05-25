using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Integration;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// SMTP integration provider — connection health check delegator.
/// </summary>
public class SmtpIntegrationProvider : IIntegrationProvider
{
    private readonly ISmtpSettingsService _smtpSettingsService;

    public string Code => "SMTP";
    public string ProviderType => "API";
    public string ConnectionType => "SMTP";

    public SmtpIntegrationProvider(ISmtpSettingsService smtpSettingsService)
    {
        _smtpSettingsService = smtpSettingsService;
    }

    public async Task<IntegrationConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var result = await _smtpSettingsService.TestConnectionAsync(ct);
        return new IntegrationConnectionTestResult
        {
            Success = result.Success,
            Message = result.Message,
            ResponseTimeMs = result.ResponseTimeMs.HasValue ? (int)result.ResponseTimeMs.Value : 0
        };
    }
}
