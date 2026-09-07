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
/// B7.1 — Em aprovação (ApprovalBatch snapshot) and Aguardando P.O. (group TotalAmount) money aggregation.
/// These two categories are direct SelectMany projections (no finance obligation query), so they run under
/// EF in-memory. The finance/paid categories need the canonical B3 projection (LocalDB) — see
/// FinancialSummaryFinanceIntegrationTests. Requests here carry no finance-relevant groups, so those two
/// categories are legitimately empty.
/// </summary>
public class FinancialSummaryProjectionTests
{
    private const int TypeQuotation = 1;
    private const int StWaitingQuotation = 11;

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

    private static async Task<FinancialCategoryDto> Build(ApplicationDbContext ctx, string code)
    {
        await ctx.SaveChangesAsync();
        var projection = new FinancialSummaryProjection(ctx, new FinancePaymentEligibilityService());
        var categories = await projection.BuildAsync(ctx.Requests, DateTime.UtcNow.Date);
        return categories.Single(c => c.Code == code);
    }

    // ── EM_APROVACAO ──
    [Fact]
    public async Task Approval_multi_batch_same_request_sums_snapshot_by_currency()
    {
        using var ctx = NewDb();
        var r = NewRequest();
        r.ApprovalBatches.Add(new ApprovalBatch { Id = Guid.NewGuid(), RequestId = r.Id, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, ApprovedTotalAmount = 3_000_000m });
        r.ApprovalBatches.Add(new ApprovalBatch { Id = Guid.NewGuid(), RequestId = r.Id, BatchNumber = 2, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, ApprovedTotalAmount = 5_000_000m });
        ctx.Requests.Add(r);

        var cat = await Build(ctx, FinancialCategories.EmAprovacao);

        Assert.Equal(2, cat.EntityCount);   // 2 batches
        Assert.Equal(1, cat.RequestCount);  // 1 request
        var row = cat.Currencies.Single();  // no candidate items → UNKNOWN currency, snapshot amount
        Assert.Equal(FinancialCurrency.Unknown, row.CurrencyCode);
        Assert.Equal(8_000_000m, row.Amount);
    }

    [Fact]
    public async Task Approval_undecided_batch_with_no_snapshot_is_counted_but_not_valued()
    {
        using var ctx = NewDb();
        var r = NewRequest();
        // No ApprovedTotalAmount and no items → no authoritative amount (never fabricated).
        r.ApprovalBatches.Add(new ApprovalBatch { Id = Guid.NewGuid(), RequestId = r.Id, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, ApprovedTotalAmount = null });
        ctx.Requests.Add(r);

        var cat = await Build(ctx, FinancialCategories.EmAprovacao);

        Assert.Equal(1, cat.EntityCount);
        Assert.False(cat.IsAuthoritative);
        Assert.Empty(cat.Currencies); // no fabricated zero
    }

    // ── AGUARDANDO_PO ──
    [Fact]
    public async Task WaitingPo_separates_currencies_and_never_sums_them()
    {
        using var ctx = NewDb();
        var r = NewRequest();
        r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = r.Id, Status = RequestConstants.PoGroupStatuses.WaitingPo, TotalAmount = 10_000_000m, CurrencyCode = "AOA" });
        r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = r.Id, Status = RequestConstants.PoGroupStatuses.WaitingPo, TotalAmount = 5_000m, CurrencyCode = "EUR" });
        r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = r.Id, Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection, TotalAmount = 2_000m, CurrencyCode = "USD" });
        ctx.Requests.Add(r);

        var cat = await Build(ctx, FinancialCategories.AguardandoPo);

        Assert.Equal(3, cat.EntityCount);
        Assert.Equal(3, cat.Currencies.Count);
        Assert.Equal(10_000_000m, cat.Currencies.Single(c => c.CurrencyCode == "AOA").Amount);
        Assert.Equal(5_000m, cat.Currencies.Single(c => c.CurrencyCode == "EUR").Amount);
        Assert.Equal(2_000m, cat.Currencies.Single(c => c.CurrencyCode == "USD").Amount);
        Assert.DoesNotContain(cat.Currencies, c => c.Amount == 10_007_000m); // never combined
    }

    [Fact]
    public async Task WaitingPo_same_currency_sums_and_uses_group_grain()
    {
        using var ctx = NewDb();
        var r = NewRequest();
        r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = r.Id, Status = RequestConstants.PoGroupStatuses.WaitingPo, TotalAmount = 1_000m, CurrencyCode = "AOA" });
        r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = r.Id, Status = RequestConstants.PoGroupStatuses.WaitingPo, TotalAmount = 4_000m, CurrencyCode = "AOA" });
        ctx.Requests.Add(r);

        var cat = await Build(ctx, FinancialCategories.AguardandoPo);
        var aoa = cat.Currencies.Single();
        Assert.Equal(5_000m, aoa.Amount);
        Assert.Equal(2, aoa.EntityCount);   // 2 groups
        Assert.Equal(1, aoa.RequestCount);  // 1 request
        Assert.Equal(FinancialEntityTypes.PoGroup, cat.EntityType);
    }

    [Fact]
    public async Task WaitingPo_null_currency_falls_into_unknown_bucket()
    {
        using var ctx = NewDb();
        var r = NewRequest();
        r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = r.Id, Status = RequestConstants.PoGroupStatuses.WaitingPo, TotalAmount = 99m, CurrencyCode = null });
        ctx.Requests.Add(r);

        var cat = await Build(ctx, FinancialCategories.AguardandoPo);
        Assert.Equal(FinancialCurrency.Unknown, cat.Currencies.Single().CurrencyCode);
        Assert.Equal(99m, cat.Currencies.Single().Amount);
    }

    [Fact]
    public async Task WaitingPo_excludes_cancelled_groups()
    {
        using var ctx = NewDb();
        var r = NewRequest();
        r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = r.Id, Status = RequestConstants.PoGroupStatuses.WaitingPo, TotalAmount = 100m, CurrencyCode = "AOA" });
        r.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = r.Id, Status = RequestConstants.PoGroupStatuses.Cancelled, TotalAmount = 999m, CurrencyCode = "AOA" });
        ctx.Requests.Add(r);

        var cat = await Build(ctx, FinancialCategories.AguardandoPo);
        Assert.Equal(1, cat.EntityCount);
        Assert.Equal(100m, cat.Currencies.Single().Amount); // cancelled 999 excluded
    }

    [Fact]
    public async Task No_fx_or_localized_fields_on_the_contract()
    {
        // (B7.3 adds PaidHistory; that is allowed. FX/conversion/reporting-currency/formatting are not.)
        var catProps = typeof(FinancialCategoryDto).GetProperties().Select(p => p.Name).ToList();
        var curProps = typeof(CurrencyAmountDto).GetProperties().Select(p => p.Name).ToList();
        var rootProps = typeof(DashboardV2FinancialDto).GetProperties().Select(p => p.Name).ToList();
        var histProps = typeof(PaidHistoryDto).GetProperties().Select(p => p.Name).ToList();
        foreach (var props in new[] { catProps, curProps, rootProps, histProps })
            Assert.DoesNotContain(props, n => n.Contains("Exchange") || n.Contains("Converted") || n.Contains("Reporting") || n.Contains("Fx") || n.Contains("Formatted") || n.Contains("Display"));
        // Amounts are decimal, not double/float.
        Assert.Equal(typeof(decimal), typeof(CurrencyAmountDto).GetProperty(nameof(CurrencyAmountDto.Amount))!.PropertyType);
    }
}
