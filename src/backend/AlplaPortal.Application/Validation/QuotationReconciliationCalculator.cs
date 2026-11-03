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

    /// <summary>
    /// v2.249.7 — the document-level (summary-only) discount the extraction read in the document's summary, net of IVA,
    /// exactly as received (the immutable OCR header baseline, counterpart of OcrHeaderTotal for the discount). 0 when the
    /// payload carried none. How it was treated is in <see cref="OcrDocumentDiscountKind"/>.
    /// </summary>
    public decimal OcrDocumentDiscount { get; init; }

    /// <summary>NONE | DOCUMENT_LEVEL | AGGREGATE_OF_LINES (<see cref="QuotationReconciliationCalculator.DocumentDiscountKinds"/>):
    /// a summary figure that equals the sum of the discounts the line baseline already carries is the AGGREGATE of the printed
    /// line discounts and is never credited a second time.</summary>
    public string OcrDocumentDiscountKind { get; init; } = QuotationReconciliationCalculator.DocumentDiscountKinds.None;

    /// <summary>
    /// Signed (≤ 0) effect of the document's own summary discount on the reconstructed document total: the discount itself
    /// (never beyond the net of the baselined lines) plus the IVA that follows it on the lines with an extracted rate,
    /// proportional to every baselined line's net — the same proportional rule the server applies to a quotation-level
    /// discount. A DOCUMENT COMPONENT read from the extraction (not an inference, no numeric guard): credited against the
    /// residual so the document's own discount is never reported as unexplained, whether the Buyer keeps it at quotation level
    /// or distributes it over the lines (both are explained buckets of that same amount). A summary discount that does not fit
    /// the header surfaces as a residual instead of being dropped.
    /// </summary>
    public decimal DocumentSummaryDiscountCredit { get; init; }

    /// <summary>
    /// v2.249.7 — the discount the quotation CARRIES beyond the OCR line baseline (line discounts changed against the baseline
    /// plus the quotation-level global discount), valued on the considered lines at their final rates; signed ≤ 0 for a discount.
    /// Computed only when the payload declares the document's summary discount (kind ≠ NONE).
    /// </summary>
    public decimal DocumentDiscountCarried { get; init; }

    /// <summary>v2.249.7 — the discount the DOCUMENT grants on the considered lines (its summary discount, proportional to the
    /// baseline nets, with the IVA that follows it at the final rates); 0 for an aggregate of line discounts (already in the
    /// baseline). Signed ≤ 0.</summary>
    public decimal DocumentDiscountExpected { get; init; }

    /// <summary>
    /// v2.249.7 — <see cref="DocumentDiscountCarried"/> − <see cref="DocumentDiscountExpected"/>: the Buyer's deviation from
    /// the document's own discount (negative = more discount than the document grants, e.g. the same discount applied twice;
    /// positive = less). Part of the residual — a deviation must be justified explicitly, it is never an implicit per-line
    /// reason. 0 when no summary discount was declared (NONE: the pre-existing explained-bucket behaviour).
    /// </summary>
    public decimal DocumentDiscountDeviation { get; init; }

    public decimal IgnoredImpact { get; init; }
    public decimal QuantityImpact { get; init; }
    public decimal UnitPriceImpact { get; init; }
    public decimal DiscountImpact { get; init; }
    public decimal IvaImpact { get; init; }
    public decimal GlobalDiscountImpact { get; init; }
    public decimal ManualAdditionsImpact { get; init; }
    public decimal ExplainedLineAdjustments { get; init; }

    /// <summary>Signed: OcrHeaderTotal + ExplainedLineAdjustments − FinalConsideredTotal − DocumentSummaryDiscountCredit −
    /// DocumentDiscountDeviation − DocumentSummaryIvaCredit. Only the residual AFTER recognized document components gates.</summary>
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
/// v2.249.7 — OCR-summary-only DISCOUNTS have the same architectural shape and are credited the same
/// way (<see cref="QuotationReconciliationResult.DocumentSummaryDiscountCredit"/>): the extracted summary
/// discount is part of the document reconstruction (net share + the IVA that follows it, proportional to
/// the baselined line nets), so a header net of a document-level discount reconciles to zero whether the
/// Buyer keeps the discount at quotation level or distributes it over the lines — a redistribution of the
/// document's own discount is not a commercial change. A summary figure equal to the sum of the line
/// discounts the baseline already carries is the aggregate of the printed line discounts (never credited
/// twice); a summary discount that does not fit the header still surfaces as residual.
///
/// Global-discount allocation and IVA re-proportioning reuse the EXACT rules of
/// <see cref="QuotationIntegrityCalculator"/> so the two calculators never diverge.
/// </summary>
public static class QuotationReconciliationCalculator
{
    /// <summary>Residual tolerance. Matches the live Financial Integrity Gate flat tolerance.</summary>
    public const decimal ToleranceAmount = QuotationIntegrityCalculator.ToleranceAmount;

    /// <summary>v2.249.7 — a summary discount within this amount of the sum of the extracted line discounts is their
    /// aggregate, not a second discount (mirror of the wizard's HEADER_DISCOUNT_AGGREGATE_TOLERANCE in lib/quotationPricing).</summary>
    public const decimal DocumentDiscountAggregateTolerance = 1.00m;

    /// <summary>v2.249.7 — how an extracted summary discount was treated by the reconciliation.</summary>
    public static class DocumentDiscountKinds
    {
        public const string None = "NONE";
        public const string DocumentLevel = "DOCUMENT_LEVEL";
        public const string AggregateOfLines = "AGGREGATE_OF_LINES";
    }

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
    /// <param name="ocrDocumentDiscount">v2.249.7 — document-level (summary-only) discount read by the extraction in the
    /// document's summary, net of IVA (immutable OCR header baseline, like <paramref name="ocrHeaderTotal"/>); 0 when the
    /// document shows none. Credited as a document component (see <see cref="QuotationReconciliationResult.DocumentSummaryDiscountCredit"/>).</param>
    public static QuotationReconciliationResult Compute(
        decimal ocrHeaderTotal,
        IReadOnlyList<ReconciliationLineInput> lines,
        decimal finalConsideredTotal,
        decimal consideredGlobalDiscountEffect,
        decimal tolerance,
        decimal ocrDocumentDiscount = 0m)
    {
        var baselined = lines.Where(l => l.HasOcrBaseline).ToList();

        // ── OCR document-internal figures (independent of final values) ──
        decimal ocrLineSumTotal = 0m;      // Σ document-extracted line totals (imputed from components only when null, flagged)
        decimal reconstructedOcrLineSum = 0m; // Σ value(OCR components)
        decimal baselineNet = 0m;             // Σ max(0, qty·price − line discount) of the baseline, document-wide (v2.249.7)
        decimal baselineLineDiscountSum = 0m; // Σ extracted line discounts, document-wide (v2.249.7 aggregate guard)

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
            baselineNet += Math.Max(0m, oQty * oUp - oDisc);
            baselineLineDiscountSum += oDisc;

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
        // ── Document-summary DISCOUNT credit (v2.249.7) — a DOCUMENT COMPONENT read from the extraction, not an inference ──
        //
        // A document whose summary carries a discount the line baseline cannot carry (REQ-03/11/2026-630: lines 50.000 + 10.000,
        // "Desconto 6.000", IVA 14 %, total 61.560) reconstructs to the UNDISCOUNTED lines (68.400), so the header was always
        // short of the reconstruction by the discount and its IVA (−6.840) — before and after "Distribuir pelas linhas", because
        // the Buyer's global discount and the line-discount changes are both explained buckets of exactly that amount and the
        // residual is anchored on the document's own consistency. The extracted summary discount is therefore part of the
        // document reconstruction:
        //   1. Aggregate guard (mirror of the wizard's resolveOcrHeaderDiscount): a summary figure equal to the sum of the
        //      discounts the line baseline already carries is the aggregate of the printed line discounts (LUANDAY:
        //      37.530 + 30.160 = 67.690) and is never credited a second time.
        //   2. Otherwise the discount reduces the net of every baselined line proportionally (the server's own rule for a
        //      quotation-level discount, document-wide — an IGNORED line's removal stays a Buyer adjustment at its gross value)
        //      and the IVA of the lines with an extracted rate follows; lines without an extracted rate take their reduced net
        //      into the summary-IVA credit below. Never beyond the net of the baselined lines.
        //   No numeric guard: a summary discount that does not fit the header (mis-extraction, a figure that is not a discount,
        //   the discount applied twice by the Buyer) must surface as a residual, never be dropped silently.
        decimal ocrDocumentDiscountReceived = Math.Max(0m, ocrDocumentDiscount);
        string documentDiscountKind = DocumentDiscountKinds.None;
        decimal documentDiscountRatio = 0m;       // share of every baselined line's net taken by the summary discount
        decimal rawDocumentDiscountCredit = 0m;   // ≤ 0
        if (ocrDocumentDiscountReceived > 0m)
        {
            if (baselineLineDiscountSum > 0m
                && Math.Abs(ocrDocumentDiscountReceived - baselineLineDiscountSum) <= DocumentDiscountAggregateTolerance)
            {
                documentDiscountKind = DocumentDiscountKinds.AggregateOfLines;
            }
            else
            {
                documentDiscountKind = DocumentDiscountKinds.DocumentLevel;
                var applied = Math.Min(ocrDocumentDiscountReceived, baselineNet);
                documentDiscountRatio = baselineNet > 0m ? applied / baselineNet : 0m;
                decimal vatFollowingTheDiscount = 0m;
                foreach (var l in baselined)
                {
                    if (l.OcrIvaPercent == null) continue;
                    var net = Math.Max(0m, (l.OcrQuantity ?? 0m) * (l.OcrUnitPrice ?? 0m) - (l.OcrDiscount ?? 0m));
                    vatFollowingTheDiscount += net * documentDiscountRatio * l.OcrIvaPercent.Value / 100m;
                }
                rawDocumentDiscountCredit = -(applied + vatFollowingTheDiscount);
            }
        }

        // ── Discount deviation from the document (v2.249.7) ──
        //
        // Both Buyer discount buckets (line discounts changed against the baseline, quotation-level global discount) equal by
        // construction their effect on the final total, so on their own they can never leave a residual: the same document
        // discount applied TWICE (REQ-630: 6.000 distributed to the lines AND re-entered as global → 54.720, or distributed
        // twice into the lines) or applied short reconciled to zero. Once the payload declares the document's summary discount
        // (DOCUMENT_LEVEL or AGGREGATE_OF_LINES), the discount the quotation carries beyond the OCR line baseline is compared with
        // the discount the document itself grants, both valued on the CONSIDERED lines at their FINAL rates (the server's
        // proportional rule; a line the Buyer ignored takes its share of the document discount away). The difference is the
        // Buyer's deviation from the document — a residual to justify explicitly (QUOTATION_RESIDUAL_JUSTIFIED), never an
        // implicit per-line reason. NONE (no summary discount read): the pre-existing explained-bucket behaviour is unchanged.
        decimal documentDiscountCarried = 0m, documentDiscountExpected = 0m, documentDiscountDeviation = 0m;
        if (documentDiscountKind != DocumentDiscountKinds.None)
        {
            decimal consideredBaselineNet = 0m, consideredBaselineIvaOnNet = 0m;
            foreach (var l in baselined)
            {
                if (!RequestConstants.ReconciliationStatuses.Considered.Contains(l.ReconciliationStatus)) continue;
                var net = Math.Max(0m, (l.OcrQuantity ?? 0m) * (l.OcrUnitPrice ?? 0m) - (l.OcrDiscount ?? 0m));
                consideredBaselineNet += net;
                consideredBaselineIvaOnNet += net * l.FinalIvaPercent / 100m;
                documentDiscountCarried += Value(l.FinalQuantity, l.FinalUnitPrice, l.FinalDiscount, l.FinalIvaPercent)
                                         - Value(l.FinalQuantity, l.FinalUnitPrice, l.OcrDiscount ?? 0m, l.FinalIvaPercent);
            }
            documentDiscountCarried += globalDiscountImpact;
            if (documentDiscountKind == DocumentDiscountKinds.DocumentLevel && baselineNet > 0m && consideredBaselineNet > 0m)
            {
                var appliedDocumentWide = Math.Min(ocrDocumentDiscountReceived, baselineNet);
                var consideredShare = appliedDocumentWide * consideredBaselineNet / baselineNet;
                documentDiscountExpected = -(consideredShare + consideredShare * consideredBaselineIvaOnNet / consideredBaselineNet);
            }
            documentDiscountDeviation = documentDiscountCarried - documentDiscountExpected;
        }

        decimal rawSummaryIvaCredit = 0m;
        foreach (var l in baselined)
        {
            if (l.OcrIvaPercent != null) continue;
            if (!RequestConstants.ReconciliationStatuses.Considered.Contains(l.ReconciliationStatus)) continue;

            var oQty = l.OcrQuantity ?? 0m;
            var oUp = l.OcrUnitPrice ?? 0m;
            var oDisc = l.OcrDiscount ?? 0m;
            // v2.249.7 — the document's tax follows the net AFTER its own summary discount.
            rawSummaryIvaCredit += (Value(oQty, oUp, oDisc, l.FinalIvaPercent) - Value(oQty, oUp, oDisc, 0m)) * (1m - documentDiscountRatio);
        }

        decimal residualWithoutCredit = ocrHeaderTotal + explained - finalConsideredTotal;
        decimal residualAfterDocumentDiscount = residualWithoutCredit - rawDocumentDiscountCredit - documentDiscountDeviation;
        decimal documentSummaryIvaCredit =
            rawSummaryIvaCredit != 0m &&
            Math.Abs(residualAfterDocumentDiscount - rawSummaryIvaCredit) < Math.Abs(residualAfterDocumentDiscount)
                ? rawSummaryIvaCredit
                : 0m;

        // Signed residual — the canonical control value. Any bucket shortfall (rounding/interaction)
        // is honestly absorbed here rather than hidden. The structural header difference remains a
        // separate diagnostic and may legitimately stay non-zero on a fully reconciled document.
        decimal residual = residualAfterDocumentDiscount - documentSummaryIvaCredit;
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
            OcrDocumentDiscount = Round2(ocrDocumentDiscountReceived),
            OcrDocumentDiscountKind = documentDiscountKind,
            DocumentSummaryDiscountCredit = Round2(rawDocumentDiscountCredit),
            DocumentDiscountCarried = Round2(documentDiscountCarried),
            DocumentDiscountExpected = Round2(documentDiscountExpected),
            DocumentDiscountDeviation = Round2(documentDiscountDeviation),
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
