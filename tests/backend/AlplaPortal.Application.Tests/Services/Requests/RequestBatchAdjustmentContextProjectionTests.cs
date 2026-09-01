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

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Adjustment V2 Phase 4 (Buyer context UX fix): GET /requests/{id} projects each batch's OPEN
/// structured adjustment cycle onto <see cref="RequestApprovalBatchDto.OpenAdjustmentCycle"/> —
/// STRUCTURED reasons kept distinct from the approver's free-text comment, item-scoped reasons
/// resolved to a business-readable line (number + catalog code + description, never a GUID). Legacy
/// pre-V2 batches (no cycle) and already-closed cycles must project null so the modal falls back to
/// the QF1 scalar. Runs on the real relational model (LocalDB) — GetRequest's large split projection
/// is not faithfully materialized by the InMemory provider; CanConnect() gates every fixture.
/// </summary>
[Collection("IntegrationTests")]
public class RequestBatchAdjustmentContextProjectionTests
{
    static RequestBatchAdjustmentContextProjectionTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var tableId = ctx.Database
                    .SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.ApprovalBatchAdjustments'), 0) AS [Value]")
                    .AsEnumerable().First();
                if (tableId == 0) ctx.Database.EnsureDeleted();
            }
            ctx.Database.EnsureCreated();
        }
        catch { /* LocalDB unavailable — CanConnect() gates every test. */ }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> DbOptions() => IntegrationTestDatabase.CreateOptions();

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        // GetRequest awaits two service calls on non-context dependencies — configure just those.
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto());
        var statusSync = new Mock<IRequestStatusSyncService>();
        statusSync.Setup(s => s.ComputeDisplayWorkflowStateAsync(It.IsAny<Guid>())).ReturnsAsync(string.Empty);

        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            statusSync.Object,
            routing.Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object,
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(new PostPaymentCompletionOptions()));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, actorId.ToString()), new Claim(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid BatchId, Guid LineItemId, Guid ActorId);

    /// <summary>Request + one line item (linked to a ZZTEST catalog code) + one ApprovalBatch
    /// (AREA_ADJUSTMENT, deliberately item-less so GetRequest's per-item mocked-service paths never
    /// run). Returns null when the shared lookups are absent (older sandbox → skip).</summary>
    private static async Task<Seed?> SeedAsync()
    {
        await using var ctx = new ApplicationDbContext(DbOptions());
        var actor = new User { Id = Guid.NewGuid(), FullName = "Leonardo Cintra", Email = $"zztest-adjctx-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(actor);

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_AREA_APPROVAL").Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstOrDefaultAsync();
        if (statusId == 0 || typeId == 0) return null;

        var catalog = new ItemCatalog { Code = "ZZTEST-ROL-6204", Description = "Rolamento 6204" };
        ctx.ItemCatalogItems.Add(catalog);

        var request = new Request
        {
            Id = Guid.NewGuid(), Title = "ZZTEST_ADJCTX_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-ADJCTX-" + Guid.NewGuid().ToString("N")[..8],
            StatusId = statusId, RequestTypeId = typeId, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = actor.Id, AreaApproverId = actor.Id, CreatedAtUtc = DateTime.UtcNow,
        };
        ctx.Requests.Add(request);

        var li = new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 4, Description = "Rolamento 6204",
            ItemCatalogItem = catalog, Quantity = 1, UnitPrice = 100m, TotalAmount = 100m,
            PlantId = 1, IsDeleted = false, CreatedAtUtc = DateTime.UtcNow,
        };
        ctx.RequestLineItems.Add(li);

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1,
            Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id,
        };
        ctx.ApprovalBatches.Add(batch);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, batch.Id, li.Id, actor.Id);
    }

    private static async Task AddCycleAsync(
        Seed s, string status, string sourceStage, string approverComment,
        string reasonCode, Guid? itemId)
    {
        await using var ctx = new ApplicationDbContext(DbOptions());
        var cycle = new ApprovalBatchAdjustment
        {
            Id = Guid.NewGuid(), ApprovalBatchId = s.BatchId, CycleNumber = 1, SourceStage = sourceStage,
            Status = status, WholeBatch = itemId == null, ApproverComment = approverComment,
            RequestedByUserId = s.ActorId, RequestedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow,
            ClosedAtUtc = AdjustmentConstants.States.Open.Contains(status) ? null : DateTime.UtcNow,
        };
        cycle.Reasons.Add(new ApprovalBatchAdjustmentReason
        {
            Id = Guid.NewGuid(), ReasonCode = reasonCode, RequestLineItemId = itemId, CreatedAtUtc = DateTime.UtcNow,
        });
        ctx.ApprovalBatchAdjustments.Add(cycle);
        await ctx.SaveChangesAsync();
    }

    private static async Task<RequestApprovalBatchDto> GetBatchDtoAsync(Seed s)
    {
        await using var ctx = new ApplicationDbContext(DbOptions());
        var result = await BuildController(ctx, s.ActorId).GetRequest(s.RequestId);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RequestDetailsDto>(ok.Value);
        return Assert.Single(dto.ApprovalBatches);
    }

    private static async Task CleanupAsync(Guid requestId)
    {
        if (requestId == Guid.Empty) return;
        await using var ctx = new ApplicationDbContext(DbOptions());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE a FROM ApprovalBatchAdjustments a INNER JOIN ApprovalBatches b ON b.Id = a.ApprovalBatchId WHERE b.RequestId = {0};" +
            "DELETE abi FROM ApprovalBatchItems abi INNER JOIN ApprovalBatches b ON b.Id = abi.ApprovalBatchId WHERE b.RequestId = {0};" +
            "DELETE FROM ApprovalBatches WHERE RequestId = {0};" +
            "DELETE FROM RequestLineItems WHERE RequestId = {0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId = {0};" +
            "DELETE FROM Requests WHERE Id = {0};" +
            "DELETE FROM ItemCatalogItems WHERE Code LIKE 'ZZTEST-%';" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-adjctx-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId = Users.Id);", requestId);
    }

    // ── Open V2 cycle → structured reason projected; approver comment kept separate ──
    [Fact]
    public async Task OpenCycle_ProjectsStructuredReason_AndApproverCommentSeparately()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await AddCycleAsync(s, AdjustmentConstants.States.WaitingBuyer, AdjustmentConstants.SourceStages.Area, "Reajuste",
                AdjustmentConstants.ReasonCodes.PriceNegotiation, null);

            var batch = await GetBatchDtoAsync(s);

            Assert.True(batch.HasOpenAdjustmentCycle);
            Assert.NotNull(batch.OpenAdjustmentCycle);
            Assert.Equal(1, batch.OpenAdjustmentCycle!.CycleNumber);
            Assert.Equal(AdjustmentConstants.SourceStages.Area, batch.OpenAdjustmentCycle.SourceStage);
            // Approver free-text comment is its OWN field — not a reason code.
            Assert.Equal("Reajuste", batch.OpenAdjustmentCycle.ApproverComment);
            var reason = Assert.Single(batch.OpenAdjustmentCycle.Reasons);
            Assert.Equal(AdjustmentConstants.ReasonCodes.PriceNegotiation, reason.ReasonCode);
            Assert.Null(reason.LineNumber); // whole-lot reason
            Assert.DoesNotContain(batch.OpenAdjustmentCycle.Reasons, r => r.ReasonCode == batch.OpenAdjustmentCycle.ApproverComment);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── Item-scoped reason resolves the affected line (number + code + description), never a GUID ──
    [Fact]
    public async Task OpenCycle_ItemScopedReason_ResolvesLineNumberAndBusinessIdentity()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await AddCycleAsync(s, AdjustmentConstants.States.WaitingBuyer, AdjustmentConstants.SourceStages.Area, "Rever quantidade",
                AdjustmentConstants.ReasonCodes.RequestedQuantity, s.LineItemId);

            var batch = await GetBatchDtoAsync(s);

            var reason = Assert.Single(batch.OpenAdjustmentCycle!.Reasons);
            Assert.Equal(AdjustmentConstants.ReasonCodes.RequestedQuantity, reason.ReasonCode);
            Assert.Equal(4, reason.LineNumber);
            Assert.Equal("ZZTEST-ROL-6204", reason.ItemCatalogCode);
            Assert.Equal("Rolamento 6204", reason.Description);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── Legacy batch with no V2 cycle → projects null (modal falls back to QF1) ──
    [Fact]
    public async Task NoCycle_LegacyBatch_ProjectsNullContext()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            var batch = await GetBatchDtoAsync(s);
            Assert.False(batch.HasOpenAdjustmentCycle);
            Assert.Null(batch.OpenAdjustmentCycle);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── A CLOSED (resubmitted) cycle is NOT the current open context ──
    [Fact]
    public async Task ClosedCycle_IsNotProjectedAsOpenContext()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await AddCycleAsync(s, AdjustmentConstants.States.Resubmitted, AdjustmentConstants.SourceStages.Area, "Reajuste",
                AdjustmentConstants.ReasonCodes.PriceNegotiation, null);

            var batch = await GetBatchDtoAsync(s);
            Assert.False(batch.HasOpenAdjustmentCycle);
            Assert.Null(batch.OpenAdjustmentCycle);
        }
        finally { await CleanupAsync(s.RequestId); }
    }
}
