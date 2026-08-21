using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Application.Models.Configuration;
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
/// The generic <c>attachments/check-duplicate</c> preflight after the v2.229.10 cross-request
/// LEVEL 1 extension: <c>isActiveSourceDocument</c> is true only when the hash is an active
/// (non-voided) source document of a live (non-CANCELLED/REJECTED) request — the same
/// discrimination the persistence guard enforces, via the same shared query
/// (<c>PaymentSourceDocumentFileTwins</c>), so preflight and enforcement cannot drift.
///
/// <para>The defect this closes: the creation wizard consulted this endpoint, which flattened
/// every hash match into one overrideable "Documento Já Existente" warning — offering
/// "Estou Ciente, Prosseguir" for a case persistence would refuse with
/// DUPLICATE_FILE_CROSS_REQUEST after the user had done the whole OCR-and-review work.</para>
/// </summary>
public class AttachmentCheckDuplicateEndpointTests : IDisposable
{
    private const string Hash = "twin-bytes-hash";
    private readonly string _scratchDir;

    public AttachmentCheckDuplicateEndpointTests()
    {
        _scratchDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZZTEST_dupcheck_" + Guid.NewGuid());
        System.IO.Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort */ }
    }

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private AttachmentsController BuildController(
        ApplicationDbContext ctx, Guid actorId, bool systemAdministrator = true)
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

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, systemAdministrator ? RoleConstants.SystemAdministrator : RoleConstants.Finance)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid AttachmentId, Guid ActorId);

    /// <summary>One request holding one attachment with the twin hash; source document optional.</summary>
    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx,
        string requestStatus = "PO_ISSUED",
        bool asSourceDocument = true,
        bool voided = false,
        int? plantId = 1)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Twin Owner", Email = "twin@test.local" };
        ctx.Users.Add(actor);

        if (!await ctx.RequestTypes.AnyAsync(t => t.Id == 2))
            ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });

        const int statusId = 500;   // one status per isolated in-memory context
        ctx.RequestStatuses.Add(new RequestStatus
        {
            Id = statusId, Code = requestStatus, Name = requestStatus, DisplayOrder = 1
        });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-TWIN-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST twin request",
            RequestTypeId = 2,
            StatusId = statusId,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = plantId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        ctx.Requests.Add(request);

        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "proposta.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_RECEIPT,
            StorageReference = "zztest/twin-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            FileHash = Hash,
            UploadedByUserId = actor.Id,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        ctx.RequestAttachments.Add(attachment);

        if (asSourceDocument)
        {
            ctx.PaymentSourceDocuments.Add(new PaymentSourceDocument
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                AttachmentId = attachment.Id,
                SupplierId = 77,
                DocumentNumber = "ONP_18910_v3",
                SequenceNumber = 1,
                IsVoided = voided,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
                CreatedByUserId = actor.Id
            });
        }

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, attachment.Id, actor.Id);
    }

    private static T Read<T>(IActionResult result, string property)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var prop = ok.Value!.GetType().GetProperty(property);
        return prop == null ? default! : (T)prop.GetValue(ok.Value)!;
    }

    private static bool Has(IActionResult result, string property)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ok.Value!.GetType().GetProperty(property) != null;
    }

    // ── A. Live source-document twin ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_live_source_document_twin_is_flagged_as_active_with_its_request_named()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, requestStatus: "PO_ISSUED", asSourceDocument: true);

        var result = await BuildController(ctx, seed.ActorId).CheckDuplicate(Hash);

        Assert.True(Read<bool>(result, "isDuplicate"));
        Assert.True(Read<bool>(result, "isActiveSourceDocument"));
        Assert.Equal(seed.RequestId, Read<Guid>(result, "requestId"));
        Assert.StartsWith("ZZTEST-TWIN-", Read<string>(result, "requestNumber"));
    }

    // ── B. Attachment-only reuse keeps the warn tier ────────────────────────────────────────

    [Fact]
    public async Task An_attachment_only_twin_is_a_duplicate_but_not_an_active_source_document()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, asSourceDocument: false);

        var result = await BuildController(ctx, seed.ActorId).CheckDuplicate(Hash);

        Assert.True(Read<bool>(result, "isDuplicate"));
        Assert.False(Read<bool>(result, "isActiveSourceDocument"));
    }

    // ── C / D. Dead requests are not blocking evidence ──────────────────────────────────────

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("REJECTED")]
    public async Task A_twin_on_a_dead_request_is_not_active_blocking_evidence(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, requestStatus: status, asSourceDocument: true);

        var result = await BuildController(ctx, seed.ActorId).CheckDuplicate(Hash);

        Assert.True(Read<bool>(result, "isDuplicate"));
        Assert.False(Read<bool>(result, "isActiveSourceDocument"));
    }

    // ── E. Voided documents are inactive evidence ───────────────────────────────────────────

    [Fact]
    public async Task A_voided_source_document_is_not_active_blocking_evidence()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, asSourceDocument: true, voided: true);

        // Even with a source-typed attachment, the DOCUMENT ROW is the authority: voided means
        // inactive evidence, and the legacy-attachment branch must never resurrect it.
        var attachment = await ctx.RequestAttachments.SingleAsync();
        attachment.AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT;
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).CheckDuplicate(Hash);

        Assert.True(Read<bool>(result, "isDuplicate"));
        Assert.False(Read<bool>(result, "isActiveSourceDocument"));
    }

    // ── F. Restricted user: the block signal survives, the metadata does not ────────────────

    [Fact]
    public async Task A_restricted_user_gets_the_blocking_signal_but_no_request_metadata()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, asSourceDocument: true, plantId: 1);

        // A non-admin whose plant scope excludes the twin's plant: the scoped query cannot see
        // the twin's request.
        var restricted = new User { Id = Guid.NewGuid(), FullName = "Restricted", Email = "restricted@test.local" };
        ctx.Users.Add(restricted);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = restricted.Id, PlantId = 99 });
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, restricted.Id, systemAdministrator: false)
            .CheckDuplicate(Hash);

        Assert.True(Read<bool>(result, "isDuplicate"));
        Assert.True(Read<bool>(result, "isActiveSourceDocument"));   // enough to hard-block
        Assert.False(Has(result, "requestNumber"));                  // nothing identifying leaks
        Assert.False(Has(result, "requestId"));
        Assert.False(Has(result, "uploadedBy"));
        Assert.False(Has(result, "createdAtUtc"));
    }

    // ── G. Authorized user keeps the permitted metadata ─────────────────────────────────────

    [Fact]
    public async Task An_authorized_user_still_receives_the_permitted_metadata()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, asSourceDocument: false);

        var result = await BuildController(ctx, seed.ActorId).CheckDuplicate(Hash);

        Assert.True(Read<bool>(result, "isDuplicate"));
        Assert.True(Has(result, "requestNumber"));
        Assert.Equal("Twin Owner", Read<string>(result, "uploadedBy"));
    }

    // ── Legacy pre-Release-3 twins (the REQ-21/07/2026-116 shape) ───────────────────────────
    //
    // PaymentSourceDocuments was born on 2026-08-04; a July request's proforma exists only as a
    // source-TYPED RequestAttachment with no document row. MODEL B of the cross-request file
    // audit: that file is still the commercial source of a live request — blocking evidence.

    private async Task<IActionResult> CheckLegacyShapeAsync(
        string attachmentTypeCode, string requestStatus = "PO_ISSUED")
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, requestStatus: requestStatus, asSourceDocument: false);

        var attachment = await ctx.RequestAttachments.SingleAsync();
        attachment.AttachmentTypeCode = attachmentTypeCode;
        await ctx.SaveChangesAsync();

        return await BuildController(ctx, seed.ActorId).CheckDuplicate(Hash);
    }

    [Theory]
    [InlineData(RequestAttachment.TYPE_PROFORMA)]
    [InlineData(RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT)]
    public async Task A_legacy_source_typed_attachment_on_a_live_request_is_blocking_evidence(string typeCode)
    {
        var result = await CheckLegacyShapeAsync(typeCode);

        Assert.True(Read<bool>(result, "isDuplicate"));
        Assert.True(Read<bool>(result, "isActiveSourceDocument"));
        Assert.StartsWith("ZZTEST-TWIN-", Read<string>(result, "requestNumber"));
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("REJECTED")]
    public async Task A_legacy_source_attachment_on_a_dead_request_is_not_blocking(string status)
    {
        var result = await CheckLegacyShapeAsync(RequestAttachment.TYPE_PROFORMA, requestStatus: status);

        Assert.True(Read<bool>(result, "isDuplicate"));
        Assert.False(Read<bool>(result, "isActiveSourceDocument"));
    }

    [Theory]
    [InlineData(RequestAttachment.TYPE_RECEIPT)]
    [InlineData(RequestAttachment.TYPE_PO)]
    [InlineData(RequestAttachment.TYPE_PAYMENT_PROOF)]
    [InlineData(RequestAttachment.TYPE_QUOTATION)]
    public async Task A_generic_supporting_attachment_stays_on_the_warn_tier(string typeCode)
    {
        // Supporting evidence legitimately recurs across requests — never hard-blocked.
        var result = await CheckLegacyShapeAsync(typeCode);

        Assert.True(Read<bool>(result, "isDuplicate"));
        Assert.False(Read<bool>(result, "isActiveSourceDocument"));
    }

    [Fact]
    public async Task An_unknown_hash_is_simply_not_a_duplicate()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);

        var result = await BuildController(ctx, seed.ActorId).CheckDuplicate("nothing-like-this");

        Assert.False(Read<bool>(result, "isDuplicate"));
        Assert.False(Has(result, "isActiveSourceDocument"));
    }
}
