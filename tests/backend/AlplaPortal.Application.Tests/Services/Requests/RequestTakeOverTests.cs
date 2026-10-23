using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// §8 — Buyer self-takeover (`POST /requests/{id}/take-over`). Rule matrix, justification and expected-owner
/// validation (omitted vs explicit null vs stale), idempotence, audit rows and the BUYER_TRANSFERRED event; plus the
/// claim/coordinator paths of `assign-buyer` no longer writing direct in-app notices. InMemory (transactions are
/// ignored by the provider — atomicity and the RowVersion race are proven on SQL Server in
/// <see cref="RequestTakeOverRelationalTests"/>).
/// </summary>
public class RequestTakeOverTests
{
    private const int S_WAITING_QUOTATION = 1, S_CANCELLED = 2, S_REJECTED = 3, S_COMPLETED = 4, S_PAYMENT_COMPLETED = 5, S_WAITING_AREA = 6;
    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private sealed class Seed { public Guid RequestId, Owner, Buyer, Admin, Manager, Requester; }

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, Guid? assignedTo = null, int statusId = S_WAITING_QUOTATION, int typeId = 1, int plantId = 1, bool ownerIsSeededOwner = true)
    {
        var s = new Seed { RequestId = Guid.NewGuid(), Owner = Guid.NewGuid(), Buyer = Guid.NewGuid(), Admin = Guid.NewGuid(), Manager = Guid.NewGuid(), Requester = Guid.NewGuid() };
        if (!await ctx.RequestTypes.AnyAsync())
        {
            ctx.RequestTypes.AddRange(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" }, new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
            ctx.RequestStatuses.AddRange(
                new RequestStatus { Id = S_WAITING_QUOTATION, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Aguardando Cotação" },
                new RequestStatus { Id = S_CANCELLED, Code = RequestConstants.Statuses.Cancelled, Name = "Cancelado" },
                new RequestStatus { Id = S_REJECTED, Code = RequestConstants.Statuses.Rejected, Name = "Rejeitado" },
                new RequestStatus { Id = S_COMPLETED, Code = RequestConstants.Statuses.Completed, Name = "Concluído" },
                new RequestStatus { Id = S_PAYMENT_COMPLETED, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pago" },
                new RequestStatus { Id = S_WAITING_AREA, Code = RequestConstants.Statuses.WaitingAreaApproval, Name = "Aguardando Aprovação de Área" });
            ctx.Roles.AddRange(new Role { Id = 4, RoleName = RoleConstants.Buyer }, new Role { Id = 5, RoleName = RoleConstants.LocalManager }, new Role { Id = 6, RoleName = RoleConstants.SystemAdministrator });
            ctx.Companies.Add(new Company { Id = 1, Name = "Alpla", IsActive = true });
            ctx.Plants.AddRange(new Plant { Id = 1, Name = "Viana 3", CompanyId = 1 }, new Plant { Id = 9, Name = "Outra", CompanyId = 1 });
        }
        ctx.Users.AddRange(
            new User { Id = s.Owner, FullName = "Samuel Mpoco", Email = "samuel@test.local", IsActive = true },
            new User { Id = s.Buyer, FullName = "Leonardo Cintra", Email = "leonardo@test.local", IsActive = true },
            new User { Id = s.Admin, FullName = "Admin", Email = "admin@test.local", IsActive = true },
            new User { Id = s.Manager, FullName = "Gestor Local", Email = "manager@test.local", IsActive = true },
            new User { Id = s.Requester, FullName = "Requisitante", Email = "req@test.local", IsActive = true });
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = s.Owner, RoleId = 4 }, new UserRoleAssignment { UserId = s.Buyer, RoleId = 4 });
        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = "REQ-20/08/2026-271", Title = "Peças", RequestTypeId = typeId, StatusId = statusId, RequesterId = s.Requester,
            BuyerId = assignedTo ?? (ownerIsSeededOwner ? s.Owner : null), DepartmentId = 5, CompanyId = 1, PlantId = plantId, EstimatedTotalAmount = 0m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1), UpdatedAtUtc = DateTime.UtcNow
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

    private static (RequestsController Ctl, Mock<IWorkflowNotificationOrchestrator> Orchestrator, Mock<INotificationService> Notif) Ctl(ApplicationDbContext ctx, Guid userId, params string[] roles)
    {
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Returns(Task.CompletedTask);
        var notif = new Mock<INotificationService>();
        var controller = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, AdminLog().Object, NullLogger<RequestsController>.Instance,
            notif.Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() } };
        return (controller, orchestrator, notif);
    }

    private static JsonElement Json(string literal) => JsonDocument.Parse(literal).RootElement.Clone();
    private static TakeOverRequestDto Dto(Guid? expected, string justification = "Comprador anterior ausente; assumo o seguimento das cotações.")
        => new() { Justification = justification, ExpectedBuyerId = Json(expected.HasValue ? $"\"{expected}\"" : "null") };

    private sealed record State(Guid? BuyerId, int Transfers, int FieldChanges, int Histories);
    private static async Task<State> StateAsync(ApplicationDbContext ctx, Seed s)
    {
        ctx.ChangeTracker.Clear();
        return new State((await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).BuyerId,
            await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == BuyerTakeOverPolicy.HistoryAction),
            await ctx.RequestFieldChangeHistories.CountAsync(h => h.RequestId == s.RequestId),
            await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId));
    }

    private static string CodeOf(IActionResult r) => (string)Assert.IsType<ProblemDetails>(((ObjectResult)r).Value).Extensions["code"]!;

    [Fact]
    public async Task Ordinary_Buyer_takes_over_with_justification_audit_field_change_and_one_transfer_event()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (ctl, orchestrator, notif) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        var before = DateTime.UtcNow;
        var ok = Assert.IsType<OkObjectResult>(await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner, "  Samuel está de férias; assumo o pedido.  ")));
        var result = Assert.IsType<TakeOverRequestResultDto>(ok.Value);
        Assert.Equal("TAKEOVER_DONE", result.Code); Assert.Equal(s.Owner, result.PreviousBuyerId); Assert.Equal(s.Buyer, result.NewBuyerId); Assert.NotNull(result.HistoryId);

        var st = await StateAsync(ctx, s);
        Assert.Equal(s.Buyer, st.BuyerId); Assert.Equal(1, st.Transfers); Assert.Equal(1, st.FieldChanges);
        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == BuyerTakeOverPolicy.HistoryAction);
        Assert.Equal(s.Buyer, history.ActorUserId);
        Assert.Contains("Samuel Mpoco", history.Comment); Assert.Contains("Leonardo Cintra", history.Comment); Assert.Contains("Samuel está de férias; assumo o pedido.", history.Comment); Assert.Contains("Luanda, WAT UTC+1", history.Comment);
        var change = await ctx.RequestFieldChangeHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId);
        Assert.Equal(BuyerTakeOverPolicy.FieldChangeName, change.FieldName); Assert.Contains("Samuel Mpoco", change.PreviousValue); Assert.Contains("Leonardo Cintra", change.NewValue); Assert.Equal(RequestConstants.Statuses.WaitingQuotation, change.StatusCodeAtChange);

        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e =>
            e.EventCode == WorkflowEventCodes.BuyerTransferred && e.RequestId == s.RequestId && e.ActorUserId == s.Buyer && e.BuyerId == s.Buyer
            && e.PreviousBuyerId == s.Owner && e.RequesterId == s.Requester && e.Justification == "Samuel está de férias; assumo o pedido."
            && e.CorrelationId == history.Id && e.OccurredAtUtc.HasValue && e.OccurredAtUtc.Value >= before && e.CompanyId == 1 && e.PlantId == 1)), Times.Once);
        orchestrator.VerifyNoOtherCalls();
        notif.VerifyNoOtherCalls();   // no direct in-app notices from the controller: the orchestration owns them
    }

    [Theory]
    [InlineData(RoleConstants.SystemAdministrator)]
    [InlineData(RoleConstants.LocalManager)]
    public async Task Coordinator_without_the_Buyer_role_cannot_take_over(string coordinatorRole)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var actor = coordinatorRole == RoleConstants.SystemAdministrator ? s.Admin : s.Manager;
        var (ctl, orchestrator, _) = Ctl(ctx, actor, coordinatorRole);
        var r = await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner));
        Assert.Equal(403, ((ObjectResult)r).StatusCode); Assert.Equal(BuyerTakeOverPolicy.CodeRoleRequired, CodeOf(r));
        Assert.Equal(s.Owner, (await StateAsync(ctx, s)).BuyerId); orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Coordinator_WITH_the_Buyer_role_takes_over_like_any_Buyer()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (ctl, orchestrator, _) = Ctl(ctx, s.Admin, RoleConstants.SystemAdministrator, RoleConstants.Buyer);
        Assert.IsType<OkObjectResult>(await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner)));
        Assert.Equal(s.Admin, (await StateAsync(ctx, s)).BuyerId);
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.BuyerTransferred)), Times.Once);
    }

    [Fact]
    public async Task PAYMENT_request_is_not_applicable_and_untouched()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, typeId: 2);
        var (ctl, orchestrator, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        var r = await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner));
        Assert.IsType<ConflictObjectResult>(r); Assert.Equal(BuyerTakeOverPolicy.CodeNotApplicable, CodeOf(r));
        Assert.Equal(s.Owner, (await StateAsync(ctx, s)).BuyerId); orchestrator.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(S_CANCELLED)] [InlineData(S_REJECTED)] [InlineData(S_COMPLETED)] [InlineData(S_PAYMENT_COMPLETED)]
    public async Task Terminal_statuses_refuse_the_takeover(int statusId)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, statusId: statusId);
        var (ctl, orchestrator, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        var r = await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner));
        Assert.IsType<ConflictObjectResult>(r); Assert.Equal(BuyerTakeOverPolicy.CodeTerminalStatus, CodeOf(r));
        Assert.Equal(s.Owner, (await StateAsync(ctx, s)).BuyerId); orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Non_terminal_approval_status_is_allowed()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, statusId: S_WAITING_AREA);
        var (ctl, _, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.IsType<OkObjectResult>(await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner)));
        Assert.Equal(s.Buyer, (await StateAsync(ctx, s)).BuyerId);
    }

    [Theory]
    [InlineData("")] [InlineData("   \t ")] [InlineData(null)]
    public async Task Missing_or_blank_justification_is_refused_before_any_change(string? justification)
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (ctl, orchestrator, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        var r = await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner, justification!));
        Assert.IsType<BadRequestObjectResult>(r); Assert.Equal(BuyerTakeOverPolicy.CodeJustificationRequired, CodeOf(r));
        Assert.Equal(s.Owner, (await StateAsync(ctx, s)).BuyerId); orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Over_long_justification_is_refused_and_the_limit_itself_is_accepted()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (ctl, _, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        var tooLong = await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner, new string('x', BuyerTakeOverPolicy.JustificationMaxLength + 1)));
        Assert.IsType<BadRequestObjectResult>(tooLong); Assert.Equal(BuyerTakeOverPolicy.CodeJustificationTooLong, CodeOf(tooLong));
        Assert.Equal(s.Owner, (await StateAsync(ctx, s)).BuyerId);
        Assert.IsType<OkObjectResult>(await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner, "  " + new string('y', BuyerTakeOverPolicy.JustificationMaxLength) + "  ")));   // trimmed length == limit
    }

    [Fact]
    public async Task Expected_owner_omitted_explicit_null_and_invalid_are_distinguished_and_refused()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (ctl, orchestrator, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        var omitted = await ctl.TakeOverRequest(s.RequestId, new TakeOverRequestDto { Justification = "ok" });   // field absent → ValueKind.Undefined
        Assert.IsType<BadRequestObjectResult>(omitted); Assert.Equal(BuyerTakeOverPolicy.CodeExpectedOwnerMissing, CodeOf(omitted));
        var explicitNull = await ctl.TakeOverRequest(s.RequestId, Dto(null));
        Assert.IsType<BadRequestObjectResult>(explicitNull); Assert.Equal(BuyerTakeOverPolicy.CodeExpectedOwnerNull, CodeOf(explicitNull));
        var invalid = await ctl.TakeOverRequest(s.RequestId, new TakeOverRequestDto { Justification = "ok", ExpectedBuyerId = Json("\"not-a-guid\"") });
        Assert.IsType<BadRequestObjectResult>(invalid); Assert.Equal(BuyerTakeOverPolicy.CodeExpectedOwnerInvalid, CodeOf(invalid));
        var noBody = await ctl.TakeOverRequest(s.RequestId, null);
        Assert.IsType<BadRequestObjectResult>(noBody); Assert.Equal(BuyerTakeOverPolicy.CodeJustificationRequired, CodeOf(noBody));
        Assert.Equal(s.Owner, (await StateAsync(ctx, s)).BuyerId); orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Stale_expected_owner_is_refused_with_the_current_owner_and_nothing_changes()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var (ctl, orchestrator, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        var r = await ctl.TakeOverRequest(s.RequestId, Dto(Guid.NewGuid() /* the owner the stale screen showed */));
        var pd = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(r).Value);
        Assert.Equal(BuyerTakeOverPolicy.CodeStale, pd.Extensions["code"]); Assert.Equal(s.Owner, pd.Extensions["currentBuyerId"]); Assert.Equal("Samuel Mpoco", pd.Extensions["currentBuyerName"]);
        var st = await StateAsync(ctx, s);
        Assert.Equal(s.Owner, st.BuyerId); Assert.Equal(0, st.Transfers); Assert.Equal(0, st.FieldChanges); orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unassigned_request_is_not_a_takeover()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, ownerIsSeededOwner: false);
        var (ctl, orchestrator, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        var r = await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner));
        Assert.IsType<ConflictObjectResult>(r); Assert.Equal(BuyerTakeOverPolicy.CodeNotAssigned, CodeOf(r));
        Assert.Null((await StateAsync(ctx, s)).BuyerId); orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Already_owner_is_idempotent_without_audit_or_event()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, assignedTo: null, ownerIsSeededOwner: true);
        var (ctl, orchestrator, _) = Ctl(ctx, s.Owner, RoleConstants.Buyer);
        var ok = Assert.IsType<OkObjectResult>(await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner)));
        Assert.Equal(BuyerTakeOverPolicy.CodeAlreadyOwner, Assert.IsType<TakeOverRequestResultDto>(ok.Value).Code);
        var st = await StateAsync(ctx, s);
        Assert.Equal(s.Owner, st.BuyerId); Assert.Equal(0, st.Transfers); Assert.Equal(0, st.Histories); orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Out_of_scope_Buyer_gets_404_before_any_rule_or_change()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, plantId: 1);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = s.Buyer, PlantId = 9 }); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (ctl, orchestrator, _) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.IsType<NotFoundObjectResult>(await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner)));
        Assert.Equal(s.Owner, (await StateAsync(ctx, s)).BuyerId); orchestrator.VerifyNoOtherCalls();
    }

    // ───────────── existing claim / coordinator-assignment paths: single notification source ─────────────

    [Fact]
    public async Task Claim_and_coordinator_assignment_emit_one_BUYER_ASSIGNED_event_and_no_direct_in_app_notice()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, ownerIsSeededOwner: false);
        var (claimCtl, claimOrch, claimNotif) = Ctl(ctx, s.Buyer, RoleConstants.Buyer);
        Assert.IsType<NoContentResult>(await claimCtl.AssignBuyer(s.RequestId));
        claimOrch.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.BuyerAssigned && e.BuyerId == s.Buyer && e.ActorUserId == s.Buyer && e.PoGroupId == null)), Times.Once);
        claimNotif.VerifyNoOtherCalls();

        await using var ctx2 = NewCtx(); var s2 = await SeedAsync(ctx2, ownerIsSeededOwner: false);
        var (coordCtl, coordOrch, coordNotif) = Ctl(ctx2, s2.Admin, RoleConstants.SystemAdministrator);
        Assert.IsType<NoContentResult>(await coordCtl.AssignBuyer(s2.RequestId, targetUserId: s2.Buyer));
        coordOrch.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.BuyerAssigned && e.BuyerId == s2.Buyer && e.ActorUserId == s2.Admin)), Times.Once);
        coordNotif.VerifyNoOtherCalls();
        Assert.Equal(s2.Buyer, (await StateAsync(ctx2, s2)).BuyerId);
    }
}
