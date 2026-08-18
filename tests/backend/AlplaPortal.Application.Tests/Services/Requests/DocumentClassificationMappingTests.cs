using System.Collections.Generic;
using AlplaPortal.Api.Helpers;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Covers the API projection of the document-classification proposal — the layer between the
/// extraction provider and the UI.
///
/// <para>Two behaviours matter here: a structured classification from the provider must survive
/// intact, and its ABSENCE must produce a labelled fallback rather than silence. Silence was the
/// original defect: with no suggestion, the UI had nothing to disagree with.</para>
/// </summary>
public class DocumentClassificationMappingTests
{
    private static ExtractionResultDto Result(ExtractionHeaderDto header) =>
        new() { Success = true, Header = header, Items = new List<ExtractionLineItemDto>() };

    // ── Structured classification present ──

    [Fact]
    public void A_structured_classification_is_passed_through_intact()
    {
        var dto = ExtractionMapper.MapToLegacyOcrResult(Result(new ExtractionHeaderDto
        {
            DocumentNumber = "FT5926S42989N/52",
            DocumentClassificationType = "INVOICE",
            DocumentClassificationConfidence = 0.93m,
            DocumentClassificationTitleFound = "FACTURA",
            DocumentClassificationSupportingEvidence = new List<string> { "Título: FACTURA" },
            DocumentClassificationFiscalMarkers = new List<string> { "Processado por programa certificado" }
        }), "simotecnica.pdf");

        var c = dto.Integration.HeaderSuggestions!.DocumentClassification;

        Assert.NotNull(c);
        Assert.Equal("INVOICE", c!.SuggestedType);
        Assert.Equal(0.93m, c.Confidence);
        Assert.Equal("FACTURA", c.TitleFound);
        Assert.True(c.IndicatesFiscalDocument);
        Assert.False(c.IsFallback);   // it came from reading the document
    }

    [Fact]
    public void An_explicit_non_fiscal_marker_prevents_a_fiscal_reading()
    {
        var dto = ExtractionMapper.MapToLegacyOcrResult(Result(new ExtractionHeaderDto
        {
            DocumentClassificationType = "PROFORMA",
            DocumentClassificationNonFiscalMarkers = new List<string> { "sem valor fiscal" }
        }));

        Assert.False(dto.Integration.HeaderSuggestions!.DocumentClassification!.IndicatesFiscalDocument);
    }

    // ── Structured classification absent — the failed manual test ──

    [Fact]
    public void The_observed_FT_document_falls_back_to_a_labelled_invoice_suggestion()
    {
        // Exactly the manual-test case: provider returned no classification block.
        var dto = ExtractionMapper.MapToLegacyOcrResult(Result(new ExtractionHeaderDto
        {
            DocumentNumber = "FT5926S42989N/52"
        }), "simotecnica.pdf");

        var c = dto.Integration.HeaderSuggestions!.DocumentClassification;

        Assert.NotNull(c);
        Assert.Equal(RequestConstants.SourceDocumentTypes.Invoice, c!.SuggestedType);
        Assert.True(c.IsFallback);
        Assert.Equal(DocumentClassificationFallback.PrefixOnlyMaxConfidence, c.Confidence);
        Assert.Contains(c.SupportingEvidence, e => e.Contains("FT"));
        Assert.True(c.IndicatesFiscalDocument);
    }

    [Fact]
    public void A_fallback_never_reaches_high_confidence()
    {
        var dto = ExtractionMapper.MapToLegacyOcrResult(Result(new ExtractionHeaderDto
        {
            DocumentNumber = "FT2026/1"
        }), "FT-something.pdf");

        Assert.True(dto.Integration.HeaderSuggestions!.DocumentClassification!.Confidence
                    < DocumentClassificationFallback.HighConfidenceThreshold);
    }

    [Fact]
    public void Nothing_recognisable_still_yields_no_classification()
    {
        // A fabricated guess would be worse than none: the user would be arguing with noise.
        var dto = ExtractionMapper.MapToLegacyOcrResult(Result(new ExtractionHeaderDto
        {
            DocumentNumber = "12345/2026"
        }), "scan001.pdf");

        Assert.Null(dto.Integration.HeaderSuggestions!.DocumentClassification);
    }

    [Fact]
    public void A_null_header_yields_no_classification()
    {
        var dto = ExtractionMapper.MapToLegacyOcrResult(new ExtractionResultDto { Success = false });

        Assert.Null(dto.Integration.HeaderSuggestions!.DocumentClassification);
    }
}
