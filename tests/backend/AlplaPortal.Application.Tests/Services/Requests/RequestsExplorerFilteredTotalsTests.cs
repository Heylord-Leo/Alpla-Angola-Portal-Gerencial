using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Explorer "Total Filtrado" (2026-11-12): one total per currency over every request matching the active filters, all
/// pages, with the same amount / currency source rules as the rows. Pure merge rules in <see cref="RequestFilteredTotals"/>;
/// the endpoint wiring is exercised like <see cref="RequestsExplorerDisplayStateTests"/> (RequestsController against an EF
/// Core InMemory context with a SystemAdministrator principal).
/// </summary>
public class RequestsExplorerFilteredTotalsTests
{
    // ── Pure merge rules ──

    [Fact]
    public void Merge_OneCurrency_SumsIntoOneBucket()
    {
        var totals = RequestFilteredTotals.Merge(
            new[] { new RequestFilteredTotals.SingleCurrencyRow { Currency = "AOA", Amount = 100000m } },
            Array.Empty<Request>());
        var t = Assert.Single(totals);
        Assert.Equal("AOA", t.Currency);
        Assert.Equal(100000m, t.Amount);
    }

    [Fact]
    public void Merge_MultipleCurrencies_NeverSummedTogether_SortedByCurrency()
    {
        var totals = RequestFilteredTotals.Merge(
            new[] { new RequestFilteredTotals.SingleCurrencyRow { Currency = "RND", Amount = 28290m }, new RequestFilteredTotals.SingleCurrencyRow { Currency = "AOA", Amount = 100000m } },
            Array.Empty<Request>());
        Assert.Equal(new[] { "AOA", "RND" }, totals.Select(t => t.Currency));
        Assert.Equal(100000m, totals[0].Amount);
        Assert.Equal(28290m, totals[1].Amount);
    }

    [Fact]
    public void Merge_MultiCurrencyRequest_EachComponentOnce_CancelledGroupExcluded()
    {
        var request = new Request { Id = Guid.NewGuid(), ApprovedTotalAmount = 300m, ApprovedCurrencyCode = "MULTIPLE" };
        request.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, CurrencyCode = "AOA", TotalAmount = 100m, Status = RequestConstants.PoGroupStatuses.WaitingPo });
        request.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, CurrencyCode = "USD", TotalAmount = 200m, Status = RequestConstants.PoGroupStatuses.PoIssued });
        request.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, CurrencyCode = "USD", TotalAmount = 999m, Status = RequestConstants.PoGroupStatuses.Cancelled });

        var totals = RequestFilteredTotals.Merge(
            new[] { new RequestFilteredTotals.SingleCurrencyRow { Currency = "AOA", Amount = 50m } },
            new[] { request });

        Assert.Equal(new[] { "AOA", "USD" }, totals.Select(t => t.Currency));
        Assert.Equal(150m, totals[0].Amount);   // 50 single-currency + the 100 AOA component (never the 300 scalar)
        Assert.Equal(200m, totals[1].Amount);   // cancelled 999 excluded
    }

    [Fact]
    public void Merge_NullCurrency_BucketedAsAoa_LikeTheRows()
    {
        var totals = RequestFilteredTotals.Merge(
            new[] { new RequestFilteredTotals.SingleCurrencyRow { Currency = null, Amount = 10m }, new RequestFilteredTotals.SingleCurrencyRow { Currency = "aoa", Amount = 5m } },
            Array.Empty<Request>());
        var t = Assert.Single(totals);
        Assert.Equal("AOA", t.Currency);
        Assert.Equal(15m, t.Amount);
    }

    [Fact]
    public void Merge_Empty_YieldsNoBuckets()
    {
        Assert.Empty(RequestFilteredTotals.Merge(Array.Empty<RequestFilteredTotals.SingleCurrencyRow>(), Array.Empty<Request>()));
    }

    // ── SQL Server translation (the InMemory provider evaluates GroupBy on the client, so it proves nothing here).
    // ToQueryString compiles the query with the SQL Server provider without opening a connection: an untranslatable
    // expression throws at this point. It does NOT prove SQL Server will execute the statement (error 130 of 2026-11-12
    // passed this check) — RequestsExplorerFilteredTotalsRelationalTests executes it on LocalDB. The shape assertion below
    // guards the regression statically: no subquery may remain inside the SUM. ──
    [Fact]
    public void GroupedSingleCurrencyQuery_TranslatesToSqlServer()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\NeverOpened;Database=NeverOpened;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var ctx = new ApplicationDbContext(options);

        var excluded = RequestFilteredTotals.ExcludedStatusCodes;
        var scope = ctx.Requests.Where(r => !excluded.Contains(r.Status!.Code));
        var sql = RequestFilteredTotals.GroupSingleCurrency(scope.Where(RequestFilteredTotals.IsMultiCurrency.Not())).ToQueryString();
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUM(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApprovedCurrencyCode", sql);
        // Error 130 guard: the aggregated expression must not contain a subquery (SELECT inside SUM(...)).
        var sumStart = sql.IndexOf("SUM(", StringComparison.OrdinalIgnoreCase);
        var sumBody = sql.Substring(sumStart, sql.IndexOf("FROM", sumStart, StringComparison.OrdinalIgnoreCase) - sumStart);
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(sumBody, @"\bSELECT\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            "Subquery inside the aggregate (SQL Server error 130):\n" + sql);

        var multiSql = scope.Where(RequestFilteredTotals.IsMultiCurrency).Select(r => r.Id).ToQueryString();
        Assert.Contains("MULTIPLE", multiSql);
    }

    // ── Endpoint wiring (GetRequests summary) ──

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new RequestsController(
            ctx,
            Mock.Of<IDocumentExtractionService>(),
            new AdminLogWriter(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            Mock.Of<INotificationService>(),
            Mock.Of<IWorkflowNotificationOrchestrator>(),
            Mock.Of<IPrimaveraRequestValidationService>(),
            Mock.Of<IGroupBuilderService>(),
            new RequestStatusSyncService(ctx, NullLogger<RequestStatusSyncService>.Instance),
            Mock.Of<IApprovalRoutingService>(),
            Mock.Of<ILineItemFactory>(),
            Mock.Of<IRequestLineItemSubmissionValidator>(),
            Mock.Of<IQuotationItemEligibilityService>(),
            Mock.Of<IBatchExtraItemDecisionService>(),
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Microsoft.Extensions.Options.Options.Create(new AlplaPortal.Domain.Configuration.PostPaymentCompletionOptions()));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.SystemAdministrator)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private sealed record Seed(User Actor, RequestType QuotationType, RequestStatus Approved, RequestStatus Cancelled, Currency Aoa);

    private static Seed SeedCommon(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Totals Tester", Email = $"{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);
        ctx.Departments.Add(new Department { Id = 1, Name = "ZZTEST Department" });
        ctx.Companies.Add(new Company { Id = 1, Name = "ZZTEST Company" });
        var quotationType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        ctx.RequestTypes.Add(quotationType);
        var approved = new RequestStatus { Id = 1, Code = "APPROVED", Name = "Aprovado", DisplayOrder = 20 };
        var cancelled = new RequestStatus { Id = 2, Code = "CANCELLED", Name = "Cancelado", DisplayOrder = 90 };
        ctx.RequestStatuses.AddRange(approved, cancelled);
        var aoa = new Currency { Id = 1, Code = "AOA", Symbol = "Kz" };
        ctx.Currencies.Add(aoa);
        return new Seed(actor, quotationType, approved, cancelled, aoa);
    }

    private static Request NewRequest(Seed s, string number, RequestStatus status, decimal estimate, int? currencyId = 1, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        RequestNumber = number,
        Title = "ZZTEST " + number,
        RequestTypeId = s.QuotationType.Id,
        StatusId = status.Id,
        RequesterId = s.Actor.Id,
        DepartmentId = 1,
        CompanyId = 1,
        CurrencyId = currencyId,
        EstimatedTotalAmount = estimate,
        CreatedAtUtc = createdAt ?? DateTime.UtcNow,
    };

    private static async Task<DashboardSummaryDto> SummaryOf(RequestsController controller, int page = 1, int pageSize = 20, string? search = null)
    {
        var result = await controller.GetRequests(search: search, sortBy: "createdatutc", isDescending: false, page: page, pageSize: pageSize);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<RequestListResponseDto>(ok.Value).Summary;
    }

    [Fact]
    public async Task GetRequests_OneCurrency_SingleBucket()
    {
        var ctx = NewContext();
        var s = SeedCommon(ctx);
        ctx.Requests.AddRange(NewRequest(s, "ZZTEST-TOT-1", s.Approved, 60000m), NewRequest(s, "ZZTEST-TOT-2", s.Approved, 40000m));
        await ctx.SaveChangesAsync();

        var summary = await SummaryOf(BuildController(ctx, s.Actor.Id));
        var bucket = Assert.Single(summary.FilteredTotalsByCurrency);
        Assert.Equal("AOA", bucket.Currency);
        Assert.Equal(100000m, bucket.Amount);
        Assert.Equal(new[] { "AOA" }, summary.FilteredCurrencyCodes);
        Assert.Equal(100000m, summary.FilteredTotal);
    }

    [Fact]
    public async Task GetRequests_MultipleCurrencies_ApprovedSnapshotCurrencyWins_OneBucketEach()
    {
        var ctx = NewContext();
        var s = SeedCommon(ctx);
        // REQ-647 shape: approved in RND (candidate-based approval, no selected quotation) while the request currency is AOA.
        var rnd = NewRequest(s, "ZZTEST-TOT-647", s.Approved, 0m);
        rnd.ApprovedTotalAmount = 11274.23m; rnd.ApprovedCurrencyCode = "RND";
        var rnd2 = NewRequest(s, "ZZTEST-TOT-684", s.Approved, 0m);
        rnd2.ApprovedTotalAmount = 15840m; rnd2.ApprovedCurrencyCode = "RND";
        ctx.Requests.AddRange(NewRequest(s, "ZZTEST-TOT-688", s.Approved, 100000m), rnd, rnd2);
        await ctx.SaveChangesAsync();

        var summary = await SummaryOf(BuildController(ctx, s.Actor.Id));
        Assert.Equal(new[] { "AOA", "RND" }, summary.FilteredTotalsByCurrency.Select(t => t.Currency));
        Assert.Equal(100000m, summary.FilteredTotalsByCurrency[0].Amount);
        Assert.Equal(27114.23m, summary.FilteredTotalsByCurrency[1].Amount);
        Assert.Equal(2, summary.FilteredCurrencyCodes.Count);
    }

    [Fact]
    public async Task GetRequests_RequestWithMultipleCurrencies_EachComponentOnce_ScalarIgnored_CancelledGroupExcluded()
    {
        var ctx = NewContext();
        var s = SeedCommon(ctx);
        var multi = NewRequest(s, "ZZTEST-TOT-MULTI", s.Approved, 0m);
        multi.ApprovedTotalAmount = 300m; multi.ApprovedCurrencyCode = "MULTIPLE";
        ctx.Requests.AddRange(multi, NewRequest(s, "ZZTEST-TOT-AOA", s.Approved, 50m));
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = multi.Id, SupplierNameSnapshot = "A", CurrencyCode = "AOA", TotalAmount = 100m, Status = RequestConstants.PoGroupStatuses.WaitingPo, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Actor.Id },
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = multi.Id, SupplierNameSnapshot = "B", CurrencyCode = "USD", TotalAmount = 200m, Status = RequestConstants.PoGroupStatuses.PoIssued, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Actor.Id },
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = multi.Id, SupplierNameSnapshot = "C", CurrencyCode = "USD", TotalAmount = 999m, Status = RequestConstants.PoGroupStatuses.Cancelled, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Actor.Id });
        await ctx.SaveChangesAsync();

        var summary = await SummaryOf(BuildController(ctx, s.Actor.Id));
        Assert.Equal(new[] { "AOA", "USD" }, summary.FilteredTotalsByCurrency.Select(t => t.Currency));
        Assert.Equal(150m, summary.FilteredTotalsByCurrency[0].Amount);
        Assert.Equal(200m, summary.FilteredTotalsByCurrency[1].Amount);
    }

    [Fact]
    public async Task GetRequests_Pagination_TotalsCoverEveryPage_AndAreIdenticalOnEachPage()
    {
        var ctx = NewContext();
        var s = SeedCommon(ctx);
        var t0 = DateTime.UtcNow;
        ctx.Requests.AddRange(
            NewRequest(s, "ZZTEST-TOT-P1", s.Approved, 10m, createdAt: t0),
            NewRequest(s, "ZZTEST-TOT-P2", s.Approved, 20m, createdAt: t0.AddMinutes(1)),
            NewRequest(s, "ZZTEST-TOT-P3", s.Approved, 30m, createdAt: t0.AddMinutes(2)));
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx, s.Actor.Id);

        var page1 = await SummaryOf(controller, page: 1, pageSize: 1);
        var page3 = await SummaryOf(controller, page: 3, pageSize: 1);
        Assert.Equal(60m, Assert.Single(page1.FilteredTotalsByCurrency).Amount);
        Assert.Equal(60m, Assert.Single(page3.FilteredTotalsByCurrency).Amount);
    }

    [Fact]
    public async Task GetRequests_CancelledRequest_ContributesNothing()
    {
        var ctx = NewContext();
        var s = SeedCommon(ctx);
        ctx.Requests.AddRange(NewRequest(s, "ZZTEST-TOT-OK", s.Approved, 70m), NewRequest(s, "ZZTEST-TOT-CANC", s.Cancelled, 1000m));
        await ctx.SaveChangesAsync();

        var summary = await SummaryOf(BuildController(ctx, s.Actor.Id));
        Assert.Equal(70m, Assert.Single(summary.FilteredTotalsByCurrency).Amount);
    }

    [Fact]
    public async Task GetRequests_EmptyResults_NoBuckets_ZeroScalar()
    {
        var ctx = NewContext();
        var s = SeedCommon(ctx);
        ctx.Requests.Add(NewRequest(s, "ZZTEST-TOT-ONLY", s.Approved, 70m));
        await ctx.SaveChangesAsync();

        var summary = await SummaryOf(BuildController(ctx, s.Actor.Id), search: "NOTHING-MATCHES-THIS");
        Assert.Empty(summary.FilteredTotalsByCurrency);
        Assert.Empty(summary.FilteredCurrencyCodes);
        Assert.Equal(0m, summary.FilteredTotal);
    }
}
