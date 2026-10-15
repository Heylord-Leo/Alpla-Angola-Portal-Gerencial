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
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// docs §G.20 D2 — "Atribuir a Mim" on a PAYMENT P.O. correction persists ownership where the personal task, the
/// dashboard count and the Buyer queue read it: the returned group's PoResponsibleBuyerId. Real RequestsController
/// (assign-buyer) and BuyerQueueController on InMemory. QUOTATION claims keep writing Request.BuyerId.
/// </summary>
public class PoCorrectionClaimTests
{
    private const int S_PO_ISSUED = 1, S_WAITING_PO_CORRECTION = 2, S_WAITING_QUOTATION = 3;

    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Seed { public Guid RequestId, GroupA, GroupB, Buyer, OtherBuyer, Coordinator, Requester; }

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string type = "PAYMENT", Guid? groupAOwner = null, bool secondCorrectionGroup = false, Guid? requestBuyerId = null, int plantId = 1)
    {
        var s = new Seed { RequestId = Guid.NewGuid(), GroupA = Guid.NewGuid(), GroupB = Guid.NewGuid(), Buyer = Guid.NewGuid(), OtherBuyer = Guid.NewGuid(), Coordinator = Guid.NewGuid(), Requester = Guid.NewGuid() };
        if (!await ctx.RequestTypes.AnyAsync())
        {
            ctx.RequestTypes.AddRange(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" }, new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
            ctx.RequestStatuses.AddRange(
                new RequestStatus { Id = S_PO_ISSUED, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" },
                new RequestStatus { Id = S_WAITING_PO_CORRECTION, Code = RequestConstants.Statuses.WaitingPoCorrection, Name = "Devolvido para Compras" },
                new RequestStatus { Id = S_WAITING_QUOTATION, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Aguardando Cotação" });
            ctx.Roles.AddRange(new Role { Id = 4, RoleName = RoleConstants.Buyer }, new Role { Id = 5, RoleName = RoleConstants.LocalManager });
            ctx.Companies.Add(new Company { Id = 1, Name = "Alpla", IsActive = true });
            ctx.Set<Department>().Add(new Department { Id = 7, Name = "Serviços Gerais", IsActive = true });
            ctx.Plants.AddRange(new Plant { Id = 1, Name = "Planta 1", CompanyId = 1 }, new Plant { Id = 9, Name = "Planta 9", CompanyId = 1 });
        }
        ctx.Users.AddRange(
            new User { Id = s.Buyer, FullName = "Celestina Bernardo", Email = "celestina@test.local", IsActive = true },
            new User { Id = s.OtherBuyer, FullName = "Samuel Mpoco", Email = "samuel@test.local", IsActive = true },
            new User { Id = s.Coordinator, FullName = "Coordenador", Email = "coord@test.local", IsActive = true },
            new User { Id = s.Requester, FullName = "Requisitante", Email = "req@test.local", IsActive = true });
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = s.Buyer, RoleId = 4 }, new UserRoleAssignment { UserId = s.OtherBuyer, RoleId = 4 }, new UserRoleAssignment { UserId = s.Coordinator, RoleId = 5 });
        var isQuotation = type == "QUOTATION";
        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = "REQ-429", Title = "Correção", RequestTypeId = isQuotation ? 1 : 2,
            StatusId = isQuotation ? S_WAITING_QUOTATION : S_WAITING_PO_CORRECTION, RequesterId = s.Requester, BuyerId = requestBuyerId,
            DepartmentId = 7, CompanyId = 1, PlantId = plantId, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid,
            EstimatedTotalAmount = 100m, CreatedAtUtc = DateTime.UtcNow.AddDays(-10), UpdatedAtUtc = DateTime.UtcNow
        });
        if (!isQuotation)
        {
            ctx.RequestPoGroups.Add(new RequestPoGroup { Id = s.GroupA, RequestId = s.RequestId, SupplierNameSnapshot = "Fornecedor A", Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection, TotalAmount = 60m, CurrencyCode = "AOA", PoResponsibleBuyerId = groupAOwner, CreatedByUserId = s.Requester, CreatedAtUtc = DateTime.UtcNow.AddDays(-5) });
            ctx.RequestPoGroups.Add(new RequestPoGroup { Id = s.GroupB, RequestId = s.RequestId, SupplierNameSnapshot = "Fornecedor B", Status = secondCorrectionGroup ? RequestConstants.PoGroupStatuses.WaitingPoCorrection : RequestConstants.PoGroupStatuses.PoIssued, TotalAmount = 40m, CurrencyCode = "AOA", PoResponsibleBuyerId = null, CreatedByUserId = s.Requester, CreatedAtUtc = DateTime.UtcNow.AddDays(-5) });
        }
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

    private static (RequestsController Ctl, Mock<IWorkflowNotificationOrchestrator> Orchestrator) RequestsCtl(ApplicationDbContext ctx, Guid userId, params string[] roles)
    {
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Returns(Task.CompletedTask);
        var notif = new Mock<INotificationService>();
        notif.Setup(n => n.CreateNotificationAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        var options = new PostPaymentCompletionOptions();
        var controller = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, AdminLog().Object, NullLogger<RequestsController>.Instance,
            notif.Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(options));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() } };
        return (controller, orchestrator);
    }

    private static BuyerQueueController QueueCtl(ApplicationDbContext ctx, Guid userId)
    {
        var c = new BuyerQueueController(ctx);
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")) } };
        return c;
    }

    private static async Task<(Guid? OwnerA, Guid? OwnerB, Guid? RequestBuyer, int ClaimHistories)> StateAsync(ApplicationDbContext ctx, Seed s)
    {
        ctx.ChangeTracker.Clear();
        var a = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA);
        var b = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB);
        var r = await ctx.Requests.AsNoTracking().SingleAsync(x => x.Id == s.RequestId);
        return (a.PoResponsibleBuyerId, b.PoResponsibleBuyerId, r.BuyerId, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == PoCorrectionClaimService.ClaimAction));
    }

    [Fact]
    public async Task Buyer_claims_the_sole_unassigned_payment_correction_group_and_request_buyer_stays_null()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (ctl, orchestrator) = RequestsCtl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.IsType<NoContentResult>(await ctl.AssignBuyer(s.RequestId));
        var st = await StateAsync(ctx, s);
        Assert.Equal(s.Buyer, st.OwnerA); Assert.Null(st.OwnerB); Assert.Null(st.RequestBuyer); Assert.Equal(1, st.ClaimHistories);
        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == PoCorrectionClaimService.ClaimAction);
        Assert.StartsWith("[Grupo P.O.: Fornecedor A", history.Comment);
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.BuyerAssigned && e.BuyerId == s.Buyer && e.CorrelationId == history.Id)), Times.Once);

        // The claim is visible where ownership is read: predicate, queue "me", and it is idempotent.
        Assert.True(await ctx.Requests.Where(PersonalPoCorrectionPredicate.OwnedBy(s.Buyer)).AnyAsync(r => r.Id == s.RequestId));
        Assert.IsType<OkObjectResult>(await ctl.AssignBuyer(s.RequestId));
        Assert.Equal(1, (await StateAsync(ctx, s)).ClaimHistories);
    }

    [Fact]
    public async Task An_existing_owner_is_never_overwritten()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, groupAOwner: null);
        var (first, _) = RequestsCtl(ctx, s.OtherBuyer, RoleConstants.Buyer);
        Assert.IsType<NoContentResult>(await first.AssignBuyer(s.RequestId));
        var (second, _) = RequestsCtl(ctx, s.Buyer, RoleConstants.Buyer);
        var res = Assert.IsType<ConflictObjectResult>(await second.AssignBuyer(s.RequestId));
        Assert.Equal("PO_CORRECTION_ALREADY_OWNED", Assert.IsType<ProblemDetails>(res.Value).Extensions["code"]);
        var st = await StateAsync(ctx, s);
        Assert.Equal(s.OtherBuyer, st.OwnerA); Assert.Equal(1, st.ClaimHistories);
        // A coordinator (same authority that reassigns a QUOTATION's Buyer) may transfer the group: previous owner
        // recorded, Request.BuyerId still untouched, one more history row, BuyerAssigned event for the new owner.
        var (coord, coordOrchestrator) = RequestsCtl(ctx, s.Coordinator, RoleConstants.LocalManager);
        Assert.IsType<NoContentResult>(await coord.AssignBuyer(s.RequestId, targetUserId: s.Buyer));
        st = await StateAsync(ctx, s);
        Assert.Equal(s.Buyer, st.OwnerA); Assert.Null(st.RequestBuyer); Assert.Equal(2, st.ClaimHistories);
        var transfer = (await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == s.RequestId && h.ActionTaken == PoCorrectionClaimService.ClaimAction).OrderByDescending(h => h.CreatedAtUtc).ToListAsync())[0];
        Assert.Contains("reatribuída ao comprador Celestina Bernardo (anterior: Samuel Mpoco)", transfer.Comment);
        coordOrchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.BuyerAssigned && e.BuyerId == s.Buyer && e.CorrelationId == transfer.Id)), Times.Once);
        // A coordinator without the Buyer role still cannot take the group for themselves (unchanged 403 rule).
        Assert.Equal(403, Assert.IsType<ObjectResult>(await coord.AssignBuyer(s.RequestId)).StatusCode);
        Assert.Equal(s.Buyer, (await StateAsync(ctx, s)).OwnerA);
    }

    [Fact]
    public async Task Several_correction_groups_without_an_explicit_group_are_ambiguous_and_nothing_is_written()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, secondCorrectionGroup: true);
        var (ctl, orchestrator) = RequestsCtl(ctx, s.Buyer, RoleConstants.Buyer);
        var res = Assert.IsType<ConflictObjectResult>(await ctl.AssignBuyer(s.RequestId));
        var pd = Assert.IsType<ProblemDetails>(res.Value);
        Assert.Equal("PO_CORRECTION_AMBIGUOUS", pd.Extensions["code"]);
        Assert.Equal(2, ((IReadOnlyList<Guid>)pd.Extensions["candidateGroupIds"]!).Count);
        var st = await StateAsync(ctx, s);
        Assert.Null(st.OwnerA); Assert.Null(st.OwnerB); Assert.Equal(0, st.ClaimHistories);
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Never);

        // Naming the group claims ONLY that group; the sibling stays unassigned.
        Assert.IsType<NoContentResult>(await ctl.AssignBuyer(s.RequestId, poGroupId: s.GroupB));
        st = await StateAsync(ctx, s);
        Assert.Null(st.OwnerA); Assert.Equal(s.Buyer, st.OwnerB); Assert.Equal(1, st.ClaimHistories);
    }

    [Fact]
    public async Task A_group_of_another_request_or_a_group_not_in_correction_is_refused()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var other = await SeedAsync(ctx);
        var (ctl, _) = RequestsCtl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.IsType<BadRequestObjectResult>(await ctl.AssignBuyer(s.RequestId, poGroupId: other.GroupA)); // cross-request
        Assert.IsType<BadRequestObjectResult>(await ctl.AssignBuyer(s.RequestId, poGroupId: s.GroupB));     // PO_ISSUED, not returned
        var st = await StateAsync(ctx, s);
        Assert.Null(st.OwnerA); Assert.Null(st.OwnerB); Assert.Equal(0, st.ClaimHistories);
        Assert.Null((await StateAsync(ctx, other)).OwnerA);
    }

    [Fact]
    public async Task A_payment_without_any_returned_group_keeps_the_pre_existing_request_level_assignment_path()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var a = await ctx.RequestPoGroups.SingleAsync(g => g.Id == s.GroupA); a.Status = RequestConstants.PoGroupStatuses.PoIssued; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (ctl, _) = RequestsCtl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.IsType<NoContentResult>(await ctl.AssignBuyer(s.RequestId));   // unchanged behaviour outside the correction scope
        var st = await StateAsync(ctx, s);
        Assert.Equal(s.Buyer, st.RequestBuyer); Assert.Null(st.OwnerA); Assert.Null(st.OwnerB);
        var h = Assert.Single(await ctx.RequestStatusHistories.AsNoTracking().Where(x => x.RequestId == s.RequestId && x.ActionTaken == "COMPRADOR_ATRIBUIDO").ToListAsync());
        Assert.Equal("O comprador assumiu a responsabilidade pelo pedido.", h.Comment);   // legacy text, no group prefix
        // Naming a group that is not returned is still refused explicitly.
        Assert.IsType<BadRequestObjectResult>(await ctl.AssignBuyer(s.RequestId, poGroupId: s.GroupA));
    }

    [Fact]
    public async Task Unauthorized_role_and_out_of_scope_buyer_are_refused_without_writes()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, plantId: 1);
        var (finance, _) = RequestsCtl(ctx, s.Buyer, RoleConstants.Finance);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await finance.AssignBuyer(s.RequestId)).StatusCode);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = s.OtherBuyer, PlantId = 9 }); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (outOfScope, _) = RequestsCtl(ctx, s.OtherBuyer, RoleConstants.Buyer);
        Assert.IsType<NotFoundObjectResult>(await outOfScope.AssignBuyer(s.RequestId));
        var st = await StateAsync(ctx, s);
        Assert.Null(st.OwnerA); Assert.Equal(0, st.ClaimHistories);
    }

    [Fact]
    public async Task Quotation_claim_is_unchanged_and_writes_the_request_buyer()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "QUOTATION");
        var (ctl, orchestrator) = RequestsCtl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.IsType<NoContentResult>(await ctl.AssignBuyer(s.RequestId));
        ctx.ChangeTracker.Clear();
        Assert.Equal(s.Buyer, (await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).BuyerId);
        var h = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(x => x.RequestId == s.RequestId && x.ActionTaken == "COMPRADOR_ATRIBUIDO");
        Assert.Equal("O comprador assumiu a responsabilidade pelo pedido.", h.Comment);
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.BuyerAssigned && e.CorrelationId == h.Id)), Times.Once);
    }

    [Fact]
    public async Task Queue_me_filter_follows_the_group_owner_for_payment_corrections_not_the_request_buyer()
    {
        await using var ctx = NewCtx();
        // Historical request-level claim on a PAYMENT (Request.BuyerId = Buyer) whose correction group belongs to OtherBuyer.
        var s = await SeedAsync(ctx, groupAOwner: null, requestBuyerId: null);
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupA); g.PoResponsibleBuyerId = s.OtherBuyer;
        var r = await ctx.Requests.SingleAsync(x => x.Id == s.RequestId); r.BuyerId = s.Buyer;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        static List<BuyerQueueItemDto> Items(ActionResult<BuyerQueuePageDto> res)
            => (res.Value ?? (BuyerQueuePageDto)((OkObjectResult)res.Result!).Value!).Items;

        Assert.Empty(Items(await QueueCtl(ctx, s.Buyer).GetQueue(ownership: "me")));        // request-level buyer is not correction ownership
        Assert.Single(Items(await QueueCtl(ctx, s.OtherBuyer).GetQueue(ownership: "me")));  // the group owner is
        Assert.Single(Items(await QueueCtl(ctx, s.Buyer).GetQueue(ownership: "all")));      // admission/placement unchanged
        Assert.False(await ctx.Requests.Where(PersonalPoCorrectionPredicate.OwnedBy(s.Buyer)).AnyAsync(x => x.Id == s.RequestId));
        Assert.True(await ctx.Requests.Where(PersonalPoCorrectionPredicate.OwnedBy(s.OtherBuyer)).AnyAsync(x => x.Id == s.RequestId));
    }
}
