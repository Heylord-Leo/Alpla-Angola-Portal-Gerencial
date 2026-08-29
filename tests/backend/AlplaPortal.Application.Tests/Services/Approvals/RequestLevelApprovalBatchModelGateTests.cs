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
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
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

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Defense-in-depth batch-model gate (REQ-20/08/2026-274 ghost-card class): the REQUEST-LEVEL
/// area/final approval endpoints must refuse APPROVE / REJECT / REQUEST_ADJUSTMENT for a QUOTATION
/// request that has ANY ApprovalBatch — those requests are decided per lot, and the request-wide
/// legacy path could otherwise act (e.g. request-wide Reject) while a lot sits in FINAL_ADJUSTMENT
/// owned by the Buyer and the scalar intentionally reads WAITING_FINAL_APPROVAL. The gate protects
/// stale browser tabs and direct API calls; PAYMENT and true legacy zero-batch QUOTATION requests
/// must pass through unchanged (gate-passing tests assert the request reaches the deterministic
/// NEXT check, proving the gate itself did not block). InMemory-EF direct-controller pattern.
/// </summary>
public class RequestLevelApprovalBatchModelGateTests
{
    private const string GateTitle = "Aprovação por Lotes";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId, params string[] roles)
    {
        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object,
            new Mock<IApprovalRoutingService>().Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object,
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(new PostPaymentCompletionOptions()));

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid ActorId);

    /// <summary>Seeds a request of <paramref name="typeCode"/> at <paramref name="statusCode"/>,
    /// optionally with one ApprovalBatch in <paramref name="batchStatus"/> (the batch-model marker).</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string typeCode, string statusCode, string? batchStatus)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Approver Tester", Email = $"gate-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(actor);

        var status = new RequestStatus { Id = 501, Code = statusCode, Name = statusCode };
        ctx.RequestStatuses.Add(status);
        var type = new RequestType { Id = 601, Code = typeCode, Name = typeCode };
        ctx.RequestTypes.Add(type);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST gate",
            RequestNumber = "ZZT-GATE-" + Guid.NewGuid().ToString("N")[..6],
            StatusId = status.Id,
            RequestTypeId = type.Id,
            DepartmentId = 4,
            CompanyId = 1,
            PlantId = 1,
            RequesterId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        if (batchStatus != null)
        {
            ctx.ApprovalBatches.Add(new ApprovalBatch
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                BatchNumber = 1,
                Status = batchStatus,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actor.Id
            });
        }

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static string? TitleOf(IActionResult result) =>
        ((result as BadRequestObjectResult)?.Value as ProblemDetails)?.Title;

    // ── FINAL: batch-model QUOTATION → every request-level action refused by the gate ──

    [Fact]
    public async Task FinalReject_BatchModelQuotation_IsRefused()
    {
        await using var ctx = NewContext();
        var seed = await SeedAsync(ctx, "QUOTATION", "WAITING_FINAL_APPROVAL", "FINAL_ADJUSTMENT");
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.FinalApprover);

        var result = await controller.RejectFinal(seed.RequestId, new ApprovalActionDto { Comment = "Rejeição indevida." });

        Assert.Equal(GateTitle, TitleOf(result));
    }

    [Fact]
    public async Task FinalApprove_BatchModelQuotation_IsRefused()
    {
        await using var ctx = NewContext();
        var seed = await SeedAsync(ctx, "QUOTATION", "WAITING_FINAL_APPROVAL", "FINAL_ADJUSTMENT");
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.FinalApprover);

        var result = await controller.ApproveFinal(seed.RequestId, new ApprovalActionDto { Comment = "ok" });

        Assert.Equal(GateTitle, TitleOf(result));
    }

    [Fact]
    public async Task FinalAdjustment_BatchModelQuotation_IsRefused()
    {
        await using var ctx = NewContext();
        var seed = await SeedAsync(ctx, "QUOTATION", "WAITING_FINAL_APPROVAL", "FINAL_ADJUSTMENT");
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.FinalApprover);

        var result = await controller.RequestAdjustmentFinal(seed.RequestId, new ApprovalActionDto { Comment = "Motivo." });

        Assert.Equal(GateTitle, TitleOf(result));
    }

    // A batch in a NORMAL waiting state also marks the batch model — the gate keys on batch
    // EXISTENCE, not on the adjustment state (request-wide actions are wrong either way).
    [Fact]
    public async Task FinalReject_QuotationWithWaitingBatch_IsAlsoRefused()
    {
        await using var ctx = NewContext();
        var seed = await SeedAsync(ctx, "QUOTATION", "WAITING_FINAL_APPROVAL", "WAITING_FINAL_APPROVAL");
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.FinalApprover);

        var result = await controller.RejectFinal(seed.RequestId, new ApprovalActionDto { Comment = "Rejeição indevida." });

        Assert.Equal(GateTitle, TitleOf(result));
    }

    // ── AREA: symmetric refusals (admin bypasses the manager check; the gate still refuses) ──

    [Fact]
    public async Task AreaReject_BatchModelQuotation_IsRefused()
    {
        await using var ctx = NewContext();
        var seed = await SeedAsync(ctx, "QUOTATION", "WAITING_AREA_APPROVAL", "AREA_ADJUSTMENT");
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.SystemAdministrator);

        var result = await controller.RejectArea(seed.RequestId, new ApprovalActionDto { Comment = "Rejeição indevida." });

        Assert.Equal(GateTitle, TitleOf(result));
    }

    [Fact]
    public async Task AreaApprove_BatchModelQuotation_IsRefused()
    {
        await using var ctx = NewContext();
        var seed = await SeedAsync(ctx, "QUOTATION", "WAITING_AREA_APPROVAL", "AREA_ADJUSTMENT");
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.SystemAdministrator);

        var result = await controller.ApproveArea(seed.RequestId, new ApprovalActionDto { Comment = "ok" });

        Assert.Equal(GateTitle, TitleOf(result));
    }

    [Fact]
    public async Task AreaAdjustment_BatchModelQuotation_IsRefused()
    {
        await using var ctx = NewContext();
        var seed = await SeedAsync(ctx, "QUOTATION", "WAITING_AREA_APPROVAL", "AREA_ADJUSTMENT");
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.SystemAdministrator);

        var result = await controller.RequestAdjustmentArea(seed.RequestId, new ApprovalActionDto { Comment = "Motivo." });

        Assert.Equal(GateTitle, TitleOf(result));
    }

    // ── Gate-passing: PAYMENT and legacy zero-batch QUOTATION reach the deterministic NEXT check ──

    [Fact]
    public async Task PaymentRequestLevel_PassesGate_ReachesPaymentAdjustmentRule()
    {
        await using var ctx = NewContext();
        var seed = await SeedAsync(ctx, "PAYMENT", "WAITING_FINAL_APPROVAL", batchStatus: null);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.FinalApprover);

        // PAYMENT never allows REQUEST_ADJUSTMENT — that refusal is the check immediately AFTER
        // the batch-model gate, so reaching it proves the gate passed the request through.
        var result = await controller.RequestAdjustmentFinal(seed.RequestId, new ApprovalActionDto { Comment = "Motivo." });

        Assert.Equal("Ação Inválida", TitleOf(result));
        Assert.NotEqual(GateTitle, TitleOf(result));
    }

    [Fact]
    public async Task LegacyZeroBatchQuotation_PassesGate_ReachesPoGroupRule()
    {
        await using var ctx = NewContext();
        var seed = await SeedAsync(ctx, "QUOTATION", "WAITING_FINAL_APPROVAL", batchStatus: null);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.FinalApprover);

        // Zero-batch QUOTATION approve next hits the "no PO groups" rule — deterministic proof
        // the batch-model gate did not block the legacy path.
        var result = await controller.ApproveFinal(seed.RequestId, new ApprovalActionDto { Comment = "ok" });

        Assert.Equal("Ação Inválida", TitleOf(result));
        Assert.NotEqual(GateTitle, TitleOf(result));
    }
}
