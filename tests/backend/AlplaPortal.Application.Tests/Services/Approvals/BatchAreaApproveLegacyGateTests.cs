using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Server-side safety net in BatchAreaApprove: a batch whose contributing quotation carries a
/// genuine EXTRA_ITEM line with NO recorded buyer decision (only possible for batches created
/// before the buyer-decision rule) must be refused with a structured 409
/// (BATCH_HAS_UNRESOLVED_LEGACY_LINES) before any allocation processing or other mutation.
/// Decided EXTRA_ITEM lines (INCLUDE or EXCLUDE) and IGNORED lines must never trip the gate —
/// the gate-passing tests assert the request advances to the allocation validation (the
/// deterministic next check), proving the gate itself did not block.
/// </summary>
public class BatchAreaApproveLegacyGateTests
{
    private static ApplicationDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class Scenario
    {
        public ApplicationDbContext Ctx = null!;
        public Request Request = null!;
        public RequestLineItem RequestedItem = null!;
        public ApprovalBatch Batch = null!;
        public QuotationItem WinnerItem = null!;
        public QuotationItem ExtraItem = null!;
        public QuotationItem IgnoredItem = null!;
        public Guid ActorId;
    }

    private const string ValidExcludeComment = "Item não será adquirido neste momento por decisão do comprador.";

    /// <summary>Seeds the REQ-15/07/2026-075 shape: 1 requested item, 1 winning SUBSTITUTE line,
    /// 1 genuine EXTRA_ITEM line (decision seeded per test), 1 IGNORED line. Batch is in
    /// WAITING_AREA_APPROVAL — exactly the state BatchAreaApprove acts on.</summary>
    private static async Task<Scenario> SeedAsync(bool includeExtraItemLine = true)
    {
        var ctx = GetInMemoryDbContext();
        var actorId = Guid.NewGuid();

        var quotationType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        // Required navigation — BatchAreaApprove's request load Includes r.Status (inner join):
        // without this row the request would silently vanish from the query and 404.
        var status = new RequestStatus { Id = 1, Code = "WAITING_APPROVAL", Name = "Em Aprovação" };

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            RequestedDateUtc = DateTime.UtcNow,
            StatusId = status.Id,
            Status = status,
            RequestTypeId = quotationType.Id,
            RequestType = quotationType
        };

        var requestedItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            LineNumber = 1,
            Description = "Martelo de Bronze/Latão 500g",
            Quantity = 1,
            UnitPrice = 100,
            TotalAmount = 100,
            IsDeleted = false
        };
        request.LineItems.Add(requestedItem);

        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "Fornecedor Teste",
            DocumentNumber = "DOC-1",
            Currency = "AOA"
        };

        var winnerItem = new QuotationItem
        {
            Id = Guid.NewGuid(),
            QuotationId = quotation.Id,
            Description = "Marreta Octogonal Bronze 450g",
            ReconciliationStatus = "SUBSTITUTE",
            MappedRequestLineItemId = requestedItem.Id,
            Quantity = 1,
            UnitPrice = 100,
            LineTotal = 100
        };
        var ignoredItem = new QuotationItem
        {
            Id = Guid.NewGuid(),
            QuotationId = quotation.Id,
            Description = "Marreta Octogonal Bronze 2KG",
            ReconciliationStatus = "IGNORED",
            Quantity = 6,
            UnitPrice = 80,
            LineTotal = 480,
            ReconciliationJustification = "Fornecedor não trabalha mais com este item específico do documento."
        };

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            BatchNumber = 1,
            Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorId
        };
        var batchItem = new ApprovalBatchItem
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = batch.Id,
            RequestLineItemId = requestedItem.Id,
            SelectedQuotationItemId = winnerItem.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        batch.Items.Add(batchItem);

        var scenario = new Scenario
        {
            Ctx = ctx,
            Request = request,
            RequestedItem = requestedItem,
            Batch = batch,
            WinnerItem = winnerItem,
            IgnoredItem = ignoredItem,
            ActorId = actorId
        };

        ctx.RequestTypes.Add(quotationType);
        ctx.RequestStatuses.Add(status);
        ctx.Requests.Add(request);
        ctx.Quotations.Add(quotation);
        ctx.QuotationItems.AddRange(winnerItem, ignoredItem);

        if (includeExtraItemLine)
        {
            var extraItem = new QuotationItem
            {
                Id = Guid.NewGuid(),
                QuotationId = quotation.Id,
                Description = "Marreta Octogonal Bronze 1KG",
                ReconciliationStatus = "EXTRA_ITEM",
                Quantity = 6,
                UnitPrice = 50,
                LineTotal = 300,
                ReconciliationJustification = "Fornecedor propôs item adicional para complementar o pedido original."
            };
            ctx.QuotationItems.Add(extraItem);
            scenario.ExtraItem = extraItem;
        }

        ctx.ApprovalBatches.Add(batch);
        ctx.ApprovalBatchItems.Add(batchItem);
        await ctx.SaveChangesAsync();

        return scenario;
    }

    private static ApprovalBatchController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new ApprovalBatchController(
            ctx,
            NullLogger<ApprovalBatchController>.Instance,
            new Mock<IRequestStatusSyncService>().Object,
            new Mock<IGroupBuilderService>().Object,
            new Mock<IApprovalRoutingService>().Object,
            new Mock<IQuotationItemEligibilityService>().Object,
            new BatchExtraItemDecisionService(ctx));

        // System Administrator bypasses both CanActAsAreaManagerAsync and the request scope
        // filter — the subject under test here is exclusively the unresolved-legacy gate.
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
            new Claim(ClaimTypes.Role, RoleConstants.SystemAdministrator)
        }, "Test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private static async Task<IActionResult> ApproveAsync(Scenario s, BatchApprovalActionDto? dto = null)
    {
        var controller = BuildController(s.Ctx, s.ActorId);
        return await controller.BatchAreaApprove(s.Request.Id, s.Batch.Id, dto ?? new BatchApprovalActionDto());
    }

    /// <summary>The deterministic check immediately AFTER the gate is the per-item allocation
    /// validation ("Atribuição de Item Incompleta") — reaching it proves the gate passed.</summary>
    private static void AssertPassedGateReachingAllocationValidation(IActionResult result)
    {
        if (result is ObjectResult unexpected && unexpected is not BadRequestObjectResult && unexpected.Value is ProblemDetails up)
            Assert.Fail($"Unexpected {result.GetType().Name}: {up.Title} — {up.Detail}");
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Equal("Atribuição de Item Incompleta", problem.Title);
    }

    [Fact]
    public async Task BatchAreaApprove_UnresolvedLegacyExtraItem_Returns409_WithPendingItems_AndPersistsNothing()
    {
        var s = await SeedAsync();
        // No ApprovalBatchExtraItemDecision row at all for the EXTRA_ITEM line — the legacy shape.

        var result = await ApproveAsync(s);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
        Assert.Equal("BATCH_HAS_UNRESOLVED_LEGACY_LINES", problem.Extensions["code"]?.ToString());

        var pendingItems = Assert.IsAssignableFrom<System.Collections.IEnumerable>(problem.Extensions["pendingItems"]).Cast<object>().ToList();
        Assert.Single(pendingItems);
        Assert.Contains(s.ExtraItem.Description, System.Text.Json.JsonSerializer.Serialize(pendingItems[0]));

        // Nothing persisted: batch still awaiting area approval, no allocations, no history.
        var batch = await s.Ctx.ApprovalBatches.AsNoTracking().SingleAsync(b => b.Id == s.Batch.Id);
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, batch.Status);
        Assert.Empty(await s.Ctx.RequestLineItemAllocations.AsNoTracking().ToListAsync());
        Assert.Empty(await s.Ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == s.Request.Id).ToListAsync());
    }

    [Fact]
    public async Task BatchAreaApprove_ExtraItemWithExcludeDecision_PassesGate()
    {
        var s = await SeedAsync();
        s.Ctx.ApprovalBatchExtraItemDecisions.Add(new ApprovalBatchExtraItemDecision
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = s.Batch.Id,
            QuotationItemId = s.ExtraItem.Id,
            Decision = "EXCLUDE",
            Comment = ValidExcludeComment,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = s.ActorId
        });
        await s.Ctx.SaveChangesAsync();

        var result = await ApproveAsync(s);

        AssertPassedGateReachingAllocationValidation(result);
    }

    [Fact]
    public async Task BatchAreaApprove_ExtraItemWithIncludeDecision_PassesGate()
    {
        var s = await SeedAsync();

        // Materialize the INCLUDE the same way CreateBatch does: generated RequestLineItem +
        // ApprovalBatchItem + decision row pointing at the generated line.
        var service = new BatchExtraItemDecisionService(s.Ctx);
        var decisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "INCLUDE" }
        };
        var applyResult = await service.ApplyAsync(s.Request, s.Batch, new[] { s.WinnerItem.Id }, decisions, s.ActorId);
        Assert.True(applyResult.Success);
        await s.Ctx.SaveChangesAsync();

        // Candidate model: the included extra is a candidate-based item (no pre-set winner), so
        // area approval must carry its winner selection; the legacy main item needs none.
        var includedItem = s.Batch.Items.Single(bi => bi.RequestLineItemId != s.RequestedItem.Id);
        var candidate = await s.Ctx.ApprovalBatchItemCandidates
            .SingleAsync(c => c.ApprovalBatchItemId == includedItem.Id);

        var result = await ApproveAsync(s, new BatchApprovalActionDto
        {
            Selections = new List<BatchWinnerSelectionDto>
            {
                new() { ApprovalBatchItemId = includedItem.Id, SelectedCandidateId = candidate.Id }
            }
        });

        AssertPassedGateReachingAllocationValidation(result);
    }

    [Fact]
    public async Task BatchAreaApprove_IgnoredOnlyQuotation_PassesGate()
    {
        // No EXTRA_ITEM line at all — only the winner and an IGNORED line. IGNORED is a complete,
        // already-justified state: it must never require a decision and never block area approval.
        var s = await SeedAsync(includeExtraItemLine: false);

        var result = await ApproveAsync(s);

        AssertPassedGateReachingAllocationValidation(result);
    }
}
