using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Role/ownership matrix of <c>POST …/assign-buyer</c> for QUOTATION requests — the rules the Workspace "Assumir pedido"
/// mirrors (docs/BUYER_WORKSPACE_QUOTATION_ACTIONS.md §6). Self-assignment needs the Buyer role; a request assigned to
/// someone else can be taken over only by a System Administrator or Local Manager (who, to take it for themselves,
/// must also hold the Buyer role); reassigning to another user is coordinator-only and the target must be a Buyer;
/// access scope applies. Audit row and BuyerAssigned event are the endpoint's.
/// </summary>
public class QuotationAssignBuyerTests
{
    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Seed { public Guid RequestId, Buyer, OtherBuyer, Admin, Manager, Requester, NonBuyer; }

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, Guid? assignedTo = null, int plantId = 1)
    {
        var s = new Seed { RequestId = Guid.NewGuid(), Buyer = Guid.NewGuid(), OtherBuyer = Guid.NewGuid(), Admin = Guid.NewGuid(), Manager = Guid.NewGuid(), Requester = Guid.NewGuid(), NonBuyer = Guid.NewGuid() };
        if (!await ctx.RequestTypes.AnyAsync())
        {
            ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
            ctx.RequestStatuses.Add(new RequestStatus { Id = 1, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Aguardando Cotação" });
            ctx.Roles.AddRange(new Role { Id = 4, RoleName = RoleConstants.Buyer }, new Role { Id = 5, RoleName = RoleConstants.LocalManager }, new Role { Id = 6, RoleName = RoleConstants.SystemAdministrator });
            ctx.Companies.Add(new Company { Id = 1, Name = "Alpla", IsActive = true });
            ctx.Plants.AddRange(new Plant { Id = 1, Name = "Viana 3", CompanyId = 1 }, new Plant { Id = 9, Name = "Outra", CompanyId = 1 });
        }
        ctx.Users.AddRange(
            new User { Id = s.Buyer, FullName = "Leonardo Cintra", Email = "leonardo@test.local", IsActive = true },
            new User { Id = s.OtherBuyer, FullName = "Samuel Mpoco", Email = "samuel@test.local", IsActive = true },
            new User { Id = s.Admin, FullName = "Admin", Email = "admin@test.local", IsActive = true },
            new User { Id = s.Manager, FullName = "Gestor Local", Email = "manager@test.local", IsActive = true },
            new User { Id = s.Requester, FullName = "Requisitante", Email = "req@test.local", IsActive = true },
            new User { Id = s.NonBuyer, FullName = "Finanças", Email = "fin@test.local", IsActive = true });
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = s.Buyer, RoleId = 4 }, new UserRoleAssignment { UserId = s.OtherBuyer, RoleId = 4 });
        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = "REQ-05/10/2026-517", Title = "Peças", RequestTypeId = 1, StatusId = 1, RequesterId = s.Requester, BuyerId = assignedTo,
            DepartmentId = 5, CompanyId = 1, PlantId = plantId, EstimatedTotalAmount = 0m, CreatedAtUtc = DateTime.UtcNow.AddDays(-1), UpdatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return s;
    }

    private static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    private static (RequestsController Ctl, Mock<IWorkflowNotificationOrchestrator> Orchestrator) Ctl(ApplicationDbContext ctx, Guid userId, params string[] roles)
    {
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Returns(Task.CompletedTask);
        var notif = new Mock<INotificationService>();
        notif.Setup(n => n.CreateNotificationAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        var controller = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, AdminLog().Object, NullLogger<RequestsController>.Instance,
            notif.Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() } };
        return (controller, orchestrator);
    }

    private static async Task<(Guid? BuyerId, int Histories)> StateAsync(ApplicationDbContext ctx, Seed s)
    {
        ctx.ChangeTracker.Clear();
        return ((await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).BuyerId,
                await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "COMPRADOR_ATRIBUIDO"));
    }

    [Fact]
    public async Task Buyer_claims_an_unassigned_request_with_audit_and_event_and_the_repeat_is_idempotent()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (ctl, orchestrator) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.IsType<NoContentResult>(await ctl.AssignBuyer(s.RequestId));
        var st = await StateAsync(ctx, s);
        Assert.Equal(s.Buyer, st.BuyerId); Assert.Equal(1, st.Histories);
        var h = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(x => x.RequestId == s.RequestId && x.ActionTaken == "COMPRADOR_ATRIBUIDO");
        Assert.Equal("O comprador assumiu a responsabilidade pelo pedido.", h.Comment);
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.BuyerAssigned && e.BuyerId == s.Buyer && e.CorrelationId == h.Id)), Times.Once);
        Assert.IsType<OkObjectResult>(await ctl.AssignBuyer(s.RequestId));
        Assert.Equal(1, (await StateAsync(ctx, s)).Histories);
    }

    [Fact]
    public async Task Ordinary_Buyer_cannot_take_over_a_request_assigned_to_another_Buyer()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, assignedTo: Guid.NewGuid() /* someone else */);
        var before = await StateAsync(ctx, s);
        var (ctl, orchestrator) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        var res = Assert.IsType<ConflictObjectResult>(await ctl.AssignBuyer(s.RequestId));
        Assert.Contains("coordenadores", Assert.IsType<ProblemDetails>(res.Value).Detail);
        Assert.Equal(before, await StateAsync(ctx, s));
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Never);
    }

    [Theory]
    [InlineData(RoleConstants.SystemAdministrator)]
    [InlineData(RoleConstants.LocalManager)]
    public async Task Coordinator_with_the_Buyer_role_takes_over_an_assigned_request_with_audit_and_event(string coordinatorRole)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, assignedTo: Guid.NewGuid());
        var (ctl, orchestrator) = Ctl(ctx, s.Buyer, coordinatorRole, RoleConstants.Buyer);
        Assert.IsType<NoContentResult>(await ctl.AssignBuyer(s.RequestId));
        var st = await StateAsync(ctx, s);
        Assert.Equal(s.Buyer, st.BuyerId); Assert.Equal(1, st.Histories);
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.BuyerAssigned && e.BuyerId == s.Buyer)), Times.Once);
    }

    [Theory]
    [InlineData(RoleConstants.SystemAdministrator)]
    [InlineData(RoleConstants.LocalManager)]
    public async Task Coordinator_without_the_Buyer_role_cannot_self_assign_even_an_unassigned_request(string coordinatorRole)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (ctl, _) = Ctl(ctx, s.Admin, coordinatorRole);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await ctl.AssignBuyer(s.RequestId)).StatusCode);
        Assert.Null((await StateAsync(ctx, s)).BuyerId);
    }

    [Fact]
    public async Task Coordinator_reassigns_to_another_Buyer_but_not_to_a_non_Buyer_and_an_ordinary_Buyer_cannot_assign_others()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, assignedTo: s_dummy());
        var (manager, orchestrator) = Ctl(ctx, s.Manager, RoleConstants.LocalManager);
        Assert.IsType<NoContentResult>(await manager.AssignBuyer(s.RequestId, targetUserId: s.OtherBuyer));
        var st = await StateAsync(ctx, s);
        Assert.Equal(s.OtherBuyer, st.BuyerId); Assert.Equal(1, st.Histories);
        var h = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(x => x.RequestId == s.RequestId && x.ActionTaken == "COMPRADOR_ATRIBUIDO");
        Assert.Contains("atribuído ao comprador Samuel Mpoco", h.Comment);
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.BuyerAssigned && e.BuyerId == s.OtherBuyer)), Times.Once);

        Assert.IsType<ConflictObjectResult>(await manager.AssignBuyer(s.RequestId, targetUserId: s.NonBuyer));   // target must be a Buyer
        var (buyer, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await buyer.AssignBuyer(s.RequestId, targetUserId: s.Admin)).StatusCode); // only coordinators assign others
        Assert.Equal(s.OtherBuyer, (await StateAsync(ctx, s)).BuyerId);
    }

    private static Guid s_dummy() => Guid.NewGuid();

    [Fact]
    public async Task Out_of_scope_Buyer_is_refused_before_any_change()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, plantId: 1);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = s.Buyer, PlantId = 9 }); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (ctl, orchestrator) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.IsType<NotFoundObjectResult>(await ctl.AssignBuyer(s.RequestId));
        Assert.Null((await StateAsync(ctx, s)).BuyerId);
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Never);
    }
}
