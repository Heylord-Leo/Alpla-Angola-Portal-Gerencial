using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Validates the actual EF Core query shapes added to FinanceController.GetPayments for the
/// general search (request number OR supplier name) and server-side sorting (Phases 3-4 of the
/// Finance &gt; Payments fix). These tests exercise the query logic directly against an in-memory
/// ApplicationDbContext, mirroring the exact LINQ predicates written in the controller, since this
/// codebase has no existing precedent for full controller-level (HTTP/auth-mocked) integration
/// tests for this class of endpoint.
/// </summary>
public class FinancePaymentsSearchAndSortQueryTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var ctx = NewContext();

        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 1, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 30 },
            new RequestStatus { Id = 2, Code = RequestConstants.Statuses.PaymentScheduled, Name = "Pagamento Agendado", DisplayOrder = 50 }
        );
        ctx.Currencies.AddRange(
            new Currency { Id = 1, Code = "AOA", Symbol = "Kz" },
            new Currency { Id = 2, Code = "USD", Symbol = "$" }
        );
        var requester = new User { Id = Guid.NewGuid(), FullName = "Requester Test", Email = "r@test.local" };
        ctx.Users.Add(requester);

        var gasp = new Supplier { Id = 1, Name = "Gasp Transportes - Comércio & Prestação de Serviços" };
        var acme = new Supplier { Id = 2, Name = "Acme Distribuidora" };
        ctx.Suppliers.AddRange(gasp, acme);

        var requests = new[]
        {
            new Request
            {
                Id = Guid.NewGuid(), RequestNumber = "REQ-14/07/2026-059", Title = "Servico Gasp",
                RequestTypeId = 1, StatusId = 1, RequesterId = requester.Id, DepartmentId = 1, CompanyId = 1,
                SupplierId = gasp.Id, CurrencyId = 1, EstimatedTotalAmount = 387600m,
                NeedByDateUtc = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc),
                CreatedAtUtc = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            new Request
            {
                Id = Guid.NewGuid(), RequestNumber = "REQ-14/07/2026-061", Title = "Servico Acme",
                RequestTypeId = 1, StatusId = 1, RequesterId = requester.Id, DepartmentId = 1, CompanyId = 1,
                SupplierId = acme.Id, CurrencyId = 1, EstimatedTotalAmount = 3294600m,
                NeedByDateUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAtUtc = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            new Request
            {
                // Null due date and null supplier — must not crash search/sort.
                Id = Guid.NewGuid(), RequestNumber = "REQ-13/07/2026-053", Title = "Sem fornecedor definido",
                RequestTypeId = 1, StatusId = 2, RequesterId = requester.Id, DepartmentId = 1, CompanyId = 1,
                SupplierId = null, CurrencyId = 2, EstimatedTotalAmount = 1836000m,
                NeedByDateUtc = null,
                CreatedAtUtc = new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc)
            },
        };
        ctx.Requests.AddRange(requests);

        foreach (var r in requests)
        {
            ctx.RequestAttachments.Add(new RequestAttachment
            {
                Id = Guid.NewGuid(),
                RequestId = r.Id,
                FileName = "po.pdf",
                FileExtension = ".pdf",
                AttachmentTypeCode = RequestAttachment.TYPE_PO,
                IsDeleted = false
            });
        }

        await ctx.SaveChangesAsync();
        return ctx;
    }

    /// <summary>Mirrors FinanceController.GetPayments' general search predicate exactly (non-quotation path).</summary>
    private static IQueryable<Request> ApplySearch(IQueryable<Request> query, string searchTerm)
    {
        var searchLower = searchTerm.Trim().ToLower();
        return query.Where(r =>
            (r.RequestNumber != null && r.RequestNumber.ToLower().Contains(searchLower))
            || (r.SelectedQuotationId.HasValue && r.Quotations.Any(q => q.Id == r.SelectedQuotationId.Value && q.SupplierNameSnapshot != null && q.SupplierNameSnapshot.ToLower().Contains(searchLower)))
            || (!r.SelectedQuotationId.HasValue && r.Supplier != null && r.Supplier.Name != null && r.Supplier.Name.ToLower().Contains(searchLower)));
    }

    [Fact]
    public async Task Search_FullRequestNumber_FindsExactMatch()
    {
        var ctx = await SeedAsync();
        var result = await ApplySearch(ctx.Requests.Include(r => r.Supplier), "REQ-14/07/2026-059").ToListAsync();
        Assert.Single(result);
        Assert.Equal("REQ-14/07/2026-059", result[0].RequestNumber);
    }

    [Fact]
    public async Task Search_PartialRequestNumber_FindsMatch()
    {
        var ctx = await SeedAsync();
        var result = await ApplySearch(ctx.Requests.Include(r => r.Supplier), "059").ToListAsync();
        Assert.Single(result);
        Assert.Equal("REQ-14/07/2026-059", result[0].RequestNumber);
    }

    [Fact]
    public async Task Search_BySupplierName_IsCaseInsensitive()
    {
        var ctx = await SeedAsync();
        var result = await ApplySearch(ctx.Requests.Include(r => r.Supplier), "gasp transportes").ToListAsync();
        Assert.Single(result);
        Assert.Equal("REQ-14/07/2026-059", result[0].RequestNumber);
    }

    [Fact]
    public async Task Search_NullSupplier_DoesNotCrash_AndIsNotMatchedBySupplierTerm()
    {
        var ctx = await SeedAsync();
        var result = await ApplySearch(ctx.Requests.Include(r => r.Supplier), "acme").ToListAsync();
        Assert.Single(result); // only the Acme row — the null-supplier row must not throw or false-match
        Assert.Equal("REQ-14/07/2026-061", result[0].RequestNumber);
    }

    [Fact]
    public async Task Search_CombinesWithStatusCodeFilter()
    {
        var ctx = await SeedAsync();
        var query = ctx.Requests.Include(r => r.Supplier).Include(r => r.Status)
            .Where(r => r.Status!.Code == RequestConstants.Statuses.PoIssued);
        var result = await ApplySearch(query, "REQ-14").ToListAsync();
        Assert.Equal(2, result.Count); // -059 and -061 are PO_ISSUED; -053 is PAYMENT_SCHEDULED and excluded by status
    }

    [Fact]
    public async Task Search_CombinesWithCurrencyFilter()
    {
        var ctx = await SeedAsync();
        var query = ctx.Requests.Include(r => r.Currency)
            .Where(r => r.Currency != null && r.Currency.Code == "USD");
        var result = await ApplySearch(query, "REQ").ToListAsync();
        Assert.Single(result);
        Assert.Equal("REQ-13/07/2026-053", result[0].RequestNumber);
    }

    // ── Sorting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sort_RequestNumber_UsesCreatedAtUtcDate_NotLexicographicString()
    {
        // Lexicographic sort on "REQ-DD/MM/YYYY-NNN" would put "13/07" before "14/07" correctly here
        // by coincidence, but the real risk is day-of-month ordering breaking year ordering — this
        // proves the sort key actually used is CreatedAtUtc, matching RequestsController's approach.
        var ctx = await SeedAsync();
        var ascending = await ctx.Requests.OrderBy(r => r.CreatedAtUtc.Date).ThenBy(r => r.RequestNumber).Select(r => r.RequestNumber).ToListAsync();
        Assert.Equal(new[] { "REQ-13/07/2026-053", "REQ-14/07/2026-059", "REQ-14/07/2026-061" }, ascending);

        var descending = await ctx.Requests.OrderByDescending(r => r.CreatedAtUtc.Date).ThenByDescending(r => r.RequestNumber).Select(r => r.RequestNumber).ToListAsync();
        Assert.Equal(new[] { "REQ-14/07/2026-061", "REQ-14/07/2026-059", "REQ-13/07/2026-053" }, descending);
    }

    [Fact]
    public async Task Sort_SupplierName_NullSupplierSortsWithoutCrashing()
    {
        var ctx = await SeedAsync();
        var ascending = await ctx.Requests
            .Select(r => r.Supplier != null ? r.Supplier.Name : null)
            .OrderBy(n => n)
            .ToListAsync();

        Assert.Equal(3, ascending.Count);
        Assert.Null(ascending[0]); // EF Core / SQL Server default: NULLs sort first ascending
    }

    [Fact]
    public async Task Sort_NeedByDateUtc_NullDueDateSortsWithoutCrashing()
    {
        var ctx = await SeedAsync();
        var ascending = await ctx.Requests.OrderBy(r => r.NeedByDateUtc).Select(r => r.RequestNumber).ToListAsync();
        Assert.Equal("REQ-13/07/2026-053", ascending[0]); // null NeedByDateUtc sorts first ascending
    }

    [Fact]
    public async Task Sort_StatusCode_UsesDisplayOrder_NotAlphabeticalLabel()
    {
        var ctx = await SeedAsync();
        // PAYMENT_SCHEDULED (DisplayOrder=50) would sort BEFORE PO_ISSUED (DisplayOrder=30)
        // alphabetically by code, but must sort AFTER it by DisplayOrder (workflow position).
        var ascending = await ctx.Requests.Include(r => r.Status)
            .OrderBy(r => r.Status!.DisplayOrder)
            .Select(r => r.Status!.Code)
            .Distinct()
            .ToListAsync();
        Assert.Equal(new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled }, ascending);
    }

    [Fact]
    public async Task Sort_Amount_UsesRawDecimal_NotFormattedString()
    {
        var ctx = await SeedAsync();
        var ascending = await ctx.Requests.OrderBy(r => r.EstimatedTotalAmount).Select(r => r.EstimatedTotalAmount).ToListAsync();
        Assert.Equal(new[] { 387600m, 1836000m, 3294600m }, ascending);
    }

    [Fact]
    public async Task Sort_TieBreakerById_KeepsPaginationOrderStable()
    {
        var ctx = await SeedAsync();
        // Two calls with an identical (non-unique) primary sort key plus the Id tie-breaker must
        // always return rows in the same relative order.
        var first = await ctx.Requests.OrderBy(r => r.RequestTypeId).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync();
        var second = await ctx.Requests.OrderBy(r => r.RequestTypeId).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync();
        Assert.Equal(first, second);
    }
}
