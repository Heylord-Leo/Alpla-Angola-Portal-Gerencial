using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// A PAYMENT request may pay several invoices at once — the same supplier billing two plants, a
/// monthly service split per site. These cover the two rules that make that safe: <b>each document
/// is validated on its own</b>, and <b>one invalid document blocks the whole request</b>.
/// </summary>
public class PaymentSourceDocumentValidatorTests
{
    private static PaymentSourceDocumentState Doc(
        string label = "Documento 1",
        Guid? id = null,
        int? supplier = 1,
        int? plant = 10,
        string? number = "FT-001",
        string? type = "PROFORMA",
        DateTime? date = null,
        DateTime? dueDate = null,
        // A separate flag, because `dueDate: null` cannot express "explicitly none" while the
        // parameter also carries a default.
        bool withoutDueDate = false,
        string? currency = "AOA",
        decimal? gross = 500_000m,
        decimal? itemsTotal = null,
        int items = 1,
        bool attachment = true,
        string? ocrSuggestion = null,
        decimal? confidence = null,
        bool acknowledged = false,
        string? justification = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Label = label,
            HasAttachment = attachment,
            SupplierId = supplier,
            PlantId = plant,
            DocumentNumber = number,
            SourceDocumentType = type,
            DocumentDate = date ?? new DateTime(2026, 8, 1),
            DueDate = withoutDueDate ? null : (dueDate ?? new DateTime(2026, 9, 1)),
            Currency = currency,
            GrossAmount = gross,
            ItemsTotal = itemsTotal ?? gross ?? 0m,
            ActiveItemCount = items,
            OcrSuggestion = ocrSuggestion,
            OcrConfidence = confidence,
            ClassificationConflictAcknowledged = acknowledged,
            ClassificationJustification = justification
        };

    private static PaymentSourceDocumentValidationResult Validate(
        params PaymentSourceDocumentState[] docs) =>
        PaymentSourceDocumentValidator.Validate(docs, requireClassification: true);

    // ── The request header is a compatibility echo, not a gate ──

    /// <summary>
    /// A multi-document request carries its classification on each document. The header's
    /// <c>SourceDocumentType</c> is a compatibility echo of the first one and must never block
    /// submission — the review screen used to demand it, leaving a request with a perfectly valid
    /// PROFORMA document refusing to submit because a field nobody edits was empty.
    /// </summary>
    [Fact]
    public void Documents_carry_the_classification_the_request_header_does_not_need()
    {
        // Nothing here mentions Request.SourceDocumentType: the validator is given DOCUMENTS, and
        // that is the whole point — the header is not one of its inputs and cannot gate the result.
        var result = Validate(Doc(type: "PROFORMA"));

        Assert.True(result.CanSubmit);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void A_document_without_a_type_blocks_and_names_itself()
    {
        var result = Validate(
            Doc(label: "Documento 1", type: "PROFORMA"),
            Doc(label: "Documento 2", type: null));

        Assert.False(result.CanSubmit);
        var problem = Assert.Single(
            result.Problems, p => p.Message == "Indique o tipo de documento anexado.");
        Assert.Equal("Documento 2", problem.Label);
    }

    [Fact]
    public void A_document_without_a_supplier_still_blocks()
    {
        // Request-level supplier is not required for multi-document; document-level still is.
        var result = Validate(Doc(supplier: null));

        Assert.False(result.CanSubmit);
        Assert.Contains(result.Problems, p => p.Message == "Indique o fornecedor.");
    }

    // ── Due date ──

    /// <summary>
    /// Every line item of a PAYMENT request must carry a due date, and an item takes it from the
    /// document it belongs to. Checked at the document so it is caught while the user is looking at
    /// it — not during persistence, after a request has already been created.
    /// </summary>
    [Fact]
    public void A_document_without_a_due_date_cannot_be_submitted()
    {
        var result = Validate(Doc(withoutDueDate: true));

        Assert.False(result.CanSubmit);
        Assert.Contains(result.Problems, p => p.Message == "Informe a data de vencimento do documento.");
    }

    [Fact]
    public void The_due_date_problem_names_its_own_document()
    {
        var result = Validate(
            Doc(label: "Documento 1"),
            Doc(label: "Documento 2", withoutDueDate: true));

        var problem = Assert.Single(
            result.Problems, p => p.Message == "Informe a data de vencimento do documento.");
        Assert.Equal("Documento 2", problem.Label);
    }

    [Fact]
    public void A_document_with_a_due_date_passes_that_rule()
    {
        var result = Validate(Doc(dueDate: new DateTime(2026, 10, 15)));

        Assert.DoesNotContain(result.Problems, p => p.Message.Contains("data de vencimento"));
    }

    // ── The happy paths ──

    [Fact]
    public void One_request_with_one_document_is_submittable()
    {
        var result = Validate(Doc());

        Assert.True(result.CanSubmit);
        Assert.Equal(500_000m, result.RequestTotal);
        Assert.Null(result.MixedTypeNotice);
    }

    [Fact]
    public void One_request_with_two_documents_totals_both()
    {
        // The Viana 1 / Viana 2 case from the business scenario.
        var result = Validate(
            Doc(label: "Documento 1", plant: 10, number: "FT-001", gross: 500_000m),
            Doc(label: "Documento 2", plant: 20, number: "FT-002", gross: 700_000m));

        Assert.True(result.CanSubmit);
        Assert.Equal(1_200_000m, result.RequestTotal);
    }

    // ── One invalid document blocks everything, and says which ──

    [Fact]
    public void One_invalid_document_blocks_the_whole_request_and_names_itself()
    {
        var result = Validate(
            Doc(label: "Documento 1"),
            Doc(label: "Documento 2", number: null));

        Assert.False(result.CanSubmit);
        Assert.All(result.Problems, p => Assert.Equal("Documento 2", p.Label));
        Assert.Contains(result.Problems, p => p.Message.Contains("número"));
    }

    [Fact]
    public void A_request_with_no_documents_cannot_be_submitted()
    {
        var result = PaymentSourceDocumentValidator.Validate(
            Array.Empty<PaymentSourceDocumentState>(), requireClassification: true);

        Assert.False(result.CanSubmit);
        Assert.Single(result.Problems);
    }

    [Theory]
    [InlineData("attachment")]
    [InlineData("supplier")]
    [InlineData("plant")]
    [InlineData("date")]
    [InlineData("currency")]
    [InlineData("items")]
    public void Every_mandatory_field_is_enforced_per_document(string missing)
    {
        var doc = missing switch
        {
            "attachment" => Doc(attachment: false),
            "supplier" => Doc(supplier: null),
            "plant" => Doc(plant: null),
            "date" => Doc(date: null) with { DocumentDate = null },
            "currency" => Doc(currency: null),
            _ => Doc(items: 0)
        };

        Assert.False(Validate(doc).CanSubmit);
    }

    [Fact]
    public void A_documents_value_must_be_attributable_to_its_items()
    {
        // Otherwise the group totals derived from items would silently disagree with what is paid.
        var result = Validate(Doc(gross: 500_000m, itemsTotal: 400_000m));

        Assert.False(result.CanSubmit);
        Assert.Contains(result.Problems, p => p.Message.Contains("soma dos itens"));
    }

    [Fact]
    public void Rounding_noise_between_items_and_the_document_total_is_tolerated()
    {
        var result = Validate(Doc(gross: 500_000m, itemsTotal: 499_999.50m));

        Assert.True(result.CanSubmit);
    }

    // ── Classification, per document ──

    [Fact]
    public void An_estimate_cannot_originate_a_payment_however_many_documents_there_are()
    {
        var result = Validate(Doc(label: "Documento 1"), Doc(label: "Documento 2", type: "ESTIMATE"));

        Assert.False(result.CanSubmit);
        Assert.Contains(result.Problems, p =>
            p.Label == "Documento 2" && p.Message.Contains("Orçamento"));
    }

    [Fact]
    public void A_factura_recibo_cannot_originate_a_payment_either()
    {
        Assert.False(Validate(Doc(type: "INVOICE_RECEIPT")).CanSubmit);
    }

    [Fact]
    public void An_unresolved_classification_conflict_blocks_only_its_own_document()
    {
        var clean = Doc(label: "Documento 1");
        var conflicted = Doc(label: "Documento 2", type: "PROFORMA",
                             ocrSuggestion: "INVOICE", confidence: 0.9m);

        var result = Validate(clean, conflicted);

        Assert.False(result.CanSubmit);
        Assert.All(result.Problems, p => Assert.Equal("Documento 2", p.Label));
    }

    [Fact]
    public void A_confirmed_and_justified_conflict_passes()
    {
        var result = Validate(Doc(
            type: "PROFORMA", ocrSuggestion: "INVOICE", confidence: 0.9m,
            acknowledged: true,
            justification: "Fornecedor confirmou por email que emitiu a pró-forma."));

        Assert.True(result.CanSubmit);
    }

    [Fact]
    public void Classification_is_optional_while_the_feature_is_off()
    {
        var result = PaymentSourceDocumentValidator.Validate(
            new[] { Doc(type: null) }, requireClassification: false);

        Assert.True(result.CanSubmit);
    }

    // ── Currency ──

    [Fact]
    public void Documents_of_the_request_must_share_one_currency()
    {
        var result = Validate(
            Doc(label: "Documento 1", currency: "AOA"),
            Doc(label: "Documento 2", currency: "USD"));

        Assert.False(result.CanSubmit);
        Assert.Contains(result.Problems, p => p.Message.Contains("mesma moeda"));
    }

    // ── Mixed types: informational, never blocking ──

    [Fact]
    public void The_same_supplier_with_different_types_is_allowed_and_only_noticed()
    {
        var result = Validate(
            Doc(label: "Documento 1", supplier: 1, type: "PROFORMA", number: "PF-1"),
            Doc(label: "Documento 2", supplier: 1, type: "INVOICE", number: "FT-1"));

        Assert.True(result.CanSubmit);   // allowed — not an error
        Assert.Equal(PaymentSourceDocumentValidator.MixedTypeMessage, result.MixedTypeNotice);
    }

    [Fact]
    public void Different_suppliers_with_different_types_raise_no_notice()
    {
        var result = Validate(
            Doc(label: "Documento 1", supplier: 1, type: "PROFORMA"),
            Doc(label: "Documento 2", supplier: 2, type: "INVOICE"));

        Assert.True(result.CanSubmit);
        Assert.Null(result.MixedTypeNotice);
    }
}

/// <summary>
/// Which PO groups a PAYMENT request's items produce. The previous behaviour created exactly one
/// group per request; with several source documents that is wrong in both directions.
/// </summary>
public class PaymentGroupPlanTests
{
    private static PaymentGroupableItem Item(
        int? supplier = 1,
        string? currency = "AOA",
        int? plant = 10,
        string? type = "PROFORMA",
        decimal amount = 100m,
        Guid? documentId = null) =>
        new()
        {
            LineItemId = Guid.NewGuid(),
            PaymentSourceDocumentId = documentId ?? Guid.NewGuid(),
            SupplierId = supplier,
            SupplierNameSnapshot = $"Fornecedor {supplier}",
            CurrencyCode = currency,
            PlantId = plant,
            SourceDocumentType = type,
            TotalAmount = amount
        };

    private static IReadOnlyList<PlannedPaymentGroup> Plan(params PaymentGroupableItem[] items) =>
        PaymentGroupPlan.Build(items, "POST_PAID");

    [Fact]
    public void One_document_for_one_plant_produces_one_group()
    {
        var doc = Guid.NewGuid();
        var groups = Plan(Item(documentId: doc, amount: 300m), Item(documentId: doc, amount: 200m));

        var group = Assert.Single(groups);
        Assert.Equal(500m, group.TotalAmount);
        Assert.Equal(2, group.LineItemIds.Count);
        Assert.Equal(doc, Assert.Single(group.SourceDocumentIds));
    }

    [Fact]
    public void Two_documents_for_different_plants_produce_two_groups()
    {
        var groups = Plan(
            Item(plant: 10, amount: 500_000m),
            Item(plant: 20, amount: 700_000m));

        Assert.Equal(2, groups.Count);
        Assert.Equal(500_000m, groups.Single(g => g.Key.PlantId == 10).TotalAmount);
        Assert.Equal(700_000m, groups.Single(g => g.Key.PlantId == 20).TotalAmount);
    }

    [Fact]
    public void Two_documents_for_the_same_plant_and_type_share_one_group()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var groups = Plan(
            Item(documentId: a, plant: 10, type: "INVOICE", amount: 100m),
            Item(documentId: b, plant: 10, type: "INVOICE", amount: 200m));

        var group = Assert.Single(groups);
        Assert.Equal(300m, group.TotalAmount);
        Assert.Equal(2, group.SourceDocumentIds.Count);
    }

    [Fact]
    public void The_same_supplier_and_plant_with_different_types_produce_two_groups()
    {
        // A proforma owes an operation invoice; a factura does not. One group cannot owe both.
        var groups = Plan(
            Item(plant: 10, type: "PROFORMA", amount: 100m),
            Item(plant: 10, type: "INVOICE", amount: 200m));

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void One_document_spanning_two_plants_produces_two_groups()
    {
        // Consolidation seen from the origin side: one invoice does not imply one group.
        var doc = Guid.NewGuid();
        var groups = Plan(
            Item(documentId: doc, plant: 10, amount: 400m),
            Item(documentId: doc, plant: 20, amount: 600m));

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(doc, Assert.Single(g.SourceDocumentIds)));
    }

    [Fact]
    public void Different_suppliers_never_share_a_group()
    {
        Assert.Equal(2, Plan(Item(supplier: 1), Item(supplier: 2)).Count);
    }

    [Fact]
    public void Group_order_is_deterministic()
    {
        var forward = Plan(Item(supplier: 2, plant: 20), Item(supplier: 1, plant: 10));
        var reverse = Plan(Item(supplier: 1, plant: 10), Item(supplier: 2, plant: 20));

        // Stable ordering is what lets concurrent transactions lock groups in the same sequence.
        Assert.Equal(
            forward.Select(g => g.Key).ToList(),
            reverse.Select(g => g.Key).ToList());
    }
}
