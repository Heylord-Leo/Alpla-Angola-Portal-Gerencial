using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Doc = RequestConstants.OperationInvoiceDocumentStatuses;
using Agg = RequestConstants.OperationInvoiceStatuses;
using Types = RequestConstants.SourceDocumentTypes;
using Reasons = OperationInvoiceObligationReasons;

/// <summary>
/// The Release 4 Phase 1 projection: composing the existing resolver and aggregate deriver into
/// one explained, request-level answer, without writing anything.
///
/// <para>Two honesty rules shape these tests. A group whose expected total was never captured
/// reports an UNKNOWN remainder, never zero. And a cached status that disagrees with the
/// recomputed one is reported as drift, never silently repaired.</para>
/// </summary>
public class OperationInvoiceObligationProjectorTests
{
    private static readonly Guid GroupId = Guid.NewGuid();

    private static OperationInvoiceObligationGroupSnapshot Group(
        string? type = Types.Proforma,
        decimal? expected = 10_000_000m,
        string persisted = Agg.PendingUpload,
        IReadOnlyList<AllocationCoverage>? allocations = null,
        string? shortClose = null,
        string? currency = "AOA") => new()
    {
        GroupId = GroupId,
        SourceDocumentType = type,
        ExpectedTotal = expected,
        ExpectedCurrency = currency,
        CurrencyCode = currency,
        PersistedStatus = persisted,
        Allocations = allocations ?? Array.Empty<AllocationCoverage>(),
        ActiveShortCloseStatus = shortClose
    };

    private static OperationInvoiceObligation Single(OperationInvoiceObligationGroupSnapshot snapshot) =>
        Assert.Single(OperationInvoiceObligationProjector.Project(new[] { snapshot }).Obligations);

    private static AllocationCoverage A(string status, decimal gross) => new(status, gross);

    // ── The base states ──

    [Fact]
    public void A_proforma_group_with_nothing_received_awaits_its_operation_invoice()
    {
        var result = Single(Group());

        Assert.Equal(Agg.PendingUpload, result.DerivedStatus);
        Assert.True(result.RequiresOperationInvoice);
        Assert.Equal(10_000_000m, result.ExpectedAmount);
        Assert.Equal(10_000_000m, result.RemainingAmount);
        Assert.Equal(Reasons.AwaitingOperationInvoice, result.ReasonCode);
    }

    [Fact]
    public void A_factura_group_owes_nothing_further()
    {
        var result = Single(Group(type: Types.Invoice, persisted: Agg.NotRequired));

        Assert.Equal(Agg.NotRequired, result.DerivedStatus);
        Assert.False(result.RequiresOperationInvoice);
        Assert.False(result.StatusDrift);
        Assert.Equal(Reasons.SourceAlreadyDocumentsOperation, result.ReasonCode);
    }

    [Fact]
    public void An_unclassified_group_reports_classification_as_the_blocker()
    {
        var result = Single(Group(type: null, persisted: Agg.Unclassified));

        Assert.Equal(Agg.Unclassified, result.DerivedStatus);
        Assert.Null(result.SourceDocumentType);
        Assert.Equal(Reasons.ClassificationPending, result.ReasonCode);
    }

    [Fact]
    public void The_legacy_final_invoice_alias_reads_as_a_factura()
    {
        var result = Single(Group(type: Types.LegacyFinalInvoice, persisted: Agg.NotRequired));

        Assert.Equal(Types.Invoice, result.SourceDocumentType);
        Assert.Equal(Agg.NotRequired, result.DerivedStatus);
    }

    // ── Coverage ──

    [Fact]
    public void Partial_validated_coverage_reads_as_partially_invoiced_with_the_remainder()
    {
        var result = Single(Group(
            persisted: Agg.PartiallyInvoiced,
            allocations: new[] { A(Doc.Validated, 6_000_000m) }));

        Assert.Equal(Agg.PartiallyInvoiced, result.DerivedStatus);
        Assert.Equal(6_000_000m, result.ValidatedCoveredAmount);
        Assert.Equal(4_000_000m, result.RemainingAmount);
        Assert.Equal(Reasons.PartiallyCovered, result.ReasonCode);
    }

    [Fact]
    public void Coverage_within_tolerance_satisfies_the_obligation()
    {
        var expected = 10_000_000m;
        var tolerance = OperationInvoiceTolerance.For(expected);

        var result = Single(Group(
            persisted: Agg.Satisfied,
            allocations: new[] { A(Doc.Validated, expected - tolerance) }));

        Assert.Equal(Agg.Satisfied, result.DerivedStatus);
        Assert.False(result.ClosedShort);
        Assert.Equal(Reasons.SatisfiedByCoverage, result.ReasonCode);
    }

    [Fact]
    public void Amounts_awaiting_finance_are_reported_as_pending_not_validated()
    {
        var result = Single(Group(
            persisted: Agg.PendingValidation,
            allocations: new[]
            {
                A(Doc.Validated, 4_000_000m),
                A(Doc.PendingValidation, 3_000_000m)
            }));

        Assert.Equal(Agg.PendingValidation, result.DerivedStatus);
        Assert.Equal(4_000_000m, result.ValidatedCoveredAmount);
        Assert.Equal(3_000_000m, result.PendingCoveredAmount);
        Assert.Equal(Reasons.AwaitingFinanceValidation, result.ReasonCode);
    }

    [Fact]
    public void A_divergence_outranks_coverage_and_demands_a_finance_decision()
    {
        var result = Single(Group(
            persisted: Agg.DivergenceDetected,
            allocations: new[]
            {
                A(Doc.Validated, 10_000_000m),
                A(Doc.DivergenceDetected, 500_000m)
            }));

        Assert.Equal(Agg.DivergenceDetected, result.DerivedStatus);
        Assert.Equal(Reasons.DivergenceRequiresFinanceDecision, result.ReasonCode);
    }

    [Fact]
    public void An_approved_short_close_satisfies_below_the_expected_total()
    {
        var result = Single(Group(
            persisted: Agg.Satisfied,
            allocations: new[] { A(Doc.Validated, 6_000_000m) },
            shortClose: RequestConstants.ShortCloseStatuses.Approved));

        Assert.Equal(Agg.Satisfied, result.DerivedStatus);
        Assert.True(result.ClosedShort);
        Assert.Equal(Reasons.SatisfiedByShortClose, result.ReasonCode);
        Assert.Equal(4_000_000m, result.RemainingAmount);   // still reported honestly
    }

    [Fact]
    public void A_merely_proposed_short_close_satisfies_nothing()
    {
        var result = Single(Group(
            persisted: Agg.PartiallyInvoiced,
            allocations: new[] { A(Doc.Validated, 6_000_000m) },
            shortClose: RequestConstants.ShortCloseStatuses.Proposed));

        Assert.Equal(Agg.PartiallyInvoiced, result.DerivedStatus);
        Assert.False(result.ClosedShort);
    }

    // ── Honesty about unknown expected totals ──

    [Fact]
    public void A_required_obligation_with_no_recorded_total_reports_unknown_never_zero()
    {
        var result = Single(Group(expected: null));

        Assert.True(result.RequiresOperationInvoice);
        Assert.Null(result.ExpectedAmount);
        Assert.Null(result.RemainingAmount);
        Assert.Equal(Reasons.ExpectedTotalUnknown, result.ReasonCode);
        Assert.Equal(Agg.PendingUpload, result.DerivedStatus);
    }

    [Fact]
    public void A_legacy_pre_flag_group_remains_honestly_unclassified()
    {
        // Groups created before the feature: no type, no expected total, schema-default status.
        var result = Single(Group(type: null, expected: null, persisted: Agg.Unclassified, currency: null));

        Assert.Equal(Agg.Unclassified, result.DerivedStatus);
        Assert.False(result.StatusDrift);
        Assert.Null(result.ExpectedAmount);
        Assert.Null(result.RemainingAmount);
    }

    // ── Drift ──

    [Fact]
    public void A_cached_status_matching_the_recomputed_one_reports_no_drift()
    {
        var result = Single(Group(persisted: Agg.PendingUpload));

        Assert.Equal(result.PersistedStatus, result.DerivedStatus);
        Assert.False(result.StatusDrift);
    }

    [Fact]
    public void A_cached_status_contradicting_the_recomputed_one_reports_drift_without_repair()
    {
        // The restamping-gap scenario: classification changed to Factura after group creation,
        // but the cached column still says an invoice is owed.
        var result = Single(Group(type: Types.Invoice, persisted: Agg.PendingUpload));

        Assert.Equal(Agg.NotRequired, result.DerivedStatus);
        Assert.Equal(Agg.PendingUpload, result.PersistedStatus);
        Assert.True(result.StatusDrift);
    }

    // ── Traceability ──

    [Fact]
    public void Contributing_source_documents_and_line_context_survive_the_projection()
    {
        var docIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var lineIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var snapshot = Group() with
        {
            SourceDocumentIds = docIds,
            LineItemIds = lineIds,
            PurchaseOrderNumber = "45000123"
        };

        var result = Single(snapshot);

        Assert.Equal(docIds, result.SourceDocumentIds);
        Assert.Equal(3, result.LineItemCount);
        Assert.Equal("45000123", result.PurchaseOrderNumber);
    }

    // ── The rollup ──

    [Fact]
    public void The_rollup_counts_each_state_family_and_sums_per_currency()
    {
        var groups = new[]
        {
            Group() with { GroupId = Guid.NewGuid() },                                      // AOA, pending upload
            Group(allocations: new[] { A(Doc.Validated, 10_000_000m) },
                persisted: Agg.Satisfied) with { GroupId = Guid.NewGuid() },                // AOA, satisfied
            Group(type: Types.Invoice, persisted: Agg.NotRequired)
                with { GroupId = Guid.NewGuid() },                                          // not required
            Group(type: null, expected: null, persisted: Agg.Unclassified, currency: null)
                with { GroupId = Guid.NewGuid() },                                          // legacy
            Group(expected: 5_000m, currency: "EUR") with { GroupId = Guid.NewGuid() }      // EUR, pending upload
        };

        var rollup = OperationInvoiceObligationProjector.Project(groups).Rollup;

        Assert.Equal(5, rollup.TotalGroups);
        Assert.Equal(4, rollup.RequiringOperationInvoiceCount);   // all but the Factura
        Assert.Equal(3, rollup.PendingActionCount);               // 2× pending upload + unclassified
        Assert.Equal(1, rollup.SatisfiedCount);
        Assert.Equal(1, rollup.NotRequiredCount);
        Assert.Equal(1, rollup.UnclassifiedCount);
        Assert.False(rollup.HasStatusDrift);

        var aoa = Assert.Single(rollup.CurrencyTotals, c => c.CurrencyCode == "AOA");
        Assert.Equal(20_000_000m, aoa.ExpectedTotal);
        Assert.Equal(10_000_000m, aoa.ValidatedTotal);
        Assert.Equal(10_000_000m, aoa.RemainingTotal);

        var eur = Assert.Single(rollup.CurrencyTotals, c => c.CurrencyCode == "EUR");
        Assert.Equal(5_000m, eur.ExpectedTotal);

        var unknown = Assert.Single(rollup.CurrencyTotals, c => c.CurrencyCode == "UNKNOWN");
        Assert.Equal(1, unknown.GroupsWithUnknownExpectedTotal);
        Assert.Equal(0m, unknown.ExpectedTotal);
    }

    [Fact]
    public void Groups_owing_nothing_never_inflate_the_pending_action_count()
    {
        var groups = new[]
        {
            Group(type: Types.Invoice, persisted: Agg.NotRequired) with { GroupId = Guid.NewGuid() },
            Group(type: Types.InvoiceReceipt, persisted: Agg.NotRequired) with { GroupId = Guid.NewGuid() }
        };

        var rollup = OperationInvoiceObligationProjector.Project(groups).Rollup;

        Assert.Equal(0, rollup.PendingActionCount);
        Assert.Equal(0, rollup.RequiringOperationInvoiceCount);
        Assert.Empty(rollup.CurrencyTotals);
    }

    [Fact]
    public void Drifting_groups_are_counted_for_the_diagnostic_rollup()
    {
        var groups = new[]
        {
            Group(type: Types.Invoice, persisted: Agg.PendingUpload) with { GroupId = Guid.NewGuid() },
            Group() with { GroupId = Guid.NewGuid() }
        };

        var rollup = OperationInvoiceObligationProjector.Project(groups).Rollup;

        Assert.Equal(1, rollup.DriftCount);
        Assert.True(rollup.HasStatusDrift);
    }

    // ── Purity ──

    [Fact]
    public void Projection_neither_mutates_its_inputs_nor_varies_between_runs()
    {
        var allocations = new List<AllocationCoverage> { A(Doc.Validated, 4_000_000m) };
        var docIds = new List<Guid> { Guid.NewGuid() };
        var snapshot = Group(allocations: allocations) with { SourceDocumentIds = docIds };
        var pristine = snapshot with { };   // shares the same lists — mutation would show in both

        var first = OperationInvoiceObligationProjector.Project(new[] { snapshot });
        var second = OperationInvoiceObligationProjector.Project(new[] { snapshot });

        Assert.Equal(pristine, snapshot);
        Assert.Single(allocations);
        Assert.Equal(4_000_000m, allocations[0].AllocatedGrossAmount);
        Assert.Single(docIds);

        Assert.Equal(first.Obligations[0], second.Obligations[0]);
        Assert.Equal(first.Rollup.PendingActionCount, second.Rollup.PendingActionCount);
    }

    [Fact]
    public void An_empty_request_projects_to_an_empty_answer()
    {
        var projection = OperationInvoiceObligationProjector.Project(
            Array.Empty<OperationInvoiceObligationGroupSnapshot>());

        Assert.Empty(projection.Obligations);
        Assert.Equal(0, projection.Rollup.TotalGroups);
        Assert.False(projection.Rollup.HasStatusDrift);
    }
}
