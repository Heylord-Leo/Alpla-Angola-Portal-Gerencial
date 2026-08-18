using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Application.Validation;

/// <summary>
/// One immutable per-line input to the reconciliation calculator. The controller projects each
/// quotation line into this shape (never the reverse), so the calculator is pure and identical
/// for SaveQuotation, UpdateQuotation, and the read-only preview endpoint.
///
/// OCR-original values are nullable on purpose. A NULL is "not extracted", NEVER an implicit zero:
/// the calculator applies an explicit, flagged fallback (0) only for reconstruction and records
/// the imputation in <see cref="LineReconciliationResult.ImputedOcrComponents"/> so nothing is silent.
/// </summary>
public sealed class ReconciliationLineInput
{
    public Guid? QuotationItemId { get; init; }
    public int LineNumber { get; init; }
    public string Description { get; init; } = string.Empty;
    public string ReconciliationStatus { get; init; } = "MAPPED";

    /// <summary>True when this line carries an OCR-original baseline (OCR-originated, extracted).</summary>
    public bool HasOcrBaseline { get; init; }

    /// <summary>True when this is a new manual line added to an OCR quotation (no baseline, must be surfaced/explained).</summary>
    public bool IsManualAddition { get; init; }

    // OCR-original baseline (nullable — null means "not extracted", never treated as 0 silently).
    public decimal? OcrQuantity { get; init; }
    public decimal? OcrUnitPrice { get; init; }
    public decimal? OcrDiscount { get; init; }
    public decimal? OcrIvaPercent { get; init; }
    public decimal? OcrLineTotal { get; init; }
    public int? OcrUnitId { get; init; }
    public string? OcrUnitText { get; init; }

    // Final (current) values.
    public decimal FinalQuantity { get; init; }
    public decimal FinalUnitPrice { get; init; }
    public decimal FinalDiscount { get; init; }
    public decimal FinalIvaPercent { get; init; }
    public int? FinalUnitId { get; init; }
    public string? FinalUnitText { get; init; }

    /// <summary>Whether a consolidated LineAdjustmentJustification is present for this line.</summary>
    public bool HasAdjustmentReason { get; init; }
    /// <summary>Whether the line's ReconciliationJustification (SUBSTITUTE/EXTRA/IGNORED reason) is present.</summary>
    public bool HasReconciliationReason { get; init; }
}

/// <summary>Per-line reconciliation diagnostics — feeds both validation and the UI breakdown.</summary>
public sealed class LineReconciliationResult
{
    public Guid? QuotationItemId { get; init; }
    public int LineNumber { get; init; }
    public string Description { get; init; } = string.Empty;
    public string ReconciliationStatus { get; init; } = "MAPPED";
    public bool HasOcrBaseline { get; init; }
    public bool IsManualAddition { get; init; }

    public bool QuantityChanged { get; init; }
    public bool UnitPriceChanged { get; init; }
    public bool DiscountChanged { get; init; }
    public bool IvaChanged { get; init; }
    public bool UnitChanged { get; init; }

    /// <summary>Names of OCR components that were null and imputed as 0 for reconstruction (explicit, not silent).</summary>
    public IReadOnlyList<string> ImputedOcrComponents { get; init; } = Array.Empty<string>();

    /// <summary>A material financial edit against the baseline exists ⇒ a consolidated line reason is required.</summary>
    public bool RequiresAdjustmentReason { get; init; }
    public bool HasAdjustmentReason { get; init; }
    public bool HasReconciliationReason { get; init; }
}

/// <summary>Authoritative reconciliation result. All monetary control values are Round2/AwayFromZero.
/// <see cref="ResidualVariance"/> is SIGNED for display/audit; only the blocking decision uses
/// <c>Math.Abs(ResidualVariance) &gt; ToleranceApplied</c>.</summary>
public sealed class QuotationReconciliationResult
{
    public decimal OcrHeaderTotal { get; init; }
    public decimal OcrLineSumTotal { get; init; }
    public decimal ReconstructedOcrLineSum { get; init; }
    public decimal StructuralHeaderDifference { get; init; }
    public decimal OcrLineComponentDifference { get; init; }

    public decimal FinalConsideredTotal { get; init; }
    public decimal ManualAdditionsTotal { get; init; }

    /// <summary>
    /// Tax the DOCUMENT itself carries only at summary level (v2.226.1). Recognized by
    /// reconciliation INFERENCE — the OCR line baseline has no per-line rate, the confirmed rate
    /// applied to the ORIGINAL line components materially improves the header consistency — and
    /// credited against the residual so the document's own IVA is never reported as an
    /// unexplained difference. Zero whenever the baseline already carries per-line rates, the
    /// document is genuinely tax-free, or the credit would not improve reconciliation.
    /// </summary>
    public decimal DocumentSummaryIvaCredit { get; init; }

    public decimal IgnoredImpact { get; init; }
    public decimal QuantityImpact { get; init; }
    public decimal UnitPriceImpact { get; init; }
    public decimal DiscountImpact { get; init; }
    public decimal IvaImpact { get; init; }
    public decimal GlobalDiscountImpact { get; init; }
    public decimal ManualAdditionsImpact { get; init; }
    public decimal ExplainedLineAdjustments { get; init; }

    /// <summary>Signed: OcrHeaderTotal + ExplainedLineAdjustments − FinalConsideredTotal −
    /// DocumentSummaryIvaCredit. Only the residual AFTER recognized document components gates.</summary>
    public decimal ResidualVariance { get; init; }
    public decimal ToleranceApplied { get; init; }
    public bool ResidualExceedsTolerance { get; init; }

    public IReadOnlyList<LineReconciliationResult> Lines { get; init; } = Array.Empty<LineReconciliationResult>();
}

/// <summary>
/// Backend-authoritative reconciliation between the OCR document (header + line baseline) and the
/// final considered quotation total, after item-level explained adjustments.
///
/// The residual is anchored to the OCR document's INTERNAL consistency
/// (OcrHeaderTotal − ReconstructedOcrLineSum), credited with recognized document-level components
/// the line baseline cannot carry (<see cref="QuotationReconciliationResult.DocumentSummaryIvaCredit"/>,
/// v2.226.1) — so a document whose header includes summary-only IVA reconciles to zero instead of
/// reporting its own tax as unexplained. Buyer line edits are attributed to the explained buckets
/// (IGNORED → quantity → unit-price → discount → IVA → manual-additions → global-discount), which
/// reconcile to <c>FinalConsideredTotal − ReconstructedOcrLineSum</c> within rounding.
///
/// Known limitation (documented follow-up, not expanded here): OCR-summary-only DISCOUNTS have the
/// same architectural shape and are not yet credited — a header net of a summary discount absent
/// from the line baseline still surfaces as residual.
///
/// Global-discount allocation and IVA re-proportioning reuse the EXACT rules of
/// <see cref="QuotationIntegrityCalculator"/> so the two calculators never diverge.
/// </summary>
public static class QuotationReconciliationCalculator
{
    /// <summary>Residual tolerance. Matches the live Financial Integrity Gate flat tolerance.</summary>
    public const decimal ToleranceAmount = QuotationIntegrityCalculator.ToleranceAmount;

    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>Unrounded line value net+IVA (intermediate math is unrounded; only outputs are Round2'd).</summary>
    private static decimal Value(decimal qty, decimal unitPrice, decimal discount, decimal ivaPercent)
    {
        var net = Math.Max(0m, qty * unitPrice - discount);
        return net + net * ivaPercent / 100m;
    }

    /// <param name="finalConsideredTotal">Authoritative considered total from the same path used to persist
    /// (QuotationIntegrityCalculator.QuotationConsideredTotal) — passed in so there is a single source of truth.</param>
    /// <param name="consideredGlobalDiscountEffect">Signed (≤ 0) effect of the quotation-level global discount on the
    /// considered total (taxable + its IVA reduction), computed by QuotationIntegrityCalculator — reused verbatim.</param>
    public static QuotationReconciliationResult Compute(
        decimal ocrHeaderTotal,
        IReadOnlyList<ReconciliationLineInput> lines,
        decimal finalConsideredTotal,
        decimal consideredGlobalDiscountEffect,
        decimal tolerance)
    {
        var baselined = lines.Where(l => l.HasOcrBaseline).ToList();

        // ── OCR document-internal figures (independent of final values) ──
        decimal ocrLineSumTotal = 0m;      // Σ document-extracted line totals (imputed from components only when null, flagged)
        decimal reconstructedOcrLineSum = 0m; // Σ value(OCR components)

        var lineResults = new List<LineReconciliationResult>(lines.Count);

        foreach (var l in baselined)
        {
            var imputed = new List<string>();
            var oQty = l.OcrQuantity ?? Impute(imputed, "quantity");
            var oUp = l.OcrUnitPrice ?? Impute(imputed, "unitPrice");
            var oDisc = l.OcrDiscount ?? Impute(imputed, "discount");
            var oIva = l.OcrIvaPercent ?? Impute(imputed, "ivaPercent");

            var reconstructed = Value(oQty, oUp, oDisc, oIva);
            reconstructedOcrLineSum += reconstructed;

            // OcrLineTotal is the document's own reported line total; fall back to the reconstruction
            // ONLY when it was not extracted (flagged as an imputed component so it is never silent).
            decimal lineTotalValue;
            if (l.OcrLineTotal.HasValue) lineTotalValue = l.OcrLineTotal.Value;
            else { lineTotalValue = reconstructed; imputed.Add("lineTotal"); }
            ocrLineSumTotal += lineTotalValue;
        }

        decimal structuralHeaderDifference = ocrHeaderTotal - ocrLineSumTotal;
        decimal ocrLineComponentDifference = ocrLineSumTotal - reconstructedOcrLineSum;

        // ── Explained line-adjustment buckets (sequential morph from the reconstructed OCR baseline) ──
        decimal ignoredImpact = 0m, quantityImpact = 0m, unitPriceImpact = 0m, discountImpact = 0m, ivaImpact = 0m;

        foreach (var l in baselined)
        {
            var oQty = l.OcrQuantity ?? 0m;
            var oUp = l.OcrUnitPrice ?? 0m;
            var oDisc = l.OcrDiscount ?? 0m;
            var oIva = l.OcrIvaPercent ?? 0m;
            var baseVal = Value(oQty, oUp, oDisc, oIva);

            var isConsidered = RequestConstants.ReconciliationStatuses.Considered.Contains(l.ReconciliationStatus);
            if (!isConsidered)
            {
                // Left the considered set (IGNORED / NOT_QUOTED): remove its reconstructed document value.
                ignoredImpact -= baseVal;
                continue;
            }

            var fQ = l.FinalQuantity; var fUp = l.FinalUnitPrice; var fD = l.FinalDiscount; var fI = l.FinalIvaPercent;
            quantityImpact += Value(fQ, oUp, oDisc, oIva) - Value(oQty, oUp, oDisc, oIva);
            unitPriceImpact += Value(fQ, fUp, oDisc, oIva) - Value(fQ, oUp, oDisc, oIva);
            discountImpact += Value(fQ, fUp, fD, oIva) - Value(fQ, fUp, oDisc, oIva);
            ivaImpact += Value(fQ, fUp, fD, fI) - Value(fQ, fUp, fD, oIva);
        }

        // Manual additions: considered lines with no OCR baseline (new manual lines in an OCR quotation).
        decimal manualAdditionsImpact = lines
            .Where(l => !l.HasOcrBaseline && RequestConstants.ReconciliationStatuses.Considered.Contains(l.ReconciliationStatus))
            .Sum(l => Value(l.FinalQuantity, l.FinalUnitPrice, l.FinalDiscount, l.FinalIvaPercent));
        decimal manualAdditionsTotal = manualAdditionsImpact;

        decimal globalDiscountImpact = consideredGlobalDiscountEffect; // ≤ 0, from QuotationIntegrityCalculator

        decimal explained = ignoredImpact + quantityImpact + unitPriceImpact + discountImpact + ivaImpact
                            + manualAdditionsImpact + globalDiscountImpact;

        // ── Document-summary IVA credit (v2.226.1) — a RECONCILIATION INFERENCE, not extraction ──
        //
        // A document that states IVA only in its summary yields a line baseline with NULL per-line
        // rates, so the reconstruction structurally undercounts the header by the document's own
        // tax — which then surfaced as an "unexplained" residual even though the confirmed rate
        // fully accounted for it. No stronger persisted summary-tax signal exists (the global-VAT
        // inference lives in client state only), so recognition is inferred, guarded twice:
        //
        //   1. Eligibility: only baselined, considered lines whose OCR rate is genuinely NULL
        //      (an extracted 0% is a confirmed tax-free line and is never credited — no double
        //      count when the baseline already carries rates, including mixed documents).
        //   2. The numeric test: the credit is granted only when it IMPROVES the header
        //      consistency (|residual − credit| < |residual|). A genuinely tax-free document
        //      whose header equals its net lines gets no artificial credit merely because the
        //      buyer selected a rate — that selection stays a buyer adjustment, not document tax.
        //
        // The credit uses the ORIGINAL line components with the confirmed rate, so buyer edits to
        // quantity/price/discount neither inflate nor deflate the document's own tax.
        decimal rawSummaryIvaCredit = 0m;
        foreach (var l in baselined)
        {
            if (l.OcrIvaPercent != null) continue;
            if (!RequestConstants.ReconciliationStatuses.Considered.Contains(l.ReconciliationStatus)) continue;

            var oQty = l.OcrQuantity ?? 0m;
            var oUp = l.OcrUnitPrice ?? 0m;
            var oDisc = l.OcrDiscount ?? 0m;
            rawSummaryIvaCredit += Value(oQty, oUp, oDisc, l.FinalIvaPercent) - Value(oQty, oUp, oDisc, 0m);
        }

        decimal residualWithoutCredit = ocrHeaderTotal + explained - finalConsideredTotal;
        decimal documentSummaryIvaCredit =
            rawSummaryIvaCredit != 0m &&
            Math.Abs(residualWithoutCredit - rawSummaryIvaCredit) < Math.Abs(residualWithoutCredit)
                ? rawSummaryIvaCredit
                : 0m;

        // Signed residual — the canonical control value. Any bucket shortfall (rounding/interaction)
        // is honestly absorbed here rather than hidden. The structural header difference remains a
        // separate diagnostic and may legitimately stay non-zero on a fully reconciled document.
        decimal residual = residualWithoutCredit - documentSummaryIvaCredit;
        decimal residualRounded = Round2(residual);

        // ── Per-line diagnostics (materiality is decided by the caller for quantity/unit; the
        // calculator flags monetary-field changes whose isolated line impact exceeds tolerance) ──
        foreach (var l in lines)
        {
            LineReconciliationResult r;
            if (!l.HasOcrBaseline)
            {
                r = new LineReconciliationResult
                {
                    QuotationItemId = l.QuotationItemId, LineNumber = l.LineNumber, Description = l.Description,
                    ReconciliationStatus = l.ReconciliationStatus, HasOcrBaseline = false,
                    IsManualAddition = l.IsManualAddition,
                    HasAdjustmentReason = l.HasAdjustmentReason, HasReconciliationReason = l.HasReconciliationReason,
                    // A manual addition that is neither EXTRA_ITEM/SUBSTITUTE (reconciliation reason) nor
                    // otherwise justified requires an origin explanation via the adjustment reason.
                    RequiresAdjustmentReason = l.IsManualAddition
                        && RequestConstants.ReconciliationStatuses.Considered.Contains(l.ReconciliationStatus)
                        && !IsInherentlyJustified(l.ReconciliationStatus) && !l.HasReconciliationReason
                };
                lineResults.Add(r);
                continue;
            }

            var imputed = new List<string>();
            var oQty = l.OcrQuantity ?? Impute(imputed, "quantity");
            var oUp = l.OcrUnitPrice ?? Impute(imputed, "unitPrice");
            var oDisc = l.OcrDiscount ?? Impute(imputed, "discount");
            var oIva = l.OcrIvaPercent ?? Impute(imputed, "ivaPercent");
            if (!l.OcrLineTotal.HasValue) imputed.Add("lineTotal");

            // Quantity change is exact (both are decimal(18,4)); any change is a change. Integer-unit
            // whole-number enforcement is a separate caller-side invalid-input check.
            bool qtyChanged = l.FinalQuantity != oQty;
            // Each monetary field's materiality = its ISOLATED line impact exceeding tolerance
            // (evaluated with the other fields held at their final values, so effects don't double-count).
            bool priceChanged = Math.Abs(Value(l.FinalQuantity, l.FinalUnitPrice, oDisc, oIva) - Value(l.FinalQuantity, oUp, oDisc, oIva)) > tolerance;
            bool discChanged = Math.Abs(Value(l.FinalQuantity, l.FinalUnitPrice, l.FinalDiscount, oIva) - Value(l.FinalQuantity, l.FinalUnitPrice, oDisc, oIva)) > tolerance;
            bool ivaChanged = Math.Abs(Value(l.FinalQuantity, l.FinalUnitPrice, l.FinalDiscount, l.FinalIvaPercent) - Value(l.FinalQuantity, l.FinalUnitPrice, l.FinalDiscount, oIva)) > tolerance;
            // Unit of measure: ANY actual change is material (prefer id comparison, fall back to text).
            bool unitChanged = (l.OcrUnitId.HasValue || l.FinalUnitId.HasValue)
                ? l.OcrUnitId != l.FinalUnitId
                : !string.Equals((l.OcrUnitText ?? string.Empty).Trim(), (l.FinalUnitText ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

            // A line that left the considered set (now IGNORED/NOT_QUOTED) uses its own reconciliation
            // reason and never requires an adjustment reason for its financial fields.
            var isConsidered = RequestConstants.ReconciliationStatuses.Considered.Contains(l.ReconciliationStatus);
            bool anyMaterialFinancialChange = isConsidered && (qtyChanged || priceChanged || discChanged || ivaChanged || unitChanged);

            r = new LineReconciliationResult
            {
                QuotationItemId = l.QuotationItemId, LineNumber = l.LineNumber, Description = l.Description,
                ReconciliationStatus = l.ReconciliationStatus, HasOcrBaseline = true, IsManualAddition = false,
                QuantityChanged = qtyChanged, UnitPriceChanged = priceChanged, DiscountChanged = discChanged, IvaChanged = ivaChanged, UnitChanged = unitChanged,
                ImputedOcrComponents = imputed,
                RequiresAdjustmentReason = anyMaterialFinancialChange,
                HasAdjustmentReason = l.HasAdjustmentReason, HasReconciliationReason = l.HasReconciliationReason
            };
            lineResults.Add(r);
        }

        return new QuotationReconciliationResult
        {
            OcrHeaderTotal = Round2(ocrHeaderTotal),
            OcrLineSumTotal = Round2(ocrLineSumTotal),
            ReconstructedOcrLineSum = Round2(reconstructedOcrLineSum),
            StructuralHeaderDifference = Round2(structuralHeaderDifference),
            OcrLineComponentDifference = Round2(ocrLineComponentDifference),
            FinalConsideredTotal = Round2(finalConsideredTotal),
            ManualAdditionsTotal = Round2(manualAdditionsTotal),
            DocumentSummaryIvaCredit = Round2(documentSummaryIvaCredit),
            IgnoredImpact = Round2(ignoredImpact),
            QuantityImpact = Round2(quantityImpact),
            UnitPriceImpact = Round2(unitPriceImpact),
            DiscountImpact = Round2(discountImpact),
            IvaImpact = Round2(ivaImpact),
            GlobalDiscountImpact = Round2(globalDiscountImpact),
            ManualAdditionsImpact = Round2(manualAdditionsImpact),
            ExplainedLineAdjustments = Round2(explained),
            ResidualVariance = residualRounded,
            ToleranceApplied = tolerance,
            ResidualExceedsTolerance = Math.Abs(residualRounded) > tolerance,
            Lines = lineResults
        };
    }

    private static decimal Impute(List<string> imputed, string field) { imputed.Add(field); return 0m; }

    /// <summary>EXTRA_ITEM/SUBSTITUTE carry their own reconciliation reason explaining why the item
    /// belongs, which is sufficient business justification for a manual addition (no separate origin reason).</summary>
    private static bool IsInherentlyJustified(string status) =>
        status == RequestConstants.ReconciliationStatuses.ExtraItem
        || status == RequestConstants.ReconciliationStatuses.Substitute;
}
