using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;
using Proj = AlplaPortal.Domain.Services.BuyerQueueProjectionBuilder;
using Agg = AlplaPortal.Domain.Services.BuyerWorkloadAggregator;
using S = AlplaPortal.Domain.Constants.BuyerQueueConstants;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// Dashboard V2 slice B1+B2 — Buyer workload aggregation. Every scenario builds its projection with the
/// REAL <see cref="Proj"/> (the single canonical Buyer projection) and only then aggregates, so these
/// tests prove reconciliation-by-construction: the aggregator never re-classifies, it tallies the exact
/// projection the Buyer queue/workspace would produce for the same input.
/// </summary>
public class BuyerWorkloadAggregatorTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly DateTime Today = new(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc);

    // ── builders (mirror BuyerQueueProjectionBuilderTests) ──
    private static Proj.ItemInput Item(string? lifecycle = null, bool deleted = false, string? statusCode = null)
        => new(Guid.NewGuid(), deleted, lifecycle, statusCode, false);

    private static (Proj.QuotationItemInput qi, Proj.ItemInput item) MappedReady()
    {
        var item = Item(); // null lifecycle => eligible pool
        var qi = new Proj.QuotationItemInput(Guid.NewGuid(), item.Id, "MAPPED");
        return (qi, item);
    }

    private static Proj.BatchInput Batch(string status, params Guid[] lineItemIds)
        => new(Guid.NewGuid(), 1, status,
            lineItemIds.Select(id => new Proj.BatchItemInput(id, null, Array.Empty<Guid>())).ToList());

    private static Proj.RequestInput Req(
        IEnumerable<Proj.ItemInput>? items = null,
        IEnumerable<Proj.BatchInput>? batches = null,
        IEnumerable<Proj.QuotationItemInput>? quotationItems = null,
        Guid? buyerId = null,
        DateTime? needBy = null,
        string status = "WAITING_QUOTATION")
        => new(
            Guid.NewGuid(), "REQ", "T", "QUOTATION", status, false,
            buyerId, null, needBy, Today.AddDays(-10), false, false,
            (items ?? Array.Empty<Proj.ItemInput>()).ToList(),
            (batches ?? Array.Empty<Proj.BatchInput>()).ToList(),
            (quotationItems ?? Array.Empty<Proj.QuotationItemInput>()).ToList(),
            Array.Empty<Guid>());

    private static Agg.BuyerWorkloadItem W(Guid? buyerId, string? name, Proj.RequestInput r)
        => new(buyerId, name, Proj.Build(r, Me, Today));

    private static Agg.BuyerWorkloadMetrics Row(IReadOnlyList<Agg.BuyerWorkloadMetrics> m, Guid buyerId)
        => m.Single(x => !x.IsUnassigned && x.BuyerId == buyerId);
    private static Agg.BuyerWorkloadMetrics Unassigned(IReadOnlyList<Agg.BuyerWorkloadMetrics> m)
        => m.Single(x => x.IsUnassigned);

    // ── a NeedsQuotation request (all pending) ──
    private static Proj.RequestInput NeedsQuotation(Guid? buyer, DateTime? needBy = null)
        => Req(items: new[] { Item(), Item() }, buyerId: buyer, needBy: needBy);

    // ── a PartialCoverage request (one ready + one pending) ──
    private static Proj.RequestInput PartialCoverage(Guid? buyer, DateTime? needBy = null)
    {
        var (qi, ready) = MappedReady();
        return Req(items: new[] { ready, Item() }, quotationItems: new[] { qi }, buyerId: buyer, needBy: needBy);
    }

    // ── a ReadyForApproval request (all covered, none pending) ──
    private static Proj.RequestInput ReadyForApproval(Guid? buyer, DateTime? needBy = null)
    {
        var (qi, ready) = MappedReady();
        return Req(items: new[] { ready }, quotationItems: new[] { qi }, buyerId: buyer, needBy: needBy);
    }

    // ── an AdjustmentRequired request (active adjustment batch) ──
    private static Proj.RequestInput AdjustmentRequired(Guid? buyer, DateTime? needBy = null)
    {
        var it = Item();
        return Req(items: new[] { it }, batches: new[] { Batch("AREA_ADJUSTMENT", it.Id) }, buyerId: buyer, needBy: needBy);
    }

    // ── an AwaitingApproval request (non-actionable for buyer) ──
    private static Proj.RequestInput AwaitingApproval(Guid? buyer, DateTime? needBy = null)
    {
        var it = Item();
        return Req(items: new[] { it }, batches: new[] { Batch("WAITING_AREA_APPROVAL", it.Id) }, buyerId: buyer, needBy: needBy);
    }

    // ════════════════════ ownership / assignment ════════════════════

    [Fact]
    public void Assigned_request_counts_only_for_its_buyer()
    {
        var m = Agg.Aggregate(new[]
        {
            W(Me, "Me", NeedsQuotation(Me)),
            W(Other, "Other", NeedsQuotation(Other)),
        });

        Assert.Equal(1, Row(m, Me).AssignedRequests);
        Assert.Equal(1, Row(m, Other).AssignedRequests);
        Assert.DoesNotContain(m, x => x.IsUnassigned);
    }

    [Fact]
    public void Unassigned_never_counts_as_a_buyer_and_lands_in_shared_bucket()
    {
        var m = Agg.Aggregate(new[]
        {
            W(Me, "Me", NeedsQuotation(Me)),
            W(null, null, NeedsQuotation(null)),
            W(null, null, PartialCoverage(null)),
        });

        Assert.Equal(1, Row(m, Me).AssignedRequests);            // personal untouched by unassigned
        var shared = Unassigned(m);
        Assert.True(shared.IsUnassigned);
        Assert.Null(shared.BuyerId);
        Assert.Equal(2, shared.AssignedRequests);
    }

    // ════════════════════ operational-state tallies (reconciliation) ════════════════════

    [Fact]
    public void NeedsQuotation_partial_ready_adjustment_are_tallied_by_canonical_state()
    {
        // Sanity: the canonical builder classifies these as we expect (no separate classifier).
        Assert.Equal(S.OperationalStates.NeedsQuotation, Proj.Build(NeedsQuotation(Me), Me, Today).OperationalState);
        Assert.Equal(S.OperationalStates.PartialCoverage, Proj.Build(PartialCoverage(Me), Me, Today).OperationalState);
        Assert.Equal(S.OperationalStates.ReadyForApproval, Proj.Build(ReadyForApproval(Me), Me, Today).OperationalState);
        Assert.Equal(S.OperationalStates.AdjustmentRequired, Proj.Build(AdjustmentRequired(Me), Me, Today).OperationalState);

        var m = Agg.Aggregate(new[]
        {
            W(Me, "Me", NeedsQuotation(Me)),
            W(Me, "Me", PartialCoverage(Me)),
            W(Me, "Me", ReadyForApproval(Me)),
            W(Me, "Me", AdjustmentRequired(Me)),
        });

        var r = Row(m, Me);
        Assert.Equal(4, r.AssignedRequests);
        Assert.Equal(4, r.ActionableRequests);                 // all four expose a buyer action
        Assert.Equal(1, r.NeedsQuotationRequests);
        Assert.Equal(1, r.PartialCoverageRequests);
        Assert.Equal(1, r.ReadyForApprovalRequests);
        Assert.Equal(1, r.AdjustmentRequests);
    }

    [Fact]
    public void Item_totals_sum_pending_and_ready()
    {
        var m = Agg.Aggregate(new[]
        {
            W(Me, "Me", NeedsQuotation(Me)),   // 2 pending, 0 ready
            W(Me, "Me", PartialCoverage(Me)),  // 1 pending, 1 ready
            W(Me, "Me", ReadyForApproval(Me)), // 0 pending, 1 ready
        });
        var r = Row(m, Me);
        Assert.Equal(3, r.PendingQuotationItems);
        Assert.Equal(2, r.ReadyForBatchItems);
    }

    // ════════════════════ urgency (PD-03) ════════════════════

    [Fact]
    public void Overdue_and_critical_only_when_a_buyer_action_is_open()
    {
        var m = Agg.Aggregate(new[]
        {
            W(Me, "Me", NeedsQuotation(Me, needBy: Today.AddDays(-1))), // overdue + actionable
            W(Me, "Me", NeedsQuotation(Me, needBy: Today)),            // critical today + actionable
            W(Me, "Me", NeedsQuotation(Me, needBy: Today.AddDays(2))), // approaching — neither
            W(Me, "Me", NeedsQuotation(Me, needBy: Today.AddDays(30))),// normal — neither
        });
        var r = Row(m, Me);
        Assert.Equal(1, r.OverdueActionableRequests);
        Assert.Equal(1, r.CriticalActionableRequests);
    }

    [Fact]
    public void Non_actionable_request_never_becomes_buyer_overdue()
    {
        // AwaitingApproval: buyer has no open action; a past NeedByDate must NOT flag buyer-overdue.
        Assert.Equal(S.OperationalStates.AwaitingApproval,
            Proj.Build(AwaitingApproval(Me, Today.AddDays(-5)), Me, Today).OperationalState);

        var m = Agg.Aggregate(new[] { W(Me, "Me", AwaitingApproval(Me, needBy: Today.AddDays(-5))) });
        var r = Row(m, Me);
        Assert.Equal(0, r.ActionableRequests);
        Assert.Equal(0, r.OverdueActionableRequests);
        Assert.Equal(0, r.CriticalActionableRequests);
    }

    // ════════════════════ shape ════════════════════

    [Fact]
    public void Multiple_buyers_each_get_a_row()
    {
        var m = Agg.Aggregate(new[]
        {
            W(Me, "Me", NeedsQuotation(Me)),
            W(Other, "Other", NeedsQuotation(Other)),
            W(null, null, NeedsQuotation(null)),
        });
        Assert.Equal(2, m.Count(x => !x.IsUnassigned));
        Assert.Equal(1, m.Count(x => x.IsUnassigned));
    }

    [Fact]
    public void Empty_input_yields_no_rows()
    {
        Assert.Empty(Agg.Aggregate(Array.Empty<Agg.BuyerWorkloadItem>()));
    }
}
