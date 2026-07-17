using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Data-access semantics for the from-proforma idempotency lookup. The production endpoint looks up
/// by (RequestId, CreationIdempotencyKey); these tests assert that predicate never crosses requests.
/// The real composite UNIQUE filtered index is verified separately on SQL Server (documented in the
/// migration checkpoint), since the EF in-memory provider does not enforce filtered unique indexes.
/// </summary>
public class RequestLineItemIdempotencyScopingTests
{
    private static ApplicationDbContext NewContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RequestLineItem Seed(Guid requestId, string key, string description = "item")
        => new()
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            LineNumber = 1,
            Description = description,
            ItemPriority = "MEDIUM",
            Quantity = 1,
            UnitPrice = 0,
            TotalAmount = 0,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreationIdempotencyKey = key,
            CreationOrigin = "BUYER_RECONCILIATION"
        };

    // Mirrors the endpoint's scoped lookup exactly.
    private static Task<RequestLineItem?> ScopedLookup(ApplicationDbContext ctx, Guid requestId, string key)
        => ctx.RequestLineItems.FirstOrDefaultAsync(li => li.RequestId == requestId && li.CreationIdempotencyKey == key);

    [Fact]
    public async Task SameKey_SameRequest_ReturnsExisting()
    {
        var reqA = Guid.NewGuid();
        using var ctx = NewContext();
        var seeded = Seed(reqA, "KEY-1");
        ctx.RequestLineItems.Add(seeded);
        await ctx.SaveChangesAsync();

        var found = await ScopedLookup(ctx, reqA, "KEY-1");

        Assert.NotNull(found);
        Assert.Equal(seeded.Id, found!.Id);
    }

    [Fact]
    public async Task SameKey_DifferentRequest_DoesNotCrossReturn()
    {
        var reqA = Guid.NewGuid();
        var reqB = Guid.NewGuid();
        using var ctx = NewContext();
        ctx.RequestLineItems.Add(Seed(reqA, "SHARED-KEY"));
        await ctx.SaveChangesAsync();

        // Request B never created an item with this key → scoped lookup must NOT return A's item.
        var found = await ScopedLookup(ctx, reqB, "SHARED-KEY");

        Assert.Null(found);
    }

    [Fact]
    public async Task SameKey_TwoRequests_Coexist_AndScopedLookupReturnsOwn()
    {
        var reqA = Guid.NewGuid();
        var reqB = Guid.NewGuid();
        using var ctx = NewContext();
        var a = Seed(reqA, "SHARED-KEY", "item A");
        var b = Seed(reqB, "SHARED-KEY", "item B");
        ctx.RequestLineItems.AddRange(a, b);
        await ctx.SaveChangesAsync(); // both coexist — different requests, same key is allowed

        Assert.Equal(2, ctx.RequestLineItems.Count(li => li.CreationIdempotencyKey == "SHARED-KEY"));

        var foundA = await ScopedLookup(ctx, reqA, "SHARED-KEY");
        var foundB = await ScopedLookup(ctx, reqB, "SHARED-KEY");

        Assert.Equal(a.Id, foundA!.Id);
        Assert.Equal(b.Id, foundB!.Id);
        Assert.NotEqual(foundA.Id, foundB.Id);
    }

    [Fact]
    public async Task Retry_SameRequest_FindsExisting_SoNoSecondInsertHappens()
    {
        var reqA = Guid.NewGuid();
        using var ctx = NewContext();
        ctx.RequestLineItems.Add(Seed(reqA, "RETRY-KEY"));
        await ctx.SaveChangesAsync();

        // On retry the endpoint short-circuits when the scoped lookup already finds the item.
        var found = await ScopedLookup(ctx, reqA, "RETRY-KEY");
        Assert.NotNull(found);
        Assert.Equal(1, ctx.RequestLineItems.Count(li => li.RequestId == reqA && li.CreationIdempotencyKey == "RETRY-KEY"));
    }
}
