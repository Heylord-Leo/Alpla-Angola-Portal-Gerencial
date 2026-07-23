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
/// Covers AttachmentsController.Upload's group-scoped eligibility for QUOTATION requests with
/// PO groups — specifically the confirmed request-100 bug: PAYMENT_PROOF/PAYMENT_SCHEDULE uploads
/// failing because RequestPoGroupId never reached the endpoint, and PAYMENT_SCHEDULE additionally
/// having no group-status fallback at all (unlike PurchaseOrder/PaymentProof, which already had one).
/// No controller-level HTTP/auth test framework exists in this repo — see
/// FinanceMarkAsPaidTransitionTests/ConfirmAdvancePaymentTests for the established direct-
/// instantiation precedent, extended here to AttachmentsController.
/// </summary>
public class AttachmentUploadTests : IDisposable
{
    private readonly string _scratchDir;

    public AttachmentUploadTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_attachments_" + Guid.NewGuid());
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private AttachmentsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(_scratchDir);

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c[It.IsAny<string>()]).Returns(_scratchDir); // resolves storage path immediately (already exists)

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

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.SystemAdministrator)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private static IFormFile MakeFormFile(string fileName = "comprovativo.pdf")
    {
        var bytes = Encoding.UTF8.GetBytes("test-file-content");
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private sealed record Seed(Guid RequestId, Guid NcrGroupId, Guid ItecGroupId, Guid ActorId, Guid OtherRequestGroupId, int PoIssuedStatusId);

    /// <summary>Mirrors request 100: QUOTATION request, NCR (PO_ISSUED) + ITEC (ADVANCE_PAYMENT_REQUIRED)
    /// PO groups, parent aggregated status ADVANCE_PAYMENT_REQUIRED (the furthest-behind group) —
    /// deliberately NOT in PaymentSchedule's own parent-level allow-list, so a successful
    /// PAYMENT_SCHEDULE upload against NCR/ITEC can only be explained by the group-level fallback,
    /// not by a lucky parent-status match. A second, unrelated request supplies a "foreign" group id.</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = "finance-attach@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        ctx.RequestTypes.Add(requestType);

        var advanceRequired = new RequestStatus { Id = 1, Code = RequestConstants.Statuses.AdvancePaymentRequired, Name = "Adiantamento Necessário", DisplayOrder = 23 };
        var poIssuedStatus = new RequestStatus { Id = 2, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 13 };
        ctx.RequestStatuses.AddRange(advanceRequired, poIssuedStatus);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ-100-CLONE-ATTACH",
            Title = "ZZTEST Attachment upload",
            RequestTypeId = requestType.Id,
            StatusId = advanceRequired.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var ncrGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "NCR ANGOLA INFORMATICA, LDA",
            CurrencyCode = "AOA",
            TotalAmount = 70341.42m,
            Status = RequestConstants.Statuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = actor.Id
        };
        var itecGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ITEC LDA",
            CurrencyCode = "AOA",
            TotalAmount = 275139.00m,
            Status = RequestConstants.Statuses.AdvancePaymentRequired,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.AddRange(ncrGroup, itecGroup);

        // A second, unrelated request — its group id must never be accepted for the first request.
        var otherRequest = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ-OTHER",
            Title = "ZZTEST Other request",
            RequestTypeId = requestType.Id,
            StatusId = advanceRequired.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(otherRequest);
        var otherGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = otherRequest.Id,
            SupplierNameSnapshot = "OTHER SUPPLIER",
            CurrencyCode = "AOA",
            TotalAmount = 1000m,
            Status = RequestConstants.Statuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(otherGroup);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, ncrGroup.Id, itecGroup.Id, actor.Id, otherGroup.Id, poIssuedStatus.Id);
    }

    // ── PAYMENT_PROOF ──

    // Test 1: valid group id succeeds.
    [Fact]
    public async Task Upload_PaymentProof_WithValidGroupId_Succeeds()
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            AttachmentConstants.Types.PaymentProof, seed.NcrGroupId);

        Assert.IsType<OkObjectResult>(result);
    }

    // Test 2: missing group id remains rejected.
    [Fact]
    public async Task Upload_PaymentProof_WithoutGroupId_Rejected()
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            AttachmentConstants.Types.PaymentProof, null);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Grupo P.O. Obrigatório", problem.Title);
    }

    // Test 3: group id from another request is rejected.
    [Fact]
    public async Task Upload_PaymentProof_WithForeignGroupId_Rejected()
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            AttachmentConstants.Types.PaymentProof, seed.OtherRequestGroupId);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Grupo P.O. Inválido", problem.Title);
    }

    // ── PAYMENT_SCHEDULE ──

    // Tests 4-7: eligible group statuses succeed via the group-level fallback, despite the parent's
    // aggregated status (ADVANCE_PAYMENT_REQUIRED) not being in PaymentSchedule's own allow-list.
    [Theory]
    [InlineData("PO_ISSUED")]
    [InlineData("PAYMENT_REQUEST_SENT")]
    [InlineData("ADVANCE_PAYMENT_REQUIRED")]
    [InlineData("PAYMENT_SCHEDULED")]
    public async Task Upload_PaymentSchedule_EligibleGroupStatus_Succeeds(string statusCode)
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.NcrGroupId);
        group.Status = statusCode;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            AttachmentConstants.Types.PaymentSchedule, seed.NcrGroupId);

        Assert.IsType<OkObjectResult>(result);
    }

    // Test 8: missing group id on a grouped QUOTATION request is rejected.
    // The parent status is overridden to PO_ISSUED (itself already eligible at the parent-level
    // gate) so the assertion isolates and exercises the group-linkage enforcement's specific
    // message, rather than the earlier generic "Upload Bloqueado" gate that would otherwise fire
    // first for request 100's actual ADVANCE_PAYMENT_REQUIRED parent status — both gates already
    // reject the request either way; this only picks the scenario that proves the more specific one.
    [Fact]
    public async Task Upload_PaymentSchedule_WithoutGroupId_Rejected()
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var request = await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId);
        request.StatusId = seed.PoIssuedStatusId;
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            AttachmentConstants.Types.PaymentSchedule, null);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Grupo P.O. Obrigatório", problem.Title);
    }

    // Test 9: group id from another request is rejected. Same parent-status override rationale as above.
    [Fact]
    public async Task Upload_PaymentSchedule_WithForeignGroupId_Rejected()
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var request = await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId);
        request.StatusId = seed.PoIssuedStatusId;
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            AttachmentConstants.Types.PaymentSchedule, seed.OtherRequestGroupId);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Grupo P.O. Inválido", problem.Title);
    }

    // Test 10: an ineligible group status is rejected — including the deliberately-excluded
    // ADVANCE_PAYMENT_SCHEDULED (no existing rule supports it; must not be invented here).
    [Theory]
    [InlineData("WAITING_PO")]
    [InlineData("ADVANCE_PAYMENT_SCHEDULED")]
    public async Task Upload_PaymentSchedule_IneligibleGroupStatus_Rejected(string ineligibleStatus)
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.NcrGroupId);
        group.Status = ineligibleStatus;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile() }, null,
            AttachmentConstants.Types.PaymentSchedule, seed.NcrGroupId);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Upload Bloqueado", problem.Title);
    }

    // Test 11: successful upload persists the exact RequestPoGroupId and attachment type code —
    // and the sibling group (ITEC) never receives it (proves NCR proof is linked only to NCR).
    [Fact]
    public async Task Upload_PaymentProof_PersistsExactGroupIdAndTypeCode_SiblingUnaffected()
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.Upload(seed.RequestId, new List<IFormFile> { MakeFormFile("comprovativo-ncr.pdf") }, null,
            AttachmentConstants.Types.PaymentProof, seed.NcrGroupId);

        Assert.IsType<OkObjectResult>(result);

        var attachment = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.RequestId == seed.RequestId);
        Assert.Equal(seed.NcrGroupId, attachment.RequestPoGroupId);
        Assert.NotEqual(seed.ItecGroupId, attachment.RequestPoGroupId);
        Assert.Equal(AttachmentConstants.Types.PaymentProof, attachment.AttachmentTypeCode);
    }
}
