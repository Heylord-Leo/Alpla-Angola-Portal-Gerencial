using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Drawing;
using System.Drawing.Imaging;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Models.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PdfiumViewer;

namespace AlplaPortal.Infrastructure.Services.Extraction;

/// <summary>
/// OpenAI implementation for document extraction with PDF rasterization support.
/// </summary>
public class OpenAiDocumentExtractionProvider : IDocumentExtractionProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiDocumentExtractionProvider> _logger;
    private readonly IDocumentExtractionSettingsService _settingsService;

    public string Name => "OPENAI";

    private const string DefaultModel = "gpt-4o-mini";
    private const string ApiBaseUrl = "https://api.openai.com/v1/chat/completions";

    public OpenAiDocumentExtractionProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        IDocumentExtractionSettingsService settingsService,
        ILogger<OpenAiDocumentExtractionProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task<ExtractionResultDto> ExtractAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        try
        {
            var options = await _settingsService.GetEffectiveSettingsAsync(ct);
            var settings = options.OpenAi;
            var apiKey = _configuration["OPENAI_API_KEY"];

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("OpenAI API Key is not configured in environment variables.");
                return new ExtractionResultDto { Success = false, ProviderName = Name };
            }

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            _logger.LogInformation("OpenAI Extraction: File extension identified as '{Extension}' for file '{FileName}'", extension, fileName);

            List<string> base64Images = new();
            string mimeType = "image/png";

            if (extension == ".pdf")
            {
                _logger.LogInformation("PDF Path detected. Calling RasterizePdfPagesAsync for '{FileName}'", fileName);
                base64Images = await RasterizePdfPagesAsync(fileStream, fileName, ct);
                _logger.LogInformation("PDF Rasterization completed for '{FileName}'. Total pages rasterized: {Count}", fileName, base64Images.Count);
            }
            else
            {
                var base64 = await ConvertStreamToBase64Async(fileStream);
                base64Images.Add(base64);
                mimeType = GetMimeType(fileName);
            }

            var model = string.IsNullOrWhiteSpace(settings.Model) ? DefaultModel : settings.Model;
            var prompt = GetSystemPrompt();
            
            // Build multi-page content array
            var userContentItems = new List<object>
            {
                new { type = "text", text = "Extract data from this document. It may contain multiple pages." }
            };

            foreach (var base64 in base64Images)
            {
                userContentItems.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:{mimeType};base64,{base64}" }
                });
            }

            var payload = new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = prompt },
                    new
                    {
                        role = "user",
                        content = userContentItems.ToArray()
                    }
                },
                response_format = new { type = "json_object" },
                temperature = 0.0
            };

            var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            _logger.LogInformation("Sending OpenAI extraction request for {FileName} (Model: {Model})", fileName, model);

            var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                _logger.LogDebug("OpenAI raw response: {Response}", content);

                // Part B: Save raw JSON response locally for inspection/debugging
                try
                {
                    string jsonDebugFolder = @"C:\dev\alpla-portal\debug\openai-json\";
                    if (!Directory.Exists(jsonDebugFolder))
                    {
                        Directory.CreateDirectory(jsonDebugFolder);
                        _logger.LogInformation("Created JSON debug directory: {Path}", jsonDebugFolder);
                    }

                    string safeFileName = Path.GetFileNameWithoutExtension(fileName);
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string jsonDebugPath = Path.Combine(jsonDebugFolder, $"{safeFileName}_{timestamp}.json");
                    
                    await File.WriteAllTextAsync(jsonDebugPath, content, ct);
                    _logger.LogInformation("Successfully saved raw OpenAI JSON response to: {Path}", jsonDebugPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CRITICAL DEBUG FAILURE: Failed to save raw OpenAI JSON response to disk.");
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                var messageContent = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

                if (string.IsNullOrEmpty(messageContent))
                {
                    _logger.LogWarning("OpenAI returned an empty completion.");
                    return new ExtractionResultDto { Success = false, ProviderName = Name };
                }

                return MapFromJson(messageContent);
            }

            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("OpenAI API returned error: {StatusCode}. Details: {Details}", response.StatusCode, errorContent);
            return new ExtractionResultDto { Success = false, ProviderName = Name };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("OpenAI extraction was cancelled or timed out for {FileName}", fileName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OpenAI extraction for {FileName}", fileName);
            return new ExtractionResultDto { Success = false, ProviderName = Name };
        }
    }

    private async Task<List<string>> RasterizePdfPagesAsync(Stream pdfStream, string originalFileName, CancellationToken ct)
    {
        _logger.LogInformation("Entering RasterizePdfPagesAsync for file: {FileName}", originalFileName);
        // Copy to MemoryStream to ensure seekability
        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms, ct);
        ms.Position = 0;
        _logger.LogDebug("PDF stream copied to memory, size: {Size} bytes", ms.Length);

        var resultBase64List = new List<string>();

        try
        {
            using var document = PdfDocument.Load(ms);
            int totalPages = document.PageCount;
            _logger.LogInformation("PdfDocument loaded. Page count: {Count}", totalPages);
            
            if (totalPages == 0)
            {
                throw new Exception("PDF document has no pages.");
            }

            // Limit to 10 pages to avoid excessive payload size and cost
            int pagesToProcess = Math.Min(totalPages, 10);
            if (totalPages > 10)
            {
                _logger.LogWarning("PDF has {Total} pages, but processing limit is 10. Only the first 10 will be processed.", totalPages);
            }

            string debugFolder = @"C:\dev\alpla-portal\debug\openai-rasterized\";
            string safeFileName = Path.GetFileNameWithoutExtension(originalFileName);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            for (int i = 0; i < pagesToProcess; i++)
            {
                _logger.LogInformation("Rasterizing page {Page} of {Total} for {FileName}", i + 1, pagesToProcess, originalFileName);
                
                // Render page at 300 DPI for high quality extraction
                var pageSize = document.PageSizes[i];
                int width = (int)(pageSize.Width * 300 / 72);
                int height = (int)(pageSize.Height * 300 / 72);

                using var image = document.Render(i, width, height, 300, 300, true);
                using var outMs = new MemoryStream();
                image.Save(outMs, ImageFormat.Png);
                byte[] bytes = outMs.ToArray();

                // Save debug image for each page
                try
                {
                    if (!Directory.Exists(debugFolder))
                    {
                        Directory.CreateDirectory(debugFolder);
                    }

                    string debugPath = Path.Combine(debugFolder, $"{safeFileName}_{timestamp}_page{i + 1}.png");
                    File.WriteAllBytes(debugPath, bytes);
                    _logger.LogInformation("Saved debug rasterized page {Page} to: {Path}", i + 1, debugPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save debug rasterized image for page {Page}.", i + 1);
                }

                resultBase64List.Add(Convert.ToBase64String(bytes));
            }

            return resultBase64List;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CRITICAL RASTERIZATION FAILURE for file {FileName}", originalFileName);
            throw new Exception($"Failed to rasterize PDF '{originalFileName}' for extraction.", ex);
        }
    }

    private async Task<string> ConvertStreamToBase64Async(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private string GetSystemPrompt()
    {
        return @"You are a professional document extractor. 
Extract data from the provided image into a structured JSON format. 
Return a JSON object with 'header' and 'items'.

'header' fields:
- supplierName: Name of the vendor/supplier
- supplierTaxId: Tax ID or NIF of the supplier. 
  PRIORITIZE patterns like 'Contribuinte N.º:', 'Contribuinte No:', 'Contribuinte Nº:', 'Numero contribuinte:', 'Número contribuinte:', 'Nº Contribuinte:'.
  Also look for 'NIF:', 'Tax ID:', 'VAT:', 'CIF:'.
  The value is a digit-only string (e.g. 5417049840) or possibly with letters if international.
  It might be on the same line as the label (after a colon or space) or on the line immediately preceding or following the label.
  IMPORTANT: The label identification is case-insensitive and punctuation-insensitive.
- documentNumber: Invoice or Quotation number
- documentDate: Date of the document (YYYY-MM-DD)
- currency: ISO Currency code (e.g. AOA, USD, EUR)
- totalAmount: Numerical total amount
- discountAmount: Numerical global discount or total deduction amount (absolute value)

'items' list fields:
- description: Item description
- quantity: Numerical quantity
- unit: Unit of measure (e.g. UN, KG, L)
- unitPrice: Numerical unit price
- taxRate: Numerical tax percentage (e.g. 14, 14.0, 0, 5). Do not include the % symbol.
- totalPrice: Numerical total price for the line

Include a 'qualityScore' (0.0 to 1.0) reflecting your confidence in the overall extraction.
Only return the JSON object, no other text.";
    }

    private ExtractionResultDto MapFromJson(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rawResult = JsonSerializer.Deserialize<JsonElement>(json, options);

            var result = new ExtractionResultDto
            {
                Success = true,
                ProviderName = Name,
                QualityScore = rawResult.TryGetProperty("qualityScore", out var qs) ? (decimal)qs.GetDouble() : 0.9m
            };

            if (rawResult.TryGetProperty("header", out var header))
            {
                result.Header = new ExtractionHeaderDto
                {
                    SupplierName = header.TryGetProperty("supplierName", out var sn) ? sn.GetString() : null,
                    SupplierTaxId = header.TryGetProperty("supplierTaxId", out var stid) ? stid.GetString() : null,
                    DocumentNumber = header.TryGetProperty("documentNumber", out var dn) ? dn.GetString() : null,
                    DocumentDate = header.TryGetProperty("documentDate", out var dd) ? dd.GetString() : null,
                    Currency = header.TryGetProperty("currency", out var cur) ? cur.GetString() : null,
                    TotalAmount = header.TryGetProperty("totalAmount", out var ta) ? (ta.ValueKind == JsonValueKind.Number ? ta.GetDecimal() : 0) : null,
                    DiscountAmount = header.TryGetProperty("discountAmount", out var da) ? (da.ValueKind == JsonValueKind.Number ? da.GetDecimal() : 0) : null
                };
            }

            if (rawResult.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                result.Items = items.EnumerateArray().Select((item, index) => new ExtractionLineItemDto
                {
                    LineNumber = index + 1,
                    Description = item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    Quantity = item.TryGetProperty("quantity", out var qty) ? (qty.ValueKind == JsonValueKind.Number ? qty.GetDecimal() : 0) : null,
                    Unit = item.TryGetProperty("unit", out var unit) ? unit.GetString() : null,
                    UnitPrice = item.TryGetProperty("unitPrice", out var up) ? (up.ValueKind == JsonValueKind.Number ? up.GetDecimal() : 0) : null,
                    TaxRate = item.TryGetProperty("taxRate", out var tr) ? (tr.ValueKind == JsonValueKind.Number ? tr.GetDecimal() : 0) : null,
                    TotalPrice = item.TryGetProperty("totalPrice", out var tp) ? (tp.ValueKind == JsonValueKind.Number ? tp.GetDecimal() : 0) : null
                }).ToList();
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to map OpenAI JSON response to internal DTO. JSON: {Json}", json);
            return new ExtractionResultDto { Success = false, ProviderName = Name };
        }
    }

    private static string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/jpeg" // Default for vision
        };
    }
}
