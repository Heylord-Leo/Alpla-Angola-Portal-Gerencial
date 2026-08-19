using System.Linq;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// MODEL 2 of the v2.229.10 monetary reconciliation — the rounding-residual rule, pinned on the
/// backend reference implementation that the frontend mirrors.
///
/// <para>The real case behind every number here: CONSULTIT CCTV Viana02 declares
/// Net 3,011,866.27 / IVA 421,661.28 / Gross 3,433,527.55, while the Portal's per-line-rounded
/// VAT-inclusive line sum reaches 3,433,527.54. The missing cent is the supplier's document-level
/// arithmetic versus per-line rounding — not an error on either side — and it must end up
/// attributed to a line so ONE total flows downstream instead of two.</para>
/// </summary>
public class PaymentRoundingResidualTests
{
    // ── Declared-triplet consistency (the strict documentary rule, never 0.1%) ──────────────

    [Fact]
    public void The_consultit_declared_triplet_is_consistent()
    {
        Assert.True(PaymentRoundingResidual.IsConsistentTriplet(
            3_011_866.27m, 421_661.28m, 3_433_527.55m));
    }

    [Fact]
    public void A_missing_tax_is_derived_and_therefore_consistent()
    {
        Assert.True(PaymentRoundingResidual.IsConsistentTriplet(
            3_011_866.27m, null, 3_433_527.55m));
    }

    [Fact]
    public void An_arithmetically_broken_triplet_is_never_authoritative()
    {
        // K: net + tax misses gross by 1,000 AOA. The 0.1% FinancialIntegrity tolerance would
        // forgive ~3,433 AOA here — which is exactly why it must NOT be the consistency rule.
        Assert.False(PaymentRoundingResidual.IsConsistentTriplet(
            3_011_866.27m, 421_661.28m, 3_434_527.55m));

        Assert.False(PaymentRoundingResidual.IsConsistentTriplet(null, 1m, 2m));
        Assert.False(PaymentRoundingResidual.IsConsistentTriplet(0m, 0m, 0m));
        Assert.False(PaymentRoundingResidual.IsConsistentTriplet(10m, -1m, 9m));
    }

    [Fact]
    public void One_cent_of_declared_arithmetic_noise_is_tolerated()
    {
        Assert.True(PaymentRoundingResidual.IsConsistentTriplet(100.00m, 14.00m, 114.01m));
        Assert.False(PaymentRoundingResidual.IsConsistentTriplet(100.00m, 14.00m, 114.02m));
    }

    // ── A. The CONSULTIT case ───────────────────────────────────────────────────────────────

    [Fact]
    public void The_consultit_residual_lands_on_the_last_line_and_the_sums_close()
    {
        // Three lines summing to 3,433,527.54 against a declared 3,433,527.55.
        var lines = new[] { 1_000_000.00m, 1_433_527.54m, 1_000_000.00m };

        var plan = PaymentRoundingResidual.Allocate(lines, 3_433_527.55m);

        Assert.True(plan.Applied);
        Assert.Equal(0.01m, plan.Residual);
        Assert.Equal(2, plan.AdjustedIndex);                       // last line, document order
        Assert.Equal(1_000_000.01m, plan.Totals[2]);
        Assert.Equal(3_433_527.55m, plan.Totals.Sum());            // ONE monetary truth
        Assert.Equal(1_000_000.00m, plan.Totals[0]);               // untouched
        Assert.Equal(1_433_527.54m, plan.Totals[1]);
    }

    // ── B. Determinism / idempotence ────────────────────────────────────────────────────────

    [Fact]
    public void The_same_inputs_always_produce_the_same_allocation()
    {
        var lines = new[] { 538_634.59m, 472_486.48m, 250_000.00m };

        var first = PaymentRoundingResidual.Allocate(lines, 1_261_121.08m);
        var second = PaymentRoundingResidual.Allocate(lines, 1_261_121.08m);

        Assert.Equal(first.AdjustedIndex, second.AdjustedIndex);
        Assert.Equal(first.Totals, second.Totals);
    }

    [Fact]
    public void Reconciling_already_reconciled_totals_changes_nothing()
    {
        var lines = new[] { 1_000_000.00m, 1_433_527.54m, 1_000_000.00m };
        var once = PaymentRoundingResidual.Allocate(lines, 3_433_527.55m);

        var twice = PaymentRoundingResidual.Allocate(once.Totals, 3_433_527.55m);

        Assert.False(twice.Applied);
        Assert.Equal(0m, twice.Residual);
        Assert.Equal(once.Totals, twice.Totals);
    }

    // ── C. Negative residual ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_negative_residual_subtracts_from_the_last_eligible_line()
    {
        var lines = new[] { 1_000_000.00m, 2_433_527.54m };

        var plan = PaymentRoundingResidual.Allocate(lines, 3_433_527.53m);

        Assert.True(plan.Applied);
        Assert.Equal(-0.01m, plan.Residual);
        Assert.Equal(1, plan.AdjustedIndex);
        Assert.Equal(2_433_527.53m, plan.Totals[1]);
        Assert.Equal(3_433_527.53m, plan.Totals.Sum());
    }

    [Fact]
    public void A_line_that_would_go_non_positive_is_skipped()
    {
        // The last line holds exactly one cent; a −0.01 residual would zero it, so the
        // adjustment moves to the previous eligible line.
        var lines = new[] { 10.00m, 0.01m };

        var plan = PaymentRoundingResidual.Allocate(lines, 10.00m);

        Assert.True(plan.Applied);
        Assert.Equal(-0.01m, plan.Residual);
        Assert.Equal(0, plan.AdjustedIndex);
        Assert.Equal(9.99m, plan.Totals[0]);
        Assert.Equal(0.01m, plan.Totals[1]);
        Assert.Equal(10.00m, plan.Totals.Sum());
    }

    // ── D. Zero residual ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Agreeing_sums_are_left_completely_alone()
    {
        var lines = new[] { 1_000_000.00m, 2_433_527.55m };

        var plan = PaymentRoundingResidual.Allocate(lines, 3_433_527.55m);

        Assert.False(plan.Applied);
        Assert.Equal(0m, plan.Residual);
        Assert.Null(plan.AdjustedIndex);
        Assert.Equal(lines, plan.Totals);
    }

    // ── E. Oversized residual — never disguised as rounding ─────────────────────────────────

    [Fact]
    public void A_residual_beyond_one_cent_per_line_is_not_allocated()
    {
        // Three lines allow at most 0.03. A 0.04 gap — and certainly a 100 AOA or 3,000 AOA
        // gap — is a real mismatch that must stay visible, even though the 0.1%
        // FinancialIntegrity tolerance would happily absorb far more on a document this size.
        var lines = new[] { 1_000_000.00m, 1_433_527.51m, 1_000_000.00m };

        var plan = PaymentRoundingResidual.Allocate(lines, 3_433_527.55m);

        Assert.False(plan.Applied);
        Assert.Equal(0.04m, plan.Residual);
        Assert.Equal(lines, plan.Totals);

        var large = PaymentRoundingResidual.Allocate(lines, 3_436_527.51m);   // +3,000 AOA
        Assert.False(large.Applied);
    }

    [Fact]
    public void The_cap_scales_with_the_number_of_eligible_lines()
    {
        // Four lines → up to 0.04 is attributable to rounding.
        var lines = new[] { 100.00m, 100.00m, 100.00m, 99.96m };

        var plan = PaymentRoundingResidual.Allocate(lines, 400.00m);

        Assert.True(plan.Applied);
        Assert.Equal(0.04m, plan.Residual);
        Assert.Equal(3, plan.AdjustedIndex);
        Assert.Equal(400.00m, plan.Totals.Sum());
    }

    // ── Guard rails ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void No_declared_gross_or_no_lines_means_no_allocation()
    {
        Assert.False(PaymentRoundingResidual.Allocate(new[] { 10m }, null).Applied);
        Assert.False(PaymentRoundingResidual.Allocate(new[] { 10m }, 0m).Applied);
        Assert.False(PaymentRoundingResidual.Allocate(System.Array.Empty<decimal>(), 10m).Applied);
    }

    [Fact]
    public void Zero_valued_lines_are_never_eligible_carriers()
    {
        var lines = new[] { 10.00m, 0m };

        var plan = PaymentRoundingResidual.Allocate(lines, 10.01m);

        Assert.True(plan.Applied);
        Assert.Equal(0, plan.AdjustedIndex);   // the zero line is skipped
        Assert.Equal(10.01m, plan.Totals[0]);
        Assert.Equal(0m, plan.Totals[1]);
    }
}
