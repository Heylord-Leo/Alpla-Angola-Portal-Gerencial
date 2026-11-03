using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Services;
using Xunit;
using static AlplaPortal.Domain.Services.QuotationLinePricingPolicy;

namespace AlplaPortal.Application.Tests.Validation;

/// <summary>
/// v2.249.6 — the pure line-resolved pricing rule, on the exact REQ-21/10/2026-595 (LUANDAY proforma) figures.
/// Document: 150 CX × 1.390,00 (desconto 37.530,00) and 100 CX × 1.885,00 (desconto 30.160,00), IVA 5 %:
/// before discounts 397.000,00; discounts 67.690,00; taxable 329.310,00; IVA 16.465,50; payable 345.775,50.
/// </summary>
public class QuotationLinePricingPolicyTests
{
    private static LineFacts L(decimal lineTotal, string status = "MAPPED") => new(status, lineTotal);

    [Fact] // The persisted TEST shape: lines without discount (IVA on gross), discount only at quotation level.
    public void REQ_595_shape_global_discount_only_is_not_line_resolved()
    {
        var e = Evaluate(quotationTotal: 345_775.50m, globalDiscount: 67_690m, lines: new[] { L(218_925m), L(197_925m) });

        Assert.False(e.IsLineResolved);
        Assert.Equal(Reasons.GlobalDiscountUnallocated, e.ReasonCode);
        Assert.Equal(416_850m, e.ConsideredLineTotalSum);      // what approval froze in TEST
        Assert.Equal(345_775.50m, e.QuotationTotal);           // what the quotation card showed
        Assert.Equal(71_074.50m, e.Difference);                // 67.690,00 + 5 % IVA on it
    }

    [Fact] // The supported shape: the document's per-line discounts on the lines, no global discount.
    public void REQ_595_shape_per_line_discounts_is_line_resolved_and_adds_up_to_the_payable_total()
    {
        var e = Evaluate(345_775.50m, 0m, new[] { L(179_518.50m), L(166_257m) });

        Assert.True(e.IsLineResolved);
        Assert.Null(e.ReasonCode);
        Assert.Equal(345_775.50m, e.ConsideredLineTotalSum);
        Assert.Equal(0m, e.Difference);
    }

    [Fact] // A global discount is unresolved even when it is tiny and even for a one-line quotation (no "share" is defined).
    public void Any_positive_global_discount_is_unresolved_regardless_of_size_or_line_count()
    {
        Assert.Equal(Reasons.GlobalDiscountUnallocated, Evaluate(104.50m, 0.50m, new[] { L(105m) }).ReasonCode);
        Assert.Equal(Reasons.GlobalDiscountUnallocated, Evaluate(0m, 10m, new List<LineFacts>()).ReasonCode);
    }

    [Fact] // IGNORED and NOT_QUOTED rows never compose the quotation value (same scope as SaveQuotation's totals).
    public void Ignored_and_not_quoted_lines_are_excluded_from_the_considered_sum()
    {
        var e = Evaluate(300m, 0m, new[] { L(100m), L(200m, "SUBSTITUTE"), L(999m, "IGNORED"), L(0m, "NOT_QUOTED"), L(50m, "EXTRA_ITEM") });
        Assert.Equal(350m, e.ConsideredLineTotalSum);       // 100 + 200 + 50
        Assert.False(e.IsLineResolved);                       // 350 ≠ 300 → mismatch, not a global discount
        Assert.Equal(Reasons.LineTotalMismatch, e.ReasonCode);
        Assert.Equal(50m, e.Difference);
    }

    [Fact] // Legacy status null counts as MAPPED (entity default).
    public void A_missing_reconciliation_status_counts_as_considered()
    {
        Assert.True(IsConsidered(null));
        Assert.True(IsConsidered(""));
        Assert.False(IsConsidered("IGNORED"));
        var e = Evaluate(150m, 0m, new[] { new LineFacts(null, 150m) });
        Assert.True(e.IsLineResolved);
    }

    [Theory] // Rounding drift within 1,00 is tolerated for legacy rows; beyond it is a mismatch.
    [InlineData(1000.00, 1000.99, true)]
    [InlineData(1000.00, 999.01, true)]
    [InlineData(1000.00, 1001.01, false)]
    [InlineData(1000.00, 998.99, false)]
    public void Line_sum_tolerance_is_one_unit_absolute(decimal total, decimal lineSum, bool resolved)
    {
        var e = Evaluate(total, 0m, new[] { L(lineSum) });
        Assert.Equal(resolved, e.IsLineResolved);
        Assert.Equal(resolved ? null : Reasons.LineTotalMismatch, e.ReasonCode);
    }

    [Fact] // A zero total is classified explicitly: LEGACY (positive lines, total never computed) vs ZERO_VALUE; a global discount still blocks.
    public void A_zero_quotation_total_is_classified_as_legacy_or_zero_value_never_as_line_resolved_pricing()
    {
        var legacy = Evaluate(0m, 0m, new[] { L(253_080m), L(660_060m) });
        Assert.True(legacy.IsLineResolved);                                   // compatibility: approval values the lines (913.140,00)
        Assert.Equal(Kinds.LegacyTotalNotComputed, legacy.Kind);              // …but it is never mistaken for computed pricing
        Assert.Equal(913_140m, legacy.ConsideredLineTotalSum);

        var zero = Evaluate(0m, 0m, new[] { L(0m), L(0m, "IGNORED") });
        Assert.True(zero.IsLineResolved);
        Assert.Equal(Kinds.ZeroValue, zero.Kind);

        var unresolved = Evaluate(0m, 5m, new[] { L(253_080m) });
        Assert.False(unresolved.IsLineResolved);
        Assert.Equal(Kinds.GlobalDiscountUnallocated, unresolved.Kind);
    }

    [Fact] // Every evaluation carries its kind; the unresolved kinds equal their reason codes.
    public void Kinds_are_explicit_for_resolved_and_unresolved_evaluations()
    {
        Assert.Equal(Kinds.LineResolved, Evaluate(345_775.50m, 0m, new[] { L(179_518.50m), L(166_257m) }).Kind);
        Assert.Equal(Kinds.GlobalDiscountUnallocated, Evaluate(345_775.50m, 67_690m, new[] { L(218_925m), L(197_925m) }).Kind);
        Assert.Equal(Kinds.LineTotalMismatch, Evaluate(1_404_060m, 0m, new[] { L(753_080m), L(755_140m) }).Kind);
        Assert.Equal(Reasons.GlobalDiscountUnallocated, Kinds.GlobalDiscountUnallocated);
        Assert.Equal(Reasons.LineTotalMismatch, Kinds.LineTotalMismatch);
    }

    [Theory] // Write-path guard: a global discount above the net of the considered lines (after their own discounts) is refused, never clamped.
    [InlineData(329_310.00, 329_310.00, false)]   // equal to the net: a 100 % discount, arithmetically valid
    [InlineData(329_310.00, 329_310.01, true)]
    [InlineData(329_310.00, 400_000.00, true)]
    [InlineData(0.00, 0.01, true)]                // nothing left to discount
    [InlineData(329_310.00, 0.00, false)]
    public void A_global_discount_above_the_remaining_net_is_an_arithmetic_refusal(decimal netAfterItemDiscounts, decimal globalDiscount, bool refused)
    {
        Assert.Equal(refused, GlobalDiscountExceedsNet(netAfterItemDiscounts, globalDiscount));
    }

    [Fact] // Per-line IVA rounding on odd values: Σ Round2 lines is exactly the quotation total written by SaveQuotation's arithmetic.
    public void Per_line_rounding_never_produces_a_false_mismatch_for_component_computed_totals()
    {
        // 3 × 1.333,33 = 3.999,99; 7 × 0,37 = 2,59; 1 × 1.234,567 → gross Round2 = 1.234,57; IVA 14 % per line, Round2 each.
        var nets = new[] { 3_999.99m, 2.59m, 1_234.57m };
        var lines = nets.Select(n => L(n + System.Math.Round(n * 0.14m, 2, System.MidpointRounding.AwayFromZero))).ToArray();
        var total = lines.Sum(l => l.LineTotal);              // SaveQuotation: taxableBase + Σ IvaAmount == Σ LineTotal
        var e = Evaluate(total, 0m, lines);
        Assert.True(e.IsLineResolved);
        Assert.Equal(0m, e.Difference);
    }
}
