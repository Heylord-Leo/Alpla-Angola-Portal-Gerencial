using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Domain.Services;

/// <summary>The outcome of a residual reconciliation over one document's lines.</summary>
public sealed record RoundingResidualPlan
{
    /// <summary>DeclaredGross − Σ(line totals), signed. Zero when the sums already agree.</summary>
    public decimal Residual { get; init; }

    /// <summary>True only when the residual was actually attributed to a line.</summary>
    public bool Applied { get; init; }

    /// <summary>Index (in document order) of the line that carries the residual, or null.</summary>
    public int? AdjustedIndex { get; init; }

    /// <summary>Every line's total after reconciliation. Same order and count as the input.</summary>
    public IReadOnlyList<decimal> Totals { get; init; } = Array.Empty<decimal>();
}

/// <summary>
/// MODEL 2 of the approved monetary reconciliation (v2.229.10): a supplier document's declared
/// gross total is the authoritative payable amount, and a cent-level difference against the
/// VAT-inclusive line sum — the inevitable artifact of per-line rounding — is attributed
/// deterministically to the LAST eligible line, so that one monetary truth flows downstream:
/// Σ(item totals) == document gross == group total == expected/paid amount.
///
/// <para>Only the line's final monetary total may carry the adjustment. Quantity, unit price,
/// discount and tax rate are extracted commercial components and are never altered.</para>
///
/// <para>The allocation cap is deliberately NOT <c>FinancialIntegrity.CalculateTolerance</c>
/// (0.1% would call a 3,000-AOA gap "rounding" on a 3.4M document). Each line's total is rounded
/// at most to the cent, so per-line rounding can explain at most one cent per line — hence
/// <c>|residual| ≤ 0.01 × lineCount</c>. Anything larger is a real mismatch and must stay visible.</para>
///
/// <para>Pure, decimal-only (integer cents internally — no floating point), deterministic and
/// idempotent: reconciling already-reconciled totals yields residual 0 and changes nothing.
/// The frontend mirrors this rule in <c>paymentRequestCreation.ts</c>; this is the tested
/// reference implementation.</para>
/// </summary>
public static class PaymentRoundingResidual
{
    /// <summary>
    /// Strict internal-consistency bound for a declared Net/Tax/Gross triplet: plain cent-level
    /// arithmetic only. A triplet that fails this is not authoritative and is never "rescued" by
    /// the far looser financial-integrity tolerance.
    /// </summary>
    public const decimal TripletConsistencyTolerance = 0.01m;

    /// <summary>Maximum residual attributable to rounding, per line.</summary>
    public const decimal PerLineResidualCap = 0.01m;

    /// <summary>
    /// Whether a declared document triplet is internally consistent enough to be authoritative.
    /// Requires positive net and gross; a missing tax is derived as gross − net by the caller and
    /// therefore consistent by construction.
    /// </summary>
    public static bool IsConsistentTriplet(decimal? net, decimal? tax, decimal? gross)
    {
        if (net is not decimal n || gross is not decimal g) return false;
        if (n <= 0m || g <= 0m) return false;

        var t = tax ?? g - n;
        if (t < 0m) return false;

        return Math.Abs(n + t - g) <= TripletConsistencyTolerance;
    }

    /// <summary>
    /// Reconciles a document's line totals against its declared gross.
    ///
    /// <para>Input totals must be the CANONICAL per-line values (each line's own arithmetic,
    /// rounded to the cent). The residual is applied to the last line, in document order, whose
    /// total is positive and remains positive after the adjustment — deterministic, so two
    /// independent reads of the same document always adjust the same line by the same amount,
    /// which is what keeps the duplicate content fingerprint stable.</para>
    /// </summary>
    public static RoundingResidualPlan Allocate(IReadOnlyList<decimal> lineTotals, decimal? declaredGross)
    {
        var totals = lineTotals.ToArray();

        if (declaredGross is not decimal gross || gross <= 0m || totals.Length == 0)
            return new RoundingResidualPlan { Totals = totals };

        // Integer cents throughout: no accumulation drift, exact signed arithmetic.
        var grossCents = ToCents(gross);
        var sumCents = totals.Sum(ToCents);
        var residualCents = grossCents - sumCents;

        if (residualCents == 0)
            return new RoundingResidualPlan { Totals = totals };

        var eligibleCount = totals.Count(t => t > 0m);
        var residual = residualCents / 100m;

        if (eligibleCount == 0 || Math.Abs(residual) > PerLineResidualCap * eligibleCount)
            return new RoundingResidualPlan { Residual = residual, Totals = totals };

        // Last eligible line in document order whose total survives the adjustment positive.
        for (var i = totals.Length - 1; i >= 0; i--)
        {
            if (totals[i] <= 0m) continue;

            var adjusted = (ToCents(totals[i]) + residualCents) / 100m;
            if (adjusted <= 0m) continue;

            totals[i] = adjusted;
            return new RoundingResidualPlan
            {
                Residual = residual,
                Applied = true,
                AdjustedIndex = i,
                Totals = totals
            };
        }

        return new RoundingResidualPlan { Residual = residual, Totals = totals };
    }

    private static long ToCents(decimal value) => (long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero);
}
