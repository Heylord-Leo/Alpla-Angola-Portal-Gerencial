using System.Collections.Generic;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Validation;
using Xunit;

namespace AlplaPortal.Application.Tests.Validation;

public class RequestLineItemSubmissionValidatorTests
{
    private static readonly RequestLineItemSubmissionValidator Validator = new();
    private static readonly HashSet<int> ValidUnitIds = new() { 1, 2 };

    private static LineItemCandidate Item(
        string? description = "Item",
        decimal quantity = 5,
        int? unitId = 1,
        decimal lineTotal = 100,
        bool isDeleted = false,
        int index = 0)
        => new() { Index = index, Description = description, Quantity = quantity, UnitId = unitId, LineTotal = lineTotal, IsDeleted = isDeleted };

    // ─────────────────────────── Cotação (CreateRequest) ───────────────────────────

    [Fact] // 1a
    public void Quotation_NullCollection_Rejects()
        => Assert.False(Validator.ValidateQuotation(null, ValidUnitIds).IsValid);

    [Fact] // 1b
    public void Quotation_EmptyCollection_Rejects()
        => Assert.False(Validator.ValidateQuotation(new List<LineItemCandidate>(), ValidUnitIds).IsValid);

    [Fact] // 2
    public void Quotation_EmptyDescription_Rejects()
        => Assert.False(Validator.ValidateQuotation(new[] { Item(description: "") }, ValidUnitIds).IsValid);

    [Fact] // 3
    public void Quotation_WhitespaceDescription_Rejects()
        => Assert.False(Validator.ValidateQuotation(new[] { Item(description: "   ") }, ValidUnitIds).IsValid);

    [Fact] // 4
    public void Quotation_ZeroQuantity_Rejects()
        => Assert.False(Validator.ValidateQuotation(new[] { Item(quantity: 0) }, ValidUnitIds).IsValid);

    [Fact] // 5
    public void Quotation_NegativeQuantity_Rejects()
        => Assert.False(Validator.ValidateQuotation(new[] { Item(quantity: -1) }, ValidUnitIds).IsValid);

    [Fact] // 6
    public void Quotation_MissingUnit_Rejects()
        => Assert.False(Validator.ValidateQuotation(new[] { Item(unitId: null) }, ValidUnitIds).IsValid);

    [Fact] // 7
    public void Quotation_NonExistentUnit_Rejects()
        => Assert.False(Validator.ValidateQuotation(new[] { Item(unitId: 999) }, ValidUnitIds).IsValid);

    [Fact] // 8
    public void Quotation_OneValidItem_Accepts()
        => Assert.True(Validator.ValidateQuotation(new[] { Item() }, ValidUnitIds).IsValid);

    [Fact] // 9
    public void Quotation_MultipleValid_Accepts()
        => Assert.True(Validator.ValidateQuotation(new[] { Item(index: 0), Item(index: 1, unitId: 2) }, ValidUnitIds).IsValid);

    [Fact] // 10
    public void Quotation_OneValidOneInvalid_RejectsWholeSet()
    {
        var result = Validator.ValidateQuotation(new[] { Item(index: 0), Item(index: 1, quantity: 0) }, ValidUnitIds);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ItemIndex == 1); // the invalid line is pinpointed
    }

    // ─────────────────────────── Pagamento (Submit) ───────────────────────────

    [Fact] // 11
    public void Payment_NoActiveItems_Rejects()
        => Assert.False(Validator.ValidatePaymentSubmit(new List<LineItemCandidate>(), ValidUnitIds).IsValid);

    [Fact] // 12
    public void Payment_DeletedItemDoesNotCount()
    {
        // A deleted invalid item is ignored; a single valid active item passes.
        var result = Validator.ValidatePaymentSubmit(new[]
        {
            Item(index: 0, description: "", isDeleted: true),
            Item(index: 1)
        }, ValidUnitIds);
        Assert.True(result.IsValid);
    }

    [Fact] // 13
    public void Payment_EmptyDescription_Rejects()
        => Assert.False(Validator.ValidatePaymentSubmit(new[] { Item(description: "") }, ValidUnitIds).IsValid);

    [Fact] // 14
    public void Payment_ZeroQuantity_Rejects()
        => Assert.False(Validator.ValidatePaymentSubmit(new[] { Item(quantity: 0) }, ValidUnitIds).IsValid);

    [Fact] // 15
    public void Payment_MissingUnit_Rejects()
        => Assert.False(Validator.ValidatePaymentSubmit(new[] { Item(unitId: null) }, ValidUnitIds).IsValid);

    [Fact] // 16
    public void Payment_NonExistentUnit_Rejects()
        => Assert.False(Validator.ValidatePaymentSubmit(new[] { Item(unitId: 999) }, ValidUnitIds).IsValid);

    [Fact] // 17
    public void Payment_ZeroTotal_Rejects()
        => Assert.False(Validator.ValidatePaymentSubmit(new[] { Item(lineTotal: 0) }, ValidUnitIds).IsValid);

    [Fact] // 18
    public void Payment_NegativeTotal_Rejects()
        => Assert.False(Validator.ValidatePaymentSubmit(new[] { Item(lineTotal: -50) }, ValidUnitIds).IsValid);

    [Fact] // 19
    public void Payment_OneValidOneZeroed_Rejects()
    {
        var result = Validator.ValidatePaymentSubmit(new[] { Item(index: 0), Item(index: 1, lineTotal: 0) }, ValidUnitIds);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ItemIndex == 1);
    }

    [Fact] // 20
    public void Payment_AllValid_Accepts()
        => Assert.True(Validator.ValidatePaymentSubmit(new[] { Item(index: 0), Item(index: 1, unitId: 2) }, ValidUnitIds).IsValid);
}
