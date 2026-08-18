using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Which PO group a payment line belongs to.
///
/// <para>Release 3 adds <b>Plant</b> and <b>SourceDocumentType</b> to the key the quotation path
/// already used. The type is the interesting one: without it, a Pró-forma line and a Factura line
/// from the same supplier for the same plant would share a group that owes an operation invoice for
/// part of its value and not the rest — an obligation the model cannot express.</para>
/// </summary>
public class PaymentGroupingKeyTests
{
    private static PaymentGroupingKey Key(
        int? supplier = 1,
        string? currency = "AOA",
        string? condition = "POST_PAID",
        int? plant = 10,
        string? type = "PROFORMA") =>
        PaymentGroupingKey.From(supplier, currency, condition, plant, type);

    [Fact]
    public void Identical_documents_share_one_group()
    {
        Assert.Equal(Key(), Key());
    }

    // ── Each dimension splits ──

    [Fact]
    public void A_different_supplier_splits_the_group()
    {
        Assert.NotEqual(Key(supplier: 1), Key(supplier: 2));
    }

    [Fact]
    public void A_different_currency_splits_the_group()
    {
        Assert.NotEqual(Key(currency: "AOA"), Key(currency: "USD"));
    }

    [Fact]
    public void A_different_payment_condition_splits_the_group()
    {
        Assert.NotEqual(Key(condition: "POST_PAID"), Key(condition: "ADVANCE_FULL"));
    }

    [Fact]
    public void Two_plants_split_the_group_even_for_one_supplier()
    {
        // The Viana 1 / Viana 2 case: one supplier, one service, two sites receiving separately.
        Assert.NotEqual(Key(plant: 10), Key(plant: 20));
    }

    [Fact]
    public void Two_document_types_split_the_group_even_for_one_supplier_and_plant()
    {
        // A proforma owes an operation invoice; a factura does not. One group cannot owe both.
        Assert.NotEqual(Key(type: "PROFORMA"), Key(type: "INVOICE"));
    }

    [Fact]
    public void Same_supplier_and_plant_with_the_same_type_stay_together()
    {
        Assert.Equal(Key(supplier: 7, plant: 3, type: "INVOICE"),
                     Key(supplier: 7, plant: 3, type: "INVOICE"));
    }

    // ── Normalization: a group must never split on spelling ──

    [Fact]
    public void Casing_and_padding_never_split_a_group()
    {
        Assert.Equal(
            PaymentGroupingKey.From(1, "AOA", "POST_PAID", 10, "PROFORMA"),
            PaymentGroupingKey.From(1, " aoa ", "post_paid", 10, " proforma "));
    }

    [Fact]
    public void The_superseded_invoice_alias_does_not_split_a_group()
    {
        // FINAL_INVOICE was the old name for INVOICE. Treating them as different types would
        // manufacture a second group out of a rename.
        Assert.Equal(Key(type: "INVOICE"), Key(type: "FINAL_INVOICE"));
    }

    [Fact]
    public void Blank_components_collapse_to_null_rather_than_to_an_empty_string()
    {
        var blank = PaymentGroupingKey.From(null, "   ", "", null, null);

        Assert.Null(blank.CurrencyCode);
        Assert.Null(blank.PaymentConditionCode);
        Assert.Null(blank.SourceDocumentType);
        Assert.Equal(blank, PaymentGroupingKey.From(null, null, null, null, null));
    }

    [Fact]
    public void The_label_names_every_dimension_for_history_and_logs()
    {
        var text = Key(supplier: 42, currency: "AOA", condition: "POST_PAID", plant: 7, type: "INVOICE")
            .ToString();

        Assert.Contains("supplier=42", text);
        Assert.Contains("currency=AOA", text);
        Assert.Contains("plant=7", text);
        Assert.Contains("document=INVOICE", text);
    }
}
