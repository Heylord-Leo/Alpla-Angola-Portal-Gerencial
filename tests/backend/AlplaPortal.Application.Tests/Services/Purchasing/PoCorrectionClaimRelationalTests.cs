using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// docs §G.20 D2 — real SQL Server (LocalDB sandbox, IntegrationTestDatabase) verification that two concurrent claims
/// of the same unassigned PAYMENT correction group cannot both win: the group's rowversion arbitrates, exactly one
/// owner and one history row are persisted, the loser's attempt leaves nothing behind. Skips silently when the sandbox
/// is unreachable; every scenario seeds and removes its own request.
/// </summary>
public class PoCorrectionClaimRelationalTests
{
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid BuyerA, Guid BuyerB);

    private static async Task<T> GetOrAdd<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, Func<T> create) where T : class
    {
        var existing = await set.FirstOrDefaultAsync(match);
        if (existing != null) return existing;
        var e = create(); set.Add(e); return e;
    }

    private static async Task<Seed> SeedAsync()
    {
        await using var ctx = NewCtx();
        var tag = "ZZCLAIM_" + Guid.NewGuid().ToString("N")[..8];
        var type = await GetOrAdd(ctx.RequestTypes, t => t.Code == RequestConstants.Types.Payment, () => new RequestType { Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        var returned = await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.WaitingPoCorrection, () => new RequestStatus { Code = RequestConstants.Statuses.WaitingPoCorrection, Name = "Devolvido para Compras" });
        await ctx.SaveChangesAsync();
        var a = new User { Id = Guid.NewGuid(), FullName = tag + " A", Email = tag.ToLower() + ".a@test.local", IsActive = true };
        var b = new User { Id = Guid.NewGuid(), FullName = tag + " B", Email = tag.ToLower() + ".b@test.local", IsActive = true };
        ctx.Users.AddRange(a, b);
        await ctx.SaveChangesAsync();
        var request = new Request { Id = Guid.NewGuid(), RequestNumber = tag, Title = tag, StatusId = returned.Id, RequestTypeId = type.Id, DepartmentId = 1, CompanyId = 1, RequesterId = a.Id, EstimatedTotalAmount = 10m, CreatedAtUtc = DateTime.UtcNow.AddDays(-10), UpdatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(request);
        var group = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = tag + " SUPPLIER", Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection, TotalAmount = 10m, CurrencyCode = "AOA", PurchaseOrderNumber = "PO-" + tag, PoResponsibleBuyerId = null, CreatedByUserId = a.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-5) };
        ctx.RequestPoGroups.Add(group);
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, group.Id, a.Id, b.Id);
    }

    private static async Task CleanupAsync(Seed s)
    {
        await using var ctx = NewCtx();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestStatusHistories WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestPoGroups WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Requests WHERE Id = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Id = {s.BuyerA} OR Id = {s.BuyerB}");
    }

    private static async Task<(Guid? Owner, int ClaimRows, Guid? RequestBuyer)> StateAsync(Seed s)
    {
        await using var ctx = NewCtx();
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupId);
        var r = await ctx.Requests.AsNoTracking().SingleAsync(x => x.Id == s.RequestId);
        return (g.PoResponsibleBuyerId, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == PoCorrectionClaimService.ClaimAction), r.BuyerId);
    }

    [Fact]
    public async Task Two_concurrent_claims_yield_exactly_one_owner_and_one_history_row()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            // Both claims load the same (unassigned) group version and rendezvous before either saves.
            using var barrier = new Barrier(2);
            async Task<(PoCorrectionClaimOutcome Outcome, Guid Claimant)> RunAsync(Guid claimant)
            {
                await using var ctx = NewCtx();
                var svc = new PoCorrectionClaimService(ctx) { BeforeSaveHookForTests = () => Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(30))) };
                var r = await svc.ClaimAsync(s.RequestId, null, claimant, claimant, null);
                return (r.Outcome, claimant);
            }
            var results = await Task.WhenAll(Task.Run(() => RunAsync(s.BuyerA)), Task.Run(() => RunAsync(s.BuyerB)));
            Assert.Equal(1, results.Count(r => r.Outcome == PoCorrectionClaimOutcome.Claimed));
            Assert.Equal(1, results.Count(r => r.Outcome == PoCorrectionClaimOutcome.ConcurrencyConflict));
            var winner = results.Single(r => r.Outcome == PoCorrectionClaimOutcome.Claimed).Claimant;
            var loser = results.Single(r => r.Outcome == PoCorrectionClaimOutcome.ConcurrencyConflict).Claimant;

            var st = await StateAsync(s);
            Assert.Equal(winner, st.Owner); Assert.Equal(1, st.ClaimRows); Assert.Null(st.RequestBuyer);

            // The loser, retrying after the race, is told the group is owned — the first owner stands.
            await using (var ctx = NewCtx())
            {
                var retry = await new PoCorrectionClaimService(ctx).ClaimAsync(s.RequestId, null, loser, loser, null);
                Assert.Equal(PoCorrectionClaimOutcome.OwnedByAnother, retry.Outcome);
                Assert.Equal(winner, retry.OwnerId);
            }
            Assert.Equal((winner, 1, (Guid?)null), await StateAsync(s));
        }
        finally { await CleanupAsync(s); }
    }

    [Fact]
    public async Task A_stale_claim_fails_when_the_group_changed_after_it_was_loaded()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            await using var ctx = NewCtx();
            var svc = new PoCorrectionClaimService(ctx)
            {
                BeforeSaveHookForTests = async () =>
                {
                    // Another writer takes the group between our load and our SaveChanges.
                    await using var other = NewCtx();
                    Assert.Equal(PoCorrectionClaimOutcome.Claimed, (await new PoCorrectionClaimService(other).ClaimAsync(s.RequestId, null, s.BuyerB, s.BuyerB, null)).Outcome);
                }
            };
            var r = await svc.ClaimAsync(s.RequestId, null, s.BuyerA, s.BuyerA, null);
            Assert.Equal(PoCorrectionClaimOutcome.ConcurrencyConflict, r.Outcome);
            var st = await StateAsync(s);
            Assert.Equal(s.BuyerB, st.Owner); Assert.Equal(1, st.ClaimRows);
        }
        finally { await CleanupAsync(s); }
    }
}
