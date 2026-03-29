using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Requests;

public class OcrStatusDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("qualityScore")]
    public decimal QualityScore { get; set; }
}

public class OcrValueDto<T>
{
    [JsonPropertyName("value")]
    public T? Value { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class OcrHeaderSuggestionsDto
{
    [JsonPropertyName("supplierName")]
    public OcrValueDto<string>? SupplierName { get; set; }

    [JsonPropertyName("supplierTaxId")]
    public OcrValueDto<string>? SupplierTaxId { get; set; }

    [JsonPropertyName("documentNumber")]
    public OcrValueDto<string>? DocumentNumber { get; set; }

    [JsonPropertyName("documentDate")]
    public OcrValueDto<string>? Date { get; set; }

    [JsonPropertyName("currency")]
    public OcrValueDto<string>? CurrencyCode { get; set; }

    [JsonPropertyName("grandTotal")]
    public OcrValueDto<decimal>? TotalAmount { get; set; }

    [JsonPropertyName("discountAmount")]
    public OcrValueDto<decimal>? DiscountAmount { get; set; }
}

public class OcrLineItemSuggestionDto
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("quantity")]
    public decimal? Quantity { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("unitPrice")]
    public decimal? UnitPrice { get; set; }

    [JsonPropertyName("totalPrice")] // Changed from TotalAmount to match JSON
    public decimal? TotalAmount { get; set; }

    [JsonPropertyName("taxRate")]
    public decimal? TaxRate { get; set; }

    [JsonPropertyName("confidence")] // Changed from Status to match JSON
    public string? Status { get; set; } 
}

public class OcrIntegrationDto
{
    [JsonPropertyName("headerSuggestions")]
    public OcrHeaderSuggestionsDto? HeaderSuggestions { get; set; }

    [JsonPropertyName("lineItemSuggestions")]
    public List<OcrLineItemSuggestionDto>? LineItemSuggestions { get; set; }

    [JsonPropertyName("lineItemsRequireReview")]
    public bool LineItemsRequireReview { get; set; }

    [JsonPropertyName("reviewRequired")]
    public bool ReviewRequired { get; set; }

    [JsonPropertyName("recommendedAutofillFields")]
    public List<string>? RecommendedAutofillFields { get; set; }
}

public class OcrExtractionResultDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("status")]
    public OcrStatusDto Status { get; set; } = new();

    [JsonPropertyName("requiredFieldsMissing")]
    public List<string> RequiredFieldsMissing { get; set; } = new();

    [JsonPropertyName("integration")]
    public OcrIntegrationDto Integration { get; set; } = new();
}
