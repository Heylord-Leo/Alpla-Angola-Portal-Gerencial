using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

public class OcrService : IOcrService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OcrService> _logger;
    private readonly string _baseUrl;

    public OcrService(HttpClient httpClient, IConfiguration configuration, ILogger<OcrService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["OcrService:BaseUrl"] ?? "http://localhost:5005";
    }

    public async Task<OcrExtractionResultDto> ExtractAsync(Stream fileStream, string fileName)
    {
        try
        {
            var contentType = GetMimeType(fileName);
            var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(fileContent, "file", fileName);

            _logger.LogInformation("Sending OCR extraction request: File={FileName}, ContentType={ContentType}, Size={Size} bytes", 
                fileName, contentType, fileStream.Length);

            var response = await _httpClient.PostAsync($"{_baseUrl}/extract/structured", content);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OcrExtractionResultDto>();
                _logger.LogInformation("OCR extraction successful for {FileName}", fileName);
                return result ?? new OcrExtractionResultDto { Success = false };
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("OCR service returned error: {StatusCode} - {Error} for file {FileName}", 
                response.StatusCode, errorContent, fileName);

            return new OcrExtractionResultDto
            {
                Success = false,
                Status = new OcrStatusDto
                {
                    Code = $"SERVICE_ERROR_{(int)response.StatusCode}",
                    QualityScore = 0
                }
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to OCR service at {BaseUrl}", _baseUrl);
            return new OcrExtractionResultDto
            {
                Success = false,
                Status = new OcrStatusDto
                {
                    Code = "SERVICE_UNAVAILABLE",
                    QualityScore = 0
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling OCR service");
            return new OcrExtractionResultDto
            {
                Success = false,
                Status = new OcrStatusDto
                {
                    Code = "INTERNAL_ERROR",
                    QualityScore = 0
                }
            };
        }
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
