using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Repairs;

/// <summary>
/// Real SQL Server (LocalDB sandbox, IntegrationTestDatabase) verification of the legacy Finance-return repair with the
/// real model, transactions and the group RowVersion. Skips silently when the sandbox is unreachable. Every scenario
/// seeds its own request and removes it afterwards. Races are made deterministic with the service's
/// BeforeApplySaveHookForTests synchronization point.
/// </summary>
// Test isolation: every class that touches the shared LocalDB sandbox runs in the serialized "IntegrationTests"
// collection (drift-aware rebuilds and lock-race tests of other classes must never overlap with this one).
[Collection("IntegrationTests")]
public class LegacyFinanceReturnRepairRelationalTests
{
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private sealed record Seed(Guid RequestId, Guid GroupId, int ScalarStatusId, Guid UserId);

    private static async Task<T> GetOrAdd<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, Func<T> create) where T : class
    {
        var existing = await set.FirstOrDefaultAsync(match);
        if (existing != null) return existing;
        var e = create(); set.Add(e); return e;
    }

    private static async Task<Seed> SeedAsync()
    {
        await using var ctx = NewCtx();
        var tag = "ZZTEST_" + Guid.NewGuid().ToString("N")[..8];
        var type = await GetOrAdd(ctx.RequestTypes, t => t.Code == RequestConstants.Types.Payment, () => new RequestType { Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        var returned = await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.WaitingPoCorrection, () => new RequestStatus { Code = RequestConstants.Statuses.WaitingPoCorrection, Name = "Devolvido" });
        var issued = await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.PoIssued, () => new RequestStatus { Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" });
        await ctx.SaveChangesAsync();
        var user = new User { Id = Guid.NewGuid(), FullName = tag, Email = tag.ToLower() + "@test.local", IsActive = true };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var request = new Request { Id = Guid.NewGuid(), RequestNumber = tag, Title = tag, StatusId = returned.Id, RequestTypeId = type.Id, DepartmentId = 1, CompanyId = 1, RequesterId = user.Id, EstimatedTotalAmount = 1097000m, ApprovedTotalAmount = 1097000m, ApprovedCurrencyCode = "AOA", ApprovedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), CreatedAtUtc = DateTime.UtcNow.AddDays(-60), UpdatedAtUtc = DateTime.UtcNow.AddDays(-30), RequestedDateUtc = DateTime.UtcNow };
        ctx.Requests.Add(request);
        var group = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = tag + " SUPPLIER", Status = RequestConstants.PoGroupStatuses.PoIssued, TotalAmount = 1097000m, CurrencyCode = "AOA", PurchaseOrderNumber = "ECF11 2026/156", CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-50) };
        ctx.RequestPoGroups.Add(group);
        ctx.RequestAttachments.Add(new RequestAttachment { Id = Guid.NewGuid(), RequestId = request.Id, RequestPoGroupId = group.Id, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = "po.pdf", StorageReference = "po.pdf", UploadedByUserId = user.Id, UploadedAtUtc = DateTime.UtcNow });
        ctx.RequestStatusHistories.AddRange(
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = request.Id, ActorUserId = user.Id, ActionTaken = "REGISTER_PO", NewStatusId = issued.Id, CreatedAtUtc = new DateTime(2026, 8, 3, 16, 23, 10, DateTimeKind.Utc) },
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = request.Id, ActorUserId = user.Id, ActionTaken = "FINANCE_RETURN_ADJUSTMENT", PreviousStatusId = issued.Id, NewStatusId = returned.Id, CreatedAtUtc = new DateTime(2026, 8, 11, 9, 42, 23, DateTimeKind.Utc) });
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, group.Id, returned.Id, user.Id);
    }

    private static async Task CleanupAsync(Seed s)
    {
        await using var ctx = NewCtx();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestPayments WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestAttachments WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestStatusHistories WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestPoGroups WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Requests WHERE Id = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Id = {s.UserId}");
    }

    private sealed record Snap(string GroupStatus, string GroupRowVersion, int ScalarStatusId, decimal? Approved, string? ApprovedCur, DateTime? ApprovedAt, int Histories, int Audits, int Attachments, int Payments, int Outbox, int ApLogs);

    private static async Task<Snap> SnapAsync(Seed s)
    {
        await using var ctx = NewCtx();
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupId);
        var r = await ctx.Requests.AsNoTracking().SingleAsync(x => x.Id == s.RequestId);
        return new Snap(g.Status, Convert.ToBase64String(g.RowVersion ?? Array.Empty<byte>()), r.StatusId, r.ApprovedTotalAmount, r.ApprovedCurrencyCode, r.ApprovedAtUtc,
            await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId),
            await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == LegacyFinanceReturnRepairService.RepairAction),
            await ctx.RequestAttachments.CountAsync(a => a.RequestId == s.RequestId),
            await ctx.RequestPayments.CountAsync(p => p.RequestId == s.RequestId),
            await ctx.EmailOutbox.CountAsync(e => e.RequestId == s.RequestId),
            await ctx.AccountsPayableNotificationLogs.CountAsync(l => l.RequestId == s.RequestId));
    }

    private static void AssertBusinessDataPreserved(Snap before, Snap after)
    {
        Assert.Equal(before.ScalarStatusId, after.ScalarStatusId);
        Assert.Equal(before.Approved, after.Approved); Assert.Equal(before.ApprovedCur, after.ApprovedCur); Assert.Equal(before.ApprovedAt, after.ApprovedAt);
        Assert.Equal(before.Attachments, after.Attachments); Assert.Equal(before.Payments, after.Payments);
        Assert.Equal(before.Outbox, after.Outbox); Assert.Equal(before.ApLogs, after.ApLogs);
    }

    [Fact]
    public async Task Preview_writes_nothing_and_apply_changes_only_the_group_plus_one_audit_then_repeats_as_already_repaired()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            var before = await SnapAsync(s);
            await using (var ctx = NewCtx())
            {
                var p = await new LegacyFinanceReturnRepairService(ctx).PreviewAsync(s.RequestId);
                Assert.Equal("QUALIFIES", p!.Decision); Assert.Equal(s.GroupId, p.GroupId);
            }
            Assert.Equal(before, await SnapAsync(s));                                                    // PREVIEW: byte-identical, RowVersion included

            await using (var ctx = NewCtx())
                Assert.Equal("REPAIRED", (await new LegacyFinanceReturnRepairService(ctx).ApplyAsync(s.RequestId, s.UserId, "relational apply", s.GroupId))!.Decision);
            var after = await SnapAsync(s);
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, after.GroupStatus);
            Assert.NotEqual(before.GroupRowVersion, after.GroupRowVersion);                               // the group row was the one written
            Assert.Equal(before.Histories + 1, after.Histories); Assert.Equal(1, after.Audits);
            AssertBusinessDataPreserved(before, after);

            await using (var ctx = NewCtx())
                Assert.Equal("ALREADY_REPAIRED", (await new LegacyFinanceReturnRepairService(ctx).ApplyAsync(s.RequestId, s.UserId, "again", s.GroupId))!.Decision);
            Assert.Equal(after, await SnapAsync(s));                                                     // repeated APPLY: no mutation, no second audit
        }
        finally { await CleanupAsync(s); }
    }

    [Fact]
    public async Task Stale_apply_fails_without_partial_writes_when_the_group_changed_after_it_was_loaded()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            var before = await SnapAsync(s);
            await using var ctx = NewCtx();
            var svc = new LegacyFinanceReturnRepairService(ctx)
            {
                BeforeApplySaveHookForTests = async () =>
                {
                    await using var other = NewCtx();                                                   // concurrent writer touches the same group row
                    var g = await other.RequestPoGroups.SingleAsync(x => x.Id == s.GroupId);
                    g.PurchaseOrderNumber = "ECF11 2026/156-B";
                    await other.SaveChangesAsync();
                }
            };
            var r = await svc.ApplyAsync(s.RequestId, s.UserId, "stale", s.GroupId);
            Assert.Equal("REFUSED", r!.Decision); Assert.Contains("RowVersion", r.Reason);
            var after = await SnapAsync(s);
            Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, after.GroupStatus);                   // status not written
            Assert.Equal(0, after.Audits); Assert.Equal(before.Histories, after.Histories);               // audit not written (same transaction, rolled back)
            AssertBusinessDataPreserved(before, after);
        }
        finally { await CleanupAsync(s); }
    }

    [Theory]
    [InlineData("scalarChanged")]
    [InlineData("paymentAdded")]
    [InlineData("reregistered")]
    public async Task Eligibility_is_rechecked_transactionally_so_changes_between_preview_and_apply_refuse_the_apply(string change)
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            await using (var ctx = NewCtx())
                Assert.Equal("QUALIFIES", (await new LegacyFinanceReturnRepairService(ctx).PreviewAsync(s.RequestId))!.Decision);
            await using (var ctx = NewCtx())
            {
                switch (change)
                {
                    case "scalarChanged":
                        var issued = await ctx.RequestStatuses.SingleAsync(x => x.Code == RequestConstants.Statuses.PoIssued);
                        (await ctx.Requests.SingleAsync(x => x.Id == s.RequestId)).StatusId = issued.Id; break;
                    case "paymentAdded":
                        ctx.RequestPayments.Add(new RequestPayment { RequestId = s.RequestId, RequestPoGroupId = s.GroupId, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = 1, PlannedAmount = 1097000m, CurrencyCode = "AOA", PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, CreatedByUserId = s.UserId, CreatedAtUtc = DateTime.UtcNow }); break;
                    case "reregistered":
                        ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.UserId, ActionTaken = "REREGISTER_PO", NewStatusId = s.ScalarStatusId, CreatedAtUtc = DateTime.UtcNow }); break;
                }
                await ctx.SaveChangesAsync();
            }
            var before = await SnapAsync(s);
            await using (var ctx = NewCtx())
            {
                var r = await new LegacyFinanceReturnRepairService(ctx).ApplyAsync(s.RequestId, s.UserId, "late apply", s.GroupId);
                Assert.Equal("REFUSED", r!.Decision);
            }
            Assert.Equal(before, await SnapAsync(s));
            Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, before.GroupStatus); Assert.Equal(0, before.Audits);
        }
        finally { await CleanupAsync(s); }
    }

    [Fact]
    public async Task Two_concurrent_applies_yield_exactly_one_repair_and_one_audit()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            var before = await SnapAsync(s);
            // Both APPLYs load and stage the same group version, then rendezvous at the barrier before either saves.
            using var barrier = new Barrier(2);
            async Task<string> RunAsync()
            {
                await using var ctx = NewCtx();
                var svc = new LegacyFinanceReturnRepairService(ctx) { BeforeApplySaveHookForTests = () => Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(30))) };
                var r = await svc.ApplyAsync(s.RequestId, s.UserId, "concurrent", s.GroupId);
                return r!.Decision;
            }
            var decisions = await Task.WhenAll(Task.Run(RunAsync), Task.Run(RunAsync));
            Assert.Equal(1, decisions.Count(d => d == "REPAIRED"));
            Assert.Equal(1, decisions.Count(d => d == "REFUSED"));
            var after = await SnapAsync(s);
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, after.GroupStatus);
            Assert.Equal(1, after.Audits); Assert.Equal(before.Histories + 1, after.Histories);
            AssertBusinessDataPreserved(before, after);
        }
        finally { await CleanupAsync(s); }
    }

    [Fact]
    public async Task Rollback_succeeds_only_while_its_preconditions_hold_and_refuses_after_a_legitimate_later_action()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            await using (var ctx = NewCtx())
                Assert.Equal("REPAIRED", (await new LegacyFinanceReturnRepairService(ctx).ApplyAsync(s.RequestId, s.UserId, "apply", s.GroupId))!.Decision);
            // (a) rollback while nothing happened after the repair
            await using (var ctx = NewCtx())
                Assert.Equal("ROLLED_BACK", (await new LegacyFinanceReturnRepairService(ctx).RollbackAsync(s.RequestId, s.UserId, "undo", s.GroupId))!.Decision);
            var afterRollback = await SnapAsync(s);
            Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, afterRollback.GroupStatus); Assert.Equal(s.ScalarStatusId, afterRollback.ScalarStatusId);
            // (b) repair again, then a legitimate later action (Buyer REREGISTER_PO) → rollback refused, nothing written
            await using (var ctx = NewCtx())
            {
                await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestStatusHistories WHERE RequestId = {s.RequestId} AND ActionTaken IN ('LEGACY_RETURN_REPAIR','LEGACY_RETURN_REPAIR_ROLLBACK')");
                Assert.Equal("REPAIRED", (await new LegacyFinanceReturnRepairService(ctx).ApplyAsync(s.RequestId, s.UserId, "apply again", s.GroupId))!.Decision);
                ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.UserId, ActionTaken = "REREGISTER_PO", NewStatusId = s.ScalarStatusId, CreatedAtUtc = DateTime.UtcNow.AddSeconds(2) });
                (await ctx.RequestPoGroups.SingleAsync(g => g.Id == s.GroupId)).Status = RequestConstants.PoGroupStatuses.PoIssued;
                await ctx.SaveChangesAsync();
            }
            var beforeLateRollback = await SnapAsync(s);
            await using (var ctx = NewCtx())
            {
                var rb = await new LegacyFinanceReturnRepairService(ctx).RollbackAsync(s.RequestId, s.UserId, "too late", s.GroupId);
                Assert.Equal("REFUSED", rb!.Decision); Assert.Contains("histórico posterior", rb.Reason);
            }
            Assert.Equal(beforeLateRollback, await SnapAsync(s));
        }
        finally { await CleanupAsync(s); }
    }
}
