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
/// Adjustment V2 Phase 4 — the composition/resubmit guard: a quotation option that is deterministically
/// SUPERSEDED by a revision (Quotation.RevisesQuotationId) cannot enter a NEW approval round during
/// AREA/FINAL_ADJUSTMENT. UpdateBatch rejects selecting it; ResubmitBatch rejects a batch still carrying
/// it; the revised option is accepted and snapshots the revised value. InMemory-EF direct-controller.
/// </summary>
public class ReworkSupersededCompositionGuardTests
{
    private sealed record Seed(Guid RequestId, Guid BatchId, Guid LineItemId,
        Guid OldQuotationItemId, Guid RevisedQuotationItemId, Guid Actor);

    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ApprovalBatchController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto { Managers = { new AreaManagerDto { UserId = actorId, FullName = "Mgr" } } });
        var controller = new ApprovalBatchController(
            ctx, NullLogger<ApprovalBatchController>.Instance, new Mock<IRequestStatusSyncService>().Object,
            new GroupBuilderService(ctx), routing.Object, new QuotationItemEligibilityService(ctx),
            new BatchExtraItemDecisionService(ctx), new AdjustmentCycleService(ctx),
            new Mock<IWorkflowNotificationOrchestrator>().Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new Claim(ClaimTypes.Role, RoleConstants.SystemAdministrator),
                }, "Test"))
            }
        };
        return controller;
    }

    /// <param name="seedOldCandidate">Seed the batch item already carrying the OLD (to-be-superseded) candidate.</param>
    private static async Task<Seed> SeedAsync(DbContextOptions<ApplicationDbContext> options, bool seedOldCandidate)
    {
        await using var ctx = new ApplicationDbContext(options);
        var actor = Guid.NewGuid();
        ctx.Users.Add(new User { Id = actor, FullName = "Buyer", Email = $"b-{Guid.NewGuid():N}@t.local" });
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 1, Code = "WAITING_AREA_APPROVAL", Name = "Área" });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        ctx.Suppliers.Add(new Supplier { Id = 5001, Name = "Forn", TaxId = "5410000001", PortalCode = "ZZG1", IsActive = true });

        var request = new Request { Id = Guid.NewGuid(), Title = "R", RequestNumber = "R-G", StatusId = 1, RequestTypeId = 1, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1, RequesterId = actor, BuyerId = actor, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(request);
        var li = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1, Description = "Serviço", Quantity = 60, UnitPrice = 82150m, TotalAmount = 4929000m, PlantId = 1, QuotationLifecycleStatus = "BATCH_ASSIGNED", IsDeleted = false, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestLineItems.Add(li);

        var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor };
        ctx.ApprovalBatches.Add(batch);
        var batchItem = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id, CreatedAtUtc = DateTime.UtcNow };
        ctx.ApprovalBatchItems.Add(batchItem);

        // Q1 (original) + item; Q2 revises Q1 + item (both mapped to line #1).
        var q1 = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = 5001, SupplierNameSnapshot = "Forn", Currency = "AOA", SourceType = "MANUAL", TotalAmount = 4929000m, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor };
        var qi1 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = q1.Id, LineNumber = 1, Description = "Serviço", Quantity = 60, UnitPrice = 82150m, GrossSubtotal = 4929000m, LineTotal = 4929000m, MappedRequestLineItemId = li.Id, ReconciliationStatus = "MAPPED" };
        var q2 = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = 5001, SupplierNameSnapshot = "Forn", Currency = "AOA", SourceType = "MANUAL", TotalAmount = 4800000m, RevisesQuotationId = q1.Id, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor };
        var qi2 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = q2.Id, LineNumber = 1, Description = "Serviço", Quantity = 60, UnitPrice = 80000m, GrossSubtotal = 4800000m, LineTotal = 4800000m, MappedRequestLineItemId = li.Id, ReconciliationStatus = "MAPPED" };
        ctx.Quotations.AddRange(q1, q2);
        ctx.QuotationItems.AddRange(qi1, qi2);

        if (seedOldCandidate)
        {
            ctx.ApprovalBatchItemCandidates.Add(new ApprovalBatchItemCandidate
            {
                Id = Guid.NewGuid(), ApprovalBatchItemId = batchItem.Id, QuotationItemId = qi1.Id, QuotationId = q1.Id,
                SupplierId = 5001, SupplierNameSnapshot = "Forn", QuotedDescription = "Serviço", QuotedQuantity = 60,
                UnitPrice = 82150m, GrossSubtotal = 4929000m, LineTotal = 4929000m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow
            });
        }

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, batch.Id, li.Id, qi1.Id, qi2.Id, actor);
    }

    private static UpdateApprovalBatchDto DtoWith(Seed s, Guid quotationItemId) => new()
    {
        Items = new List<BatchItemDto>
        {
            new() { RequestLineItemId = s.LineItemId, Candidates = { new BatchCandidateInputDto { QuotationItemId = quotationItemId } } }
        }
    };

    // ── UpdateBatch rejects selecting the SUPERSEDED (old) option ──
    [Fact]
    public async Task UpdateBatch_SelectingSupersededOption_Rejected()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, seedOldCandidate: false);
        await using var ctx = new ApplicationDbContext(options);
        var result = await BuildController(ctx, s.Actor).UpdateBatch(s.RequestId, s.BatchId, DtoWith(s, s.OldQuotationItemId));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Cotação Substituída", ((ProblemDetails)bad.Value!).Title);
    }

    // ── UpdateBatch accepts the REVISED option and snapshots the revised value ──
    [Fact]
    public async Task UpdateBatch_SelectingRevisedOption_Accepted_SnapshotsRevisedValue()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, seedOldCandidate: false);
        await using (var ctx = new ApplicationDbContext(options))
        {
            var result = await BuildController(ctx, s.Actor).UpdateBatch(s.RequestId, s.BatchId, DtoWith(s, s.RevisedQuotationItemId));
            Assert.IsType<OkObjectResult>(result);
        }
        await using (var v = new ApplicationDbContext(options))
        {
            var cand = await v.ApprovalBatchItemCandidates.AsNoTracking().SingleAsync(c => c.QuotationItemId == s.RevisedQuotationItemId);
            Assert.Equal(80000m, cand.UnitPrice);
            Assert.Equal(4800000m, cand.LineTotal);
        }
    }

    // ── ResubmitBatch rejects a batch still carrying the superseded candidate ──
    [Fact]
    public async Task ResubmitBatch_WithSupersededCandidate_Rejected()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, seedOldCandidate: true);
        await using var ctx = new ApplicationDbContext(options);
        var result = await BuildController(ctx, s.Actor).ResubmitBatch(s.RequestId, s.BatchId,
            new BatchApprovalActionDto { AdjustmentResponse = "resposta" });
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Cotação Substituída", ((ProblemDetails)bad.Value!).Title);
    }
}
