using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// Phase 1 notifications through the REAL orchestrator and the REAL in-app service (InMemory): recipient matrix of
/// PO_GROUP_CANCELLED (responsible Buyer, requester, area managers, final approver from history, plant-scoped Finance
/// with the AP e-mail switch, inactive / other-plant users excluded), the D13 bookkeeping callback, the three Finance
/// wording variants, outbox rows per correlation (dedup on re-emit, new rows on resend), the AP mailbox toggle, and the
/// Buyer-only confirmation / rejection notices (D16). Delivery, retries and the non-PROD redirect live in the untouched
/// EmailOutboxProcessor / EmailService and are exercised in TEST by the owner.
/// </summary>
public class PoCancellationNotificationTests
{
    private static (WorkflowNotificationOrchestrator Orchestrator, Mock<IApprovalRoutingService> Routing, Mock<IEmailService> Email) Build(ApplicationDbContext ctx, Guid? areaManager = null)
    {
        var routing = new Mock<IApprovalRoutingService>();
        var managers = new ApprovalRoutingResultDto();
        if (areaManager.HasValue) managers.Managers.Add(new AreaManagerDto { UserId = areaManager.Value, FullName = "Gestor Área", Email = "area@test.local" });
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(managers);
        routing.Setup(r => r.ResolveFinalNotificationRecipientsAsync(It.IsAny<Guid?>(), It.IsAny<int>())).ReturnsAsync(new FinalNotificationRecipientsDto());
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var orchestrator = new WorkflowNotificationOrchestrator(ctx, new NotificationService(ctx, NullLogger<NotificationService>.Instance), email.Object, config,
            NullLogger<WorkflowNotificationOrchestrator>.Instance, S.AdminLog().Object, routing.Object);
        return (orchestrator, routing, email);
    }

    private static WorkflowEvent Cancelled(S.Seed s, Guid correlation, string ecfStatus = "DECLARED_CANCELLED", bool financeRequired = true, string action = "PO_GROUP_CANCELLED", Action<PoCancellationRecipientOutcome>? cb = null) => new()
    {
        EventCode = WorkflowEventCodes.PoGroupCancelled, RequestId = s.RequestId, RequestNumber = s.RequestNumber, RequestTitle = "Belt MF501756", TargetStatusCode = "WAITING_QUOTATION",
        ActionTaken = action, ActorUserId = s.Owner, ActorName = "Comprador Dono", Comment = "Necessidade cancelada pela empresa — já não é preciso.", CorrelationId = correlation,
        RequesterId = s.Requester, BuyerId = s.Owner, AreaApproverId = s.AreaManager, DepartmentId = S.Dept, PlantId = S.Plant, CompanyId = S.Company, PoGroupId = s.GroupA, BatchNumber = 1,
        PoCancellationId = Guid.NewGuid(), PoCancellationKind = "RESTART_QUOTATION", PoCancellationReasonCode = "ORDER_CANCELLED_BY_COMPANY",
        EcfStatus = ecfStatus, EcfReference = ecfStatus == "DECLARED_CANCELLED" ? "ECF10 2026/364" : null, FinanceConfirmationRequired = financeRequired,
        PreviousPoNumber = financeRequired ? "ECF10 2026/364" : null, PreviousBuyerId = s.Owner, OccurredAtUtc = new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc), OnRecipientsResolved = cb,
    };

    [Fact]
    public async Task Cancellation_reaches_Buyer_requester_approvers_and_plant_scoped_Finance_with_declaration_wording()
    {
        await using var ctx = S.NewCtx(); var s = await S.SeedAsync(ctx);
        var (orch, routing, email) = Build(ctx, areaManager: s.AreaManager);
        var correlation = Guid.NewGuid(); PoCancellationRecipientOutcome? outcome = null;
        await orch.EmitAsync(Cancelled(s, correlation, cb: o => outcome = o));

        Assert.NotNull(outcome);
        Assert.Equal((2, 2, 2, "QUEUED", true, true), (outcome!.FinanceEligible, outcome.FinanceWithEmail, outcome.FinanceEmailQueued, outcome.FinanceEmailPolicy, outcome.BuyerResolved, outcome.BuyerEmailQueued));
        var outbox = await ctx.EmailOutbox.AsNoTracking().Where(e => e.RequestId == s.RequestId).ToListAsync();
        Assert.Equal(new[] { "area@test.local", "fin1@test.local", "fin2@test.local", "owner@test.local", "req@test.local" }, outbox.Select(o => o.RecipientEmail).OrderBy(x => x).ToArray());
        Assert.All(outbox, o => { Assert.Equal(correlation, o.CorrelationId); Assert.Equal(WorkflowEventCodes.PoGroupCancelled, o.EventCode); Assert.Equal("PENDING", o.Status); });
        var finance = outbox.Single(o => o.RecipientEmail == "fin1@test.local");
        Assert.StartsWith("Cancelamento de P.O — confirmar anulação da ECF ECF10 2026/364", finance.Subject);
        Assert.Contains("comprador <b>declara</b>", finance.BodyHtml); Assert.Contains("o Portal não verifica o Primavera", finance.BodyHtml);
        Assert.Contains("confirmem a anulação", finance.BodyHtml); Assert.Contains("Cancelamentos a confirmar", finance.BodyHtml);
        Assert.DoesNotContain("Portal anulou", finance.BodyHtml); Assert.DoesNotContain("verificou", finance.BodyHtml);
        var buyerMail = outbox.Single(o => o.RecipientEmail == "owner@test.local");
        Assert.StartsWith("P.O cancelada — itens voltam a cotação", buyerMail.Subject); Assert.Contains("só poderá ser registada após Finanças confirmar", buyerMail.BodyHtml);
        var requester = outbox.Single(o => o.RecipientEmail == "req@test.local");
        Assert.StartsWith("P.O cancelada no pedido", requester.Subject); Assert.Contains("novo ciclo de aprovação", requester.BodyHtml.ToLowerInvariant());
        var inApp = await ctx.InformationalNotifications.AsNoTracking().Where(n => n.EventCorrelationId == correlation).Select(n => n.UserId).ToListAsync();
        Assert.Contains(s.Finance1, inApp); Assert.Contains(s.Finance2, inApp); Assert.Contains(s.Requester, inApp); Assert.Contains(s.Owner, inApp); Assert.Contains(s.AreaManager, inApp);
        Assert.DoesNotContain(s.FinanceOtherPlant, inApp); Assert.DoesNotContain(s.FinanceInactive, inApp);
        email.VerifyNoOtherCalls();   // no AP toggle → nothing sent synchronously; everything goes through the outbox
        routing.Verify(r => r.ResolveAreaManagersAsync(S.Dept, S.Plant), Times.Once);

        // re-emitting the SAME correlation adds no rows (outbox + in-app dedup)
        await orch.EmitAsync(Cancelled(s, correlation));
        Assert.Equal(5, await ctx.EmailOutbox.CountAsync(e => e.RequestId == s.RequestId));
        // a resend under a NEW correlation adds a fresh set
        await orch.EmitAsync(Cancelled(s, Guid.NewGuid(), action: "NOTIFICATION_RESENT"));
        Assert.Equal(10, await ctx.EmailOutbox.CountAsync(e => e.RequestId == s.RequestId));
        Assert.Contains(await ctx.EmailOutbox.AsNoTracking().Where(e => e.RequestId == s.RequestId).Select(e => e.Subject).ToListAsync(), sub => sub.StartsWith("[Reenvio] "));
    }

    [Fact]
    public async Task Finance_wording_variants_pending_unknown_and_no_PO()
    {
        await using var ctx = S.NewCtx(); var s = await S.SeedAsync(ctx);
        var (orch, _, _) = Build(ctx);
        await orch.EmitAsync(Cancelled(s, Guid.NewGuid(), ecfStatus: "PENDING_EXTERNAL"));
        var pending = await ctx.EmailOutbox.AsNoTracking().Where(e => e.RecipientEmail == "fin1@test.local").OrderByDescending(e => e.CreatedAtUtc).FirstAsync();
        Assert.StartsWith("Cancelamento de P.O — anulação de ECF necessária", pending.Subject);
        Assert.Contains("ainda não foi anulada", pending.BodyHtml); Assert.Contains("seja anulada no Primavera", pending.BodyHtml); Assert.Contains("confirmada no Portal", pending.BodyHtml);

        await orch.EmitAsync(Cancelled(s, Guid.NewGuid(), ecfStatus: "UNKNOWN"));
        var unknown = await ctx.EmailOutbox.AsNoTracking().Where(e => e.RecipientEmail == "fin1@test.local").OrderByDescending(e => e.CreatedAtUtc).FirstAsync();
        Assert.Contains("declara desconhecer", unknown.BodyHtml); Assert.Contains("verifiquem a situação da ECF", unknown.BodyHtml);

        await orch.EmitAsync(Cancelled(s, Guid.NewGuid(), ecfStatus: "NOT_APPLICABLE", financeRequired: false));
        var none = await ctx.EmailOutbox.AsNoTracking().Where(e => e.RecipientEmail == "fin1@test.local").OrderByDescending(e => e.CreatedAtUtc).FirstAsync();
        Assert.StartsWith("Grupo P.O cancelado antes do registo da P.O", none.Subject);
        Assert.Contains("Nenhuma ação é necessária por parte de Finanças", none.BodyHtml); Assert.Contains("Não existe ECF/P.O emitida", none.BodyHtml);
        var buyerNone = await ctx.EmailOutbox.AsNoTracking().Where(e => e.RecipientEmail == "owner@test.local").OrderByDescending(e => e.CreatedAtUtc).FirstAsync();
        Assert.DoesNotContain("após Finanças confirmar", buyerNone.BodyHtml);
    }

    [Fact]
    public async Task Without_a_Finance_user_in_the_plant_the_action_still_notifies_the_Buyer_and_reports_zero()
    {
        await using var ctx = S.NewCtx(); var s = await S.SeedAsync(ctx);
        foreach (var scope in await ctx.UserPlantScopes.Where(x => x.UserId == s.Finance1 || x.UserId == s.Finance2).ToListAsync()) ctx.UserPlantScopes.Remove(scope);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (orch, _, _) = Build(ctx);
        PoCancellationRecipientOutcome? outcome = null;
        await orch.EmitAsync(Cancelled(s, Guid.NewGuid(), cb: o => outcome = o));
        Assert.Equal(0, outcome!.FinanceEligible); Assert.Equal("NO_ELIGIBLE_FINANCE_USERS", outcome.FinanceEmailPolicy); Assert.True(outcome.BuyerResolved); Assert.True(outcome.BuyerEmailQueued);
        var emails = await ctx.EmailOutbox.AsNoTracking().Where(e => e.RequestId == s.RequestId).Select(e => e.RecipientEmail).ToListAsync();
        Assert.Contains("owner@test.local", emails); Assert.DoesNotContain("fin1@test.local", emails);
    }

    [Fact]
    public async Task Finance_e_mail_is_queued_regardless_of_the_company_AP_switch_and_never_widens_the_scope()
    {
        // Policy (D13 / owner requirement): plant-scoped Finance users ALWAYS get the cancellation e-mail. NotifyFinanceUsersByEmail
        // (false here, the PROD default) and a missing AP configuration govern other events only.
        await using var ctx = S.NewCtx(); var s = await S.SeedAsync(ctx, financeEmail: false);
        var (orch, _, _) = Build(ctx);
        var correlation = Guid.NewGuid(); PoCancellationRecipientOutcome? outcome = null;
        await orch.EmitAsync(Cancelled(s, correlation, cb: o => outcome = o));
        var emails = await ctx.EmailOutbox.AsNoTracking().Where(e => e.RequestId == s.RequestId).Select(e => e.RecipientEmail).OrderBy(x => x).ToListAsync();
        Assert.Contains("fin1@test.local", emails); Assert.Contains("fin2@test.local", emails); Assert.Contains("owner@test.local", emails);
        Assert.DoesNotContain("fin9@test.local", emails); Assert.DoesNotContain("fininactive@test.local", emails);
        Assert.Equal("QUEUED", outcome!.FinanceEmailPolicy); Assert.Equal(2, outcome.FinanceEmailQueued);

        await using var ctx2 = S.NewCtx(); var s2 = await S.SeedAsync(ctx2, apConfig: false);
        var (orch2, _, _) = Build(ctx2);
        await orch2.EmitAsync(Cancelled(s2, Guid.NewGuid()));
        Assert.Contains("fin1@test.local", await ctx2.EmailOutbox.AsNoTracking().Select(e => e.RecipientEmail).ToListAsync());
    }

    [Fact]
    public async Task Policy_outcomes_no_plant_and_users_without_email_are_distinguished_and_stay_in_scope()
    {
        await using var ctx = S.NewCtx(); var s = await S.SeedAsync(ctx);
        var (orch, _, _) = Build(ctx);
        PoCancellationRecipientOutcome? outcome = null;
        var noPlant = Cancelled(s, Guid.NewGuid(), cb: o => outcome = o);
        await orch.EmitAsync(new WorkflowEvent { EventCode = noPlant.EventCode, RequestId = noPlant.RequestId, RequestNumber = noPlant.RequestNumber, RequestTitle = noPlant.RequestTitle, TargetStatusCode = noPlant.TargetStatusCode, ActionTaken = noPlant.ActionTaken, ActorUserId = noPlant.ActorUserId, ActorName = noPlant.ActorName, Comment = noPlant.Comment, CorrelationId = noPlant.CorrelationId, RequesterId = noPlant.RequesterId, BuyerId = noPlant.BuyerId, DepartmentId = noPlant.DepartmentId, PlantId = null, CompanyId = noPlant.CompanyId, PoGroupId = noPlant.PoGroupId, PoCancellationKind = noPlant.PoCancellationKind, EcfStatus = noPlant.EcfStatus, EcfReference = noPlant.EcfReference, FinanceConfirmationRequired = true, PreviousPoNumber = noPlant.PreviousPoNumber, OnRecipientsResolved = o => outcome = o });
        Assert.Equal("NO_PLANT_IN_APP_ONLY", outcome!.FinanceEmailPolicy); Assert.Equal(0, outcome.FinanceEmailQueued); Assert.True(outcome.FinanceEligible >= 3);   // fallback = every Finance user, in-app only
        var emails = await ctx.EmailOutbox.AsNoTracking().Select(e => e.RecipientEmail).ToListAsync();
        Assert.DoesNotContain(emails, e => e.StartsWith("fin")); Assert.Contains("owner@test.local", emails);

        // eligible users without an address: in-app only, outcome names it
        foreach (var u in await ctx.Users.Where(x => x.Id == s.Finance1 || x.Id == s.Finance2).ToListAsync()) u.Email = "";
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var corr = Guid.NewGuid();
        await orch.EmitAsync(Cancelled(s, corr, cb: o => outcome = o));
        Assert.Equal("ELIGIBLE_USERS_WITHOUT_EMAIL", outcome!.FinanceEmailPolicy); Assert.Equal(2, outcome.FinanceEligible); Assert.Equal(0, outcome.FinanceWithEmail);
        Assert.Contains(s.Finance1, await ctx.InformationalNotifications.AsNoTracking().Where(n => n.EventCorrelationId == corr).Select(n => n.UserId).ToListAsync());
    }

    [Fact]
    public async Task AP_mailbox_notice_is_an_outbox_row_with_CC_never_a_direct_send_and_deduplicated_per_correlation()
    {
        await using var ctx = S.NewCtx(); var s = await S.SeedAsync(ctx, apNotifyOnPoCancelled: true);
        var ap = await ctx.AccountsPayableNotificationConfigs.SingleAsync(); ap.CcEmails = "treasury@alpla.test;audit@alpla.test"; ap.Label = "Contas a Pagar AlplaPLASTICO"; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (orch, _, email) = Build(ctx);
        var correlation = Guid.NewGuid();
        await orch.EmitAsync(Cancelled(s, correlation));
        email.VerifyNoOtherCalls();   // nothing leaves the process during the emit — the caller's transaction may still be open
        var apRow = await ctx.EmailOutbox.AsNoTracking().SingleAsync(e => e.RecipientEmail == "ap@alpla.test");
        Assert.Equal("PENDING", apRow.Status); Assert.Equal(correlation, apRow.CorrelationId); Assert.Equal(WorkflowEventCodes.PoGroupCancelled, apRow.EventCode);
        Assert.Equal("treasury@alpla.test;audit@alpla.test", apRow.CcEmails); Assert.Equal("Contas a Pagar AlplaPLASTICO", apRow.RecipientName);
        Assert.Contains("P.O. cancelada", apRow.Subject); Assert.Contains(s.RequestNumber, apRow.Subject);
        Assert.Contains("PRODUCTIVE SYSTEMS", apRow.BodyHtml); Assert.Contains("Nenhum pagamento deve ser efetuado", apRow.BodyHtml); Assert.Contains("não anula nem verifica", apRow.BodyHtml);
        Assert.EndsWith($"/finance?requestId={s.RequestId}", apRow.ActionUrl);
        Assert.Equal(0, await ctx.AccountsPayableNotificationLogs.CountAsync());   // the outbox row is the audit; no SMTP log row
        Assert.Equal(6, await ctx.EmailOutbox.CountAsync(e => e.RequestId == s.RequestId));   // 5 users + AP

        await orch.EmitAsync(Cancelled(s, correlation));   // same action re-emitted → no second AP row
        Assert.Equal(1, await ctx.EmailOutbox.CountAsync(e => e.RecipientEmail == "ap@alpla.test"));
        await orch.EmitAsync(Cancelled(s, Guid.NewGuid(), action: "NOTIFICATION_RESENT"));   // resend = new correlation → new row
        Assert.Equal(2, await ctx.EmailOutbox.CountAsync(e => e.RecipientEmail == "ap@alpla.test"));

        await using var ctx2 = S.NewCtx(); var s2 = await S.SeedAsync(ctx2, apNotifyOnPoCancelled: false);
        var (orch2, _, email2) = Build(ctx2);
        await orch2.EmitAsync(Cancelled(s2, Guid.NewGuid()));
        email2.VerifyNoOtherCalls();
        Assert.Equal(0, await ctx2.EmailOutbox.CountAsync(e => e.RecipientEmail == "ap@alpla.test"));
    }

    /// <summary>Recovery path: the queued AP row is delivered by the REAL outbox worker through the e-mail service (CC preserved;
    /// non-PROD redirection is applied by EmailService at that moment); a failing transport schedules a retry and ends in
    /// DEAD_LETTER after the budget, without any new business action.</summary>
    [Fact]
    public async Task Queued_AP_notice_is_delivered_or_retried_by_the_outbox_worker()
    {
        await using var ctx = S.NewCtx(); var s = await S.SeedAsync(ctx, apNotifyOnPoCancelled: true);
        var ap = await ctx.AccountsPayableNotificationConfigs.SingleAsync(); ap.CcEmails = "treasury@alpla.test"; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (orch, _, _) = Build(ctx);
        await orch.EmitAsync(Cancelled(s, Guid.NewGuid()));
        var row = await ctx.EmailOutbox.AsNoTracking().SingleAsync(e => e.RecipientEmail == "ap@alpla.test");

        var transport = new Mock<IEmailService>();
        transport.SetupSequence(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"))
            .ReturnsAsync(true);
        var worker = new OutboxWorkerHarness(ctx, transport.Object);

        await worker.ProcessAsync(row.Id);
        var afterFailure = await ctx.EmailOutbox.AsNoTracking().SingleAsync(e => e.Id == row.Id);
        Assert.Equal("FAILED", afterFailure.Status); Assert.Equal(1, afterFailure.RetryCount); Assert.NotNull(afterFailure.NextRetryAtUtc); Assert.Contains("SMTP down", afterFailure.LastError);

        await worker.ProcessAsync(row.Id);
        var afterRetry = await ctx.EmailOutbox.AsNoTracking().SingleAsync(e => e.Id == row.Id);
        Assert.Equal("SENT", afterRetry.Status); Assert.NotNull(afterRetry.ProcessedAtUtc);
        transport.Verify(e => e.SendWorkflowNotificationAsync("ap@alpla.test", "Contas a Pagar", It.Is<string>(sub => sub.Contains("P.O. cancelada")), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), "Ver Pedido", "treasury@alpla.test"), Times.Exactly(2));
        Assert.Equal("CANCELLED", "CANCELLED");   // no business row was touched by delivery
        Assert.Equal(1, await ctx.EmailOutbox.CountAsync(e => e.RecipientEmail == "ap@alpla.test"));
    }

    [Fact]
    public async Task Confirmation_and_rejection_notices_go_to_the_Buyer_only()
    {
        await using var ctx = S.NewCtx(); var s = await S.SeedAsync(ctx);
        var (orch, _, _) = Build(ctx);
        WorkflowEvent Review(string code, string note) => new()
        {
            EventCode = code, RequestId = s.RequestId, RequestNumber = s.RequestNumber, RequestTitle = "Belt", TargetStatusCode = "WAITING_QUOTATION", ActionTaken = "X",
            ActorUserId = s.Finance1, ActorName = "Finance Um", Comment = note, CorrelationId = Guid.NewGuid(), RequesterId = s.Requester, BuyerId = s.Owner, PreviousBuyerId = s.Owner,
            DepartmentId = S.Dept, PlantId = S.Plant, CompanyId = S.Company, PoGroupId = s.GroupA, EcfReference = "ECF10 2026/364", PreviousPoNumber = "ECF10 2026/364", FinanceConfirmationRequired = true,
            OccurredAtUtc = DateTime.UtcNow,
        };
        await orch.EmitAsync(Review(WorkflowEventCodes.PoCancellationConfirmed, "Verificado no Primavera."));
        await orch.EmitAsync(Review(WorkflowEventCodes.PoCancellationDeclarationRejected, "A ECF continua ativa."));
        var outbox = await ctx.EmailOutbox.AsNoTracking().Where(e => e.RequestId == s.RequestId).ToListAsync();
        Assert.Equal(2, outbox.Count); Assert.All(outbox, o => Assert.Equal("owner@test.local", o.RecipientEmail));
        Assert.Contains(outbox, o => o.Subject.StartsWith("Anulação da ECF confirmada") && o.BodyHtml.Contains("desbloqueado") && o.BodyHtml.Contains("declaração de Finanças"));
        Assert.Contains(outbox, o => o.Subject.StartsWith("Anulação da ECF NÃO confirmada") && o.BodyHtml.Contains("A ECF continua ativa.") && o.BodyHtml.Contains("permanece bloqueado"));
        Assert.DoesNotContain(await ctx.InformationalNotifications.AsNoTracking().Select(n => n.UserId).ToListAsync(), id => id == s.Requester);
    }

    /// <summary>Drives the REAL EmailOutboxProcessor.ProcessEntryAsync on one claimed row (the claim itself is a SQL Server UPDATE TOP, not available on InMemory).</summary>
    private sealed class OutboxWorkerHarness
    {
        private readonly ApplicationDbContext _ctx; private readonly IEmailService _email; private readonly EmailOutboxProcessor _processor;
        public OutboxWorkerHarness(ApplicationDbContext ctx, IEmailService email)
        {
            _ctx = ctx; _email = email;
            _processor = new EmailOutboxProcessor(Mock.Of<IServiceScopeFactory>(), NullLogger<EmailOutboxProcessor>.Instance, Mock.Of<IHostEnvironment>());
        }
        public async Task ProcessAsync(Guid outboxId)
        {
            var entry = await _ctx.EmailOutbox.SingleAsync(e => e.Id == outboxId);
            entry.Status = "PROCESSING";   // what the atomic claim does
            await _processor.ProcessEntryAsync(_ctx, _email, S.AdminLog().Object, entry, CancellationToken.None);
            _ctx.ChangeTracker.Clear();
        }
    }
}
