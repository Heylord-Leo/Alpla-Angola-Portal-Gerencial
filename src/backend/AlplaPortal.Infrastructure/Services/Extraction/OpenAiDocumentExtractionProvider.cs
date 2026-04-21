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
            _logger.LogInformation(
                "[OCR] ExtractAsync started — File: '{FileName}', Extension: '{Extension}', SourceContext: '{SourceContext}', StreamLength: {Bytes} bytes",
                fileName, extension, sourceContext ?? "(none)", safeMemoryStream.Length);

            // ── Context-forced CONTRACT strategy ─────────────────────────────────────
            // When the caller explicitly passes sourceContext="CONTRACT", we skip the
            // keyword-based triage entirely. The triage is designed for invoice vs contract
            // disambiguation in general upload flows; for the contract OCR trigger endpoint
            // the document type is already known. Without this override, a contract PDF
            // that happens to score < 2 contract keyword signals (short, multi-language,
            // or scanned) will fall through to the invoice pipeline and produce
            // extractionResult.Contract == null → background processor FAILED.
            bool contextForcesContract = !string.IsNullOrWhiteSpace(sourceContext) &&
                sourceContext.Equals("CONTRACT", StringComparison.OrdinalIgnoreCase);

            if (extension == ".pdf")
            {
                if (contextForcesContract)
                {
                    // Force contract strategy; still run triage to detect native text for text-first path
                    _logger.LogInformation(
                        "[OCR] sourceContext='CONTRACT' detected — forcing DocumentStrategy.Contract for '{FileName}'. Triage will run only to detect native text.",
                        fileName);
                    using var triageStream = new MemoryStream(safeMemoryStream.ToArray());
                    var triageResult = TriageDocument(triageStream, fileName, sourceContext);
                    tryTextFirst = triageResult.isNative;
                    pagesText    = triageResult.pagesText;
                    strategy     = DocumentStrategy.Contract; // Override regardless of keyword signals
                    _logger.LogInformation(
                        "[OCR] Triage result for forced-contract '{FileName}': IsNative={IsNative}, PageTextChunks={Chunks}. Strategy locked to Contract.",
                        fileName, tryTextFirst, pagesText.Count);
                }
                else
                {
                    using var triageStream = new MemoryStream(safeMemoryStream.ToArray());
                    var triageResult = TriageDocument(triageStream, fileName, sourceContext);
                    tryTextFirst = triageResult.isNative;
                    pagesText    = triageResult.pagesText;
                    strategy     = triageResult.strategy;
                    _logger.LogInformation(
                        "[OCR] Triage completed for '{FileName}': Strategy={Strategy}, IsNative={IsNative}, PageTextChunks={Chunks}.",
                        fileName, strategy, tryTextFirst, pagesText.Count);
                }
            }
            else
            {
                _logger.LogInformation(
                    "[OCR] Non-PDF file '{FileName}' (extension='{Extension}'): skipping triage. Will use Vision path with invoice pipeline. SourceContext='{SourceContext}'.",
                    fileName, extension, sourceContext ?? "(none)");
                if (contextForcesContract)
                {
                    _logger.LogWarning(
                        "[OCR] sourceContext='CONTRACT' but file '{FileName}' is not a PDF (extension='{Extension}'). " +
                        "Non-PDF contract extraction is not fully supported: triage cannot run, contract keyword classification skipped. " +
                        "Forcing Contract strategy anyway — quality may be degraded.",
                        fileName, extension);
                    strategy = DocumentStrategy.Contract;
                }
            }

            safeMemoryStream.Position = 0; // Reset for downstream

            var model = string.IsNullOrWhiteSpace(settings.Model) ? DefaultModel : settings.Model;
            _logger.LogInformation(
                "[OCR] Final routing decision for '{FileName}': Strategy={Strategy}, Model={Model}, TryTextFirst={TryTextFirst}",
                fileName, strategy, model, tryTextFirst);

            if (strategy == DocumentStrategy.Contract)
            {
                _logger.LogInformation("[OCR] Dispatching '{FileName}' to ProcessContractAsync.", fileName);
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

            // If the request is from the contract OCR trigger, the caller already knows
            // the document type — return early so the caller can force Contract strategy.
            bool contextForcesContract = !string.IsNullOrWhiteSpace(sourceContext) &&
                sourceContext.Equals("CONTRACT", StringComparison.OrdinalIgnoreCase);

            if (contextForcesInvoice)
            {
                _logger.LogInformation(
                    "[OCR-Triage] sourceContext='{Context}' forces Invoice strategy for '{FileName}'. Skipping contract classification.",
                    sourceContext, fileName);
            }
            else if (contextForcesContract)
            {
                // Do NOT classify here — the caller in ExtractAsync will override strategy.
                // We only run triage to detect native text for the TextFirst path.
                _logger.LogInformation(
                    "[OCR-Triage] sourceContext='CONTRACT' for '{FileName}': running text detection only, skipping keyword classification. IsNative={IsNative}, ExtractedTextLength={Len}.",
                    fileName, isNative, triageText.Length);
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
- UNIT PRICE (Einzelpreis/Stückpreis/Pr. Unitário): Capture the full numerical value. IGNORE ALL THOUSAND SEPARATORS (spaces, dots, commas). Convert to a pure JSON number using a period for decimals (e.g. '15 358,00' or '15.358,00' MUST become 15358.00). Ensure NO leading digits are lost before spaces.
- TOTAL AMOUNTS (Valor/Net/Gross/Grand Total): Follow the exact same formatting rules as UNIT PRICE. Ensure ALL spaces and thousand separators are removed (e.g. '36 552,96' MUST become 36552.96).

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
      CRITICAL: If no IVA/Taxa column exists for line items, or IVA is only shown in the document summary/totals and NOT per item, set taxRate=null (NOT 0).
      Only set taxRate=0 when the document EXPLICITLY shows 0% IVA (Isento) for that specific item.
    * If no discount column or value exists for a line, set discountPercent=0 and discountAmount=0.
  SELF-VALIDATION: totalPrice should equal (quantity × unitPrice) - discountAmount. 
    If this does not match the printed 'Valor' column, re-examine which column is discount vs tax.

- LINE TOTAL (Ges.preis/Valor): totalPrice = (quantity × unitPrice) - discountAmount. If discount=100%, totalPrice = 0.
- HEADER totalAmount = the Zwischensumme/Subtotal (net total after all line discounts, before tax).
- HEADER grandTotal = the FINAL total the buyer must pay, INCLUDING all taxes (IVA). This is the 'Total' or 'Total (AKZ)' or 'TOTAL DOCUMENTO' line on the document. If there is no separate grand total, set grandTotal = totalAmount.
- HEADER discountAmount = the GLOBAL / COMMERCIAL discount applied to the ENTIRE INVOICE (NOT per-item).
  Look for labels like 'Desconto Comercial', 'Desconto Valor s/Serviços', 'Desconto Global', 'Gesamtrabatt', 'Trade Discount' in the invoice SUMMARY / FOOTER section (e.g. 'Quadro Resumo', 'Resumo de Impostos').
  This is a POSITIVE number representing the absolute discount value (e.g. if the summary shows 'Desconto Comercial -823 261,00', set discountAmount = 823261.00).
  If no global/commercial discount exists on the document, set discountAmount = 0.
  NOTE: Do NOT confuse this with per-item discounts in the 'Desc.' column — those are already captured per line item.

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
    ""grandTotal"": number (Final total INCLUDING tax/IVA - this is what the buyer actually pays),
    ""discountAmount"": number (Global/commercial invoice-level discount, 0 if none — see rules above)
  },
  ""items"": [
    {
      ""description"": ""string"",
      ""quantity"": number,
      ""unit"": ""string"",
      ""unitPrice"": number,
      ""discountPercent"": number (from Desc./Desconto column, e.g. 7 for 7%, 25 for 25%, 0 if none),
      ""discountAmount"": number (TOTAL line discount = qty × unitPrice × discountPercent/100),
      ""taxRate"": number or null (from IVA/Taxa column — null if not explicitly per item),
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
                    TaxRate = item.TryGetProperty("taxRate", out var tr) ? (tr.ValueKind == JsonValueKind.Number ? tr.GetDecimal() : (decimal?)null) : null,
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
            _logger.LogInformation(
                "[OCR-Contract] TextFirst path chosen for '{FileName}': {PageCount} page(s) of native text extracted. Building chunks.",
                fileName, pagesText.Count);

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

            _logger.LogInformation(
                "[OCR-Contract] '{FileName}' divided into {ChunkCount} chunk(s) for TextFirst extraction.",
                fileName, chunkCount);

            for (int i = 0; i < chunks.Count; i++)
            {
                _logger.LogInformation(
                    "[OCR-Contract] Processing chunk {Current}/{Total} for '{FileName}' (~{Chars} chars).",
                    i + 1, chunkCount, fileName, chunks[i].Length);

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

                promptTokens += chunkResult.Metadata?.PromptTokens ?? 0;
                completionTokens += chunkResult.Metadata?.CompletionTokens ?? 0;

                if (chunkResult.Success && chunkResult.Contract != null)
                {
                    _logger.LogInformation(
                        "[OCR-Contract] Chunk {Current}/{Total} succeeded for '{FileName}'. Merging results.",
                        i + 1, chunkCount, fileName);
                    MergeContractResults(result.Contract, chunkResult.Contract, ref conflictsDetected);
                }
                else
                {
                    _logger.LogWarning(
                        "[OCR-Contract] Chunk {Current}/{Total} for '{FileName}' did not produce usable contract data. Success={Success}, ContractNull={IsNull}.",
                        i + 1, chunkCount, fileName, chunkResult.Success, chunkResult.Contract == null);
                }
            }

            // Diagnostic: log what was actually extracted after all chunks
            var c = result.Contract;
            _logger.LogInformation(
                "[OCR-Contract] TextFirst complete for '{FileName}'. Extracted — EffectiveDate={EffDate}, EndDate={EndDate}, Value={Value}, Currency={Cur}, Supplier={Sup}, Parties={Parties}. IsPartial={IsPartial}, ConflictsDetected={Conflicts}.",
                fileName,
                c?.EffectiveDate ?? "(null)", c?.EndDate ?? "(null)",
                c?.TotalContractValue ?? "(null)", c?.CurrencyRaw ?? "(null)",
                c?.SupplierName ?? "(null)", c?.Parties ?? "(null)",
                IsContractPartial(c), conflictsDetected);

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

        _logger.LogInformation(
            "[OCR-Contract] Vision fallback path chosen for '{FileName}'. Reason: tryTextFirst={TryTextFirst}, pagesText.Count={PageCount}. This is expected for scanned/image-only PDFs.",
            fileName, tryTextFirst, pagesText.Count);
        
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
        // Scalar string fields — inline merge (C# properties cannot be passed as ref)
        static string? Ms(string? t, string? s, ref bool c) {
            if (string.IsNullOrWhiteSpace(s)) return t;
            if (string.IsNullOrWhiteSpace(t)) return s;
            if (t != s) c = true;
            return t;
        }

        target.DocumentType              = Ms(target.DocumentType, source.DocumentType, ref conflicts);
        target.ExternalContractReference = Ms(target.ExternalContractReference, source.ExternalContractReference, ref conflicts);
        target.SupplierName              = Ms(target.SupplierName, source.SupplierName, ref conflicts);
        target.SupplierTaxId             = Ms(target.SupplierTaxId, source.SupplierTaxId, ref conflicts);
        target.SignatoryNames             = Ms(target.SignatoryNames, source.SignatoryNames, ref conflicts);

        // Helper: take higher-confidence value; flag conflict if both populated and differ
        static void MergeConfidenceField(ref string? targetVal, ref decimal? targetConf,
            string? sourceVal, decimal? sourceConf, ref bool conflictsFlag)
        {
            if (string.IsNullOrWhiteSpace(sourceVal)) return;
            if (string.IsNullOrWhiteSpace(targetVal)) { targetVal = sourceVal; targetConf = sourceConf; return; }
            if (targetVal != sourceVal)
            {
                conflictsFlag = true;
                if ((sourceConf ?? 0) > (targetConf ?? 0)) { targetVal = sourceVal; targetConf = sourceConf; }
            }
        }

        // Parties: accumulate rather than conflict (multiple chunks may name more parties)

        if (!string.IsNullOrWhiteSpace(source.Parties))
        {
            if (string.IsNullOrWhiteSpace(target.Parties)) target.Parties = source.Parties;
            else if (!target.Parties.Contains(source.Parties)) target.Parties += "; " + source.Parties;
        }

        string? ed = target.EffectiveDate; decimal? edc = target.EffectiveDateConfidence;
        MergeConfidenceField(ref ed, ref edc, source.EffectiveDate, source.EffectiveDateConfidence, ref conflicts);
        target.EffectiveDate = ed; target.EffectiveDateConfidence = edc;

        string? end = target.EndDate; decimal? endc = target.EndDateConfidence;
        MergeConfidenceField(ref end, ref endc, source.EndDate, source.EndDateConfidence, ref conflicts);
        target.EndDate = end; target.EndDateConfidence = endc;

        string? sd = target.SignatureDate; decimal? sdc = target.SignatureDateConfidence;
        MergeConfidenceField(ref sd, ref sdc, source.SignatureDate, source.SignatureDateConfidence, ref conflicts);
        target.SignatureDate = sd; target.SignatureDateConfidence = sdc;

        string? tv = target.TotalContractValue; decimal? tvc = target.TotalContractValueConfidence;
        MergeConfidenceField(ref tv, ref tvc, source.TotalContractValue, source.TotalContractValueConfidence, ref conflicts);
        target.TotalContractValue = tv; target.TotalContractValueConfidence = tvc;

        string? cr = target.CurrencyRaw; decimal? crc = target.CurrencyConfidence;
        MergeConfidenceField(ref cr, ref crc, source.CurrencyRaw, source.CurrencyConfidence, ref conflicts);
        target.CurrencyRaw = cr; target.CurrencyConfidence = crc;

        string? gl = target.GoverningLaw; decimal? glc = target.GoverningLawConfidence;
        MergeConfidenceField(ref gl, ref glc, source.GoverningLaw, source.GoverningLawConfidence, ref conflicts);
        target.GoverningLaw = gl; target.GoverningLawConfidence = glc;

        // Payment terms and termination: take first found (reference data, no conflict logic)
        if (!string.IsNullOrWhiteSpace(source.PaymentTerms) && string.IsNullOrWhiteSpace(target.PaymentTerms))
            target.PaymentTerms = source.PaymentTerms;
        if (!string.IsNullOrWhiteSpace(source.TerminationClauses) && string.IsNullOrWhiteSpace(target.TerminationClauses))
            target.TerminationClauses = source.TerminationClauses;

        // Phase 1.1 — recurring amount: take higher-confidence value across chunks
        string? rma = target.RecurringMonthlyAmount; decimal? rmac = target.RecurringMonthlyAmountConfidence;
        MergeConfidenceField(ref rma, ref rmac, source.RecurringMonthlyAmount, source.RecurringMonthlyAmountConfidence, ref conflicts);
        target.RecurringMonthlyAmount = rma; target.RecurringMonthlyAmountConfidence = rmac;

        // Duration: take first found (a contract has one term)
        if (target.ContractDurationMonths == null && source.ContractDurationMonths != null)
        {
            target.ContractDurationMonths = source.ContractDurationMonths;
            target.ContractDurationMonthsConfidence = source.ContractDurationMonthsConfidence;
        }

        // Written amount: take first found
        if (string.IsNullOrWhiteSpace(target.WrittenAmountText) && !string.IsNullOrWhiteSpace(source.WrittenAmountText))
            target.WrittenAmountText = source.WrittenAmountText;

        // Inconsistency: if ANY chunk flagged it, propagate
        if (source.WrittenAmountInconsistencyDetected)
            target.WrittenAmountInconsistencyDetected = true;
    }

    private bool IsContractPartial(ExtractionContractDto? c)
    {
        if (c == null) return true;
        // A contract result is considered partial if none of the key date/value fields were extracted
        return string.IsNullOrWhiteSpace(c.EffectiveDate)
            && string.IsNullOrWhiteSpace(c.EndDate)
            && string.IsNullOrWhiteSpace(c.TotalContractValue);
    }

    private string GetContractSystemPrompt()
    {
        return @"You are a specialized legal contract metadata extractor for an Angolan enterprise portal.
Your task: extract structured metadata from the provided contract document.

IMPORTANT INSTRUCTIONS:
- Output ONLY a JSON object. No markdown. No commentary. No triple backticks.
- If a field is not found in the current chunk, set it to null.
- Do NOT hallucinate. Only extract what is explicitly present in the document.
- For every extracted field value, provide a confidence score (0.0 to 1.0).
- Dates must be output in ISO-8601 format (YYYY-MM-DD) when recognisable.
  Common Portuguese date patterns: '10 de Janeiro de 2024', '10/01/2024', 'jan. 2024'.
  If ambiguous (e.g. 05/06/2024 could be May 6 or June 5), choose most likely from context.
- The contract is likely in Portuguese, English, or German.
- AlplaPLASTICOS or AlplaSOPRO is almost always one of the parties — do not extract them as the supplier.

Output this exact JSON structure:
{
  ""contract"": {
    ""documentType"": ""string or null"",
    ""externalContractReference"": ""external contract/order number from counterparty, or null"",
    ""supplierName"": ""name of supplier/counterparty (not AlplaPLASTICOS/AlplaSOPRO), or null"",
    ""supplierTaxId"": ""tax ID / NIF / CNPJ / VAT of the supplier, or null"",
    ""parties"": ""all party names semicolon-separated, or null"",
    ""signatoryNames"": ""names near signature blocks semicolon-separated, or null"",
    ""effectiveDate"": ""YYYY-MM-DD or null"",
    ""effectiveDateConfidence"": 0.0,
    ""endDate"": ""YYYY-MM-DD or null"",
    ""endDateConfidence"": 0.0,
    ""signatureDate"": ""YYYY-MM-DD or null"",
    ""signatureDateConfidence"": 0.0,
    ""totalContractValue"": ""raw text including currency symbol if present, e.g. 'USD 150,000.00', or null"",
    ""totalContractValueConfidence"": 0.0,
    ""currencyRaw"": ""currency code or symbol as written in document, e.g. 'USD', 'Kwanza', '$', or null"",
    ""currencyConfidence"": 0.0,
    ""governingLaw"": ""governing law clause text (max 200 chars), or null"",
    ""governingLawConfidence"": 0.0,
    ""paymentTerms"": ""payment terms text, or null"",
    ""terminationClauses"": ""brief summary of termination conditions (max 300 chars), or null"",
    ""contractScope"": ""1–2 sentence operational summary of the contract object or scope-of-services clause. Focus on: what service or work is provided, by whom, and where if stated. Exclude payment amounts, termination conditions, liability clauses, and legal recitals. Max 300 characters. null if no clear object/scope clause is found."",


    ""recurringMonthlyAmount"": ""raw text of per-month amount if the contract is priced monthly (e.g. '770,330.00 Kz'), or null if no monthly pricing clause exists"",
    ""recurringMonthlyAmountConfidence"": 0.0,
    ""contractDurationMonths"": null,
    ""contractDurationMonthsConfidence"": 0.0,
    ""writtenAmountText"": ""the financial amount written in words if it appears in the document, e.g. 'nine hundred and eighty thousand kwanzas', or null"",
    ""writtenAmountInconsistencyDetected"": false
  },
  ""qualityScore"": 0.0
}

Additional extraction rules for financial fields:
- recurringMonthlyAmount: only populate when the clause explicitly describes a MONTHLY or PERIODIC amount (keywords: 'monthly', 'per month', 'por mês', 'valor mensal', 'mensalmente', 'cada mês', 'cada mes'). Do NOT populate if the amount is the global contract total.
- contractDurationMonths: populate when the document states a contract term in months (e.g. 'vigência de 12 meses', 'term of 12 months', 'válido por 12 meses'). Output as an integer, not a string.
- writtenAmountText: populate only when a financial amount is explicitly spelled out in words in the same clause as a numeric amount.
- writtenAmountInconsistencyDetected: set to true ONLY when BOTH a numeric amount AND a written-word amount are present in the same clause AND they clearly do not match. Do NOT infer — only flag explicit contradictions you can see in the document text.
- contractScope: summarise the object clause or scope-of-services clause in 1–2 clear operational sentences. Focus on what service or work is provided, by whom, and where (if stated). Do NOT include payment amounts, termination conditions, liability clauses, or recitals. If you cannot find a clear object or scope clause, output null. Maximum 300 characters.

Confidence guidance:
  1.0 = exact explicit text in document
  0.85 = high confidence, minor ambiguity
  0.65 = moderate confidence, inferred from context
  below 0.50 = low confidence, likely incomplete";
    }

    private ExtractionResultDto MapContractFromJson(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rawResult = JsonSerializer.Deserialize<JsonElement>(json, options);

            // Detect if LLM returned an invoice-shaped JSON instead of a contract JSON.
            // This happens when the document was misclassified by triage and the invoice
            // prompt was used — the JSON will contain 'header' and 'items' keys but NOT 'contract'.
            bool hasContractKey  = rawResult.TryGetProperty("contract", out var co);
            bool hasHeaderKey    = rawResult.TryGetProperty("header", out _);
            bool hasItemsKey     = rawResult.TryGetProperty("items", out _);

            if (!hasContractKey && hasHeaderKey)
            {
                _logger.LogError(
                    "[OCR-Contract] MapContractFromJson received an INVOICE-shaped JSON (has 'header'/'items', no 'contract' key). " +
                    "This indicates the document was routed through the invoice pipeline instead of the contract pipeline. " +
                    "Likely cause: triage keyword classification failed — sourceContext='CONTRACT' override should have prevented this. " +
                    "Returning Success=false. Raw JSON (first 500 chars): {JsonSnippet}",
                    json.Length > 500 ? json[..500] : json);
                return new ExtractionResultDto
                {
                    Success = false,
                    ProviderName = Name,
                    QualityScore = rawResult.TryGetProperty("qualityScore", out var qs2) ? (decimal)qs2.GetDouble() : 0m
                };
            }

            var result = new ExtractionResultDto
            {
                Success = true,
                ProviderName = Name,
                QualityScore = rawResult.TryGetProperty("qualityScore", out var qs) ? (decimal)qs.GetDouble() : 0.5m,
                Contract = new ExtractionContractDto()
            };

            var contractObj = hasContractKey ? co : rawResult; // Fallback: flat object instead of nested

            static string? S(JsonElement el, string key)
                => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            static decimal? D(JsonElement el, string key)
                => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? (decimal?)v.GetDouble() : null;

            result.Contract.DocumentType              = S(contractObj, "documentType");
            result.Contract.ExternalContractReference = S(contractObj, "externalContractReference");
            result.Contract.SupplierName              = S(contractObj, "supplierName");
            result.Contract.SupplierTaxId             = S(contractObj, "supplierTaxId");
            result.Contract.Parties                   = S(contractObj, "parties");
            result.Contract.SignatoryNames             = S(contractObj, "signatoryNames");

            result.Contract.EffectiveDate             = S(contractObj, "effectiveDate");
            result.Contract.EffectiveDateConfidence   = D(contractObj, "effectiveDateConfidence");

            result.Contract.EndDate                   = S(contractObj, "endDate");
            result.Contract.EndDateConfidence         = D(contractObj, "endDateConfidence");

            result.Contract.SignatureDate             = S(contractObj, "signatureDate");
            result.Contract.SignatureDateConfidence   = D(contractObj, "signatureDateConfidence");

            result.Contract.TotalContractValue           = S(contractObj, "totalContractValue");
            result.Contract.TotalContractValueConfidence = D(contractObj, "totalContractValueConfidence");

            result.Contract.CurrencyRaw               = S(contractObj, "currencyRaw");
            result.Contract.CurrencyConfidence        = D(contractObj, "currencyConfidence");

            result.Contract.GoverningLaw              = S(contractObj, "governingLaw");
            result.Contract.GoverningLawConfidence    = D(contractObj, "governingLawConfidence");

            result.Contract.PaymentTerms              = S(contractObj, "paymentTerms");
            result.Contract.TerminationClauses        = S(contractObj, "terminationClauses");
            result.Contract.ContractScope             = S(contractObj, "contractScope");

            // ── Phase 1.1 financial rule fields ──────────────────────────
            result.Contract.RecurringMonthlyAmount           = S(contractObj, "recurringMonthlyAmount");
            result.Contract.RecurringMonthlyAmountConfidence = D(contractObj, "recurringMonthlyAmountConfidence");
            result.Contract.WrittenAmountText                = S(contractObj, "writtenAmountText");
            result.Contract.WrittenAmountInconsistencyDetected =
                contractObj.TryGetProperty("writtenAmountInconsistencyDetected", out var wai)
                && wai.ValueKind == JsonValueKind.True;

            // contractDurationMonths is an integer (not a string) in the prompt
            if (contractObj.TryGetProperty("contractDurationMonths", out var cdm)
                && cdm.ValueKind == JsonValueKind.Number
                && cdm.TryGetInt32(out var cdmInt))
            {
                result.Contract.ContractDurationMonths = cdmInt;
            }
            result.Contract.ContractDurationMonthsConfidence = D(contractObj, "contractDurationMonthsConfidence");

            // Diagnostic: check how many fields were actually populated
            var populatedFields = new[]
            {
                result.Contract.EffectiveDate, result.Contract.EndDate, result.Contract.SignatureDate,
                result.Contract.TotalContractValue, result.Contract.CurrencyRaw,
                result.Contract.SupplierName, result.Contract.Parties, result.Contract.GoverningLaw,
                result.Contract.PaymentTerms, result.Contract.TerminationClauses
            }.Count(f => !string.IsNullOrWhiteSpace(f));

            _logger.LogInformation(
                "[OCR-Contract] MapContractFromJson complete. {PopulatedCount}/10 key fields populated. " +
                "QualityScore={Quality}. HasContractKey={HasKey}, FlatFallback={Flat}.",
                populatedFields, result.QualityScore, hasContractKey, !hasContractKey);

            if (populatedFields == 0)
            {
                _logger.LogWarning(
                    "[OCR-Contract] MapContractFromJson produced zero populated fields for this chunk/call. " +
                    "The LLM may have returned an empty or unexpected JSON structure. JSON snippet: {JsonSnippet}",
                    json.Length > 300 ? json[..300] : json);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OCR-Contract] MapContractFromJson threw an exception — JSON parsing or mapping failed. " +
                "This means the LLM returned malformed JSON or an unexpected schema. JSON snippet: {JsonSnippet}",
                json.Length > 500 ? json[..500] : json);
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
