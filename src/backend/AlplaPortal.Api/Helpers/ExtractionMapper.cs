using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;

namespace AlplaPortal.Api.Helpers;

public static class ExtractionMapper
{
    /// <summary>
    /// Temporary mapper to preserve backward compatibility with the frontend's expected OCR response shape.
    /// This should be replaced when the frontend is updated to consume the provider-agnostic ExtractionResultDto.
    /// </summary>
    public static OcrExtractionResultDto MapToLegacyOcrResult(
        ExtractionResultDto internalResult, string? fileName = null)
    {
        // Deterministic Primavera PO identification (v2.229.12 groundwork), parsed once.
        var poReference = PrimaveraPoReference.TryParse(internalResult.Header?.DocumentNumber);

        var integrationDto = new OcrIntegrationDto
        {
            HeaderSuggestions = new OcrHeaderSuggestionsDto
            {
                SupplierName = new OcrValueDto<string> { Value = internalResult.Header?.SupplierName, Status = "recommended" },
                SupplierTaxId = new OcrValueDto<string> { Value = internalResult.Header?.SupplierTaxId, Status = "recommended" },
                BilledCompany = new OcrValueDto<string> { Value = internalResult.Header?.BilledCompanyName, Status = "recommended" },
                BilledCompanyTaxId = new OcrValueDto<string> { Value = internalResult.Header?.BilledCompanyTaxId, Status = "recommended" },
                DocumentNumber = new OcrValueDto<string> { Value = internalResult.Header?.DocumentNumber, Status = "recommended" },
                Date = new OcrValueDto<string> { Value = internalResult.Header?.DocumentDate, Status = "recommended" },
                DueDate = new OcrValueDto<string> { Value = internalResult.Header?.DueDate, Status = "recommended" },
                CurrencyCode = new OcrValueDto<string> { Value = internalResult.Header?.Currency, Status = "recommended" },
                TotalAmount = new OcrValueDto<decimal> { Value = internalResult.Header?.GrandTotal ?? internalResult.Header?.TotalAmount ?? 0, Status = "recommended" },
                // v2.229.10 monetary reconciliation: the DECLARED subtotal (provider "totalAmount"
                // = net after discounts, before tax) travels separately from the grand total, so a
                // supplier-stated net is never lost to line-derived reconstruction downstream.
                NetAmount = internalResult.Header?.TotalAmount is decimal declaredNet && declaredNet > 0
                    ? new OcrValueDto<decimal> { Value = declaredNet, Status = "recommended" }
                    : null,
                // Tax is derived (grand − net), only when both are present and the difference is
                // non-negative — the provider has no explicit document-level tax field.
                TaxAmount = internalResult.Header?.GrandTotal is decimal declaredGrand
                            && internalResult.Header?.TotalAmount is decimal declaredSub
                            && declaredSub > 0 && declaredGrand >= declaredSub
                    ? new OcrValueDto<decimal> { Value = declaredGrand - declaredSub, Status = "recommended" }
                    : null,
                DiscountAmount = new OcrValueDto<decimal> { Value = internalResult.Header?.DiscountAmount ?? 0, Status = "recommended" },
                // The ECF / ECF10 / ECF11 grammar is parsed server-side over the extracted
                // document number, so consumers receive a POSITIVELY identified reference —
                // never a bare numeric field that might be the supplier's NIF.
                PurchaseOrderReference = poReference != null
                    ? new OcrValueDto<string> { Value = poReference.Display, Status = "recommended" }
                    : null,
                PurchaseOrderReferenceCanonical = poReference != null
                    ? new OcrValueDto<string> { Value = poReference.Canonical, Status = "recommended" }
                    : null,
                PurchaseOrderFamily = poReference != null
                    ? new OcrValueDto<string> { Value = poReference.Family, Status = "recommended" }
                    : null,
                PaymentCondition = !string.IsNullOrWhiteSpace(internalResult.Header?.PaymentConditionType) 
                    ? new OcrValueDto<string> { Value = internalResult.Header.PaymentConditionType, Status = internalResult.Header.PaymentConditionConfidence >= 0.7m ? "recommended" : "suggested" }
                    : null,
                PaymentConditionRawText = !string.IsNullOrWhiteSpace(internalResult.Header?.PaymentConditionRawText)
                    ? new OcrValueDto<string> { Value = internalResult.Header.PaymentConditionRawText, Status = "informational" }
                    : null,
                PaymentConditionAdvancePercent = internalResult.Header?.PaymentConditionAdvancePercent.HasValue == true
                    ? new OcrValueDto<decimal?> { Value = internalResult.Header.PaymentConditionAdvancePercent, Status = "suggested" }
                    : null,
                VendorAddress = !string.IsNullOrWhiteSpace(internalResult.Header?.VendorAddress) ? new OcrValueDto<string> { Value = internalResult.Header.VendorAddress, Status = "suggested" } : null,
                VendorContactName = !string.IsNullOrWhiteSpace(internalResult.Header?.VendorContactName) ? new OcrValueDto<string> { Value = internalResult.Header.VendorContactName, Status = "suggested" } : null,
                VendorContactEmail = !string.IsNullOrWhiteSpace(internalResult.Header?.VendorContactEmail) ? new OcrValueDto<string> { Value = internalResult.Header.VendorContactEmail, Status = "suggested" } : null,
                VendorContactPhone = !string.IsNullOrWhiteSpace(internalResult.Header?.VendorContactPhone) ? new OcrValueDto<string> { Value = internalResult.Header.VendorContactPhone, Status = "suggested" } : null,
                VendorIban = !string.IsNullOrWhiteSpace(internalResult.Header?.VendorIban) ? new OcrValueDto<string> { Value = internalResult.Header.VendorIban, Status = "suggested" } : null,
                VendorBankAccount = !string.IsNullOrWhiteSpace(internalResult.Header?.VendorBankAccount) ? new OcrValueDto<string> { Value = internalResult.Header.VendorBankAccount, Status = "suggested" } : null,
                VendorSwift = !string.IsNullOrWhiteSpace(internalResult.Header?.VendorSwift) ? new OcrValueDto<string> { Value = internalResult.Header.VendorSwift, Status = "suggested" } : null,
                VendorPaymentTerms = !string.IsNullOrWhiteSpace(internalResult.Header?.VendorPaymentTerms) ? new OcrValueDto<string> { Value = internalResult.Header.VendorPaymentTerms, Status = "suggested" } : null,

                // Document classification (Release 2 corrected). Passed through as a PROPOSAL with
                // its evidence so the UI can ask the user to confirm or correct it. Deliberately
                // never mapped into any field the user's selection is read from.
                DocumentClassification = BuildDocumentClassification(internalResult.Header, fileName)
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

    /// <summary>
    /// Projects the extraction's document-identity opinion, or null when it had none.
    ///
    /// <para>Returning null rather than an empty object matters: "the extraction could not identify
    /// this document" and "the extraction identified nothing suspicious" are different situations,
    /// and only the first should leave the UI without a suggestion to reconcile against.</para>
    /// </summary>
    private static OcrDocumentClassificationDto? BuildDocumentClassification(
        ExtractionHeaderDto? header, string? fileName)
    {
        if (header == null) return null;

        var hasOpinion = !string.IsNullOrWhiteSpace(header.DocumentClassificationType)
                         || !string.IsNullOrWhiteSpace(header.DocumentClassificationTitleFound)
                         || header.DocumentClassificationSupportingEvidence?.Count > 0
                         || header.DocumentClassificationConflictingEvidence?.Count > 0
                         || header.DocumentClassificationFiscalMarkers?.Count > 0
                         || header.DocumentClassificationNonFiscalMarkers?.Count > 0;

        // No structured block — older provider, truncated response, or a scan with little text.
        // Fall back to a weak, clearly-labelled suggestion rather than staying silent: with no
        // suggestion the UI has nothing to reconcile the user's choice against, which is exactly
        // how an FT invoice was accepted as a "Fatura Proforma" unchallenged.
        if (!hasOpinion)
        {
            var fallback = DocumentClassificationFallback.Derive(header.DocumentNumber, fileName);
            if (fallback.SuggestedType == null) return null;

            return new OcrDocumentClassificationDto
            {
                SuggestedType = fallback.SuggestedType,
                Confidence = fallback.Confidence,
                TitleFound = fallback.TitleFound,
                SupportingEvidence = fallback.SupportingEvidence,
                ConflictingEvidence = new List<string>(),
                FiscalMarkers = new List<string>(),
                NonFiscalMarkers = fallback.NonFiscalMarkers,
                IndicatesFiscalDocument = fallback.IndicatesFiscalDocument,
                IsFallback = true
            };
        }

        var fiscalMarkers = header.DocumentClassificationFiscalMarkers ?? new List<string>();
        var nonFiscalMarkers = header.DocumentClassificationNonFiscalMarkers ?? new List<string>();

        var suggested = header.DocumentClassificationType?.Trim().ToUpperInvariant();

        // A fiscal reading is claimed either by the suggested type itself or by certification
        // markers on the page — unless the document explicitly declares itself non-fiscal, which
        // always wins ("sem valor fiscal" is a statement by the issuer, not an inference).
        var indicatesFiscal =
            nonFiscalMarkers.Count == 0 &&
            (RequestConstants.SourceDocumentTypes.IsFiscal(suggested) || fiscalMarkers.Count > 0);

        return new OcrDocumentClassificationDto
        {
            SuggestedType = suggested,
            Confidence = header.DocumentClassificationConfidence,
            TitleFound = header.DocumentClassificationTitleFound,
            SupportingEvidence = header.DocumentClassificationSupportingEvidence ?? new List<string>(),
            ConflictingEvidence = header.DocumentClassificationConflictingEvidence ?? new List<string>(),
            FiscalMarkers = fiscalMarkers,
            NonFiscalMarkers = nonFiscalMarkers,
            IndicatesFiscalDocument = indicatesFiscal
        };
    }
}
