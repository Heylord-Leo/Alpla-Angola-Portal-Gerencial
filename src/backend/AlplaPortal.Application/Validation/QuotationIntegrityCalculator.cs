using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Application.Validation;

/// <summary>Result of the financial integrity comparison for a quotation save.</summary>
public sealed record QuotationIntegrityResult(
    decimal OcrOriginalTotal,
    decimal ExcludedIgnoredTotal,
    decimal ComparableDocumentTotal,
    decimal QuotationConsideredTotal,
    decimal VarianceAmount,
    decimal VariancePercent);

/// <summary>
/// Financial Integrity Gate math for SaveQuotation, extracted for testability.
///
/// The gate must compare totals of EQUIVALENT SCOPE: the OCR total covers the WHOLE document,
/// while the quotation total only covers reconciled lines (MAPPED/SUBSTITUTE/EXTRA_ITEM). Lines the
/// buyer explicitly reconciled as IGNORED are document lines deliberately excluded from the
/// quotation — their value is therefore subtracted from the OCR baseline to form the
/// comparable document total. (NOT_QUOTED entries represent requested items absent from the
/// document, so they affect neither side.) Real divergences between considered lines and the
/// document — prices, discounts, IVA, rounding — still trip the gate.
/// </summary>
public static class QuotationIntegrityCalculator
{
    public const decimal ToleranceAmount = 2.00m;

    private static readonly string[] ConsideredStatuses = RequestConstants.ReconciliationStatuses.Considered;

    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <param name="ocrBaseline">OCR-extracted total of the FULL document (dto.OcrTotal).</param>
    /// <param name="items">Quotation items with per-line totals and reconciliation status already computed.</param>
    /// <param name="globalDiscount">Commercial (quotation-level) discount.</param>
    /// <param name="netAfterItemDiscounts">Net total of ALL items after item-level discounts (basis for the proportional global-discount split).</param>
    public static QuotationIntegrityResult Compute(
        decimal ocrBaseline,
        IEnumerable<QuotationItem> items,
        decimal globalDiscount,
        decimal netAfterItemDiscounts)
    {
        var all = items.ToList();
        var considered = all.Where(i => ConsideredStatuses.Contains(i.ReconciliationStatus)).ToList();

        var consideredGross = considered.Sum(i => i.GrossSubtotal);
        var consideredDiscount = considered.Sum(i => i.DiscountAmount);
        var consideredNet = Round2(Math.Max(0, consideredGross - consideredDiscount));

        // Adjust the global discount proportionally to the considered items (unchanged behavior).
        decimal globalDiscountRatio = netAfterItemDiscounts > 0 ? (consideredNet / netAfterItemDiscounts) : 0m;
        decimal consideredGlobalDiscount = Round2(globalDiscount * globalDiscountRatio);
        decimal consideredTaxableBase = Round2(Math.Max(0, consideredNet - consideredGlobalDiscount));

        var consideredIvaAmount = considered.Sum(i => i.IvaAmount);
        decimal consideredAdjustedIva = Round2(consideredIvaAmount * (consideredNet > 0 ? (consideredTaxableBase / consideredNet) : 1m));

        decimal quotationConsideredTotal = Round2(consideredTaxableBase + consideredAdjustedIva);

        // Lines the buyer explicitly excluded from the quotation during reconciliation: their
        // document value must leave the baseline too, otherwise every IGNORED line becomes a
        // false divergence exactly equal to its own total.
        decimal excludedIgnoredTotal = Round2(all
            .Where(i => i.ReconciliationStatus == RequestConstants.ReconciliationStatuses.Ignored)
            .Sum(i => i.LineTotal));

        decimal comparableDocumentTotal = Round2(Math.Max(0, ocrBaseline - excludedIgnoredTotal));

        decimal varianceAmount = Math.Abs(comparableDocumentTotal - quotationConsideredTotal);
        decimal variancePercent = comparableDocumentTotal > 0
            ? Math.Round((varianceAmount / comparableDocumentTotal) * 100m, 2)
            : (varianceAmount > 0 ? 100m : 0m);

        return new QuotationIntegrityResult(
            OcrOriginalTotal: ocrBaseline,
            ExcludedIgnoredTotal: excludedIgnoredTotal,
            ComparableDocumentTotal: comparableDocumentTotal,
            QuotationConsideredTotal: quotationConsideredTotal,
            VarianceAmount: varianceAmount,
            VariancePercent: variancePercent);
    }
}
