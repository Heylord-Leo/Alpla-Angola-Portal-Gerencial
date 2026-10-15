using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Data.Migrations;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Reminders;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Reminders;

/// <summary>
/// Reminder digests against a REAL SQL Server (LocalDB sandbox): (1) two overlapping instances racing
/// the same Luanda day produce exactly one digest and one outbox row per recipient (the unique index is
/// the guard); (2) a same-day restart is deduplicated and a direct duplicate INSERT is refused; (3) the
/// deterministic StageEnteredAtUtc backfill applies R1/R2/R3, leaves R0 NULL, never confuses lot #1 with
/// lot #10, ignores settled batches and is idempotent. Single-final-approver model: the company nominee
/// is the only final recipient.
/// </summary>
[Collection("IntegrationTests")]
public class ApprovalReminderRelationalTests
{
    static ApprovalReminderRelationalTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var digests = ctx.Database.SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.ApprovalReminderDigests'), 0) AS [Value]").AsEnumerable().First();
                var stageEntry = ctx.Database.SqlQueryRaw<int>("SELECT CAST(ISNULL(COL_LENGTH('dbo.ApprovalBatches', 'StageEnteredAtUtc'), 0) AS int) AS [Value]").AsEnumerable().First();
                var deferredTable = ctx.Database.SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.CompanyFinalApprovers'), 0) AS [Value]").AsEnumerable().First();
                if (digests == 0 || stageEntry == 0 || deferredTable != 0)
                {
                    Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                    ctx.Database.ExecuteSqlRaw("ALTER DATABASE CURRENT SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                    ctx.Database.EnsureDeleted();
                }
            }
            ctx.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ApprovalReminderRelationalTests] sandbox bootstrap: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static readonly DateTime Now = new(2026, 10, 7, 7, 0, 0, DateTimeKind.Utc);
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> AdminLogCapture = new();
    private static string Diagnostics(IEnumerable<DigestCycleResult> results) =>
        string.Join(" | ", results.Select(r => $"q={r.Run.DigestsQueued} dedup={r.Run.SkippedDedup} allow={r.Run.SkippedAllowList} failed={r.Run.Failed} err={r.Run.Error}"))
        + Environment.NewLine + string.Join(Environment.NewLine, AdminLogCapture.Where(l => l.Contains("FAILED") || l.Contains("CYCLE")).Take(10));
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private static async Task<T> GetOrAdd<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, Func<T> create) where T : class
    {
        var existing = await set.FirstOrDefaultAsync(match);
        if (existing != null) return existing;
        var e = create(); set.Add(e); return e;
    }

    private static ApprovalReminderDigestCycle Cycle(ApplicationDbContext ctx, ApprovalReminderOptions opts)
    {
        var adminLog = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        adminLog.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string, string, string?, string?>((_, _, evt, msg, exd, _) => AdminLogCapture.Add($"{evt}: {msg} {exd}"))
            .Returns(Task.CompletedTask);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        return new ApprovalReminderDigestCycle(ctx, new ApprovalRoutingService(ctx), Options.Create(opts), config, adminLog.Object, NullLogger<ApprovalReminderDigestCycle>.Instance);
    }

    private sealed record Seed(Guid RequestId, List<Guid> BatchIds, int CompanyId, int DepartmentId, List<Guid> UserIds, Guid Nominee, Guid AreaA, Guid AreaB, string[] RecipientEmails);

    /// <summary>One QUOTATION request with batch #1 WAITING_AREA (5 days) and batch #2 WAITING_FINAL (4 days); two area managers + one company nominee (also set as the request nominee).</summary>
    private static async Task<Seed> SeedDigestScenarioAsync()
    {
        await using var ctx = NewCtx();
        var tag = "ZZTEST_" + Guid.NewGuid().ToString("N")[..8];
        var quotationType = await GetOrAdd(ctx.RequestTypes, t => t.Code == RequestConstants.Types.Quotation, () => new RequestType { Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        var status = await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.WaitingQuotation, () => new RequestStatus { Code = RequestConstants.Statuses.WaitingQuotation, Name = "Cotação" });
        var role = await GetOrAdd(ctx.Roles, r => r.RoleName == RoleConstants.FinalApprover, () => new Role { RoleName = RoleConstants.FinalApprover });
        await ctx.SaveChangesAsync();

        var users = new[] { "fn", "aa", "ab", "req" }.Select(k => new User { Id = Guid.NewGuid(), FullName = $"{tag} {k}", Email = $"{tag.ToLower()}.{k}@test.local", IsActive = true }).ToList();
        ctx.Users.AddRange(users);
        await ctx.SaveChangesAsync();
        var (fn, aa, ab, req) = (users[0], users[1], users[2], users[3]);
        var company = new Company { Name = tag + " Co", IsActive = true, FinalApproverUserId = fn.Id };
        var dept = new Department { Name = tag + " Dep", IsActive = true };
        ctx.Companies.Add(company); ctx.Departments.Add(dept);
        await ctx.SaveChangesAsync();
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = fn.Id, RoleId = role.Id });
        ctx.DepartmentManagers.AddRange(new DepartmentManager { DepartmentId = dept.Id, UserId = aa.Id, IsActive = true }, new DepartmentManager { DepartmentId = dept.Id, UserId = ab.Id, IsActive = true });

        var request = new Request { Id = Guid.NewGuid(), RequestNumber = tag, Title = tag, StatusId = status.Id, RequestTypeId = quotationType.Id, DepartmentId = dept.Id, CompanyId = company.Id, RequesterId = req.Id, FinalApproverId = fn.Id, CreatedAtUtc = Now.AddDays(-10), RequestedDateUtc = Now.AddDays(-10) };
        ctx.Requests.Add(request);
        var b1 = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = Now.AddDays(-5), StageEnteredAtUtc = Now.AddDays(-5), CreatedByUserId = req.Id };
        var b2 = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 2, Status = RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, CreatedAtUtc = Now.AddDays(-8), StageEnteredAtUtc = Now.AddDays(-4), CreatedByUserId = req.Id };
        ctx.ApprovalBatches.AddRange(b1, b2);
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, new List<Guid> { b1.Id, b2.Id }, company.Id, dept.Id, users.Select(u => u.Id).ToList(), fn.Id, aa.Id, ab.Id, new[] { fn.Email, aa.Email, ab.Email });
    }

    private static async Task CleanupAsync(Seed s)
    {
        await using var ctx = NewCtx();
        foreach (var r in new[] { s.Nominee, s.AreaA, s.AreaB })
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE o FROM EmailOutbox o JOIN ApprovalReminderDigests d ON d.OutboxEntryId = o.Id WHERE d.RecipientUserId = {r}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ApprovalReminderDigests WHERE RecipientUserId = {r}"); // items cascade
        }
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM ApprovalReminderRuns WHERE NOT EXISTS (SELECT 1 FROM ApprovalReminderDigests d WHERE d.RunId = ApprovalReminderRuns.Id)");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestStatusHistories WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ApprovalBatches WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Requests WHERE Id = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DepartmentManagers WHERE DepartmentId = {s.DepartmentId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Companies WHERE Id = {s.CompanyId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Departments WHERE Id = {s.DepartmentId}");
        foreach (var u in s.UserIds)
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM UserRoleAssignments WHERE UserId = {u}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Id = {u}");
        }
    }

    [Fact]
    public async Task Two_overlapping_instances_on_the_same_day_queue_exactly_one_digest_and_one_outbox_row_per_recipient()
    {
        if (!CanConnect()) return;
        var s = await SeedDigestScenarioAsync();
        try
        {
            var opts = new ApprovalReminderOptions { Enabled = true, DryRun = false, RecipientAllowList = s.RecipientEmails };
            var recipients = new[] { s.Nominee, s.AreaA, s.AreaB };

            await using var ctx1 = NewCtx();
            await using var ctx2 = NewCtx();
            var gate = new SemaphoreSlim(0, 2);
            Task<DigestCycleResult> Run(ApplicationDbContext ctx) => Task.Run(async () => { await gate.WaitAsync(); return await Cycle(ctx, opts).RunAsync(Now, CancellationToken.None); });
            var t1 = Run(ctx1); var t2 = Run(ctx2);
            gate.Release(2);
            var results = await Task.WhenAll(t1, t2);

            await using var verify = NewCtx();
            var digests = await verify.ApprovalReminderDigests.AsNoTracking().Where(d => recipients.Contains(d.RecipientUserId) && d.DigestDateLocal == Now.Date).Include(d => d.Items).ToListAsync();
            Assert.Equal(3, digests.Count);
            Assert.Equal(3, digests.Select(d => d.RecipientUserId).Distinct().Count());
            var outboxIds = digests.Select(d => d.OutboxEntryId!.Value).ToList();
            Assert.Equal(3, await verify.EmailOutbox.CountAsync(o => outboxIds.Contains(o.Id)));
            Assert.All(digests, d => Assert.NotEmpty(d.Items));
            Assert.Equal(3, results.Sum(r => r.Run.DigestsQueued));
            Assert.True(6 == results.Sum(r => r.Run.DigestsQueued + r.Run.SkippedDedup), Diagnostics(results)); // each instance saw 3 recipients; losers were dedup-skipped, never failed
            Assert.Equal(0, results.Sum(r => r.Run.Failed));
            Assert.All(results, r => Assert.Null(r.Run.Error));
        }
        finally { await CleanupAsync(s); }
    }

    [Fact]
    public async Task Restart_on_the_same_day_is_deduplicated_by_the_persistent_key()
    {
        if (!CanConnect()) return;
        var s = await SeedDigestScenarioAsync();
        try
        {
            var opts = new ApprovalReminderOptions { Enabled = true, DryRun = false, RecipientAllowList = s.RecipientEmails };
            await using (var ctx = NewCtx()) { var first = await Cycle(ctx, opts).RunAsync(Now, CancellationToken.None); Assert.Equal(3, first.Run.DigestsQueued); }
            await using (var ctx = NewCtx())
            {
                var second = await Cycle(ctx, opts).RunAsync(Now.AddHours(3), CancellationToken.None);
                Assert.Equal(0, second.Run.DigestsQueued);
                Assert.Equal(3, second.Run.SkippedDedup);
            }
            await using (var ctx = NewCtx())
            {
                var existing = await ctx.ApprovalReminderDigests.AsNoTracking().FirstAsync(d => d.RecipientUserId == s.Nominee);
                ctx.ApprovalReminderDigests.Add(new ApprovalReminderDigest { RunId = existing.RunId, RecipientUserId = existing.RecipientUserId, DigestDateLocal = existing.DigestDateLocal, DryRun = existing.DryRun, Subject = "dup", PayloadHtml = "dup", CreatedAtUtc = Now });
                var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
                Assert.True(ApprovalReminderDigestCycle.IsUniqueViolation(ex));
            }
        }
        finally { await CleanupAsync(s); }
    }

    // ───────────────────────── StageEnteredAtUtc backfill ─────────────────────────

    private sealed record BackfillSeed(Guid RequestId, Dictionary<string, Guid> Batches, int CompanyId, int DepartmentId, Guid UserId);

    private static async Task<BackfillSeed> SeedBackfillScenarioAsync()
    {
        await using var ctx = NewCtx();
        var tag = "ZZTEST_" + Guid.NewGuid().ToString("N")[..8];
        var quotationType = await GetOrAdd(ctx.RequestTypes, t => t.Code == RequestConstants.Types.Quotation, () => new RequestType { Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        var status = await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.WaitingQuotation, () => new RequestStatus { Code = RequestConstants.Statuses.WaitingQuotation, Name = "Cotação" });
        await ctx.SaveChangesAsync();
        var company = new Company { Name = tag + " Co", IsActive = true };
        var dept = new Department { Name = tag + " Dep", IsActive = true };
        var user = new User { Id = Guid.NewGuid(), FullName = tag, Email = tag.ToLower() + "@test.local", IsActive = true };
        ctx.Companies.Add(company); ctx.Departments.Add(dept); ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var request = new Request { Id = Guid.NewGuid(), RequestNumber = tag, Title = tag, StatusId = status.Id, RequestTypeId = quotationType.Id, DepartmentId = dept.Id, CompanyId = company.Id, RequesterId = user.Id, CreatedAtUtc = Now.AddDays(-30), RequestedDateUtc = Now.AddDays(-30) };
        ctx.Requests.Add(request);

        ApprovalBatch B(int n, string st, DateTime created) => new() { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = n, Status = st, CreatedAtUtc = created, StageEnteredAtUtc = null, CreatedByUserId = user.Id };
        RequestStatusHistory H(string action, string comment, DateTime at) => new() { RequestId = request.Id, ActorUserId = user.Id, ActionTaken = action, NewStatusId = status.Id, Comment = comment, CreatedAtUtc = at };

        var r1 = B(1, "WAITING_FINAL_APPROVAL", Now.AddDays(-20));
        var r2 = B(2, "WAITING_AREA_APPROVAL", Now.AddDays(-19));
        var r3 = B(3, "WAITING_AREA_APPROVAL", Now.AddDays(-7));
        var r0a = B(4, "WAITING_FINAL_APPROVAL", Now.AddDays(-18));
        var r0b = B(5, "WAITING_AREA_APPROVAL", Now.AddDays(-17));
        var r10 = B(10, "WAITING_FINAL_APPROVAL", Now.AddDays(-16));
        var settled = B(6, "APPROVED", Now.AddDays(-25));
        var legacyNoHistory = B(7, "WAITING_FINAL_APPROVAL", Now.AddDays(-26));
        ctx.ApprovalBatches.AddRange(r1, r2, r3, r0a, r0b, r10, settled, legacyNoHistory);
        ctx.RequestStatusHistories.AddRange(
            H("BATCH_CREATED", "Lote #1 criado com 2 item(ns) e 3 opção(ões) de cotação. Vencedores a definir pelo Aprovador de Área.", Now.AddDays(-20)),
            H("BATCH_AREA_APPROVED", "Aprovação da Área do Lote #1 realizada. ok", Now.AddDays(-12)),
            H("BATCH_CREATED", "Lote #2 criado com 1 item(ns) e 1 opção(ões) de cotação. Vencedores a definir pelo Aprovador de Área.", Now.AddDays(-19)),
            H("BATCH_AREA_ADJUSTMENT", "Solicitado reajuste no Lote #2 na Aprovação da Área. Motivo: x", Now.AddDays(-15)),
            H("BATCH_RESUBMITTED", "Lote #2 reenviado para aprovação da área.", Now.AddDays(-14)),
            H("BATCH_AREA_ADJUSTMENT", "Solicitado reajuste no Lote #2 na Aprovação da Área. Motivo: y", Now.AddDays(-11)),
            H("BATCH_RESUBMITTED", "Lote #2 reenviado para aprovação da área. segunda", Now.AddDays(-9)),
            H("BATCH_CREATED", "Lote #3 criado com 1 item(ns) e 1 opção(ões) de cotação. Vencedores a definir pelo Aprovador de Área.", Now.AddDays(-7)),
            H("BATCH_CANDIDATES_SUBMITTED", "[Lote #3] Item #1 — 1 opção(ões) enviadas para aprovação: x", Now.AddDays(-7)),
            H("BATCH_CREATED", "Lote #4 criado com 1 item(ns) e 1 opção(ões) de cotação. Vencedores a definir pelo Aprovador de Área.", Now.AddDays(-18)),
            H("BATCH_CREATED", "Lote #5 criado com 1 item(ns) e 1 opção(ões) de cotação. Vencedores a definir pelo Aprovador de Área.", Now.AddDays(-17)),
            H("BATCH_AREA_ADJUSTMENT", "Solicitado reajuste no Lote #5 na Aprovação da Área. Motivo: z", Now.AddDays(-13)),
            H("BATCH_CREATED", "Lote #10 criado com 1 item(ns) e 1 opção(ões) de cotação. Vencedores a definir pelo Aprovador de Área.", Now.AddDays(-16)));
        await ctx.SaveChangesAsync();
        return new BackfillSeed(request.Id, new Dictionary<string, Guid> { ["r1"] = r1.Id, ["r2"] = r2.Id, ["r3"] = r3.Id, ["r0a"] = r0a.Id, ["r0b"] = r0b.Id, ["r10"] = r10.Id, ["settled"] = settled.Id, ["legacy"] = legacyNoHistory.Id }, company.Id, dept.Id, user.Id);
    }

    [Fact]
    public async Task Stage_entry_backfill_applies_only_deterministic_rules_and_is_idempotent()
    {
        if (!CanConnect()) return;
        var s = await SeedBackfillScenarioAsync();
        try
        {
            await using var ctx = NewCtx();
            await ctx.Database.ExecuteSqlRawAsync(AddApprovalReminderDigestsAndBatchStageEntry.StageEntryBackfillSql);
            var after1 = await ctx.ApprovalBatches.AsNoTracking().Where(b => b.RequestId == s.RequestId).ToDictionaryAsync(b => b.Id, b => b.StageEnteredAtUtc);
            await ctx.Database.ExecuteSqlRawAsync(AddApprovalReminderDigestsAndBatchStageEntry.StageEntryBackfillSql); // idempotent
            var after2 = await ctx.ApprovalBatches.AsNoTracking().Where(b => b.RequestId == s.RequestId).ToDictionaryAsync(b => b.Id, b => b.StageEnteredAtUtc);
            Assert.Equal(after1, after2);

            var created = await ctx.ApprovalBatches.AsNoTracking().Where(b => b.RequestId == s.RequestId).ToDictionaryAsync(b => b.Id, b => b.CreatedAtUtc);
            Assert.Equal(Now.AddDays(-12), after1[s.Batches["r1"]]);
            Assert.Equal(Now.AddDays(-9), after1[s.Batches["r2"]]);
            Assert.Equal(created[s.Batches["r3"]], after1[s.Batches["r3"]]);
            Assert.Null(after1[s.Batches["r0a"]]);
            Assert.Null(after1[s.Batches["r0b"]]);
            Assert.Null(after1[s.Batches["r10"]]);
            Assert.Null(after1[s.Batches["settled"]]);
            Assert.Null(after1[s.Batches["legacy"]]);
        }
        finally
        {
            await using var ctx = NewCtx();
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestStatusHistories WHERE RequestId = {s.RequestId}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ApprovalBatches WHERE RequestId = {s.RequestId}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Requests WHERE Id = {s.RequestId}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Companies WHERE Id = {s.CompanyId}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Departments WHERE Id = {s.DepartmentId}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Id = {s.UserId}");
        }
    }
}
