using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;
using C = AlplaPortal.Domain.Constants.PoGroupCancellationConstants;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// D12 evidence at the API boundary: the Finance confirm / reject endpoints (`FinancePoCancellationController`) are called with
/// nothing but a ClaimsPrincipal, exactly as the HTTP pipeline would, and the decision is made server-side from the database
/// (role assignment, plant scope, active flag, declarer identity) — a role CLAIM alone is never enough, the UI is not involved.
/// </summary>
public class PoCancellationFinanceAuthorizationApiTests
{
    private static FinancePoCancellationController Ctl(ApplicationDbContext ctx, Guid userId, params string[] roleClaims)
    {
        var (orch, _) = S.OrchestratorMock();
        var ctl = new FinancePoCancellationController(ctx, S.Service(ctx, orch.Object));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roleClaims.Select(r => new Claim(ClaimTypes.Role, r)));
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        return ctl;
    }

    private static async Task<(ApplicationDbContext Ctx, S.Seed Seed, Guid CancellationId)> CancelledAsync()
    {
        var ctx = S.NewCtx(); var s = await S.SeedAsync(ctx);
        var (orch, _) = S.OrchestratorMock();
        var r = await S.Service(ctx, orch.Object).CancelAsync(S.Cancel(s, S.Buyer(s.Owner)));
        Assert.True(r.Succeeded, r.Detail);
        ctx.ChangeTracker.Clear();
        return (ctx, s, ((PoGroupCancellationResultDto)r.Payload!).CancellationId);
    }

    private static (int Status, string? Code) Outcome(IActionResult r)
    {
        var obj = (ObjectResult)r;
        var code = obj.Value is ProblemDetails pd && pd.Extensions.TryGetValue("code", out var c) ? c as string : null;
        return (obj.StatusCode ?? 200, code);
    }

    private static async Task AssertUnchanged(ApplicationDbContext ctx, Guid id)
    {
        ctx.ChangeTracker.Clear();
        var c = await ctx.RequestPoGroupCancellations.AsNoTracking().Include(x => x.EcfEvents).SingleAsync(x => x.Id == id);
        Assert.Equal("PENDING", c.FinanceConfirmationStatus); Assert.Null(c.FinanceConfirmedByUserId); Assert.Null(c.FinanceReviewedByUserId);
        Assert.Single(c.EcfEvents);   // only the Buyer's declaration
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == C.HistoryActions.FinanceConfirmed || h.ActionTaken == C.HistoryActions.FinanceRejected));
    }

    [Fact]
    public async Task SysAdmin_alone_cannot_confirm_or_reject()
    {
        var (ctx, s, id) = await CancelledAsync();
        var confirm = Outcome(await Ctl(ctx, s.Admin, RoleConstants.SystemAdministrator).Confirm(id, new FinanceCancellationReviewDto()));
        var reject = Outcome(await Ctl(ctx, s.Admin, RoleConstants.SystemAdministrator).Reject(id, new FinanceCancellationReviewDto { Note = "A ECF continua ativa no Primavera." }));
        Assert.Equal((403, C.ErrorCodes.FinanceScopeRequired), confirm); Assert.Equal((403, C.ErrorCodes.FinanceScopeRequired), reject);
        await AssertUnchanged(ctx, id);
    }

    [Fact]
    public async Task Finance_claim_without_a_database_role_assignment_is_refused()
    {
        var (ctx, s, id) = await CancelledAsync();
        // OtherBuyer presents a forged/stale Finance claim; the database holds no Finance assignment for them.
        var r = Outcome(await Ctl(ctx, s.OtherBuyer, RoleConstants.Finance).Confirm(id, new FinanceCancellationReviewDto()));
        Assert.Equal((403, C.ErrorCodes.FinanceScopeRequired), r);
        await AssertUnchanged(ctx, id);
    }

    [Fact]
    public async Task Finance_user_without_the_plant_scope_cannot_confirm_or_reject()
    {
        var (ctx, s, id) = await CancelledAsync();
        // Finance of another plant is outside the request scope → 404 (nothing disclosed), both for confirm and reject
        Assert.Equal(404, Outcome(await Ctl(ctx, s.FinanceOtherPlant, RoleConstants.Finance).Confirm(id, new FinanceCancellationReviewDto())).Status);
        Assert.Equal(404, Outcome(await Ctl(ctx, s.FinanceOtherPlant, RoleConstants.Finance).Reject(id, new FinanceCancellationReviewDto { Note = "Motivo suficientemente longo para rejeitar." })).Status);
        // A Finance user who can SEE the request (plant 1 scope) but whose group belongs to another plant is refused 403
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupA); g.PlantId = S.OtherPlant; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.Equal((403, C.ErrorCodes.FinanceScopeRequired), Outcome(await Ctl(ctx, s.Finance1, RoleConstants.Finance).Confirm(id, new FinanceCancellationReviewDto())));
        Assert.Equal((403, C.ErrorCodes.FinanceScopeRequired), Outcome(await Ctl(ctx, s.Finance1, RoleConstants.Finance).Reject(id, new FinanceCancellationReviewDto { Note = "Motivo suficientemente longo para rejeitar." })));
        await AssertUnchanged(ctx, id);
    }

    [Fact]
    public async Task Inactive_Finance_user_is_refused()
    {
        var (ctx, s, id) = await CancelledAsync();
        Assert.Equal((403, C.ErrorCodes.FinanceScopeRequired), Outcome(await Ctl(ctx, s.FinanceInactive, RoleConstants.Finance).Confirm(id, new FinanceCancellationReviewDto())));
        await AssertUnchanged(ctx, id);
    }

    [Fact]
    public async Task Nobody_confirms_their_own_declaration_even_with_Finance_and_SysAdmin_roles()
    {
        var (ctx, s, id) = await CancelledAsync();
        // the declaring Buyer is ALSO granted Finance (DB assignment + plant scope) and SysAdmin
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = s.Owner, RoleId = S.R_FINANCE }, new UserRoleAssignment { UserId = s.Owner, RoleId = S.R_ADMIN });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var all = new[] { RoleConstants.Buyer, RoleConstants.Finance, RoleConstants.SystemAdministrator };
        Assert.Equal((403, C.ErrorCodes.SelfConfirmationForbidden), Outcome(await Ctl(ctx, s.Owner, all).Confirm(id, new FinanceCancellationReviewDto())));
        Assert.Equal((403, C.ErrorCodes.SelfConfirmationForbidden), Outcome(await Ctl(ctx, s.Owner, all).Reject(id, new FinanceCancellationReviewDto { Note = "Motivo suficientemente longo para rejeitar." })));
        await AssertUnchanged(ctx, id);
        // after a Buyer UPDATE of the declaration by another responsible user, the new declarer is the one locked out
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupA); g.PoResponsibleBuyerId = s.OtherBuyer; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (orch, _) = S.OrchestratorMock();
        Assert.True((await S.Service(ctx, orch.Object).UpdateEcfDeclarationAsync(new UpdateEcfDeclarationCommand(id, S.Buyer(s.OtherBuyer), "DECLARED_CANCELLED", "ECF10 2026/364", null, null, null))).Succeeded);
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = s.OtherBuyer, RoleId = S.R_FINANCE }); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.Equal((403, C.ErrorCodes.SelfConfirmationForbidden), Outcome(await Ctl(ctx, s.OtherBuyer, RoleConstants.Finance).Confirm(id, new FinanceCancellationReviewDto())));
    }

    [Fact]
    public async Task Active_Finance_user_with_the_plant_scope_confirms_or_rejects_another_users_declaration()
    {
        var (ctx, s, id) = await CancelledAsync();
        var rejected = await Ctl(ctx, s.Finance1, RoleConstants.Finance).Reject(id, new FinanceCancellationReviewDto { Note = "A ECF 2026/364 continua ativa no Primavera." });
        var dto1 = Assert.IsType<PoGroupCancellationDto>(Assert.IsType<OkObjectResult>(rejected).Value);
        Assert.Equal("REJECTED", dto1.FinanceConfirmationStatus); Assert.Equal("Finance Um", dto1.FinanceReviewedByName); Assert.Equal("Comprador Dono", dto1.EcfDeclaredByName);
        var confirmed = await Ctl(ctx, s.Finance2, RoleConstants.Finance).Confirm(id, new FinanceCancellationReviewDto { Note = "Verificado no Primavera." });
        var dto2 = Assert.IsType<PoGroupCancellationDto>(Assert.IsType<OkObjectResult>(confirmed).Value);
        Assert.Equal("CONFIRMED", dto2.FinanceConfirmationStatus); Assert.Equal("Finance Dois", dto2.FinanceConfirmedByName); Assert.NotNull(dto2.FinanceConfirmedAtUtc);
        Assert.Contains(dto2.Events, e => e.EventType == "FINANCE_REJECTED"); Assert.Contains(dto2.Events, e => e.EventType == "FINANCE_CONFIRMED");
        // a SysAdmin who ALSO holds Finance with the plant scope acts in the Finance capacity (on a fresh cancellation)
        var (ctx2, s2, id2) = await CancelledAsync();
        ctx2.UserRoleAssignments.Add(new UserRoleAssignment { UserId = s2.Admin, RoleId = S.R_FINANCE }); ctx2.UserPlantScopes.Add(new UserPlantScope { UserId = s2.Admin, PlantId = S.Plant }); await ctx2.SaveChangesAsync(); ctx2.ChangeTracker.Clear();
        Assert.IsType<OkObjectResult>(await Ctl(ctx2, s2.Admin, RoleConstants.SystemAdministrator, RoleConstants.Finance).Confirm(id2, new FinanceCancellationReviewDto()));
    }

    [Fact]
    public async Task Queue_endpoint_is_Finance_or_SysAdmin_only_and_marks_reviewability_per_caller()
    {
        var (ctx, s, id) = await CancelledAsync();
        Assert.Equal(403, Outcome(await Ctl(ctx, s.Owner, RoleConstants.Buyer).List(null, null, null)).Status);
        var list = Assert.IsType<FinancePoCancellationListDto>(Assert.IsType<OkObjectResult>(await Ctl(ctx, s.Finance1, RoleConstants.Finance).List(null, null, null)).Value);
        Assert.True(Assert.Single(list.Items).CanReview);
        var adminList = Assert.IsType<FinancePoCancellationListDto>(Assert.IsType<OkObjectResult>(await Ctl(ctx, s.Admin, RoleConstants.SystemAdministrator).List(null, null, null)).Value);
        Assert.False(Assert.Single(adminList.Items).CanReview);
    }
}
