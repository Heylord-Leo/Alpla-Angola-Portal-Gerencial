using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Requests;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Regression for TEST REQ-08/10/2026-449: RegisterPo emitted PO_REGISTERED without the request context
/// (no PlantId, no RequestNumber, fresh GUID correlation). The orchestrator's Finance routing is
/// plant-scoped; with a null plant it falls back to a global fan-out that SUPPRESSES e-mail, so Finance
/// got seven in-app notifications and no outbox row. These tests run the real RequestsController.RegisterPo
/// against the REAL WorkflowNotificationOrchestrator on an InMemory context.
/// </summary>
public class RegisterPoFinanceNotificationTests
{
    private const int PlantA = 5, PlantB = 6;
    private const int S_APPROVED = 7, S_PO_ISSUED = 9, S_WAITING_PO_CORRECTION = 12, S_PO_PARTIAL = 13, S_ADVANCE = 14;

    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Seed
    {
        public Guid RequestId, GroupId, BuyerId, FinanceA1, FinanceA2, FinanceB;
        public string RequestNumber = string.Empty;
    }

    /// <summary>
    /// PAYMENT request APPROVED on plant A with one PENDING group (supplier set, P.O. attached). Finance: two users
    /// scoped to plant A, one scoped only to plant B. Company 1 has an active Accounts Payable configuration whose two
    /// opt-in switches are set by the caller (both default OFF in production).
    /// </summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, bool financeEmail = true, bool apOnPoRegistered = false, bool withApConfig = true)
    {
        if (withApConfig)
            ctx.AccountsPayableNotificationConfigs.Add(new AccountsPayableNotificationConfig { Id = 1, CompanyId = 1, Email = "alpla-plasticos-accounts@alpla.com", CcEmails = "aovia-treasury@alpla.com", IsActive = true, NotifyOnPoRegistered = apOnPoRegistered, NotifyFinanceUsersByEmail = financeEmail });
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaPLASTICO", IsActive = true });
        var s = new Seed { RequestId = Guid.NewGuid(), GroupId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), FinanceA1 = Guid.NewGuid(), FinanceA2 = Guid.NewGuid(), FinanceB = Guid.NewGuid(), RequestNumber = "REQ-TEST-PO-449" };
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_APPROVED, Code = RequestConstants.Statuses.FinalApproved, Name = "Aprovado" },
            new RequestStatus { Id = S_PO_ISSUED, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" },
            new RequestStatus { Id = S_WAITING_PO_CORRECTION, Code = RequestConstants.Statuses.WaitingPoCorrection, Name = "Correção P.O." },
            new RequestStatus { Id = S_PO_PARTIAL, Code = RequestConstants.Statuses.PoPartiallyUploaded, Name = "P.O. parcial" },
            new RequestStatus { Id = S_ADVANCE, Code = RequestConstants.Statuses.AdvancePaymentRequired, Name = "Adiantamento" });
        ctx.Plants.AddRange(new Plant { Id = PlantA, Name = "Planta A" }, new Plant { Id = PlantB, Name = "Planta B" });
        var finance = new Role { Id = 3, RoleName = RoleConstants.Finance };
        var buyerRole = new Role { Id = 4, RoleName = RoleConstants.Buyer };
        ctx.Roles.AddRange(finance, buyerRole);
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "IP WORLD, LDA", TaxId = "5000", RegistrationStatus = "ACTIVE" });
        var buyer = new User { Id = s.BuyerId, FullName = "Comprador Teste", Email = "buyer@test.local", IsActive = true };
        var fa1 = new User { Id = s.FinanceA1, FullName = "Finance A1", Email = "finance.a1@test.local", IsActive = true };
        var fa2 = new User { Id = s.FinanceA2, FullName = "Finance A2", Email = "finance.a2@test.local", IsActive = true };
        var fb = new User { Id = s.FinanceB, FullName = "Finance B", Email = "finance.b@test.local", IsActive = true };
        ctx.Users.AddRange(buyer, fa1, fa2, fb);
        ctx.UserRoleAssignments.AddRange(
            new UserRoleAssignment { UserId = buyer.Id, RoleId = buyerRole.Id },
            new UserRoleAssignment { UserId = fa1.Id, RoleId = finance.Id },
            new UserRoleAssignment { UserId = fa2.Id, RoleId = finance.Id },
            new UserRoleAssignment { UserId = fb.Id, RoleId = finance.Id });
        ctx.UserPlantScopes.AddRange(
            new UserPlantScope { UserId = fa1.Id, PlantId = PlantA },
            new UserPlantScope { UserId = fa2.Id, PlantId = PlantA },
            new UserPlantScope { UserId = fb.Id, PlantId = PlantB });

        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = s.RequestNumber, Title = "Serviços IP World", RequestTypeId = 2, StatusId = S_APPROVED,
            RequesterId = buyer.Id, BuyerId = buyer.Id, DepartmentId = 7, CompanyId = 1, PlantId = PlantA, SupplierId = 10,
            PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, EstimatedTotalAmount = 400758.34m, CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
        });
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = s.GroupId, RequestId = s.RequestId, SupplierId = 10, SupplierNameSnapshot = "IP WORLD, LDA", Status = RequestConstants.PoGroupStatuses.Pending,
            TotalAmount = 400758.34m, CurrencyCode = "AOA", CreatedByUserId = buyer.Id
        });
        ctx.RequestAttachments.Add(new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = s.RequestId, RequestPoGroupId = s.GroupId, AttachmentTypeCode = RequestAttachment.TYPE_PO,
            FileName = "po.pdf", StorageReference = "po.pdf", UploadedByUserId = buyer.Id, UploadedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return s;
    }

    private static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    /// <summary>Real orchestrator; in-app creation is captured through the INotificationService mock; e-mail is never sent (outbox only).</summary>
    private static (IWorkflowNotificationOrchestrator Orchestrator, Mock<INotificationService> InApp, Mock<IEmailService> Email) RealOrchestrator(ApplicationDbContext ctx)
    {
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        var email = new Mock<IEmailService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var orchestrator = new WorkflowNotificationOrchestrator(ctx, inApp.Object, email.Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, AdminLog().Object, new Mock<IApprovalRoutingService>().Object);
        return (orchestrator, inApp, email);
    }

    private static RequestsController BuildBuyerController(ApplicationDbContext ctx, Guid buyerId, IWorkflowNotificationOrchestrator orchestrator)
    {
        var options = new PostPaymentCompletionOptions();
        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            AdminLog().Object,
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            orchestrator,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object,
            new Mock<IApprovalRoutingService>().Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object,
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(options));
        var services = new ServiceCollection();
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, buyerId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    private static RegisterPoActionDto Dto(Guid groupId) => new() { PoGroupId = groupId, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, PurchaseOrderNumber = "ECF11 2026/Teste449" };

    [Fact]
    public async Task RegisterPo_queues_PO_REGISTERED_emails_for_plant_scoped_finance_users_and_keeps_in_app_notifications()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var (orchestrator, inApp, email) = RealOrchestrator(ctx);

        var result = await BuildBuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId));
        Assert.IsType<OkObjectResult>(result);

        // Persisted transition: REGISTER_PO history row; group and request PO_ISSUED
        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO");
        Assert.Equal(RequestConstants.Statuses.PoIssued, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
        Assert.Equal(S_PO_ISSUED, (await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).StatusId);

        // Outbox: exactly one PO_REGISTERED row per Finance user scoped to the request's plant, correlated to the history row,
        // carrying the request reference; the plant-B-only Finance user gets nothing.
        var outbox = await ctx.EmailOutbox.AsNoTracking().Where(o => o.RequestId == s.RequestId).ToListAsync();
        Assert.Equal(2, outbox.Count);
        Assert.All(outbox, o =>
        {
            Assert.Equal(WorkflowEventCodes.PoRegistered, o.EventCode);
            Assert.Equal(history.Id, o.CorrelationId);
            Assert.Equal(s.RequestNumber, o.RequestNumber);
            Assert.Contains(s.RequestNumber, o.Subject);                 // "Nova P.O para Processamento — REQ-…"
            Assert.Contains("Comprador Teste", o.BodyHtml);               // actor name resolved, not "Sistema"
            Assert.Equal("PENDING", o.Status);
        });
        Assert.Equal(new[] { "finance.a1@test.local", "finance.a2@test.local" }, outbox.Select(o => o.RecipientEmail).OrderBy(x => x).ToArray());
        Assert.DoesNotContain(outbox, o => o.RecipientEmail == "finance.b@test.local");

        // In-app notifications preserved for the same two recipients, same correlation
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(s.FinanceA1, "Nova P.O Registrada", It.Is<string>(m => m.Contains(s.RequestNumber)), It.IsAny<string>(), It.IsAny<string>(), history.Id, It.IsAny<string?>()), Times.Once);
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(s.FinanceA2, "Nova P.O Registrada", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), history.Id, It.IsAny<string?>()), Times.Once);
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(s.FinanceB, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);

        // No direct send: delivery is the outbox processor's job (and the environment policy applies there)
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Repeating_the_registration_on_an_issued_group_is_refused_and_queues_nothing_more()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var (orchestrator, _, _) = RealOrchestrator(ctx);
        Assert.IsType<OkObjectResult>(await BuildBuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId)));
        ctx.ChangeTracker.Clear();

        // PAYMENT path: the request is now PO_ISSUED, which is not an allowed status for register-po → 400 "Ação Inválida".
        var replay = await BuildBuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId));
        var bad = Assert.IsType<BadRequestObjectResult>(replay);
        Assert.Equal("Ação Inválida", Assert.IsType<ProblemDetails>(bad.Value).Title);

        Assert.Equal(2, await ctx.EmailOutbox.CountAsync(o => o.RequestId == s.RequestId));
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO"));
    }

    [Fact]
    public async Task Correction_re_registration_keeps_the_PO_REGISTERED_event_code_with_the_same_context()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        // Finance returned the P.O.: group and request in WAITING_PO_CORRECTION (allowed re-registration path)
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupId); g.Status = RequestConstants.Statuses.WaitingPoCorrection;
        var r = await ctx.Requests.SingleAsync(x => x.Id == s.RequestId); r.StatusId = S_WAITING_PO_CORRECTION;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (orchestrator, inApp, _) = RealOrchestrator(ctx);

        Assert.IsType<OkObjectResult>(await BuildBuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId)));

        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REREGISTER_PO");
        var outbox = await ctx.EmailOutbox.AsNoTracking().Where(o => o.RequestId == s.RequestId).ToListAsync();
        Assert.Equal(2, outbox.Count);
        // Event code deliberately unchanged by the fix (PO_REGISTERED for corrections too); the history row says REREGISTER_PO.
        Assert.All(outbox, o => { Assert.Equal(WorkflowEventCodes.PoRegistered, o.EventCode); Assert.Equal(history.Id, o.CorrelationId); Assert.Contains(s.RequestNumber, o.Subject); });
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), history.Id, It.IsAny<string?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Finance_switch_off_default_keeps_in_app_notifications_and_queues_no_finance_email()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, financeEmail: false);
        var (orchestrator, inApp, email) = RealOrchestrator(ctx);

        Assert.IsType<OkObjectResult>(await BuildBuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId)));

        Assert.Equal(0, await ctx.EmailOutbox.CountAsync(o => o.RequestId == s.RequestId));
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), "Nova P.O Registrada", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Exactly(2));
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task No_company_configuration_keeps_in_app_notifications_and_queues_no_finance_email()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, withApConfig: false);
        var (orchestrator, inApp, _) = RealOrchestrator(ctx);
        Assert.IsType<OkObjectResult>(await BuildBuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId)));
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync(o => o.RequestId == s.RequestId));
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), "Nova P.O Registrada", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Ap_switch_on_sends_the_review_notice_to_the_company_ap_address_once_per_registration()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, financeEmail: false, apOnPoRegistered: true);
        var (orchestrator, _, email) = RealOrchestrator(ctx);
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);

        Assert.IsType<OkObjectResult>(await BuildBuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId)));

        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO");
        email.Verify(e => e.SendWorkflowNotificationAsync("alpla-plasticos-accounts@alpla.com", "AlplaPLASTICO",
            It.Is<string>(sub => sub.Contains("P.O. registada") && sub.Contains(s.RequestNumber)), It.Is<string>(h => h.Contains("Revisão Necessária")),
            It.Is<string>(b => b.Contains("significa que o pagamento est") && !b.Contains("pedido de pagamento entrou")), It.IsAny<string?>(), It.IsAny<string?>(), "aovia-treasury@alpla.com"), Times.Once); // body uses HTML entities for accents
        var log = await ctx.AccountsPayableNotificationLogs.SingleAsync();
        Assert.Equal(history.Id, log.CorrelationId); Assert.True(log.Success);
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync(o => o.RequestId == s.RequestId)); // Finance switch off → no individual mail
    }

    [Fact]
    public async Task Request_without_plant_still_notifies_finance_in_app_but_queues_no_email_unchanged_fallback()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var r = await ctx.Requests.SingleAsync(x => x.Id == s.RequestId); r.PlantId = null; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (orchestrator, inApp, _) = RealOrchestrator(ctx);

        Assert.IsType<OkObjectResult>(await BuildBuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId)));

        // Routing rule untouched: no plant → global in-app fan-out to all Finance users, e-mail suppressed.
        Assert.Equal(0, await ctx.EmailOutbox.CountAsync(o => o.RequestId == s.RequestId));
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), "Nova P.O Registrada", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Exactly(3));
    }
}
