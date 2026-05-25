using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// OpenAI integration provider — connection health check delegator.
/// </summary>
public class OpenAiIntegrationProvider : IIntegrationProvider
{
    private readonly IDocumentExtractionSettingsService _extractionSettingsService;

    public string Code => "OPENAI";
    public string ProviderType => "API";
    public string ConnectionType => "REST_API";

    public OpenAiIntegrationProvider(IDocumentExtractionSettingsService extractionSettingsService)
    {
        _extractionSettingsService = extractionSettingsService;
    }

    public async Task<IntegrationConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var result = await _extractionSettingsService.TestConnectionAsync(ct);
        return new IntegrationConnectionTestResult
        {
            Success = result.Success,
            Message = result.Message,
            ResponseTimeMs = result.ResponseTimeMs.HasValue ? (int)result.ResponseTimeMs.Value : 0
        };
    }
}
