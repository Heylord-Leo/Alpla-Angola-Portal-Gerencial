using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.Validation;
using Xunit;

namespace AlplaPortal.Application.Tests.Validation;

/// <summary>
/// Pure-math coverage of the signed-residual reconciliation calculator, including the approved
/// worked Examples A–E. The residual is anchored to the OCR document's internal consistency
/// (OcrHeaderTotal − ReconstructedOcrLineSum) and is independent of the buyer's final values, so it
/// is never zero by construction.
/// </summary>
public class QuotationReconciliationCalculatorTests
{
    private const decimal Tol = 2.00m;

    private static ReconciliationLineInput OcrLine(
        int line, string status, decimal oQty, decimal oUp, decimal oDisc, decimal oIva, decimal? oLineTotal,
        decimal fQty, decimal fUp, decimal fDisc, decimal fIva, bool hasAdjReason = false, bool hasReconReason = false,
        int? oUnitId = null, int? fUnitId = null)
        => new()
        {
            LineNumber = line, ReconciliationStatus = status, HasOcrBaseline = true,
            OcrQuantity = oQty, OcrUnitPrice = oUp, OcrDiscount = oDisc, OcrIvaPercent = oIva,
            OcrLineTotal = oLineTotal, OcrUnitId = oUnitId,
            FinalQuantity = fQty, FinalUnitPrice = fUp, FinalDiscount = fDisc, FinalIvaPercent = fIva, FinalUnitId = fUnitId,
            HasAdjustmentReason = hasAdjReason, HasReconciliationReason = hasReconReason
        };

    private static ReconciliationLineInput ManualLine(int line, string status, decimal fQty, decimal fUp, bool hasReconReason)
        => new()
        {
            LineNumber = line, ReconciliationStatus = status, HasOcrBaseline = false, IsManualAddition = true,
            FinalQuantity = fQty, FinalUnitPrice = fUp, FinalDiscount = 0, FinalIvaPercent = 0,
            HasReconciliationReason = hasReconReason
        };

    /// <summary>Pre-global considered total (net+IVA), no global discount in these examples.</summary>
    private static decimal PreGlobal(IEnumerable<ReconciliationLineInput> lines)
        => lines.Where(l => l.ReconciliationStatus is "MAPPED" or "SUBSTITUTE" or "EXTRA_ITEM")
                .Sum(l => System.Math.Max(0, l.FinalQuantity * l.FinalUnitPrice - l.FinalDiscount)
                          + System.Math.Max(0, l.FinalQuantity * l.FinalUnitPrice - l.FinalDiscount) * l.FinalIvaPercent / 100m);

    [Fact] // Example A: header = line sum; qty 6→1; one ignored; residual 0
    public void ExampleA_QuantitySixToOne_OneIgnored_ResidualZero()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "SUBSTITUTE", 6, 100, 0, 0, 600, fQty: 1, fUp: 100, fDisc: 0, fIva: 0, hasAdjReason: true),
            OcrLine(2, "IGNORED",    1, 400, 0, 0, 400, fQty: 1, fUp: 400, fDisc: 0, fIva: 0, hasReconReason: true),
        };
        var final = PreGlobal(lines); // = 100
        var r = QuotationReconciliationCalculator.Compute(1000m, lines, final, 0m, Tol);

        Assert.Equal(0m, r.StructuralHeaderDifference);
        Assert.Equal(0m, r.OcrLineComponentDifference);
        Assert.Equal(-400m, r.IgnoredImpact);
        Assert.Equal(-500m, r.QuantityImpact);
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);
        Assert.True(r.Lines.Single(l => l.LineNumber == 1).RequiresAdjustmentReason);
    }

    [Fact] // Example B: header exceeds line sum by 50, no explanation; signed residual 50
    public void ExampleB_StructuralDifference_ResidualFifty()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 6, 100, 0, 0, 600, 6, 100, 0, 0),
            OcrLine(2, "MAPPED", 1, 400, 0, 0, 400, 1, 400, 0, 0),
        };
        var final = PreGlobal(lines); // 1000
        var r = QuotationReconciliationCalculator.Compute(1050m, lines, final, 0m, Tol);

        Assert.Equal(50m, r.StructuralHeaderDifference);
        Assert.Equal(0m, r.OcrLineComponentDifference);
        Assert.Equal(50m, r.ResidualVariance);
        Assert.True(r.ResidualExceedsTolerance);
    }

    [Fact] // Example D: OcrLineTotal ≠ reconstructed components → component bucket carries the diff
    public void ExampleD_ComponentDifference_DoesNotVanish()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 6, 100, 0, 0, oLineTotal: 610, fQty: 6, fUp: 100, fDisc: 0, fIva: 0), // reported 610 vs reconstructed 600
            OcrLine(2, "MAPPED", 1, 400, 0, 0, oLineTotal: 400, fQty: 1, fUp: 400, fDisc: 0, fIva: 0),
        };
        var final = PreGlobal(lines); // 1000
        var r = QuotationReconciliationCalculator.Compute(1010m, lines, final, 0m, Tol);

        Assert.Equal(0m, r.StructuralHeaderDifference);
        Assert.Equal(10m, r.OcrLineComponentDifference);
        Assert.Equal(10m, r.ResidualVariance);
    }

    [Fact] // Example E: new manual line in an OCR quotation appears in its own bucket; residual 0
    public void ExampleE_ManualAddition_SurfacedNotSilent_ResidualZero()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 2, 100, 0, 0, 200, 2, 100, 0, 0),
            ManualLine(2, "EXTRA_ITEM", fQty: 1, fUp: 50, hasReconReason: true),
        };
        var final = PreGlobal(lines); // 250
        var r = QuotationReconciliationCalculator.Compute(200m, lines, final, 0m, Tol);

        Assert.Equal(50m, r.ManualAdditionsImpact);
        Assert.Equal(50m, r.ManualAdditionsTotal);
        Assert.Equal(0m, r.ResidualVariance);
        var manual = r.Lines.Single(l => l.LineNumber == 2);
        Assert.True(manual.IsManualAddition);
        Assert.False(manual.RequiresAdjustmentReason); // EXTRA_ITEM reconciliation reason suffices
    }

    [Fact] // A MAPPED manual addition (no reconciliation reason) requires an origin/adjustment reason
    public void ManualAddition_Mapped_RequiresOriginReason()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 2, 100, 0, 0, 200, 2, 100, 0, 0),
            ManualLine(2, "MAPPED", fQty: 1, fUp: 50, hasReconReason: false),
        };
        var r = QuotationReconciliationCalculator.Compute(200m, lines, PreGlobal(lines), 0m, Tol);
        Assert.True(r.Lines.Single(l => l.LineNumber == 2).RequiresAdjustmentReason);
    }

    [Fact] // Negative signed residual is preserved (header < reconstructed lines); Abs gates
    public void NegativeResidual_IsSignedAndGated()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 1, 1000, 0, 0, 1000, 1, 1000, 0, 0),
        };
        var final = PreGlobal(lines); // 1000
        var r = QuotationReconciliationCalculator.Compute(940m, lines, final, 0m, Tol); // header 60 below lines
        Assert.Equal(-60m, r.ResidualVariance);          // signed, not absolute
        Assert.True(r.ResidualExceedsTolerance);         // Math.Abs(-60) > 2
    }

    [Fact] // Residual just within tolerance does not block
    public void ResidualWithinTolerance_DoesNotBlock()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 1, 1000, 0, 0, 1000, 1, 1000, 0, 0),
        };
        var final = PreGlobal(lines);
        var r = QuotationReconciliationCalculator.Compute(1001.50m, lines, final, 0m, Tol);
        Assert.Equal(1.50m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);
    }

    [Fact] // Null OcrLineTotal → imputed from components (flagged), never silently dropped
    public void NullOcrLineTotal_ImputedFromComponents_AndFlagged()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 2, 100, 0, 0, oLineTotal: null, fQty: 2, fUp: 100, fDisc: 0, fIva: 0),
        };
        var r = QuotationReconciliationCalculator.Compute(200m, lines, PreGlobal(lines), 0m, Tol);
        Assert.Equal(200m, r.OcrLineSumTotal);            // imputed from 2×100
        Assert.Equal(0m, r.OcrLineComponentDifference);
        Assert.Contains("lineTotal", r.Lines.Single().ImputedOcrComponents);
    }

    [Fact] // Incomplete OCR components (null discount/iva) → imputed as 0 but flagged
    public void NullComponents_ImputedZero_ButFlagged()
    {
        var lines = new List<ReconciliationLineInput>
        {
            new()
            {
                LineNumber = 1, ReconciliationStatus = "MAPPED", HasOcrBaseline = true,
                OcrQuantity = 2, OcrUnitPrice = 100, OcrDiscount = null, OcrIvaPercent = null, OcrLineTotal = 200,
                FinalQuantity = 2, FinalUnitPrice = 100, FinalDiscount = 0, FinalIvaPercent = 0
            }
        };
        var r = QuotationReconciliationCalculator.Compute(200m, lines, PreGlobal(lines), 0m, Tol);
        var line = r.Lines.Single();
        Assert.Contains("discount", line.ImputedOcrComponents);
        Assert.Contains("ivaPercent", line.ImputedOcrComponents);
    }

    [Fact] // Price-only change flags the price field; explained buckets reconcile to final − reconstructed
    public void PriceOnlyChange_FlaggedAndBucketed()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 1, 100, 0, 0, 100, fQty: 1, fUp: 130, fDisc: 0, fIva: 0, hasAdjReason: true),
        };
        var final = PreGlobal(lines); // 130
        var r = QuotationReconciliationCalculator.Compute(100m, lines, final, 0m, Tol);
        Assert.Equal(30m, r.UnitPriceImpact);
        Assert.Equal(0m, r.QuantityImpact);
        Assert.True(r.Lines.Single().UnitPriceChanged);
        // buckets reconcile to (final − reconstructed) = 130 − 100 = 30
        Assert.Equal(30m, r.ExplainedLineAdjustments);
        Assert.Equal(0m, r.ResidualVariance);
    }

    [Fact] // Unit-of-measure change (any) flags the line as requiring a reason
    public void UnitChange_RequiresReason()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 1, 100, 0, 0, 100, fQty: 1, fUp: 100, fDisc: 0, fIva: 0, oUnitId: 5, fUnitId: 9),
        };
        var r = QuotationReconciliationCalculator.Compute(100m, lines, PreGlobal(lines), 0m, Tol);
        var line = r.Lines.Single();
        Assert.True(line.UnitChanged);
        Assert.True(line.RequiresAdjustmentReason);
    }

    [Fact] // Global discount bucket uses the passed-through effect and reconciles the buckets
    public void GlobalDiscountBucket_IsApplied()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 1, 1000, 0, 0, 1000, 1, 1000, 0, 0),
        };
        decimal finalWithGlobal = 900m;         // 100 global discount applied
        decimal globalEffect = 900m - 1000m;    // -100
        var r = QuotationReconciliationCalculator.Compute(1000m, lines, finalWithGlobal, globalEffect, Tol);
        Assert.Equal(-100m, r.GlobalDiscountImpact);
        Assert.Equal(0m, r.ResidualVariance);   // header 1000 = reconstructed 1000
    }
}
