using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.249.4 — the shared P.O. DOCUMENT identification rule (registration and correction). Pure Domain; the extraction
/// facts are the input, the verdict the output. Pins: the three business series (ECF11 = Serviços, ECF10 = Materiais
/// diversos, ECF = Material de stock) with realistic spacing/punctuation; longest-family-first and word boundaries (no
/// substring classification — ECF99 / ECF1 / ECF12 are NOT generic ECF); identity (own number + heading) over body
/// references; several identities / conflicts / missing identifiers → UNCERTAIN (explicit review); a positively
/// identified quotation / invoice without any P.O. identity → IDENTIFIED_OTHER_TYPE (blocked, no override); no
/// extraction → UNCERTAIN. Matching supplier/amount never enters this rule.
/// </summary>
public class PurchaseOrderDocumentIdentifierTests
{
    private static PoDocumentIdentification Id(string? number, string? title = null, string? cls = null, decimal? conf = null, IReadOnlyList<string>? refs = null) =>
        PurchaseOrderDocumentIdentifier.Identify(new PoDocumentIdentificationInput
        {
            DocumentNumber = number, DocumentTitle = title, ClassificationType = cls, ClassificationConfidence = conf, ReferencedDocumentNumbers = refs,
        });

    [Theory]
    [InlineData("ECF11 2026/421", "ECF11", "Serviços", "ECF11 2026/421")]
    [InlineData("ECF10 2026/219", "ECF10", "Material diverso / escritório", "ECF10 2026/219")]
    [InlineData("ECF 2026/107", "ECF", "Material de stock", "ECF 2026/107")]
    [InlineData("ecf11  2026 / 421", "ECF11", "Serviços", "ECF11 2026/421")]      // OCR spacing + lower case
    [InlineData("ECF10: 2026-219", "ECF10", "Material diverso / escritório", "ECF10 2026/219")] // punctuation + dash
    [InlineData("ECF.2026/9003", "ECF", "Material de stock", "ECF 2026/9003")]
    public void Recognises_the_three_business_series_with_realistic_whitespace_and_punctuation(string number, string family, string label, string display)
    {
        var id = Id(number);
        Assert.Equal(PoDocumentVerdicts.IdentifiedPo, id.Verdict);
        Assert.Equal(family, id.Family); Assert.Equal(label, id.FamilyLabel); Assert.Equal(display, id.Reference);
        Assert.False(id.IsBlocked); Assert.False(id.RequiresExplicitConfirmation);
    }

    [Fact]
    public void The_complete_number_is_read_from_the_heading_when_the_number_field_is_a_fragment()
    {
        var id = Id("2026/421", "PO Serviços ECF11 2026/421");
        Assert.Equal(PoDocumentVerdicts.IdentifiedPo, id.Verdict);
        Assert.Equal("ECF11 2026/421", id.Reference);
        Assert.Equal(PoDocumentCandidateSources.Title, Assert.Single(id.IdentityCandidates).Source);
    }

    [Theory]
    [InlineData("ECF99 2026/9003")]   // the synthetic TEST prefix
    [InlineData("ECF1 2026/5")]
    [InlineData("ECF12 2026/77")]
    [InlineData("ECF112026/421")]     // no boundary between family and year
    public void An_unrecognised_ECF_prefix_is_never_classified_as_generic_ECF(string number)
    {
        var id = Id(number, "Encomenda a Fornecedor " + number);
        Assert.Equal(PoDocumentVerdicts.Uncertain, id.Verdict);
        Assert.Empty(id.IdentityCandidates);
        Assert.Null(id.Family);
        Assert.True(id.RequiresExplicitConfirmation);
        Assert.False(id.IsBlocked);
        Assert.Contains(id.ReasonCode, new[] { PoDocumentIdentificationReasons.UnrecognisedIdentifier, PoDocumentIdentificationReasons.NoIdentifier });
        if (id.ReasonCode == PoDocumentIdentificationReasons.UnrecognisedIdentifier) Assert.Contains("ECF11 = Serviços", id.Reason);   // the series guidance names the three business prefixes
    }

    [Fact]
    public void ECF11_and_ECF10_are_matched_before_the_generic_family()
    {
        Assert.Equal("ECF11", Id("ECF11 2026/421").Family);
        Assert.Equal("ECF10", Id("ECF10 2026/421").Family);
        Assert.Equal("ECF", Id("ECF 2026/421").Family);
        Assert.NotEqual(Id("ECF11 2026/421").Canonical, Id("ECF 2026/421").Canonical);
    }

    [Fact]
    public void Several_identity_references_are_uncertain_and_listed_for_the_Buyer_to_choose()
    {
        var id = Id("ECF11 2026/421", "Encomenda ECF10 2026/219");
        Assert.Equal(PoDocumentVerdicts.Uncertain, id.Verdict);
        Assert.Equal(PoDocumentIdentificationReasons.MultiplePoIdentities, id.ReasonCode);
        Assert.Equal(2, id.IdentityCandidates.Count);
        Assert.Null(id.Reference);
    }

    [Fact]
    public void Body_references_to_previous_or_cancelled_orders_never_identify_the_document()
    {
        // A cancellation note / a quotation that cites the ECF in its body: the own number is not a P.O.
        var id = Id("NC-2026-18", "Nota de cancelamento", refs: new[] { "em substituição da ECF10 2026/364" });
        Assert.Equal(PoDocumentVerdicts.Uncertain, id.Verdict);
        Assert.Equal(PoDocumentIdentificationReasons.PoReferencedOnly, id.ReasonCode);
        Assert.Empty(id.IdentityCandidates);
        Assert.Equal("ECF10 2026/364", Assert.Single(id.BodyReferences).Reference);
        Assert.Equal(PoDocumentCandidateSources.BodyReference, id.BodyReferences[0].Source);
    }

    [Fact]
    public void A_real_PO_that_also_cites_the_order_it_replaces_keeps_its_own_identity()
    {
        var id = Id("ECF10 2026/9003", "Encomenda Mat Escritório/Diversos ECF10 2026/9003", refs: new[] { "anula ECF10 2026/364" });
        Assert.Equal(PoDocumentVerdicts.IdentifiedPo, id.Verdict);
        Assert.Equal("ECF10 2026/9003", id.Reference);
        Assert.Equal("ECF10 2026/364", Assert.Single(id.BodyReferences).Reference);
    }

    [Theory]
    [InlineData("TEST-MAQSIS-2026-9003", "Cotação", "PROFORMA", 0.92, "Cotação / Factura pró-forma")]
    [InlineData("FT 2026/18", "FACTURA", "INVOICE", 0.9, "Factura")]
    [InlineData("PF 2026/3578", "Factura Pró-forma", "PROFORMA", 0.55, "Cotação / Factura pró-forma")]
    public void A_positively_identified_quotation_or_invoice_without_PO_identity_is_blocked_whatever_the_amount(string number, string title, string cls, decimal conf, string label)
    {
        var id = Id(number, title, cls, conf);
        Assert.Equal(PoDocumentVerdicts.IdentifiedOtherType, id.Verdict);
        Assert.True(id.IsBlocked);
        Assert.Equal(label, id.OtherTypeLabel);
        Assert.Contains("mesmo que o fornecedor e o valor coincidam", id.Reason);
    }

    [Fact]
    public void A_quotation_title_blocks_even_when_the_classifier_was_silent()
    {
        var id = Id("TEST-MAQSIS-2026-9003", "PROPOSTA COMERCIAL");
        Assert.Equal(PoDocumentVerdicts.IdentifiedOtherType, id.Verdict);
    }

    [Fact]
    public void A_low_confidence_classification_without_identity_is_uncertain_not_blocked()
    {
        var id = Id("DOC-77", null, "PROFORMA", 0.3m);
        Assert.Equal(PoDocumentVerdicts.Uncertain, id.Verdict);
        Assert.Equal(PoDocumentIdentificationReasons.NoIdentifier, id.ReasonCode);
        Assert.False(id.IsBlocked);
    }

    [Fact]
    public void A_PO_identity_contradicted_by_a_confident_quotation_classification_is_uncertain_never_silently_accepted()
    {
        var id = Id("ECF11 2026/421", null, "PROFORMA", 0.85m);
        Assert.Equal(PoDocumentVerdicts.Uncertain, id.Verdict);
        Assert.Equal(PoDocumentIdentificationReasons.PoIdentityConflictsWithClassification, id.ReasonCode);
        Assert.Equal("ECF11 2026/421", id.Reference);        // the suggestion survives for the field
        Assert.True(id.RequiresExplicitConfirmation);
    }

    [Fact]
    public void A_PO_identity_with_a_weak_contrary_classification_is_identified()
    {
        var id = Id("ECF11 2026/421", "PO Serviços ECF11 2026/421", "PROFORMA", 0.4m);
        Assert.Equal(PoDocumentVerdicts.IdentifiedPo, id.Verdict);
    }

    [Fact]
    public void A_family_word_without_a_complete_reference_asks_for_manual_entry()
    {
        var id = Id("ECF11", "Encomenda ECF11");
        Assert.Equal(PoDocumentVerdicts.Uncertain, id.Verdict);
        Assert.Equal(PoDocumentIdentificationReasons.FamilyWithoutReference, id.ReasonCode);
    }

    [Fact]
    public void A_fiscal_number_is_never_a_reference()
    {
        var id = Id("5001713205", "Encomenda a Fornecedor");
        Assert.Equal(PoDocumentVerdicts.Uncertain, id.Verdict);
        Assert.Empty(id.IdentityCandidates);
    }

    [Fact]
    public void No_extraction_is_uncertain_with_its_own_reason()
    {
        var id = PurchaseOrderDocumentIdentifier.Identify(new PoDocumentIdentificationInput { ExtractionPerformed = false });
        Assert.Equal(PoDocumentVerdicts.Uncertain, id.Verdict);
        Assert.Equal(PoDocumentIdentificationReasons.NoExtraction, id.ReasonCode);
        Assert.True(id.RequiresExplicitConfirmation);
        Assert.Equal(PoDocumentIdentificationReasons.NoExtraction, PurchaseOrderDocumentIdentifier.Identify(null).ReasonCode);
    }
}
