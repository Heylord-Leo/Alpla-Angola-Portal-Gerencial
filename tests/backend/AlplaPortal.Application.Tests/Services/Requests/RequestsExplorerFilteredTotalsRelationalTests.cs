using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// DEV blocker 2026-11-12: the per-currency "Total Filtrado" query compiled (ToQueryString) but SQL Server refused to EXECUTE
/// it — error 130 "Cannot perform an aggregate function on an expression containing an aggregate or a subquery" — because the
/// SUM wrapped the correlated selected-quotation subquery. The InMemory provider and a compile-only check cannot catch that,
/// so this suite EXECUTES the grouped query against the LocalDB sandbox (Portal-Gerencial-IntegrationTests, never a real
/// database) with one request per branch: approved snapshot, selected quotation, estimate, multi-currency, cancelled.
/// Skips silently when LocalDB is unavailable (CI sets ALPLA_INTEGRATION_TESTS_REQUIRED=true to fail loudly instead).
/// </summary>
[Collection("IntegrationTests")]
public class RequestsExplorerFilteredTotalsRelationalTests
{
    private readonly ITestOutputHelper _out;
    public RequestsExplorerFilteredTotalsRelationalTests(ITestOutputHelper output) => _out = output;

    static RequestsExplorerFilteredTotalsRelationalTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (!ctx.Database.CanConnect()
                || ctx.Database.SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.__EFMigrationsHistory'), 0) AS [Value]").AsEnumerable().First() == 0)
                ctx.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RequestsExplorerFilteredTotalsRelationalTests] sandbox bootstrap: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private static async Task<T> GetOrAdd<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, Func<T> create) where T : class
    {
        var existing = await set.FirstOrDefaultAsync(match);
        if (existing != null) return existing;
        var e = create(); set.Add(e); return e;
    }

    private sealed record Seed(string Tag, Guid UserId, int CompanyId, int DepartmentId, List<Guid> RequestIds, Guid QuotationId);

    /// <summary>Five ZZTEST requests of one company: approved snapshot (RND 11 274,23 while the request currency is AOA),
    /// selected quotation (USD 500 quotation, estimate 999 ignored), plain estimate (AOA 100 000), MULTIPLE approval
    /// (AOA 100 + USD 200 live groups, USD 999 cancelled group, scalar 300 ignored), cancelled request (AOA 1 000 000 ignored).</summary>
    private static async Task<Seed> SeedAsync()
    {
        await using var ctx = NewCtx();
        var tag = "ZZTEST_" + Guid.NewGuid().ToString("N")[..8];
        var quotationType = await GetOrAdd(ctx.RequestTypes, t => t.Code == RequestConstants.Types.Quotation, () => new RequestType { Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        var approved = await GetOrAdd(ctx.RequestStatuses, s => s.Code == "APPROVED", () => new RequestStatus { Code = "APPROVED", Name = "Aprovado" });
        var cancelled = await GetOrAdd(ctx.RequestStatuses, s => s.Code == "CANCELLED", () => new RequestStatus { Code = "CANCELLED", Name = "Cancelado" });
        var aoa = await GetOrAdd(ctx.Currencies, c => c.Code == "AOA", () => new Currency { Code = "AOA", Symbol = "Kz" });
        await ctx.SaveChangesAsync();

        var user = new User { Id = Guid.NewGuid(), FullName = tag, Email = tag.ToLower() + "@test.local", IsActive = true };
        var company = new Company { Name = tag + " Co", IsActive = true };
        var department = new Department { Name = tag + " Dept" };
        ctx.Users.Add(user); ctx.Companies.Add(company); ctx.Departments.Add(department);
        await ctx.SaveChangesAsync();

        Request NewRequest(string suffix, RequestStatus status, decimal estimate) => new()
        {
            Id = Guid.NewGuid(), RequestNumber = $"{tag}-{suffix}", Title = $"{tag} {suffix}", StatusId = status.Id, RequestTypeId = quotationType.Id,
            DepartmentId = department.Id, CompanyId = company.Id, RequesterId = user.Id, CurrencyId = aoa.Id,
            EstimatedTotalAmount = estimate, CreatedAtUtc = DateTime.UtcNow, RequestedDateUtc = DateTime.UtcNow,
        };

        var snapshot = NewRequest("SNAP", approved, 0m);
        snapshot.ApprovedTotalAmount = 11274.23m; snapshot.ApprovedCurrencyCode = "RND";

        var quoted = NewRequest("QUOT", approved, 999m);
        var quotation = new Quotation { Id = Guid.NewGuid(), RequestId = quoted.Id, SupplierNameSnapshot = tag + " SUP", TotalAmount = 500m, Currency = "USD", CreatedAtUtc = DateTime.UtcNow };
        quoted.SelectedQuotationId = quotation.Id;

        var estimate = NewRequest("EST", approved, 100000m);

        var multi = NewRequest("MULTI", approved, 0m);
        multi.ApprovedTotalAmount = 300m; multi.ApprovedCurrencyCode = "MULTIPLE";

        var cancelledRequest = NewRequest("CANC", cancelled, 1000000m);

        ctx.Requests.AddRange(snapshot, quoted, estimate, multi, cancelledRequest);
        ctx.Set<Quotation>().Add(quotation);
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = multi.Id, SupplierNameSnapshot = "A", CurrencyCode = "AOA", TotalAmount = 100m, Status = RequestConstants.PoGroupStatuses.WaitingPo, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = user.Id },
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = multi.Id, SupplierNameSnapshot = "B", CurrencyCode = "USD", TotalAmount = 200m, Status = RequestConstants.PoGroupStatuses.PoIssued, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = user.Id },
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = multi.Id, SupplierNameSnapshot = "C", CurrencyCode = "USD", TotalAmount = 999m, Status = RequestConstants.PoGroupStatuses.Cancelled, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = user.Id });
        await ctx.SaveChangesAsync();

        return new Seed(tag, user.Id, company.Id, department.Id,
            new List<Guid> { snapshot.Id, quoted.Id, estimate.Id, multi.Id, cancelledRequest.Id }, quotation.Id);
    }

    private static async Task CleanupAsync(Seed s)
    {
        await using var ctx = NewCtx();
        foreach (var id in s.RequestIds)
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestPoGroups WHERE RequestId = {id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE Requests SET SelectedQuotationId = NULL WHERE Id = {id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Quotations WHERE RequestId = {id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Requests WHERE Id = {id}");
        }
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Departments WHERE Id = {s.DepartmentId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Companies WHERE Id = {s.CompanyId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Id = {s.UserId}");
    }

    [Fact]
    public async Task GroupedTotals_ExecuteOnSqlServer_EveryBranch_SeparateCurrencies()
    {
        if (!IntegrationTestDatabase.CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            await using var ctx = NewCtx();
            var excluded = RequestFilteredTotals.ExcludedStatusCodes;
            var scope = ctx.Requests.AsNoTracking()
                .Where(r => r.CompanyId == s.CompanyId)
                .Where(r => !excluded.Contains(r.Status!.Code));

            var grouped = RequestFilteredTotals.GroupSingleCurrency(scope.Where(RequestFilteredTotals.IsMultiCurrency.Not()));
            _out.WriteLine(grouped.ToQueryString());

            // The actual execution — this is what error 130 broke.
            var single = await grouped.ToListAsync();

            var multiIds = await scope.Where(RequestFilteredTotals.IsMultiCurrency).Select(r => r.Id).ToListAsync();
            var multi = await ctx.Requests.AsNoTracking().Where(r => multiIds.Contains(r.Id)).Include(r => r.PoGroups).Include(r => r.ApprovalBatches).ToListAsync();
            var totals = RequestFilteredTotals.Merge(single, multi);

            Assert.Equal(new[] { "AOA", "RND", "USD" }, totals.Select(t => t.Currency));
            Assert.Equal(100100m, totals.Single(t => t.Currency == "AOA").Amount);    // estimate 100 000 + MULTI component 100; cancelled 1 000 000 excluded
            Assert.Equal(11274.23m, totals.Single(t => t.Currency == "RND").Amount);  // approved snapshot, NOT the AOA request currency
            Assert.Equal(700m, totals.Single(t => t.Currency == "USD").Amount);       // quotation 500 (estimate 999 ignored) + MULTI 200 (cancelled 999 excluded)
        }
        finally
        {
            await CleanupAsync(s);
        }
    }
}
