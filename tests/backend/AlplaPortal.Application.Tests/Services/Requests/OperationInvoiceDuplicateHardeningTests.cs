using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Doc = RequestConstants.OperationInvoiceDocumentStatuses;
using Req = RequestConstants.Statuses;

/// <summary>
/// Release 4 Phase 2d: attachment and duplicate hardening.
///
/// <para>Two independent duplicate dimensions, both GLOBAL over effective invoices: the fiscal
/// identity (supplier + normalized number + series) and the physical file (hash). Terminal
/// invoices — VOIDED, REJECTED, superseded predecessors — release both. Preflight advises;
/// Create/Update/Replace stay authoritative; exact retries are idempotent.</para>
/// </summary>
public class OperationInvoiceDuplicateHardeningTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static OperationInvoicesController BuildController(
        ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.Finance)
    {
        var controller = new OperationInvoicesController(
            ctx,
            NullLogger<OperationInvoicesController>.Instance,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, role)
                }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid ActorId);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Hardening Tester", Email = "hard@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 30, Code = Req.Paid, Name = Req.Paid, DisplayOrder = 30 });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "ZZTEST Supplier", TaxId = "111000111" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-HARD-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST hardening",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-25)
        };
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static Request AddSecondRequest(ApplicationDbContext ctx, Seed seed)
    {
        var other = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-HARD-2",
            Title = "ZZTEST second",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = seed.ActorId,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(other);
        return other;
    }

    private static RequestAttachment AddAttachment(
        ApplicationDbContext ctx, Guid requestId, Guid actorId, string? fileHash = null)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            FileName = "fatura.pdf",
            FileExtension = ".pdf",
            FileHash = fileHash,
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/h-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = actorId,
            UploadedAtUtc = DateTime.UtcNow
        };
        ctx.RequestAttachments.Add(attachment);
        return attachment;
    }

    private static OperationInvoice AddInvoice(
        ApplicationDbContext ctx, Guid requestId, Guid actorId,
        string status = Doc.PendingValidation,
        string number = "FT 100", string? series = "A",
        Guid? attachmentId = null)
    {
        var invoice = new OperationInvoice
        {
            RequestId = requestId,
            AttachmentId = attachmentId ?? Guid.NewGuid(),
            SupplierId = 10,
            SupplierTaxIdSnapshot = "111000111",
            DocumentNumber = number,
            DocumentSeries = series,
            DocumentDate = new DateTime(2026, 8, 1),
            Currency = "AOA",
            GrossAmount = 100_000m,
            Status = status,
            AmountsEnteredManually = true,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            UploadedByUserId = actorId
        };
        ctx.OperationInvoices.Add(invoice);
        return invoice;
    }

    private static SaveOperationInvoiceDto Dto(
        Guid attachmentId, string number = "FT 100", string? series = "A") => new()
    {
        AttachmentId = attachmentId,
        SupplierId = 10,
        DocumentNumber = number,
        DocumentSeries = series,
        DocumentDate = new DateTime(2026, 8, 2),
        Currency = "AOA",
        GrossAmount = 100_000m
    };

    private static void AssertDuplicate(IActionResult result, string expectedCode)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(expectedCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
    }

    // ── Business identity: normalization ──

    [Fact]
    public async Task Case_and_whitespace_never_split_one_fiscal_identity()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        AddInvoice(ctx, seed.RequestId, seed.ActorId, number: "FT 100", series: "A");
        var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId);
        await ctx.SaveChangesAsync();

        AssertDuplicate(
            await BuildController(ctx, seed.ActorId)
                .Create(seed.RequestId, Dto(attachment.Id, number: "  ft 100  ", series: " a ")),
            OperationInvoicesController.DuplicateErrorCode);
    }

    [Fact]
    public async Task A_null_series_and_a_blank_series_are_the_same_series()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        AddInvoice(ctx, seed.RequestId, seed.ActorId, number: "FT 100", series: null);
        var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId);
        await ctx.SaveChangesAsync();

        AssertDuplicate(
            await BuildController(ctx, seed.ActorId)
                .Create(seed.RequestId, Dto(attachment.Id, number: "FT 100", series: "  ")),
            OperationInvoicesController.DuplicateErrorCode);
    }

    // ── Business identity: the effective-status matrix, pinned per status ──

    [Theory]
    [InlineData(Doc.Uploaded, true)]
    [InlineData(Doc.PendingValidation, true)]
    [InlineData(Doc.Validated, true)]
    [InlineData(Doc.DivergenceDetected, true)]   // still a claimed identity awaiting a decision
    [InlineData(Doc.Rejected, false)]
    [InlineData(Doc.Voided, false)]
    [InlineData(Doc.ReplacementRequested, false)]
    public async Task Each_lifecycle_status_participates_in_duplicates_exactly_as_approved(
        string existingStatus, bool blocks)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        AddInvoice(ctx, seed.RequestId, seed.ActorId, status: existingStatus);
        var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, Dto(attachment.Id));

        if (blocks)
            AssertDuplicate(result, OperationInvoicesController.DuplicateErrorCode);
        else
            Assert.IsType<OkObjectResult>(result);
    }

    // ── File hash: authoritative enforcement, global scope ──

    [Fact]
    public async Task The_same_physical_file_in_a_new_attachment_row_is_refused_on_create()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var firstFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-A");
        AddInvoice(ctx, seed.RequestId, seed.ActorId, number: "FT 100", attachmentId: firstFile.Id);
        // A DIFFERENT RequestAttachment row, same bytes: AttachmentId uniqueness cannot see it.
        var secondFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-A");
        await ctx.SaveChangesAsync();

        AssertDuplicate(
            await BuildController(ctx, seed.ActorId)
                .Create(seed.RequestId, Dto(secondFile.Id, number: "FT 999")),
            OperationInvoicesController.FileDuplicateErrorCode);
    }

    [Fact]
    public async Task The_same_file_on_a_different_request_is_still_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var firstFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-B");
        AddInvoice(ctx, seed.RequestId, seed.ActorId, attachmentId: firstFile.Id);
        var other = AddSecondRequest(ctx, seed);
        var otherFile = AddAttachment(ctx, other.Id, seed.ActorId, fileHash: "ZZHASH-B");
        await ctx.SaveChangesAsync();

        // The same fiscal file must not become a new debt in another request.
        AssertDuplicate(
            await BuildController(ctx, seed.ActorId)
                .Create(other.Id, Dto(otherFile.Id, number: "FT 777")),
            OperationInvoicesController.FileDuplicateErrorCode);
    }

    [Fact]
    public async Task A_voided_invoice_releases_its_file_for_a_legitimate_reupload()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var firstFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-C");
        AddInvoice(ctx, seed.RequestId, seed.ActorId,
            status: Doc.Voided, attachmentId: firstFile.Id);
        var reupload = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-C");
        await ctx.SaveChangesAsync();

        Assert.IsType<OkObjectResult>(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, Dto(reupload.Id)));
    }

    [Fact]
    public async Task An_update_cannot_swap_in_a_file_that_is_already_someone_elses_invoice()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var otherFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-D");
        AddInvoice(ctx, seed.RequestId, seed.ActorId, number: "FT 100", attachmentId: otherFile.Id);
        var target = AddInvoice(ctx, seed.RequestId, seed.ActorId, number: "FT 200");
        var newFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-D");
        await ctx.SaveChangesAsync();

        AssertDuplicate(
            await BuildController(ctx, seed.ActorId).Update(seed.RequestId, target.Id,
                new SaveOperationInvoiceDto { AttachmentId = newFile.Id }),
            OperationInvoicesController.FileDuplicateErrorCode);
    }

    [Fact]
    public async Task A_replacement_may_reuse_the_originals_file_content_but_not_a_third_partys()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);

        var originalFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-E");
        var original = AddInvoice(ctx, seed.RequestId, seed.ActorId,
            status: Doc.Validated, number: "FT 100", attachmentId: originalFile.Id);

        var thirdFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-F");
        AddInvoice(ctx, seed.RequestId, seed.ActorId, number: "FT 300", attachmentId: thirdFile.Id);

        // Same content as the ORIGINAL (allowed: only the header was wrong) …
        var sameContentAsOriginal = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-E");
        // … and same content as the third party's effective invoice (never allowed).
        var sameContentAsThird = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-F");
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        var collide = new ReplaceOperationInvoiceDto
        {
            AttachmentId = sameContentAsThird.Id,
            SupplierId = 10,
            DocumentNumber = "FT 100",
            DocumentSeries = "A",
            DocumentDate = new DateTime(2026, 8, 5),
            Currency = "AOA",
            GrossAmount = 100_000m,
            ReplacementReason = "ZZTEST tentativa com ficheiro alheio"
        };
        AssertDuplicate(await controller.Replace(seed.RequestId, original.Id, collide),
            OperationInvoicesController.FileDuplicateErrorCode);

        var legitimate = new ReplaceOperationInvoiceDto
        {
            AttachmentId = sameContentAsOriginal.Id,
            SupplierId = 10,
            DocumentNumber = "FT 100",
            DocumentSeries = "A",
            DocumentDate = new DateTime(2026, 8, 5),
            Currency = "AOA",
            GrossAmount = 120_000m,
            ReplacementReason = "ZZTEST cabeçalho corrigido, mesmo documento"
        };
        Assert.IsType<OkObjectResult>(await controller.Replace(seed.RequestId, original.Id, legitimate));
    }

    // ── Preflight: the four quadrants ──

    [Fact]
    public async Task The_preflight_distinguishes_file_identity_both_and_neither()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var claimedFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-G");
        AddInvoice(ctx, seed.RequestId, seed.ActorId, number: "FT 100", attachmentId: claimedFile.Id);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        async Task<OperationInvoiceDuplicateResultDto> Check(string? hash, string? number) =>
            Assert.IsType<OperationInvoiceDuplicateResultDto>(Assert.IsType<OkObjectResult>(
                (await controller.CheckDuplicate(seed.RequestId, new CheckOperationInvoiceDuplicateDto
                {
                    ContentHash = hash,
                    SupplierId = 10,
                    DocumentNumber = number,
                    DocumentSeries = "A"
                })).Result).Value);

        var neither = await Check("ZZHASH-NEW", "FT 999");
        Assert.False(neither.HasDuplicate);
        Assert.Null(neither.SameFile);
        Assert.Null(neither.SameBusinessDocument);

        var fileOnly = await Check("ZZHASH-G", "FT 999");
        Assert.True(fileOnly.HasDuplicate);
        Assert.NotNull(fileOnly.SameFile);
        Assert.Null(fileOnly.SameBusinessDocument);

        var identityOnly = await Check("ZZHASH-NEW", "ft 100");
        Assert.True(identityOnly.HasDuplicate);
        Assert.Null(identityOnly.SameFile);
        Assert.NotNull(identityOnly.SameBusinessDocument);

        var both = await Check("ZZHASH-G", "FT 100");
        Assert.True(both.HasDuplicate);
        Assert.NotNull(both.SameFile);
        Assert.NotNull(both.SameBusinessDocument);
        Assert.Equal(seed.RequestId, both.SameBusinessDocument!.RequestId);
        Assert.Equal(Doc.PendingValidation, both.SameBusinessDocument.Status);
    }

    [Fact]
    public async Task A_clean_preflight_never_weakens_the_authoritative_create()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment1 = AddAttachment(ctx, seed.RequestId, seed.ActorId);
        var attachment2 = AddAttachment(ctx, seed.RequestId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        // Preflight: nothing there.
        var check = Assert.IsType<OperationInvoiceDuplicateResultDto>(Assert.IsType<OkObjectResult>(
            (await controller.CheckDuplicate(seed.RequestId, new CheckOperationInvoiceDuplicateDto
            {
                SupplierId = 10, DocumentNumber = "FT 100", DocumentSeries = "A"
            })).Result).Value);
        Assert.False(check.HasDuplicate);

        // Someone else wins the race between preflight and create.
        Assert.IsType<OkObjectResult>(await controller.Create(seed.RequestId, Dto(attachment1.Id)));

        // The original caller's create still hits the authoritative wall.
        AssertDuplicate(await controller.Create(seed.RequestId, Dto(attachment2.Id)),
            OperationInvoicesController.DuplicateErrorCode);
    }

    // ── Idempotent retries ──

    [Fact]
    public async Task An_exact_void_retry_returns_the_voided_invoice_without_a_second_history_row()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed.RequestId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var dto = new VoidOperationInvoiceDto { Reason = "ZZTEST registada em erro" };

        Assert.IsType<OkObjectResult>(await controller.Void(seed.RequestId, invoice.Id, dto));
        var retry = Assert.IsType<OkObjectResult>(await controller.Void(seed.RequestId, invoice.Id, dto));
        Assert.Equal(Doc.Voided,
            Assert.IsType<OperationInvoiceDto>(retry.Value).Status);

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.RequestStatusHistories
            .CountAsync(h => h.ActionTaken == "FATURA_OPERACAO_ANULADA"));
    }

    [Fact]
    public async Task An_exact_replace_retry_returns_the_existing_correction()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var original = AddInvoice(ctx, seed.RequestId, seed.ActorId, status: Doc.Validated);
        var newFile = AddAttachment(ctx, seed.RequestId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var dto = new ReplaceOperationInvoiceDto
        {
            AttachmentId = newFile.Id,
            SupplierId = 10,
            DocumentNumber = "FT 100",
            DocumentSeries = "A",
            DocumentDate = new DateTime(2026, 8, 5),
            Currency = "AOA",
            GrossAmount = 120_000m,
            ReplacementReason = "ZZTEST correção"
        };

        var first = Assert.IsType<OperationInvoiceDto>(
            Assert.IsType<OkObjectResult>(await controller.Replace(seed.RequestId, original.Id, dto)).Value);
        var second = Assert.IsType<OperationInvoiceDto>(
            Assert.IsType<OkObjectResult>(await controller.Replace(seed.RequestId, original.Id, dto)).Value);

        Assert.Equal(first.Id, second.Id);   // the SAME correction, not a second one

        ctx.ChangeTracker.Clear();
        Assert.Equal(2, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(1, await ctx.RequestStatusHistories
            .CountAsync(h => h.ActionTaken == "FATURA_OPERACAO_SUBSTITUIDA"));
    }

    // ── The database-race fallback, testable at the mapping seam ──

    [Fact]
    public void Only_the_attachment_unique_index_maps_to_the_attachment_claimed_conflict()
    {
        var attachmentRace = new DbUpdateException("boom",
            new Exception("Cannot insert duplicate key row with unique index " +
                          "'UX_OperationInvoice_AttachmentId'."));
        var unrelated = new DbUpdateException("boom",
            new Exception("Violation of PRIMARY KEY constraint 'PK_Something'."));
        var noInner = new DbUpdateException("boom");

        Assert.True(OperationInvoicesController.IsAttachmentUniqueViolation(attachmentRace));
        Assert.False(OperationInvoicesController.IsAttachmentUniqueViolation(unrelated));
        Assert.False(OperationInvoicesController.IsAttachmentUniqueViolation(noInner));
    }
}
