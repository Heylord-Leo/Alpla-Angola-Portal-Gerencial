using System;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Agg = RequestConstants.OperationInvoiceStatuses;
using Types = RequestConstants.SourceDocumentTypes;
using Reasons = PoGroupReclassificationBlockReasons;
using Dims = PoGroupReclassificationPlanner.DimensionNames;

/// <summary>
/// Release 4 Phase 1c/1d: what a source-document edit touching the grouping key means for the PO
/// groups already built over its lines. The conservative order being pinned: disagreeing
/// contributors block, agreement with the stamp needs nothing, financial evidence blocks a
/// commercial-identity change, operation-invoice activity blocks any identity change, and only
/// then is a re-stamp allowed.
/// </summary>
public class PoGroupReclassificationPlannerTests
{
    private static readonly Guid GroupId = Guid.NewGuid();

    private static PaymentGroupingKey Key(
        int? supplier = 10, string? currency = "AOA", string? condition = null,
        int? plant = 1, string? type = Types.Proforma) =>
        PaymentGroupingKey.From(supplier, currency, condition, plant, type);

    private static PoGroupReclassificationDecision Plan(
        PaymentGroupingKey current,
        PaymentGroupingKey[] contributing,
        bool operationInvoiceActivity = false,
        bool commercialEvidence = false,
        bool capturedExpectedTotal = false) =>
        Assert.Single(PoGroupReclassificationPlanner.Plan(new[]
        {
            new PoGroupReclassificationInput
            {
                GroupId = GroupId,
                CurrentGroupKey = current,
                ContributingKeys = contributing,
                HasOperationInvoiceActivity = operationInvoiceActivity,
                HasCommercialEvidence = commercialEvidence,
                HasCapturedExpectedTotal = capturedExpectedTotal
            }
        }));

    // ── Phase 1c semantics, preserved: the type dimension ──

    [Fact]
    public void A_proforma_group_whose_document_became_a_factura_is_restamped_to_not_required()
    {
        var d = Plan(Key(type: Types.Proforma), new[] { Key(type: Types.Invoice) });

        Assert.Equal(PoGroupReclassificationAction.Restamp, d.Action);
        Assert.Equal(Types.Invoice, d.NewKey!.Value.SourceDocumentType);
        Assert.Equal(new[] { Dims.SourceDocumentType }, d.ChangedDimensions);
        Assert.False(d.Obligations!.RequiresOperationInvoice);
        Assert.Equal(Agg.NotRequired, d.Obligations.OperationInvoiceStatus);
    }

    [Fact]
    public void A_factura_group_corrected_to_proforma_owes_an_operation_invoice_again()
    {
        var d = Plan(Key(type: Types.Invoice), new[] { Key(type: Types.Proforma) });

        Assert.Equal(PoGroupReclassificationAction.Restamp, d.Action);
        Assert.True(d.Obligations!.RequiresOperationInvoice);
        Assert.Equal(Agg.PendingUpload, d.Obligations.OperationInvoiceStatus);
    }

    [Fact]
    public void A_group_whose_documents_still_agree_with_its_stamp_needs_nothing()
    {
        var d = Plan(Key(), new[] { Key(), Key() });

        Assert.Equal(PoGroupReclassificationAction.NoChange, d.Action);
    }

    [Fact]
    public void A_group_with_operation_invoice_activity_refuses_a_change_of_type()
    {
        var d = Plan(Key(type: Types.Proforma), new[] { Key(type: Types.Invoice) },
            operationInvoiceActivity: true);

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.PostPaymentActivityStarted, d.BlockReasonCode);
    }

    [Fact]
    public void Activity_does_not_block_when_the_documents_still_agree_with_the_stamp()
    {
        var d = Plan(Key(), new[] { Key() },
            operationInvoiceActivity: true, commercialEvidence: true);

        Assert.Equal(PoGroupReclassificationAction.NoChange, d.Action);
    }

    [Fact]
    public void A_group_left_with_no_active_documents_is_undecidable_and_blocks()
    {
        var d = Plan(Key(), Array.Empty<PaymentGroupingKey>());

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.GroupingKeyInvalidated, d.BlockReasonCode);
    }

    [Fact]
    public void The_legacy_final_invoice_alias_agrees_with_invoice()
    {
        // PaymentGroupingKey.From normalizes, so FINAL_INVOICE and INVOICE are one type,
        // not a mixed group.
        var d = Plan(Key(type: Types.Invoice),
            new[] { Key(type: "FINAL_INVOICE"), Key(type: Types.Invoice) });

        Assert.Equal(PoGroupReclassificationAction.NoChange, d.Action);
    }

    // ── Phase 1d: every dimension can invalidate the key ──

    [Theory]
    [InlineData(99, "AOA", 1, "PROFORMA", Dims.Supplier)]
    [InlineData(10, "USD", 1, "PROFORMA", Dims.Currency)]
    [InlineData(10, "AOA", 2, "PROFORMA", Dims.Plant)]
    [InlineData(10, "AOA", 1, "INVOICE", Dims.SourceDocumentType)]
    public void One_document_diverging_on_any_dimension_invalidates_the_grouping_key(
        int supplier, string currency, int plant, string type, string expectedDimension)
    {
        var d = Plan(Key(), new[]
        {
            Key(),                                                              // the sibling stays
            Key(supplier: supplier, currency: currency, plant: plant, type: type)
        });

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.GroupingKeyInvalidated, d.BlockReasonCode);
        Assert.Contains(expectedDimension, d.ChangedDimensions);
        Assert.Contains(expectedDimension, d.BlockReason);
    }

    [Fact]
    public void Contributors_disagreeing_on_payment_condition_also_invalidate_the_key()
    {
        // Unreachable through a document edit today (documents carry no condition), but the
        // mechanism must hold if a future path ever produces it.
        var d = Plan(Key(condition: "NET30"),
            new[] { Key(condition: "NET30"), Key(condition: "NET15") });

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.GroupingKeyInvalidated, d.BlockReasonCode);
        Assert.Contains(Dims.PaymentCondition, d.ChangedDimensions);
    }

    // ── Phase 1d: whole-group commercial changes ──

    [Fact]
    public void A_coherent_supplier_change_with_no_evidence_is_restamped()
    {
        var d = Plan(Key(supplier: 10), new[] { Key(supplier: 99) });

        Assert.Equal(PoGroupReclassificationAction.Restamp, d.Action);
        Assert.Equal(99, d.NewKey!.Value.SupplierId);
        Assert.Equal(new[] { Dims.Supplier }, d.ChangedDimensions);
    }

    [Fact]
    public void A_registered_po_blocks_a_supplier_change_even_when_coherent()
    {
        var d = Plan(Key(supplier: 10), new[] { Key(supplier: 99) }, commercialEvidence: true);

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.FinancialEvidenceExists, d.BlockReasonCode);
    }

    [Fact]
    public void Operation_invoice_activity_blocks_a_commercial_change_too()
    {
        var d = Plan(Key(supplier: 10), new[] { Key(supplier: 99) },
            operationInvoiceActivity: true);

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.FinancialEvidenceExists, d.BlockReasonCode);
    }

    [Fact]
    public void A_captured_expected_total_blocks_a_currency_change()
    {
        // The captured amount is denominated in the captured currency; relabelling the group's
        // currency would falsify the snapshot.
        var d = Plan(Key(currency: "AOA"), new[] { Key(currency: "USD") },
            capturedExpectedTotal: true);

        Assert.Equal(PoGroupReclassificationAction.Blocked, d.Action);
        Assert.Equal(Reasons.FinancialEvidenceExists, d.BlockReasonCode);
    }

    [Fact]
    public void A_currency_change_with_nothing_captured_and_no_evidence_is_restamped()
    {
        var d = Plan(Key(currency: "AOA", type: Types.Invoice),
            new[] { Key(currency: "USD", type: Types.Invoice) });

        Assert.Equal(PoGroupReclassificationAction.Restamp, d.Action);
        Assert.Equal("USD", d.NewKey!.Value.CurrencyCode);
    }

    [Fact]
    public void A_captured_expected_total_does_not_block_a_plant_or_supplier_change()
    {
        // The snapshot is an amount in a currency; supplier and plant do not denominate it.
        var d = Plan(Key(plant: 1), new[] { Key(plant: 2) }, capturedExpectedTotal: true);

        Assert.Equal(PoGroupReclassificationAction.Restamp, d.Action);
        Assert.Equal(2, d.NewKey!.Value.PlantId);
    }

    [Fact]
    public void A_type_only_change_is_not_blocked_by_commercial_evidence()
    {
        // Phase 1c semantics preserved deliberately: a P.O. documents the commercial identity
        // (who, what currency, which plant) — not the obligation the type derives. Correcting
        // PROFORMA→INVOICE under a registered P.O. fixes the obligation without touching what
        // the P.O. says.
        var d = Plan(Key(type: Types.Proforma), new[] { Key(type: Types.Invoice) },
            commercialEvidence: true);

        Assert.Equal(PoGroupReclassificationAction.Restamp, d.Action);
    }

    [Fact]
    public void Each_group_is_judged_independently()
    {
        var okGroup = Guid.NewGuid();
        var mixedGroup = Guid.NewGuid();

        var decisions = PoGroupReclassificationPlanner.Plan(new[]
        {
            new PoGroupReclassificationInput
            {
                GroupId = okGroup,
                CurrentGroupKey = Key(type: Types.Proforma),
                ContributingKeys = new[] { Key(type: Types.Invoice) }
            },
            new PoGroupReclassificationInput
            {
                GroupId = mixedGroup,
                CurrentGroupKey = Key(type: Types.Proforma),
                ContributingKeys = new[] { Key(type: Types.Proforma), Key(type: Types.Invoice) }
            }
        });

        Assert.Equal(PoGroupReclassificationAction.Restamp,
            decisions.Single(x => x.GroupId == okGroup).Action);
        Assert.Equal(PoGroupReclassificationAction.Blocked,
            decisions.Single(x => x.GroupId == mixedGroup).Action);
    }
}
