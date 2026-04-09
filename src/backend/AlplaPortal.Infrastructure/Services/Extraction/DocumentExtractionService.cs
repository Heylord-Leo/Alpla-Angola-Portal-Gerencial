using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Extraction;

public class DocumentExtractionService : IDocumentExtractionService
{
    private readonly IEnumerable<IDocumentExtractionProvider> _providers;
    private readonly ILogger<DocumentExtractionService> _logger;
    private readonly IDocumentExtractionSettingsService _settingsService;

    public DocumentExtractionService(
        IEnumerable<IDocumentExtractionProvider> providers,
        IDocumentExtractionSettingsService settingsService,
        ILogger<DocumentExtractionService> logger)
    {
        _providers = providers;
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task<ExtractionResultDto> ExtractAsync(Stream fileStream, string fileName, string? sourceContext = null, CancellationToken ct = default)
    {
        var options = await _settingsService.GetEffectiveSettingsAsync(ct);

        if (!options.IsEnabled)
        {
            _logger.LogWarning("Document extraction is globally disabled in settings.");
            return new ExtractionResultDto { Success = false };
        }

        var providerName = options.DefaultProvider;
        var provider = GetEnabledProvider(providerName, options);

        if (provider == null)
        {
            _logger.LogWarning("Configured provider '{ProviderName}' is missing, disabled, or invalid. Falling back to LOCAL_OCR.", providerName);
            provider = GetEnabledProvider("LOCAL_OCR", options);
        }

        if (provider == null)
        {
            _logger.LogCritical("No enabled document extraction providers available, including fallback LOCAL_OCR.");
            return new ExtractionResultDto { Success = false };
        }

        var timeoutSeconds = GetTimeoutForProvider(provider.Name, options);
        _logger.LogInformation("Using extraction provider: {ProviderName} with timeout {Timeout}s", provider.Name, timeoutSeconds);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try 
        {
            return await provider.ExtractAsync(fileStream, fileName, sourceContext, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogError("Extraction timed out after {Timeout}s using provider {ProviderName}", timeoutSeconds, provider.Name);
            return new ExtractionResultDto { Success = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during extraction using provider {ProviderName}", provider.Name);
            return new ExtractionResultDto { Success = false };
        }
    }

    private IDocumentExtractionProvider? GetEnabledProvider(string name, DocumentExtractionOptions options)
    {
        var provider = _providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (provider == null) return null;

        var isEnabled = name.ToUpperInvariant() switch
        {
            "LOCAL_OCR" => options.LocalOcr.Enabled,
            "OPENAI" => options.OpenAi.Enabled,
            "AZURE_DOCUMENT_INTELLIGENCE" => options.AzureDocumentIntelligence.Enabled,
            _ => false
        };

        return isEnabled ? provider : null;
    }

    private int GetTimeoutForProvider(string name, DocumentExtractionOptions options)
    {
        int? providerTimeout = name.ToUpperInvariant() switch
        {
            "LOCAL_OCR" => options.LocalOcr.TimeoutSeconds,
            "OPENAI" => options.OpenAi.TimeoutSeconds,
            "AZURE_DOCUMENT_INTELLIGENCE" => options.AzureDocumentIntelligence.TimeoutSeconds,
            _ => null
        };

        return providerTimeout ?? (options.GlobalTimeoutSeconds > 0 ? options.GlobalTimeoutSeconds : 30);
    }
}
