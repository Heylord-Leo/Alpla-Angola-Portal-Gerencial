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

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// §8 BUYER_TRANSFERRED recipient matrix and content through the real orchestrator + real in-app service (InMemory):
/// previous/new Buyer with role-specific wording, requester, persisted participant pointers, actors of explicit
/// history actions only, pending area managers through the routing resolver, pending final nominee through the
/// final-recipient resolver, active-user and e-mail rules, dedup by user and by normalized e-mail, outbox rows keyed by
/// the correlation id (no duplicates on re-emit), and the BUYER_ASSIGNED wording for claim vs coordinator assignment.
/// </summary>
public class BuyerTransferNotificationTests
{
    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    private static (WorkflowNotificationOrchestrator Orchestrator, Mock<IApprovalRoutingService> Routing, Mock<IEmailService> Email) Build(ApplicationDbContext ctx)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new ApprovalRoutingResultDto());
        routing.Setup(r => r.ResolveFinalNotificationRecipientsAsync(It.IsAny<Guid?>(), It.IsAny<int>())).ReturnsAsync(new FinalNotificationRecipientsDto());
        var email = new Mock<IEmailService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var orchestrator = new WorkflowNotificationOrchestrator(ctx, new NotificationService(ctx, NullLogger<NotificationService>.Instance), email.Object, config,
            NullLogger<WorkflowNotificationOrchestrator>.Instance, AdminLog().Object, routing.Object);
        return (orchestrator, routing, email);
    }

    private sealed class Seed
    {
        public Guid RequestId, Prev, New, Requester, AreaApprover, FinalNominee, GroupOwner, ReceiptCompleter, Scheduler, Payer, WinnerSelector, ReworkRequester, ReuseAuthoriser,
            BatchActor, ObservationActor, InactivePointer, NoEmailPointer, SharedEmailA, SharedEmailB, Batch1, Batch2;
    }

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string batch1Status = "WAITING_AREA_APPROVAL", string batch2Status = "WAITING_FINAL_APPROVAL", string requestStatus = "WAITING_QUOTATION")
    {
        var s = new Seed();
        foreach (var f in typeof(Seed).GetFields()) f.SetValue(s, Guid.NewGuid());
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.AddRange(new RequestStatus { Id = 1, Code = requestStatus, Name = requestStatus });
        ctx.Companies.Add(new Company { Id = 1, Name = "Alpla", IsActive = true });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 3", CompanyId = 1 });
        ctx.Set<Department>().Add(new Department { Id = 5, Name = "Manutenção", IsActive = true });
        User U(Guid id, string name, string? email, bool active = true) => new() { Id = id, FullName = name, Email = email ?? "", IsActive = active };
        ctx.Users.AddRange(
            U(s.Prev, "Samuel Mpoco", "samuel@test.local"), U(s.New, "Leonardo Cintra", "leonardo@test.local"), U(s.Requester, "Requisitante", "req@test.local"),
            U(s.AreaApprover, "Aprovador Área", "area@test.local"), U(s.FinalNominee, "Aprovador Final", "final@test.local"), U(s.GroupOwner, "Dono P.O", "po@test.local"),
            U(s.ReceiptCompleter, "Receção", "receiving@test.local"), U(s.Scheduler, "Finanças Agenda", "fin1@test.local"), U(s.Payer, "Finanças Paga", "fin2@test.local"),
            U(s.WinnerSelector, "Selecionador", "winner@test.local"), U(s.ReworkRequester, "Reajuste", "rework@test.local"), U(s.ReuseAuthoriser, "Reuso", "reuse@test.local"),
            U(s.BatchActor, "Ator Lote", "batch@test.local"), U(s.ObservationActor, "Observador", "obs@test.local"),
            U(s.InactivePointer, "Inativo", "inactive@test.local", active: false), U(s.NoEmailPointer, "Sem Email", null),
            U(s.SharedEmailA, "Conta A", "Shared@Test.Local"), U(s.SharedEmailB, "Conta B", "shared@test.local "));
        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = "REQ-20/08/2026-271", Title = "Peças", RequestTypeId = 1, StatusId = 1, RequesterId = s.Requester, BuyerId = s.New,
            AreaApproverId = s.AreaApprover, FinalApproverId = s.FinalNominee, DepartmentId = 5, CompanyId = 1, PlantId = 1, EstimatedTotalAmount = 0m, CreatedAtUtc = DateTime.UtcNow.AddDays(-3), UpdatedAtUtc = DateTime.UtcNow
        });
        ctx.ApprovalBatches.AddRange(
            new ApprovalBatch { Id = s.Batch1, RequestId = s.RequestId, BatchNumber = 1, Status = batch1Status, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedByUserId = s.Prev },
            new ApprovalBatch { Id = s.Batch2, RequestId = s.RequestId, BatchNumber = 2, Status = batch2Status, CreatedAtUtc = DateTime.UtcNow.AddDays(-1), CreatedByUserId = s.Prev });
        var lineId = Guid.NewGuid();
        ctx.RequestLineItems.Add(new RequestLineItem { Id = lineId, RequestId = s.RequestId, LineNumber = 1, Description = "Peça", Quantity = 1, CreatedAtUtc = DateTime.UtcNow });
        ctx.ApprovalBatchItems.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = s.Batch1, RequestLineItemId = lineId, CreatedAtUtc = DateTime.UtcNow, WinnerSelectedByUserId = s.WinnerSelector });
        ctx.ApprovalBatchAdjustments.Add(new ApprovalBatchAdjustment { Id = Guid.NewGuid(), ApprovalBatchId = s.Batch1, CycleNumber = 1, SourceStage = "AREA", Status = "OPEN", ApproverComment = "rever", RequestedByUserId = s.ReworkRequester, RequestedAtUtc = DateTime.UtcNow });
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.RequestId, Status = RequestConstants.PoGroupStatuses.WaitingPo, PoResponsibleBuyerId = s.GroupOwner, OperationalReceiptCompletedByUserId = s.ReceiptCompleter, FiscalReceiptUploadedByUserId = s.InactivePointer, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Prev });
        ctx.RequestPayments.Add(new RequestPayment { Id = 1, RequestId = s.RequestId, ScheduledByUserId = s.Scheduler, PaidByUserId = s.Payer, CreatedByUserId = s.Prev, CreatedAtUtc = DateTime.UtcNow });
        ctx.QuotationReuseAuthorizations.Add(new QuotationReuseAuthorization { Id = Guid.NewGuid(), RequestId = s.RequestId, QuotationId = Guid.NewGuid(), QuotationItemId = Guid.NewGuid(), SourceApprovalBatchId = s.Batch1, AuthorizedByUserId = s.ReuseAuthoriser, AuthorizedAtUtc = DateTime.UtcNow });
        ctx.RequestStatusHistories.AddRange(
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.BatchActor, ActionTaken = "BATCH_AREA_APPROVED", NewStatusId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.ObservationActor, ActionTaken = "OBSERVACAO", NewStatusId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.NoEmailPointer, ActionTaken = "CONFIRM_RECEIVING", NewStatusId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.SharedEmailA, ActionTaken = "REGISTER_PO", NewStatusId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.SharedEmailB, ActionTaken = "PAYMENT_COMPLETED", NewStatusId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return s;
    }

    private static WorkflowEvent Transfer(Seed s, Guid correlationId, DateTime? at = null) => new()
    {
        EventCode = WorkflowEventCodes.BuyerTransferred, RequestId = s.RequestId, RequestNumber = "REQ-20/08/2026-271", RequestTitle = "Peças", TargetStatusCode = "WAITING_QUOTATION",
        ActionTaken = BuyerTakeOverPolicy.HistoryAction, ActorUserId = s.New, ActorName = "Leonardo Cintra", CorrelationId = correlationId, RequesterId = s.Requester, BuyerId = s.New,
        PreviousBuyerId = s.Prev, AreaApproverId = s.AreaApprover, FinalApproverId = s.FinalNominee, DepartmentId = 5, PlantId = 1, CompanyId = 1,
        Justification = "Samuel <ausente> & eu assumo", OccurredAtUtc = at ?? new DateTime(2026, 10, 22, 14, 5, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task Recipient_matrix_is_derived_from_persisted_relationships_with_role_specific_wording_and_dedup()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (orch, routing, email) = Build(ctx);
        var pendingManager = Guid.NewGuid(); ctx.Users.Add(new User { Id = pendingManager, FullName = "Gestor Pendente", Email = "pending-area@test.local", IsActive = true }); await ctx.SaveChangesAsync();
        routing.Setup(r => r.ResolveAreaManagersAsync(5, 1)).ReturnsAsync(new ApprovalRoutingResultDto { Managers = new List<AreaManagerDto> { new() { UserId = pendingManager, FullName = "Gestor Pendente", Email = "pending-area@test.local" } } });
        routing.Setup(r => r.ResolveFinalNotificationRecipientsAsync(s.FinalNominee, 1)).ReturnsAsync(new FinalNotificationRecipientsDto { Recipients = new List<FinalApproverDto> { new() { UserId = s.FinalNominee, FullName = "Aprovador Final", Email = "final@test.local" } } });
        var correlation = Guid.NewGuid();

        await orch.EmitAsync(Transfer(s, correlation));

        var outbox = await ctx.EmailOutbox.AsNoTracking().Where(e => e.RequestId == s.RequestId).ToListAsync();
        var expectedEmails = new[] { "samuel@test.local", "leonardo@test.local", "req@test.local", "area@test.local", "final@test.local", "po@test.local", "receiving@test.local",
            "fin1@test.local", "fin2@test.local", "winner@test.local", "rework@test.local", "reuse@test.local", "batch@test.local", "pending-area@test.local", "Shared@Test.Local" };
        Assert.Equal(expectedEmails.OrderBy(x => x), outbox.Select(o => o.RecipientEmail).OrderBy(x => x));   // one per mailbox: shared mailbox once, inactive and OBSERVACAO excluded
        Assert.All(outbox, o => { Assert.Equal(correlation, o.CorrelationId); Assert.Equal(WorkflowEventCodes.BuyerTransferred, o.EventCode); Assert.Equal("PENDING", o.Status); Assert.Contains($"/requests/{s.RequestId}", o.ActionUrl); });
        Assert.All(outbox, o => { Assert.Contains("Samuel Mpoco", o.BodyHtml); Assert.Contains("Leonardo Cintra", o.BodyHtml); Assert.Contains("Samuel &lt;ausente&gt; &amp; eu assumo", o.BodyHtml); Assert.Contains("22/10/2026 15:05 (Luanda, WAT UTC+1) — 14:05 UTC", o.BodyHtml); });
        Assert.StartsWith("Deixou de ser o comprador responsável", outbox.Single(o => o.RecipientEmail == "samuel@test.local").Subject);
        Assert.StartsWith("Passou a ser o comprador responsável", outbox.Single(o => o.RecipientEmail == "leonardo@test.local").Subject);
        Assert.StartsWith("Responsabilidade do pedido transferida", outbox.Single(o => o.RecipientEmail == "req@test.local").Subject);

        var inApp = await ctx.InformationalNotifications.AsNoTracking().Where(n => n.EventCorrelationId == correlation).Select(n => n.UserId).ToListAsync();
        Assert.Contains(s.NoEmailPointer, inApp);    // no address: in-app only
        Assert.DoesNotContain(s.InactivePointer, inApp); Assert.DoesNotContain(s.ObservationActor, inApp);
        Assert.Contains(s.SharedEmailA, inApp); Assert.Contains(s.SharedEmailB, inApp);   // both accounts keep their in-app notice; the mailbox gets ONE e-mail
        Assert.Equal(expectedEmails.Length + 2, inApp.Distinct().Count());
        email.VerifyNoOtherCalls();   // nothing is sent synchronously — the outbox worker delivers later
        routing.Verify(r => r.ResolveAreaManagersAsync(5, 1), Times.Once); routing.Verify(r => r.ResolveFinalNotificationRecipientsAsync(s.FinalNominee, 1), Times.Once);
    }

    [Fact]
    public async Task In_app_persistence_failure_propagates_for_the_transfer_event_only()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, batch1Status: "APPROVED", batch2Status: "APPROVED");
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("in-app store unavailable"));
        var routing = new Mock<IApprovalRoutingService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var orch = new WorkflowNotificationOrchestrator(ctx, inApp.Object, new Mock<IEmailService>().Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, AdminLog().Object, routing.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orch.EmitAsync(Transfer(s, Guid.NewGuid())));   // §8: transactional event propagates

        var assigned = new WorkflowEvent
        {
            EventCode = WorkflowEventCodes.BuyerAssigned, RequestId = s.RequestId, RequestNumber = "REQ-20/08/2026-271", RequestTitle = "Peças", TargetStatusCode = "WAITING_QUOTATION",
            ActionTaken = "BUYER_ASSIGNED", ActorUserId = s.New, ActorName = "Leonardo Cintra", CorrelationId = Guid.NewGuid(), RequesterId = s.Requester, BuyerId = s.New, DepartmentId = 5, PlantId = 1
        };
        await orch.EmitAsync(assigned);   // unchanged policy for other events: logged, outbox still queued
        Assert.True(await ctx.EmailOutbox.AnyAsync(e => e.CorrelationId == assigned.CorrelationId));
    }

    [Fact]
    public async Task Pending_resolvers_are_consulted_only_while_a_decision_is_pending()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, batch1Status: "APPROVED", batch2Status: "REJECTED");
        var (orch, routing, _) = Build(ctx);
        await orch.EmitAsync(Transfer(s, Guid.NewGuid()));
        routing.Verify(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
        routing.Verify(r => r.ResolveFinalNotificationRecipientsAsync(It.IsAny<Guid?>(), It.IsAny<int>()), Times.Never);
        Assert.True(await ctx.EmailOutbox.AnyAsync(e => e.RecipientEmail == "final@test.local"));   // still included: persisted FinalApproverId pointer, not the pending rule
    }

    [Fact]
    public async Task Re_emitting_the_same_correlation_creates_no_duplicate_outbox_or_in_app_rows()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, batch1Status: "APPROVED", batch2Status: "APPROVED");
        var (orch, _, _) = Build(ctx);
        var correlation = Guid.NewGuid();
        await orch.EmitAsync(Transfer(s, correlation));
        var outboxBefore = await ctx.EmailOutbox.CountAsync(e => e.CorrelationId == correlation);
        var inAppBefore = await ctx.InformationalNotifications.CountAsync(n => n.EventCorrelationId == correlation);
        await orch.EmitAsync(Transfer(s, correlation));
        Assert.Equal(outboxBefore, await ctx.EmailOutbox.CountAsync(e => e.CorrelationId == correlation));
        Assert.Equal(inAppBefore, await ctx.InformationalNotifications.CountAsync(n => n.EventCorrelationId == correlation));
    }

    [Fact]
    public async Task Buyer_assigned_wording_distinguishes_self_claim_coordinator_assignment_and_po_correction()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, batch1Status: "APPROVED", batch2Status: "APPROVED");
        var (orch, _, _) = Build(ctx);
        WorkflowEvent Assigned(Guid actor, Guid buyer, Guid? poGroup, Guid correlation) => new()
        {
            EventCode = WorkflowEventCodes.BuyerAssigned, RequestId = s.RequestId, RequestNumber = "REQ-20/08/2026-271", RequestTitle = "Peças", TargetStatusCode = "WAITING_QUOTATION",
            ActionTaken = "BUYER_ASSIGNED", ActorUserId = actor, ActorName = actor == s.New ? "Leonardo Cintra" : "Admin", CorrelationId = correlation, RequesterId = s.Requester, BuyerId = buyer, DepartmentId = 5, PlantId = 1, PoGroupId = poGroup
        };
        var c1 = Guid.NewGuid(); await orch.EmitAsync(Assigned(s.New, s.New, null, c1));
        var self = await ctx.InformationalNotifications.AsNoTracking().FirstAsync(n => n.EventCorrelationId == c1 && n.UserId == s.Requester);
        Assert.Contains("assumiu o pedido de cotação", self.Message);
        var c2 = Guid.NewGuid(); await orch.EmitAsync(Assigned(s.Prev /* acts as coordinator here */, s.New, null, c2));
        var coord = await ctx.InformationalNotifications.AsNoTracking().FirstAsync(n => n.EventCorrelationId == c2 && n.UserId == s.Requester);
        Assert.Contains("foi atribuído(a) ao comprador Leonardo Cintra por", coord.Message);
        Assert.Contains("foi atribuído(a) ao comprador", (await ctx.EmailOutbox.AsNoTracking().FirstAsync(e => e.CorrelationId == c2 && e.RecipientEmail == "req@test.local")).BodyHtml);
        var c3 = Guid.NewGuid(); await orch.EmitAsync(Assigned(s.New, s.New, Guid.NewGuid(), c3));
        Assert.Contains("correção da P.O do pedido", (await ctx.InformationalNotifications.AsNoTracking().FirstAsync(n => n.EventCorrelationId == c3 && n.UserId == s.Requester)).Message);
        // Exactly ONE in-app notice per recipient per correlation — the former direct controller notices are gone.
        Assert.Equal(1, await ctx.InformationalNotifications.CountAsync(n => n.EventCorrelationId == c1 && n.UserId == s.Requester));
    }
}
