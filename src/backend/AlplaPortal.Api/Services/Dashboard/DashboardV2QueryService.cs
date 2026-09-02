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
using Proj = AlplaPortal.Domain.Services.BuyerQueueProjectionBuilder;

namespace AlplaPortal.Api.Services.Dashboard;

/// <summary>
/// Dashboard V2 composition service (Phase B foundation, slice B1+B2). It loads the SAME canonical
/// Buyer population as the Buyer queue and projects each request via <see cref="Proj"/> reusing
/// the shared <see cref="BuyerQueueProjectionInputFactory"/> — so Dashboard counts reconcile exactly with the
/// Buyer queue / Buyer Workspace (spec §14). The per-buyer tally is the pure
/// <see cref="BuyerWorkloadAggregator"/>. NO coverage/state logic is re-implemented here.
///
/// Designed for composition: later slices add Finance/Receiving/Pipeline builders alongside
/// <see cref="BuildBuyerSectionAsync"/> without changing this method or the Buyer contract.
/// </summary>
public sealed class DashboardV2QueryService
{
    private readonly ApplicationDbContext _context;

    public DashboardV2QueryService(ApplicationDbContext context) => _context = context;

    // Mirrors BuyerQueueController's default bounding so the Dashboard sees the identical
    // "buyer-active" population (reconciliation). Status list only — never workflow logic.
    private static readonly string[] BuyerActiveRequestStatusCodes =
    {
        RequestConstants.Statuses.Draft, RequestConstants.Statuses.WaitingQuotation,
        RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.AreaAdjustment,
        RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.FinalAdjustment,
    };

    /// <summary>
    /// Build the Buyer section. <paramref name="scoped"/> must already be RequestAccessScope-filtered.
    /// Optional structural filters (company/plant/department/needLevel) are applied in SQL exactly as the
    /// Buyer queue does, so the Buyer-list distribution block can pass its active filters and reconcile;
    /// the Dashboard passes none (full effective scope).
    /// </summary>
    public async Task<DashboardV2BuyerSectionDto> BuildBuyerSectionAsync(
        IQueryable<Request> scoped, Guid currentUserId, bool isBuyer, bool canSeeWorkload, DateTime today,
        int? company = null, int? plant = null, int? department = null, string? needLevel = null)
    {
        var q = scoped.Where(r => r.RequestType.Code == RequestConstants.Types.Quotation);

        if (company.HasValue) q = q.Where(r => r.CompanyId == company.Value);
        if (plant.HasValue) q = q.Where(r => r.PlantId == plant.Value);
        if (department.HasValue) q = q.Where(r => r.DepartmentId == department.Value);
        if (!string.IsNullOrWhiteSpace(needLevel))
            q = q.Where(r => r.NeedLevel != null && r.NeedLevel.Code == needLevel);

        // Bound the working set to buyer-active statuses (residual hidden states dropped post-projection),
        // identical to the queue's default hydration — keeps the load small at any data volume.
        q = q.Where(r => BuyerActiveRequestStatusCodes.Contains(r.Status.Code));

        var requests = await q
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.NeedLevel)
            .Include(r => r.Buyer)
            .Include(r => r.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(r => r.ApprovalBatches).ThenInclude(b => b.Items).ThenInclude(bi => bi.Candidates)
            .Include(r => r.PoGroups)
            .Include(r => r.Quotations).ThenInclude(qq => qq.Items)
            .Include(r => r.Attachments)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        var items = new List<BuyerWorkloadAggregator.BuyerWorkloadItem>(requests.Count);
        foreach (var r in requests)
        {
            // Reuse the SAME entity→input adapter + canonical projection as the Buyer queue/workspace.
            var projection = Proj.Build(BuyerQueueProjectionInputFactory.FromRequest(r), currentUserId, today);
            if (BuyerQueueConstants.OperationalStates.HiddenByDefault.Contains(projection.OperationalState))
                continue;
            items.Add(new BuyerWorkloadAggregator.BuyerWorkloadItem(r.BuyerId, r.Buyer?.FullName, projection));
        }

        var metrics = BuyerWorkloadAggregator.Aggregate(items);
        var unassigned = metrics.FirstOrDefault(m => m.IsUnassigned);

        var dto = new DashboardV2BuyerSectionDto();

        if (isBuyer)
        {
            var mine = metrics.FirstOrDefault(m => !m.IsUnassigned && m.BuyerId == currentUserId);
            dto.Personal = new BuyerPersonalSummaryDto
            {
                AssignedRequests = mine?.AssignedRequests ?? 0,
                ActionableRequests = mine?.ActionableRequests ?? 0,
                PendingQuotationItems = mine?.PendingQuotationItems ?? 0,
                ReadyForBatchItems = mine?.ReadyForBatchItems ?? 0,
                AdjustmentRequests = mine?.AdjustmentRequests ?? 0,
                OverdueActionableRequests = mine?.OverdueActionableRequests ?? 0,
                CriticalActionableRequests = mine?.CriticalActionableRequests ?? 0,
            };

            dto.Shared = new BuyerSharedQueueSummaryDto
            {
                UnassignedRequests = unassigned?.AssignedRequests ?? 0,
                UnassignedActionableRequests = unassigned?.ActionableRequests ?? 0,
                UnassignedPendingItems = unassigned?.PendingQuotationItems ?? 0,
                UnassignedReadyItems = unassigned?.ReadyForBatchItems ?? 0,
                UnassignedNeedsQuotationRequests = unassigned?.NeedsQuotationRequests ?? 0,
                UnassignedPartialCoverageRequests = unassigned?.PartialCoverageRequests ?? 0,
                UnassignedReadyForApprovalRequests = unassigned?.ReadyForApprovalRequests ?? 0,
                UnassignedAdjustmentRequests = unassigned?.AdjustmentRequests ?? 0,
                UnassignedOverdueActionableRequests = unassigned?.OverdueActionableRequests ?? 0,
                UnassignedCriticalActionableRequests = unassigned?.CriticalActionableRequests ?? 0,
            };
        }

        if (canSeeWorkload)
        {
            dto.Workload = new BuyerWorkloadSummaryDto
            {
                Rows = metrics
                    .Where(m => !m.IsUnassigned)
                    .OrderByDescending(m => m.ActionableRequests)
                    .ThenByDescending(m => m.AssignedRequests)
                    .ThenBy(m => m.BuyerName)
                    .Select(ToRow)
                    .ToList(),
                Unassigned = unassigned == null ? null : ToRow(unassigned),
            };
        }

        return dto;
    }

    private static BuyerWorkloadRowDto ToRow(BuyerWorkloadAggregator.BuyerWorkloadMetrics m) => new()
    {
        BuyerId = m.BuyerId,
        BuyerName = m.BuyerName,
        IsUnassigned = m.IsUnassigned,
        AssignedRequests = m.AssignedRequests,
        ActionableRequests = m.ActionableRequests,
        PendingQuotationItems = m.PendingQuotationItems,
        ReadyForBatchItems = m.ReadyForBatchItems,
        NeedsQuotationRequests = m.NeedsQuotationRequests,
        PartialCoverageRequests = m.PartialCoverageRequests,
        ReadyForApprovalRequests = m.ReadyForApprovalRequests,
        AdjustmentRequests = m.AdjustmentRequests,
        OverdueActionableRequests = m.OverdueActionableRequests,
        CriticalActionableRequests = m.CriticalActionableRequests,
    };
}
