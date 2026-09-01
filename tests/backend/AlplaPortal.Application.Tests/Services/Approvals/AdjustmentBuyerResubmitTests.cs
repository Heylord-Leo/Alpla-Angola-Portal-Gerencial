using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Adjustment V2 Phase 4 — the BUYER resolution side of <c>ResubmitBatch</c>. A batch with an OPEN
/// structured cycle requires the Buyer's mandatory "Resposta ao reajuste"; on resubmit exactly one
/// BUYER <see cref="ApprovalBatchAdjustmentResolution"/> is recorded, the cycle closes RESUBMITTED,
/// the batch returns to WAITING_AREA_APPROVAL (from Area OR Final adjustment) and the Area approver
/// is notified. Legacy batches with NO cycle keep the comment-only resubmit — no response required,
/// no resolution written. InMemory harness (the relational unique-index / atomicity guarantees live
/// in <see cref="AdjustmentCycleServiceTests"/>).
/// </summary>
public class AdjustmentBuyerResubmitTests
{
    private sealed record Seed(Guid RequestId, Guid BatchId, Guid LineItemId, Guid ActorId, Guid? CycleId);

    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    /// <param name="batchStatus">AREA_ADJUSTMENT or FINAL_ADJUSTMENT.</param>
    /// <param name="withCycle">Seed an OPEN V2 cycle (WAITING_BUYER) on the batch.</param>
    /// <param name="cycleSourceStage">AREA or FINAL for the seeded cycle.</param>
    private static async Task<Seed> SeedAsync(
        DbContextOptions<ApplicationDbContext> options,
        string batchStatus,
        bool withCycle,
        string cycleSourceStage = AdjustmentConstants.SourceStages.Area)
    {
        await using var ctx = new ApplicationDbContext(options);
        var actor = new User { Id = Guid.NewGuid(), FullName = "Comprador Teste", Email = $"buyer-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 1, Code = "WAITING_AREA_APPROVAL", Name = "Aguardando Área" });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });

        var request = new Request
        {
            Id = Guid.NewGuid(), Title = "REQ Phase4", RequestNumber = "REQ-P4",
            StatusId = 1, RequestTypeId = 1, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = actor.Id, BuyerId = actor.Id, CreatedAtUtc = DateTime.UtcNow,
        };
        ctx.Requests.Add(request);

        var li = new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1, Description = "Item 1",
            Quantity = 1, UnitPrice = 100m, TotalAmount = 100m, PlantId = 1, IsDeleted = false, CreatedAtUtc = DateTime.UtcNow,
        };
        ctx.RequestLineItems.Add(li);

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1, Status = batchStatus,
            CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id,
        };
        ctx.ApprovalBatches.Add(batch);
        ctx.ApprovalBatchItems.Add(new ApprovalBatchItem
        {
            Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id, CreatedAtUtc = DateTime.UtcNow,
        });

        Guid? cycleId = null;
        if (withCycle)
        {
            var cycle = new ApprovalBatchAdjustment
            {
                Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, CycleNumber = 1,
                SourceStage = cycleSourceStage, Status = AdjustmentConstants.States.WaitingBuyer,
                WholeBatch = false, ApproverComment = "Rever preço.", RequestedByUserId = actor.Id,
                RequestedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow,
            };
            cycle.Reasons.Add(new ApprovalBatchAdjustmentReason
            {
                Id = Guid.NewGuid(), ReasonCode = AdjustmentConstants.ReasonCodes.PriceNegotiation, CreatedAtUtc = DateTime.UtcNow,
            });
            ctx.ApprovalBatchAdjustments.Add(cycle);
            cycleId = cycle.Id;
        }

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, batch.Id, li.Id, actor.Id, cycleId);
    }

    private static ApprovalBatchController BuildController(
        ApplicationDbContext ctx, Guid actorId, IWorkflowNotificationOrchestrator orchestrator)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto { Managers = { new AreaManagerDto { UserId = actorId, FullName = "Area Mgr" } } });

        var controller = new ApprovalBatchController(
            ctx,
            NullLogger<ApprovalBatchController>.Instance,
            new Mock<IRequestStatusSyncService>().Object,
            new GroupBuilderService(ctx),
            routing.Object,
            new QuotationItemEligibilityService(ctx),
            new BatchExtraItemDecisionService(ctx),
            new AdjustmentCycleService(ctx),
            orchestrator);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    // ── A. V2 Area resubmit with response → cycle RESUBMITTED, batch back to Area, 1 BUYER resolution, notified ──
    [Fact]
    public async Task ResubmitBatch_V2AreaCycle_WithResponse_ResolvesAndReturnsToArea_AndNotifies()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options, RequestConstants.ApprovalBatchStatuses.AreaAdjustment, withCycle: true);
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();

        await using (var ctx = new ApplicationDbContext(options))
        {
            var controller = BuildController(ctx, seed.ActorId, orchestrator.Object);
            var result = await controller.ResubmitBatch(seed.RequestId, seed.BatchId,
                new BatchApprovalActionDto { AdjustmentResponse = "Cotação corrigida conforme solicitado." });
            Assert.IsType<OkObjectResult>(result);
        }

        await using (var verify = new ApplicationDbContext(options))
        {
            var batch = await verify.ApprovalBatches.SingleAsync(b => b.Id == seed.BatchId);
            Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, batch.Status);

            var cycle = await verify.ApprovalBatchAdjustments.Include(a => a.Resolutions).SingleAsync(a => a.Id == seed.CycleId);
            Assert.Equal(AdjustmentConstants.States.Resubmitted, cycle.Status);
            Assert.NotNull(cycle.ClosedAtUtc);
            var res = Assert.Single(cycle.Resolutions);
            Assert.Equal(AdjustmentConstants.ActorTypes.Buyer, res.ActorType);
            Assert.Equal("Cotação corrigida conforme solicitado.", res.ResolutionComment);
            Assert.Equal(seed.ActorId, res.ResolvedByUserId);
        }

        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(
            e => e.EventCode == WorkflowEventCodes.BatchResubmitted && e.CorrelationId == seed.CycleId)), Times.Once);
    }

    // ── B. V2 Final resubmit with response → batch returns to Area (Final routes back via Area) ──
    [Fact]
    public async Task ResubmitBatch_V2FinalCycle_WithResponse_ReturnsToArea_AndResolves()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options, RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
            withCycle: true, cycleSourceStage: AdjustmentConstants.SourceStages.Final);
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();

        await using (var ctx = new ApplicationDbContext(options))
        {
            var controller = BuildController(ctx, seed.ActorId, orchestrator.Object);
            var result = await controller.ResubmitBatch(seed.RequestId, seed.BatchId,
                new BatchApprovalActionDto { AdjustmentResponse = "Ajustado após aprovação final." });
            Assert.IsType<OkObjectResult>(result);
        }

        await using (var verify = new ApplicationDbContext(options))
        {
            var batch = await verify.ApprovalBatches.SingleAsync(b => b.Id == seed.BatchId);
            Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, batch.Status);
            var cycle = await verify.ApprovalBatchAdjustments.Include(a => a.Resolutions).SingleAsync(a => a.Id == seed.CycleId);
            Assert.Equal(AdjustmentConstants.States.Resubmitted, cycle.Status);
            Assert.Single(cycle.Resolutions);
        }
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Once);
    }

    // ── C. Missing response (null) on a V2 cycle → 400, nothing changes ──
    [Fact]
    public async Task ResubmitBatch_V2Cycle_MissingResponse_Returns400_NoMutation()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options, RequestConstants.ApprovalBatchStatuses.AreaAdjustment, withCycle: true);
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();

        await using (var ctx = new ApplicationDbContext(options))
        {
            var controller = BuildController(ctx, seed.ActorId, orchestrator.Object);
            var result = await controller.ResubmitBatch(seed.RequestId, seed.BatchId, new BatchApprovalActionDto { AdjustmentResponse = null });
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, ((ProblemDetails)bad.Value!).Status);
        }

        await using (var verify = new ApplicationDbContext(options))
        {
            var batch = await verify.ApprovalBatches.SingleAsync(b => b.Id == seed.BatchId);
            Assert.Equal(RequestConstants.ApprovalBatchStatuses.AreaAdjustment, batch.Status); // unchanged
            var cycle = await verify.ApprovalBatchAdjustments.Include(a => a.Resolutions).SingleAsync(a => a.Id == seed.CycleId);
            Assert.Equal(AdjustmentConstants.States.WaitingBuyer, cycle.Status); // still open
            Assert.Empty(cycle.Resolutions);
        }
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Never);
    }

    // ── D. Whitespace-only response on a V2 cycle → 400 (trim-aware) ──
    [Fact]
    public async Task ResubmitBatch_V2Cycle_WhitespaceResponse_Returns400()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options, RequestConstants.ApprovalBatchStatuses.AreaAdjustment, withCycle: true);

        await using var ctx = new ApplicationDbContext(options);
        var controller = BuildController(ctx, seed.ActorId, new Mock<IWorkflowNotificationOrchestrator>().Object);
        var result = await controller.ResubmitBatch(seed.RequestId, seed.BatchId, new BatchApprovalActionDto { AdjustmentResponse = "   \t  " });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── E. Legacy batch with NO cycle → resubmit succeeds without response; no resolution created ──
    [Fact]
    public async Task ResubmitBatch_LegacyNoCycle_NoResponse_Succeeds_NoResolution()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options, RequestConstants.ApprovalBatchStatuses.AreaAdjustment, withCycle: false);
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();

        await using (var ctx = new ApplicationDbContext(options))
        {
            var controller = BuildController(ctx, seed.ActorId, orchestrator.Object);
            var result = await controller.ResubmitBatch(seed.RequestId, seed.BatchId, new BatchApprovalActionDto()); // no response
            Assert.IsType<OkObjectResult>(result);
        }

        await using (var verify = new ApplicationDbContext(options))
        {
            var batch = await verify.ApprovalBatches.SingleAsync(b => b.Id == seed.BatchId);
            Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, batch.Status);
            Assert.False(await verify.Set<ApprovalBatchAdjustmentResolution>().AnyAsync());
        }
        // Legacy resubmit keeps the pre-V2 behavior — no BATCH_RESUBMITTED_TO_AREA V2 notification.
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Never);
    }

    // ── F. Idempotency: a second resubmit after the cycle already closed is a legacy no-op — it does
    //      NOT add a second BUYER resolution (the cycle is no longer open). ──
    [Fact]
    public async Task ResubmitBatch_SecondResubmitAfterClose_DoesNotDuplicateResolution()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options, RequestConstants.ApprovalBatchStatuses.AreaAdjustment, withCycle: true);

        await using (var ctx = new ApplicationDbContext(options))
        {
            var controller = BuildController(ctx, seed.ActorId, new Mock<IWorkflowNotificationOrchestrator>().Object);
            Assert.IsType<OkObjectResult>(await controller.ResubmitBatch(seed.RequestId, seed.BatchId,
                new BatchApprovalActionDto { AdjustmentResponse = "Primeira resposta." }));
        }

        // The batch is now WAITING_AREA_APPROVAL, so a naive re-post is rejected by the rework gate,
        // but even forcing the batch back to adjustment must not create a second resolution.
        await using (var reopen = new ApplicationDbContext(options))
        {
            var b = await reopen.ApprovalBatches.SingleAsync(x => x.Id == seed.BatchId);
            b.Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment;
            await reopen.SaveChangesAsync();
        }
        await using (var ctx = new ApplicationDbContext(options))
        {
            var controller = BuildController(ctx, seed.ActorId, new Mock<IWorkflowNotificationOrchestrator>().Object);
            // No open cycle remains (it closed RESUBMITTED) → treated as legacy resubmit, response not required.
            Assert.IsType<OkObjectResult>(await controller.ResubmitBatch(seed.RequestId, seed.BatchId, new BatchApprovalActionDto()));
        }

        await using (var verify = new ApplicationDbContext(options))
        {
            Assert.Equal(1, await verify.Set<ApprovalBatchAdjustmentResolution>().CountAsync());
        }
    }
}
