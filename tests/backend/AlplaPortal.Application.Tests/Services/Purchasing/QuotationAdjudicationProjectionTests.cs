using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;
using P = AlplaPortal.Domain.Services.QuotationAdjudicationProjection;
using R = AlplaPortal.Domain.Services.QuotationReuseEligibilityPolicy;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// v2.249.5 — lifecycle context of saved quotations, per item, from batches / lines / the reuse rule only
/// (docs/PO_EVIDENCE_AND_QUOTATION_LIFECYCLE_v2.249.5.md §3). The REQ-628 shape: TREVOTECH awarded on items 1+2 (batch 1),
/// P.O. group cancelled with RESTART_QUOTATION, item 1 re-quoted and awarded to MAQSIS (batch 2).
/// </summary>
public class QuotationAdjudicationProjectionTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Line1 = Guid.NewGuid(), Line2 = Guid.NewGuid();
    private static readonly Guid Trevo = Guid.NewGuid(), Maqsis = Guid.NewGuid();
    private static readonly Guid T1 = Guid.NewGuid(), T2 = Guid.NewGuid(), M1 = Guid.NewGuid();
    private static readonly Guid Batch1 = Guid.NewGuid(), Batch2 = Guid.NewGuid();

    private static IReadOnlyList<P.QuotationFacts> Quotations() => new[] { new P.QuotationFacts(Trevo, "TREVOTECH"), new P.QuotationFacts(Maqsis, "MAQSIS") };
    private static IReadOnlyList<P.QuotationItemFacts> Items() => new[]
    {
        new P.QuotationItemFacts(T1, Trevo, Line1, RequestConstants.ReconciliationStatuses.Mapped),
        new P.QuotationItemFacts(T2, Trevo, Line2, RequestConstants.ReconciliationStatuses.Mapped),
        new P.QuotationItemFacts(M1, Maqsis, Line1, RequestConstants.ReconciliationStatuses.Mapped),
    };

    private static P.BatchFacts ApprovedBatch1(DateTime? releasedAt, string? releaseKind, bool releaseLine2 = true) => new(
        Batch1, 1, RequestConstants.ApprovalBatchStatuses.Approved, T0,
        new[]
        {
            new P.BatchItemFacts(Line1, T1, new[] { T1 }, releasedAt, releasedAt.HasValue ? releaseKind : null),
            new P.BatchItemFacts(Line2, T2, new[] { T2 }, releaseLine2 ? releasedAt : null, releaseLine2 && releasedAt.HasValue ? releaseKind : null),
        });

    private static P.BatchFacts Batch2Approved() => new(
        Batch2, 2, RequestConstants.ApprovalBatchStatuses.Approved, T0.AddDays(1),
        new[] { new P.BatchItemFacts(Line1, M1, new[] { M1 }, null, null) });

    private static IReadOnlyList<R.FinishedUse> Finished(params P.BatchFacts[] batches)
    {
        // Mirror of FinishedUsesFromLoaded over facts: released items of approved batches + all items of cancelled batches.
        var uses = new List<R.FinishedUse>();
        foreach (var b in batches)
        {
            foreach (var bi in b.Items)
            {
                var cancelledBatch = b.Status == RequestConstants.ApprovalBatchStatuses.Cancelled;
                if (!cancelledBatch && !bi.ReleasedAtUtc.HasValue) continue;
                var at = cancelledBatch ? b.CreatedAtUtc : bi.ReleasedAtUtc!.Value;
                foreach (var c in bi.CandidateQuotationItemIds) uses.Add(new R.FinishedUse(c, b.Id, b.BatchNumber, at));
                if (bi.SelectedQuotationItemId.HasValue) uses.Add(new R.FinishedUse(bi.SelectedQuotationItemId.Value, b.Id, b.BatchNumber, at));
            }
        }
        return uses.Distinct().ToList();
    }

    [Fact]
    public void REQ628_shape_TREVOTECH_historical_cancelled_item1_requoted_with_MAQSIS_item2_pending()
    {
        var released = T0.AddHours(5);
        var b1 = ApprovedBatch1(released, "RESTART_QUOTATION");
        var b2 = Batch2Approved();
        var lines = new[]
        {
            new P.LineFacts(Line1, 1, RequestConstants.QuotationLifecycleStatuses.QuotationApproved, M1),
            new P.LineFacts(Line2, 2, RequestConstants.QuotationLifecycleStatuses.QuotationPending, null),
        };
        var result = P.Project(Quotations(), Items(), new[] { b1, b2 }, lines, Finished(b1, b2), Array.Empty<R.AuthorizationFacts>());

        var trevo = result[Trevo];
        Assert.Equal(P.QuotationStatuses.HistoricalCancelled, trevo.LifecycleStatus);
        Assert.Equal(2, trevo.HistoricalItemCount);
        Assert.Equal(0, trevo.ActiveAwardedItemCount);
        Assert.Equal(1, trevo.HistoricalBatchNumber);
        Assert.Equal("RESTART_QUOTATION", trevo.HistoricalReleaseKind);

        var t1 = trevo.Items.Single(i => i.QuotationItemId == T1);
        Assert.Equal(P.ItemStatuses.AwardCancelled, t1.AdjudicationStatus);
        Assert.Equal(1, t1.BatchNumber);
        Assert.Equal("RESTART_QUOTATION", t1.ReleaseKind);
        Assert.Equal(P.LineStates.RequotedWithOther, t1.LineState);
        Assert.Equal("MAQSIS", t1.LineCurrentSupplierName);
        Assert.Equal(R.Reasons.ReuseAuthorizationRequired, t1.ReuseStatus);   // history ≠ current eligibility, both reported

        var t2 = trevo.Items.Single(i => i.QuotationItemId == T2);
        Assert.Equal(P.ItemStatuses.AwardCancelled, t2.AdjudicationStatus);
        Assert.Equal(P.LineStates.PendingRequote, t2.LineState);
        Assert.Null(t2.LineCurrentSupplierName);

        var maqsis = result[Maqsis];
        Assert.Equal(P.QuotationStatuses.Awarded, maqsis.LifecycleStatus);
        Assert.Equal(1, maqsis.ActiveAwardedItemCount);
        Assert.Equal(0, maqsis.HistoricalItemCount);
        var m1 = maqsis.Items.Single();
        Assert.Equal(P.ItemStatuses.Awarded, m1.AdjudicationStatus);
        Assert.Equal(2, m1.BatchNumber);
        Assert.Equal(P.LineStates.AwardedHere, m1.LineState);
        Assert.Null(m1.ReuseStatus);
    }

    [Fact]
    public void Partial_release_classifies_per_item_never_the_whole_quotation()
    {
        var released = T0.AddHours(5);
        var b1 = ApprovedBatch1(released, "CANCEL_ITEMS", releaseLine2: false);
        var lines = new[]
        {
            new P.LineFacts(Line1, 1, RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval, null),
            new P.LineFacts(Line2, 2, RequestConstants.QuotationLifecycleStatuses.QuotationApproved, T2),
        };
        var result = P.Project(Quotations(), Items().Where(i => i.QuotationId == Trevo).ToList(), new[] { b1 }, lines, Finished(b1), Array.Empty<R.AuthorizationFacts>());
        var trevo = result[Trevo];
        Assert.Equal(P.QuotationStatuses.PartiallyHistorical, trevo.LifecycleStatus);
        Assert.Equal(1, trevo.HistoricalItemCount);
        Assert.Equal(1, trevo.ActiveAwardedItemCount);
        Assert.Equal("CANCEL_ITEMS", trevo.HistoricalReleaseKind);
        Assert.Equal(P.LineStates.ClosedAfterApproval, trevo.Items.Single(i => i.QuotationItemId == T1).LineState);
        Assert.Equal(P.ItemStatuses.Awarded, trevo.Items.Single(i => i.QuotationItemId == T2).AdjudicationStatus);
    }

    [Fact]
    public void Reuse_authorization_is_reported_separately_from_the_historical_status()
    {
        var released = T0.AddHours(5);
        var b1 = ApprovedBatch1(released, "RESTART_QUOTATION");
        var lines = new[] { new P.LineFacts(Line1, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending, null), new P.LineFacts(Line2, 2, RequestConstants.QuotationLifecycleStatuses.QuotationPending, null) };
        var auth = new R.AuthorizationFacts(Guid.NewGuid(), T1, Batch1, released.AddMinutes(10), true, null, null, null);
        var result = P.Project(Quotations(), Items(), new[] { b1 }, lines, Finished(b1), new[] { auth });
        var t1 = result[Trevo].Items.Single(i => i.QuotationItemId == T1);
        Assert.Equal(P.ItemStatuses.AwardCancelled, t1.AdjudicationStatus);
        Assert.Equal(R.Reasons.ReuseAuthorized, t1.ReuseStatus);
        var t2 = result[Trevo].Items.Single(i => i.QuotationItemId == T2);
        Assert.Equal(R.Reasons.ReuseAuthorizationRequired, t2.ReuseStatus);
        Assert.Equal(P.QuotationStatuses.HistoricalCancelled, result[Trevo].LifecycleStatus);
    }

    [Fact]
    public void Cancelled_batch_marks_candidates_and_winner_as_history()
    {
        var cancelled = new P.BatchFacts(Batch1, 1, RequestConstants.ApprovalBatchStatuses.Cancelled, T0,
            new[] { new P.BatchItemFacts(Line1, T1, new[] { T1, M1 }, null, null) });
        var lines = new[] { new P.LineFacts(Line1, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending, null), new P.LineFacts(Line2, 2, null, null) };
        var result = P.Project(Quotations(), Items(), new[] { cancelled }, lines, Finished(cancelled), Array.Empty<R.AuthorizationFacts>());
        Assert.Equal(P.ItemStatuses.AwardCancelled, result[Trevo].Items.Single(i => i.QuotationItemId == T1).AdjudicationStatus);
        Assert.Null(result[Trevo].Items.Single(i => i.QuotationItemId == T1).ReleaseKind);
        Assert.Equal(P.ItemStatuses.CandidateCancelled, result[Maqsis].Items.Single().AdjudicationStatus);
        Assert.Equal(P.ItemStatuses.NotSubmitted, result[Trevo].Items.Single(i => i.QuotationItemId == T2).AdjudicationStatus);
        Assert.Equal(P.QuotationStatuses.PartiallyHistorical, result[Trevo].LifecycleStatus);
        Assert.Equal(P.QuotationStatuses.HistoricalCancelled, result[Maqsis].LifecycleStatus);
    }

    [Fact]
    public void In_approval_not_awarded_and_rejected()
    {
        var waiting = new P.BatchFacts(Batch1, 1, RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, T0,
            new[] { new P.BatchItemFacts(Line1, T1, new[] { T1, M1 }, null, null) });
        var lines = new[] { new P.LineFacts(Line1, 1, RequestConstants.QuotationLifecycleStatuses.BatchAssigned, null), new P.LineFacts(Line2, 2, null, null) };
        var r1 = P.Project(Quotations(), Items(), new[] { waiting }, lines, Finished(waiting), Array.Empty<R.AuthorizationFacts>());
        Assert.Equal(P.QuotationStatuses.InApproval, r1[Trevo].LifecycleStatus);
        Assert.Equal(P.ItemStatuses.InApproval, r1[Maqsis].Items.Single().AdjudicationStatus);

        var approved = waiting with { Status = RequestConstants.ApprovalBatchStatuses.Approved };
        var r2 = P.Project(Quotations(), Items(), new[] { approved }, lines, Finished(approved), Array.Empty<R.AuthorizationFacts>());
        Assert.Equal(P.ItemStatuses.CandidateNotAwarded, r2[Maqsis].Items.Single().AdjudicationStatus);
        Assert.Equal(P.QuotationStatuses.NotAwarded, r2[Maqsis].LifecycleStatus);
        Assert.Equal(P.QuotationStatuses.Awarded, r2[Trevo].LifecycleStatus);

        var rejected = waiting with { Status = RequestConstants.ApprovalBatchStatuses.Rejected };
        var r3 = P.Project(Quotations(), Items(), new[] { rejected }, lines, Finished(rejected), Array.Empty<R.AuthorizationFacts>());
        Assert.Equal(P.ItemStatuses.Rejected, r3[Trevo].Items.Single(i => i.QuotationItemId == T1).AdjudicationStatus);
        Assert.Equal(P.QuotationStatuses.Rejected, r3[Maqsis].LifecycleStatus);
    }

    [Fact]
    public void Not_quoted_placeholders_and_never_submitted_quotations_stay_open()
    {
        var items = new[]
        {
            new P.QuotationItemFacts(T1, Trevo, Line1, RequestConstants.ReconciliationStatuses.Mapped),
            new P.QuotationItemFacts(T2, Trevo, Line2, RequestConstants.ReconciliationStatuses.NotQuoted),
        };
        var lines = new[] { new P.LineFacts(Line1, 1, null, null), new P.LineFacts(Line2, 2, null, null) };
        var result = P.Project(Quotations().Take(1).ToList(), items, Array.Empty<P.BatchFacts>(), lines, Array.Empty<R.FinishedUse>(), Array.Empty<R.AuthorizationFacts>());
        Assert.Equal(P.QuotationStatuses.Open, result[Trevo].LifecycleStatus);
        Assert.Equal(P.ItemStatuses.NotSubmitted, result[Trevo].Items.Single(i => i.QuotationItemId == T1).AdjudicationStatus);
        Assert.Equal(P.ItemStatuses.NotQuoted, result[Trevo].Items.Single(i => i.QuotationItemId == T2).AdjudicationStatus);
        Assert.Equal(0, result[Trevo].HistoricalItemCount);
    }
}
