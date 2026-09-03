using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Receiving;

/// <summary>
/// The single reusable group-level Receiving queue projection. It builds one row per Receiving-actionable
/// <c>RequestPoGroup</c> (actionability judged by the canonical <see cref="ReceivingActionEvaluator"/>)
/// over a scoped Request population, and computes the count summary. Consumed by BOTH the group-level
/// Receiving queue endpoint AND DashboardV2 so they reconcile exactly:
///   ReceivingQueueController + DashboardV2QueryService  →  this projection  →  ReceivingActionEvaluator.
/// No status/action predicate is re-implemented here; the SQL prefilter uses the evaluator's canonical
/// ActionableStatuses set, and final actionability/bucketing come from the evaluator. No aging, no money.
/// </summary>
public sealed class ReceivingQueueProjection
{
    public sealed record BuildResult(List<ReceivingQueueRowDto> Rows, ReceivingSharedQueueSummaryDto Summary);

    /// <summary>
    /// Build the actionable Receiving rows + summary for the finance/receiving-relevant population within
    /// the already-scoped <paramref name="scoped"/> query. Optional <paramref name="bucket"/> filters to a
    /// single actionable bucket (from <see cref="ReceivingActionEvaluator.Buckets"/>); when
    /// <paramref name="actionableOnly"/> is false the population is unchanged (all rows are actionable by
    /// construction here, so it currently behaves the same — kept for a stable filter contract).
    /// </summary>
    public async Task<BuildResult> BuildAsync(
        IQueryable<Request> scoped, bool actionableOnly = true, string? bucket = null)
    {
        var actionableStatuses = ReceivingActionEvaluator.ActionableStatuses; // canonical, single source

        // SQL prefilter: only requests that have >=1 non-cancelled group in a Receiving-actionable status.
        var loaded = await scoped
            .Where(r => r.PoGroups.Any(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled
                                            && actionableStatuses.Contains(g.Status)))
            .Include(r => r.RequestType)
            .Include(r => r.PoGroups).ThenInclude(g => g.Supplier)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        var rows = new List<ReceivingQueueRowDto>();
        foreach (var r in loaded)
        {
            foreach (var g in r.PoGroups.Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled))
            {
                // Final actionability from the canonical evaluator (never a scalar/status shortcut here).
                if (!ReceivingActionEvaluator.IsReceivingActionable(g.Status)) continue;
                var b = ReceivingActionEvaluator.ActionableBucket(g.Status);
                if (b == null) continue;
                if (bucket != null && !string.Equals(b, bucket, StringComparison.OrdinalIgnoreCase)) continue;

                rows.Add(new ReceivingQueueRowDto
                {
                    RequestId = r.Id,
                    RequestNumber = r.RequestNumber ?? string.Empty,
                    RequestTypeCode = r.RequestType?.Code ?? string.Empty,
                    Title = r.Title,
                    RequestPoGroupId = g.Id,
                    GroupStatus = g.Status,
                    SupplierName = g.Supplier?.Name ?? g.SupplierNameSnapshot,
                    PurchaseOrderNumber = g.PurchaseOrderNumber,
                    ActionableBucket = b,
                    AvailableActions = ReceivingActionEvaluator.Evaluate(g.Status).ToList(),
                });
            }
        }

        return new BuildResult(rows, Summarize(rows));
    }

    /// <summary>Count summary over an actionable-row set (each row is exactly one bucket → no double-count).</summary>
    public static ReceivingSharedQueueSummaryDto Summarize(IReadOnlyList<ReceivingQueueRowDto> rows) => new()
    {
        ActionableGroups = rows.Count,
        ActionableRequests = rows.Select(x => x.RequestId).Distinct().Count(),
        ReadyForReceiptGroups = rows.Count(x => x.ActionableBucket == ReceivingActionEvaluator.Buckets.ReadyForReceipt),
        WaitingReceiptGroups = rows.Count(x => x.ActionableBucket == ReceivingActionEvaluator.Buckets.WaitingReceipt),
        FollowUpGroups = rows.Count(x => x.ActionableBucket == ReceivingActionEvaluator.Buckets.FollowUp),
        WaitingSupplierDeliveryGroups = rows.Count(x => x.ActionableBucket == ReceivingActionEvaluator.Buckets.WaitingSupplierDelivery),
    };
}
