using System;

namespace AlplaPortal.Application.Validation;

/// <summary>
/// Backend-authoritative guard for a line item's monetary discount (v2.241.0 hardening — the
/// defensive follow-up to the legacy monetary-scale defect, incident REQ-11/08/2026-228).
///
/// <para>A discount may never be NEGATIVE (a negative discount inflates the line total, which is
/// exactly how <c>TotalAmount = (Quantity*UnitPrice) - DiscountAmount</c> produced the x1000 value),
/// and may never EXCEED the gross subtotal. Frontend validation may mirror this, but this is the
/// authoritative check.</para>
/// </summary>
public static class LineItemDiscountRule
{
    public const string NegativeMessage = "O desconto não pode ser negativo.";
    public const string ExceedsSubtotalMessage = "O desconto não pode ser superior ao subtotal do item.";

    /// <summary>Returns the first violated message, or null when the discount is acceptable
    /// (including a null/absent discount).</summary>
    public static string? Validate(decimal quantity, decimal unitPrice, decimal? discountAmount)
    {
        if (discountAmount is null) return null;
        var discount = discountAmount.Value;
        if (discount < 0) return NegativeMessage;
        var subtotal = Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        if (discount > subtotal) return ExceedsSubtotalMessage;
        return null;
    }
}
