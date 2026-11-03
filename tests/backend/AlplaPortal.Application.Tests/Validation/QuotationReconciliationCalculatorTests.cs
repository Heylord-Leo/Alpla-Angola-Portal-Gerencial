using System;
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
    // ════ v2.249.7 — Document-summary DISCOUNT credit (REQ-03/11/2026-630, synthetic fixture K) ════
    // Document: 10 × 5.000,00 and 20 × 500,00 (lines printed NET: 50.000 / 10.000), "Desconto 6.000,00" ONLY in the summary,
    // IVA 14 % on both lines, taxable 54.000, IVA 7.560, total 61.560. Proportional allocation: 5.000 / 1.000 → 51.300 / 10.260.

    private const decimal KHeader = 61_560m;
    private const decimal KDocumentDiscount = 6_000m;

    private static List<ReconciliationLineInput> KLines(decimal fDisc1, decimal fDisc2, bool reasons) => new()
    {
        OcrLine(1, "MAPPED", 10, 5_000, 0, 14, 50_000, fQty: 10, fUp: 5_000, fDisc: fDisc1, fIva: 14, hasAdjReason: reasons),
        OcrLine(2, "MAPPED", 20, 500, 0, 14, 10_000, fQty: 20, fUp: 500, fDisc: fDisc2, fIva: 14, hasAdjReason: reasons),
    };

    [Fact] // The TEST observation on v2.249.6: without the header-discount baseline the residual IS the discount's VAT-inclusive effect
    public void DocumentDiscount_K_WithoutHeaderBaseline_IsTheObservedMinus6840()
    {
        // After "Distribuir pelas linhas": line discounts 5.000 / 1.000, global 0, final 61.560 (= 51.300 + 10.260).
        var r = QuotationReconciliationCalculator.Compute(KHeader, KLines(5_000, 1_000, true), 61_560m, 0m, Tol);
        Assert.Equal(60_000m, r.OcrLineSumTotal);
        Assert.Equal(68_400m, r.ReconstructedOcrLineSum);
        Assert.Equal(1_560m, r.StructuralHeaderDifference);
        Assert.Equal(-8_400m, r.OcrLineComponentDifference);
        Assert.Equal(-6_840m, r.DiscountImpact);
        Assert.Equal(-6_840m, r.ExplainedLineAdjustments);
        Assert.Equal(-6_840m, r.ResidualVariance);
        Assert.True(r.ResidualExceedsTolerance);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.None, r.OcrDocumentDiscountKind);

        // …and EXACTLY the same residual BEFORE the allocation (global 6.000 kept): both are explained buckets of the same amount,
        // so the residual never depended on the allocation — it is the document's own summary discount missing from the reconstruction.
        var before = QuotationReconciliationCalculator.Compute(KHeader, KLines(0, 0, false), 61_560m, -6_840m, Tol);
        Assert.Equal(-6_840m, before.GlobalDiscountImpact);
        Assert.Equal(-6_840m, before.ResidualVariance);
    }

    [Fact] // Fixture K, discount kept at quotation level: the document's own discount is credited → residual 0 (submission still needs the lines)
    public void DocumentDiscount_K_KeptAtQuotationLevel_ResidualZero()
    {
        var r = QuotationReconciliationCalculator.Compute(KHeader, KLines(0, 0, false), 61_560m, -6_840m, Tol, KDocumentDiscount);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.DocumentLevel, r.OcrDocumentDiscountKind);
        Assert.Equal(6_000m, r.OcrDocumentDiscount);
        Assert.Equal(-6_840m, r.DocumentSummaryDiscountCredit);   // 6.000 + 14 % that follows it
        Assert.Equal(-6_840m, r.GlobalDiscountImpact);
        Assert.Equal(0m, r.DiscountImpact);
        Assert.Equal(1_560m, r.StructuralHeaderDifference);        // diagnostic only (lines printed net)
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);
        Assert.All(r.Lines, l => Assert.False(l.RequiresAdjustmentReason));
    }

    [Fact] // Fixture K after "Distribuir pelas linhas": a redistribution of the same discount → residual 0, allocation audited per line
    public void DocumentDiscount_K_DistributedToLines_ResidualZero_AllocationAuditedPerLine()
    {
        var r = QuotationReconciliationCalculator.Compute(KHeader, KLines(5_000, 1_000, true), 61_560m, 0m, Tol, KDocumentDiscount);
        Assert.Equal(-6_840m, r.DocumentSummaryDiscountCredit);
        Assert.Equal(-6_840m, r.DiscountImpact);                   // 51.300 − 57.000 + 10.260 − 11.400
        Assert.Equal(0m, r.GlobalDiscountImpact);
        Assert.Equal(61_560m, r.FinalConsideredTotal);
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);
        // The audit of the allocation lives on the lines: each line changed its discount against the OCR baseline and carries a reason.
        Assert.All(r.Lines, l => { Assert.True(l.DiscountChanged); Assert.True(l.RequiresAdjustmentReason); Assert.True(l.HasAdjustmentReason); });

        // Without the per-line reasons the gate still demands them (the allocation is audited, not silent).
        var noReasons = QuotationReconciliationCalculator.Compute(KHeader, KLines(5_000, 1_000, false), 61_560m, 0m, Tol, KDocumentDiscount);
        Assert.All(noReasons.Lines, l => Assert.False(l.HasAdjustmentReason));
    }

    [Fact] // A "Recalcular resumo" is a pure recomputation: the same inputs give the same result, nothing accumulates
    public void DocumentDiscount_K_Recalculation_IsIdempotent()
    {
        var first = QuotationReconciliationCalculator.Compute(KHeader, KLines(5_000, 1_000, true), 61_560m, 0m, Tol, KDocumentDiscount);
        var second = QuotationReconciliationCalculator.Compute(KHeader, KLines(5_000, 1_000, true), 61_560m, 0m, Tol, KDocumentDiscount);
        Assert.Equal(first.ResidualVariance, second.ResidualVariance);
        Assert.Equal(first.DocumentSummaryDiscountCredit, second.DocumentSummaryDiscountCredit);
        Assert.Equal(first.DiscountImpact, second.DiscountImpact);
        Assert.Equal(0m, second.ResidualVariance);
    }

    [Fact] // The extracted discount does not fit the header (total NOT net of it) → genuine inconsistency, still blocks
    public void DocumentDiscount_HeaderNotNetOfTheExtractedDiscount_StillBlocks()
    {
        // Header 68.400 = undiscounted lines; a "6.000" was read as a summary discount; the Buyer kept it as global (final 61.560).
        var r = QuotationReconciliationCalculator.Compute(68_400m, KLines(0, 0, false), 61_560m, -6_840m, Tol, KDocumentDiscount);
        Assert.Equal(-6_840m, r.DocumentSummaryDiscountCredit);
        Assert.Equal(6_840m, r.ResidualVariance);                  // the credit is never dropped to make it fit
        Assert.True(r.ResidualExceedsTolerance);
    }

    [Fact] // An omitted OCR line (only line 1 extracted) is still a residual equal to its value
    public void DocumentDiscount_OmittedLine_StillBlocks()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 10, 5_000, 0, 14, 50_000, fQty: 10, fUp: 5_000, fDisc: 0, fIva: 14),
        };
        // Buyer kept the global 6.000 on the single line: final (50.000 − 6.000) × 1,14 = 50.160; effect −6.840.
        var r = QuotationReconciliationCalculator.Compute(KHeader, lines, 50_160m, -6_840m, Tol, KDocumentDiscount);
        Assert.Equal(-6_840m, r.DocumentSummaryDiscountCredit);   // 6.000 over a 50.000 net + its 14 %
        Assert.Equal(11_400m, r.ResidualVariance);                 // the missing 20 × 500 line with IVA
        Assert.True(r.ResidualExceedsTolerance);
    }

    [Fact] // The SAME document discount applied twice is a deviation from the document → residual +6.840 (never an implicit per-line reason)
    public void DocumentDiscount_K_SameDiscountAppliedTwice_IsAResidual()
    {
        // (a) distributed to the lines (5.000 / 1.000) AND re-entered as global 6.000: final (54.000 − 6.000) × 1,14 = 54.720.
        var reentered = QuotationReconciliationCalculator.Compute(KHeader, KLines(5_000, 1_000, true), 54_720m, -6_840m, Tol, KDocumentDiscount);
        Assert.Equal(-6_840m, reentered.DiscountImpact);
        Assert.Equal(-6_840m, reentered.GlobalDiscountImpact);
        Assert.Equal(-13_680m, reentered.DocumentDiscountCarried);    // lines + global, at the final 14 %
        Assert.Equal(-6_840m, reentered.DocumentDiscountExpected);    // what the document grants
        Assert.Equal(-6_840m, reentered.DocumentDiscountDeviation);
        Assert.Equal(6_840m, reentered.ResidualVariance);
        Assert.True(reentered.ResidualExceedsTolerance);

        // (b) distributed TWICE into the lines (10.000 / 2.000, global 0): line-resolved for the pricing policy, same deviation here.
        var twice = QuotationReconciliationCalculator.Compute(KHeader, KLines(10_000, 2_000, true), 54_720m, 0m, Tol, KDocumentDiscount);
        Assert.Equal(-13_680m, twice.DiscountImpact);
        Assert.Equal(-6_840m, twice.DocumentDiscountDeviation);
        Assert.Equal(6_840m, twice.ResidualVariance);
        Assert.True(twice.ResidualExceedsTolerance);

        // (c) applied SHORT (3.000 / 1.000 = 4.000 of the 6.000): the quotation pays more than the document → residual −2.280.
        var shortOf = QuotationReconciliationCalculator.Compute(KHeader, KLines(3_000, 1_000, true), 63_840m, 0m, Tol, KDocumentDiscount);
        Assert.Equal(2_280m, shortOf.DocumentDiscountDeviation);
        Assert.Equal(-2_280m, shortOf.ResidualVariance);
        Assert.True(shortOf.ResidualExceedsTolerance);

        // (d) the SAME doubled payload WITHOUT the declared figure (older client) keeps the pre-v2.249.7 semantics: explained buckets.
        var undeclared = QuotationReconciliationCalculator.Compute(KHeader, KLines(5_000, 1_000, true), 54_720m, -6_840m, Tol);
        Assert.Equal(0m, undeclared.DocumentDiscountDeviation);
        Assert.Equal(-6_840m, undeclared.ResidualVariance);           // the observed −6.840 of v2.249.6, not the deviation
    }

    [Fact] // Fixture J (LUANDAY): the summary "Desconto Comercial 67.690,00" is the AGGREGATE of the printed line discounts → never credited twice
    public void DocumentDiscount_AggregateOfPrintedLineDiscounts_IsNotCreditedTwice()
    {
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 150, 1_390, 37_530, 5, 179_518.50m, fQty: 150, fUp: 1_390, fDisc: 37_530, fIva: 5),
            OcrLine(2, "MAPPED", 100, 1_885, 30_160, 5, 166_257m, fQty: 100, fUp: 1_885, fDisc: 30_160, fIva: 5),
        };
        var withAggregate = QuotationReconciliationCalculator.Compute(345_775.50m, lines, 345_775.50m, 0m, Tol, 67_690m);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.AggregateOfLines, withAggregate.OcrDocumentDiscountKind);
        Assert.Equal(67_690m, withAggregate.OcrDocumentDiscount);
        Assert.Equal(0m, withAggregate.DocumentSummaryDiscountCredit);
        Assert.Equal(0m, withAggregate.ResidualVariance);

        var withoutBaseline = QuotationReconciliationCalculator.Compute(345_775.50m, lines, 345_775.50m, 0m, Tol);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.None, withoutBaseline.OcrDocumentDiscountKind);
        Assert.Equal(0m, withoutBaseline.ResidualVariance);

        // Within 1,00 of the sum (per-line rounding) is still the aggregate; beyond it is a document-level discount on top.
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.AggregateOfLines,
            QuotationReconciliationCalculator.Compute(345_775.50m, lines, 345_775.50m, 0m, Tol, 67_690.99m).OcrDocumentDiscountKind);
        var onTop = QuotationReconciliationCalculator.Compute(345_775.50m, lines, 345_775.50m, 0m, Tol, 70_000m);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.DocumentLevel, onTop.OcrDocumentDiscountKind);
        Assert.Equal(-73_500m, onTop.DocumentSummaryDiscountCredit);  // 70.000 + 5 % — a credit the deviation cancels exactly…
        Assert.Equal(73_500m, onTop.DocumentDiscountDeviation);
        Assert.Equal(0m, onTop.ResidualVariance);                      // …so a wrong figure changes nothing: the final still equals the header
        // Had the Buyer applied that extra 2.310 (70.000 − 67.690) as a global discount, the final would leave the header → residual.
        var applied = QuotationReconciliationCalculator.Compute(345_775.50m, lines, 343_350m, -2_425.50m, Tol, 70_000m);
        Assert.Equal(2_425.50m, applied.ResidualVariance);
        Assert.True(applied.ResidualExceedsTolerance);

        // Fixture J doubled: the printed line discounts kept AND 67.690 re-entered as global (final 274.701,00) → deviation −71.074,50.
        var doubled = QuotationReconciliationCalculator.Compute(345_775.50m, lines, 274_701m, -71_074.50m, Tol, 67_690m);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.AggregateOfLines, doubled.OcrDocumentDiscountKind);
        Assert.Equal(0m, doubled.DocumentDiscountExpected);           // the document's discount is already in the baseline
        Assert.Equal(-71_074.50m, doubled.DocumentDiscountDeviation);
        Assert.Equal(71_074.50m, doubled.ResidualVariance);
        Assert.True(doubled.ResidualExceedsTolerance);
    }

    [Fact] // A document that assigns its summary discount NON-proportionally reconciles when the Buyer enters it as the document prints it
    public void DocumentDiscount_NonProportionalDocument_ReconcilesWhenEnteredAsPrinted()
    {
        // 10 × 1.000 (14 %) + 3 × 2.000 (5 %); the document takes the whole 1.000 off the 14 % line: 9.000 × 1,14 + 6.000 × 1,05 = 16.560.
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 10, 1_000, 0, 14, 10_000, fQty: 10, fUp: 1_000, fDisc: 1_000, fIva: 14, hasAdjReason: true),
            OcrLine(2, "MAPPED", 3, 2_000, 0, 5, 6_000, fQty: 3, fUp: 2_000, fDisc: 0, fIva: 5),
        };
        var r = QuotationReconciliationCalculator.Compute(16_560m, lines, 16_560m, 0m, Tol, 1_000m);
        Assert.Equal(-1_106.25m, r.DocumentSummaryDiscountCredit);   // proportional reconstruction of the document
        Assert.Equal(-1_140m, r.DocumentDiscountCarried);             // the document's own (non-proportional) assignment
        Assert.Equal(-33.75m, r.DocumentDiscountDeviation);           // the IVA difference between the two assignments…
        Assert.Equal(0m, r.ResidualVariance);                         // …is exactly what the header already shows: reconciled
        Assert.False(r.ResidualExceedsTolerance);
    }

    [Fact] // Mixed VAT (14 % / 5 %) with the proportional allocation: credit = net share + the IVA that follows it, residual exactly 0
    public void DocumentDiscount_MixedVat_ProportionalAllocation_ReconcilesExactly()
    {
        // Fixture K of the manual script §3.11: 10 × 1.000 (IVA 14) + 3 × 2.000 (IVA 5), summary discount 1.000 → 625 / 375,
        // taxable 15.000, IVA 1.312,50 + 281,25 = 1.593,75, total 16.593,75.
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 10, 1_000, 0, 14, 10_000, fQty: 10, fUp: 1_000, fDisc: 625, fIva: 14, hasAdjReason: true),
            OcrLine(2, "MAPPED", 3, 2_000, 0, 5, 6_000, fQty: 3, fUp: 2_000, fDisc: 375, fIva: 5, hasAdjReason: true),
        };
        var r = QuotationReconciliationCalculator.Compute(16_593.75m, lines, 16_593.75m, 0m, Tol, 1_000m);
        Assert.Equal(-1_106.25m, r.DocumentSummaryDiscountCredit);   // 1.000 + (625 × 14 % + 375 × 5 %)
        Assert.Equal(-1_106.25m, r.DiscountImpact);
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);

        // The same document with the discount kept at quotation level (server effect −1.106,25) reconciles too.
        var kept = QuotationReconciliationCalculator.Compute(16_593.75m,
            new List<ReconciliationLineInput>
            {
                OcrLine(1, "MAPPED", 10, 1_000, 0, 14, 10_000, fQty: 10, fUp: 1_000, fDisc: 0, fIva: 14),
                OcrLine(2, "MAPPED", 3, 2_000, 0, 5, 6_000, fQty: 3, fUp: 2_000, fDisc: 0, fIva: 5),
            }, 16_593.75m, -1_106.25m, Tol, 1_000m);
        Assert.Equal(0m, kept.ResidualVariance);
    }

    [Fact] // Largest-remainder cents on three equal nets with three rates stay inside the tolerance (never a false residual)
    public void DocumentDiscount_MixedVat_RoundingRemainders_WithinTolerance()
    {
        // 3 × (1 × 1.000) at 14 % / 5 % / 0 %, summary discount 1.000 → shares 333,34 / 333,33 / 333,33 (largest remainder).
        // Document arithmetic: taxable 2.000; IVA 666,67 × 14 % + 666,67 × 5 % = 126,67; total 2.126,67.
        var lines = new List<ReconciliationLineInput>
        {
            OcrLine(1, "MAPPED", 1, 1_000, 0, 14, 1_000, fQty: 1, fUp: 1_000, fDisc: 333.34m, fIva: 14, hasAdjReason: true),
            OcrLine(2, "MAPPED", 1, 1_000, 0, 5, 1_000, fQty: 1, fUp: 1_000, fDisc: 333.33m, fIva: 5, hasAdjReason: true),
            OcrLine(3, "MAPPED", 1, 1_000, 0, 0, 1_000, fQty: 1, fUp: 1_000, fDisc: 333.33m, fIva: 0, hasAdjReason: true),
        };
        decimal final = 759.99m + 700.00m + 666.67m;   // Round2 per line, as the server persists them
        var r = QuotationReconciliationCalculator.Compute(2_126.67m, lines, final, 0m, Tol, 1_000m);
        Assert.True(Math.Abs(r.ResidualVariance) <= 0.05m, $"residual {r.ResidualVariance}");
        Assert.False(r.ResidualExceedsTolerance);
    }

    [Fact] // Summary-only IVA AND summary-only discount (no per-line rate extracted): both document components credited, residual 0
    public void DocumentDiscount_WithSummaryOnlyIva_BothCreditsApply()
    {
        var lines = new List<ReconciliationLineInput>
        {
            SummaryIvaLine(1, 10, 5_000, 50_000, fIva: 14),
            SummaryIvaLine(2, 20, 500, 10_000, fIva: 14),
        };
        // Buyer confirmed 14 % and kept the global 6.000: final 61.560, global effect −6.840.
        var r = QuotationReconciliationCalculator.Compute(KHeader, lines, 61_560m, -6_840m, Tol, KDocumentDiscount);
        Assert.Equal(-6_000m, r.DocumentSummaryDiscountCredit);   // no extracted rate → the IVA part is in the summary-IVA credit
        Assert.Equal(7_560m, r.DocumentSummaryIvaCredit);          // 14 % of the net AFTER the summary discount (54.000)
        Assert.Equal(0m, r.ResidualVariance);
        Assert.False(r.ResidualExceedsTolerance);
    }

    [Fact] // A line the Buyer ignores takes its share of the document discount away: keeping the full discount is a deviation, the proportional share is not
    public void DocumentDiscount_IgnoredLine_TakesItsShareOfTheDocumentDiscountAway()
    {
        List<ReconciliationLineInput> Lines() => new()
        {
            OcrLine(1, "MAPPED",  10, 5_000, 0, 14, 50_000, fQty: 10, fUp: 5_000, fDisc: 0, fIva: 14),
            OcrLine(2, "MAPPED",  20, 500, 0, 14, 10_000, fQty: 20, fUp: 500, fDisc: 0, fIva: 14),
            OcrLine(3, "IGNORED", 1, 10_000, 0, 14, 10_000, fQty: 1, fUp: 10_000, fDisc: 0, fIva: 14, hasReconReason: true),
        };
        // Document: (70.000 − 6.000) × 1,14 = 72.960. The document-wide credit stays −6.840 (its removal of line 3 is at gross).
        // Buyer keeps the FULL 6.000 on the remaining 60.000 → final 61.560: 857,14 more discount than the document grants on those lines.
        var full = QuotationReconciliationCalculator.Compute(72_960m, Lines(), 61_560m, -6_840m, Tol, KDocumentDiscount);
        Assert.Equal(-11_400m, full.IgnoredImpact);
        Assert.Equal(-6_840m, full.DocumentSummaryDiscountCredit);
        Assert.Equal(-5_862.86m, full.DocumentDiscountExpected);     // 6.000 × 60.000 / 70.000 = 5.142,86 + 14 %
        Assert.Equal(-977.14m, full.DocumentDiscountDeviation);
        Assert.Equal(977.14m, full.ResidualVariance);
        Assert.True(full.ResidualExceedsTolerance);

        // Buyer reduces the global discount to the proportional share (5.142,86 → effect −5.862,86, final 62.537,14) → reconciled.
        var proportional = QuotationReconciliationCalculator.Compute(72_960m, Lines(), 62_537.14m, -5_862.86m, Tol, KDocumentDiscount);
        Assert.True(Math.Abs(proportional.ResidualVariance) <= 0.05m, $"residual {proportional.ResidualVariance}");
        Assert.False(proportional.ResidualExceedsTolerance);
    }

    [Fact] // An inflated figure never loosens the verdict: credited only up to the baselined net, and the final must still land on the header
    public void DocumentDiscount_InflatedFigure_NeverLoosensTheVerdict()
    {
        // Figure 100.000 declared on fixture K (baselined net 60.000). Buyer keeps a global 6.000 → final 61.560 = header → 0, as with 6.000.
        var kept = QuotationReconciliationCalculator.Compute(KHeader, KLines(0, 0, false), 61_560m, -6_840m, Tol, 100_000m);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.DocumentLevel, kept.OcrDocumentDiscountKind);
        Assert.Equal(-68_400m, kept.DocumentSummaryDiscountCredit);  // 60.000 net + 14 % — never beyond the lines
        Assert.Equal(0m, kept.ResidualVariance);
        // …and the doubled quotation (54.720) is STILL a residual of +6.840 whatever figure the client declares.
        var doubled = QuotationReconciliationCalculator.Compute(KHeader, KLines(5_000, 1_000, true), 54_720m, -6_840m, Tol, 100_000m);
        Assert.Equal(6_840m, doubled.ResidualVariance);
        // A figure that is not even a discount (negative) is "none": the pre-v2.249.7 verdict (−6.840) comes back, nothing is loosened.
        var negative = QuotationReconciliationCalculator.Compute(KHeader, KLines(5_000, 1_000, true), 61_560m, 0m, Tol, -6_000m);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.None, negative.OcrDocumentDiscountKind);
        Assert.Equal(-6_840m, negative.ResidualVariance);
    }
}
