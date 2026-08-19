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

    [JsonPropertyName("billedCompany")]
    public OcrValueDto<string>? BilledCompany { get; set; }

    /// <summary>The customer's fiscal number, kept distinct from the supplier's.</summary>
    public OcrValueDto<string>? BilledCompanyTaxId { get; set; }

    [JsonPropertyName("documentNumber")]
    public OcrValueDto<string>? DocumentNumber { get; set; }

    [JsonPropertyName("documentDate")]
    public OcrValueDto<string>? Date { get; set; }

    [JsonPropertyName("dueDate")]
    public OcrValueDto<string>? DueDate { get; set; }

    [JsonPropertyName("currency")]
    public OcrValueDto<string>? CurrencyCode { get; set; }

    [JsonPropertyName("grandTotal")]
    public OcrValueDto<decimal>? TotalAmount { get; set; }

    /// <summary>
    /// The document's DECLARED net subtotal (after discounts, before tax), forwarded separately
    /// (v2.229.10 monetary reconciliation) so the declared value is never lost to line-derived
    /// reconstruction. Null when the document did not state one.
    /// </summary>
    [JsonPropertyName("netAmount")]
    public OcrValueDto<decimal>? NetAmount { get; set; }

    /// <summary>
    /// The document's tax amount: grand total − net subtotal, derived only when both are present
    /// and numerically sane. Null otherwise — never a guess.
    /// </summary>
    [JsonPropertyName("taxAmount")]
    public OcrValueDto<decimal>? TaxAmount { get; set; }

    [JsonPropertyName("discountAmount")]
    public OcrValueDto<decimal>? DiscountAmount { get; set; }

    [JsonPropertyName("vendorAddress")]
    public OcrValueDto<string>? VendorAddress { get; set; }

    [JsonPropertyName("vendorContactName")]
    public OcrValueDto<string>? VendorContactName { get; set; }

    [JsonPropertyName("vendorContactEmail")]
    public OcrValueDto<string>? VendorContactEmail { get; set; }

    [JsonPropertyName("vendorContactPhone")]
    public OcrValueDto<string>? VendorContactPhone { get; set; }

    [JsonPropertyName("vendorIban")]
    public OcrValueDto<string>? VendorIban { get; set; }

    [JsonPropertyName("vendorBankAccount")]
    public OcrValueDto<string>? VendorBankAccount { get; set; }

    [JsonPropertyName("vendorSwift")]
    public OcrValueDto<string>? VendorSwift { get; set; }

    [JsonPropertyName("vendorPaymentTerms")]
    public OcrValueDto<string>? VendorPaymentTerms { get; set; }

    [JsonPropertyName("paymentCondition")]
    public OcrValueDto<string>? PaymentCondition { get; set; }

    [JsonPropertyName("paymentConditionRawText")]
    public OcrValueDto<string>? PaymentConditionRawText { get; set; }

    [JsonPropertyName("paymentConditionAdvancePercent")]
    public OcrValueDto<decimal?>? PaymentConditionAdvancePercent { get; set; }

    // ── Document classification (Release 2 corrected) ──
    /// <summary>
    /// What the extraction believes the document IS, with the evidence behind it. Surfaced to the
    /// user for confirmation; the UI must never write it into the classification field on their
    /// behalf. Null when the extraction could not identify the document.
    /// </summary>
    public OcrDocumentClassificationDto? DocumentClassification { get; set; }
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

    [JsonPropertyName("discountAmount")]
    public decimal? DiscountAmount { get; set; }

    [JsonPropertyName("discountPercent")]
    public decimal? DiscountPercent { get; set; }

    [JsonPropertyName("totalPrice")] // Changed from TotalAmount to match JSON
    public decimal? TotalAmount { get; set; }

    [JsonPropertyName("taxRate")]
    public decimal? TaxRate { get; set; }

    [JsonPropertyName("confidence")] // Changed from Status to match JSON
    public string? Status { get; set; } 
}

public class OcrContractSuggestionsDto
{
    [JsonPropertyName("documentType")]
    public OcrValueDto<string>? DocumentType { get; set; }

    [JsonPropertyName("parties")]
    public OcrValueDto<string>? Parties { get; set; }

    [JsonPropertyName("effectiveDate")]
    public OcrValueDto<string>? EffectiveDate { get; set; }

    [JsonPropertyName("endDate")]
    public OcrValueDto<string>? EndDate { get; set; }

    [JsonPropertyName("governingLaw")]
    public OcrValueDto<string>? GoverningLaw { get; set; }

    [JsonPropertyName("paymentTerms")]
    public OcrValueDto<string>? PaymentTerms { get; set; }

    [JsonPropertyName("terminationClauses")]
    public OcrValueDto<string>? TerminationClauses { get; set; }
}

public class OcrIntegrationDto
{
    [JsonPropertyName("headerSuggestions")]
    public OcrHeaderSuggestionsDto? HeaderSuggestions { get; set; }

    [JsonPropertyName("lineItemSuggestions")]
    public List<OcrLineItemSuggestionDto>? LineItemSuggestions { get; set; }
    
    [JsonPropertyName("contractSuggestions")]
    public OcrContractSuggestionsDto? ContractSuggestions { get; set; }

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

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// The extraction's opinion about the document's identity, with the evidence behind it.
///
/// <para>Deliberately a PROPOSAL and nothing more. It is shown to the user so they can confirm or
/// correct it, and is never applied to the classification field automatically — the observed defect
/// this design corrects was an FT invoice silently accepted as a Pró-forma because nothing in the
/// system had an opinion to disagree with.</para>
/// </summary>
public class OcrDocumentClassificationDto
{
    /// <summary>ESTIMATE, PROFORMA, ADVANCE_INVOICE, INVOICE, INVOICE_RECEIPT or OTHER.</summary>
    public string? SuggestedType { get; set; }

    /// <summary>0.0–1.0. A prefix-only match is capped at 0.50 by the extraction prompt.</summary>
    public decimal? Confidence { get; set; }

    /// <summary>The document heading read verbatim — the strongest single piece of evidence.</summary>
    public string? TitleFound { get; set; }

    public List<string> SupportingEvidence { get; set; } = new();
    public List<string> ConflictingEvidence { get; set; } = new();
    public List<string> FiscalMarkers { get; set; } = new();
    public List<string> NonFiscalMarkers { get; set; } = new();

    /// <summary>
    /// True when the evidence indicates a fiscal document. Used to raise a selection of a
    /// non-fiscal type to a high-risk conflict regardless of the numeric confidence.
    /// </summary>
    public bool IndicatesFiscalDocument { get; set; }

    /// <summary>
    /// The suggestion came from the Portal's own weak heuristics (document-number prefix, filename)
    /// because the extraction provider returned no structured classification — not from reading the
    /// document. The UI labels it accordingly so it is never mistaken for a verified reading.
    /// </summary>
    public bool IsFallback { get; set; }
}
