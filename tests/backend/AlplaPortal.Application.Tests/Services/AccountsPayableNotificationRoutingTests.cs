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
/// Real WorkflowNotificationOrchestrator on InMemory. Accounts Payable group e-mail (direct send through
/// IEmailService, mocked — nothing is sent) and individual Finance e-mail (outbox rows) under every combination of
/// the two per-company switches; company selection, To/CC; per-action dedup for PO_REGISTERED across several P.O.
/// groups and corrections; unchanged request-level dedup for payment scheduling/completion; Finance in-app
/// notifications preserved throughout.
/// </summary>
public class AccountsPayableNotificationRoutingTests
{
    private const int PlantA = 5, PlantB = 6;

    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Seed
    {
        public Request Request = null!;
        public Guid FinanceA1, FinanceA2, FinanceB, Buyer;
        public AccountsPayableNotificationConfig? Config;
    }

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, bool withConfig = true, bool configActive = true, bool notifyOnPoRegistered = false, bool notifyFinanceByEmail = false, bool notifyOnScheduled = true)
    {
        var s = new Seed { FinanceA1 = Guid.NewGuid(), FinanceA2 = Guid.NewGuid(), FinanceB = Guid.NewGuid(), Buyer = Guid.NewGuid() };
        ctx.Companies.AddRange(new Company { Id = 1, Name = "AlplaPLASTICO", IsActive = true }, new Company { Id = 2, Name = "AlplaSOPRO", IsActive = true });
        ctx.Plants.AddRange(new Plant { Id = PlantA, Name = "A" }, new Plant { Id = PlantB, Name = "B" });
        var finance = new Role { Id = 3, RoleName = RoleConstants.Finance }; ctx.Roles.Add(finance);
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "IP WORLD, LDA", TaxId = "5000", RegistrationStatus = "ACTIVE" });
        ctx.Users.AddRange(
            new User { Id = s.Buyer, FullName = "Comprador", Email = "buyer@test.local", IsActive = true },
            new User { Id = s.FinanceA1, FullName = "Finance A1", Email = "finance.a1@test.local", IsActive = true },
            new User { Id = s.FinanceA2, FullName = "Finance A2", Email = "finance.a2@test.local", IsActive = true },
            new User { Id = s.FinanceB, FullName = "Finance B", Email = "finance.b@test.local", IsActive = true });
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = s.FinanceA1, RoleId = 3 }, new UserRoleAssignment { UserId = s.FinanceA2, RoleId = 3 }, new UserRoleAssignment { UserId = s.FinanceB, RoleId = 3 });
        ctx.UserPlantScopes.AddRange(new UserPlantScope { UserId = s.FinanceA1, PlantId = PlantA }, new UserPlantScope { UserId = s.FinanceA2, PlantId = PlantA }, new UserPlantScope { UserId = s.FinanceB, PlantId = PlantB });
        // A second company's configuration must never be selected for company 1
        ctx.AccountsPayableNotificationConfigs.Add(new AccountsPayableNotificationConfig { Id = 99, CompanyId = 2, Email = "sopro-ap@alpla.com", IsActive = true, NotifyOnPoRegistered = true, NotifyFinanceUsersByEmail = true });
        if (withConfig)
        {
            s.Config = new AccountsPayableNotificationConfig
            {
                Id = 1, CompanyId = 1, Email = "alpla-plasticos-accounts@alpla.com", CcEmails = "aovia-treasury@alpla.com", IsActive = configActive,
                NotifyOnScheduled = notifyOnScheduled, NotifyOnCompleted = true, NotifyOnPoRegistered = notifyOnPoRegistered, NotifyFinanceUsersByEmail = notifyFinanceByEmail
            };
            ctx.AccountsPayableNotificationConfigs.Add(s.Config);
        }
        s.Request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-08/10/2026-449", Title = "Serviços IP World", RequestTypeId = 2, StatusId = 9,
            RequesterId = s.Buyer, BuyerId = s.Buyer, DepartmentId = 7, CompanyId = 1, PlantId = PlantA, SupplierId = 10, EstimatedTotalAmount = 400758.34m, CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(s.Request);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return s;
    }

    private static (WorkflowNotificationOrchestrator Orchestrator, Mock<INotificationService> InApp, Mock<IEmailService> Email) Build(ApplicationDbContext ctx, bool smtpAccepts = true)
    {
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(smtpAccepts);
        var adminLog = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        adminLog.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new AlplaPortal.Application.DTOs.Requests.ApprovalRoutingResultDto());
        return (new WorkflowNotificationOrchestrator(ctx, inApp.Object, email.Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, adminLog.Object, routing.Object), inApp, email);
    }

    private static WorkflowEvent PoEvent(Request r, Guid correlation, string action = "REGISTER_PO") => new()
    {
        EventCode = WorkflowEventCodes.PoRegistered, RequestId = r.Id, RequestNumber = r.RequestNumber!, RequestTitle = r.Title, TargetStatusCode = "PO_ISSUED",
        ActionTaken = action, ActorUserId = r.BuyerId!.Value, ActorName = "Comprador", CorrelationId = correlation,
        RequesterId = r.RequesterId, BuyerId = r.BuyerId, DepartmentId = r.DepartmentId, PlantId = r.PlantId, CompanyId = r.CompanyId
    };

    private static WorkflowEvent PaymentEvent(Request r, string code, Guid correlation, Guid? groupId = null, int? paymentId = null) => new()
    {
        EventCode = code, RequestId = r.Id, RequestNumber = r.RequestNumber!, TargetStatusCode = code, ActionTaken = code, ActorUserId = r.BuyerId!.Value, ActorName = "Finance",
        CorrelationId = correlation, RequesterId = r.RequesterId, BuyerId = r.BuyerId, DepartmentId = r.DepartmentId, PlantId = r.PlantId, CompanyId = r.CompanyId,
        PoGroupId = groupId, PaymentId = paymentId
    };

    /// <summary>Two groups on the seeded request: A (MULTI BIZ, 285,000.00 AOA, scheduled row) and B (USD VENDOR &amp; CO, 1,500.00 USD, completed row paid 1,480.00).</summary>
    private static async Task<(RequestPoGroup A, RequestPoGroup B, RequestPayment PayA, RequestPayment PayB)> SeedTwoGroupsAsync(ApplicationDbContext ctx, Seed s)
    {
        ctx.Suppliers.AddRange(
            new Supplier { Id = 20, Name = "MULTI BIZ, LDA", TaxId = "5020", RegistrationStatus = "ACTIVE" },
            new Supplier { Id = 21, Name = "USD VENDOR & CO", TaxId = "5021", RegistrationStatus = "ACTIVE" });
        var a = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.Request.Id, SupplierId = 20, SupplierNameSnapshot = "MULTI BIZ, LDA", TotalAmount = 285000m, CurrencyCode = "AOA", Status = RequestConstants.Statuses.PaymentScheduled, CreatedByUserId = s.Buyer };
        var b = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.Request.Id, SupplierId = 21, SupplierNameSnapshot = "USD VENDOR & CO", TotalAmount = 1500m, CurrencyCode = "USD", Status = RequestConstants.Statuses.PaymentCompleted, CreatedByUserId = s.Buyer };
        var payA = new RequestPayment { RequestId = s.Request.Id, RequestPoGroupId = a.Id, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = 1, PlannedAmount = 285000m, CurrencyCode = "AOA", ScheduledDateUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, CreatedByUserId = s.Buyer, CreatedAtUtc = DateTime.UtcNow };
        var payB = new RequestPayment { RequestId = s.Request.Id, RequestPoGroupId = b.Id, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = 2, PlannedAmount = 1500m, ActualPaidAmount = 1480m, PaidDateUtc = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), CurrencyCode = "USD", PaymentStatus = RequestPayment.PaymentStatuses.Completed, CreatedByUserId = s.Buyer, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestPoGroups.AddRange(a, b);
        ctx.RequestPayments.AddRange(payA, payB);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return (a, b, payA, payB);
    }

    private static List<(string Subject, string Headline, string Body)> ApMails(Mock<IEmailService> email) =>
        email.Invocations.Where(i => i.Method.Name == nameof(IEmailService.SendWorkflowNotificationAsync))
            .Select(i => ((string)i.Arguments[2], (string)i.Arguments[3], (string)i.Arguments[4])).ToList();

    private static void VerifyApSend(Mock<IEmailService> email, Times times, string? subjectContains = null) =>
        email.Verify(e => e.SendWorkflowNotificationAsync("alpla-plasticos-accounts@alpla.com", "AlplaPLASTICO", It.Is<string>(s => subjectContains == null || s.Contains(subjectContains)), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), "aovia-treasury@alpla.com"), times);

    // ───────────── switch combinations on PO_REGISTERED ─────────────

    [Fact]
    public async Task Both_switches_off_default_in_app_only_no_ap_mail_no_finance_mail()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (o, inApp, email) = Build(ctx);
        await o.EmitAsync(PoEvent(s.Request, Guid.NewGuid()));
        VerifyApSend(email, Times.Never());
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync());
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), "Nova P.O Registrada", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Exactly(2)); // A1, A2 (plant A)
        Assert.Equal(0, await ctx.AccountsPayableNotificationLogs.CountAsync());
    }

    [Fact]
    public async Task Only_ap_switch_on_sends_review_notice_to_ap_to_and_cc_and_no_finance_mail()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, notifyOnPoRegistered: true);
        var corr = Guid.NewGuid();
        var (o, inApp, email) = Build(ctx);
        await o.EmitAsync(PoEvent(s.Request, corr));

        VerifyApSend(email, Times.Once(), "P.O. registada");
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(sub => sub.Contains("revisão de Contas a Pagar") && sub.Contains("REQ-08/10/2026-449")), It.Is<string>(h => h.Contains("Revisão Necessária")),
            It.Is<string>(b => b.Contains("significa que o pagamento est") && b.Contains("aguarda a revis") && !b.Contains("pedido de pagamento entrou")), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once); // body uses HTML entities for accents
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync());
        var log = await ctx.AccountsPayableNotificationLogs.SingleAsync();
        Assert.Equal(WorkflowEventCodes.PoRegistered, log.EventCode); Assert.True(log.Success); Assert.False(log.Skipped); Assert.Equal(corr, log.CorrelationId); Assert.Equal(1, log.CompanyId);
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), corr, It.IsAny<string?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Only_finance_switch_on_queues_plant_scoped_finance_mail_and_no_ap_mail()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, notifyFinanceByEmail: true);
        var corr = Guid.NewGuid();
        var (o, inApp, email) = Build(ctx);
        await o.EmitAsync(PoEvent(s.Request, corr));

        VerifyApSend(email, Times.Never());
        var outbox = await ctx.EmailOutbox.ToListAsync();
        Assert.Equal(new[] { "finance.a1@test.local", "finance.a2@test.local" }, outbox.Select(x => x.RecipientEmail).OrderBy(x => x));
        Assert.All(outbox, x => { Assert.Equal(corr, x.CorrelationId); Assert.Equal(WorkflowEventCodes.PoRegistered, x.EventCode); });
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(s.FinanceB, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Both_switches_on_send_ap_mail_and_queue_finance_mail_once_each()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, notifyOnPoRegistered: true, notifyFinanceByEmail: true);
        var (o, _, email) = Build(ctx);
        await o.EmitAsync(PoEvent(s.Request, Guid.NewGuid()));
        VerifyApSend(email, Times.Once());
        Assert.Equal(2, await ctx.EmailOutbox.CountAsync());
        Assert.Equal(1, await ctx.AccountsPayableNotificationLogs.CountAsync());
    }

    [Fact]
    public async Task Missing_or_inactive_company_configuration_never_enables_finance_mail_but_keeps_in_app()
    {
        await using (var ctx = NewCtx())
        {
            var s = await SeedAsync(ctx, withConfig: false);
            var (o, inApp, email) = Build(ctx);
            await o.EmitAsync(PoEvent(s.Request, Guid.NewGuid()));
            VerifyApSend(email, Times.Never());
            Assert.Equal(0, await ctx.EmailOutbox.CountAsync());
            inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Exactly(2));
        }
        await using (var ctx = NewCtx())
        {
            var s = await SeedAsync(ctx, configActive: false, notifyOnPoRegistered: true, notifyFinanceByEmail: true);
            var (o, inApp, email) = Build(ctx);
            await o.EmitAsync(PoEvent(s.Request, Guid.NewGuid()));
            VerifyApSend(email, Times.Never());
            Assert.Equal(0, await ctx.EmailOutbox.CountAsync());
            inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Exactly(2));
        }
    }

    [Fact]
    public async Task Event_without_company_or_plant_falls_back_to_in_app_only()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, notifyOnPoRegistered: true, notifyFinanceByEmail: true);
        var (o, inApp, email) = Build(ctx);
        var evt = PoEvent(s.Request, Guid.NewGuid());
        await o.EmitAsync(new WorkflowEvent { EventCode = evt.EventCode, RequestId = evt.RequestId, RequestNumber = evt.RequestNumber, TargetStatusCode = evt.TargetStatusCode, ActionTaken = evt.ActionTaken, ActorUserId = evt.ActorUserId, CorrelationId = evt.CorrelationId }); // the pre-fix shape
        VerifyApSend(email, Times.Never());                      // no CompanyId → AP skipped
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync());     // no PlantId → global fan-out, e-mail suppressed
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Exactly(3)); // all Finance users in-app
    }

    [Fact]
    public async Task Ap_notice_is_sent_even_when_no_finance_user_exists_for_the_plant()
    {
        // Found on the LocalDB sandbox (no Finance users seeded): the per-user "no recipients" early return used to skip
        // the AP block too, so a company whose plant has no Finance user would never get its P.O. review notice.
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, notifyOnPoRegistered: true);
        foreach (var ps in await ctx.UserPlantScopes.ToListAsync()) ctx.UserPlantScopes.Remove(ps); // nobody scoped to any plant
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (o, inApp, email) = Build(ctx);

        await o.EmitAsync(PoEvent(s.Request, Guid.NewGuid()));

        VerifyApSend(email, Times.Once());
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync());
        Assert.Equal(1, await ctx.AccountsPayableNotificationLogs.CountAsync(l => l.Success));
    }

    [Fact]
    public async Task Ap_notice_content_comes_from_the_event_group_not_the_request_header_and_falls_back_without_a_group()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, notifyOnPoRegistered: true);
        ctx.Suppliers.Add(new Supplier { Id = 30, Name = "Grupo & Filhos, Lda", TaxId = "8000", RegistrationStatus = "ACTIVE" });
        var g = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.Request.Id, SupplierId = 30, SupplierNameSnapshot = "Grupo & Filhos, Lda", Status = RequestConstants.PoGroupStatuses.Pending, TotalAmount = 1234.56m, CurrencyCode = "USD", CreatedByUserId = s.Buyer };
        ctx.RequestPoGroups.Add(g); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (o, _, email) = Build(ctx);
        var bodies = new List<string>();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
             .Callback<string, string, string, string, string, string?, string?, string?>((_, _, _, _, body, _, _, _) => bodies.Add(body)).ReturnsAsync(true);

        var withGroup = PoEvent(s.Request, Guid.NewGuid());
        await o.EmitAsync(new WorkflowEvent { EventCode = withGroup.EventCode, RequestId = withGroup.RequestId, RequestNumber = withGroup.RequestNumber, RequestTitle = withGroup.RequestTitle, TargetStatusCode = withGroup.TargetStatusCode, ActionTaken = withGroup.ActionTaken, ActorUserId = withGroup.ActorUserId, ActorName = withGroup.ActorName, CorrelationId = withGroup.CorrelationId, RequesterId = withGroup.RequesterId, BuyerId = withGroup.BuyerId, DepartmentId = withGroup.DepartmentId, PlantId = withGroup.PlantId, CompanyId = withGroup.CompanyId, PoGroupId = g.Id });
        Assert.Contains("Grupo &amp; Filhos, Lda", bodies[0]);
        Assert.Contains($"{1234.56m:N2} USD", bodies[0]);
        Assert.DoesNotContain("IP WORLD", bodies[0]);
        Assert.DoesNotContain($"{400758.34m:N2}", bodies[0]);

        await o.EmitAsync(PoEvent(s.Request, Guid.NewGuid())); // no PoGroupId → documented fallback to header values (logged)
        Assert.Contains("IP WORLD, LDA", bodies[1]);
    }

    // ───────────── dedup granularity ─────────────

    [Fact]
    public async Task Second_po_group_and_correction_each_notify_ap_while_a_repeated_emission_of_the_same_action_is_skipped()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, notifyOnPoRegistered: true);
        var (o, _, email) = Build(ctx);
        var group1 = Guid.NewGuid(); var group2 = Guid.NewGuid(); var correction = Guid.NewGuid();

        await o.EmitAsync(PoEvent(s.Request, group1));
        await o.EmitAsync(PoEvent(s.Request, group2));                       // second P.O. group on the same request
        await o.EmitAsync(PoEvent(s.Request, correction, "REREGISTER_PO")); // legitimate correction
        await o.EmitAsync(PoEvent(s.Request, group1));                       // same action emitted again (e.g. retry)

        VerifyApSend(email, Times.Exactly(3));
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(sub => sub.Contains("P.O. corrigida e re-registada")), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
        var logs = await ctx.AccountsPayableNotificationLogs.OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(4, logs.Count);
        Assert.Equal(3, logs.Count(l => l.Success && !l.Skipped));
        var skipped = Assert.Single(logs.Where(l => l.Skipped));
        Assert.Equal(group1, skipped.CorrelationId);
    }

    [Fact]
    public async Task Payment_scheduling_and_completion_have_distinct_wording_and_keep_request_level_dedup_recipients_and_logs()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (a, b, payA, payB) = await SeedTwoGroupsAsync(ctx, s);
        var (o, _, email) = Build(ctx);

        await o.EmitAsync(PaymentEvent(s.Request, WorkflowEventCodes.PaymentScheduled, Guid.NewGuid(), a.Id, payA.Id));
        await o.EmitAsync(PaymentEvent(s.Request, WorkflowEventCodes.PaymentScheduled, Guid.NewGuid(), b.Id, payB.Id)); // different correlation AND group, same request → still deduped (unchanged rule)
        await o.EmitAsync(PaymentEvent(s.Request, WorkflowEventCodes.PaymentCompleted, Guid.NewGuid(), b.Id, payB.Id));

        // Recipients (TO + CC + company name) unchanged; exactly one mail per event code
        VerifyApSend(email, Times.Exactly(2));
        var mails = ApMails(email);
        var scheduled = Assert.Single(mails, m => m.Subject.Contains("Pagamento agendado"));
        var completed = Assert.Single(mails, m => m.Subject.Contains("Pagamento realizado"));
        Assert.DoesNotContain(mails, m => m.Subject.Contains("Novo pedido de pagamento"));
        Assert.Contains("Pagamento Agendado \u2014 AlplaPLASTICO", scheduled.Headline);
        Assert.Contains("Pagamento Realizado \u2014 AlplaPLASTICO", completed.Headline);
        Assert.Contains("foi <b>agendado</b> pelas Finan&#231;as e entrou na lista de Contas a Pagar", scheduled.Body);
        Assert.Contains("<b>Status atual:</b></td><td style='padding:6px 0;'>Pagamento Agendado", scheduled.Body);
        Assert.Contains("confirmado como <b>realizado</b> pelas Finan&#231;as", completed.Body);
        Assert.DoesNotContain("entrou na lista de Contas a Pagar", completed.Body);
        Assert.Contains("<b>Status atual:</b></td><td style='padding:6px 0;'>Pagamento Realizado", completed.Body);

        var logs = await ctx.AccountsPayableNotificationLogs.ToListAsync();
        Assert.Equal(2, logs.Count(l => l.Success));
        Assert.Equal(1, logs.Count(l => l.Skipped && l.EventCode == WorkflowEventCodes.PaymentScheduled));
        Assert.All(logs, l => Assert.Null(l.CorrelationId)); // payment rows keep NULL correlation
        Assert.All(logs, l => Assert.Equal("alpla-plasticos-accounts@alpla.com", l.RecipientEmail));
        Assert.Contains(await ctx.EmailOutbox.ToListAsync(), x => x.RecipientEmail == "buyer@test.local"); // requester routing unchanged
    }

    [Fact]
    public async Task Payment_notices_render_the_acted_on_group_and_payment_row_never_the_header_another_group_or_a_mixed_currency()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);                    // header: IP WORLD, 400,758.34 (no CurrencyId → AOA)
        var (a, b, payA, payB) = await SeedTwoGroupsAsync(ctx, s);
        var (o, _, email) = Build(ctx);

        await o.EmitAsync(PaymentEvent(s.Request, WorkflowEventCodes.PaymentScheduled, Guid.NewGuid(), a.Id, payA.Id));
        await o.EmitAsync(PaymentEvent(s.Request, WorkflowEventCodes.PaymentCompleted, Guid.NewGuid(), b.Id, payB.Id));

        var mails = ApMails(email);
        var sch = Assert.Single(mails, m => m.Subject.Contains("Pagamento agendado")).Body;
        Assert.Contains("<b>Fornecedor:</b></td><td style='padding:6px 0;'>MULTI BIZ, LDA</td>", sch);
        Assert.Contains($"<b>Montante agendado (grupo P.O.):</b></td><td style='padding:6px 0;'>{285000m:N2} AOA</td>", sch);
        Assert.Contains("<b>Data agendada:</b></td><td style='padding:6px 0;'>20/10/2026</td>", sch);
        Assert.DoesNotContain("IP WORLD", sch); Assert.DoesNotContain($"{400758.34m:N2}", sch); Assert.DoesNotContain("USD", sch); Assert.DoesNotContain("Data do pagamento", sch);

        var cmp = Assert.Single(mails, m => m.Subject.Contains("Pagamento realizado")).Body;
        Assert.Contains($"<b>Fornecedor:</b></td><td style='padding:6px 0;'>{System.Net.WebUtility.HtmlEncode("USD VENDOR & CO")}</td>", cmp);
        Assert.Contains($"<b>Montante pago (grupo P.O.):</b></td><td style='padding:6px 0;'>{1480m:N2} USD</td>", cmp); // actual paid, not planned 1,500.00
        Assert.Contains("<b>Data do pagamento:</b></td><td style='padding:6px 0;'>09/10/2026</td>", cmp);
        Assert.DoesNotContain($"{1500m:N2}", cmp); Assert.DoesNotContain("AOA", cmp); Assert.DoesNotContain("MULTI BIZ", cmp); Assert.DoesNotContain($"{400758.34m:N2}", cmp); Assert.DoesNotContain("Data agendada", cmp);
    }

    [Fact]
    public async Task Payment_notice_without_a_group_reference_uses_the_request_header_and_labels_it_as_request_level()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        await SeedTwoGroupsAsync(ctx, s);                                                 // groups exist but the (legacy) event does not reference one
        var (o, _, email) = Build(ctx);

        await o.EmitAsync(PaymentEvent(s.Request, WorkflowEventCodes.PaymentScheduled, Guid.NewGuid()));

        var body = Assert.Single(ApMails(email)).Body;
        Assert.Contains("<b>Fornecedor:</b></td><td style='padding:6px 0;'>IP WORLD, LDA</td>", body);
        Assert.Contains($"<b>Montante agendado (pedido):</b></td><td style='padding:6px 0;'>{400758.34m:N2} AOA</td>", body);
        Assert.DoesNotContain("MULTI BIZ", body); Assert.DoesNotContain("USD VENDOR", body); Assert.DoesNotContain("grupo P.O.", body);
    }

    [Fact]
    public async Task Payment_notice_for_a_legacy_group_without_supplier_or_currency_falls_back_to_the_request_supplier_and_currency()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var legacy = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.Request.Id, SupplierId = null, SupplierNameSnapshot = null, CurrencyCode = null, TotalAmount = 400758.34m, Status = RequestConstants.Statuses.PaymentScheduled, CreatedByUserId = s.Buyer };
        var pay = new RequestPayment { RequestId = s.Request.Id, RequestPoGroupId = legacy.Id, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = 1, PlannedAmount = 400758.34m, CurrencyCode = "---", ScheduledDateUtc = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, CreatedByUserId = s.Buyer, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestPoGroups.Add(legacy); ctx.RequestPayments.Add(pay); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (o, _, email) = Build(ctx);

        await o.EmitAsync(PaymentEvent(s.Request, WorkflowEventCodes.PaymentScheduled, Guid.NewGuid(), legacy.Id, pay.Id));

        var body = Assert.Single(ApMails(email)).Body;
        Assert.Contains("<b>Fornecedor:</b></td><td style='padding:6px 0;'>IP WORLD, LDA</td>", body);      // request supplier (FinanceGroupDisplayResolver fall-through)
        Assert.Contains($"<b>Montante agendado (grupo P.O.):</b></td><td style='padding:6px 0;'>{400758.34m:N2} AOA</td>", body); // "---" payment currency → request currency
        Assert.Contains("15/10/2026", body);
        Assert.DoesNotContain("\u2014</td>", body);
    }

    [Fact]
    public async Task Ap_switches_do_not_affect_payment_events_and_scheduling_toggle_still_governs()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, notifyOnPoRegistered: true, notifyFinanceByEmail: true, notifyOnScheduled: false);
        var (o, _, email) = Build(ctx);
        await o.EmitAsync(PaymentEvent(s.Request, WorkflowEventCodes.PaymentScheduled, Guid.NewGuid()));
        VerifyApSend(email, Times.Never());                                                              // NotifyOnScheduled=false still governs
        var outbox = await ctx.EmailOutbox.ToListAsync();
        Assert.DoesNotContain(outbox, x => x.RecipientEmail.StartsWith("finance."));                     // Finance switch never adds Finance mail to payment events
        Assert.Contains(outbox, x => x.RecipientEmail == "buyer@test.local");                             // requester routing unchanged
    }

    [Fact]
    public async Task Ap_send_failure_is_logged_not_retried_and_does_not_block_finance_outbox_rows()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, notifyOnPoRegistered: true, notifyFinanceByEmail: true);
        var (o, _, email) = Build(ctx, smtpAccepts: false);
        await o.EmitAsync(PoEvent(s.Request, Guid.NewGuid()));
        var log = await ctx.AccountsPayableNotificationLogs.SingleAsync();
        Assert.False(log.Success); Assert.Contains("returned false", log.ErrorMessage);
        Assert.Equal(2, await ctx.EmailOutbox.CountAsync()); // individual Finance rows are queued regardless (outbox has its own retries)
        VerifyApSend(email, Times.Once());                   // exactly one direct attempt; no retry loop
    }
}
