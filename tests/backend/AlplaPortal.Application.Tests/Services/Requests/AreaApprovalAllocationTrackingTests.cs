using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Covers the allocation-replacement pattern used by area approval
/// (RequestsController.ProcessAreaApproval "Multi-Allocation Propagation" block and
/// ApprovalBatchController): RemoveRange(existing) + navigation Clear + explicit
/// DbSet.Add(new) + navigation Add.
///
/// Root cause guarded here (PAYMENT area-approval 500): RequestLineItemAllocation has a
/// CLIENT-SET Guid PK. A new allocation reached ONLY through the navigation collection is
/// tracked as Modified (EF assumes an existing row), producing an UPDATE that affects 0
/// rows → DbUpdateConcurrencyException. The explicit DbSet.Add forces Added → INSERT.
///
/// SQL Server (LocalDB) is used because the InMemory provider does not reproduce the
/// rowcount-based concurrency failure. Tests are skipped when LocalDB is unavailable and
/// clean up after themselves inside a rolled-back state (unique ids + explicit deletes).
/// </summary>
public class AreaApprovalAllocationTrackingTests
{
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();

    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    /// <summary>Seeds a minimal PAYMENT-like request with N line items (no allocations). Returns ids.</summary>
    private static async Task<(Guid RequestId, List<Guid> ItemIds, Guid Actor)> SeedRequestAsync(ApplicationDbContext ctx, int items)
    {
        var actor = await ctx.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
        if (actor == Guid.Empty) return (Guid.Empty, new List<Guid>(), Guid.Empty);

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_AREA_APPROVAL").Select(s => s.Id).FirstAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "PAYMENT").Select(t => t.Id).FirstAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST_ALLOC_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-" + Guid.NewGuid().ToString("N")[..10],
            StatusId = statusId,
            RequestTypeId = typeId,
            DepartmentId = 4,
            CompanyId = 1,
            PlantId = 1,
            CurrencyId = 1,
            RequesterId = actor,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var itemIds = new List<Guid>();
        for (int i = 1; i <= items; i++)
        {
            var li = new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                LineNumber = i,
                Description = $"ZZTEST item {i}",
                Quantity = 1,
                UnitPrice = 10,
                TotalAmount = 10,
                PlantId = 1,
                IsDeleted = false,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actor
            };
            ctx.RequestLineItems.Add(li);
            itemIds.Add(li.Id);
        }
        await ctx.SaveChangesAsync();
        return (request.Id, itemIds, actor);
    }

    private static async Task CleanupAsync(Guid requestId)
    {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE a FROM RequestLineItemAllocations a INNER JOIN RequestLineItems li ON li.Id=a.RequestLineItemId WHERE li.RequestId={0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId={0};" +
            "DELETE FROM RequestLineItems WHERE RequestId={0};" +
            "DELETE FROM Requests WHERE Id={0};", requestId);
    }

    /// <summary>
    /// Executes the EXACT corrected replacement pattern from ProcessAreaApproval against a
    /// tracked item and returns the tracked allocation entries' states before SaveChanges.
    /// </summary>
    private static (List<EntityState> NewStates, List<EntityState> OldStates) ReplaceAllocations(
        ApplicationDbContext ctx, RequestLineItem item, List<RequestLineItemAllocation> allocsToSave)
    {
        var oldAllocs = item.Allocations?.ToList() ?? new List<RequestLineItemAllocation>();

        if (item.Allocations == null) item.Allocations = new List<RequestLineItemAllocation>();
        ctx.RequestLineItemAllocations.RemoveRange(item.Allocations);
        item.Allocations.Clear();

        foreach (var a in allocsToSave)
        {
            ctx.RequestLineItemAllocations.Add(a); // the fix under test
            item.Allocations.Add(a);
        }

        var newStates = allocsToSave.Select(a => ctx.Entry(a).State).ToList();
        var oldStates = oldAllocs.Select(a => ctx.Entry(a).State).ToList();
        return (newStates, oldStates);
    }

    private static RequestLineItemAllocation NewAlloc(Guid itemId, Guid actor, int order = 0, decimal pct = 100m) => new()
    {
        Id = Guid.NewGuid(),
        RequestLineItemId = itemId,
        PlantId = 1,
        CostCenterId = 1,
        Percentage = pct,
        AllocationOrder = order,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedByUserId = actor
    };

    [Fact] // Happy path (PAYMENT): 4 items, no prior allocations → 4 Added → INSERTs persist, no concurrency exception
    public async Task Approve_NoPriorAllocations_NewAllocationsAreAdded_AndPersist()
    {
        if (!CanConnect()) return;
        var (requestId, itemIds, actor) = await SeedRequestAsync(new ApplicationDbContext(Options()), items: 4);
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var request = await ctx.Requests
                .Include(r => r.LineItems).ThenInclude(li => li.Allocations)
                .FirstAsync(r => r.Id == requestId);

            foreach (var item in request.LineItems.Where(l => !l.IsDeleted))
            {
                var (newStates, _) = ReplaceAllocations(ctx, item, new List<RequestLineItemAllocation> { NewAlloc(item.Id, actor) });
                Assert.All(newStates, s => Assert.Equal(EntityState.Added, s)); // tracked as Added BEFORE SaveChanges
            }

            await ctx.SaveChangesAsync(); // must NOT throw DbUpdateConcurrencyException

            await using var verify = new ApplicationDbContext(Options());
            var count = await verify.RequestLineItemAllocations
                .CountAsync(a => itemIds.Contains(a.RequestLineItemId));
            Assert.Equal(4, count); // INSERTs persisted
        }
        finally { await CleanupAsync(requestId); }
    }

    [Fact] // Regression guard: WITHOUT the explicit DbSet.Add, the navigation-only pattern is tracked as Modified and fails
    public async Task NavigationOnlyAdd_IsTrackedAsModified_AndFailsWithConcurrencyException()
    {
        if (!CanConnect()) return;
        var (requestId, itemIds, actor) = await SeedRequestAsync(new ApplicationDbContext(Options()), items: 1);
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var request = await ctx.Requests
                .Include(r => r.LineItems).ThenInclude(li => li.Allocations)
                .FirstAsync(r => r.Id == requestId);
            var item = request.LineItems.First();

            var alloc = NewAlloc(item.Id, actor);
            item.Allocations!.Add(alloc); // the OLD buggy pattern: navigation only, no DbSet.Add

            ctx.ChangeTracker.DetectChanges();
            Assert.Equal(EntityState.Modified, ctx.Entry(alloc).State); // documents the root cause

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx.SaveChangesAsync());
        }
        finally { await CleanupAsync(requestId); }
    }

    [Fact] // Replacement with PRE-EXISTING allocations: old → Deleted (removed), new → Added (inserted), disjoint ids
    public async Task Approve_WithExistingAllocations_OldDeleted_NewAdded_NoIdOverlap()
    {
        if (!CanConnect()) return;
        var (requestId, itemIds, actor) = await SeedRequestAsync(new ApplicationDbContext(Options()), items: 1);
        if (requestId == Guid.Empty) return;
        try
        {
            // Seed a pre-existing allocation (simulates the backfill migration / a prior approval)
            Guid oldAllocId;
            await using (var seed = new ApplicationDbContext(Options()))
            {
                var pre = NewAlloc(itemIds[0], actor);
                seed.RequestLineItemAllocations.Add(pre);
                await seed.SaveChangesAsync();
                oldAllocId = pre.Id;
            }

            await using var ctx = new ApplicationDbContext(Options());
            var request = await ctx.Requests
                .Include(r => r.LineItems).ThenInclude(li => li.Allocations)
                .FirstAsync(r => r.Id == requestId);
            var item = request.LineItems.First();
            Assert.Single(item.Allocations!); // pre-existing loaded

            var newAllocs = new List<RequestLineItemAllocation>
            {
                NewAlloc(item.Id, actor, order: 0, pct: 60m),
                NewAlloc(item.Id, actor, order: 1, pct: 40m)
            };
            var (newStates, oldStates) = ReplaceAllocations(ctx, item, newAllocs);

            Assert.All(oldStates, s => Assert.Equal(EntityState.Deleted, s));  // old marked Deleted
            Assert.All(newStates, s => Assert.Equal(EntityState.Added, s));    // new marked Added
            Assert.DoesNotContain(oldAllocId, newAllocs.Select(a => a.Id));    // never same id Deleted+Added

            await ctx.SaveChangesAsync();

            await using var verify = new ApplicationDbContext(Options());
            var remaining = await verify.RequestLineItemAllocations
                .Where(a => a.RequestLineItemId == itemIds[0])
                .OrderBy(a => a.AllocationOrder)
                .ToListAsync();
            Assert.Equal(2, remaining.Count);                                   // old removed, new inserted
            Assert.DoesNotContain(remaining, a => a.Id == oldAllocId);
            Assert.Equal(new[] { 0, 1 }, remaining.Select(a => a.AllocationOrder).ToArray()); // order preserved
            Assert.Equal(new[] { 60m, 40m }, remaining.Select(a => a.Percentage).ToArray());  // values intact
        }
        finally { await CleanupAsync(requestId); }
    }
}
