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
        return new OcrExtractionResultDto
        {
            Success = internalResult.Success,
            Status = new OcrStatusDto
            {
                Code = internalResult.Success ? "ok" : "ERROR",
                QualityScore = internalResult.QualityScore
            },
            Integration = new OcrIntegrationDto
            {
                HeaderSuggestions = new OcrHeaderSuggestionsDto
                {
                    SupplierName = new OcrValueDto<string> { Value = internalResult.Header?.SupplierName, Status = "recommended" },
                    SupplierTaxId = new OcrValueDto<string> { Value = internalResult.Header?.SupplierTaxId, Status = "recommended" },
                    DocumentNumber = new OcrValueDto<string> { Value = internalResult.Header?.DocumentNumber, Status = "recommended" },
                    Date = new OcrValueDto<string> { Value = internalResult.Header?.DocumentDate, Status = "recommended" },
                    CurrencyCode = new OcrValueDto<string> { Value = internalResult.Header?.Currency, Status = "recommended" },
                    TotalAmount = new OcrValueDto<decimal> { Value = internalResult.Header?.TotalAmount ?? 0, Status = "recommended" },
                    DiscountAmount = new OcrValueDto<decimal> { Value = internalResult.Header?.DiscountAmount ?? 0, Status = "recommended" }
                },
                LineItemSuggestions = (internalResult.Items ?? new()).Select(item => new OcrLineItemSuggestionDto
                {
                    Description = item.Description,
                    Quantity = item.Quantity,
                    Unit = item.Unit,
                    UnitPrice = item.UnitPrice,
                    TotalAmount = item.TotalPrice,
                    TaxRate = item.TaxRate,
                    Status = "suggested"
                }).ToList(),
                LineItemsRequireReview = true,
                ReviewRequired = true
            }
        };
    }
}
