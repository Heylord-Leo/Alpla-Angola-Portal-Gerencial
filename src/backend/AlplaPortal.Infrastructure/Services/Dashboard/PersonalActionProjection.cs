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

namespace AlplaPortal.Infrastructure.Services.Dashboard;

/// <summary>
/// Dashboard V2 B5 — canonical "Minha Operação" (PESSOAL) projection. Composes the SAME canonical
/// domain sources the operational screens use; it never re-implements actionability:
///   • Buyer      → <see cref="BuyerQueueProjectionBuilder"/> over the user's assigned requests
///                  (BuyerId == me), keeping only requests with an open canonical Buyer action.
///                  Buyer-resolved adjustments already surface here as RESOLVE_ADJUSTMENT, so there
///                  is NO separate "adjustment" domain (that would double-count — see the audit).
///   • Approval   → the canonical Area-approval ownership predicate (identical to
///                  RequestsController.GetPendingApprovals area query): AreaApproverId == me OR an
///                  active DepartmentManager scope. Final Approval is role-shared (PD-01) and is
///                  never included.
///   • Requester  → the user's own DRAFT requests (RequesterId == me), an unambiguous personal action.
///
/// Explicitly excluded from personal ownership: Final Approval (shared, PD-01), Finance obligations,
/// Receiving obligations, the unassigned Buyer pool (BuyerId == null), role-only visibility, and
/// SystemAdministrator global bypass — for /personal, current-user ownership always wins, so the
/// ownership filters are applied even for an admin (admin/global visibility != personal ownership).
///
/// No monetary amounts; no urgency/aging (B5.1 defers date buckets — every personal domain would need
/// a defensible domain-specific due date, which does not yet exist for Approval/Requester, §8).
/// </summary>
public sealed class PersonalActionProjection
{
    private readonly ApplicationDbContext _context;

    public PersonalActionProjection(ApplicationDbContext context) => _context = context;

    // Mirrors the Buyer queue / Dashboard Buyer section hydration bound — status list only, never logic.
    private static readonly string[] BuyerActiveRequestStatusCodes =
    {
        RequestConstants.Statuses.Draft, RequestConstants.Statuses.WaitingQuotation,
        RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.AreaAdjustment,
        RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.FinalAdjustment,
    };

    // Canonical Area-approval statuses (mirror GetPendingApprovals).
    private static readonly string[] AreaApprovalStatusCodes =
    {
        RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.WaitingCostCenter,
    };

    /// <param name="scoped">RequestAccessScope-filtered request query (as for every other V2 section).</param>
    /// <param name="userId">The signed-in user — the ONLY owner whose actions may appear.</param>
    /// <param name="today">UTC date, for the canonical Buyer projection's deadline math.</param>
    /// <param name="maxActions">Upper bound on returned action rows; the summary always counts the full set.</param>
    public async Task<DashboardV2PersonalSectionDto> BuildAsync(
        IQueryable<Request> scoped, Guid userId, DateTime today, int maxActions = 200)
    {
        var actions = new List<PersonalActionDto>();
        actions.AddRange(await BuildBuyerActionsAsync(scoped, userId, today));
        actions.AddRange(await BuildAreaApprovalActionsAsync(scoped, userId));
        actions.AddRange(await BuildRequesterActionsAsync(scoped, userId));

        // Identity = Domain + EntityType + EntityId + ActionType. A request may legitimately hold
        // several distinct actions; duplicate representations of the SAME obligation collapse here.
        var deduped = actions
            .GroupBy(a => (a.Domain, a.EntityType, a.EntityId, a.ActionType))
            .Select(g => g.First())
            .ToList();

        var summary = new PersonalActionSummaryDto
        {
            ActionableActions = deduped.Count,
            ActionableRequests = deduped.Select(a => a.RequestId).Distinct().Count(),
            ByDomain = deduped
                .GroupBy(a => a.Domain)
                .OrderBy(g => g.Key)
                .Select(g => new PersonalActionDomainCountDto
                {
                    Domain = g.Key,
                    Actions = g.Count(),
                    Requests = g.Select(a => a.RequestId).Distinct().Count(),
                })
                .ToList(),
        };

        return new DashboardV2PersonalSectionDto
        {
            Summary = summary,
            Actions = deduped.Take(maxActions).ToList(),
        };
    }

    // ── BUYER: assigned (BuyerId == me) requests with at least one open canonical Buyer action. ──
    private async Task<List<PersonalActionDto>> BuildBuyerActionsAsync(
        IQueryable<Request> scoped, Guid userId, DateTime today)
    {
        var requests = await scoped
            .Where(r => r.RequestType!.Code == RequestConstants.Types.Quotation
                        && r.BuyerId == userId
                        && BuyerActiveRequestStatusCodes.Contains(r.Status!.Code))
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

        var result = new List<PersonalActionDto>();
        foreach (var r in requests)
        {
            // Reuse the SAME entity→input adapter + canonical projection as the Buyer queue/workspace.
            var projection = Proj.Build(BuyerQueueProjectionInputFactory.FromRequest(r), userId, today);
            if (BuyerQueueConstants.OperationalStates.HiddenByDefault.Contains(projection.OperationalState))
                continue;

            foreach (var action in projection.NextBuyerActions.Where(a => a.Actionable))
            {
                result.Add(new PersonalActionDto
                {
                    Domain = PersonalActionDomains.Buyer,
                    EntityType = PersonalActionEntityTypes.Request,
                    EntityId = r.Id.ToString(),
                    RequestId = r.Id,
                    RequestNumber = r.RequestNumber ?? string.Empty,
                    ActionType = action.Code,
                    Title = r.Title,
                    TargetPath = "/buyer/items?ownership=me",
                    DueDate = null, // urgency deferred (B5.1)
                });
            }
        }
        return result;
    }

    // ── APPROVAL (Area only): ownership predicate identical to the canonical pending-approvals area
    //    query. Final Approval is role-shared (PD-01) and is NOT queried. Request-grain in B5.1
    //    (batch-grain refinement is deferred to the dedicated Approvals slice). ──
    private async Task<List<PersonalActionDto>> BuildAreaApprovalActionsAsync(
        IQueryable<Request> scoped, Guid userId)
    {
        var rows = await scoped
            .Where(r =>
                (AreaApprovalStatusCodes.Contains(r.Status!.Code)
                 || r.ApprovalBatches.Any(b => b.Status == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval))
                && (r.AreaApproverId == userId
                    || _context.DepartmentManagers.Any(dm =>
                        dm.UserId == userId && dm.IsActive
                        && dm.DepartmentId == r.DepartmentId
                        && (dm.PlantId == null || (r.PlantId != null && dm.PlantId == r.PlantId)))))
            .Select(r => new { r.Id, r.RequestNumber, r.Title })
            .ToListAsync();

        return rows.Select(r => new PersonalActionDto
        {
            Domain = PersonalActionDomains.Approval,
            EntityType = PersonalActionEntityTypes.Request,
            EntityId = r.Id.ToString(),
            RequestId = r.Id,
            RequestNumber = r.RequestNumber ?? string.Empty,
            ActionType = PersonalActionTypes.AreaApproval,
            Title = r.Title,
            TargetPath = "/approvals",
            DueDate = null,
        }).ToList();
    }

    // ── REQUESTER: the user's own DRAFT requests (must submit or discard) — unambiguous ownership. ──
    private async Task<List<PersonalActionDto>> BuildRequesterActionsAsync(
        IQueryable<Request> scoped, Guid userId)
    {
        var rows = await scoped
            .Where(r => r.RequesterId == userId && r.Status!.Code == RequestConstants.Statuses.Draft)
            .Select(r => new { r.Id, r.RequestNumber, r.Title })
            .ToListAsync();

        return rows.Select(r => new PersonalActionDto
        {
            Domain = PersonalActionDomains.Requester,
            EntityType = PersonalActionEntityTypes.Request,
            EntityId = r.Id.ToString(),
            RequestId = r.Id,
            RequestNumber = r.RequestNumber ?? string.Empty,
            ActionType = PersonalActionTypes.SubmitDraft,
            Title = r.Title,
            TargetPath = $"/requests/{r.Id}",
            DueDate = null,
        }).ToList();
    }
}
