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
/// Regression for TEST REQ-05/10/2026-526 (department 4, plant 1): the requester was also the only manager resolved by the
/// department/plant cascade; the manager branch skipped him, so no departmental summary was produced. Required behaviour:
/// a requester who is also a resolved eligible manager receives ONE e-mail (and one in-app notification) per action that
/// carries the requester wording PLUS the v2.247.2 departmental context; other managers keep their departmental notice;
/// requesters who are not managers keep the standard notice without context. Manager resolution is mocked (unchanged rules).
/// </summary>
public class DepartmentalRequesterManagerTests
{
    private const int Dept = 4, Plant = 1;
    private const int S_PAYMENT_SCHEDULED = 14, S_PAYMENT_COMPLETED = 15;

    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static readonly Guid Leonardo = Guid.NewGuid(), Milton = Guid.NewGuid(), Adelaide = Guid.NewGuid();

    private sealed class Seed { public Request Request = null!; public RequestPoGroup Group = null!; public RequestPayment Scheduled = null!, Paid = null!; }

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, Guid requesterId)
    {
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_PAYMENT_SCHEDULED, Code = RequestConstants.Statuses.PaymentScheduled, Name = "Pagamento Agendado" },
            new RequestStatus { Id = S_PAYMENT_COMPLETED, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Realizado" });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "PTA-ÁGUAS, LDA", TaxId = "5010", RegistrationStatus = "ACTIVE" });
        ctx.Users.AddRange(
            new User { Id = Leonardo, FullName = "Leonardo Cintra", Email = "leonardo.cintra@alpla.com", IsActive = true },
            new User { Id = Milton, FullName = "Milton Figueiredo", Email = "milton@test.local", IsActive = true },
            new User { Id = Adelaide, FullName = "Adelaide Kambambe", Email = "adelaide@test.local", IsActive = true });
        var s = new Seed();
        s.Request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-05/10/2026-526", Title = "Manutenção", RequestTypeId = 2, StatusId = S_PAYMENT_SCHEDULED, RequesterId = requesterId,
            DepartmentId = Dept, PlantId = Plant, CurrencyId = 1, EstimatedTotalAmount = 364800m, CreatedAtUtc = DateTime.UtcNow.AddDays(-5), UpdatedAtUtc = DateTime.UtcNow
        };
        s.Group = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.Request.Id, SupplierId = 10, SupplierNameSnapshot = "PTA-ÁGUAS, LDA", TotalAmount = 364800m, CurrencyCode = "AOA", Status = RequestConstants.Statuses.PaymentScheduled, CreatedByUserId = requesterId, CreatedAtUtc = DateTime.UtcNow.AddDays(-2) };
        s.Scheduled = new RequestPayment { RequestId = s.Request.Id, RequestPoGroupId = s.Group.Id, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = 1, PlannedAmount = 364800m, CurrencyCode = "AOA", ScheduledDateUtc = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, CreatedByUserId = requesterId, CreatedAtUtc = DateTime.UtcNow };
        s.Paid = new RequestPayment { RequestId = s.Request.Id, RequestPoGroupId = s.Group.Id, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = 2, PlannedAmount = 364800m, ActualPaidAmount = 364800m, CurrencyCode = "AOA", PaidDateUtc = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), PaymentStatus = RequestPayment.PaymentStatuses.Completed, CreatedByUserId = requesterId, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(s.Request); ctx.RequestPoGroups.Add(s.Group); ctx.RequestPayments.AddRange(s.Scheduled, s.Paid);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return s;
    }

    private static (WorkflowNotificationOrchestrator Orchestrator, Mock<INotificationService> InApp) Build(ApplicationDbContext ctx, params (Guid Id, string Name, string Email)[] managers)
    {
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        var adminLog = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        adminLog.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(Dept, Plant)).ReturnsAsync(new ApprovalRoutingResultDto
        {
            Managers = managers.Select(m => new AreaManagerDto { UserId = m.Id, FullName = m.Name, Email = m.Email, PlantId = Plant }).ToList()
        });
        return (new WorkflowNotificationOrchestrator(ctx, inApp.Object, new Mock<IEmailService>().Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, adminLog.Object, routing.Object), inApp);
    }

    private static WorkflowEvent Event(Seed s, string code) => new()
    {
        EventCode = code, RequestId = s.Request.Id, RequestNumber = s.Request.RequestNumber!, TargetStatusCode = code, ActionTaken = code, ActorUserId = Milton, ActorName = "Finance",
        CorrelationId = Guid.NewGuid(), RequesterId = s.Request.RequesterId, DepartmentId = s.Request.DepartmentId, PlantId = s.Request.PlantId, CompanyId = null,
        PoGroupId = s.Group.Id, PaymentId = code == WorkflowEventCodes.PaymentScheduled ? s.Scheduled.Id : s.Paid.Id
    };

    private const string Context = "Contexto Financeiro Departamental";

    private static void AssertDepartmentalContext(string body, bool scheduled)
    {
        Assert.Contains(Context, body);
        Assert.Contains(scheduled ? $"<b>Valor desta ação (agendado, grupo P.O.):</b> {364800m:N2} AOA" : $"<b>Valor desta ação (pago, grupo P.O.):</b> {364800m:N2} AOA", body);
        Assert.Contains($"<b>Valor deste pedido:</b> {364800m:N2} AOA", body);
        Assert.Contains("na moeda resolvida deste pedido, incl. este):</b>", body);
        Assert.Contains($"representa <b>{100m:N1}%</b>", body);
        Assert.Contains("<b>Período:</b> pedidos do departamento com última atualização desde", body);
    }

    public static IEnumerable<object[]> Events => new[] { new object[] { WorkflowEventCodes.PaymentScheduled }, new object[] { WorkflowEventCodes.PaymentCompleted } };

    [Theory, MemberData(nameof(Events))]
    public async Task Requester_who_is_the_only_eligible_manager_gets_one_email_with_requester_wording_and_departmental_context(string code)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, Leonardo);
        var (o, inApp) = Build(ctx, (Leonardo, "Leonardo Cintra", "leonardo.cintra@alpla.com"));
        var scheduled = code == WorkflowEventCodes.PaymentScheduled;

        await o.EmitAsync(Event(s, code));

        var row = Assert.Single(await ctx.EmailOutbox.ToListAsync());                                      // exactly ONE e-mail for the person and action
        Assert.Equal("leonardo.cintra@alpla.com", row.RecipientEmail);
        Assert.Equal(scheduled ? "Pagamento Agendado — REQ-05/10/2026-526 (com informação de departamento)" : "Pagamento Realizado — REQ-05/10/2026-526 (com informação de departamento)", row.Subject);
        Assert.Contains(scheduled ? "foi agendado pela equipa financeira" : "foi concluído pela equipa financeira", row.BodyHtml);  // requester wording kept
        AssertDepartmentalContext(row.BodyHtml, scheduled);                                               // v2.247.2 renderer reused
        Assert.DoesNotContain("O processo financeiro para o pedido", row.BodyHtml);                        // manager intro not duplicated
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(Leonardo, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Once); // one in-app
    }

    [Theory, MemberData(nameof(Events))]
    public async Task Requester_who_is_one_of_several_eligible_managers_gets_the_combined_email_and_the_other_manager_the_departmental_one(string code)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, Leonardo);
        var (o, inApp) = Build(ctx, (Milton, "Milton Figueiredo", "milton@test.local"), (Leonardo, "Leonardo Cintra", "leonardo.cintra@alpla.com"));
        var scheduled = code == WorkflowEventCodes.PaymentScheduled;

        await o.EmitAsync(Event(s, code));

        var rows = await ctx.EmailOutbox.ToListAsync();
        Assert.Equal(2, rows.Count);
        var leo = Assert.Single(rows, r => r.RecipientEmail == "leonardo.cintra@alpla.com");
        Assert.EndsWith("(com informação de departamento)", leo.Subject);
        AssertDepartmentalContext(leo.BodyHtml, scheduled);
        var milton = Assert.Single(rows, r => r.RecipientEmail == "milton@test.local");
        Assert.StartsWith(scheduled ? "[AGENDADO] Informa" : "[REALIZADO] Informa", milton.Subject);
        Assert.Contains("O processo financeiro para o pedido", milton.BodyHtml);
        AssertDepartmentalContext(milton.BodyHtml, scheduled);
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Exactly(2));
    }

    [Theory, MemberData(nameof(Events))]
    public async Task Requester_who_is_not_a_manager_keeps_the_standard_notice_and_the_manager_gets_the_departmental_one(string code)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, Adelaide);
        var (o, inApp) = Build(ctx, (Milton, "Milton Figueiredo", "milton@test.local"));
        var scheduled = code == WorkflowEventCodes.PaymentScheduled;

        await o.EmitAsync(Event(s, code));

        var rows = await ctx.EmailOutbox.ToListAsync();
        Assert.Equal(2, rows.Count);
        var adelaide = Assert.Single(rows, r => r.RecipientEmail == "adelaide@test.local");
        Assert.Equal(scheduled ? "Pagamento Agendado — REQ-05/10/2026-526" : "Pagamento Realizado — REQ-05/10/2026-526", adelaide.Subject);
        Assert.DoesNotContain(Context, adelaide.BodyHtml);
        var milton = Assert.Single(rows, r => r.RecipientEmail == "milton@test.local");
        AssertDepartmentalContext(milton.BodyHtml, scheduled);
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Exactly(2));
    }
}
