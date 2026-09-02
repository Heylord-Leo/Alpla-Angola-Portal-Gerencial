using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Common;
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
/// Read-only Buyer Request Workspace read model (Phase 3A). GET /api/v1/buyer/requests/{id}/workspace.
/// Additive and canonical: reuses BuyerQueueProjectionBuilder (identical coverage/state taxonomy as the
/// queue) plus request detail + contextual supplier metrics. No mutations; the Quotation Wizard and the
/// classic workbench are untouched. See docs/BUYER_QUEUE_CANONICAL_MODEL.md.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/buyer/requests")]
public class BuyerWorkspaceController : BaseController
{
    public BuyerWorkspaceController(ApplicationDbContext context) : base(context) { }

    [HttpGet("{id:guid}/workspace")]
    public async Task<ActionResult<BuyerWorkspaceDto>> GetWorkspace(Guid id)
    {
        var scoped = await GetScopedRequestsQuery();
        var r = await scoped.Where(x => x.Id == id)
            .Include(x => x.RequestType)
            .Include(x => x.Status)
            .Include(x => x.NeedLevel)
            .Include(x => x.Company)
            .Include(x => x.Plant)
            .Include(x => x.Department)
            .Include(x => x.Buyer)
            .Include(x => x.Requester)
            .Include(x => x.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(x => x.LineItems).ThenInclude(li => li.Unit)
            .Include(x => x.LineItems).ThenInclude(li => li.ItemCatalogItem)
            .Include(x => x.ApprovalBatches).ThenInclude(b => b.Items).ThenInclude(bi => bi.Candidates)
            .Include(x => x.PoGroups)
            .Include(x => x.Quotations).ThenInclude(q => q.Items)
            .Include(x => x.Quotations).ThenInclude(q => q.Supplier)
            .Include(x => x.Attachments)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (r == null)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado", Status = 404 });
        if (r.RequestType.Code != RequestConstants.Types.Quotation)
            return NotFound(new ProblemDetails { Title = "Workspace indisponível", Detail = "O Workspace do Comprador aplica-se apenas a pedidos de cotação.", Status = 404 });

        var input = BuyerQueueProjectionInputFactory.FromRequest(r);
        var p = Proj.Build(input, CurrentUserId, DateTime.UtcNow);
        var buckets = Proj.ClassifyItemCoverage(input);
        var supersededIds = input.SupersededBatchIds;

        var creatorName = await _context.Users
            .Where(u => u.Id == r.CreatedByUserId).Select(u => u.FullName).FirstOrDefaultAsync();

        // Adjustment V2 (Phase 3 + 4): the LATEST structured cycle per batch (read-only detail for
        // the "Lotes & Aprovações" surface) — the open cycle while one is in progress, otherwise the
        // most recent closed cycle (so a resubmitted cycle and its Buyer "Resposta ao reajuste" stay
        // visible after RESUBMITTED). Legacy/never-adjusted batches project no cycle. NOT the Phase 7
        // timeline — one latest cycle only.
        var batchIds = r.ApprovalBatches.Select(b => b.Id).ToList();
        var allCycles = batchIds.Count == 0
            ? new List<ApprovalBatchAdjustment>()
            : await _context.ApprovalBatchAdjustments.AsNoTracking()
                .Include(a => a.Reasons)
                .Include(a => a.Resolutions)
                .Where(a => batchIds.Contains(a.ApprovalBatchId))
                .ToListAsync();
        var latestCycles = allCycles
            .GroupBy(a => a.ApprovalBatchId)
            .Select(g => g.OrderByDescending(a => a.CycleNumber).First())
            .ToList();
        var cycleActorIds = latestCycles.Select(a => a.RequestedByUserId)
            .Concat(latestCycles.SelectMany(a => a.Resolutions).Select(res => res.ResolvedByUserId))
            .Distinct().ToList();
        var cycleActorNames = cycleActorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _context.Users.AsNoTracking()
                .Where(u => cycleActorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName);
        var cycleLineNumberById = r.LineItems.ToDictionary(li => li.Id, li => li.LineNumber);
        var adjustmentsByBatch = latestCycles.ToDictionary(a => a.ApprovalBatchId, a =>
        {
            var buyerResolution = a.Resolutions
                .FirstOrDefault(res => res.ActorType == AdjustmentConstants.ActorTypes.Buyer);
            return new BuyerWorkspaceBatchAdjustmentDto
            {
                CycleNumber = a.CycleNumber,
                SourceStage = a.SourceStage,
                Status = a.Status,
                WholeBatch = a.WholeBatch,
                ApproverComment = a.ApproverComment,
                RequestedByName = cycleActorNames.TryGetValue(a.RequestedByUserId, out var nm) ? nm : null,
                RequestedAtUtc = a.RequestedAtUtc,
                Reasons = a.Reasons.Select(rn => new BuyerWorkspaceBatchAdjustmentReasonDto
                {
                    ReasonCode = rn.ReasonCode,
                    RequestLineItemId = rn.RequestLineItemId,
                    LineNumber = rn.RequestLineItemId.HasValue && cycleLineNumberById.TryGetValue(rn.RequestLineItemId.Value, out var ln) ? ln : (int?)null,
                    Detail = rn.Detail,
                }).ToList(),
                ResponseNote = buyerResolution?.ResolutionComment,
                RespondedByName = buyerResolution != null && cycleActorNames.TryGetValue(buyerResolution.ResolvedByUserId, out var rnm) ? rnm : null,
                RespondedAtUtc = buyerResolution?.ResolvedAtUtc,
            };
        });

        int Bkt(string key) => p.CoverageCounts.TryGetValue(key, out var v) ? v : 0;

        var dto = new BuyerWorkspaceDto
        {
            RequestId = r.Id,
            RequestNumber = r.RequestNumber ?? string.Empty,
            Title = r.Title,
            Description = r.Description,
            RequestStatusCode = r.Status.Code,
            RequesterId = r.RequesterId,
            RequesterName = r.Requester?.FullName,
            BuyerId = r.BuyerId,
            BuyerName = r.Buyer?.FullName,
            CreatedByName = creatorName,
            CompanyName = r.Company?.Name,
            CompanyTaxId = CompanyTaxIds.Resolve(r.Company?.Name, r.Company?.TaxId),
            PlantName = r.Plant?.Name,
            DepartmentName = r.Department?.Name,
            NeedLevelCode = r.NeedLevel?.Code,
            NeedByDateUtc = r.NeedByDateUtc,
            CreatedAtUtc = r.CreatedAtUtc,
            OperationalState = p.OperationalState,
            OperationalStateLabel = p.OperationalStateLabel,
            NextActions = p.NextBuyerActions.Select(a => new BuyerNextActionDto { Code = a.Code, Label = a.Label, Actionable = a.Actionable }).ToList(),
            PriorityBand = p.PriorityBand,
            DeadlineCondition = p.DeadlineCondition,
            RequiresAttention = p.RequiresAttention,
            Coverage = new BuyerWorkspaceCoverageDto
            {
                TotalItems = p.ActiveItemCount,
                Treated = p.CoveredCount,
                Pending = p.PendingCount,
                CoverageStatus = p.CoverageStatus,
                Approved = Bkt(BuyerQueueConstants.CoverageBuckets.Approved),
                InActiveBatch = Bkt(BuyerQueueConstants.CoverageBuckets.InActiveBatch),
                ReadyForBatch = Bkt(BuyerQueueConstants.CoverageBuckets.QuotedReadyForBatch),
                ClosedNotQuoted = Bkt(BuyerQueueConstants.CoverageBuckets.ClosedNotQuoted),
                NotQuotedProposed = Bkt(BuyerQueueConstants.CoverageBuckets.NotQuotedProposed),
                NotQuotedAccepted = Bkt(BuyerQueueConstants.CoverageBuckets.NotQuotedAccepted),
                CancelledDeleted = Bkt(BuyerQueueConstants.CoverageBuckets.CancelledDeleted),
            },
            Items = BuildItems(r, buckets, ActorCanCloseNotQuoted(r)),
            Quotations = BuildQuotations(r),
            Batches = BuildBatches(r, supersededIds, adjustmentsByBatch),
            Suppliers = await BuildSuppliersAsync(r),
        };

        return Ok(dto);
    }

    /// <summary>
    /// Actor-level authorization for close-not-quoted on THIS request — the SAME rule the endpoint
    /// enforces: a System Administrator (in-scope) or a Buyer who owns the request (assigned, or the
    /// request is unassigned). The Workspace read scope (GetScopedRequestsQuery) is broader than mutation
    /// ownership, so this prevents offering an action the endpoint would reject.
    /// </summary>
    private bool ActorCanCloseNotQuoted(Request r)
    {
        var roles = CurrentUserRoles;
        if (roles.Contains(RoleConstants.SystemAdministrator)) return true;
        return roles.Contains(RoleConstants.Buyer) && (r.BuyerId == null || r.BuyerId == CurrentUserId);
    }

    private static List<BuyerWorkspaceItemDto> BuildItems(Request r, IReadOnlyDictionary<Guid, string> buckets, bool actorCanClose)
    {
        // Map selected-quotation-item id → parent quotation (for the "current selection" summary).
        var qItemToQuotation = r.Quotations
            .SelectMany(q => q.Items.Select(qi => new { qi.Id, Quotation = q }))
            .ToDictionary(x => x.Id, x => x.Quotation);

        // Line items sitting in an active/approved batch cannot be closed-not-quoted (mirrors the endpoint
        // guard). Same active-batch status set the endpoint uses.
        var activeBatchStatuses = new[]
        {
            RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
            RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
            RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
            RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
            RequestConstants.ApprovalBatchStatuses.Approved,
        };
        var activeBatchLineItemIds = r.ApprovalBatches
            .Where(b => activeBatchStatuses.Contains(b.Status))
            .SelectMany(b => b.Items.Select(bi => bi.RequestLineItemId))
            .ToHashSet();

        // Item eligibility (lifecycle + not-in-active-batch) AND actor authorization together — the flag
        // is true only when the endpoint would actually accept the close for this caller.
        bool CanClose(RequestLineItem li) =>
            actorCanClose
            && (li.QuotationLifecycleStatus == null ||
                li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending)
            && !activeBatchLineItemIds.Contains(li.Id);

        return r.LineItems
            .Where(li => !li.IsDeleted)
            .OrderBy(li => li.LineNumber)
            .Select(li =>
            {
                string? selectedSummary = null;
                if (li.SelectedQuotationItemId.HasValue && qItemToQuotation.TryGetValue(li.SelectedQuotationItemId.Value, out var q))
                    selectedSummary = q.SupplierNameSnapshot;
                return new BuyerWorkspaceItemDto
                {
                    Id = li.Id,
                    LineNumber = li.LineNumber,
                    ItemCatalogCode = li.ItemCatalogItem?.Code,
                    Description = li.Description,
                    Quantity = li.Quantity,
                    UnitName = li.Unit?.Name,
                    CoverageBucket = buckets.TryGetValue(li.Id, out var b) ? b : BuyerQueueConstants.CoverageBuckets.PendingQuotation,
                    SupplierName = string.IsNullOrEmpty(li.SupplierName) ? null : li.SupplierName,
                    SelectedQuotationSummary = selectedSummary,
                    CanCloseNotQuoted = CanClose(li),
                };
            })
            .ToList();
    }

    private static List<BuyerWorkspaceQuotationDto> BuildQuotations(Request r)
        => r.Quotations
            .OrderByDescending(q => q.CreatedAtUtc)
            .Select(q => new BuyerWorkspaceQuotationDto
            {
                Id = q.Id,
                SupplierId = q.SupplierId,
                SupplierName = q.Supplier?.Name ?? q.SupplierNameSnapshot,
                DocumentNumber = q.DocumentNumber,
                DocumentDate = q.DocumentDate,
                ItemsQuotedCount = q.Items.Count,
                Currency = q.Currency,
                TotalAmount = q.TotalAmount,
                DocumentCount = q.ProformaAttachmentId.HasValue ? 1 : 0,
                IsSelected = q.IsSelected,
            })
            .ToList();

    private static List<BuyerWorkspaceBatchDto> BuildBatches(
        Request r, IReadOnlyCollection<Guid> supersededIds,
        IReadOnlyDictionary<Guid, BuyerWorkspaceBatchAdjustmentDto> adjustmentsByBatch)
    {
        var lineNumberById = r.LineItems.ToDictionary(li => li.Id, li => li.LineNumber);
        return r.ApprovalBatches
            .OrderBy(b => b.BatchNumber)
            .Select(b => new BuyerWorkspaceBatchDto
            {
                Id = b.Id,
                BatchNumber = b.BatchNumber,
                Status = b.Status,
                Kind = BatchKind(b, supersededIds),
                ItemCount = b.Items.Count,
                ItemLineNumbers = b.Items
                    .Select(bi => lineNumberById.TryGetValue(bi.RequestLineItemId, out var ln) ? ln : 0)
                    .Where(ln => ln > 0).OrderBy(ln => ln).ToList(),
                ApprovedTotalAmount = b.ApprovedTotalAmount,
                CreatedAtUtc = b.CreatedAtUtc,
                UpdatedAtUtc = b.UpdatedAtUtc,
                AreaDecisionAtUtc = b.Items.Where(bi => bi.WinnerSelectedAtUtc.HasValue)
                    .Select(bi => bi.WinnerSelectedAtUtc).OrderBy(t => t).FirstOrDefault(),
                Adjustment = adjustmentsByBatch.TryGetValue(b.Id, out var adj) ? adj : null,
            })
            .ToList();
    }

    private static string BatchKind(ApprovalBatch b, IReadOnlyCollection<Guid> supersededIds)
    {
        if (supersededIds.Contains(b.Id)) return "SUPERSEDED";
        return b.Status switch
        {
            RequestConstants.ApprovalBatchStatuses.Approved => "APPROVED",
            RequestConstants.ApprovalBatchStatuses.Rejected => "REJECTED",
            RequestConstants.ApprovalBatchStatuses.Cancelled => "CANCELLED",
            _ => "ACTIVE",
        };
    }

    // Contextual supplier intelligence: only suppliers INVOLVED in THIS request (via Quotations), with
    // GLOBAL MVP track-record metrics. Dedup by normalized NIF; per-currency totals are NEVER summed.
    private async Task<List<BuyerWorkspaceSupplierDto>> BuildSuppliersAsync(Request r)
    {
        var involvedIds = r.Quotations.Where(q => q.SupplierId.HasValue).Select(q => q.SupplierId!.Value).Distinct().ToList();
        if (involvedIds.Count == 0) return new();

        var suppliers = await _context.Suppliers.AsNoTracking().Where(s => involvedIds.Contains(s.Id)).ToListAsync();

        var po = await _context.RequestPoGroups.AsNoTracking()
            .Where(g => g.SupplierId.HasValue && involvedIds.Contains(g.SupplierId.Value)
                        && g.PurchaseOrderNumber != null && g.Status != RequestConstants.PoGroupStatuses.Cancelled)
            .Select(g => new { SupplierId = g.SupplierId!.Value, g.CurrencyCode, g.TotalAmount, g.CreatedAtUtc })
            .ToListAsync();

        var quo = await _context.Quotations.AsNoTracking()
            .Where(q => q.SupplierId.HasValue && involvedIds.Contains(q.SupplierId.Value))
            .Select(q => new { SupplierId = q.SupplierId!.Value, q.IsSelected })
            .ToListAsync();

        var selectedThisRequest = r.Quotations.Where(q => q.IsSelected && q.SupplierId.HasValue)
            .Select(q => q.SupplierId!.Value).ToHashSet();

        // Dedup by normalized NIF (falls back to a per-id key when NIF is absent).
        var result = new List<BuyerWorkspaceSupplierDto>();
        foreach (var group in suppliers.GroupBy(s => TaxIdNormalizer.NormalizeOrNull(s.TaxId) is { } n ? "nif:" + n : "sid:" + s.Id))
        {
            var members = group.ToList();
            var memberIds = members.Select(s => s.Id).ToHashSet();
            var rep = members[0];
            var poRows = po.Where(x => memberIds.Contains(x.SupplierId)).ToList();
            var quoRows = quo.Where(x => memberIds.Contains(x.SupplierId)).ToList();

            result.Add(new BuyerWorkspaceSupplierDto
            {
                SupplierId = rep.Id,
                Name = rep.Name,
                Nif = rep.TaxId,
                IsActive = rep.IsActive,
                RegistrationStatus = rep.RegistrationStatus,
                PurchaseCount = poRows.Count,
                TotalsByCurrency = poRows
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.CurrencyCode) ? "—" : x.CurrencyCode!)
                    .Select(g => new CurrencyAmountDto { Currency = g.Key, Amount = g.Sum(x => x.TotalAmount) })
                    .OrderBy(x => x.Currency).ToList(),
                LastPurchaseUtc = poRows.Count == 0 ? null : poRows.Max(x => x.CreatedAtUtc),
                QuotationsReceived = quoRows.Count,
                QuotationsSelected = quoRows.Count(x => x.IsSelected),
                InvolvedSelected = memberIds.Overlaps(selectedThisRequest),
                CanOpenSheet = false, // Phase 3A: Supplier Sheet reuse is INVASIVE — full profile deferred to 3B.
            });
        }
        return result.OrderByDescending(s => s.InvolvedSelected).ThenBy(s => s.Name).ToList();
    }
}
