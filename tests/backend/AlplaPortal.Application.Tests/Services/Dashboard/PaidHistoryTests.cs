using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using AlplaPortal.Infrastructure.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B7.3 — Paid History: confirmed payment evidence in the last-30-day window, by payment currency. This is
/// a flat RequestPayments query (no finance-obligation projection), so it runs under EF in-memory.
/// </summary>
public class PaidHistoryTests
{
    private const int TypeQuotation = 1, StWaitingQuotation = 11;

    private static ApplicationDbContext NewDb()
    {
        var ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        ctx.RequestTypes.Add(new RequestType { Id = TypeQuotation, Code = RequestConstants.Types.Quotation, Name = "Q" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = StWaitingQuotation, Code = RequestConstants.Statuses.WaitingQuotation, Name = "AgCot" });
        ctx.SaveChanges();
        return ctx;
    }

    private static Request NewRequest() => new()
    {
        Id = Guid.NewGuid(), Title = "t", RequestNumber = "R-" + Guid.NewGuid().ToString("N")[..6],
        StatusId = StWaitingQuotation, RequestTypeId = TypeQuotation, RequesterId = Guid.NewGuid(),
        DepartmentId = 5, CompanyId = 1, PlantId = 1, CurrencyId = 1, CreatedAtUtc = DateTime.UtcNow,
    };

    private static int _seq;
    private static RequestPayment Pay(Guid reqId, string type, string status, decimal? amt, string? cur, DateTime? paidDate) => new()
    {
        RequestId = reqId, PaymentType = type, PaymentStatus = status, PlannedAmount = amt ?? 0m,
        ActualPaidAmount = amt, CurrencyCode = cur ?? string.Empty, PaidDateUtc = paidDate,
        PaymentSequence = ++_seq, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = Guid.NewGuid(),
    };

    private static async Task<PaidHistoryDto> Build(ApplicationDbContext ctx, IQueryable<Request>? scoped = null, DateTime? today = null)
    {
        await ctx.SaveChangesAsync();
        var p = new FinancialSummaryProjection(ctx, new FinancePaymentEligibilityService());
        return await p.BuildPaidHistoryAsync(scoped ?? ctx.Requests, today ?? DateTime.UtcNow.Date, null);
    }

    [Fact]
    public async Task Default_period_is_last_30_days_half_open()
    {
        using var ctx = NewDb();
        var today = new DateTime(2026, 6, 15);
        var h = await Build(ctx, today: today);
        Assert.Equal(FinancialPeriods.Last30Days, h.PeriodCode);
        Assert.Equal("Últimos 30 dias", h.PeriodLabel);
        Assert.Equal(today.AddDays(-29), h.FromUtc);
        Assert.Equal(today.AddDays(1), h.ToUtc);
    }

    [Fact]
    public async Task Sums_completed_owed_types_by_currency_excluding_refund_scheduled_and_out_of_window()
    {
        using var ctx = NewDb();
        var today = new DateTime(2026, 6, 15);
        var r = NewRequest();
        ctx.Requests.Add(r);
        ctx.RequestPayments.AddRange(
            Pay(r.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, 3_000_000m, "AOA", today.AddDays(-10)),
            Pay(r.Id, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Completed, 7_000_000m, "AOA", today.AddDays(-5)),
            Pay(r.Id, RequestPayment.PaymentTypes.Regularization, RequestPayment.PaymentStatuses.Completed, 5_000m, "EUR", today),
            Pay(r.Id, RequestPayment.PaymentTypes.Refund, RequestPayment.PaymentStatuses.Completed, 2_000_000m, "AOA", today.AddDays(-1)),        // refund excluded
            Pay(r.Id, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Scheduled, 99_000_000m, "AOA", today.AddDays(-1)), // not completed
            Pay(r.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, 1_000m, "AOA", today.AddDays(-40)));         // out of window

        var h = await Build(ctx, today: today);

        Assert.Equal(10_000_000m, h.Currencies.Single(c => c.CurrencyCode == "AOA").Amount);
        Assert.Equal(5_000m, h.Currencies.Single(c => c.CurrencyCode == "EUR").Amount);
        Assert.DoesNotContain(h.Currencies, c => c.Amount == 10_005_000m); // never combined
        Assert.Equal(3, h.PaymentCount);      // 2 AOA + 1 EUR (owed, completed, in-window)
        Assert.Equal(1, h.RequestCount);
        Assert.True(h.IsAuthoritative);
    }

    [Fact]
    public async Task Boundary_start_included_end_excluded()
    {
        using var ctx = NewDb();
        var today = new DateTime(2026, 6, 15);
        var r = NewRequest();
        ctx.Requests.Add(r);
        ctx.RequestPayments.AddRange(
            Pay(r.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, 100m, "AOA", today.AddDays(-29)), // == FromUtc → included
            Pay(r.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, 999m, "AOA", today.AddDays(-30))); // < FromUtc → excluded

        var h = await Build(ctx, today: today);
        Assert.Equal(100m, h.Currencies.Single().Amount);
        Assert.Equal(1, h.PaymentCount);
    }

    [Fact]
    public async Task Null_paid_amount_is_counted_but_not_valued_and_flags_not_authoritative()
    {
        using var ctx = NewDb();
        var today = new DateTime(2026, 6, 15);
        var r = NewRequest();
        ctx.Requests.Add(r);
        ctx.RequestPayments.AddRange(
            Pay(r.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, 500m, "AOA", today.AddDays(-2)),
            Pay(r.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, null, "AOA", today.AddDays(-3)));

        var h = await Build(ctx, today: today);
        var aoa = h.Currencies.Single();
        Assert.Equal(500m, aoa.Amount);   // null contributes no fabricated value
        Assert.Equal(2, aoa.EntityCount); // both payments counted
        Assert.False(h.IsAuthoritative);
    }

    [Fact]
    public async Task Null_currency_isolated_as_unknown()
    {
        using var ctx = NewDb();
        var today = new DateTime(2026, 6, 15);
        var r = NewRequest();
        ctx.Requests.Add(r);
        ctx.RequestPayments.Add(Pay(r.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, 500m, null, today.AddDays(-2)));

        var h = await Build(ctx, today: today);
        Assert.Equal(FinancialCurrency.Unknown, h.Currencies.Single().CurrencyCode);
        Assert.Equal(500m, h.Currencies.Single().Amount);
    }

    [Fact]
    public async Task Scope_excludes_payments_of_out_of_scope_requests()
    {
        using var ctx = NewDb();
        var today = new DateTime(2026, 6, 15);
        var inScope = NewRequest();
        var outScope = NewRequest();
        ctx.Requests.AddRange(inScope, outScope);
        ctx.RequestPayments.AddRange(
            Pay(inScope.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, 100m, "AOA", today.AddDays(-2)),
            Pay(outScope.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, 999m, "AOA", today.AddDays(-2)));

        var scoped = ctx.Requests.Where(r => r.Id == inScope.Id);
        var h = await Build(ctx, scoped, today);
        Assert.Equal(100m, h.Currencies.Single().Amount); // out-of-scope 999 excluded
        Assert.Equal(1, h.PaymentCount);
    }

    [Fact]
    public async Task Empty_period_yields_no_currency_rows()
    {
        using var ctx = NewDb();
        var h = await Build(ctx);
        Assert.Empty(h.Currencies);
        Assert.Equal(0, h.PaymentCount);
        Assert.Equal(0, h.RequestCount);
        Assert.True(h.IsAuthoritative);
    }
}
