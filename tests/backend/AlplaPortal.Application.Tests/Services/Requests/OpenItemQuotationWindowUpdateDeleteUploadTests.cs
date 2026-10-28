using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.249.2 — the item-level quotation window on the REMAINING write endpoints: <c>PUT …/quotations/{id}</c>
/// (edit an unsubmitted quotation of a released line), <c>DELETE …/quotations/{id}</c> and the PROFORMA / QUOTATION
/// document upload (<c>AttachmentsController</c>, which v2.249.1 documented but did not change). Scalar past the
/// P.O. gate (PO_PARTIALLY_UPLOADED, sibling group PO_ISSUED), line 2 released by a cancellation. Runs on LocalDB
/// (CanConnect() gates every fixture); the upload gate runs in-memory.
/// </summary>
[Collection("IntegrationTests")]
public class OpenItemQuotationWindowUpdateDeleteUploadTests : IDisposable
{
    static OpenItemQuotationWindowUpdateDeleteUploadTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            ctx.Database.EnsureCreated();
        }
        catch { /* LocalDB unavailable — CanConnect() gates every test. */ }
    }

    private readonly string _scratchDir;
    public OpenItemQuotationWindowUpdateDeleteUploadTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_quotation_doc_window_" + Guid.NewGuid());
        Directory.CreateDirectory(_scratchDir);
    }
    public void Dispose() { try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort */ } }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> DbOptions() => IntegrationTestDatabase.CreateOptions();

    private sealed record Seed(Guid RequestId, Guid HeldLineId, Guid ReleasedLineId, Guid BuyerId, int SupplierId, int StatusId);

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
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() }
        };
        return controller;
    }

    private static async Task<Seed?> SeedAsync(bool withBatch = true, string requestStatus = "PO_PARTIALLY_UPLOADED")
    {
        await using var ctx = new ApplicationDbContext(DbOptions());
        var buyer = new User { Id = Guid.NewGuid(), FullName = "ZZ Buyer Window2", Email = $"zztest-win2-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(buyer);
        var statusId = await ctx.RequestStatuses.Where(s => s.Code == requestStatus).Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstOrDefaultAsync();
        if (statusId == 0 || typeId == 0) return null;
        var supplier = new Supplier { Name = "ZZ Fornecedor Win2 " + Guid.NewGuid().ToString("N")[..8], TaxId = "56" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(), PortalCode = "ZX" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), IsActive = true };
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();
        var request = new Request
        {
            Id = Guid.NewGuid(), Title = "ZZTEST_WINDOW2", RequestNumber = "ZZT-WIN2-" + Guid.NewGuid().ToString("N")[..8],
            StatusId = statusId, RequestTypeId = typeId, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = buyer.Id, BuyerId = buyer.Id, CreatedAtUtc = DateTime.UtcNow,
        };
        ctx.Requests.Add(request);
        var held = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1, Description = "Linha aprovada (P.O. emitida)", Quantity = 10, UnitPrice = 100m, TotalAmount = 1000m, PlantId = 1, QuotationLifecycleStatus = "QUOTATION_APPROVED", CreatedAtUtc = DateTime.UtcNow };
        var released = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 2, Description = "Linha devolvida à cotação", Quantity = 20, UnitPrice = 0m, TotalAmount = 0m, PlantId = 1, QuotationLifecycleStatus = "QUOTATION_PENDING", CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestLineItems.AddRange(held, released);
        if (withBatch)
        {
            var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1, Status = "APPROVED", CreatedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedByUserId = buyer.Id };
            ctx.ApprovalBatches.Add(batch);
            ctx.ApprovalBatchItems.AddRange(
                new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = held.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
                new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = released.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), ReleasedAtUtc = DateTime.UtcNow.AddHours(-1), ReleasedByUserId = buyer.Id, ReleaseKind = "RESTART_QUOTATION" });
            ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, SupplierNameSnapshot = supplier.Name, CurrencyCode = "AOA", PlantId = 1, TotalAmount = 1000m, Status = "PO_ISSUED", PurchaseOrderNumber = "ECF99 2026/9002", ApprovalBatchId = batch.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedByUserId = buyer.Id });
        }
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, held.Id, released.Id, buyer.Id, supplier.Id, statusId);
    }

    private static SaveQuotationRequestDto Dto(Seed s, Guid mappedLineId, decimal unitPrice = 11_500m) => new()
    {
        SupplierId = s.SupplierId, SupplierNameSnapshot = "ZZ Fornecedor Win2", Currency = "AOA", SourceType = "MANUAL",
        Items = new List<SaveQuotationItemDto> { new() { LineNumber = 1, Description = "Linha devolvida à cotação", Quantity = 20, UnitPrice = unitPrice, MappedRequestLineItemId = mappedLineId, ReconciliationStatus = "MAPPED" } }
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
            "DELETE FROM RequestAttachments WHERE RequestId={0};" +
            "DELETE FROM RequestLineItems WHERE RequestId={0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId={0};" +
            "DELETE FROM Requests WHERE Id={0};" +
            "DELETE FROM Suppliers WHERE Name LIKE 'ZZ Fornecedor Win2 %' AND NOT EXISTS (SELECT 1 FROM Quotations q WHERE q.SupplierId=Suppliers.Id) AND NOT EXISTS (SELECT 1 FROM RequestPoGroups g WHERE g.SupplierId=Suppliers.Id);" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-win2-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId=Users.Id);", requestId);
    }

    private static async Task<Guid> CreateQuotationForReleasedLineAsync(Seed s)
    {
        await using var ctx = new ApplicationDbContext(DbOptions());
        var result = await BuildController(ctx, s.BuyerId, RoleConstants.Buyer).SaveQuotation(s.RequestId, null, Dto(s, s.ReleasedLineId));
        Assert.IsType<OkObjectResult>(result.Result);
        return await ctx.Quotations.AsNoTracking().Where(q => q.RequestId == s.RequestId).Select(q => q.Id).SingleAsync();
    }

    // ── A. PUT on the unsubmitted quotation of the released line is accepted; the scalar is unchanged ──
    [Fact]
    public async Task A_UpdateQuotation_ReleasedLine_Accepted_ScalarUnchanged()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            var quotationId = await CreateQuotationForReleasedLineAsync(s);
            await using (var ctx = new ApplicationDbContext(DbOptions()))
            {
                var result = await BuildController(ctx, s.BuyerId, RoleConstants.Buyer).UpdateQuotation(s.RequestId, quotationId, Dto(s, s.ReleasedLineId, unitPrice: 11_000m));
                Assert.IsType<OkObjectResult>(result.Result);
            }
            await using var v = new ApplicationDbContext(DbOptions());
            var q = await v.Quotations.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == quotationId);
            Assert.Equal(11_000m, q.Items.Single().UnitPrice);
            Assert.Equal(s.StatusId, (await v.Requests.AsNoTracking().SingleAsync(x => x.Id == s.RequestId)).StatusId);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── B. PUT that re-maps a line onto the held/approved line → 409 per line, nothing changed ──
    [Fact]
    public async Task B_UpdateQuotation_MappedToHeldLine_Refused409()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            var quotationId = await CreateQuotationForReleasedLineAsync(s);
            await using (var ctx = new ApplicationDbContext(DbOptions()))
            {
                var result = await BuildController(ctx, s.BuyerId, RoleConstants.Buyer).UpdateQuotation(s.RequestId, quotationId, Dto(s, s.HeldLineId));
                var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
                Assert.Equal("Item já comprometido", ((ProblemDetails)conflict.Value!).Title);
            }
            await using var v = new ApplicationDbContext(DbOptions());
            Assert.Equal(s.ReleasedLineId, (await v.QuotationItems.AsNoTracking().SingleAsync(x => x.QuotationId == quotationId)).MappedRequestLineItemId);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── C. DELETE of the unsubmitted quotation on the item-window path is accepted ──
    [Fact]
    public async Task C_DeleteQuotation_ReleasedLine_Accepted()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            var quotationId = await CreateQuotationForReleasedLineAsync(s);
            await using (var ctx = new ApplicationDbContext(DbOptions()))
            {
                var result = await BuildController(ctx, s.BuyerId, RoleConstants.Buyer).DeleteQuotation(s.RequestId, quotationId);
                Assert.False(result is ConflictObjectResult, "delete must not be refused by the scalar window");
            }
            await using var v = new ApplicationDbContext(DbOptions());
            Assert.Equal(0, await v.Quotations.CountAsync(x => x.Id == quotationId));
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── D. Legacy request without batches: PUT and DELETE stay refused ──
    [Fact]
    public async Task D_LegacyRequestWithoutBatches_UpdateAndDelete_Still409()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(withBatch: false, requestStatus: "PO_ISSUED"); if (s == null) return;
        try
        {
            Guid quotationId;
            await using (var seed = new ApplicationDbContext(DbOptions()))
            {
                var q = new Quotation { Id = Guid.NewGuid(), RequestId = s.RequestId, SupplierId = s.SupplierId, SupplierNameSnapshot = "ZZ Fornecedor Win2", Currency = "AOA", TotalAmount = 1m, SourceType = "MANUAL", CreatedByUserId = s.BuyerId, CreatedAtUtc = DateTime.UtcNow };
                seed.Quotations.Add(q); await seed.SaveChangesAsync(); quotationId = q.Id;
            }
            await using var ctx = new ApplicationDbContext(DbOptions());
            var c = BuildController(ctx, s.BuyerId, RoleConstants.Buyer);
            Assert.IsType<ConflictObjectResult>((await c.UpdateQuotation(s.RequestId, quotationId, Dto(s, s.ReleasedLineId))).Result);
            Assert.IsType<ConflictObjectResult>(await c.DeleteQuotation(s.RequestId, quotationId));
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    // ── E. PROFORMA / QUOTATION document upload (in-memory): item window accepted, legacy refused ──

    private static ApplicationDbContext NewInMemory() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private AttachmentsController BuildAttachments(ApplicationDbContext ctx, Guid actorId)
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(_scratchDir);
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c[It.IsAny<string>()]).Returns(_scratchDir);
        var securityOptions = Options.Create(new SecurityOptions
        {
            Upload = new UploadOptions { AllowedExtensions = new List<string> { ".pdf" }, BlockedExtensions = new List<string>(), MaxFileSizeBytes = 10_000_000 }
        });
        var controller = new AttachmentsController(ctx, envMock.Object, securityOptions, configMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")) }
        };
        return controller;
    }

    private static IFormFile Pdf()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% TEST ONLY - Portal Gerencial v2.249.2\n%%EOF\n");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", "TEST-ONLY-quotation.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    }

    private static async Task<(Guid requestId, Guid buyer)> SeedInMemoryAsync(ApplicationDbContext ctx, bool withBatch)
    {
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 5, Code = RequestConstants.Statuses.PoPartiallyUploaded, Name = "P.O. parcial" });
        ctx.Companies.Add(new Company { Id = 1, Name = "C" }); ctx.Departments.Add(new Department { Id = 1, Name = "D" }); ctx.Plants.Add(new Plant { Id = 1, Name = "P", CompanyId = 1 });
        var buyer = Guid.NewGuid();
        ctx.Users.Add(new User { Id = buyer, FullName = "Comprador", Email = "b@t.local", IsActive = true });
        var req = new Request { Id = Guid.NewGuid(), RequestNumber = "REQ-UP", Title = "t", Description = "d", RequestTypeId = 1, StatusId = 5, RequesterId = buyer, CreatedByUserId = buyer, BuyerId = buyer, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(req);
        var held = new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 1, Description = "h", Quantity = 1, UnitPrice = 1, TotalAmount = 1, QuotationLifecycleStatus = "QUOTATION_APPROVED" };
        var released = new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 2, Description = "r", Quantity = 1, UnitPrice = 0, TotalAmount = 0, QuotationLifecycleStatus = "QUOTATION_PENDING" };
        ctx.RequestLineItems.AddRange(held, released);
        if (withBatch)
        {
            var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = req.Id, BatchNumber = 1, Status = "APPROVED", CreatedByUserId = buyer, CreatedAtUtc = DateTime.UtcNow };
            ctx.ApprovalBatches.Add(batch);
            ctx.ApprovalBatchItems.AddRange(
                new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = held.Id, CreatedAtUtc = DateTime.UtcNow },
                new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = released.Id, CreatedAtUtc = DateTime.UtcNow, ReleasedAtUtc = DateTime.UtcNow, ReleaseKind = "RESTART_QUOTATION" });
        }
        await ctx.SaveChangesAsync();
        return (req.Id, buyer);
    }

    [Theory]
    [InlineData(AttachmentConstants.Types.Proforma)]
    [InlineData(AttachmentConstants.Types.Quotation)]
    public async Task E_QuotationDocumentUpload_ItemWindow_Accepted(string typeCode)
    {
        using var ctx = NewInMemory();
        var (requestId, buyer) = await SeedInMemoryAsync(ctx, withBatch: true);
        var result = await BuildAttachments(ctx, buyer).Upload(requestId, new List<IFormFile> { Pdf() }, null, typeCode);
        var status = (result as ObjectResult)?.StatusCode ?? (result as StatusCodeResult)?.StatusCode ?? 200;
        Assert.True(status < 400, $"upload refused with {status}: {(result as ObjectResult)?.Value}");
        Assert.Equal(1, await ctx.RequestAttachments.CountAsync(a => a.RequestId == requestId && a.AttachmentTypeCode == typeCode));
    }

    [Fact]
    public async Task F_QuotationDocumentUpload_LegacyWithoutBatches_StillBlocked()
    {
        using var ctx = NewInMemory();
        var (requestId, buyer) = await SeedInMemoryAsync(ctx, withBatch: false);
        var result = await BuildAttachments(ctx, buyer).Upload(requestId, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.Quotation);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Upload Bloqueado", ((ProblemDetails)bad.Value!).Title);
        Assert.Equal(0, await ctx.RequestAttachments.CountAsync(a => a.RequestId == requestId));
    }

    // ── G. The static window helper itself ──
    [Fact]
    public async Task G_QuotationDocumentWindowOpenAsync_Scalar_Or_ItemWindow()
    {
        using var ctx = NewInMemory();
        var (requestId, _) = await SeedInMemoryAsync(ctx, withBatch: true);
        var request = await ctx.Requests.Include(r => r.Status).Include(r => r.RequestType).SingleAsync(r => r.Id == requestId);
        Assert.True(await AttachmentsController.QuotationDocumentWindowOpenAsync(ctx, request));
        // Close the window: mark the released line as closed-not-quoted → no open line → closed.
        var released = await ctx.RequestLineItems.SingleAsync(li => li.RequestId == requestId && li.LineNumber == 2);
        released.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted;
        await ctx.SaveChangesAsync();
        Assert.False(await AttachmentsController.QuotationDocumentWindowOpenAsync(ctx, request));
    }
}
