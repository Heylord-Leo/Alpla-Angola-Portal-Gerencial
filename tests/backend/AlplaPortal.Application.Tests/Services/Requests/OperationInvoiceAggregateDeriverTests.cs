using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Doc = RequestConstants.OperationInvoiceDocumentStatuses;
using Agg = RequestConstants.OperationInvoiceStatuses;

/// <summary>
/// Cumulative coverage: how a PO group's operation-invoice state falls out of the allocations
/// pointing at it.
///
/// <para>The rule that shapes everything here is that <b>a partial invoice is not a divergence</b>.
/// A group expecting 10M and holding a validated 7M is legitimately mid-delivery, not in error —
/// so it must read as PARTIALLY_INVOICED and keep accepting documents until the cumulative total
/// arrives. The old single-invoice model could not express that state at all.</para>
/// </summary>
public class OperationInvoiceAggregateDeriverTests
{
    private static AllocationCoverage A(string status, decimal gross) => new(status, gross);

    private static OperationInvoiceCoverage Derive(
        decimal expected,
        IEnumerable<AllocationCoverage>? allocations = null,
        bool classified = true,
        bool requires = true,
        bool shortClosed = false) =>
        OperationInvoiceAggregateDeriver.Derive(
            classified, requires, expected,
            allocations ?? Enumerable.Empty<AllocationCoverage>(), shortClosed);

    // ── The states nothing else can override ──

    [Fact]
    public void An_unclassified_group_is_unclassified_whatever_it_holds()
    {
        // A document nobody has identified cannot be reasoned about — not even to say it is covered.
        var result = Derive(10_000_000m, new[] { A(Doc.Validated, 10_000_000m) }, classified: false);

        Assert.Equal(Agg.Unclassified, result.Status);
        Assert.True(Agg.IsBlocking(result.Status));
    }

    [Fact]
    public void A_group_owing_no_operation_invoice_is_not_required()
    {
        var result = Derive(0m, requires: false);

        Assert.Equal(Agg.NotRequired, result.Status);
        Assert.True(Agg.IsSatisfied(result.Status));
    }

    // ── Cumulative coverage ──

    [Fact]
    public void Two_invoices_of_seven_and_three_million_satisfy_a_ten_million_expectation()
    {
        var partial = Derive(10_000_000m, new[] { A(Doc.Validated, 7_000_000m) });

        Assert.Equal(Agg.PartiallyInvoiced, partial.Status);
        Assert.Equal(7_000_000m, partial.ValidatedTotal);
        Assert.Equal(3_000_000m, partial.RemainingAmount);

        var complete = Derive(10_000_000m, new[]
        {
            A(Doc.Validated, 7_000_000m),
            A(Doc.Validated, 3_000_000m)
        });

        Assert.Equal(Agg.Satisfied, complete.Status);
        Assert.Equal(0m, complete.RemainingAmount);
        Assert.True(Agg.IsSatisfied(complete.Status));
    }

    [Fact]
    public void A_partial_invoice_is_not_a_divergence_merely_for_being_below_the_total()
    {
        var result = Derive(10_000_000m, new[] { A(Doc.Validated, 1m) });

        Assert.Equal(Agg.PartiallyInvoiced, result.Status);
        Assert.NotEqual(Agg.DivergenceDetected, result.Status);
    }

    [Fact]
    public void Rounding_residue_from_a_consolidated_split_does_not_leave_a_group_short()
    {
        // 10M split three ways leaves cents behind. Tolerance absorbs it; without that the group
        // would sit one cêntimo from satisfied forever.
        var expected = 10_000_000m;
        var third = 3_333_333.33m;

        var result = Derive(expected, new[]
        {
            A(Doc.Validated, third), A(Doc.Validated, third), A(Doc.Validated, third)
        });

        Assert.Equal(Agg.Satisfied, result.Status);
        Assert.True(result.AppliedTolerance > 0m);
    }

    [Fact]
    public void Coverage_just_outside_tolerance_does_not_satisfy()
    {
        var expected = 10_000_000m;
        var tolerance = OperationInvoiceTolerance.For(expected);

        var result = Derive(expected, new[] { A(Doc.Validated, expected - tolerance - 1m) });

        Assert.Equal(Agg.PartiallyInvoiced, result.Status);
    }

    [Fact]
    public void Only_validated_invoices_count_toward_coverage()
    {
        var result = Derive(10_000_000m, new[]
        {
            A(Doc.Rejected, 5_000_000m),
            A(Doc.ReplacementRequested, 5_000_000m),
            A(Doc.Validated, 2_000_000m)
        });

        Assert.Equal(2_000_000m, result.ValidatedTotal);
        Assert.Equal(8_000_000m, result.RemainingAmount);
        Assert.Equal(Agg.PartiallyInvoiced, result.Status);
    }

    [Fact]
    public void A_rejected_invoice_leaves_a_group_pending_upload_never_satisfied()
    {
        var result = Derive(10_000_000m, new[] { A(Doc.Rejected, 10_000_000m) });

        Assert.Equal(Agg.PendingUpload, result.Status);
        Assert.False(Agg.IsSatisfied(result.Status));
    }

    // ── Precedence ──

    [Fact]
    public void Divergence_outranks_everything_that_could_otherwise_look_finished()
    {
        var result = Derive(10_000_000m, new[]
        {
            A(Doc.Validated, 10_000_000m),
            A(Doc.DivergenceDetected, 500_000m)
        });

        Assert.Equal(Agg.DivergenceDetected, result.Status);
    }

    [Fact]
    public void Work_with_finance_outranks_work_with_the_uploader()
    {
        // Finance can act without waiting for a third party, so their queue wins the label.
        var result = Derive(10_000_000m, new[]
        {
            A(Doc.Validated, 7_000_000m),
            A(Doc.PendingValidation, 3_000_000m)
        });

        Assert.Equal(Agg.PendingValidation, result.Status);
        Assert.Equal(3_000_000m, result.PendingValidationTotal);
        Assert.Equal(7_000_000m, result.ValidatedTotal);
    }

    [Fact]
    public void A_freshly_uploaded_invoice_already_reads_as_awaiting_finance()
    {
        var result = Derive(10_000_000m, new[] { A(Doc.Uploaded, 10_000_000m) });

        Assert.Equal(Agg.PendingValidation, result.Status);
    }

    // ── Short close ──

    [Fact]
    public void An_approved_short_close_satisfies_a_group_below_its_expected_total()
    {
        // Partial delivery accepted, remainder cancelled: without this exit the group could never
        // complete and would block the request forever.
        var result = Derive(10_000_000m, new[] { A(Doc.Validated, 6_000_000m) }, shortClosed: true);

        Assert.Equal(Agg.Satisfied, result.Status);
        Assert.True(result.ClosedShort);
        Assert.Equal(4_000_000m, result.RemainingAmount);   // still reported honestly
    }

    [Fact]
    public void A_short_close_does_not_override_an_unresolved_divergence()
    {
        var result = Derive(10_000_000m, new[] { A(Doc.DivergenceDetected, 6_000_000m) }, shortClosed: true);

        Assert.Equal(Agg.DivergenceDetected, result.Status);
    }

    // ── Purity ──

    [Fact]
    public void The_same_allocation_set_always_yields_the_same_status()
    {
        var allocations = new[] { A(Doc.Validated, 4_000_000m), A(Doc.PendingValidation, 1_000_000m) };

        var first = Derive(10_000_000m, allocations);
        var second = Derive(10_000_000m, allocations);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_group_with_nothing_at_all_is_pending_upload()
    {
        var result = Derive(10_000_000m);

        Assert.Equal(Agg.PendingUpload, result.Status);
        Assert.Equal(0m, result.ValidatedTotal);
        Assert.Equal(10_000_000m, result.RemainingAmount);
    }

    // ── The aggregate's contract with Release 4 ──

    [Fact]
    public void Only_not_required_and_satisfied_count_as_done()
    {
        Assert.True(Agg.IsSatisfied(Agg.NotRequired));
        Assert.True(Agg.IsSatisfied(Agg.Satisfied));

        foreach (var blocking in new[]
                 {
                     Agg.Unclassified, Agg.PendingUpload, Agg.PartiallyInvoiced,
                     Agg.PendingValidation, Agg.DivergenceDetected
                 })
        {
            Assert.False(Agg.IsSatisfied(blocking), blocking);
            Assert.True(Agg.IsBlocking(blocking), blocking);
        }
    }

    [Fact]
    public void A_satisfied_or_unclassified_group_accepts_no_further_upload()
    {
        Assert.False(Agg.AcceptsUploadIn(Agg.Satisfied));
        Assert.False(Agg.AcceptsUploadIn(Agg.NotRequired));
        Assert.False(Agg.AcceptsUploadIn(Agg.Unclassified));

        // A partially invoiced group must keep accepting them — that is the whole point.
        Assert.True(Agg.AcceptsUploadIn(Agg.PartiallyInvoiced));
        Assert.True(Agg.AcceptsUploadIn(Agg.PendingUpload));
    }
}
