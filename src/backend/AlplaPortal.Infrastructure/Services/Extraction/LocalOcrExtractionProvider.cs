using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Extraction;

public class LocalOcrExtractionProvider : IDocumentExtractionProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LocalOcrExtractionProvider> _logger;
    private readonly IDocumentExtractionSettingsService _settingsService;

    public string Name => "LOCAL_OCR";

    public LocalOcrExtractionProvider(
        HttpClient httpClient, 
        IDocumentExtractionSettingsService settingsService, 
        ILogger<LocalOcrExtractionProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task<ExtractionResultDto> ExtractAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        try
        {
            var options = await _settingsService.GetEffectiveSettingsAsync(ct);
            var settings = options.LocalOcr;
            var baseUrl = settings.BaseUrl;

            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogError("Local OCR BaseUrl is not configured.");
                return new ExtractionResultDto { Success = false, ProviderName = Name };
            }

            var contentType = GetMimeType(fileName);
            var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(fileContent, "file", fileName);

            _logger.LogInformation("Sending local OCR extraction request for {FileName} to {BaseUrl}", fileName, baseUrl);

            var response = await _httpClient.PostAsync($"{baseUrl}/extract/structured", content, ct);

            if (response.IsSuccessStatusCode)
            {
                var ocrResult = await response.Content.ReadFromJsonAsync<OcrExtractionResultDto>(cancellationToken: ct);
                if (ocrResult == null) return new ExtractionResultDto { Success = false, ProviderName = Name };

                return MapToInternalDto(ocrResult);
            }

            _logger.LogWarning("Local OCR service returned error: {StatusCode}", response.StatusCode);
            return new ExtractionResultDto { Success = false, ProviderName = Name };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Local OCR extraction was cancelled or timed out for {FileName}", fileName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during local OCR extraction for {FileName}", fileName);
            return new ExtractionResultDto { Success = false, ProviderName = Name };
        }
    }

    private ExtractionResultDto MapToInternalDto(OcrExtractionResultDto ocrResult)
    {
        var result = new ExtractionResultDto
        {
            Success = ocrResult.Success,
            ProviderName = Name,
            QualityScore = ocrResult.Status?.QualityScore ?? 0,
            Header = new ExtractionHeaderDto
            {
                SupplierName = ocrResult.Integration?.HeaderSuggestions?.SupplierName?.Value,
                DocumentNumber = ocrResult.Integration?.HeaderSuggestions?.DocumentNumber?.Value,
                DocumentDate = ocrResult.Integration?.HeaderSuggestions?.Date?.Value,
                Currency = ocrResult.Integration?.HeaderSuggestions?.CurrencyCode?.Value,
                TotalAmount = ocrResult.Integration?.HeaderSuggestions?.TotalAmount?.Value
            },
            Items = (ocrResult.Integration?.LineItemSuggestions ?? new()).Select((item, index) => new ExtractionLineItemDto
            {
                LineNumber = index + 1,
                Description = item.Description,
                Quantity = item.Quantity,
                Unit = item.Unit,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.TotalAmount
            }).ToList()
        };

        return result;
    }

    private static string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}
