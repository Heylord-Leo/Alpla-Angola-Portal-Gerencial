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

    // ════ v2.226.1 — Document-summary IVA credit (summary-only tax must not read as unexplained) ════

    /// <summary>A line whose OCR baseline has NO extracted IVA rate (null, not 0) — the
    /// summary-only-tax document shape. The final rate is the buyer-confirmed one.</summary>
    private static ReconciliationLineInput SummaryIvaLine(
        int line, decimal oQty, decimal oUp, decimal? oLineTotal, decimal fIva,
        decimal? fQty = null, decimal? fUp = null)
        => new()
        {
            LineNumber = line, ReconciliationStatus = "MAPPED", HasOcrBaseline = true,
            OcrQuantity = oQty, OcrUnitPrice = oUp, OcrDiscount = 0m, OcrIvaPercent = null,
            OcrLineTotal = oLineTotal,
            FinalQuantity = fQty ?? oQty, FinalUnitPrice = fUp ?? oUp, FinalDiscount = 0m,
            FinalIvaPercent = fIva
        };

    [Fact] // Reproduction A: net lines 1,323,000; summary IVA 14%; header 1,508,220 → residual 0
    public void SummaryIva_A_FullyExplainedDocument_ResidualZero_NoBlocker()
    {
        var lines = new List<ReconciliationLineInput>
        {
            SummaryIvaLine(1, oQty: 1, oUp: 1_323_000m, oLineTotal: 1_323_000m, fIva: 14m)
        };
        var final = PreGlobal(lines); // 1,508,220
        var r = QuotationReconciliationCalculator.Compute(1_508_220m, lines, final, 0m, Tol);

        // The structural diagnostic stays visible and non-zero — it is not the blocker.
        Assert.Equal(185_220m, r.StructuralHeaderDifference);
        Assert.Equal(0m, r.OcrLineComponentDifference);
        Assert.Equal(185_220m, r.IvaImpact);                    // buyer-side explanation, unchanged
        Assert.Equal(185_220m, r.DocumentSummaryIvaCredit);     // the document's own summary tax
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);               // no justification required
    }

    [Fact] // Regression B: same document but header 1,510,000 → credit granted, residual 1,780 blocks
    public void SummaryIva_B_HeaderExceedsReconstruction_TrueResidualStillBlocks()
    {
        var lines = new List<ReconciliationLineInput>
        {
            SummaryIvaLine(1, 1, 1_323_000m, 1_323_000m, fIva: 14m)
        };
        var r = QuotationReconciliationCalculator.Compute(1_510_000m, lines, PreGlobal(lines), 0m, Tol);

        Assert.Equal(185_220m, r.DocumentSummaryIvaCredit);
        Assert.Equal(1_780m, r.ResidualVariance);
        Assert.True(r.ResidualExceedsTolerance);
    }

    [Fact] // Regression C: per-line IVA WAS extracted → reconstruction already carries it, credit 0
    public void SummaryIva_C_ExtractedPerLineIva_NoCredit_NoDoubleCount()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 1, 1_323_000m, 0, oIva: 14m, oLineTotal: 1_508_220m,
                fQty: 1, fUp: 1_323_000m, fDisc: 0, fIva: 14m)
        };
        var r = QuotationReconciliationCalculator.Compute(1_508_220m, lines, PreGlobal(lines), 0m, Tol);

        Assert.Equal(0m, r.DocumentSummaryIvaCredit);
        Assert.Equal(0m, r.StructuralHeaderDifference);
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);
    }

    [Fact] // Regression D (mandatory false-positive guard): tax-free document, null line rates,
           // no final rate either → no artificial credit, residual 0
    public void SummaryIva_D_GenuinelyTaxFreeDocument_NoArtificialCredit()
    {
        var lines = new List<ReconciliationLineInput>
        {
            SummaryIvaLine(1, 1, 1_323_000m, 1_323_000m, fIva: 0m)
        };
        var r = QuotationReconciliationCalculator.Compute(1_323_000m, lines, PreGlobal(lines), 0m, Tol);

        Assert.Equal(0m, r.DocumentSummaryIvaCredit);
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);
    }

    [Fact] // Regression E: tax-free document (header == net lines) but the buyer selects 14% —
           // the rate is a BUYER adjustment, never claimed as document summary tax
    public void SummaryIva_E_BuyerAddedRateOnTaxFreeDocument_IsNotDocumentTax()
    {
        var lines = new List<ReconciliationLineInput>
        {
            SummaryIvaLine(1, 1, 1_323_000m, 1_323_000m, fIva: 14m)
        };
        // Header equals the net lines: crediting 185,220 would WORSEN consistency, so the
        // improvement guard refuses it.
        var r = QuotationReconciliationCalculator.Compute(1_323_000m, lines, PreGlobal(lines), 0m, Tol);

        Assert.Equal(0m, r.DocumentSummaryIvaCredit);
        Assert.Equal(185_220m, r.IvaImpact);        // still visible as a buyer-side adjustment
        Assert.Equal(0m, r.ResidualVariance);       // header ↔ final reconcile through the bucket
        Assert.False(r.ResidualExceedsTolerance);
    }

    [Fact] // Regression F: buyer removes the inferred 14% → credit gone, residual returns, blocker
    public void SummaryIva_F_RemovedInferredRate_ResidualReturns()
    {
        var lines = new List<ReconciliationLineInput>
        {
            SummaryIvaLine(1, 1, 1_323_000m, 1_323_000m, fIva: 0m)
        };
        var r = QuotationReconciliationCalculator.Compute(1_508_220m, lines, PreGlobal(lines), 0m, Tol);

        Assert.Equal(0m, r.DocumentSummaryIvaCredit);
        Assert.Equal(185_220m, r.ResidualVariance);
        Assert.True(r.ResidualExceedsTolerance);
    }

    [Fact] // Regression G: buyer changes the rate to 10% → partial defensible credit, the document
           // does not falsely reconcile, the remaining residual blocks
    public void SummaryIva_G_WrongRate_PartialCredit_RemainingResidualBlocks()
    {
        var lines = new List<ReconciliationLineInput>
        {
            SummaryIvaLine(1, 1, 1_323_000m, 1_323_000m, fIva: 10m)
        };
        var r = QuotationReconciliationCalculator.Compute(1_508_220m, lines, PreGlobal(lines), 0m, Tol);

        Assert.Equal(132_300m, r.DocumentSummaryIvaCredit);   // 10% of the original net
        Assert.Equal(52_920m, r.ResidualVariance);            // the unreconciled remainder
        Assert.True(r.ResidualExceedsTolerance);
    }

    [Fact] // Regression H: mixed document — one line with extracted IVA, one summary-only line;
           // only the missing-baseline line participates in the credit
    public void SummaryIva_H_MixedLines_CreditOnlyForMissingBaseline()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 1, 100m, 0, oIva: 14m, oLineTotal: 114m,
                fQty: 1, fUp: 100m, fDisc: 0, fIva: 14m),           // extracted → contributes 0
            SummaryIvaLine(2, 1, 100m, 100m, fIva: 14m)              // summary-only → credited 14
        };
        var r = QuotationReconciliationCalculator.Compute(228m, lines, PreGlobal(lines), 0m, Tol);

        Assert.Equal(14m, r.DocumentSummaryIvaCredit);
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);
    }

    [Fact] // The credit survives buyer quantity edits: it is computed on ORIGINAL components
    public void SummaryIva_CreditUsesOriginalComponents_NotBuyerEditedOnes()
    {
        var lines = new List<ReconciliationLineInput>
        {
            // Buyer halves the quantity; the document's own tax is still 14% of the ORIGINAL net.
            SummaryIvaLine(1, oQty: 2, oUp: 661_500m, oLineTotal: 1_323_000m, fIva: 14m, fQty: 1)
        };
        var final = PreGlobal(lines); // 1 × 661,500 × 1.14 = 754,110
        var r = QuotationReconciliationCalculator.Compute(1_508_220m, lines, final, 0m, Tol);

        Assert.Equal(185_220m, r.DocumentSummaryIvaCredit);   // 14% of 1,323,000, not of 661,500
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);
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
