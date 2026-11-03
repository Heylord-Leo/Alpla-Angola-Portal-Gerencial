using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
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
/// SQL Server translation of the resolver's set-based fact queries (navigation COALESCE on group currency, Contains over
/// request Ids, GroupBy count on history) on the disposable LocalDB sandbox (IntegrationTestDatabase). Skips silently
/// when the sandbox is unreachable. Seeds a REQ-478-shaped legacy batch approval and a recorded-currency neighbour,
/// emits PAYMENT_COMPLETED through the real orchestrator and checks the manager's outbox body; everything seeded is removed.
/// </summary>
// Test isolation: every class that touches the shared LocalDB sandbox runs in the serialized "IntegrationTests"
// collection (drift-aware rebuilds and lock-race tests of other classes must never overlap with this one).
[Collection("IntegrationTests")]
public class AccountsPayableDepartmentalResolverRelationalTests
{
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private static async Task<T> GetOrAdd<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, Func<T> create) where T : class
    {
        var existing = await set.FirstOrDefaultAsync(match);
        if (existing != null) return existing;
        var e = create(); set.Add(e); return e;
    }

    private sealed record Seed(Guid CurrentId, Guid NeighbourId, Guid UserId, int DepartmentId, Guid GroupId, int PaymentId);

    private static async Task<Seed> SeedAsync()
    {
        await using var ctx = NewCtx();
        var tag = "ZZTEST_" + Guid.NewGuid().ToString("N")[..8];
        var type = await GetOrAdd(ctx.RequestTypes, t => t.Code == RequestConstants.Types.Quotation, () => new RequestType { Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        var paid = await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.PaymentCompleted, () => new RequestStatus { Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Realizado" });
        var sched = await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.PaymentScheduled, () => new RequestStatus { Code = RequestConstants.Statuses.PaymentScheduled, Name = "Pagamento Agendado" });
        var aoa = await GetOrAdd(ctx.Currencies, c => c.Code == "AOA", () => new Currency { Code = "AOA", Symbol = "Kz" });
        await ctx.SaveChangesAsync();
        var user = new User { Id = Guid.NewGuid(), FullName = tag, Email = tag.ToLower() + "@test.local", IsActive = true };
        var dept = new Department { Name = tag + " Dept", IsActive = true };
        ctx.Users.Add(user); ctx.Departments.Add(dept);
        await ctx.SaveChangesAsync();

        var current = new Request { Id = Guid.NewGuid(), RequestNumber = tag + "-478", Title = tag, StatusId = paid.Id, RequestTypeId = type.Id, DepartmentId = dept.Id, CompanyId = 1, CurrencyId = aoa.Id, RequesterId = user.Id, EstimatedTotalAmount = 0m, ApprovedTotalAmount = 351179.88m, ApprovedCurrencyCode = null, CreatedAtUtc = DateTime.UtcNow.AddDays(-5), UpdatedAtUtc = DateTime.UtcNow, RequestedDateUtc = DateTime.UtcNow };
        var neighbour = new Request { Id = Guid.NewGuid(), RequestNumber = tag + "-REC", Title = tag, StatusId = sched.Id, RequestTypeId = type.Id, DepartmentId = dept.Id, CompanyId = 1, CurrencyId = aoa.Id, RequesterId = user.Id, EstimatedTotalAmount = 0m, ApprovedTotalAmount = 390000m, ApprovedCurrencyCode = "AOA", ApprovedAtUtc = DateTime.UtcNow.AddDays(-3), CreatedAtUtc = DateTime.UtcNow.AddDays(-5), UpdatedAtUtc = DateTime.UtcNow, RequestedDateUtc = DateTime.UtcNow };
        ctx.Requests.AddRange(current, neighbour);
        var ga = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = current.Id, SupplierNameSnapshot = "AFRICANA DISCOUNT", TotalAmount = 319180.08m, CurrencyCode = "AOA", Status = RequestConstants.Statuses.PaymentCompleted, CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow };
        var gb = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = current.Id, SupplierNameSnapshot = "L.D.A. MORGEROT", TotalAmount = 31999.80m, CurrencyId = aoa.Id, CurrencyCode = null, Status = RequestConstants.Statuses.PaymentCompleted, CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow }; // currency via lookup only
        var gn = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = neighbour.Id, SupplierNameSnapshot = "REC", TotalAmount = 390000m, CurrencyCode = "AOA", Status = RequestConstants.Statuses.PaymentScheduled, CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestPoGroups.AddRange(ga, gb, gn);
        ctx.ApprovalBatches.AddRange(
            new ApprovalBatch { Id = Guid.NewGuid(), RequestId = current.Id, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.Approved, ApprovedTotalAmount = 319180.08m, CreatedAtUtc = DateTime.UtcNow.AddDays(-4), CreatedByUserId = user.Id },
            new ApprovalBatch { Id = Guid.NewGuid(), RequestId = current.Id, BatchNumber = 2, Status = RequestConstants.ApprovalBatchStatuses.Approved, ApprovedTotalAmount = 31999.80m, CreatedAtUtc = DateTime.UtcNow.AddDays(-4), CreatedByUserId = user.Id });
        ctx.RequestStatusHistories.AddRange(
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = current.Id, ActorUserId = user.Id, ActionTaken = "BATCH_FINAL_APPROVED", NewStatusId = paid.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-4) },
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = current.Id, ActorUserId = user.Id, ActionTaken = "BATCH_FINAL_APPROVED", NewStatusId = paid.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-4) });
        var pay = new RequestPayment { RequestId = current.Id, RequestPoGroupId = ga.Id, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = 1, PlannedAmount = 319180.08m, ActualPaidAmount = 319180.08m, PaidDateUtc = DateTime.UtcNow, CurrencyCode = "AOA", PaymentStatus = RequestPayment.PaymentStatuses.Completed, CreatedByUserId = user.Id, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestPayments.Add(pay);
        await ctx.SaveChangesAsync();
        return new Seed(current.Id, neighbour.Id, user.Id, dept.Id, ga.Id, pay.Id);
    }

    private static async Task CleanupAsync(Seed s)
    {
        await using var ctx = NewCtx();
        foreach (var id in new[] { s.CurrentId, s.NeighbourId })
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM EmailOutbox WHERE RequestId = {id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestPayments WHERE RequestId = {id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestStatusHistories WHERE RequestId = {id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ApprovalBatches WHERE RequestId = {id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestPoGroups WHERE RequestId = {id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Requests WHERE Id = {id}");
        }
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM InformationalNotifications WHERE UserId = {s.UserId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Departments WHERE Id = {s.DepartmentId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Id = {s.UserId}");
    }

    private static WorkflowNotificationOrchestrator Build(ApplicationDbContext ctx, Seed s)
    {
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        var adminLog = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        adminLog.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new ApprovalRoutingResultDto
        {
            Managers = new List<AreaManagerDto> { new() { UserId = Guid.NewGuid(), FullName = "Gestor", Email = "zztest.manager@test.local" } }
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        return new WorkflowNotificationOrchestrator(ctx, inApp.Object, new Mock<IEmailService>().Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, adminLog.Object, routing.Object);
    }

    [Fact]
    public async Task Resolver_queries_translate_to_sql_server_and_derive_the_legacy_currency_with_a_recorded_neighbour_in_the_same_bucket()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            await using (var ctx = NewCtx())
            {
                await Build(ctx, s).EmitAsync(new WorkflowEvent
                {
                    EventCode = WorkflowEventCodes.PaymentCompleted, RequestId = s.CurrentId, RequestNumber = "REQ-ZZ-478", TargetStatusCode = "PAYMENT_COMPLETED", ActionTaken = "PAYMENT_COMPLETED",
                    ActorUserId = s.UserId, ActorName = "Finance", CorrelationId = Guid.NewGuid(), RequesterId = s.UserId, DepartmentId = s.DepartmentId, PlantId = null, CompanyId = null,
                    PoGroupId = s.GroupId, PaymentId = s.PaymentId
                });
            }
            await using (var verify = NewCtx())
            {
                var body = (await verify.EmailOutbox.AsNoTracking().SingleAsync(x => x.RequestId == s.CurrentId && x.RecipientEmail == "zztest.manager@test.local")).BodyHtml;
                Assert.Contains($"<b>Valor deste pedido:</b> {351179.88m:N2} AOA <span style='color:#555;'>(base: valor aprovado, moeda derivada de dados atuais consistentes (não registada na aprovação))</span>", body);
                Assert.Contains($"{741179.88m:N2} AOA <span style='color:#555;'>(composição: 1 aprovado(s) registado(s), 1 com moeda derivada, 0 estimativa(s) — fallback)</span>", body);
                Assert.Contains($"representa <b>{47.4m:N1}%</b>", body);
                Assert.DoesNotContain("<b>Excluídos:</b>", body);
            }
        }
        finally { await CleanupAsync(s); }
    }
}
