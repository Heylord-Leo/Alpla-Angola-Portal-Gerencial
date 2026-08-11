using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
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
using Agg = RequestConstants.OperationInvoiceStatuses;
using Req = RequestConstants.Statuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// Release 4 Phase 2c: POST …/{id}/replace — the only path out of VALIDATED.
///
/// <para>Pinned: Finance-only; one transaction moves the original to REPLACEMENT_REQUESTED with
/// the forward pointer and creates the correction in PENDING_VALIDATION; the original's identity
/// is freed in that same transaction while every OTHER effective invoice still blocks; downstream
/// Phase 3 evidence blocks replacement outright; and the chain A→B→C stays walkable.</para>
/// </summary>
public class OperationInvoiceReplaceTests
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

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string statusCode = Req.Paid)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Replace Tester", Email = "rep@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 30, Code = statusCode, Name = statusCode, DisplayOrder = 30 });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "ZZTEST Supplier", TaxId = "111000111" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REP-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST replace",
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

    private static OperationInvoice AddValidatedInvoice(
        ApplicationDbContext ctx, Seed seed,
        string number = "FT 100", string? series = "A")
    {
        var invoice = new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SupplierId = 10,
            SupplierTaxIdSnapshot = "111000111",
            DocumentNumber = number,
            DocumentSeries = series,
            DocumentDate = new DateTime(2026, 8, 1),
            Currency = "AOA",
            GrossAmount = 114_000m,
            Status = Doc.Validated,
            ValidatedAtUtc = DateTime.UtcNow.AddHours(-2),
            ValidatedByUserId = seed.ActorId,
            AmountsEnteredManually = true,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            UploadedByUserId = seed.ActorId
        };
        ctx.OperationInvoices.Add(invoice);
        return invoice;
    }

    private static RequestAttachment AddInvoiceAttachment(ApplicationDbContext ctx, Seed seed)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "fatura-corrigida.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/rep-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow
        };
        ctx.RequestAttachments.Add(attachment);
        return attachment;
    }

    /// <summary>A corrected invoice reusing the ORIGINAL's fiscal identity — the standard case.</summary>
    private static ReplaceOperationInvoiceDto CorrectionDto(Guid attachmentId) => new()
    {
        AttachmentId = attachmentId,
        SupplierId = 10,
        DocumentNumber = "FT 100",
        DocumentSeries = "A",
        DocumentDate = new DateTime(2026, 8, 5),
        Currency = "AOA",
        GrossAmount = 120_000m,
        ReplacementReason = "ZZTEST valores corrigidos pelo fornecedor"
    };

    private static OperationInvoiceDto Body(IActionResult result) =>
        Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(result).Value);

    // ── The transaction ──

    [Fact]
    public async Task A_validated_invoice_is_superseded_and_the_correction_enters_the_queue()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var original = AddValidatedInvoice(ctx, seed);
        var originalAttachmentId = original.AttachmentId;
        var newAttachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var replacement = Body(await controller.Replace(
            seed.RequestId, original.Id, CorrectionDto(newAttachment.Id)));

        // The correction: same identity, PENDING_VALIDATION, its own file.
        Assert.Equal(Doc.PendingValidation, replacement.Status);
        Assert.Equal("FT 100", replacement.DocumentNumber);
        Assert.Equal(newAttachment.Id, replacement.AttachmentId);
        Assert.Equal(120_000m, replacement.GrossAmount);

        ctx.ChangeTracker.Clear();
        var old = await ctx.OperationInvoices.SingleAsync(i => i.Id == original.Id);
        Assert.Equal(Doc.ReplacementRequested, old.Status);
        Assert.Equal(replacement.Id, old.SupersededByOperationInvoiceId);   // the walkable pointer
        Assert.Equal("ZZTEST valores corrigidos pelo fornecedor", old.RejectionReason);
        Assert.Equal(originalAttachmentId, old.AttachmentId);               // keeps its file forever

        // Both stay visible; the audit tells the story once.
        var list = Assert.IsType<List<OperationInvoiceDto>>(Assert.IsType<OkObjectResult>(
            (await controller.List(seed.RequestId)).Result).Value);
        Assert.Equal(2, list.Count);
        Assert.True(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_SUBSTITUIDA"));
    }

    [Fact]
    public async Task Replacing_with_no_allocations_changes_no_phase_1_state()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = 10,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 114_000m,
            Status = RequestConstants.PoGroupStatuses.WaitingPo,
            SourceDocumentType = Types.Proforma,
            OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true,
            ExpectedOperationInvoiceTotal = 114_000m,
            ExpectedOperationInvoiceCurrency = "AOA",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        var original = AddValidatedInvoice(ctx, seed);
        var newAttachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        Body(await BuildController(ctx, seed.ActorId).Replace(
            seed.RequestId, original.Id, CorrectionDto(newAttachment.Id)));

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(Agg.PendingUpload, persisted.OperationInvoiceStatus);
        Assert.Equal(114_000m, persisted.ExpectedOperationInvoiceTotal);
    }

    // ── Who and when ──

    [Fact]
    public async Task The_buyer_cannot_replace_a_validated_invoice()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var original = AddValidatedInvoice(ctx, seed);
        var newAttachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var forbidden = Assert.IsType<ObjectResult>(
            await BuildController(ctx, seed.ActorId, RoleConstants.Buyer)
                .Replace(seed.RequestId, original.Id, CorrectionDto(newAttachment.Id)));
        Assert.Equal(403, forbidden.StatusCode);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.Validated,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == original.Id)).Status);
    }

    [Theory]
    [InlineData(Doc.PendingValidation)]
    [InlineData(Doc.Uploaded)]
    [InlineData(Doc.Rejected)]
    [InlineData(Doc.Voided)]
    [InlineData(Doc.ReplacementRequested)]   // a terminal predecessor is never replaced again
    public async Task Only_a_validated_invoice_can_be_replaced(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var original = AddValidatedInvoice(ctx, seed);
        original.Status = status;
        var newAttachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var conflict = Assert.IsType<ConflictObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Replace(seed.RequestId, original.Id, CorrectionDto(newAttachment.Id)));
        Assert.Equal(OperationInvoicesController.NotReplaceableCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
    }

    [Fact]
    public async Task A_missing_reason_refuses_the_replacement_atomically()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var original = AddValidatedInvoice(ctx, seed);
        var newAttachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var dto = CorrectionDto(newAttachment.Id);
        dto.ReplacementReason = "  ";

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId).Replace(seed.RequestId, original.Id, dto));
        Assert.Contains("ReplacementReason",
            Assert.IsType<ValidationProblemDetails>(bad.Value).Errors.Keys);

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(Doc.Validated,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == original.Id)).Status);
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_SUBSTITUIDA"));
    }

    [Fact]
    public async Task The_original_attachment_cannot_be_reused_for_the_correction()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var originalAttachment = AddInvoiceAttachment(ctx, seed);
        var original = AddValidatedInvoice(ctx, seed);
        original.AttachmentId = originalAttachment.Id;
        await ctx.SaveChangesAsync();

        var dto = CorrectionDto(originalAttachment.Id);   // reusing the claimed file

        var conflict = Assert.IsType<ConflictObjectResult>(
            await BuildController(ctx, seed.ActorId).Replace(seed.RequestId, original.Id, dto));
        Assert.Contains("já está registado",
            Assert.IsType<ProblemDetails>(conflict.Value).Detail);
    }

    // ── Duplicate identity during replacement ──

    [Fact]
    public async Task The_correction_may_reuse_the_originals_identity_but_not_anyone_elses()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var original = AddValidatedInvoice(ctx, seed, number: "FT 100");
        AddValidatedInvoice(ctx, seed, number: "FT 200");   // another effective invoice
        var attachment1 = AddInvoiceAttachment(ctx, seed);
        var attachment2 = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        // Colliding with the OTHER effective invoice: refused, atomically.
        var collision = CorrectionDto(attachment1.Id);
        collision.DocumentNumber = "FT 200";
        var conflict = Assert.IsType<ConflictObjectResult>(
            await controller.Replace(seed.RequestId, original.Id, collision));
        Assert.Equal(OperationInvoicesController.DuplicateErrorCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal(2, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(Doc.Validated,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == original.Id)).Status);

        // Reusing the ORIGINAL's own identity: allowed — it stops being effective in the same
        // transaction that creates the correction.
        Assert.IsType<OkObjectResult>(
            await controller.Replace(seed.RequestId, original.Id, CorrectionDto(attachment2.Id)));
    }

    // ── Downstream evidence ──

    [Fact]
    public async Task Phase_3_evidence_on_the_original_blocks_replacement_outright()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 114_000m,
            Status = RequestConstants.PoGroupStatuses.WaitingPo,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        var original = AddValidatedInvoice(ctx, seed);
        ctx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
        {
            OperationInvoiceId = original.Id,
            RequestPoGroupId = group.Id,
            AllocatedGrossAmount = 114_000m,
            SequenceNumber = 1
        });
        var newAttachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var conflict = Assert.IsType<ConflictObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Replace(seed.RequestId, original.Id, CorrectionDto(newAttachment.Id)));
        Assert.Equal(OperationInvoicesController.DownstreamEvidenceCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);

        // Nothing was cascaded, transferred or half-written.
        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(Doc.Validated,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == original.Id)).Status);
        Assert.Equal(1, await ctx.OperationInvoiceAllocations.CountAsync());
    }

    // ── The chain ──

    [Fact]
    public async Task A_two_step_chain_stays_walkable()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoiceA = AddValidatedInvoice(ctx, seed);
        var attachmentB = AddInvoiceAttachment(ctx, seed);
        var attachmentC = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        var invoiceB = Body(await controller.Replace(
            seed.RequestId, invoiceA.Id, CorrectionDto(attachmentB.Id)));

        // B is later validated and itself needs correction.
        var trackedB = await ctx.OperationInvoices.SingleAsync(i => i.Id == invoiceB.Id);
        trackedB.Status = Doc.Validated;
        await ctx.SaveChangesAsync();

        var dtoC = CorrectionDto(attachmentC.Id);
        dtoC.ReplacementReason = "ZZTEST segunda correção";
        var invoiceC = Body(await controller.Replace(seed.RequestId, invoiceB.Id, dtoC));

        ctx.ChangeTracker.Clear();
        var a = await ctx.OperationInvoices.SingleAsync(i => i.Id == invoiceA.Id);
        var b = await ctx.OperationInvoices.SingleAsync(i => i.Id == invoiceB.Id);
        var c = await ctx.OperationInvoices.SingleAsync(i => i.Id == invoiceC.Id);

        Assert.Equal(b.Id, a.SupersededByOperationInvoiceId);   // A → B
        Assert.Equal(c.Id, b.SupersededByOperationInvoiceId);   // B → C
        Assert.Null(c.SupersededByOperationInvoiceId);          // C is the live end of the chain
        Assert.Equal(Doc.ReplacementRequested, a.Status);
        Assert.Equal(Doc.ReplacementRequested, b.Status);
        Assert.Equal(Doc.PendingValidation, c.Status);
    }

    // ── Concurrency and request-status gates ──

    [Fact]
    public async Task A_stale_original_token_refuses_the_replacement_atomically()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var original = AddValidatedInvoice(ctx, seed);
        var newAttachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var dto = CorrectionDto(newAttachment.Id);
        dto.RowVersion = new byte[] { 7, 7, 7 };   // stale by construction

        var conflict = Assert.IsType<ConflictObjectResult>(
            await BuildController(ctx, seed.ActorId).Replace(seed.RequestId, original.Id, dto));
        Assert.Equal(OperationInvoicesController.ConcurrencyCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(Doc.Validated,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == original.Id)).Status);
    }

    [Theory]
    [InlineData(Req.WaitingPoCorrection)]
    [InlineData(Req.Completed)]
    public async Task A_blocked_request_status_blocks_replacement_too(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: status);
        var original = AddValidatedInvoice(ctx, seed);
        var newAttachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        Assert.IsType<ConflictObjectResult>(await BuildController(ctx, seed.ActorId)
            .Replace(seed.RequestId, original.Id, CorrectionDto(newAttachment.Id)));

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.OperationInvoices.CountAsync());
    }
}
