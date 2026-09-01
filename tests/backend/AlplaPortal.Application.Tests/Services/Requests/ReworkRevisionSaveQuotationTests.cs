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
/// Adjustment V2 Phase 4 — the narrow rework-REVISION exception to the scalar quotation gate
/// (<c>SaveQuotation</c> + <c>reworkBatchId</c>). A batch-model QUOTATION keeps its scalar at
/// WAITING_*_APPROVAL while a batch is in AREA/FINAL_ADJUSTMENT; the Buyer may then ADD a revised
/// quotation for THAT batch (never mutate the original or its frozen candidate). Runs on the real
/// relational model (LocalDB); CanConnect() gates every fixture.
/// </summary>
[Collection("IntegrationTests")]
public class ReworkRevisionSaveQuotationTests
{
    static ReworkRevisionSaveQuotationTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var tableId = ctx.Database.SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.ApprovalBatchAdjustments'), 0) AS [Value]").AsEnumerable().First();
                if (tableId == 0) ctx.Database.EnsureDeleted();
            }
            ctx.Database.EnsureCreated();
        }
        catch { /* LocalDB unavailable — CanConnect() gates every test. */ }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> DbOptions() => IntegrationTestDatabase.CreateOptions();

    private sealed record Seed(Guid RequestId, Guid BatchId, Guid LineItemId, Guid OtherLineItemId,
        Guid OriginalQuotationId, Guid OriginalQuotationItemId, Guid CandidateId, Guid BuyerId, int SupplierId);

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId, params string[] roles)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new ApprovalRoutingResultDto());
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

    private static async Task<Seed?> SeedAsync(string batchStatus = "AREA_ADJUSTMENT")
    {
        await using var ctx = new ApplicationDbContext(DbOptions());
        var buyer = new User { Id = Guid.NewGuid(), FullName = "ZZ Buyer", Email = $"zztest-rev-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(buyer);

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_AREA_APPROVAL").Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstOrDefaultAsync();
        if (statusId == 0 || typeId == 0) return null;

        var supplier = new Supplier { Name = "ZZ Fornecedor Rev " + Guid.NewGuid().ToString("N")[..8], TaxId = "5410000999", PortalCode = "ZZRV", IsActive = true };
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync(); // realize the identity Id
        var supplierId = supplier.Id;

        var request = new Request
        {
            Id = Guid.NewGuid(), Title = "ZZTEST_REV", RequestNumber = "ZZT-REV-" + Guid.NewGuid().ToString("N")[..8],
            StatusId = statusId, RequestTypeId = typeId, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = buyer.Id, BuyerId = buyer.Id, CreatedAtUtc = DateTime.UtcNow,
        };
        ctx.Requests.Add(request);

        var li = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1, Description = "Serviço", Quantity = 60, UnitPrice = 82150m, TotalAmount = 4929000m, PlantId = 1, QuotationLifecycleStatus = "BATCH_ASSIGNED", IsDeleted = false, CreatedAtUtc = DateTime.UtcNow };
        var otherLi = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 2, Description = "Outro item", Quantity = 1, UnitPrice = 100m, TotalAmount = 100m, PlantId = 1, QuotationLifecycleStatus = "BATCH_ASSIGNED", IsDeleted = false, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestLineItems.AddRange(li, otherLi);

        var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1, Status = batchStatus, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = buyer.Id };
        ctx.ApprovalBatches.Add(batch);
        var batchItem = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id, CreatedAtUtc = DateTime.UtcNow };
        ctx.ApprovalBatchItems.Add(batchItem);

        var quotation = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplierId, SupplierNameSnapshot = "ZZ Fornecedor Rev", Currency = "AOA", SourceType = "MANUAL", TotalAmount = 4929000m, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = buyer.Id };
        ctx.Quotations.Add(quotation);
        var qItem = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation.Id, LineNumber = 1, Description = "Serviço", Quantity = 60, UnitPrice = 82150m, GrossSubtotal = 4929000m, LineTotal = 4929000m, MappedRequestLineItemId = li.Id, ReconciliationStatus = "MAPPED" };
        ctx.QuotationItems.Add(qItem);

        var candidate = new ApprovalBatchItemCandidate { Id = Guid.NewGuid(), ApprovalBatchItemId = batchItem.Id, QuotationItemId = qItem.Id, QuotationId = quotation.Id, SupplierId = supplierId, SupplierNameSnapshot = "ZZ Fornecedor Rev", QuotedDescription = "Serviço", QuotedQuantity = 60, UnitPrice = 82150m, GrossSubtotal = 4929000m, LineTotal = 4929000m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow };
        ctx.ApprovalBatchItemCandidates.Add(candidate);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, batch.Id, li.Id, otherLi.Id, quotation.Id, qItem.Id, candidate.Id, buyer.Id, supplierId);
    }

    private static SaveQuotationRequestDto RevisionDto(Seed s, Guid mappedLineId, decimal unit = 80000m) => new()
    {
        SupplierId = s.SupplierId,
        SupplierNameSnapshot = "ZZ Fornecedor Rev",
        Currency = "AOA",
        SourceType = "MANUAL",
        Items = new List<SaveQuotationItemDto>
        {
            new() { LineNumber = 1, Description = "Serviço", Quantity = 60, UnitPrice = unit, MappedRequestLineItemId = mappedLineId, ReconciliationStatus = "MAPPED" }
        }
    };

    private static async Task CleanupAsync(Guid requestId)
    {
        if (requestId == Guid.Empty) return;
        await using var ctx = new ApplicationDbContext(DbOptions());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE c FROM ApprovalBatchItemCandidates c INNER JOIN ApprovalBatchItems abi ON abi.Id=c.ApprovalBatchItemId INNER JOIN ApprovalBatches b ON b.Id=abi.ApprovalBatchId WHERE b.RequestId={0};" +
            "DELETE abi FROM ApprovalBatchItems abi INNER JOIN ApprovalBatches b ON b.Id=abi.ApprovalBatchId WHERE b.RequestId={0};" +
            "DELETE FROM ApprovalBatches WHERE RequestId={0};" +
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id=qi.QuotationId WHERE q.RequestId={0};" +
            "DELETE FROM Quotations WHERE RequestId={0};" +
            "DELETE FROM RequestLineItems WHERE RequestId={0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId={0};" +
            "DELETE FROM Requests WHERE Id={0};" +
            "DELETE FROM Suppliers WHERE PortalCode='ZZRV';" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-rev-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId=Users.Id);", requestId);
    }

    // ── A. AREA_ADJUSTMENT revision allowed; original preserved; revised stored; no duplicate line ──
    [Fact]
    public async Task A_AreaAdjustment_Revision_Allowed_PreservesOriginal()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync("AREA_ADJUSTMENT");
        if (s == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(DbOptions()))
            {
                var controller = BuildController(ctx, s.BuyerId, RoleConstants.SystemAdministrator);
                var result = await controller.SaveQuotation(s.RequestId, null, RevisionDto(s, s.LineItemId), s.BatchId, s.OriginalQuotationId);
                Assert.IsType<OkObjectResult>(result.Result);
            }
            await using (var v = new ApplicationDbContext(DbOptions()))
            {
                // Original quotation + item + candidate UNCHANGED.
                var origItem = await v.QuotationItems.AsNoTracking().SingleAsync(qi => qi.Id == s.OriginalQuotationItemId);
                Assert.Equal(82150m, origItem.UnitPrice);
                var cand = await v.ApprovalBatchItemCandidates.AsNoTracking().SingleAsync(c => c.Id == s.CandidateId);
                Assert.Equal(82150m, cand.UnitPrice);
                Assert.Equal(4929000m, cand.LineTotal);
                // A NEW quotation exists with the revised value, mapped to the SAME request line.
                var quotations = await v.Quotations.AsNoTracking().Where(q => q.RequestId == s.RequestId).ToListAsync();
                Assert.Equal(2, quotations.Count);
                var revised = quotations.Single(q => q.Id != s.OriginalQuotationId);
                // Revision provenance persisted: Q2.RevisesQuotationId = Q1.Id; original has none.
                Assert.Equal(s.OriginalQuotationId, revised.RevisesQuotationId);
                Assert.Null(quotations.Single(q => q.Id == s.OriginalQuotationId).RevisesQuotationId);
                var revisedItem = await v.QuotationItems.AsNoTracking().SingleAsync(qi => qi.QuotationId == revised.Id);
                Assert.Equal(80000m, revisedItem.UnitPrice);
                Assert.Equal(s.LineItemId, revisedItem.MappedRequestLineItemId);
                // No duplicate RequestLineItem.
                Assert.Equal(2, await v.RequestLineItems.CountAsync(li => li.RequestId == s.RequestId && !li.IsDeleted));
            }
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── B. FINAL_ADJUSTMENT revision allowed ──
    [Fact]
    public async Task B_FinalAdjustment_Revision_Allowed()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync("FINAL_ADJUSTMENT");
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, s.BuyerId, RoleConstants.SystemAdministrator);
            var result = await controller.SaveQuotation(s.RequestId, null, RevisionDto(s, s.LineItemId), s.BatchId, s.OriginalQuotationId);
            Assert.IsType<OkObjectResult>(result.Result);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── C. No reworkBatchId → the scalar gate still blocks (409) ──
    [Fact]
    public async Task C_NoReworkBatchId_Blocked_409()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, s.BuyerId, RoleConstants.SystemAdministrator);
            var result = await controller.SaveQuotation(s.RequestId, null, RevisionDto(s, s.LineItemId), null);
            var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
            Assert.Equal(409, ((ProblemDetails)conflict.Value!).Status);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── D. Foreign/unknown batch → 404 ──
    [Fact]
    public async Task D_ForeignBatch_Blocked_404()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, s.BuyerId, RoleConstants.SystemAdministrator);
            var result = await controller.SaveQuotation(s.RequestId, null, RevisionDto(s, s.LineItemId), Guid.NewGuid());
            Assert.IsType<NotFoundObjectResult>(result.Result);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── E. Item not belonging to the rework batch → 400 ──
    [Fact]
    public async Task E_ItemNotInBatch_Blocked_400()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, s.BuyerId, RoleConstants.SystemAdministrator);
            // Map to line #2, which is NOT in the rework batch.
            var result = await controller.SaveQuotation(s.RequestId, null, RevisionDto(s, s.OtherLineItemId), s.BatchId, s.OriginalQuotationId);
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── F. Unauthorized actor (not the Buyer, not admin) → 403 ──
    [Fact]
    public async Task F_UnauthorizedActor_Blocked_403()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            // A REAL user who is neither the Buyer nor an admin, with no scopes (so the request is still
            // visible) — must be rejected by the Buyer-scope check, not the earlier unknown-user gate.
            Guid stranger;
            await using (var seedCtx = new ApplicationDbContext(DbOptions()))
            {
                var u = new User { Id = Guid.NewGuid(), FullName = "ZZ Stranger", Email = $"zztest-rev-{Guid.NewGuid():N}@test.local" };
                seedCtx.Users.Add(u);
                await seedCtx.SaveChangesAsync();
                stranger = u.Id;
            }
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, stranger);
            var result = await controller.SaveQuotation(s.RequestId, null, RevisionDto(s, s.LineItemId), s.BatchId, s.OriginalQuotationId);
            var obj = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(403, obj.StatusCode);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── G. Revision requires provenance: missing revisesQuotationId → 400 ──
    [Fact]
    public async Task G_MissingRevisesQuotationId_Blocked_400()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, s.BuyerId, RoleConstants.SystemAdministrator);
            var result = await controller.SaveQuotation(s.RequestId, null, RevisionDto(s, s.LineItemId), s.BatchId, revisesQuotationId: null);
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── H. Foreign/unknown original quotation → 404 ──
    [Fact]
    public async Task H_ForeignOriginalQuotation_Blocked_404()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, s.BuyerId, RoleConstants.SystemAdministrator);
            var result = await controller.SaveQuotation(s.RequestId, null, RevisionDto(s, s.LineItemId), s.BatchId, revisesQuotationId: Guid.NewGuid());
            Assert.IsType<NotFoundObjectResult>(result.Result);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── I. Original quotation does not contribute to the rework batch → 400 ──
    [Fact]
    public async Task I_OriginalNotContributingToBatch_Blocked_400()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            // A second quotation on the same request that contributes NO candidate to the batch.
            Guid strayQuotationId;
            await using (var seedCtx = new ApplicationDbContext(DbOptions()))
            {
                var q = new Quotation { Id = Guid.NewGuid(), RequestId = s.RequestId, SupplierId = s.SupplierId, SupplierNameSnapshot = "ZZ Fornecedor Rev", Currency = "AOA", SourceType = "MANUAL", TotalAmount = 1m, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.BuyerId };
                seedCtx.Quotations.Add(q);
                await seedCtx.SaveChangesAsync();
                strayQuotationId = q.Id;
            }
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, s.BuyerId, RoleConstants.SystemAdministrator);
            var result = await controller.SaveQuotation(s.RequestId, null, RevisionDto(s, s.LineItemId), s.BatchId, revisesQuotationId: strayQuotationId);
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
        finally { await CleanupAsync(s.RequestId); }
    }
}
