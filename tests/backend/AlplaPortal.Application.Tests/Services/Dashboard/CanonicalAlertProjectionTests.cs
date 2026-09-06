using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B8.1 — canonical Alerts. Buyer alerts fire only while a canonical Buyer action is open (near-deadline),
/// never on a past NeedBy after the request left the Buyer phase (the legacy 91%-stale fix). Finance alerts
/// come from a flat SCHEDULED-payment query, one per PO group. Planes/severity/scope/dedup locked here.
/// </summary>
public class CanonicalAlertProjectionTests
{
    private const int TypeQuotation = 1;
    private const int StDraft = 10, StWaitingQuotation = 11, StPaymentCompleted = 14, StPoIssued = 15;

    private static readonly DateTime Today = new(2026, 6, 15);

    private static ApplicationDbContext NewDb()
    {
        var ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        ctx.RequestTypes.Add(new RequestType { Id = TypeQuotation, Code = RequestConstants.Types.Quotation, Name = "Q" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = StDraft, Code = RequestConstants.Statuses.Draft, Name = "D" },
            new RequestStatus { Id = StWaitingQuotation, Code = RequestConstants.Statuses.WaitingQuotation, Name = "WQ" },
            new RequestStatus { Id = StPaymentCompleted, Code = RequestConstants.Statuses.PaymentCompleted, Name = "PC" },
            new RequestStatus { Id = StPoIssued, Code = RequestConstants.Statuses.PoIssued, Name = "PO" });
        ctx.SaveChanges();
        return ctx;
    }

    // A WAITING_QUOTATION quotation request with one pending line item → NeedsQuotation → ADD_QUOTATION (open).
    private static Request BuyerActionable(int statusId, DateTime? needBy, Guid? buyer)
    {
        var r = new Request
        {
            Id = Guid.NewGuid(), Title = "t", RequestNumber = "R-" + Guid.NewGuid().ToString("N")[..6],
            StatusId = statusId, RequestTypeId = TypeQuotation, RequesterId = Guid.NewGuid(), BuyerId = buyer,
            DepartmentId = 5, CompanyId = 1, PlantId = 1, CurrencyId = 1, CreatedAtUtc = Today, NeedByDateUtc = needBy,
        };
        r.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = r.Id, Description = "i", Quantity = 1, IsDeleted = false, QuotationLifecycleStatus = null });
        return r;
    }

    private static int _seq;
    private static RequestPayment ScheduledPay(Guid reqId, Guid groupId, DateTime scheduled, string type = "FINAL_BALANCE") => new()
    {
        RequestId = reqId, RequestPoGroupId = groupId, PaymentType = type, PaymentStatus = RequestPayment.PaymentStatuses.Scheduled,
        PlannedAmount = 100m, ScheduledDateUtc = scheduled, PaymentSequence = ++_seq, CreatedAtUtc = Today, CreatedByUserId = Guid.NewGuid(),
    };

    private static async Task<DashboardV2AlertsDto> Build(ApplicationDbContext ctx, Guid me,
        bool isBuyer = false, bool isFinance = false, bool manager = false)
    {
        await ctx.SaveChangesAsync();
        return await new CanonicalAlertProjection(ctx).BuildAsync(ctx.Requests, me, isBuyer, isFinance, manager, Today);
    }

    // ── BUYER severity/window ──
    [Theory]
    [InlineData(-3, AlertTypes.BuyerOverdue, AlertSeverities.Critical)]
    [InlineData(-1, AlertTypes.BuyerOverdue, AlertSeverities.Critical)]
    [InlineData(0, AlertTypes.BuyerDueToday, AlertSeverities.Critical)]
    [InlineData(1, AlertTypes.BuyerDueSoon, AlertSeverities.Attention)]
    [InlineData(2, AlertTypes.BuyerDueSoon, AlertSeverities.Attention)]
    public async Task Buyer_open_action_alerts_by_needby_window(int deltaDays, string type, string severity)
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(deltaDays), me));
        var d = await Build(ctx, me, isBuyer: true);
        var a = Assert.Single(d.Alerts);
        Assert.Equal(type, a.AlertType);
        Assert.Equal(severity, a.Severity);
        Assert.Equal(deltaDays, a.DaysDelta);
    }

    [Fact]
    public async Task Buyer_more_than_two_days_out_is_not_alerted()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(3), me));
        var d = await Build(ctx, me, isBuyer: true);
        Assert.Empty(d.Alerts);
    }

    [Fact]
    public async Task Payment_completed_request_with_past_needby_produces_no_buyer_alert()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        // Past the Buyer phase (PAYMENT_COMPLETED) with a NeedBy 30 days ago → the legacy 91%-stale case.
        var r = BuyerActionable(StPaymentCompleted, Today.AddDays(-30), me);
        ctx.Requests.Add(r);
        var d = await Build(ctx, me, isBuyer: true, isFinance: true, manager: true);
        Assert.DoesNotContain(d.Alerts, a => a.Domain == AlertDomains.Buyer);
    }

    // ── BUYER planes ──
    [Fact]
    public async Task Buyer_assigned_to_me_is_pessoal_with_exact_target()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(-1), me));
        var d = await Build(ctx, me, isBuyer: true);
        var a = Assert.Single(d.Alerts);
        Assert.Equal(AlertPlanes.Pessoal, a.Plane);
        Assert.True(a.CanNavigate);
        Assert.Equal("/buyer/items?ownership=me", a.TargetPath);
    }

    [Fact]
    public async Task Buyer_unassigned_is_compartilhado_for_a_buyer()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(-1), buyer: null));
        var d = await Build(ctx, me, isBuyer: true);
        var a = Assert.Single(d.Alerts);
        Assert.Equal(AlertPlanes.Compartilhado, a.Plane);
        Assert.Equal("/buyer/items?ownership=unassigned", a.TargetPath);
    }

    [Fact]
    public async Task Another_buyers_assignment_is_not_visible_to_a_plain_buyer_but_gerencial_to_a_manager()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(-1), buyer: other));

        var plain = await Build(ctx, me, isBuyer: true);
        Assert.Empty(plain.Alerts); // another buyer's assigned work is not personal/shared to me

        var mgr = await Build(ctx, me, isBuyer: false, manager: true);
        var a = Assert.Single(mgr.Alerts);
        Assert.Equal(AlertPlanes.Gerencial, a.Plane);
        Assert.False(a.CanNavigate);
    }

    [Fact]
    public async Task SysAdmin_role_alone_does_not_make_others_work_personal()
    {
        using var ctx = NewDb();
        var admin = Guid.NewGuid();
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(-1), buyer: Guid.NewGuid()));
        var d = await Build(ctx, admin, manager: true); // SysAdmin/Manager
        var a = Assert.Single(d.Alerts);
        Assert.Equal(AlertPlanes.Gerencial, a.Plane);
    }

    // ── FINANCE ──
    private (Request r, Guid groupId) FinanceReq(ApplicationDbContext ctx)
    {
        var r = new Request
        {
            Id = Guid.NewGuid(), Title = "f", RequestNumber = "F-" + Guid.NewGuid().ToString("N")[..6],
            StatusId = StPoIssued, RequestTypeId = TypeQuotation, RequesterId = Guid.NewGuid(),
            DepartmentId = 5, CompanyId = 1, PlantId = 1, CurrencyId = 1, CreatedAtUtc = Today,
        };
        ctx.Requests.Add(r);
        return (r, Guid.NewGuid());
    }

    [Theory]
    [InlineData(-1, AlertTypes.FinanceScheduledOverdue, AlertSeverities.Critical)]
    [InlineData(0, AlertTypes.FinanceScheduledDueSoon, AlertSeverities.Attention)]
    [InlineData(1, AlertTypes.FinanceScheduledDueSoon, AlertSeverities.Attention)]
    public async Task Finance_scheduled_payment_alerts_by_window(int deltaDays, string type, string severity)
    {
        using var ctx = NewDb();
        var (r, g) = FinanceReq(ctx);
        ctx.RequestPayments.Add(ScheduledPay(r.Id, g, Today.AddDays(deltaDays)));
        var d = await Build(ctx, Guid.NewGuid(), isFinance: true);
        var a = Assert.Single(d.Alerts);
        Assert.Equal(AlertDomains.Finance, a.Domain);
        Assert.Equal(AlertEntityTypes.PoGroup, a.EntityType);
        Assert.Equal(type, a.AlertType);
        Assert.Equal(severity, a.Severity);
    }

    [Fact]
    public async Task Finance_two_days_out_and_completed_and_cancelled_are_not_alerted()
    {
        using var ctx = NewDb();
        var (r, g) = FinanceReq(ctx);
        ctx.RequestPayments.Add(ScheduledPay(r.Id, g, Today.AddDays(2))); // beyond window
        var completed = ScheduledPay(r.Id, Guid.NewGuid(), Today.AddDays(-1)); completed.PaymentStatus = RequestPayment.PaymentStatuses.Completed;
        var cancelled = ScheduledPay(r.Id, Guid.NewGuid(), Today.AddDays(-1)); cancelled.PaymentStatus = RequestPayment.PaymentStatuses.Cancelled;
        ctx.RequestPayments.AddRange(completed, cancelled);
        var d = await Build(ctx, Guid.NewGuid(), isFinance: true);
        Assert.Empty(d.Alerts);
    }

    [Fact]
    public async Task Finance_multiple_qualifying_payments_in_one_group_yield_one_critical_alert_earliest_date()
    {
        using var ctx = NewDb();
        var (r, g) = FinanceReq(ctx);
        ctx.RequestPayments.AddRange(
            ScheduledPay(r.Id, g, Today.AddDays(-5)),  // overdue (earliest)
            ScheduledPay(r.Id, g, Today.AddDays(-2)),  // overdue
            ScheduledPay(r.Id, g, Today.AddDays(1)));  // tomorrow
        var d = await Build(ctx, Guid.NewGuid(), isFinance: true);
        var a = Assert.Single(d.Alerts);                    // one alert per group
        Assert.Equal(AlertTypes.FinanceScheduledOverdue, a.AlertType); // any overdue → overdue
        Assert.Equal(AlertSeverities.Critical, a.Severity);
        Assert.Equal(Today.AddDays(-5), a.DateUtc);         // earliest qualifying date
    }

    [Fact]
    public async Task Finance_plane_is_compartilhado_for_finance_and_gerencial_for_manager()
    {
        using var ctx = NewDb();
        var (r, g) = FinanceReq(ctx);
        ctx.RequestPayments.Add(ScheduledPay(r.Id, g, Today.AddDays(-1)));
        await ctx.SaveChangesAsync();

        var fin = await new CanonicalAlertProjection(ctx).BuildAsync(ctx.Requests, Guid.NewGuid(), false, true, false, Today);
        Assert.Equal(AlertPlanes.Compartilhado, fin.Alerts.Single().Plane);
        Assert.True(fin.Alerts.Single().CanNavigate);
        Assert.Equal("/finance/payments?overdueOnly=true", fin.Alerts.Single().TargetPath);

        var mgr = await new CanonicalAlertProjection(ctx).BuildAsync(ctx.Requests, Guid.NewGuid(), false, false, true, Today);
        Assert.Equal(AlertPlanes.Gerencial, mgr.Alerts.Single().Plane);
        Assert.False(mgr.Alerts.Single().CanNavigate);
    }

    [Fact]
    public async Task Finance_scope_excludes_out_of_scope_requests()
    {
        using var ctx = NewDb();
        var (rIn, gIn) = FinanceReq(ctx);
        var (rOut, gOut) = FinanceReq(ctx);
        ctx.RequestPayments.AddRange(ScheduledPay(rIn.Id, gIn, Today.AddDays(-1)), ScheduledPay(rOut.Id, gOut, Today.AddDays(-1)));
        await ctx.SaveChangesAsync();
        var scoped = ctx.Requests.Where(x => x.Id == rIn.Id);
        var d = await new CanonicalAlertProjection(ctx).BuildAsync(scoped, Guid.NewGuid(), false, true, false, Today);
        var a = Assert.Single(d.Alerts);
        Assert.Equal(rIn.Id, a.RequestId);
    }

    // ── GLOBAL ──
    [Fact]
    public async Task Not_entitled_returns_null_summary_and_no_alerts()
    {
        using var ctx = NewDb();
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(-1), Guid.NewGuid()));
        var d = await Build(ctx, Guid.NewGuid()); // no roles
        Assert.Null(d.Summary);
        Assert.Empty(d.Alerts);
    }

    [Fact]
    public async Task Summary_derives_from_the_final_deduped_list_and_sorts_critical_first()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(2), me));   // ATTENTION
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(-1), me));  // CRITICAL
        var (r, g) = FinanceReq(ctx);
        ctx.RequestPayments.Add(ScheduledPay(r.Id, g, Today.AddDays(-1)));             // CRITICAL
        var d = await Build(ctx, me, isBuyer: true, isFinance: true);

        Assert.Equal(2, d.Summary!.CriticalCount);
        Assert.Equal(1, d.Summary.AttentionCount);
        Assert.Equal(AlertSeverities.Critical, d.Alerts.First().Severity); // critical sorted first
        Assert.Equal(d.Summary.CriticalCount + d.Summary.AttentionCount, d.Alerts.Count);
        // Per-domain summary present.
        Assert.Contains(d.Summary.ByDomain, x => x.Domain == AlertDomains.Buyer);
        Assert.Contains(d.Summary.ByDomain, x => x.Domain == AlertDomains.Finance);
    }

    // ── B8.1a truncation metadata ──
    [Fact]
    public async Task Not_truncated_when_at_or_below_the_display_cap()
    {
        using var ctx = NewDb();
        var (r, _) = FinanceReq(ctx);
        for (var i = 0; i < 5; i++) ctx.RequestPayments.Add(ScheduledPay(r.Id, Guid.NewGuid(), Today.AddDays(-1 - i)));
        var d = await Build(ctx, Guid.NewGuid(), isFinance: true);

        Assert.Equal(5, d.Summary!.TotalAlertCount);
        Assert.Equal(5, d.Summary.DisplayedAlertCount);
        Assert.Equal(5, d.Alerts.Count);
        Assert.False(d.Summary.IsTruncated);
    }

    [Fact]
    public async Task Truncated_above_cap_but_counts_reflect_full_population()
    {
        using var ctx = NewDb();
        var (r, _) = FinanceReq(ctx);
        // 105 distinct groups, each one overdue scheduled payment (distinct dates) → 105 alerts.
        for (var i = 0; i < 105; i++) ctx.RequestPayments.Add(ScheduledPay(r.Id, Guid.NewGuid(), Today.AddDays(-1 - i)));
        var d = await Build(ctx, Guid.NewGuid(), isFinance: true);

        Assert.Equal(105, d.Summary!.TotalAlertCount);   // full population before the cap
        Assert.Equal(100, d.Summary.DisplayedAlertCount); // after the cap
        Assert.Equal(100, d.Alerts.Count);
        Assert.True(d.Summary.IsTruncated);
        // Summary counts remain the complete population.
        Assert.Equal(105, d.Summary.CriticalCount);
        Assert.Equal(0, d.Summary.AttentionCount);
        Assert.Equal(d.Summary.AttentionCount + d.Summary.CriticalCount, d.Summary.TotalAlertCount);
        // ByDomain reconciles to the total.
        Assert.Equal(d.Summary.TotalAlertCount, d.Summary.ByDomain.Sum(x => x.Attention + x.Critical));
        // Displayed = the canonical first 100 after sort (earliest date first → the 100 most overdue).
        Assert.True(d.Alerts.SequenceEqual(d.Alerts.OrderBy(a => a.DateUtc)));
        Assert.Equal(Today.AddDays(-105), d.Alerts.First().DateUtc); // most overdue kept
        Assert.Equal(Today.AddDays(-6), d.Alerts.Last().DateUtc);    // the 5 least-overdue dropped
    }

    [Fact]
    public async Task Alert_ids_are_unique_and_no_money_or_aging_types_present()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        ctx.Requests.Add(BuyerActionable(StWaitingQuotation, Today.AddDays(-1), me));
        var (r, g) = FinanceReq(ctx);
        ctx.RequestPayments.Add(ScheduledPay(r.Id, g, Today.AddDays(-1)));
        var d = await Build(ctx, me, isBuyer: true, isFinance: true);

        Assert.Equal(d.Alerts.Select(a => a.Id).Distinct().Count(), d.Alerts.Count);
        Assert.All(d.Alerts, a => Assert.DoesNotContain("APPROVAL", a.AlertType));
        Assert.All(d.Alerts, a => Assert.DoesNotContain("RECEIVING", a.AlertType));
        Assert.All(d.Alerts, a => Assert.DoesNotContain("PO_AGE", a.AlertType));
        // No monetary fields on the alert contract.
        var props = typeof(DashboardV2AlertDto).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(props, n => n.Contains("Amount") || n.Contains("Currency") || n.Contains("Exchange"));
    }
}
