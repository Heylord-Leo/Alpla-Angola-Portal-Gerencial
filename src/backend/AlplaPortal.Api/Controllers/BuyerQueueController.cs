using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Proj = AlplaPortal.Domain.Services.BuyerQueueProjectionBuilder;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Canonical Buyer queue (Phase 1). Additive, read-only endpoint that projects the Buyer's
/// quotation workload as ONE row per Request — fixing the line-item pagination defect of
/// GET /api/v1/line-items (which flattens Request×LineItem and can split one Request across pages).
/// Pagination and totalCount are strictly Request-level. Operational state / coverage / priority /
/// deadline / attention / capabilities are all server-derived via BuyerQueueProjectionBuilder; the
/// frontend consumes them and does not re-derive the workflow. The existing /line-items endpoint and
/// the Quotation Wizard are untouched. See docs/BUYER_QUEUE_CANONICAL_MODEL.md.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/buyer/queue")]
public class BuyerQueueController : BaseController
{
    public BuyerQueueController(ApplicationDbContext context) : base(context) { }

    [HttpGet]
    public async Task<ActionResult<BuyerQueuePageDto>> GetQueue(
        [FromQuery] string? query = null,
        [FromQuery] int? company = null,
        [FromQuery] int? plant = null,
        [FromQuery] int? department = null,
        [FromQuery] string ownership = "all",
        [FromQuery] Guid? buyer = null,
        [FromQuery] string? operationalState = null,
        [FromQuery] string? priority = null,
        [FromQuery] string? deadline = null,
        [FromQuery] bool includeCompleted = false,
        [FromQuery] string sort = "priority",
        [FromQuery] string? needLevel = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        var projected = await LoadAndProjectAsync(includeCompleted, ownership, query, company, plant, department, needLevel, buyer);

        // Projection-derived filters (not SQL-translatable).
        IEnumerable<Row> rows = projected;
        if (!string.IsNullOrWhiteSpace(operationalState))
            rows = rows.Where(x => x.P.OperationalState == operationalState);
        if (!string.IsNullOrWhiteSpace(priority))
            rows = rows.Where(x => x.P.PriorityBand == priority);
        if (!string.IsNullOrWhiteSpace(deadline))
            rows = rows.Where(x => x.P.DeadlineCondition == deadline);

        var ordered = Sort(rows, sort).ToList();

        var totalCount = ordered.Count; // Requests, never line-items.
        var pageRows = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // Note metadata is loaded ONLY for the returned page slice (never for the whole set).
        var notes = await LoadNoteMetadataAsync(pageRows.Select(r => r.R.Id).ToList());

        return Ok(new BuyerQueuePageDto
        {
            Items = pageRows.Select(r => MapItem(r, notes)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    private sealed record NoteMeta(int Count, string? LatestText, DateTime LatestAtUtc, string? LatestActorName);

    private async Task<Dictionary<Guid, NoteMeta>> LoadNoteMetadataAsync(List<Guid> requestIds)
    {
        if (requestIds.Count == 0) return new();
        var rows = await _context.RequestStatusHistories
            .AsNoTracking()
            .Where(h => requestIds.Contains(h.RequestId) && h.ActionTaken == RequestConstants.StatusHistoryActions.Note)
            .Select(h => new { h.RequestId, h.Comment, h.CreatedAtUtc, ActorName = h.ActorUser.FullName })
            .ToListAsync();

        return rows
            .GroupBy(h => h.RequestId)
            .ToDictionary(g => g.Key, g =>
            {
                var latest = g.OrderByDescending(h => h.CreatedAtUtc).First();
                return new NoteMeta(g.Count(), latest.Comment, latest.CreatedAtUtc, latest.ActorName);
            });
    }

    [HttpGet("summary")]
    public async Task<ActionResult<BuyerQueueSummaryDto>> GetSummary(
        [FromQuery] string? query = null,
        [FromQuery] int? company = null,
        [FromQuery] int? plant = null,
        [FromQuery] int? department = null,
        [FromQuery] string ownership = "all",
        [FromQuery] Guid? buyer = null,
        [FromQuery] bool includeCompleted = false,
        [FromQuery] string? needLevel = null)
    {
        var projected = await LoadAndProjectAsync(includeCompleted, ownership, query, company, plant, department, needLevel, buyer);

        var byState = projected
            .GroupBy(x => x.P.OperationalState)
            .ToDictionary(g => g.Key, g => g.Count());

        int Count(params string[] states) => projected.Count(x => states.Contains(x.P.OperationalState));

        return Ok(new BuyerQueueSummaryDto
        {
            Total = projected.Count,
            RequiresAttention = projected.Count(x => x.P.RequiresAttention),
            NeedsAction = Count(
                BuyerQueueConstants.OperationalStates.NeedsQuotation,
                BuyerQueueConstants.OperationalStates.PartialCoverage,
                BuyerQueueConstants.OperationalStates.ReadyForApproval,
                BuyerQueueConstants.OperationalStates.AdjustmentRequired),
            AwaitingApproval = Count(BuyerQueueConstants.OperationalStates.AwaitingApproval),
            Unassigned = projected.Count(x => x.P.OwnershipState == BuyerQueueConstants.OwnershipStates.Unassigned),
            ByOperationalState = byState
        });
    }

    // ── internals ──

    private sealed record Row(Request R, Proj.BuyerQueueProjection P);

    private async Task<List<Row>> LoadAndProjectAsync(
        bool includeCompleted, string ownership, string? query, int? company, int? plant, int? department,
        string? needLevel = null, Guid? buyer = null)
    {
        var currentUserId = CurrentUserId;
        var scoped = await GetScopedRequestsQuery();

        var q = scoped.Where(r => r.RequestType.Code == RequestConstants.Types.Quotation);

        // Ownership (auth-scope-first: scope already applied; this narrows within it).
        switch ((ownership ?? "all").ToLowerInvariant())
        {
            case "me": q = q.Where(r => r.BuyerId == currentUserId); break;
            case "unassigned": q = q.Where(r => r.BuyerId == null); break;
        }

        // Explicit buyer filter (used by the Dashboard V2 workload drill-down). Applied within the
        // authorization scope; narrows to one buyer's assigned requests.
        if (buyer.HasValue) q = q.Where(r => r.BuyerId == buyer.Value);

        // Org filters — applied in SQL BEFORE pagination, so both the list and the summary share the
        // exact same company/plant/department scope.
        if (company.HasValue) q = q.Where(r => r.CompanyId == company.Value);
        if (plant.HasValue) q = q.Where(r => r.PlantId == plant.Value);
        if (department.HasValue) q = q.Where(r => r.DepartmentId == department.Value);
        // Need-level filter (an actual filter, not a sort) — applied in SQL like the org filters so the
        // list and the summary cards share the exact same need-level scope.
        if (!string.IsNullOrWhiteSpace(needLevel))
            q = q.Where(r => r.NeedLevel != null && r.NeedLevel.Code == needLevel);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(r => (r.RequestNumber != null && EF.Functions.Like(r.RequestNumber, $"%{term}%"))
                             || EF.Functions.Like(r.Title, $"%{term}%"));
        }

        // Bound the working set: when completed is hidden, keep only Buyer-active request statuses in
        // SQL (residual completed dropped after projection). Keeps hydration small at any data volume.
        if (!includeCompleted)
            q = q.Where(r => BuyerActiveRequestStatusCodes.Contains(r.Status.Code));

        var requests = await q
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.NeedLevel)
            .Include(r => r.Company)
            .Include(r => r.Plant)
            .Include(r => r.Department)
            .Include(r => r.Buyer)
            .Include(r => r.Requester)
            .Include(r => r.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(r => r.ApprovalBatches).ThenInclude(b => b.Items).ThenInclude(bi => bi.Candidates)
            .Include(r => r.PoGroups)
            .Include(r => r.Quotations).ThenInclude(qq => qq.Items)
            .Include(r => r.Attachments)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        var today = DateTime.UtcNow;
        var rows = new List<Row>(requests.Count);
        foreach (var r in requests)
        {
            var projection = Project(r, currentUserId, today);
            if (!includeCompleted && BuyerQueueConstants.OperationalStates.HiddenByDefault.Contains(projection.OperationalState))
                continue;
            rows.Add(new Row(r, projection));
        }
        return rows;
    }

    private static readonly string[] BuyerActiveRequestStatusCodes =
    {
        RequestConstants.Statuses.Draft, RequestConstants.Statuses.WaitingQuotation,
        RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.AreaAdjustment,
        RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.FinalAdjustment,
    };

    private static Proj.BuyerQueueProjection Project(Request r, Guid currentUserId, DateTime today)
        // Entity→input mapping lives in the shared Domain factory (BuyerQueueProjectionInputFactory) so
        // the queue, the Buyer Workspace and the Dashboard all feed the projection the identical input.
        => Proj.Build(BuyerQueueProjectionInputFactory.FromRequest(r), currentUserId, today);

    private IEnumerable<Row> Sort(IEnumerable<Row> rows, string sort) => (sort ?? "priority").ToLowerInvariant() switch
    {
        "deadline" => rows.OrderBy(x => x.P.DeadlineRank).ThenBy(x => x.R.NeedByDateUtc ?? DateTime.MaxValue).ThenBy(x => x.R.CreatedAtUtc),
        "created" => rows.OrderByDescending(x => x.R.CreatedAtUtc),
        "number" => rows.OrderBy(x => x.R.RequestNumber),
        // "priority" (canonical): attention first, then exception/overdue band, need-level, deadline, needBy, age.
        _ => rows
            .OrderByDescending(x => x.P.RequiresAttention)
            .ThenBy(x => x.P.PriorityBand == BuyerQueueConstants.PriorityBands.ExceptionOrOverdue ? 0 : 1)
            .ThenBy(x => x.P.NeedLevelRank)
            .ThenBy(x => x.P.DeadlineRank)
            .ThenBy(x => x.R.NeedByDateUtc ?? DateTime.MaxValue)
            .ThenBy(x => x.R.CreatedAtUtc),
    };

    private BuyerQueueItemDto MapItem(Row row, Dictionary<Guid, NoteMeta> notes)
    {
        var r = row.R;
        var p = row.P;
        notes.TryGetValue(r.Id, out var note);
        var isSystemAdmin = CurrentUserRoles.Contains(RoleConstants.SystemAdministrator);
        var isLocalManager = CurrentUserRoles.Contains(RoleConstants.LocalManager);
        var isBuyer = CurrentUserRoles.Contains(RoleConstants.Buyer);

        var canClaim = p.OwnershipState == BuyerQueueConstants.OwnershipStates.Unassigned && isBuyer;
        var canReassign = isSystemAdmin || isLocalManager;

        return new BuyerQueueItemDto
        {
            RequestId = r.Id,
            RequestNumber = r.RequestNumber ?? string.Empty,
            Title = r.Title,
            RequesterId = r.RequesterId,
            RequesterName = r.Requester?.FullName,
            CompanyName = r.Company?.Name,
            PlantName = r.Plant?.Name,
            DepartmentName = r.Department?.Name,
            RequestStatusCode = r.Status.Code,
            NeedLevelCode = r.NeedLevel?.Code,
            NeedByDateUtc = r.NeedByDateUtc,
            CreatedAtUtc = r.CreatedAtUtc,
            PriorityBand = p.PriorityBand,
            DeadlineCondition = p.DeadlineCondition,
            BuyerId = r.BuyerId,
            BuyerName = r.Buyer?.FullName,
            OwnershipState = p.OwnershipState,
            OperationalState = p.OperationalState,
            OperationalStateLabel = p.OperationalStateLabel,
            NextActions = p.NextBuyerActions.Select(a => new BuyerNextActionDto { Code = a.Code, Label = a.Label, Actionable = a.Actionable }).ToList(),
            CoverageStatus = p.CoverageStatus,
            ActiveItemCount = p.ActiveItemCount,
            CoveredCount = p.CoveredCount,
            PendingCount = p.PendingCount,
            QuotationCount = r.Quotations.Count,
            ActiveBatchCount = p.ActiveBatchCount,
            CoverageCounts = new Dictionary<string, int>(p.CoverageCounts),
            AttentionSignals = p.AttentionSignals.Select(s => new BuyerAttentionSignalDto { Code = s.Code, Severity = s.Severity }).ToList(),
            RequiresAttention = p.RequiresAttention,
            HasNotes = note != null && note.Count > 0,
            NoteCount = note?.Count ?? 0,
            LatestNoteText = note?.LatestText,
            LatestNoteAtUtc = note != null ? note.LatestAtUtc : (DateTime?)null,
            LatestNoteActorName = note?.LatestActorName,
            CanOpen = true,
            CanClaim = canClaim,
            CanReassign = canReassign,
            CanCancel = p.CanCancel,
            CancelBlockReason = p.CancelBlockReason
        };
    }
}
