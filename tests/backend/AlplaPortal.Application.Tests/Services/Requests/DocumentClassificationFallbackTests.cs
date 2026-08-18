using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Regression tests for the fallback classifier.
///
/// <para><b>The defect this closes.</b> When the extraction provider returned no structured
/// classification block, the UI had nothing to reconcile the user's selection against — so an
/// <c>FT</c> invoice selected as "Fatura Pró-forma" produced no suggestion, no warning and no
/// justification prompt. Silence looked identical to agreement.</para>
///
/// <para>The fallback exists to raise a question, never to answer one. Every ceiling asserted here
/// keeps it below the high-confidence bar that would let it dominate a human decision.</para>
/// </summary>
public class DocumentClassificationFallbackTests
{
    // ── The exact document from the failed manual test ──

    [Fact]
    public void The_observed_FT_document_number_yields_a_low_confidence_invoice_suggestion()
    {
        var result = DocumentClassificationFallback.Derive("FT5926S42989N/52", fileName: null);

        Assert.Equal(RequestConstants.SourceDocumentTypes.Invoice, result.SuggestedType);
        Assert.Equal(DocumentClassificationFallback.PrefixOnlyMaxConfidence, result.Confidence);
        Assert.Contains(result.SupportingEvidence, e => e.Contains("FT"));
        Assert.True(result.IndicatesFiscalDocument);
    }

    [Fact]
    public void A_prefix_alone_never_reaches_high_confidence()
    {
        foreach (var number in new[] { "FT5926S42989N/52", "FR-2026/11", "PF 900", "ORC/1" })
        {
            var result = DocumentClassificationFallback.Derive(number, fileName: null);

            Assert.True(result.Confidence <= DocumentClassificationFallback.PrefixOnlyMaxConfidence, number);
            Assert.True(result.Confidence < DocumentClassificationFallback.HighConfidenceThreshold, number);
        }
    }

    [Theory]
    [InlineData("FT2026/1", "INVOICE")]
    [InlineData("FR2026/1", "INVOICE_RECEIPT")]
    [InlineData("PF2026/1", "PROFORMA")]
    [InlineData("PRO2026/1", "PROFORMA")]
    [InlineData("ORC2026/1", "PROFORMA")]   // v2.229.10: commercial offers are canonically PROFORMA
    public void Known_prefixes_map_to_their_document_type(string number, string expected)
    {
        Assert.Equal(expected, DocumentClassificationFallback.Derive(number, null).SuggestedType);
    }

    [Fact]
    public void The_ambiguous_FA_prefix_produces_no_suggestion()
    {
        // ALPLA uses FA for both "Factura" and "Factura de Adiantamento". Guessing either way
        // would be worse than staying silent — pending Finance confirmation.
        var result = DocumentClassificationFallback.Derive("FA2026/1", null);

        Assert.Null(result.SuggestedType);
    }

    // ── Filename is the weakest evidence ──

    [Fact]
    public void A_filename_alone_cannot_produce_high_confidence()
    {
        var result = DocumentClassificationFallback.Derive(documentNumber: null, fileName: "FT-simotecnica.pdf");

        Assert.Equal(RequestConstants.SourceDocumentTypes.Invoice, result.SuggestedType);
        Assert.Equal(DocumentClassificationFallback.FileNameOnlyMaxConfidence, result.Confidence);
        Assert.True(result.Confidence < DocumentClassificationFallback.PrefixOnlyMaxConfidence);
        Assert.True(result.Confidence < DocumentClassificationFallback.HighConfidenceThreshold);
    }

    [Fact]
    public void A_document_number_outranks_a_contradicting_filename()
    {
        var result = DocumentClassificationFallback.Derive("FT2026/1", "PROFORMA-draft.pdf");

        Assert.Equal(RequestConstants.SourceDocumentTypes.Invoice, result.SuggestedType);
        Assert.Equal(DocumentClassificationFallback.PrefixOnlyMaxConfidence, result.Confidence);
    }

    // ── Explicit title is the only strong evidence ──

    [Theory]
    [InlineData("FACTURA-RECIBO N.º 12", "INVOICE_RECEIPT")]
    [InlineData("FACTURA DE ADIANTAMENTO", "ADVANCE_INVOICE")]
    [InlineData("FACTURA PRÓ-FORMA", "PROFORMA")]
    [InlineData("ORÇAMENTO N.º 5", "PROFORMA")]        // v2.229.10 canonical commercial offer
    [InlineData("COTAÇÃO N.º 7", "PROFORMA")]
    [InlineData("PROPOSTA COMERCIAL 2026-01", "PROFORMA")]
    [InlineData("FACTURA N.º 900", "INVOICE")]
    public void Explicit_title_wording_is_decisive(string rawText, string expected)
    {
        var result = DocumentClassificationFallback.Derive("FT2026/1", null, rawText);

        Assert.Equal(expected, result.SuggestedType);
        Assert.True(result.Confidence > DocumentClassificationFallback.PrefixOnlyMaxConfidence);
        Assert.NotNull(result.TitleFound);
    }

    [Fact]
    public void A_specific_title_is_not_flattened_into_plain_invoice()
    {
        // "FACTURA-RECIBO" and "FACTURA DE ADIANTAMENTO" both contain "FACTURA"; matching order
        // must keep them distinct or every specific document collapses to INVOICE.
        Assert.Equal(RequestConstants.SourceDocumentTypes.InvoiceReceipt,
            DocumentClassificationFallback.Derive(null, null, "FACTURA-RECIBO").SuggestedType);
        Assert.Equal(RequestConstants.SourceDocumentTypes.AdvanceInvoice,
            DocumentClassificationFallback.Derive(null, null, "FACTURA DE ADIANTAMENTO").SuggestedType);
    }

    [Fact]
    public void An_explicit_non_fiscal_declaration_overrides_a_fiscal_title()
    {
        // "sem valor fiscal" is a statement by the issuer, not an inference — it wins.
        var result = DocumentClassificationFallback.Derive(
            "FT2026/1", null, "FACTURA N.º 900 — DOCUMENTO SEM VALOR FISCAL");

        Assert.Equal(RequestConstants.SourceDocumentTypes.Proforma, result.SuggestedType);
        Assert.False(result.IndicatesFiscalDocument);
        Assert.NotEmpty(result.NonFiscalMarkers);
    }

    // ── Silence when there is nothing to say ──

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("12345/2026", "scan001.pdf")]
    public void No_recognisable_evidence_produces_no_suggestion(string? number, string? fileName)
    {
        var result = DocumentClassificationFallback.Derive(number, fileName);

        Assert.Null(result.SuggestedType);
        Assert.Equal(0m, result.Confidence);
    }
}
