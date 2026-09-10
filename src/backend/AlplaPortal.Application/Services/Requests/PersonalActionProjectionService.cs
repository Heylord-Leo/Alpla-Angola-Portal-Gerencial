using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;

namespace AlplaPortal.Application.Services.Requests;

/// <summary>
/// v2.242.0 Phase 1 — Application-layer projection of a user's hydrated, access-scoped requests into
/// personal ACTION ITEMS (not request scalars), grouped into categories with counts, priority-sorted
/// and paginated per category. Owns DTO shape, PT labels, route hints and priority metadata; delegates
/// the pure membership/ownership rules to Domain (<see cref="PersonalActionPredicates"/> /
/// <see cref="PersonalPoCorrectionPredicate"/>). No persistence, no data mutation.
///
/// <para>Scope + ownership are the caller's contract: the caller passes ONLY requests the user may
/// access (RequestAccessScope) that match a broad actionable pre-filter; this service then applies the
/// per-action-type ownership rules so each item is genuinely the user's own work.</para>
/// </summary>
public sealed class PersonalActionProjectionService
{
    public sealed record Context(Guid UserId, bool IsBuyer, bool IsFinance, bool IsReceiver, bool IsFinalApprover, DateTime UtcNow);

    private static readonly string[] ReceivingCodes =
        { "WAITING_RECEIPT", RequestConstants.Statuses.PaymentCompleted, "PAG_REALIZADO", "AG_RECIBO", "WAITING_SUPPLIER_DELIVERY" };

    public MyActionsResponseDto Build(
        IReadOnlyList<Request> requests, Context ctx, string? actionType, int page, int pageSize, string? sort,
        Guid? targetRequestId, Guid? targetPoGroupId, string? targetActionType)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        var all = new List<PersonalActionItemDto>();
        foreach (var r in requests)
            all.AddRange(ProjectRequest(r, ctx));

        // Category summary (chips) — always the full picture, independent of the selected category.
        var categories = all
            .GroupBy(i => i.ActionType)
            .Select(g => new MyActionCategoryDto
            {
                ActionType = g.Key,
                Label = CategoryLabel(g.Key),
                Count = g.Count(),
                HighestPriorityBand = g.Any(i => i.PriorityBand == BuyerQueueConstants.PriorityBands.ExceptionOrOverdue)
                    ? BuyerQueueConstants.PriorityBands.ExceptionOrOverdue
                    : BuyerQueueConstants.PriorityBands.Standard
            })
            .OrderBy(c => CategoryRank(c.ActionType))
            .ToList();

        // Items of the selected category (or all when omitted), priority-sorted.
        IEnumerable<PersonalActionItemDto> items = all;
        if (!string.IsNullOrWhiteSpace(actionType))
            items = items.Where(i => i.ActionType == actionType);
        var ordered = Sort(items, sort).ToList();

        var totalCount = ordered.Count;

        // Target lookup (H): guarantee the deep-link target is on the returned page even if its natural
        // position is deeper. If present and not already on the requested page, return its page instead.
        if (targetRequestId.HasValue)
        {
            var idx = ordered.FindIndex(i => i.RequestId == targetRequestId.Value
                && (!targetPoGroupId.HasValue || i.PoGroupId == targetPoGroupId.Value
                    || i.AffectedSuppliers.Any(s => s.PoGroupId == targetPoGroupId.Value))
                && (string.IsNullOrWhiteSpace(targetActionType) || i.ActionType == targetActionType));
            if (idx >= 0) page = idx / pageSize + 1;
        }

        var slice = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new MyActionsResponseDto
        {
            Categories = categories,
            Items = slice,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private IEnumerable<PersonalActionItemDto> ProjectRequest(Request r, Context ctx)
    {
        var tc = r.RequestType!.Code;
        var sc = r.Status!.Code;
        var items = new List<PersonalActionItemDto>();

        // ── PO_CORRECTION (group-level, cross-type, owner-scoped). One item per request grouping the
        //    user's own WPC groups (same owner + same action → one card with AffectedSuppliers). ──
        if (ctx.IsBuyer)
        {
            var owned = r.PoGroups
                .Where(g => PersonalActionPredicates.OwnsCorrectionGroup(r, g, ctx.UserId))
                .ToList();
            if (owned.Count > 0)
            {
                var primary = owned[0];
                items.Add(NewItem(r, ctx, PersonalActionPredicates.ActionTypes.PoCorrection, "Corrigir P.O.", "WAITING_PO_CORRECTION",
                    poGroup: primary,
                    affected: owned.Select(g => new BuyerPoCorrectionGroupDto
                    {
                        PoGroupId = g.Id, SupplierId = g.SupplierId, SupplierName = g.SupplierNameSnapshot, PurchaseOrderNumber = g.PurchaseOrderNumber
                    }).ToList()));
            }
        }

        var nonDeleted = r.LineItems.Where(li => !li.IsDeleted).ToList();
        var mineOrUnassignedBuyer = r.BuyerId == ctx.UserId || r.BuyerId == null;

        // ── QUOTATION_REQUIRED (Buyer): active quotation with pending items. ──
        if (ctx.IsBuyer && tc == RequestConstants.Types.Quotation && !PersonalActionPredicates.IsTerminal(sc) && mineOrUnassignedBuyer
            && (sc == RequestConstants.Statuses.WaitingQuotation
                || nonDeleted.Any(li => li.QuotationLifecycleStatus == null
                    || li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending)))
            items.Add(NewItem(r, ctx, PersonalActionPredicates.ActionTypes.QuotationRequired, "Cotar itens", sc));

        // ── PO_REGISTRATION (Buyer): QUOTATION awaiting P.O. after final approval. ──
        if (ctx.IsBuyer && tc == RequestConstants.Types.Quotation && sc == RequestConstants.Statuses.FinalApproved && mineOrUnassignedBuyer)
            items.Add(NewItem(r, ctx, PersonalActionPredicates.ActionTypes.PoRegistration, "Registrar P.O.", sc));

        // ── AREA_APPROVAL / FINAL_APPROVAL (owner via approver id). ──
        if (r.AreaApproverId == ctx.UserId && sc == RequestConstants.Statuses.WaitingAreaApproval)
            items.Add(NewItem(r, ctx, PersonalActionPredicates.ActionTypes.AreaApproval, "Aprovar (Área)", sc));
        if (ctx.IsFinalApprover && sc == RequestConstants.Statuses.WaitingFinalApproval)
            items.Add(NewItem(r, ctx, PersonalActionPredicates.ActionTypes.FinalApproval, "Aprovar (Final)", sc));

        // ── REQUEST_ADJUSTMENT (Requester). ──
        if (r.RequesterId == ctx.UserId
            && (sc == RequestConstants.Statuses.Draft || sc == RequestConstants.Statuses.AreaAdjustment || sc == RequestConstants.Statuses.FinalAdjustment))
            items.Add(NewItem(r, ctx, PersonalActionPredicates.ActionTypes.RequestAdjustment, "Ajustar pedido", sc));

        // ── NOT_QUOTED_DECISION (Requester). ──
        if (r.RequesterId == ctx.UserId && tc == RequestConstants.Types.Quotation && !PersonalActionPredicates.IsTerminal(sc)
            && nonDeleted.Any(li => li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed))
            items.Add(NewItem(r, ctx, PersonalActionPredicates.ActionTypes.NotQuotedDecision, "Decidir não cotados", sc));

        // ── RECEIVING (Receiver or Requester). ──
        if ((ctx.IsReceiver || r.RequesterId == ctx.UserId) && ReceivingCodes.Contains(sc))
            items.Add(NewItem(r, ctx, PersonalActionPredicates.ActionTypes.Receiving, "Receber", sc));

        // ── FINANCE_ACTION (Finance). ──
        if (ctx.IsFinance && ((sc == RequestConstants.Statuses.FinalApproved && tc == RequestConstants.Types.Payment)
            || sc == RequestConstants.Statuses.PoIssued || sc == RequestConstants.Statuses.PaymentRequestSent
            || sc == RequestConstants.Statuses.PaymentScheduled || sc == "ADVANCE_PAYMENT_REQUIRED" || sc == "WAITING_RECONCILIATION"))
            items.Add(NewItem(r, ctx, PersonalActionPredicates.ActionTypes.FinanceAction, "Ação financeira", sc));

        return items;
    }

    private PersonalActionItemDto NewItem(Request r, Context ctx, string actionType, string label, string actionStatus,
        RequestPoGroup? poGroup = null, List<BuyerPoCorrectionGroupDto>? affected = null)
    {
        var overdue = r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value.Date < ctx.UtcNow.Date;
        var band = (overdue || actionType == PersonalActionPredicates.ActionTypes.PoCorrection)
            ? BuyerQueueConstants.PriorityBands.ExceptionOrOverdue
            : BuyerQueueConstants.PriorityBands.Standard;
        var route = $"/requests?action={actionType}&requestId={r.Id}" + (poGroup != null ? $"&poGroupId={poGroup.Id}" : "");
        return new PersonalActionItemDto
        {
            ActionId = poGroup != null ? $"{actionType}:{r.Id}:{poGroup.Id}" : $"{actionType}:{r.Id}",
            RequestId = r.Id,
            RequestNumber = r.RequestNumber ?? string.Empty,
            RequestTitle = r.Title,
            RequestTypeCode = r.RequestType!.Code,
            ActionType = actionType,
            ActionLabel = label,
            ActionStatus = actionStatus,
            PoGroupId = poGroup?.Id,
            SupplierId = poGroup?.SupplierId,
            SupplierName = poGroup?.SupplierNameSnapshot,
            PurchaseOrderNumber = poGroup?.PurchaseOrderNumber,
            AffectedSuppliers = affected ?? new List<BuyerPoCorrectionGroupDto>(),
            OwnerUserId = ctx.UserId,
            OwnerName = r.Buyer?.FullName,
            DueDateUtc = r.NeedByDateUtc,
            NeedLevelCode = r.NeedLevel?.Code,
            StageEnteredAtUtc = null,
            CreatedAtUtc = r.CreatedAtUtc,
            IsOverdue = overdue,
            PriorityBand = band,
            Amount = poGroup?.TotalAmount ?? r.EstimatedTotalAmount,
            CurrencyCode = poGroup?.CurrencyCode,
            Route = route
        };
    }

    private static int NeedLevelRank(string? code) => code switch
    { "CRITICO" => 0, "URGENTE" => 1, "NORMAL" => 2, "BAIXO" => 3, _ => 4 };

    private static IEnumerable<PersonalActionItemDto> Sort(IEnumerable<PersonalActionItemDto> items, string? sort) =>
        // Deterministic priority: exception/overdue → nearest/oldest due → need level → stage/created age → number.
        items
            .OrderByDescending(i => i.PriorityBand == BuyerQueueConstants.PriorityBands.ExceptionOrOverdue)
            .ThenBy(i => i.DueDateUtc ?? DateTime.MaxValue)
            .ThenBy(i => NeedLevelRank(i.NeedLevelCode))
            .ThenBy(i => i.StageEnteredAtUtc ?? i.CreatedAtUtc)
            .ThenBy(i => i.RequestNumber);

    private static string CategoryLabel(string actionType) => actionType switch
    {
        PersonalActionPredicates.ActionTypes.PoCorrection => "Correções de P.O.",
        PersonalActionPredicates.ActionTypes.QuotationRequired => "Cotações",
        PersonalActionPredicates.ActionTypes.PoRegistration => "P.O. a registrar",
        PersonalActionPredicates.ActionTypes.AreaApproval => "Aprovações de área",
        PersonalActionPredicates.ActionTypes.FinalApproval => "Aprovações finais",
        PersonalActionPredicates.ActionTypes.RequestAdjustment => "Ajustes",
        PersonalActionPredicates.ActionTypes.NotQuotedDecision => "Decisões de não cotados",
        PersonalActionPredicates.ActionTypes.Receiving => "Recebimentos",
        PersonalActionPredicates.ActionTypes.FinanceAction => "Ações financeiras",
        _ => actionType
    };

    // Landing/category order: most-severe first (corrections, adjustments) → quotation → PO → approvals → rest.
    private static int CategoryRank(string actionType) => actionType switch
    {
        PersonalActionPredicates.ActionTypes.PoCorrection => 0,
        PersonalActionPredicates.ActionTypes.RequestAdjustment => 1,
        PersonalActionPredicates.ActionTypes.NotQuotedDecision => 2,
        PersonalActionPredicates.ActionTypes.QuotationRequired => 3,
        PersonalActionPredicates.ActionTypes.PoRegistration => 4,
        PersonalActionPredicates.ActionTypes.AreaApproval => 5,
        PersonalActionPredicates.ActionTypes.FinalApproval => 6,
        PersonalActionPredicates.ActionTypes.Receiving => 7,
        PersonalActionPredicates.ActionTypes.FinanceAction => 8,
        _ => 99
    };
}
