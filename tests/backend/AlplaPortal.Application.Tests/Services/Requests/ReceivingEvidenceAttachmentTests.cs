using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.229.4 (REQ-17/08/2026-232): the RECEIVING_EVIDENCE attachment type — optional operational
/// receiving evidence, strictly separated from the legacy TYPE_RECEIPT (untouched, rule R18) and
/// from the Finance-owned TYPE_FISCAL_RECEIPT (fiscal facts never change through this path).
/// </summary>
public class ReceivingEvidenceAttachmentTests : IDisposable
{
    private readonly string _scratchDir;

    public ReceivingEvidenceAttachmentTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_recv_evidence_" + Guid.NewGuid());
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort */ }
    }

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private AttachmentsController BuildController(ApplicationDbContext ctx, Guid actorId, string role)
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(_scratchDir);
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c[It.IsAny<string>()]).Returns(_scratchDir);

        var securityOptions = Options.Create(new SecurityOptions
        {
            Upload = new UploadOptions
            {
                AllowedExtensions = new List<string> { ".pdf" },
                BlockedExtensions = new List<string>(),
                MaxFileSizeBytes = 10_000_000
            }
        });

        var controller = new AttachmentsController(ctx, envMock.Object, securityOptions, configMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, role)
                }, "Test"))
            }
        };
        return controller;
    }

    private static IFormFile MakeFormFile(string fileName = "guia-entrega.pdf")
    {
        var bytes = Encoding.UTF8.GetBytes("zz-evidence");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId);

    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx,
        string requestStatusCode = RequestConstants.Statuses.WaitingSupplierDelivery,
        string groupStatusCode = RequestConstants.Statuses.WaitingSupplierDelivery)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Receiving Evidence", Email = "re4@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 1, Code = requestStatusCode, Name = "ZZTEST", DisplayOrder = 1 });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-RE4-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST receiving evidence",
            RequestTypeId = 1,
            StatusId = 1,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST Evidence Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 1000m,
            Status = groupStatusCode,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, actor.Id);
    }

    // ── C/F/G: allowed at WSD for Receiving, linked to the group, fiscal fields untouched ──

    [Fact]
    public async Task Receiving_uploads_evidence_at_supplier_delivery_linked_to_the_group()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Receiving);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            RequestAttachment.TYPE_RECEIVING_EVIDENCE, seed.GroupId);

        Assert.IsType<OkObjectResult>(result);

        var attachment = await ctx.RequestAttachments.AsNoTracking()
            .SingleAsync(a => a.RequestId == seed.RequestId);
        Assert.Equal(RequestAttachment.TYPE_RECEIVING_EVIDENCE, attachment.AttachmentTypeCode);
        Assert.Equal(seed.GroupId, attachment.RequestPoGroupId); // group-linked, not request-only

        // Fiscal separation: uploading operational evidence can never write fiscal facts.
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Null(group.FiscalReceiptAttachmentId);
        Assert.Null(group.FiscalReceiptUploadedAtUtc);
        Assert.Null(group.FiscalReceiptUploadedByUserId);

        // History labels it as the operational document, never "Recibo".
        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync();
        Assert.Contains("Comprovativo de Recebimento", history.Comment);
    }

    [Theory]
    [InlineData(RoleConstants.Buyer)]
    [InlineData(RoleConstants.SystemAdministrator)]
    public async Task Buyer_and_sysadmin_share_the_operational_receiving_capability(string role)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.PaymentCompleted);
        var controller = BuildController(ctx, seed.ActorId, role);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            RequestAttachment.TYPE_RECEIVING_EVIDENCE, seed.GroupId);

        Assert.IsType<OkObjectResult>(result);
    }

    // ── D: roles outside the receiving capability are refused ──

    [Theory]
    [InlineData(RoleConstants.Finance)]
    [InlineData("Requester")]
    public async Task Roles_outside_the_receiving_capability_are_refused(string role)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId, role);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            RequestAttachment.TYPE_RECEIVING_EVIDENCE, seed.GroupId);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Contains("Recebimento", problem.Detail);
        Assert.False(await ctx.RequestAttachments.AnyAsync());
    }

    // ── E: outside the receiving phase the type is refused ──

    [Fact]
    public async Task Outside_the_receiving_phase_the_evidence_type_is_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, RequestConstants.Statuses.Draft, RequestConstants.PoGroupStatuses.WaitingPo);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Receiving);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            RequestAttachment.TYPE_RECEIVING_EVIDENCE, seed.GroupId);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(await ctx.RequestAttachments.AnyAsync());
    }

    // ── Group-status fallback: lagging parent aggregate, receiving-stage group ──

    [Fact]
    public async Task Group_level_receiving_stage_unlocks_the_upload_when_the_parent_lags()
    {
        using var ctx = NewContext();
        // Parent projects an earlier stage (multi-group lag); the target group is receiving-ready.
        var seed = await SeedAsync(ctx, RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.WaitingSupplierDelivery);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Receiving);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            RequestAttachment.TYPE_RECEIVING_EVIDENCE, seed.GroupId);

        Assert.IsType<OkObjectResult>(result);
    }

    // ── M: legacy TYPE_RECEIPT semantics untouched ──

    [Fact]
    public async Task Legacy_receipt_type_keeps_its_exact_legacy_gate()
    {
        using var ctx = NewContext();

        // Still Finance-only and WAITING_RECEIPT-only — byte-identical to before this patch.
        var atDelivery = await SeedAsync(ctx);
        var receivingController = BuildController(ctx, atDelivery.ActorId, RoleConstants.Receiving);
        var refused = await receivingController.Upload(atDelivery.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            AttachmentConstants.Types.Receipt, atDelivery.GroupId);
        Assert.IsType<BadRequestObjectResult>(refused);

        using var ctx2 = NewContext();
        var atReceipt = await SeedAsync(ctx2, RequestConstants.Statuses.WaitingReceipt, RequestConstants.Statuses.WaitingReceipt);
        var financeController = BuildController(ctx2, atReceipt.ActorId, RoleConstants.Finance);
        var accepted = await financeController.Upload(atReceipt.RequestId, new List<IFormFile> { MakeFormFile("recibo.pdf") }, null,
            AttachmentConstants.Types.Receipt, atReceipt.GroupId);
        Assert.IsType<OkObjectResult>(accepted);
    }
}
