using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Dashboard V2 — Buyer workload aggregation (Phase B, slice B1+B2). PURE and node-of-truth-free:
/// it consumes the OUTPUT of <see cref="BuyerQueueProjectionBuilder"/> (the single canonical Buyer
/// projection) and only tallies it per buyer. It MUST NOT re-implement coverage/state/action logic —
/// that lives exclusively in BuyerQueueProjectionBuilder, so the Dashboard, the Buyer queue and the
/// Buyer Workspace can never disagree about how a request classifies. See
/// docs/DASHBOARD_V2_PHASE_B_SPECIFICATION.md (§6, §14).
///
/// Product rules already DECIDED (spec §27):
/// - BuyerId == null is a SHARED bucket (IsUnassigned), never merged into a buyer's personal row.
/// - Buyer urgency counts only while a real Buyer action is open (ADD_QUOTATION / SUBMIT_BATCH /
///   RESOLVE_ADJUSTMENT) — Overdue = NeedByDate &lt; today (DeadlineCondition Overdue), Critical =
///   NeedByDate == today (DeadlineCondition DueToday). No stage-age / performance metrics here.
/// </summary>
public static class BuyerWorkloadAggregator
{
    /// <summary>One projected buyer-active request fed into the aggregation.</summary>
    public sealed record BuyerWorkloadItem(
        Guid? BuyerId,
        string? BuyerName,
        BuyerQueueProjectionBuilder.BuyerQueueProjection Projection);

    /// <summary>Per-bucket tallies. A bucket is either one buyer (BuyerId set) or the shared
    /// unassigned pool (IsUnassigned = true, BuyerId null).</summary>
    public sealed record BuyerWorkloadMetrics(
        Guid? BuyerId,
        string? BuyerName,
        bool IsUnassigned,
        int AssignedRequests,
        int ActionableRequests,
        int PendingQuotationItems,
        int ReadyForBatchItems,
        int NeedsQuotationRequests,
        int PartialCoverageRequests,
        int ReadyForApprovalRequests,
        int AdjustmentRequests,
        int OverdueActionableRequests,
        int CriticalActionableRequests);

    private static bool IsActionable(BuyerQueueProjectionBuilder.BuyerQueueProjection p)
        => p.NextBuyerActions.Any(a => a.Actionable);

    private static int Coverage(BuyerQueueProjectionBuilder.BuyerQueueProjection p, string bucket)
        => p.CoverageCounts.TryGetValue(bucket, out var n) ? n : 0;

    /// <summary>
    /// Aggregate projected items into one metrics row per buyer plus one shared unassigned row.
    /// Every field is derived purely from the canonical projection outputs.
    /// </summary>
    public static IReadOnlyList<BuyerWorkloadMetrics> Aggregate(IEnumerable<BuyerWorkloadItem> items)
    {
        var result = new List<BuyerWorkloadMetrics>();

        // Group by buyer bucket; the null key is the shared unassigned pool.
        foreach (var group in items.GroupBy(i => i.BuyerId))
        {
            var isUnassigned = group.Key == null;
            string? buyerName = isUnassigned ? null : group.Select(i => i.BuyerName).FirstOrDefault(n => n != null);

            int assigned = 0, actionable = 0, pendingItems = 0, readyItems = 0,
                needs = 0, partial = 0, ready = 0, adjustment = 0, overdue = 0, critical = 0;

            foreach (var item in group)
            {
                var p = item.Projection;
                var act = IsActionable(p);

                assigned++;
                if (act) actionable++;

                pendingItems += Coverage(p, BuyerQueueConstants.CoverageBuckets.PendingQuotation);
                readyItems += Coverage(p, BuyerQueueConstants.CoverageBuckets.QuotedReadyForBatch);

                switch (p.OperationalState)
                {
                    case BuyerQueueConstants.OperationalStates.NeedsQuotation: needs++; break;
                    case BuyerQueueConstants.OperationalStates.PartialCoverage: partial++; break;
                    case BuyerQueueConstants.OperationalStates.ReadyForApproval: ready++; break;
                    case BuyerQueueConstants.OperationalStates.AdjustmentRequired: adjustment++; break;
                }

                // Urgency only while a buyer action is actually open (spec PD-03).
                if (act && p.DeadlineCondition == BuyerQueueConstants.DeadlineConditions.Overdue) overdue++;
                if (act && p.DeadlineCondition == BuyerQueueConstants.DeadlineConditions.DueToday) critical++;
            }

            result.Add(new BuyerWorkloadMetrics(
                isUnassigned ? null : group.Key, buyerName, isUnassigned,
                assigned, actionable, pendingItems, readyItems,
                needs, partial, ready, adjustment, overdue, critical));
        }

        return result;
    }
}
