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
/// Dashboard V2 B8 — canonical Alerts. Risk/deadline conditions over canonical entities that still have an
/// OPEN action, higher-signal than the operational queues.
///
/// Buyer alerts reuse the CANONICAL Buyer actionability (<see cref="BuyerQueueProjectionBuilder"/>, the
/// single source of truth — never a second predicate copy), but the expensive hydrate is bounded to a tiny
/// NEAR-DEADLINE candidate set (NeedByDateUtc &lt;= today+2 within buyer-active statuses), so this does NOT
/// add a third full Buyer sweep. NeedByDateUtc becomes an alert ONLY while a Buyer action is open — a
/// PAYMENT_COMPLETED/PO/receiving request with a past NeedBy produces no alert (fixes the legacy 91% stale).
///
/// Finance alerts use ONE flat, scope-bound RequestPayments query (NOT FinanceObligationSummaryProjection):
/// SCHEDULED owed-money payments whose ScheduledDateUtc is past (overdue) or today/tomorrow, deduped to one
/// alert per PO group. No Receiving/Approval/PO/Documentation aging (deferred to B9). No money/FX.
/// </summary>
public sealed class CanonicalAlertProjection
{
    private readonly ApplicationDbContext _context;

    public CanonicalAlertProjection(ApplicationDbContext context) => _context = context;

    private const int MaxAlerts = 100;

    private static readonly string[] BuyerActiveRequestStatusCodes =
    {
        RequestConstants.Statuses.Draft, RequestConstants.Statuses.WaitingQuotation,
        RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.AreaAdjustment,
        RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.FinalAdjustment,
    };

    private static readonly string[] OwedMoneyPaymentTypes =
    {
        RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentTypes.Regularization,
    };

    public async Task<DashboardV2AlertsDto> BuildAsync(
        IQueryable<Request> scoped, Guid currentUserId,
        bool isBuyer, bool isFinance, bool canSeeManagerial, DateTime today)
    {
        var entitled = isBuyer || isFinance || canSeeManagerial;
        if (!entitled)
            return new DashboardV2AlertsDto { Summary = null, Alerts = new(), GeneratedAtUtc = DateTime.UtcNow };

        var alerts = new List<DashboardV2AlertDto>();
        if (isBuyer || canSeeManagerial)
            alerts.AddRange(await BuildBuyerAlertsAsync(scoped, currentUserId, isBuyer, canSeeManagerial, today));
        if (isFinance || canSeeManagerial)
            alerts.AddRange(await BuildFinanceAlertsAsync(scoped, isFinance, canSeeManagerial, today));

        // Stable identity dedup, then deterministic sort.
        var deduped = alerts
            .GroupBy(a => a.Id)
            .Select(g => g.First())
            .ToList();

        deduped = deduped
            .OrderByDescending(a => a.Severity == AlertSeverities.Critical ? 1 : 0) // CRITICAL first
            .ThenBy(a => a.DateUtc)                                                  // earliest relevant date
            .ThenBy(a => a.Domain)
            .ThenBy(a => a.EntityId)
            .ToList();

        // Display cap: counts always reflect the COMPLETE deduped population; the list is bounded and the
        // truncation is made explicit. No extra query — everything derives from `deduped`.
        var displayed = deduped.Take(MaxAlerts).ToList();

        var summary = new DashboardV2AlertsSummaryDto
        {
            AttentionCount = deduped.Count(a => a.Severity == AlertSeverities.Attention),
            CriticalCount = deduped.Count(a => a.Severity == AlertSeverities.Critical),
            ByDomain = deduped
                .GroupBy(a => a.Domain)
                .OrderBy(g => g.Key)
                .Select(g => new AlertDomainCountDto
                {
                    Domain = g.Key,
                    Attention = g.Count(a => a.Severity == AlertSeverities.Attention),
                    Critical = g.Count(a => a.Severity == AlertSeverities.Critical),
                })
                .ToList(),
            TotalAlertCount = deduped.Count,
            DisplayedAlertCount = displayed.Count,
            IsTruncated = deduped.Count > displayed.Count,
        };

        return new DashboardV2AlertsDto
        {
            Summary = summary,
            Alerts = displayed,
            GeneratedAtUtc = DateTime.UtcNow,
        };
    }

    // ── BUYER: canonical actionability over a bounded near-deadline candidate set. ──
    private async Task<List<DashboardV2AlertDto>> BuildBuyerAlertsAsync(
        IQueryable<Request> scoped, Guid currentUserId, bool isBuyer, bool canSeeManagerial, DateTime today)
    {
        var candidateCutoff = today.AddDays(3); // NeedBy date <= today+2  ⇔  NeedByDateUtc < today+3

        var candidates = await scoped
            .Where(r => r.RequestType!.Code == RequestConstants.Types.Quotation
                        && BuyerActiveRequestStatusCodes.Contains(r.Status!.Code)
                        && r.NeedByDateUtc != null && r.NeedByDateUtc < candidateCutoff)
            .Include(r => r.RequestType).Include(r => r.Status).Include(r => r.NeedLevel).Include(r => r.Buyer)
            .Include(r => r.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(r => r.ApprovalBatches).ThenInclude(b => b.Items).ThenInclude(bi => bi.Candidates)
            .Include(r => r.PoGroups)
            .Include(r => r.Quotations).ThenInclude(qq => qq.Items)
            .Include(r => r.Attachments)
            .AsSplitQuery().AsNoTracking()
            .ToListAsync();

        var result = new List<DashboardV2AlertDto>();
        foreach (var r in candidates)
        {
            // CANONICAL open-action test — actionability is ownership-independent.
            var p = Proj.Build(BuyerQueueProjectionInputFactory.FromRequest(r), Guid.Empty, today);
            if (BuyerQueueConstants.OperationalStates.HiddenByDefault.Contains(p.OperationalState)) continue;
            if (!p.NextBuyerActions.Any(a => a.Actionable)) continue;

            var plane = ResolveBuyerPlane(r.BuyerId, currentUserId, isBuyer, canSeeManagerial,
                out var canNavigate, out var targetPath);
            if (plane == null) continue; // not visible to this viewer

            var needDate = r.NeedByDateUtc!.Value.Date;
            var daysDelta = (needDate - today).Days;
            string alertType, severity, title;
            if (needDate < today) { alertType = AlertTypes.BuyerOverdue; severity = AlertSeverities.Critical; title = "Cotação vencida"; }
            else if (needDate == today) { alertType = AlertTypes.BuyerDueToday; severity = AlertSeverities.Critical; title = "Cotação vence hoje"; }
            else { alertType = AlertTypes.BuyerDueSoon; severity = AlertSeverities.Attention; title = "Cotação vence em breve"; }

            result.Add(new DashboardV2AlertDto
            {
                Id = Id(AlertDomains.Buyer, AlertEntityTypes.Request, r.Id.ToString(), alertType),
                Domain = AlertDomains.Buyer, EntityType = AlertEntityTypes.Request, EntityId = r.Id.ToString(),
                RequestId = r.Id, RequestNumber = r.RequestNumber ?? string.Empty,
                AlertType = alertType, Severity = severity, Plane = plane,
                Title = title, Description = DeadlineDescription(daysDelta),
                DateUtc = needDate, DaysDelta = daysDelta,
                TargetPath = targetPath, CanNavigate = canNavigate,
            });
        }
        return result;
    }

    // Assigned to me → Pessoal; unassigned & I'm a Buyer → Compartilhado; else a manager sees it Gerencial
    // (view-only); another buyer's assigned work is not visible to a plain buyer.
    private static string? ResolveBuyerPlane(Guid? buyerId, Guid currentUserId, bool isBuyer, bool canSeeManagerial,
        out bool canNavigate, out string? targetPath)
    {
        if (buyerId == currentUserId)
        {
            canNavigate = true; targetPath = "/buyer/items?ownership=me";
            return AlertPlanes.Pessoal;
        }
        if (buyerId == null && isBuyer)
        {
            canNavigate = true; targetPath = "/buyer/items?ownership=unassigned";
            return AlertPlanes.Compartilhado;
        }
        if (canSeeManagerial)
        {
            canNavigate = false; targetPath = null;
            return AlertPlanes.Gerencial;
        }
        canNavigate = false; targetPath = null;
        return null;
    }

    // ── FINANCE: one flat scope-bound RequestPayments query, deduped to one alert per PO group. ──
    private async Task<List<DashboardV2AlertDto>> BuildFinanceAlertsAsync(
        IQueryable<Request> scoped, bool isFinance, bool canSeeManagerial, DateTime today)
    {
        var dueCutoff = today.AddDays(2); // ScheduledDate <= today+1

        var rows = await (
            from p in _context.RequestPayments
            join r in scoped on p.RequestId equals r.Id
            where p.PaymentStatus == RequestPayment.PaymentStatuses.Scheduled
                  && OwedMoneyPaymentTypes.Contains(p.PaymentType)
                  && p.ScheduledDateUtc != null && p.ScheduledDateUtc < dueCutoff
                  && p.RequestPoGroupId != null
            select new { GroupId = p.RequestPoGroupId!.Value, RequestId = r.Id, r.RequestNumber, Scheduled = p.ScheduledDateUtc!.Value })
            .ToListAsync();

        var result = new List<DashboardV2AlertDto>();
        foreach (var g in rows.GroupBy(x => x.GroupId))
        {
            var earliest = g.Min(x => x.Scheduled).Date;              // relevant date = earliest qualifying
            var overdue = g.Any(x => x.Scheduled.Date < today);       // any overdue → overdue alert
            var first = g.First();
            var daysDelta = (earliest - today).Days;

            var alertType = overdue ? AlertTypes.FinanceScheduledOverdue : AlertTypes.FinanceScheduledDueSoon;
            var severity = overdue ? AlertSeverities.Critical : AlertSeverities.Attention;

            var plane = ResolveFinancePlane(isFinance, canSeeManagerial, overdue, out var canNavigate, out var targetPath);
            if (plane == null) continue;

            result.Add(new DashboardV2AlertDto
            {
                Id = Id(AlertDomains.Finance, AlertEntityTypes.PoGroup, g.Key.ToString(), alertType),
                Domain = AlertDomains.Finance, EntityType = AlertEntityTypes.PoGroup, EntityId = g.Key.ToString(),
                RequestId = first.RequestId, RequestNumber = first.RequestNumber ?? string.Empty,
                AlertType = alertType, Severity = severity, Plane = plane,
                Title = overdue ? "Pagamento agendado vencido" : "Pagamento agendado para breve",
                Description = overdue ? DeadlineDescription(daysDelta) : DeadlineDescription(daysDelta),
                DateUtc = earliest, DaysDelta = daysDelta,
                TargetPath = targetPath, CanNavigate = canNavigate,
            });
        }
        return result;
    }

    private static string? ResolveFinancePlane(bool isFinance, bool canSeeManagerial, bool overdue,
        out bool canNavigate, out string? targetPath)
    {
        if (isFinance)
        {
            // Overdue has an exact /finance/payments filter; due-soon has none → non-navigable.
            canNavigate = overdue; targetPath = overdue ? "/finance/payments?overdueOnly=true" : null;
            return AlertPlanes.Compartilhado;
        }
        if (canSeeManagerial)
        {
            canNavigate = false; targetPath = null;
            return AlertPlanes.Gerencial;
        }
        canNavigate = false; targetPath = null;
        return null;
    }

    private static string Id(string domain, string entityType, string entityId, string alertType)
        => $"{domain}:{entityType}:{entityId}:{alertType}";

    private static string DeadlineDescription(int daysDelta)
    {
        if (daysDelta < 0) return $"Vencido há {-daysDelta} dia(s).";
        if (daysDelta == 0) return "Vence hoje.";
        return $"Vence em {daysDelta} dia(s).";
    }
}
