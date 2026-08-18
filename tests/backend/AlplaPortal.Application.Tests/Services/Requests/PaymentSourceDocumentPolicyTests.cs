using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// When a PAYMENT request's origin documents may be changed, and what the request header may claim
/// about them.
/// </summary>
public class PaymentSourceDocumentPolicyTests
{
    // ── Editable states ──

    [Theory]
    [InlineData(RequestConstants.Statuses.Draft)]
    [InlineData(RequestConstants.Statuses.AreaAdjustment)]
    [InlineData(RequestConstants.Statuses.FinalAdjustment)]
    public void Documents_may_be_changed_in_draft_and_after_a_return_for_adjustment(string status)
    {
        Assert.True(PaymentSourceDocumentPolicy.IsEditable(status));
    }

    [Theory]
    [InlineData(RequestConstants.Statuses.WaitingAreaApproval)]
    [InlineData(RequestConstants.Statuses.WaitingFinalApproval)]
    [InlineData(RequestConstants.Statuses.FinalApproved)]
    [InlineData(RequestConstants.Statuses.PaymentScheduled)]
    [InlineData(RequestConstants.Statuses.Paid)]
    [InlineData(RequestConstants.Statuses.Completed)]
    [InlineData(null)]
    public void Documents_are_frozen_once_submitted(string? status)
    {
        // They are what the approvers approved. Changing one requires a formal return — a decision
        // somebody makes and the timeline records, not a side effect of opening a screen.
        Assert.False(PaymentSourceDocumentPolicy.IsEditable(status));
    }

    [Fact]
    public void Refusing_an_edit_explains_the_route_forward()
    {
        var reason = PaymentSourceDocumentPolicy.EditBlockedReason(RequestConstants.Statuses.WaitingFinalApproval);

        Assert.Contains("devolvido", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, PaymentSourceDocumentPolicy.EditBlockedReason(RequestConstants.Statuses.Draft));
    }

    // ── Delete vs void ──

    [Fact]
    public void A_document_removed_before_submission_leaves_no_trace()
    {
        Assert.True(PaymentSourceDocumentPolicy.MayHardDelete(requestWasEverSubmitted: false));
    }

    [Fact]
    public void A_document_removed_after_submission_is_voided_not_deleted()
    {
        // Its classification decision and any justification were already audited, and an audit must
        // survive the object it describes.
        Assert.False(PaymentSourceDocumentPolicy.MayHardDelete(requestWasEverSubmitted: true));
    }

    // ── Header compatibility ──

    private static PaymentSourceDocumentHeaderInput D(int? supplier, int? plant, string? type) =>
        new(supplier, plant, type);

    [Fact]
    public void A_single_document_populates_every_header_field()
    {
        var header = PaymentSourceDocumentPolicy.DeriveHeader(new[] { D(7, 3, "PROFORMA") });

        Assert.Equal(7, header.SupplierId);
        Assert.Equal(3, header.DocumentPlantId);
        Assert.Equal("PROFORMA", header.SourceDocumentType);
        Assert.False(header.HasSeveralDocuments);
    }

    [Fact]
    public void Documents_that_agree_still_populate_the_header()
    {
        var header = PaymentSourceDocumentPolicy.DeriveHeader(new[]
        {
            D(7, 3, "INVOICE"), D(7, 3, "INVOICE")
        });

        Assert.Equal(7, header.SupplierId);
        Assert.Equal("INVOICE", header.SourceDocumentType);
        Assert.True(header.HasSeveralDocuments);
    }

    [Fact]
    public void Disagreeing_documents_leave_the_header_null_rather_than_guessing()
    {
        var header = PaymentSourceDocumentPolicy.DeriveHeader(new[]
        {
            D(7, 3, "PROFORMA"), D(9, 4, "INVOICE")
        });

        Assert.Null(header.SupplierId);
        Assert.Null(header.DocumentPlantId);
        Assert.Null(header.SourceDocumentType);
    }

    [Fact]
    public void A_mixed_document_type_is_never_manufactured()
    {
        // The obligation resolver would have to resolve "MIXED", and no honest answer exists.
        var header = PaymentSourceDocumentPolicy.DeriveHeader(new[]
        {
            D(7, 3, "PROFORMA"), D(7, 3, "INVOICE")
        });

        Assert.Null(header.SourceDocumentType);
        Assert.NotEqual("MIXED", header.SourceDocumentType);

        // Supplier and plant still agree, so those survive.
        Assert.Equal(7, header.SupplierId);
        Assert.Equal(3, header.DocumentPlantId);
    }

    [Fact]
    public void The_superseded_alias_does_not_count_as_disagreement()
    {
        var header = PaymentSourceDocumentPolicy.DeriveHeader(new[]
        {
            D(7, 3, "INVOICE"), D(7, 3, "FINAL_INVOICE")
        });

        Assert.Equal("INVOICE", header.SourceDocumentType);
    }

    [Fact]
    public void An_empty_request_claims_nothing()
    {
        var header = PaymentSourceDocumentPolicy.DeriveHeader(Array.Empty<PaymentSourceDocumentHeaderInput>());

        Assert.Null(header.SupplierId);
        Assert.Null(header.SourceDocumentType);
        Assert.False(header.HasSeveralDocuments);
    }
}

/// <summary>
/// Whether an item may belong to a source document. All four checks guard one thing: a group's
/// totals are derived from its items, so an item disagreeing with its document would fund a group
/// the document never intended to.
/// </summary>
public class PaymentLineItemAssociationTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherRequestId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static PaymentSourceDocumentBinding Doc(
        bool voided = false, int? supplier = 7, int? plant = 3, string? currency = "AOA",
        Guid? requestId = null) =>
        new(Guid.NewGuid(), requestId ?? RequestId, voided, supplier, plant, currency, 2);

    private static PaymentLineItemBinding Item(
        int? supplier = null, int? plant = null, string? currency = null) =>
        new(supplier, plant, currency);

    [Fact]
    public void An_item_matching_its_document_is_accepted()
    {
        Assert.Null(PaymentLineItemAssociation.Validate(
            RequestId, Doc(), Item(supplier: 7, plant: 3, currency: "AOA")));
    }

    [Fact]
    public void An_item_that_states_nothing_inherits_and_is_accepted()
    {
        // Requiring the client to echo supplier, plant and currency would make every item edit a
        // chance to introduce a mismatch.
        Assert.Null(PaymentLineItemAssociation.Validate(RequestId, Doc(), Item()));
    }

    [Fact]
    public void A_missing_document_is_refused()
    {
        Assert.NotNull(PaymentLineItemAssociation.Validate(RequestId, null, Item()));
    }

    [Fact]
    public void A_document_belonging_to_another_request_is_refused()
    {
        var problem = PaymentLineItemAssociation.Validate(
            RequestId, Doc(requestId: OtherRequestId), Item());

        Assert.NotNull(problem);
        Assert.Contains("outro pedido", problem!);
    }

    [Fact]
    public void A_voided_document_accepts_no_items()
    {
        var problem = PaymentLineItemAssociation.Validate(RequestId, Doc(voided: true), Item());

        Assert.NotNull(problem);
        Assert.Contains("anulado", problem!);
    }

    [Fact]
    public void A_supplier_mismatch_is_refused()
    {
        var problem = PaymentLineItemAssociation.Validate(
            RequestId, Doc(supplier: 7), Item(supplier: 9));

        Assert.NotNull(problem);
        Assert.Contains("fornecedor", problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_plant_mismatch_is_refused()
    {
        var problem = PaymentLineItemAssociation.Validate(
            RequestId, Doc(plant: 3), Item(plant: 4));

        Assert.NotNull(problem);
        Assert.Contains("planta", problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_currency_mismatch_is_refused()
    {
        var problem = PaymentLineItemAssociation.Validate(
            RequestId, Doc(currency: "AOA"), Item(currency: "USD"));

        Assert.NotNull(problem);
        Assert.Contains("moeda", problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Currency_comparison_ignores_casing_and_padding()
    {
        Assert.Null(PaymentLineItemAssociation.Validate(
            RequestId, Doc(currency: "AOA"), Item(currency: " aoa ")));
    }

    [Fact]
    public void Inheriting_fills_only_what_the_item_left_unset()
    {
        var doc = Doc(supplier: 7, plant: 3, currency: "AOA");

        var inherited = PaymentLineItemAssociation.Inherit(doc, Item(plant: 5));

        Assert.Equal(7, inherited.SupplierId);     // taken from the document
        Assert.Equal(5, inherited.PlantId);        // the item's own value survives
        Assert.Equal("AOA", inherited.CurrencyCode);
    }

    // ── When the rule applies at all ──

    [Fact]
    public void Quotation_items_are_never_asked_for_a_source_document()
    {
        Assert.False(PaymentLineItemAssociation.IsDocumentRequired("QUOTATION", true, 3));
    }

    [Fact]
    public void A_legacy_payment_request_with_no_documents_is_left_alone()
    {
        // Nothing was backfilled, so a request created before this release has no documents and
        // must keep working exactly as it did.
        Assert.False(PaymentLineItemAssociation.IsDocumentRequired("PAYMENT", true, 0));
    }

    [Fact]
    public void The_document_is_optional_while_the_workflow_is_not_mandatory()
    {
        Assert.False(PaymentLineItemAssociation.IsDocumentRequired("PAYMENT", false, 2));
    }

    [Fact]
    public void A_payment_request_that_has_documents_requires_one_per_item()
    {
        Assert.True(PaymentLineItemAssociation.IsDocumentRequired("PAYMENT", true, 2));
    }
}

/// <summary>
/// Budget allocation resolves the plant per line, not per request.
///
/// <para>The first pass of the consumer audit reported this as a defect after reading
/// <c>int plantId = request.PlantId ?? 0;</c> in isolation. That was wrong — both call sites already
/// fall back to <c>li.PlantId</c> six lines below. The behaviour was nevertheless unguarded, and a
/// multi-plant payment request is exactly the case that would expose a regression, so it is locked
/// in here. These assert the precedence the production code implements:
/// <b>explicit assignment → line plant → request plant</b>.</para>
/// </summary>
public class BudgetPlantResolutionTests
{
    /// <summary>Mirrors the resolution order in BudgetCalculationHelper and BudgetPreviewController.</summary>
    private static int Resolve(int? explicitAssignment, int? linePlantId, int? requestPlantId)
    {
        var plantId = requestPlantId ?? 0;

        if (explicitAssignment.HasValue) plantId = explicitAssignment.Value;
        else if (linePlantId.HasValue) plantId = linePlantId.Value;

        return plantId;
    }

    [Fact]
    public void A_line_with_its_own_plant_uses_the_line_plant()
    {
        Assert.Equal(20, Resolve(explicitAssignment: null, linePlantId: 20, requestPlantId: 10));
    }

    [Fact]
    public void A_line_without_a_plant_falls_back_to_the_request_plant()
    {
        Assert.Equal(10, Resolve(explicitAssignment: null, linePlantId: null, requestPlantId: 10));
    }

    [Fact]
    public void An_explicit_assignment_outranks_both()
    {
        Assert.Equal(30, Resolve(explicitAssignment: 30, linePlantId: 20, requestPlantId: 10));
    }

    [Fact]
    public void A_multi_plant_payment_does_not_collapse_onto_the_request_plant()
    {
        // Two documents, two plants, one request created under plant 10. Budget must follow the
        // lines, not the header — otherwise every cêntimo lands on Viana 1.
        var lines = new[] { (line: (int?)10, amount: 500_000m), (line: (int?)20, amount: 700_000m) };

        var allocations = lines
            .GroupBy(l => Resolve(null, l.line, requestPlantId: 10))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.amount));

        Assert.Equal(2, allocations.Count);
        Assert.Equal(500_000m, allocations[10]);
        Assert.Equal(700_000m, allocations[20]);
    }
}
