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
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Legacy request-level Finance return (docs §G.19, REQ-27/07/2026-156 shape): scalar WAITING_PO_CORRECTION, sole live
/// group still PO_ISSUED, FINANCE_RETURN_ADJUSTMENT after the last REGISTER_PO, no live payment. Real controllers on an
/// InMemory context: RegisterPo treats the registration of that group as the correction (REREGISTER_PO, default
/// correction comment, correction wording in the AP notice) for PAYMENT and QUOTATION; SchedulePayment / MarkAsPaid
/// refuse with 409 and write nothing; valid actions on unaffected shapes (REQ-275: PO_PARTIALLY_UPLOADED with one
/// returned group among others; current-code single-group returns) are unchanged.
/// </summary>
public class LegacyFinanceReturnCorrectionTests
{
    private const int S_PO_ISSUED = 9, S_WAITING_PO_CORRECTION = 12, S_PO_PARTIAL = 13, S_PAYMENT_SCHEDULED = 14, S_PAYMENT_COMPLETED = 15, S_ADVANCE = 16;

    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Seed { public Guid RequestId, GroupId, BuyerId, FinanceId, ProofId; }

    /// <summary>REQ-156 shape. <paramref name="type"/> PAYMENT or QUOTATION; <paramref name="returned"/> adds the Finance return after the registration.</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string type = "PAYMENT", bool returned = true, bool registrationAfterReturn = false, bool livePayment = false, bool secondLiveGroup = false, bool apConfig = false)
    {
        var s = new Seed { RequestId = Guid.NewGuid(), GroupId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), FinanceId = Guid.NewGuid(), ProofId = Guid.NewGuid() };
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaPLASTICO", IsActive = true });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Planta 1" });
        ctx.Set<Department>().Add(new Department { Id = 7, Name = "Serviços Gerais", IsActive = true }); // required navigation of the detail projection (GetRequest)
        ctx.RequestTypes.AddRange(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" }, new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_PO_ISSUED, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" },
            new RequestStatus { Id = S_WAITING_PO_CORRECTION, Code = RequestConstants.Statuses.WaitingPoCorrection, Name = "Devolvido para Compras" },
            new RequestStatus { Id = S_PO_PARTIAL, Code = RequestConstants.Statuses.PoPartiallyUploaded, Name = "P.O. parcial" },
            new RequestStatus { Id = S_PAYMENT_SCHEDULED, Code = RequestConstants.Statuses.PaymentScheduled, Name = "Pagamento Agendado" },
            new RequestStatus { Id = S_PAYMENT_COMPLETED, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Realizado" },
            new RequestStatus { Id = S_ADVANCE, Code = RequestConstants.Statuses.AdvancePaymentRequired, Name = "Adiantamento" });
        ctx.Roles.AddRange(new Role { Id = 3, RoleName = RoleConstants.Finance }, new Role { Id = 4, RoleName = RoleConstants.Buyer });
        ctx.Suppliers.AddRange(new Supplier { Id = 10, Name = "Qlerius | Serviços de Limpeza", TaxId = "5010", RegistrationStatus = "ACTIVE" }, new Supplier { Id = 11, Name = "Outro", TaxId = "5011", RegistrationStatus = "ACTIVE" });
        ctx.Users.AddRange(
            new User { Id = s.BuyerId, FullName = "Comprador", Email = "buyer@test.local", IsActive = true },
            new User { Id = s.FinanceId, FullName = "Finanças", Email = "finance@test.local", IsActive = true });
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = s.BuyerId, RoleId = 4 }, new UserRoleAssignment { UserId = s.FinanceId, RoleId = 3 });
        if (apConfig)
            ctx.AccountsPayableNotificationConfigs.Add(new AccountsPayableNotificationConfig { Id = 1, CompanyId = 1, Email = "ap@alpla.com", IsActive = true, NotifyOnScheduled = true, NotifyOnCompleted = true, NotifyOnPoRegistered = true });
        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = "REQ-27/07/2026-156", Title = "Serviços de limpeza", RequestTypeId = type == "PAYMENT" ? 2 : 1, StatusId = S_WAITING_PO_CORRECTION,
            RequesterId = s.BuyerId, BuyerId = s.BuyerId, DepartmentId = 7, CompanyId = 1, PlantId = 1, SupplierId = 10,
            PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, EstimatedTotalAmount = 1097000m, ApprovedTotalAmount = 1097000m, ApprovedCurrencyCode = "AOA",
            ApprovedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), CreatedAtUtc = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = DateTime.UtcNow
        });
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = s.GroupId, RequestId = s.RequestId, SupplierId = 10, SupplierNameSnapshot = "Qlerius | Serviços de Limpeza", Status = RequestConstants.PoGroupStatuses.PoIssued, TotalAmount = 1097000m, CurrencyCode = "AOA", PurchaseOrderNumber = "ECF11 2026/156", PoResponsibleBuyerId = s.BuyerId, CreatedByUserId = s.BuyerId, CreatedAtUtc = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc) });
        if (secondLiveGroup)
            ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.RequestId, SupplierId = 11, SupplierNameSnapshot = "Outro", Status = RequestConstants.PoGroupStatuses.PoIssued, TotalAmount = 10m, CurrencyCode = "AOA", CreatedByUserId = s.BuyerId, CreatedAtUtc = DateTime.UtcNow });
        ctx.RequestAttachments.AddRange(
            new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, AttachmentTypeCode = RequestAttachment.TYPE_PROFORMA, FileName = "proforma.pdf", StorageReference = "proforma.pdf", UploadedByUserId = s.BuyerId, UploadedAtUtc = DateTime.UtcNow },
            new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, RequestPoGroupId = s.GroupId, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = "po.pdf", StorageReference = "po.pdf", UploadedByUserId = s.BuyerId, UploadedAtUtc = DateTime.UtcNow },
            new RequestAttachment { Id = s.ProofId, RequestId = s.RequestId, AttachmentTypeCode = AttachmentConstants.Types.PaymentProof, FileName = "proof.pdf", FileExtension = ".pdf", UploadedByUserId = s.FinanceId, UploadedAtUtc = DateTime.UtcNow });
        var reg = new DateTime(2026, 8, 3, 16, 23, 10, DateTimeKind.Utc);
        var ret = new DateTime(2026, 8, 11, 9, 42, 23, DateTimeKind.Utc);
        ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.BuyerId, ActionTaken = "REGISTER_PO", NewStatusId = S_PO_ISSUED, CreatedAtUtc = registrationAfterReturn ? ret.AddMinutes(10) : reg, Comment = "P.O registrada" });
        if (returned)
            ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = s.FinanceId, ActionTaken = "FINANCE_RETURN_ADJUSTMENT", PreviousStatusId = S_PO_ISSUED, NewStatusId = S_WAITING_PO_CORRECTION, CreatedAtUtc = ret, Comment = "Devolvido por Finanças para ajuste: INSERIR A FACTURA OU PROFORMA" });
        if (livePayment)
            ctx.RequestPayments.Add(new RequestPayment { RequestId = s.RequestId, RequestPoGroupId = s.GroupId, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = 1, PlannedAmount = 1097000m, CurrencyCode = "AOA", PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, CreatedByUserId = s.FinanceId, CreatedAtUtc = DateTime.UtcNow });
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

    private static (WorkflowNotificationOrchestrator Orchestrator, Mock<IEmailService> Email) RealOrchestrator(ApplicationDbContext ctx)
    {
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new ApprovalRoutingResultDto());
        return (new WorkflowNotificationOrchestrator(ctx, inApp.Object, email.Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, AdminLog().Object, routing.Object), email);
    }

    private static RequestsController BuyerController(ApplicationDbContext ctx, Guid buyerId, IWorkflowNotificationOrchestrator orchestrator) => RequestsCtl(ctx, buyerId, orchestrator, RoleConstants.Buyer);

    private static RequestsController RequestsCtl(ApplicationDbContext ctx, Guid userId, IWorkflowNotificationOrchestrator orchestrator, string role)
    {
        var options = new PostPaymentCompletionOptions();
        var controller = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(options));
        var services = new ServiceCollection();
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, role), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    private static FinanceController FinanceCtl(ApplicationDbContext ctx, Guid actorId, IWorkflowNotificationOrchestrator orchestrator)
    {
        var controller = new FinanceController(ctx, orchestrator, NullLogger<FinanceController>.Instance, new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance), new FinancePaymentEligibilityService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")) }
        };
        return controller;
    }

    private static RegisterPoActionDto Dto(Guid groupId, string? comment) => new() { PoGroupId = groupId, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, PurchaseOrderNumber = "ECF11 2026/156", Comment = comment };

    private static async Task<(int Histories, int Payments, int Attachments, int Outbox, int ApLogs)> CountsAsync(ApplicationDbContext ctx, Guid requestId) =>
        (await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == requestId), await ctx.RequestPayments.CountAsync(p => p.RequestId == requestId),
         await ctx.RequestAttachments.CountAsync(a => a.RequestId == requestId), await ctx.EmailOutbox.CountAsync(e => e.RequestId == requestId), await ctx.AccountsPayableNotificationLogs.CountAsync(l => l.RequestId == requestId));

    // ── RegisterPo: the legacy shape IS a correction ─────────────────────────

    [Theory]
    [InlineData("PAYMENT")]
    [InlineData("QUOTATION")]
    public async Task Legacy_shape_registers_as_a_correction_with_REREGISTER_PO_history_and_correction_notice_when_justified(string type)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, type, apConfig: true);
        var (orchestrator, email) = RealOrchestrator(ctx);
        var events = new List<WorkflowEvent>();

        var result = await BuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId, "Proforma anexada e P.O. corrigida conforme devolução."));
        Assert.IsType<OkObjectResult>(result);

        var history = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == s.RequestId).OrderBy(h => h.CreatedAtUtc).ToListAsync();
        var rereg = Assert.Single(history, h => h.ActionTaken == "REREGISTER_PO");
        Assert.DoesNotContain(history, h => h.ActionTaken == "REGISTER_PO" && h.CreatedAtUtc > new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains("Justificativa: Proforma anexada e P.O. corrigida conforme devolução.", rereg.Comment);
        Assert.Contains("P.O corrigida e re-registrada com sucesso.", rereg.Comment);
        Assert.StartsWith("[Grupo P.O.:", rereg.Comment);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, group.Status);
        Assert.Equal(s.BuyerId, group.PoResponsibleBuyerId);
        var req = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId);
        Assert.Equal(S_PO_ISSUED, req.StatusId);                                                           // scalar leaves WAITING_PO_CORRECTION
        Assert.Equal(1097000m, req.ApprovedTotalAmount); Assert.Equal("AOA", req.ApprovedCurrencyCode);     // approval metadata preserved
        // Correction wording through the real orchestrator (AP notice) and per-action correlation
        email.Verify(e => e.SendWorkflowNotificationAsync("ap@alpla.com", It.IsAny<string>(), It.Is<string>(sub => sub.Contains("corrigida e re-registada")), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
        var log = Assert.Single(await ctx.AccountsPayableNotificationLogs.ToListAsync());
        Assert.Equal(rereg.Id, log.CorrelationId);

        // Idempotency of the predicate: the request no longer qualifies, so a second registration is NOT a correction.
        var verdict = await LegacyFinanceReturnShape.EvaluateAsync(ctx, s.RequestId);
        Assert.False(verdict.Qualifies);
    }

    [Fact]
    public async Task Explicit_comment_is_kept_on_the_legacy_correction()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "PAYMENT");
        var (orchestrator, _) = RealOrchestrator(ctx);
        Assert.IsType<OkObjectResult>(await BuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId, "Proforma anexada conforme pedido das Finanças.")));
        var rereg = Assert.Single(await ctx.RequestStatusHistories.Where(h => h.RequestId == s.RequestId && h.ActionTaken == "REREGISTER_PO").ToListAsync());
        Assert.Contains("Justificativa: Proforma anexada conforme pedido das Finanças.", rereg.Comment);
        Assert.Contains("P.O corrigida e re-registrada com sucesso.", rereg.Comment);
    }

    [Theory]
    [InlineData("noReturn")]
    [InlineData("registrationAfterReturn")]
    [InlineData("livePayment")]
    [InlineData("secondLiveGroup")]
    public async Task Quotation_without_the_full_legacy_evidence_is_still_refused_on_an_issued_group_and_nothing_is_written(string defect)
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, "QUOTATION", returned: defect != "noReturn", registrationAfterReturn: defect == "registrationAfterReturn", livePayment: defect == "livePayment", secondLiveGroup: defect == "secondLiveGroup");
        var before = await CountsAsync(ctx, s.RequestId);
        var (orchestrator, _) = RealOrchestrator(ctx);

        var result = await BuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId, "tentativa"));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("PO_ISSUED", Assert.IsType<ProblemDetails>(bad.Value).Detail);
        Assert.Equal(before, await CountsAsync(ctx, s.RequestId));
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
    }

    [Fact]
    public async Task Current_code_single_group_return_still_corrects_normally()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "PAYMENT");
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupId); g.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (orchestrator, _) = RealOrchestrator(ctx);
        Assert.IsType<OkObjectResult>(await BuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId, null)));
        Assert.Single(await ctx.RequestStatusHistories.Where(h => h.RequestId == s.RequestId && h.ActionTaken == "REREGISTER_PO").ToListAsync());
    }

    // ── Finance: no scheduling or payment on the legacy shape, nothing written ──

    [Fact]
    public async Task Schedule_and_pay_are_refused_with_409_and_no_side_effects_on_the_legacy_shape()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "PAYMENT", apConfig: true);
        var before = await CountsAsync(ctx, s.RequestId);
        var (orchestrator, email) = RealOrchestrator(ctx);
        var finance = FinanceCtl(ctx, s.FinanceId, orchestrator);

        var sched = await finance.SchedulePayment(s.RequestId, new SchedulePaymentDto { RequestPoGroupId = s.GroupId, ScheduledDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) });
        var pay = await finance.MarkAsPaid(s.RequestId, new ConfirmPaymentDto { RequestPoGroupId = s.GroupId, PaymentProofAttachmentId = s.ProofId, ActualPaidAmount = 1097000m, PaidDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) });

        foreach (var r in new[] { sched, pay })
        {
            var conflict = Assert.IsType<ConflictObjectResult>(r);
            var pd = Assert.IsType<ProblemDetails>(conflict.Value);
            Assert.Equal(409, pd.Status); Assert.Equal(LegacyFinanceReturnEvaluator.ReasonCode, pd.Extensions["code"]); Assert.Contains("ao nível do pedido", pd.Detail);
        }
        Assert.Equal(before, await CountsAsync(ctx, s.RequestId));
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
        Assert.Equal(S_WAITING_PO_CORRECTION, (await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).StatusId);
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task After_the_legacy_correction_the_group_can_be_scheduled_normally()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "PAYMENT");
        var (orchestrator, _) = RealOrchestrator(ctx);
        Assert.IsType<OkObjectResult>(await BuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId, "Corrigida conforme devolução.")));
        Assert.IsType<OkResult>(await FinanceCtl(ctx, s.FinanceId, orchestrator).SchedulePayment(s.RequestId, new SchedulePaymentDto { RequestPoGroupId = s.GroupId, ScheduledDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) }));
        Assert.Equal(1, await ctx.RequestPayments.CountAsync(p => p.RequestId == s.RequestId));
    }

    [Fact]
    public async Task Req275_shape_keeps_valid_actions_on_the_unaffected_issued_group()
    {
        // PO_PARTIALLY_UPLOADED scalar, one group WAITING_PO_CORRECTION (current-code return) and one sibling PO_ISSUED: not the legacy shape.
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "QUOTATION", secondLiveGroup: true);
        var req = await ctx.Requests.SingleAsync(r => r.Id == s.RequestId); req.StatusId = S_PO_PARTIAL;
        var returnedSibling = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == s.RequestId && g.Id != s.GroupId); returnedSibling.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.False((await LegacyFinanceReturnShape.EvaluateAsync(ctx, s.RequestId)).Qualifies);
        var (orchestrator, _) = RealOrchestrator(ctx);

        Assert.IsType<OkResult>(await FinanceCtl(ctx, s.FinanceId, orchestrator).SchedulePayment(s.RequestId, new SchedulePaymentDto { RequestPoGroupId = s.GroupId, ScheduledDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) }));
        Assert.Equal(RequestConstants.PoGroupStatuses.PaymentScheduled, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == returnedSibling.Id)).Status);
    }
    // ── Point 1: the Buyer's justification is mandatory for the legacy correction ──

    [Theory]
    [InlineData("PAYMENT", null)]
    [InlineData("PAYMENT", "")]
    [InlineData("PAYMENT", "   ")]
    [InlineData("QUOTATION", null)]
    [InlineData("QUOTATION", "\t ")]
    public async Task Legacy_correction_without_a_justification_is_refused_before_any_mutation_or_notification(string type, string? comment)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, type, apConfig: true);
        var before = await CountsAsync(ctx, s.RequestId);
        var (orchestrator, email) = RealOrchestrator(ctx);

        var result = await BuyerController(ctx, s.BuyerId, orchestrator).RegisterPo(s.RequestId, Dto(s.GroupId, comment));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Justificativa obrigatória", Assert.IsType<ProblemDetails>(bad.Value).Title);
        Assert.Equal(before, await CountsAsync(ctx, s.RequestId));
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, group.Status); Assert.Equal("ECF11 2026/156", group.PurchaseOrderNumber);
        Assert.Equal(S_WAITING_PO_CORRECTION, (await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).StatusId);
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    // ── Point 2: financial guard = unresolved request-level return, not the repair predicate ──

    private static async Task<Seed> SeedBlockShapeAsync(ApplicationDbContext ctx, string shape)
    {
        var s = await SeedAsync(ctx, "QUOTATION", returned: shape != "noReturnHistory", livePayment: shape == "scheduledPayment", secondLiveGroup: shape is "twoLiveGroupsNoneReturned" or "groupScopedWithSibling");
        if (shape == "scheduledPayment") { var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupId); g.Status = RequestConstants.PoGroupStatuses.PaymentScheduled; }
        if (shape == "groupScopedWithSibling") { var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupId); g.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection; }
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return s;
    }

    [Theory]
    [InlineData("fullEvidence")]
    [InlineData("noReturnHistory")]
    [InlineData("scheduledPayment")]
    [InlineData("twoLiveGroupsNoneReturned")]
    public async Task Unresolved_request_level_return_blocks_schedule_and_pay_on_every_live_group_with_no_side_effects(string shape)
    {
        await using var ctx = NewCtx(); var s = await SeedBlockShapeAsync(ctx, shape);
        var before = await CountsAsync(ctx, s.RequestId);
        var (orchestrator, email) = RealOrchestrator(ctx);
        var finance = FinanceCtl(ctx, s.FinanceId, orchestrator);
        var groups = await ctx.RequestPoGroups.AsNoTracking().Where(g => g.RequestId == s.RequestId).Select(g => g.Id).ToListAsync();
        foreach (var gid in groups)
        {
            var sched = await finance.SchedulePayment(s.RequestId, new SchedulePaymentDto { RequestPoGroupId = gid, ScheduledDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) });
            var pay = await finance.MarkAsPaid(s.RequestId, new ConfirmPaymentDto { RequestPoGroupId = gid, PaymentProofAttachmentId = s.ProofId, ActualPaidAmount = 1097000m, PaidDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) });
            foreach (var r in new[] { sched, pay })
                Assert.Equal(LegacyFinanceReturnEvaluator.ReasonCode, Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(r).Value).Extensions["code"]);
        }
        Assert.Equal(before, await CountsAsync(ctx, s.RequestId));
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Group_scoped_return_with_sibling_keeps_the_sibling_schedulable_and_the_returned_group_refused_by_its_own_status()
    {
        await using var ctx = NewCtx(); var s = await SeedBlockShapeAsync(ctx, "groupScopedWithSibling");   // scalar WAITING_PO_CORRECTION, group A WAITING_PO_CORRECTION, sibling PO_ISSUED
        var sibling = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.RequestId == s.RequestId && g.Id != s.GroupId);
        var (orchestrator, _) = RealOrchestrator(ctx);
        var finance = FinanceCtl(ctx, s.FinanceId, orchestrator);
        Assert.IsType<OkResult>(await finance.SchedulePayment(s.RequestId, new SchedulePaymentDto { RequestPoGroupId = sibling.Id, ScheduledDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) }));
        var refused = await finance.SchedulePayment(s.RequestId, new SchedulePaymentDto { RequestPoGroupId = s.GroupId, ScheduledDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) });
        Assert.IsType<BadRequestObjectResult>(refused);                                                     // existing group-status guard, not the legacy block
    }

    [Fact]
    public async Task Advance_endpoints_are_blocked_on_an_unresolved_request_level_return_with_no_side_effects()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "QUOTATION");
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupId); g.Status = RequestConstants.PoGroupStatuses.AdvancePaymentRequired; g.PaymentConditionCode = RequestConstants.PaymentConditions.AdvanceFull; g.AdvancePaymentPercent = 100m;
        ctx.RequestPayments.Add(new RequestPayment { RequestId = s.RequestId, RequestPoGroupId = s.GroupId, PaymentType = RequestPayment.PaymentTypes.Advance, PaymentSequence = 1, PlannedAmount = 1097000m, PlannedPercent = 100m, CurrencyCode = "AOA", PaymentStatus = RequestPayment.PaymentStatuses.Planned, CreatedByUserId = s.BuyerId, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var before = await CountsAsync(ctx, s.RequestId);
        var (orchestrator, email) = RealOrchestrator(ctx);
        var finance = RequestsCtl(ctx, s.FinanceId, orchestrator, RoleConstants.Finance);

        var sched = await finance.ScheduleAdvancePayment(s.RequestId, new ScheduleAdvancePaymentDto { RequestPoGroupId = s.GroupId, ScheduledDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) });
        var confirm = await finance.ConfirmAdvancePayment(s.RequestId, new ConfirmAdvancePaymentDto { RequestPoGroupId = s.GroupId, PaymentProofAttachmentId = s.ProofId, ActualPaidAmount = 1097000m, PaidDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) });

        foreach (var r in new[] { sched, confirm })
            Assert.Equal(LegacyFinanceReturnEvaluator.ReasonCode, Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(r).Value).Extensions["code"]);
        Assert.Equal(before, await CountsAsync(ctx, s.RequestId));
        var pay = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestId == s.RequestId);
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, pay.PaymentStatus); Assert.Null(pay.ScheduledDateUtc);
        email.Verify(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    // ── Detail projection: the justification decision is the backend's (docs §G.19.3) ──────────────────────

    private static async Task<RequestDetailsDto> DetailAsync(RequestsController ctl, Guid requestId)
        => Assert.IsType<RequestDetailsDto>(Assert.IsType<OkObjectResult>((await ctl.GetRequest(requestId)).Result).Value);

    [Theory]
    [InlineData("PAYMENT")]
    [InlineData("QUOTATION")]
    public async Task Detail_exposes_the_mandatory_justification_decision_for_the_legacy_group_and_clears_it_after_the_correction(string type)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, type);
        var (orchestrator, _) = RealOrchestrator(ctx);
        var ctl = BuyerController(ctx, s.BuyerId, orchestrator);

        var before = await DetailAsync(ctl, s.RequestId);
        Assert.NotNull(before.LegacyReturnCorrection);
        Assert.Equal(s.GroupId, before.LegacyReturnCorrection!.PoGroupId);
        Assert.True(before.LegacyReturnCorrection.JustificationRequired);
        Assert.Equal(LegacyFinanceReturnEvaluator.ReasonCode, before.LegacyReturnCorrection.Reason);
        Assert.Null(before.DisplayStatusName);                                   // badge rule of §G.19.1 unchanged: scalar name stands
        Assert.Equal(RequestConstants.Statuses.WaitingPoCorrection, before.StatusCode);

        // The decision the detail advertises is exactly the one RegisterPo enforces.
        var refused = await ctl.RegisterPo(s.RequestId, Dto(s.GroupId, "   "));
        Assert.Equal(400, Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(refused).Value).Status);

        Assert.IsType<OkObjectResult>(await ctl.RegisterPo(s.RequestId, Dto(s.GroupId, "P.O corrigida: NIF do fornecedor.")));
        ctx.ChangeTracker.Clear();
        var after = await DetailAsync(ctl, s.RequestId);
        Assert.Null(after.LegacyReturnCorrection);                               // no longer the legacy shape
        Assert.NotEqual(RequestConstants.Statuses.WaitingPoCorrection, after.StatusCode);
    }

    [Theory]
    [InlineData("currentCodeReturn")]   // group itself WAITING_PO_CORRECTION → current-code correction, comment optional
    [InlineData("notReturned")]         // scalar WAITING_PO_CORRECTION without a return row → not repair/correction eligible
    [InlineData("twoLiveGroups")]       // returned group unknown → never advertised
    public async Task Detail_does_not_advertise_a_mandatory_justification_outside_the_exact_legacy_shape(string shape)
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, "PAYMENT", returned: shape != "notReturned", secondLiveGroup: shape == "twoLiveGroups");
        if (shape == "currentCodeReturn")
        {
            var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupId);
            g.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection;
            await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        }
        var (orchestrator, _) = RealOrchestrator(ctx);
        var dto = await DetailAsync(BuyerController(ctx, s.BuyerId, orchestrator), s.RequestId);
        Assert.Null(dto.LegacyReturnCorrection);
    }
}
