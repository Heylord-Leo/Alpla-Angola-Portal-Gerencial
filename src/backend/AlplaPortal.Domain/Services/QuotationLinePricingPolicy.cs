using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.249.6 — "line-resolved pricing" of a saved quotation, as ONE pure rule.
///
/// <para><b>Why.</b> Everything downstream of the quotation prices PER LINE: the candidate snapshot frozen at batch submission
/// (<see cref="ApprovalBatchCandidateSnapshotFactory"/> copies <c>QuotationItem.LineTotal</c>), the P.O. group total
/// (<c>GroupBuilderService</c> sums the winners' line totals) and the approved amount of the batch (sum of the activated groups).
/// A quotation-level ("global") commercial discount — <c>Quotation.DiscountAmount</c> — is applied ONLY to the quotation's own
/// totals (<c>TotalTaxableBase</c>, <c>TotalIvaAmount</c>, <c>TotalAmount</c>); no line carries a share of it. Submitting such a
/// quotation therefore approves the UNDISCOUNTED line values while the quotation card shows the discounted total — the
/// 416.850,00 vs 345.775,50 divergence found in TEST on REQ-21/10/2026-595. The share of a document-level discount that belongs
/// to a PARTIAL batch (some lines only) is not even defined by the model.</para>
///
/// <para><b>Rule.</b> A quotation is "line-resolved" when (1) it carries no unallocated global discount and (2) the sum of the
/// line totals of its CONSIDERED lines (MAPPED / SUBSTITUTE / EXTRA_ITEM) equals its <c>TotalAmount</c> (within
/// <see cref="LineSumTolerance"/>, for legacy rows whose totals were written by older arithmetic). By construction of
/// SaveQuotation / UpdateQuotation the equality is exact whenever the global discount is zero, and the server computes
/// <c>TotalAmount</c> from the lines it recomputes itself (the client header totals are never persisted), so a current write
/// cannot produce a zero total with positive considered lines. Such a row can only be LEGACY (written before the totals were
/// computed server-side) and is classified explicitly as <see cref="Kinds.LegacyTotalNotComputed"/>: it stays submittable for
/// compatibility (the approval amount is the well-defined line sum) but is never confused with a zero-value quotation
/// (<see cref="Kinds.ZeroValue"/>: no considered value at all) nor with incomplete pricing (the two unresolved kinds).</para>
///
/// <para><b>Consumers.</b> The batch endpoints refuse (409 <see cref="ConflictCode"/>) any candidate from a quotation that is not
/// line-resolved — BEFORE any write, whatever the batch composition; the DTOs expose the evaluation so the Buyer surfaces see
/// the same verdict ("Desconto global não distribuído"). Nothing is ever redistributed silently: the Buyer resolves it on the
/// quotation (per-line discounts, or the explicit "Distribuir pelas linhas" action of the wizard) and the server recomputes.
/// Historical snapshots (candidates, groups, approved totals) are never recalculated by this rule.</para>
///
/// <para><b>Write-path boundary.</b> <see cref="GlobalDiscountExceedsNet"/> is the server-side arithmetic guard of
/// SaveQuotation / UpdateQuotation / reconcile-preview: a global discount larger than the net of the considered lines after
/// their own discounts is refused (400 <see cref="GlobalDiscountExceedsNetCode"/>) instead of being clamped to a zero taxable
/// base — a clamped total would hide the excess and make the quotation's own figures meaningless.</para>
/// </summary>
public static class QuotationLinePricingPolicy
{
    /// <summary>ProblemDetails <c>code</c> of the batch refusal.</summary>
    public const string ConflictCode = "QUOTATION_PRICING_NOT_LINE_RESOLVED";

    /// <summary>ProblemDetails <c>code</c> of the write-path refusal when the global discount exceeds the remaining net.</summary>
    public const string GlobalDiscountExceedsNetCode = "QUOTATION_GLOBAL_DISCOUNT_EXCEEDS_NET";

    /// <summary>Absolute tolerance on |Σ considered line totals − TotalAmount| (legacy rounding only; never a global discount).</summary>
    public const decimal LineSumTolerance = 1.00m;

    public static class Reasons
    {
        /// <summary><c>Quotation.DiscountAmount &gt; 0</c>: a document-level discount no line carries.</summary>
        public const string GlobalDiscountUnallocated = "GLOBAL_DISCOUNT_UNALLOCATED";
        /// <summary>No global discount, but the considered lines do not add up to the quotation total (legacy arithmetic drift).</summary>
        public const string LineTotalMismatch = "LINE_TOTAL_MISMATCH";
    }

    /// <summary>The explicit classification of every evaluation (resolved kinds first).</summary>
    public static class Kinds
    {
        /// <summary>Considered lines add up to a positive quotation total; no global discount.</summary>
        public const string LineResolved = "LINE_RESOLVED";
        /// <summary>No considered value at all (total 0 and considered lines 0): legitimately zero, not incomplete.</summary>
        public const string ZeroValue = "ZERO_VALUE";
        /// <summary>Total 0 with positive considered lines: a LEGACY row whose total was never computed server-side. Resolved for
        /// compatibility (approval values the lines), but explicitly distinguished so it is never mistaken for a zero-value quotation
        /// or for current pricing. The current write path cannot produce it (server-computed totals).</summary>
        public const string LegacyTotalNotComputed = "LEGACY_TOTAL_NOT_COMPUTED";
        /// <summary>= <see cref="Reasons.GlobalDiscountUnallocated"/> (unresolved).</summary>
        public const string GlobalDiscountUnallocated = Reasons.GlobalDiscountUnallocated;
        /// <summary>= <see cref="Reasons.LineTotalMismatch"/> (unresolved).</summary>
        public const string LineTotalMismatch = Reasons.LineTotalMismatch;
    }

    /// <summary>The facts of one quotation line the rule reads.</summary>
    public sealed record LineFacts(string? ReconciliationStatus, decimal LineTotal);

    /// <summary>The explained verdict. <see cref="Difference"/> = ConsideredLineTotalSum − QuotationTotal (signed);
    /// <see cref="Kind"/> is one of <see cref="Kinds"/> (always set, also for resolved evaluations).</summary>
    public sealed record Evaluation(
        bool IsLineResolved,
        string? ReasonCode,
        decimal QuotationTotal,
        decimal GlobalDiscount,
        decimal ConsideredLineTotalSum,
        decimal Difference,
        string Kind);

    /// <summary>A line composes the quotation value when its reconciliation status is MAPPED / SUBSTITUTE / EXTRA_ITEM.
    /// A missing status (legacy rows) counts as MAPPED, mirroring the entity default.</summary>
    public static bool IsConsidered(string? reconciliationStatus)
        => string.IsNullOrWhiteSpace(reconciliationStatus)
           || RequestConstants.ReconciliationStatuses.Considered.Contains(reconciliationStatus, StringComparer.Ordinal);

    /// <summary>Write-path guard: true when the quotation-level discount is larger than the net of the considered lines after
    /// their own discounts (the taxable base would be clamped to 0 and the excess silently lost).</summary>
    public static bool GlobalDiscountExceedsNet(decimal netAfterItemDiscounts, decimal globalDiscount)
        => globalDiscount > 0m && globalDiscount > netAfterItemDiscounts;

    public static Evaluation Evaluate(decimal quotationTotal, decimal globalDiscount, IEnumerable<LineFacts> lines)
    {
        var sum = Math.Round(lines.Where(l => IsConsidered(l.ReconciliationStatus)).Sum(l => l.LineTotal), 2, MidpointRounding.AwayFromZero);
        var difference = Math.Round(sum - quotationTotal, 2, MidpointRounding.AwayFromZero);

        if (globalDiscount > 0m)
            return new Evaluation(false, Reasons.GlobalDiscountUnallocated, quotationTotal, globalDiscount, sum, difference, Kinds.GlobalDiscountUnallocated);

        if (quotationTotal > 0m && Math.Abs(difference) > LineSumTolerance)
            return new Evaluation(false, Reasons.LineTotalMismatch, quotationTotal, globalDiscount, sum, difference, Kinds.LineTotalMismatch);

        if (quotationTotal <= 0m)
        {
            var kind = sum > 0m ? Kinds.LegacyTotalNotComputed : Kinds.ZeroValue;
            return new Evaluation(true, null, quotationTotal, globalDiscount, sum, difference, kind);
        }

        return new Evaluation(true, null, quotationTotal, globalDiscount, sum, difference, Kinds.LineResolved);
    }
}
