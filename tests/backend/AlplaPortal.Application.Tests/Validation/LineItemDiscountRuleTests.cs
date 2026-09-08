using AlplaPortal.Application.Validation;
using Xunit;

namespace AlplaPortal.Application.Tests.Validation;

/// <summary>
/// v2.241.0 discount hardening: a line discount may never be negative (a negative discount inflates
/// the total — the legacy monetary-scale defect) nor exceed the gross subtotal.
/// </summary>
public class LineItemDiscountRuleTests
{
    [Fact]
    public void Null_discount_is_accepted()
        => Assert.Null(LineItemDiscountRule.Validate(4000m, 4600m, null));

    [Fact]
    public void Zero_discount_is_accepted()
        => Assert.Null(LineItemDiscountRule.Validate(4000m, 4600m, 0m));

    [Fact]
    public void Valid_in_range_discount_is_accepted()
        => Assert.Null(LineItemDiscountRule.Validate(4000m, 4600m, 100m));

    [Fact]
    public void Discount_equal_to_subtotal_is_accepted()
        => Assert.Null(LineItemDiscountRule.Validate(10m, 10m, 100m)); // subtotal 100

    [Fact]
    public void Negative_discount_is_rejected()
        => Assert.Equal(LineItemDiscountRule.NegativeMessage,
            LineItemDiscountRule.Validate(4000m, 4600m, -18_381_600_000m));

    [Fact]
    public void Discount_above_subtotal_is_rejected()
        => Assert.Equal(LineItemDiscountRule.ExceedsSubtotalMessage,
            LineItemDiscountRule.Validate(4000m, 4600m, 18_400_001m));
}
