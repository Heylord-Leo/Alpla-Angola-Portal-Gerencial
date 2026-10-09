using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services;

/// <summary>
/// Accounts Payable dedup against a REAL SQL Server (LocalDB sandbox, or the database named by
/// ALPLA_TEST_CONNECTION_STRING): both the application rule in the orchestrator and the filtered unique index
/// IX_ApNotifLogs_Dedup (RequestId, EventCode, RecipientEmail, CorrelationId) WHERE Success = 1 AND Skipped = 0.
/// - payment events: CorrelationId NULL → one success per (request, event, recipient), as before;
/// - PO_REGISTERED: one success per ACTION (history correlation): several groups and corrections are all recorded,
///   re-emitting the same action is skipped, and a forced duplicate success row is refused by the index.
/// E-mail is mocked; nothing is sent.
/// </summary>
[Collection("IntegrationTests")]
public class AccountsPayableDedupRelationalTests
{
    static AccountsPayableDedupRelationalTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var corr = ctx.Database.SqlQueryRaw<int>("SELECT CAST(ISNULL(COL_LENGTH('dbo.AccountsPayableNotificationLogs', 'CorrelationId'), 0) AS int) AS [Value]").AsEnumerable().First();
                var flag = ctx.Database.SqlQueryRaw<int>("SELECT CAST(ISNULL(COL_LENGTH('dbo.AccountsPayableNotificationConfigs', 'NotifyOnPoRegistered'), 0) AS int) AS [Value]").AsEnumerable().First();
                var migrated = ctx.Database.SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.__EFMigrationsHistory'), 0) AS [Value]").AsEnumerable().First();
                // Only the EnsureCreated sandbox is recreated on drift; a migrated database (ALPLA_TEST_CONNECTION_STRING) is left alone.
                if ((corr == 0 || flag == 0) && migrated == 0)
                {
                    Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                    ctx.Database.ExecuteSqlRaw("ALTER DATABASE CURRENT SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                    ctx.Database.EnsureDeleted();
                }
            }
            if (!ctx.Database.CanConnect() || ctx.Database.SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.__EFMigrationsHistory'), 0) AS [Value]").AsEnumerable().First() == 0)
                ctx.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AccountsPayableDedupRelationalTests] sandbox bootstrap: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private static async Task<T> GetOrAdd<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, Func<T> create) where T : class
    {
        var existing = await set.FirstOrDefaultAsync(match);
        if (existing != null) return existing;
        var e = create(); set.Add(e); return e;
    }

    private sealed record Seed(Guid RequestId, int CompanyId, string ApEmail, Guid UserId, int? ConfigId);

    private static async Task<Seed> SeedAsync(bool notifyOnPoRegistered)
    {
        await using var ctx = NewCtx();
        var tag = "ZZTEST_" + Guid.NewGuid().ToString("N")[..8];
        var paymentType = await GetOrAdd(ctx.RequestTypes, t => t.Code == RequestConstants.Types.Payment, () => new RequestType { Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        var status = await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.PoIssued, () => new RequestStatus { Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" });
        await ctx.SaveChangesAsync();
        var user = new User { Id = Guid.NewGuid(), FullName = tag, Email = tag.ToLower() + "@test.local", IsActive = true };
        var company = new Company { Name = tag + " Co", IsActive = true };
        ctx.Users.Add(user); ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var apEmail = tag.ToLower() + ".ap@test.local";
        var cfg = new AccountsPayableNotificationConfig { CompanyId = company.Id, Email = apEmail, CcEmails = tag.ToLower() + ".cc@test.local", IsActive = true, NotifyOnScheduled = true, NotifyOnCompleted = true, NotifyOnPoRegistered = notifyOnPoRegistered };
        ctx.AccountsPayableNotificationConfigs.Add(cfg);
        var request = new Request { Id = Guid.NewGuid(), RequestNumber = tag, Title = tag, StatusId = status.Id, RequestTypeId = paymentType.Id, DepartmentId = 1, CompanyId = company.Id, RequesterId = user.Id, BuyerId = user.Id, EstimatedTotalAmount = 100m, CreatedAtUtc = DateTime.UtcNow, RequestedDateUtc = DateTime.UtcNow };
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, company.Id, apEmail, user.Id, cfg.Id);
    }

    private static async Task CleanupAsync(Seed s)
    {
        await using var ctx = NewCtx();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AccountsPayableNotificationLogs WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM EmailOutbox WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM InformationalNotifications WHERE UserId = {s.UserId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Requests WHERE Id = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AccountsPayableNotificationConfigs WHERE CompanyId = {s.CompanyId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Companies WHERE Id = {s.CompanyId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Id = {s.UserId}");
    }

    private static (WorkflowNotificationOrchestrator Orchestrator, Mock<IEmailService> Email) Build(ApplicationDbContext ctx)
    {
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);
        var adminLog = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        adminLog.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new AlplaPortal.Application.DTOs.Requests.ApprovalRoutingResultDto());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        return (new WorkflowNotificationOrchestrator(ctx, inApp.Object, email.Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, adminLog.Object, routing.Object), email);
    }

    private static WorkflowEvent Evt(Seed s, string code, Guid correlation, string action) => new()
    {
        EventCode = code, RequestId = s.RequestId, RequestNumber = "REQ-DEDUP", TargetStatusCode = "PO_ISSUED", ActionTaken = action,
        ActorUserId = s.UserId, ActorName = "Tester", CorrelationId = correlation, RequesterId = s.UserId, BuyerId = s.UserId, DepartmentId = 1, PlantId = null, CompanyId = s.CompanyId
    };

    [Fact]
    public async Task Payment_events_keep_one_success_per_request_event_recipient_with_null_correlation_and_the_index_enforces_it()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(notifyOnPoRegistered: false);
        try
        {
            await using (var ctx = NewCtx())
            {
                var (o, email) = Build(ctx);
                await o.EmitAsync(Evt(s, WorkflowEventCodes.PaymentScheduled, Guid.NewGuid(), "SCHEDULE_PAYMENT"));
                await o.EmitAsync(Evt(s, WorkflowEventCodes.PaymentScheduled, Guid.NewGuid(), "SCHEDULE_PAYMENT")); // new correlation, same request → still deduped
                await o.EmitAsync(Evt(s, WorkflowEventCodes.PaymentCompleted, Guid.NewGuid(), "COMPLETE_PAYMENT"));
                email.Verify(e => e.SendWorkflowNotificationAsync(s.ApEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Exactly(2));
            }
            await using (var verify = NewCtx())
            {
                var logs = await verify.AccountsPayableNotificationLogs.AsNoTracking().Where(l => l.RequestId == s.RequestId).ToListAsync();
                Assert.Equal(2, logs.Count(l => l.Success && !l.Skipped));
                Assert.Equal(1, logs.Count(l => l.Skipped && l.EventCode == WorkflowEventCodes.PaymentScheduled));
                Assert.All(logs, l => Assert.Null(l.CorrelationId));

                // Database constraint: a second SUCCESS row for the same (request, event, recipient) with NULL correlation is refused.
                verify.AccountsPayableNotificationLogs.Add(new AccountsPayableNotificationLog { RequestId = s.RequestId, CompanyId = s.CompanyId, EventCode = WorkflowEventCodes.PaymentScheduled, RecipientEmail = s.ApEmail, Subject = "dup", SentAtUtc = DateTime.UtcNow, Success = true, Skipped = false, CorrelationId = null });
                var ex = await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
                Assert.True(ApprovalReminderDigestCycleIsUniqueViolation(ex));
            }
        }
        finally { await CleanupAsync(s); }
    }

    [Fact]
    public async Task Po_registered_records_each_action_once_and_the_index_refuses_a_duplicate_success_for_the_same_action()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(notifyOnPoRegistered: true);
        var group1 = Guid.NewGuid(); var group2 = Guid.NewGuid(); var correction = Guid.NewGuid();
        try
        {
            await using (var ctx = NewCtx())
            {
                var (o, email) = Build(ctx);
                await o.EmitAsync(Evt(s, WorkflowEventCodes.PoRegistered, group1, "REGISTER_PO"));
                await o.EmitAsync(Evt(s, WorkflowEventCodes.PoRegistered, group2, "REGISTER_PO"));          // second P.O. group, same request + recipient
                await o.EmitAsync(Evt(s, WorkflowEventCodes.PoRegistered, correction, "REREGISTER_PO"));    // correction with its own history row
                await o.EmitAsync(Evt(s, WorkflowEventCodes.PoRegistered, group1, "REGISTER_PO"));          // same action re-emitted → skipped
                email.Verify(e => e.SendWorkflowNotificationAsync(s.ApEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Exactly(3));
            }
            await using (var verify = NewCtx())
            {
                var logs = await verify.AccountsPayableNotificationLogs.AsNoTracking().Where(l => l.RequestId == s.RequestId).OrderBy(l => l.Id).ToListAsync();
                Assert.Equal(4, logs.Count);
                Assert.Equal(new[] { group1, group2, correction }, logs.Where(l => l.Success && !l.Skipped).Select(l => l.CorrelationId!.Value).ToArray());
                var skipped = Assert.Single(logs.Where(l => l.Skipped));
                Assert.Equal(group1, skipped.CorrelationId);

                // Database constraint: a forced second SUCCESS row for the SAME action is refused …
                verify.AccountsPayableNotificationLogs.Add(new AccountsPayableNotificationLog { RequestId = s.RequestId, CompanyId = s.CompanyId, EventCode = WorkflowEventCodes.PoRegistered, RecipientEmail = s.ApEmail, Subject = "dup", SentAtUtc = DateTime.UtcNow, Success = true, Skipped = false, CorrelationId = group1 });
                var ex = await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
                Assert.True(ApprovalReminderDigestCycleIsUniqueViolation(ex));
                verify.ChangeTracker.Clear();

                // … while a further distinct action (another correction) is accepted by the index.
                verify.AccountsPayableNotificationLogs.Add(new AccountsPayableNotificationLog { RequestId = s.RequestId, CompanyId = s.CompanyId, EventCode = WorkflowEventCodes.PoRegistered, RecipientEmail = s.ApEmail, Subject = "another correction", SentAtUtc = DateTime.UtcNow, Success = true, Skipped = false, CorrelationId = Guid.NewGuid() });
                await verify.SaveChangesAsync();
                Assert.Equal(4, await verify.AccountsPayableNotificationLogs.CountAsync(l => l.RequestId == s.RequestId && l.Success && !l.Skipped));
            }
        }
        finally { await CleanupAsync(s); }
    }

    private static bool ApprovalReminderDigestCycleIsUniqueViolation(DbUpdateException ex) =>
        AlplaPortal.Infrastructure.Services.Reminders.ApprovalReminderDigestCycle.IsUniqueViolation(ex);
}
