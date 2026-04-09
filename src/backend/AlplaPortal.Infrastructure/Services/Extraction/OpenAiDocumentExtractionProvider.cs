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

public record DocumentRenderProfile(int Dpi, int MaxPages, ImageFormat Format, long Quality, string OpenAiDetailMode)
{
    public static DocumentRenderProfile InvoiceProfile => new(150, 3, ImageFormat.Jpeg, 85L, "high");
    public static DocumentRenderProfile SimpleInvoiceProfile => new(150, 3, ImageFormat.Jpeg, 85L, "low");
}

public enum DocumentStrategy
{
    Invoice,
    Contract,
    Other
}

/// <summary>
/// OpenAI implementation for document extraction with adaptive PDF rasterization support.
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

    public async Task<ExtractionResultDto> ExtractAsync(Stream fileStream, string fileName, string? sourceContext = null, CancellationToken ct = default)
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

            bool tryTextFirst = false;
            List<string> pagesText = new();
            DocumentStrategy strategy = DocumentStrategy.Invoice;

            var safeMemoryStream = new MemoryStream();
            await fileStream.CopyToAsync(safeMemoryStream, ct);
            safeMemoryStream.Position = 0;

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            _logger.LogInformation("OpenAI Extraction: File extension identified as '{Extension}' for file '{FileName}'", extension, fileName);

            if (extension == ".pdf")
            {
                using var triageStream = new MemoryStream(safeMemoryStream.ToArray());
                var triageResult = TriageDocument(triageStream, fileName, sourceContext);
                tryTextFirst = triageResult.isNative;
                pagesText = triageResult.pagesText;
                strategy = triageResult.strategy;
            }

            safeMemoryStream.Position = 0; // Reset for downstream

            var model = string.IsNullOrWhiteSpace(settings.Model) ? DefaultModel : settings.Model;

            if (strategy == DocumentStrategy.Contract)
            {
                _logger.LogInformation("Routing document '{FileName}' to Contract Pipeline.", fileName);
                return await ProcessContractAsync(pagesText, safeMemoryStream, fileName, model, apiKey, tryTextFirst, extension, ct);
            }

            string extractedNativeText = string.Join("\n\n--- PAGE BREAK ---\n\n", pagesText);
            var prompt = GetSystemPrompt();

            // TextFirst Attempt -> For native PDF Invoices
            if (tryTextFirst && strategy == DocumentStrategy.Invoice)
            {
                _logger.LogInformation("Native text detected (Length: {Len}). Attempting TextFirst path for {FileName}.", extractedNativeText.Length, fileName);
                var textPayload = new
                {
                    model = model,
                    messages = new object[]
                    {
                        new { role = "system", content = prompt },
                        new { role = "user", content = $"Extract data from this document:\n\n{extractedNativeText}" }
                    },
                    response_format = new { type = "json_object" },
                    temperature = 0.0
                };

                var textResult = await ExecuteOpenAiRequestAsync(textPayload, apiKey, fileName, "TextFirst", "n/a", model, ct);

                if (textResult.Success && textResult.QualityScore >= 0.7m && (textResult.Header.TotalAmount > 0 || !string.IsNullOrWhiteSpace(textResult.Header.SupplierName)))
                {
                    _logger.LogInformation("TextFirst extraction successful for {FileName} with QualityScore {Score}.", fileName, textResult.QualityScore);
                    textResult.Metadata.NativeTextDetected = true;
                    return textResult; // Successfully avoided Vision!
                }

                _logger.LogWarning("TextFirst extraction failed or returned insufficient quality ({Score}). Falling back to Vision for {FileName}.", textResult.QualityScore, fileName);
            }

            // --- Vision Path (Primary or Fallback) ---
            safeMemoryStream.Position = 0;

            // Phase 2: detail="low" experiment (Only if explicitly flagged. For now, manual trigger check could be added. Sticking to default high).
            bool useLowDetailExperiment = false; // Could check settings.OpenAi.UseLowDetailExperiment or similar
            var renderProfile = useLowDetailExperiment ? DocumentRenderProfile.SimpleInvoiceProfile : DocumentRenderProfile.InvoiceProfile;

            List<string> base64Images = new();
            string mimeType = renderProfile.Format == ImageFormat.Jpeg ? "image/jpeg" : "image/png";

            if (extension == ".pdf")
            {
                _logger.LogInformation("PDF Path detected. Calling RasterizePdfPagesAsync for '{FileName}'", fileName);
                base64Images = await RasterizePdfPagesAsync(safeMemoryStream, fileName, renderProfile, ct);
                _logger.LogInformation("PDF Rasterization completed for '{FileName}'. Total pages rasterized: {Count}", fileName, base64Images.Count);
            }
            else
            {
                var base64 = await ConvertStreamToBase64Async(safeMemoryStream);
                base64Images.Add(base64);
                mimeType = GetMimeType(fileName);
            }

            var userContentItems = new List<object>
            {
                new { type = "text", text = "Extract data from this document. It may contain multiple pages." }
            };

            foreach (var base64 in base64Images)
            {
                userContentItems.Add(new
                {
                    type = "image_url",
                    image_url = new 
                    { 
                        url = $"data:{mimeType};base64,{base64}",
                        detail = renderProfile.OpenAiDetailMode
                    }
                });
            }

            var visionPayload = new
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

            var routingStrat = tryTextFirst ? "VisionFallback" : "VisionFirst";
            var finalResult = await ExecuteOpenAiRequestAsync(visionPayload, apiKey, fileName, routingStrat, renderProfile.OpenAiDetailMode, model, ct);
            
            finalResult.Metadata.NativeTextDetected = tryTextFirst;
            finalResult.Metadata.PagesProcessed = base64Images.Count;
            finalResult.Metadata.ProcessingDpi = renderProfile.Dpi;
            finalResult.Metadata.ProcessingFormat = renderProfile.Format.ToString();

            return finalResult;
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

    private async Task<ExtractionResultDto> ExecuteOpenAiRequestAsync(object payload, string apiKey, string fileName, string routingStrat, string detailMode, string model, CancellationToken ct)
    {
        var jsonOptions = new JsonSerializerOptions 
        { 
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
        };
        var jsonPayloadStr = JsonSerializer.Serialize(payload, jsonOptions);

        _logger.LogInformation("OpenAI Request Structure -> Provider: {Provider}, Routing: {Strat}, Detail: {Detail}, Approx Payload Size: {Size} chars", 
            Name, routingStrat, detailMode, jsonPayloadStr.Length);

        var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl)
        {
            Content = new StringContent(jsonPayloadStr, Encoding.UTF8, "application/json")
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
                    string jsonDebugPath = Path.Combine(jsonDebugFolder, $"{safeFileName}_{routingStrat}_{timestamp}.json");
                    
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

                int promptTokens = 0, completionTokens = 0, totalTokens = 0;
                if (root.TryGetProperty("usage", out var usageProp))
                {
                    promptTokens = usageProp.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                    completionTokens = usageProp.TryGetProperty("completion_tokens", out var ctProp) ? ctProp.GetInt32() : 0;
                    totalTokens = usageProp.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : 0;
                }

                if (string.IsNullOrEmpty(messageContent))
                {
                    _logger.LogWarning("OpenAI returned an empty completion.");
                    return new ExtractionResultDto { Success = false, ProviderName = Name };
                }

                var extractedResult = routingStrat.StartsWith("Contract") 
                    ? MapContractFromJson(messageContent) 
                    : MapFromJson(messageContent);
                
                extractedResult.Metadata = new ExtractionMetadataDto
                {
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = totalTokens,
                    PagesProcessed = 0,
                    RoutingStrategy = routingStrat,
                    DetailMode = detailMode
                };
                
                _logger.LogInformation("OpenAI Extraction Success [{Strat}]. Tokens Used -> Prompt: {Prompt}, Completion: {Completion}, Total: {Total}", 
                    routingStrat, promptTokens, completionTokens, totalTokens);

                return extractedResult;
            }

            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("OpenAI API returned error: {StatusCode}. Details: {Details}", response.StatusCode, errorContent);
            return new ExtractionResultDto { Success = false, ProviderName = Name };
    }

    private (bool isNative, List<string> pagesText, DocumentStrategy strategy) TriageDocument(Stream pdfStream, string fileName, string? sourceContext = null)
    {
        try
        {
            using var document = PdfDocument.Load(pdfStream);
            int totalPages = document.PageCount;
            
            // 1. Triage: read first 3 pages
            int triagePages = Math.Min(totalPages, 3);
            StringBuilder triageSb = new StringBuilder();
            
            for(int i = 0; i < triagePages; i++)
            {
                var text = document.GetPdfText(i);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    triageSb.AppendLine(text);
                }
            }

            var triageText = triageSb.ToString();
            var triageLines = triageText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool isNative = triageText.Length > 100 && triageLines.Length >= 3;

            DocumentStrategy strategy = DocumentStrategy.Invoice;
            
            // --- CONTEXT-AWARE OVERRIDE ---
            // If the request originates from quotation or payment request screens,
            // the user is definitely uploading an invoice/proforma, never a contract.
            bool contextForcesInvoice = !string.IsNullOrWhiteSpace(sourceContext) &&
                (sourceContext.Equals("quotation", StringComparison.OrdinalIgnoreCase) ||
                 sourceContext.Equals("payment_request", StringComparison.OrdinalIgnoreCase));

            if (contextForcesInvoice)
            {
                _logger.LogInformation("Triage: sourceContext='{Context}' forces Invoice strategy for '{FileName}'. Skipping contract classification.", sourceContext, fileName);
            }
            else if (isNative)
            {
                var lowerText = triageText.ToLowerInvariant();
                int invoiceSignals = 0;
                int contractSignals = 0;

                // Strong invoice keywords — if ANY of these match, the document is an invoice/proforma
                string[] strongInvoiceKeywords = { "invoice", "fatura", "factura", "proforma", "pro-forma", "pro forma",
                    "purchase order", "encomenda", "nota de débito", "nota de debito", "recibo", "receipt" };
                // General invoice signals
                string[] invoiceKeywords = { "subtotal", "zwischensumme", "iva", "tax", "vat", "total amount",
                    "total documento", "quantidade", "unit price", "preço unitário" };
                // Contract keywords
                string[] contractKeywords = { "contrato", "contract", "acordo", "nda", "n.d.a", "master service agreement",
                    "terms and conditions", "confidentiality", "cláusula", "clausula", "signatário", "signatario" };
                // Weak contract signals — common in invoices too, should NOT trigger contract classification alone
                // "termo", "termos" removed from contract keywords as they appear frequently in payment terms of invoices

                // Check strong invoice keywords first — these are definitive
                bool hasStrongInvoiceSignal = false;
                foreach (var kw in strongInvoiceKeywords)
                {
                    if (lowerText.Contains(kw))
                    {
                        hasStrongInvoiceSignal = true;
                        invoiceSignals += 3; // Heavy weight
                    }
                }

                foreach (var kw in invoiceKeywords) if (lowerText.Contains(kw)) invoiceSignals++;
                foreach (var kw in contractKeywords) if (lowerText.Contains(kw)) contractSignals++;

                // Filename heuristic — common invoice/PO prefixes
                var baseFileName = Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant();
                string[] invoiceFilePrefixes = { "FA", "INV", "PRO", "PO", "ENC", "NF", "REC", "FT" };
                if (invoiceFilePrefixes.Any(p => baseFileName.StartsWith(p)))
                {
                    invoiceSignals += 2;
                    _logger.LogInformation("Triage: Filename '{FileName}' matches invoice prefix pattern. Boosting invoice score.", fileName);
                }

                bool denseText = triageText.Length > 2000 && triageLines.Length > 20;

                // Contract classification requires ALL of:
                // 1. No strong invoice signal present
                // 2. At least 2 distinct contract keywords matched
                // 3. Contract signals clearly dominate invoice signals
                // 4. Dense text (multi-paragraph legal document)
                // 5. Multi-page document (contracts are rarely 1 page)
                if (!hasStrongInvoiceSignal && contractSignals >= 2 && contractSignals > invoiceSignals + 1 && denseText && totalPages > 1)
                {
                    strategy = DocumentStrategy.Contract;
                    _logger.LogInformation("Triage: Classified '{FileName}' as Contract (contractSignals={CS}, invoiceSignals={IS}, pages={Pages}).",
                        fileName, contractSignals, invoiceSignals, totalPages);
                }
                else
                {
                    _logger.LogInformation("Triage: Classified '{FileName}' as Invoice (contractSignals={CS}, invoiceSignals={IS}, strongInvoice={SI}, pages={Pages}).",
                        fileName, contractSignals, invoiceSignals, hasStrongInvoiceSignal, totalPages);
                }
            }

            List<string> pagesText = new();
            if (isNative)
            {
                if (strategy == DocumentStrategy.Contract)
                {
                    // For contract, get up to 50 pages to prevent overflow, but enough for most contracts
                    int contractLimit = Math.Min(totalPages, 50);
                    for(int i = 0; i < contractLimit; i++)
                    {
                        var text = document.GetPdfText(i);
                        if (!string.IsNullOrWhiteSpace(text)) pagesText.Add(text);
                    }
                }
                else
                {
                    // For invoice, get up to 5 pages
                    int invoiceLimit = Math.Min(totalPages, 5);
                    StringBuilder sb = new StringBuilder();
                    for(int i = 0; i < invoiceLimit; i++)
                    {
                        var text = document.GetPdfText(i);
                        if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine(text);
                    }
                    if (sb.Length > 0) pagesText.Add(sb.ToString());
                }
            }

            return (isNative, pagesText, strategy);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to comprehensively extract native text from PDF for triage.");
            return (false, new List<string>(), DocumentStrategy.Invoice);
        }
    }

    private async Task<List<string>> RasterizePdfPagesAsync(Stream pdfStream, string originalFileName, DocumentRenderProfile profile, CancellationToken ct)
    {
        _logger.LogInformation("Entering RasterizePdfPagesAsync for file: {FileName} (Profile: {Format}, {Dpi} DPI, Limit {MaxPages} pages)", 
            originalFileName, profile.Format.ToString(), profile.Dpi, profile.MaxPages);
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

            // Apply profile limit to avoid excessive payload size and cost
            int pagesToProcess = Math.Min(totalPages, profile.MaxPages);
            if (totalPages > profile.MaxPages)
            {
                _logger.LogWarning("PDF has {Total} pages, but processing limit is {MaxPages}. Only the first {MaxPages} will be processed.", totalPages, profile.MaxPages, profile.MaxPages);
            }

            string debugFolder = @"C:\dev\alpla-portal\debug\openai-rasterized\";
            string safeFileName = Path.GetFileNameWithoutExtension(originalFileName);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            for (int i = 0; i < pagesToProcess; i++)
            {
                _logger.LogInformation("Rasterizing page {Page} of {Total} for {FileName}", i + 1, pagesToProcess, originalFileName);
                
                // Render page at profile DPI
                var pageSize = document.PageSizes[i];
                int width = (int)(pageSize.Width * profile.Dpi / 72);
                int height = (int)(pageSize.Height * profile.Dpi / 72);

                using var image = document.Render(i, width, height, profile.Dpi, profile.Dpi, true);
                using var outMs = new MemoryStream();
                
                if (profile.Format == ImageFormat.Jpeg)
                {
                    var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, profile.Quality);
                    image.Save(outMs, jpegEncoder, encoderParams);
                }
                else
                {
                    image.Save(outMs, profile.Format);
                }
                byte[] bytes = outMs.ToArray();

                // Save debug image for each page
                try
                {
                    if (!Directory.Exists(debugFolder))
                    {
                        Directory.CreateDirectory(debugFolder);
                    }

                    string debugExt = profile.Format == ImageFormat.Jpeg ? "jpg" : "png";
                    string debugPath = Path.Combine(debugFolder, $"{safeFileName}_{timestamp}_page{i + 1}.{debugExt}");
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
        return @"You are a financial OCR expert. Extract data from this invoice, proforma, or purchase order (Encomenda).
CRITICAL PRECISION RULES:
- SUPPLIER IDENTIFICATION: If the document is a Purchase Order (e.g. 'Encomenda'), the issuing company (like 'ALPLA') is usually at the top, and the ACTUAL SUPPLIER is usually listed under 'Exmo.(s) Sr.(s)' or 'Srs.' or 'Fornecedor'. DO NOT confuse the billed company with the supplier.
- QUANTITY (Menge/Qtd.): Capture EVERY digit. If it says '21', do not return '2'. Look closely at column alignment and digit spacing.
- UNIT PRICE (Einzelpreis/Stückpreis/Pr. Unitário): Capture the full numerical value exactly as printed.

- DISCOUNTS — CRITICAL COLUMN DISAMBIGUATION:
  Portuguese invoices have SEPARATE columns for discount and tax. You MUST distinguish:
    * Column 'Desc.' or 'Desconto' = DISCOUNT PERCENTAGE (e.g. 7.00 means 7% discount, 25.00 means 25% discount)
    * Column 'IVA' or 'Taxa' = TAX/VAT RATE (e.g. 14.00 means 14% tax)
  These are DIFFERENT columns. Do NOT confuse them. Do NOT use the IVA value as discountPercent.
  German invoices use 'Rabatt' for discount and 'MwSt' for tax.

  Extraction rules for each item:
    * discountPercent = the value from the Desc./Desconto/Rabatt column. If no discount column exists, set to 0.
    * discountAmount = THE TOTAL DISCOUNT FOR THE ENTIRE LINE = quantity × unitPrice × (discountPercent / 100).
      Example: qty=1, unitPrice=510000, Desc.=7% → discountAmount = 1 × 510000 × 0.07 = 35700.
    * taxRate = the value from the IVA/Taxa/MwSt column. This is NOT a discount. If 14.00 appears in the IVA column, taxRate=14.
    * If no discount column or value exists for a line, set discountPercent=0 and discountAmount=0.
  SELF-VALIDATION: totalPrice should equal (quantity × unitPrice) - discountAmount. 
    If this does not match the printed 'Valor' column, re-examine which column is discount vs tax.

- LINE TOTAL (Ges.preis/Valor): totalPrice = (quantity × unitPrice) - discountAmount. If discount=100%, totalPrice = 0.
- HEADER totalAmount = the Zwischensumme/Subtotal (net total after all line discounts, before tax).
- HEADER grandTotal = the FINAL total the buyer must pay, INCLUDING all taxes (IVA). This is the 'Total' or 'Total (AKZ)' or 'TOTAL DOCUMENTO' line on the document. If there is no separate grand total, set grandTotal = totalAmount.

Output ONLY JSON with this structure:
{
  ""header"": {
    ""supplierName"": ""string"",
    ""supplierTaxId"": ""string"",
    ""billedCompanyName"": ""Name of company being billed. Typically 'AlplaPLASTICO' or 'AlplaSOPRO'. Look for keywords PLASTICO vs SOPRO."",
    ""documentNumber"": ""string"",
    ""documentDate"": ""ISO date"",
    ""dueDate"": ""ISO date"",
    ""currency"": ""string (e.g. EUR, USD, AOA)"",
    ""totalAmount"": number (Zwischensumme / Subtotal after all discounts, before tax),
    ""grandTotal"": number (Final total INCLUDING tax/IVA - this is what the buyer actually pays)
  },
  ""items"": [
    {
      ""description"": ""string"",
      ""quantity"": number,
      ""unit"": ""string"",
      ""unitPrice"": number,
      ""discountPercent"": number (from Desc./Desconto column, e.g. 7 for 7%, 25 for 25%, 0 if none),
      ""discountAmount"": number (TOTAL line discount = qty × unitPrice × discountPercent/100),
      ""taxRate"": number (from IVA/Taxa column — NOT the discount),
      ""totalPrice"": number (after discount, before tax)
    }
  ],
  ""qualityScore"": 0.95
}";
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
                    BilledCompanyName = header.TryGetProperty("billedCompanyName", out var bcn) ? bcn.GetString() : null,
                    DocumentNumber = header.TryGetProperty("documentNumber", out var dn) ? dn.GetString() : null,
                    DocumentDate = header.TryGetProperty("documentDate", out var dd) ? dd.GetString() : null,
                    Currency = header.TryGetProperty("currency", out var cur) ? cur.GetString() : null,
                    TotalAmount = header.TryGetProperty("totalAmount", out var ta) ? (ta.ValueKind == JsonValueKind.Number ? ta.GetDecimal() : 0) : null,
                    GrandTotal = header.TryGetProperty("grandTotal", out var gt) ? (gt.ValueKind == JsonValueKind.Number ? gt.GetDecimal() : 0) : null,
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
                    DiscountAmount = item.TryGetProperty("discountAmount", out var da) ? (da.ValueKind == JsonValueKind.Number ? da.GetDecimal() : 0) : 0,
                    DiscountPercent = item.TryGetProperty("discountPercent", out var dp) ? (dp.ValueKind == JsonValueKind.Number ? dp.GetDecimal() : 0) : 0,
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

    private async Task<ExtractionResultDto> ProcessContractAsync(List<string> pagesText, Stream pdfStream, string fileName, string model, string apiKey, bool tryTextFirst, string extension, CancellationToken ct)
    {
        var result = new ExtractionResultDto { Success = true, ProviderName = Name, Contract = new ExtractionContractDto() };
        int promptTokens = 0, completionTokens = 0;
        int chunkCount = 0;
        bool conflictsDetected = false;
        
        string systemPrompt = GetContractSystemPrompt();

        if (tryTextFirst && pagesText.Count > 0)
        {
            _logger.LogInformation("Starting TextFirst sequential chunking for contract: {FileName}", fileName);
            var chunks = new List<string>();
            StringBuilder currentChunk = new();
            foreach (var page in pagesText)
            {
                if (currentChunk.Length + page.Length > 20000)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                }
                currentChunk.AppendLine(page);
            }
            if (currentChunk.Length > 0) chunks.Add(currentChunk.ToString());
            chunkCount = chunks.Count;

            _logger.LogInformation("Contract divided into {ChunkCount} chunks for {FileName}", chunkCount, fileName);

            for (int i = 0; i < chunks.Count; i++)
            {
                string currentContext = JsonSerializer.Serialize(result.Contract);
                string userContent = $"This is chunk {i + 1} of {chunks.Count}. Update the context with any NEW findings. Do NOT overwrite existing data unless there is strong contradictory evidence. If unsure, leave as is. \n\nCurrent State: {currentContext}\n\nDocument Text:\n{chunks[i]}";

                var payload = new
                {
                    model = model,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContent }
                    },
                    response_format = new { type = "json_object" },
                    temperature = 0.0
                };

                var chunkResult = await ExecuteOpenAiRequestAsync(payload, apiKey, fileName, "ContractTextChunk", "n/a", model, ct);
                
                promptTokens += chunkResult.Metadata.PromptTokens;
                completionTokens += chunkResult.Metadata.CompletionTokens;

                if (chunkResult.Success && chunkResult.Contract != null)
                {
                    MergeContractResults(result.Contract, chunkResult.Contract, ref conflictsDetected);
                }
            }

            result.Metadata = new ExtractionMetadataDto
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens,
                PagesProcessed = pagesText.Count,
                RoutingStrategy = "ContractTextFirst",
                DetailMode = "n/a",
                NativeTextDetected = true,
                ChunkCount = chunkCount,
                ConflictsDetected = conflictsDetected,
                IsPartial = IsContractPartial(result.Contract)
            };
            
            result.QualityScore = 0.9m;
            return result;
        }

        _logger.LogInformation("Falling back to Vision for Contract: {FileName}", fileName);
        
        pdfStream.Position = 0;
        var renderProfile = DocumentRenderProfile.InvoiceProfile with { MaxPages = 5 }; // Narrow Vision Fallback

        var base64Images = new List<string>();
        if (extension == ".pdf")
        {
            base64Images = await RasterizePdfPagesAsync(pdfStream, fileName, renderProfile, ct);
        }
        else
        {
            var base64 = await ConvertStreamToBase64Async(pdfStream);
            base64Images.Add(base64);
        }

        var userContentItems = new List<object>
        {
            new { type = "text", text = "This is a contract or legal document. Please extract the requested metadata. It may be partial." }
        };

        foreach (var img in base64Images)
        {
            userContentItems.Add(new
            {
                type = "image_url",
                image_url = new { url = $"data:image/jpeg;base64,{img}", detail = "low" }
            });
        }

        var visionPayload = new
        {
            model = model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContentItems.ToArray() }
            },
            response_format = new { type = "json_object" },
            temperature = 0.0
        };

        var visionResult = await ExecuteOpenAiRequestAsync(visionPayload, apiKey, fileName, "ContractVisionFallback", "low", model, ct);
        
        visionResult.Metadata.ChunkCount = 1;
        visionResult.Metadata.IsPartial = IsContractPartial(visionResult.Contract);
        visionResult.Metadata.RoutingStrategy = "ContractVisionFallback";
        return visionResult;
    }

    private void MergeContractResults(ExtractionContractDto target, ExtractionContractDto source, ref bool conflicts)
    {
        if (!string.IsNullOrWhiteSpace(source.DocumentType) && string.IsNullOrWhiteSpace(target.DocumentType)) target.DocumentType = source.DocumentType;
        else if (!string.IsNullOrWhiteSpace(source.DocumentType) && target.DocumentType != source.DocumentType && target.DocumentType != null && !target.DocumentType.Contains(source.DocumentType)) conflicts = true;

        if (!string.IsNullOrWhiteSpace(source.Parties) && string.IsNullOrWhiteSpace(target.Parties)) target.Parties = source.Parties;
        else if (!string.IsNullOrWhiteSpace(source.Parties) && target.Parties != source.Parties && target.Parties != null && !target.Parties.Contains(source.Parties)) target.Parties += "; " + source.Parties;

        if (!string.IsNullOrWhiteSpace(source.EffectiveDate) && string.IsNullOrWhiteSpace(target.EffectiveDate)) target.EffectiveDate = source.EffectiveDate;
        if (!string.IsNullOrWhiteSpace(source.EndDate) && string.IsNullOrWhiteSpace(target.EndDate)) target.EndDate = source.EndDate;
        if (!string.IsNullOrWhiteSpace(source.GoverningLaw) && string.IsNullOrWhiteSpace(target.GoverningLaw)) target.GoverningLaw = source.GoverningLaw;
        if (!string.IsNullOrWhiteSpace(source.PaymentTerms) && string.IsNullOrWhiteSpace(target.PaymentTerms)) target.PaymentTerms = source.PaymentTerms;
        if (!string.IsNullOrWhiteSpace(source.TerminationClauses) && string.IsNullOrWhiteSpace(target.TerminationClauses)) target.TerminationClauses = source.TerminationClauses;
    }

    private bool IsContractPartial(ExtractionContractDto? c)
    {
        if (c == null) return true;
        return string.IsNullOrWhiteSpace(c.DocumentType) || string.IsNullOrWhiteSpace(c.Parties);
    }

    private string GetContractSystemPrompt()
    {
        return @"You are a specialized legal document extractor.
Extract the core metadata from the provided contract text or image. 
Return a JSON object with a single 'contract' object containing these fields:
- documentType: Type of contract (e.g. NDA, Master Service Agreement, Lease, etc.)
- parties: Names of the entities involved
- effectiveDate: Start or signature date (YYYY-MM-DD if possible)
- endDate: Expiration or termination date (YYYY-MM-DD)
- governingLaw: State or country law governing the agreement
- paymentTerms: Any extracted payment terms
- terminationClauses: Brief summary of termination conditions

If a field is not found in the current chunk, return null. Do not hallucinate.
Only return the JSON object, no other text.";
    }

    private ExtractionResultDto MapContractFromJson(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rawResult = JsonSerializer.Deserialize<JsonElement>(json, options);

            var result = new ExtractionResultDto
            {
                Success = true,
                ProviderName = Name,
                QualityScore = rawResult.TryGetProperty("qualityScore", out var qs) ? (decimal)qs.GetDouble() : 0.9m,
                Contract = new ExtractionContractDto()
            };

            if (rawResult.TryGetProperty("contract", out var contractObj))
            {
                result.Contract.DocumentType = contractObj.TryGetProperty("documentType", out var dt) ? dt.GetString() : null;
                result.Contract.Parties = contractObj.TryGetProperty("parties", out var p) ? p.GetString() : null;
                result.Contract.EffectiveDate = contractObj.TryGetProperty("effectiveDate", out var ed) ? ed.GetString() : null;
                result.Contract.EndDate = contractObj.TryGetProperty("endDate", out var endd) ? endd.GetString() : null;
                result.Contract.GoverningLaw = contractObj.TryGetProperty("governingLaw", out var gl) ? gl.GetString() : null;
                result.Contract.PaymentTerms = contractObj.TryGetProperty("paymentTerms", out var pt) ? pt.GetString() : null;
                result.Contract.TerminationClauses = contractObj.TryGetProperty("terminationClauses", out var tc) ? tc.GetString() : null;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to map OpenAI Contract JSON response. JSON: {Json}", json);
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
