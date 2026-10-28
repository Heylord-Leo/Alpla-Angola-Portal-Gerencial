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
using AlplaPortal.Infrastructure.Services.Purchasing;
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
/// v2.249.1 — item-level quotation window on <c>SaveQuotation</c>: a batch-model QUOTATION request whose
/// scalar is past the P.O. gate (PO_PARTIALLY_UPLOADED: group A PO_ISSUED for line 1) and whose line 2 was
/// RELEASED by a P.O.-group cancellation ("voltar a cotar") accepts a NEW quotation for line 2, refuses a
/// quotation mapped to the held/approved line 1, refuses a replacement, and keeps refusing everything on a
/// legacy request without batches. The scalar is never touched. Runs on the real relational model (LocalDB);
/// CanConnect() gates every fixture.
/// </summary>
[Collection("IntegrationTests")]
public class OpenItemQuotationWindowSaveQuotationTests
{
    static OpenItemQuotationWindowSaveQuotationTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            ctx.Database.EnsureCreated();
        }
        catch { /* LocalDB unavailable — CanConnect() gates every test. */ }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> DbOptions() => IntegrationTestDatabase.CreateOptions();

    private sealed record Seed(Guid RequestId, Guid BatchId, Guid HeldLineId, Guid ReleasedLineId, Guid BuyerId, int SupplierId, int StatusId);

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

    /// <param name="withBatch">false → legacy shape: same lines and scalar but no ApprovalBatch (window must stay closed).</param>
    private static async Task<Seed?> SeedAsync(bool withBatch = true, string requestStatus = "PO_PARTIALLY_UPLOADED")
    {
        await using var ctx = new ApplicationDbContext(DbOptions());
        var buyer = new User { Id = Guid.NewGuid(), FullName = "ZZ Buyer Window", Email = $"zztest-win-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(buyer);

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == requestStatus).Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstOrDefaultAsync();
        if (statusId == 0 || typeId == 0) return null;

        var supplier = new Supplier { Name = "ZZ Fornecedor Win " + Guid.NewGuid().ToString("N")[..8], TaxId = "55" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(), PortalCode = "ZW" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), IsActive = true };
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(), Title = "ZZTEST_WINDOW", RequestNumber = "ZZT-WIN-" + Guid.NewGuid().ToString("N")[..8],
            StatusId = statusId, RequestTypeId = typeId, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = buyer.Id, BuyerId = buyer.Id, CreatedAtUtc = DateTime.UtcNow,
        };
        ctx.Requests.Add(request);

        var held = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1, Description = "Linha aprovada (P.O. emitida)", Quantity = 10, UnitPrice = 100m, TotalAmount = 1000m, PlantId = 1, QuotationLifecycleStatus = "QUOTATION_APPROVED", IsDeleted = false, CreatedAtUtc = DateTime.UtcNow };
        var released = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 2, Description = "Linha devolvida à cotação", Quantity = 20, UnitPrice = 0m, TotalAmount = 0m, PlantId = 1, QuotationLifecycleStatus = "QUOTATION_PENDING", IsDeleted = false, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestLineItems.AddRange(held, released);

        var batchId = Guid.Empty;
        if (withBatch)
        {
            var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1, Status = "APPROVED", CreatedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedByUserId = buyer.Id };
            batchId = batch.Id;
            ctx.ApprovalBatches.Add(batch);
            ctx.ApprovalBatchItems.AddRange(
                new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = held.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
                new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = released.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), ReleasedAtUtc = DateTime.UtcNow.AddHours(-1), ReleasedByUserId = buyer.Id, ReleaseKind = "RESTART_QUOTATION" });
            ctx.RequestPoGroups.AddRange(
                new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, SupplierNameSnapshot = supplier.Name, CurrencyCode = "AOA", PlantId = 1, TotalAmount = 1000m, Status = "PO_ISSUED", PurchaseOrderNumber = "ECF99 2026/9001", ApprovalBatchId = batch.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedByUserId = buyer.Id },
                new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, SupplierNameSnapshot = supplier.Name, CurrencyCode = "AOA", PlantId = 1, TotalAmount = 400m, Status = "CANCELLED", ApprovalBatchId = batch.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedByUserId = buyer.Id });
        }

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, batchId, held.Id, released.Id, buyer.Id, supplier.Id, statusId);
    }

    private static SaveQuotationRequestDto Dto(Seed s, Guid mappedLineId, string description = "Linha devolvida à cotação") => new()
    {
        SupplierId = s.SupplierId,
        SupplierNameSnapshot = "ZZ Fornecedor Win",
        Currency = "AOA",
        SourceType = "MANUAL",
        Items = new List<SaveQuotationItemDto>
        {
            new() { LineNumber = 1, Description = description, Quantity = 20, UnitPrice = 11500m, MappedRequestLineItemId = mappedLineId, ReconciliationStatus = "MAPPED" }
        }
    };

    private static async Task CleanupAsync(Guid requestId)
    {
        if (requestId == Guid.Empty) return;
        await using var ctx = new ApplicationDbContext(DbOptions());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE c FROM ApprovalBatchItemCandidates c INNER JOIN ApprovalBatchItems abi ON abi.Id=c.ApprovalBatchItemId INNER JOIN ApprovalBatches b ON b.Id=abi.ApprovalBatchId WHERE b.RequestId={0};" +
            "DELETE abi FROM ApprovalBatchItems abi INNER JOIN ApprovalBatches b ON b.Id=abi.ApprovalBatchId WHERE b.RequestId={0};" +
            "DELETE FROM RequestPoGroups WHERE RequestId={0};" +
            "DELETE FROM ApprovalBatches WHERE RequestId={0};" +
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id=qi.QuotationId WHERE q.RequestId={0};" +
            "DELETE FROM Quotations WHERE RequestId={0};" +
            "DELETE FROM RequestLineItems WHERE RequestId={0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId={0};" +
            "DELETE FROM Requests WHERE Id={0};" +
            "DELETE FROM Suppliers WHERE Name LIKE 'ZZ Fornecedor Win %' AND NOT EXISTS (SELECT 1 FROM Quotations q WHERE q.SupplierId=Suppliers.Id) AND NOT EXISTS (SELECT 1 FROM RequestPoGroups g WHERE g.SupplierId=Suppliers.Id);" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-win-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId=Users.Id);", requestId);
    }

    // ── A. Released line on a past-P.O.-gate scalar → new quotation accepted; scalar unchanged ──
    [Fact]
    public async Task A_ReleasedLine_PastPoGate_QuotationAccepted_ScalarUnchanged()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(DbOptions()))
            {
                var controller = BuildController(ctx, s.BuyerId, RoleConstants.Buyer);
                var result = await controller.SaveQuotation(s.RequestId, null, Dto(s, s.ReleasedLineId));
                Assert.IsType<OkObjectResult>(result.Result);
            }
            await using (var v = new ApplicationDbContext(DbOptions()))
            {
                var q = await v.Quotations.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.RequestId == s.RequestId);
                Assert.Equal(s.ReleasedLineId, q.Items.Single().MappedRequestLineItemId);
                var r = await v.Requests.AsNoTracking().SingleAsync(x => x.Id == s.RequestId);
                Assert.Equal(s.StatusId, r.StatusId); // scalar never regressed to make the action possible
                var held = await v.RequestLineItems.AsNoTracking().SingleAsync(x => x.Id == s.HeldLineId);
                Assert.Equal("QUOTATION_APPROVED", held.QuotationLifecycleStatus);
            }
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── B. Mapping onto the held/approved line → 409 per line, nothing persisted ──
    [Fact]
    public async Task B_MappedToHeldLine_Refused409_NothingPersisted()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(DbOptions()))
            {
                var controller = BuildController(ctx, s.BuyerId, RoleConstants.Buyer);
                var result = await controller.SaveQuotation(s.RequestId, null, Dto(s, s.HeldLineId, "Linha aprovada (P.O. emitida)"));
                var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
                Assert.Equal("Item já comprometido", ((ProblemDetails)conflict.Value!).Title);
            }
            await using var v = new ApplicationDbContext(DbOptions());
            Assert.Equal(0, await v.Quotations.CountAsync(x => x.RequestId == s.RequestId));
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── C. Replacement of an existing quotation is not allowed on the item-window path ──
    [Fact]
    public async Task C_ReplaceQuotation_OnItemWindowPath_Refused409()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, s.BuyerId, RoleConstants.Buyer);
            var result = await controller.SaveQuotation(s.RequestId, Guid.NewGuid(), Dto(s, s.ReleasedLineId));
            var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
            Assert.Equal(409, ((ProblemDetails)conflict.Value!).Status);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── D. Legacy request (no batches) with the same scalar and a null/pending line → still 409 ──
    [Fact]
    public async Task D_LegacyRequestWithoutBatches_StillBlocked409()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(withBatch: false, requestStatus: "PO_ISSUED");
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var controller = BuildController(ctx, s.BuyerId, RoleConstants.Buyer);
            var result = await controller.SaveQuotation(s.RequestId, null, Dto(s, s.ReleasedLineId));
            var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
            Assert.Equal("Não é possível adicionar cotações neste status do pedido.", ((ProblemDetails)conflict.Value!).Detail);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── E. The evaluator itself (DB-backed) reports exactly the released line ──
    [Fact]
    public async Task E_Evaluator_ReportsOnlyReleasedLine()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var e = await QuotationWindowEvaluator.EvaluateAsync(ctx, s.RequestId, null, null, false);
            Assert.True(e.IsOpen);
            Assert.Single(e.OpenLineItemIds, s.ReleasedLineId);
        }
        finally { await CleanupAsync(s.RequestId); }
    }
}
