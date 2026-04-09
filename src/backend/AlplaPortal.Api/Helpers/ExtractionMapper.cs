using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.DTOs.Requests;

namespace AlplaPortal.Api.Helpers;

public static class ExtractionMapper
{
    /// <summary>
    /// Temporary mapper to preserve backward compatibility with the frontend's expected OCR response shape.
    /// This should be replaced when the frontend is updated to consume the provider-agnostic ExtractionResultDto.
    /// </summary>
    public static OcrExtractionResultDto MapToLegacyOcrResult(ExtractionResultDto internalResult)
    {
        var integrationDto = new OcrIntegrationDto
        {
            HeaderSuggestions = new OcrHeaderSuggestionsDto
            {
                SupplierName = new OcrValueDto<string> { Value = internalResult.Header?.SupplierName, Status = "recommended" },
                SupplierTaxId = new OcrValueDto<string> { Value = internalResult.Header?.SupplierTaxId, Status = "recommended" },
                BilledCompany = new OcrValueDto<string> { Value = internalResult.Header?.BilledCompanyName, Status = "recommended" },
                DocumentNumber = new OcrValueDto<string> { Value = internalResult.Header?.DocumentNumber, Status = "recommended" },
                Date = new OcrValueDto<string> { Value = internalResult.Header?.DocumentDate, Status = "recommended" },
                CurrencyCode = new OcrValueDto<string> { Value = internalResult.Header?.Currency, Status = "recommended" },
                TotalAmount = new OcrValueDto<decimal> { Value = internalResult.Header?.GrandTotal ?? internalResult.Header?.TotalAmount ?? 0, Status = "recommended" },
                DiscountAmount = new OcrValueDto<decimal> { Value = internalResult.Header?.DiscountAmount ?? 0, Status = "recommended" }
            },
            LineItemSuggestions = (internalResult.Items ?? new()).Select(item => new OcrLineItemSuggestionDto
            {
                Description = item.Description,
                Quantity = item.Quantity,
                Unit = item.Unit,
                UnitPrice = item.UnitPrice,
                DiscountAmount = item.DiscountAmount,
                DiscountPercent = item.DiscountPercent,
                TotalAmount = item.TotalPrice,
                TaxRate = item.TaxRate,
                Status = "suggested"
            }).ToList(),
            LineItemsRequireReview = true,
            ReviewRequired = true
        };

        if (internalResult.Contract != null)
        {
            integrationDto.ContractSuggestions = new OcrContractSuggestionsDto
            {
                DocumentType = new OcrValueDto<string> { Value = internalResult.Contract.DocumentType, Status = "suggested" },
                Parties = new OcrValueDto<string> { Value = internalResult.Contract.Parties, Status = "suggested" },
                EffectiveDate = new OcrValueDto<string> { Value = internalResult.Contract.EffectiveDate, Status = "suggested" },
                EndDate = new OcrValueDto<string> { Value = internalResult.Contract.EndDate, Status = "suggested" },
                GoverningLaw = new OcrValueDto<string> { Value = internalResult.Contract.GoverningLaw, Status = "suggested" },
                PaymentTerms = new OcrValueDto<string> { Value = internalResult.Contract.PaymentTerms, Status = "suggested" },
                TerminationClauses = new OcrValueDto<string> { Value = internalResult.Contract.TerminationClauses, Status = "suggested" }
            };
        }

        return new OcrExtractionResultDto
        {
            Success = internalResult.Success,
            Status = new OcrStatusDto
            {
                Code = internalResult.Success ? "ok" : "ERROR",
                QualityScore = internalResult.QualityScore
            },
            Integration = integrationDto,
            Metadata = new Dictionary<string, object>
            {
                { "promptTokens", internalResult.Metadata?.PromptTokens ?? 0 },
                { "completionTokens", internalResult.Metadata?.CompletionTokens ?? 0 },
                { "totalTokens", internalResult.Metadata?.TotalTokens ?? 0 },
                { "pagesProcessed", internalResult.Metadata?.PagesProcessed ?? 0 },
                { "processingDpi", internalResult.Metadata?.ProcessingDpi ?? 0 },
                { "processingFormat", internalResult.Metadata?.ProcessingFormat ?? "unknown" },
                { "provider", internalResult.ProviderName ?? "OPENAI" },
                { "chunkCount", internalResult.Metadata?.ChunkCount ?? 0 },
                { "isPartial", internalResult.Metadata?.IsPartial ?? false },
                { "conflictsDetected", internalResult.Metadata?.ConflictsDetected ?? false }
            }
        };
    }
}
