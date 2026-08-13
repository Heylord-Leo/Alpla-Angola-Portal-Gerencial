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
/// Release 4 Phase 2c: PUT …/{id} and POST …/{id}/void.
///
/// <para>The properties pinned: editing and voiding stop at validation; every Create gate re-runs
/// on Update; a void frees the fiscal identity while the record stays readable forever; and no
/// refusal — including a stale concurrency token — leaves partial state behind.</para>
/// </summary>
public class OperationInvoiceUpdateVoidTests
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
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            new AlplaPortal.Infrastructure.Services.OperationInvoiceCoverageService(ctx));

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
        var actor = new User { Id = Guid.NewGuid(), FullName = "UV Tester", Email = "uv@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 30, Code = statusCode, Name = statusCode, DisplayOrder = 30 });
        ctx.Suppliers.AddRange(
            new Supplier { Id = 10, Name = "ZZTEST Supplier A", TaxId = "111000111" },
            new Supplier { Id = 20, Name = "ZZTEST Supplier B", TaxId = "222000222" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-UV-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST update/void",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-25)
        };
        ctx.Requests.Add(request);

        // v2.228.1: registration is obligation-driven — seed the group every Create call needs.
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierId = 10,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 114_000m,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.PendingUpload,
            RequiresOperationInvoice = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedByUserId = actor.Id
        });

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static OperationInvoice AddInvoice(
        ApplicationDbContext ctx, Seed seed,
        string status = Doc.PendingValidation,
        int supplierId = 10,
        string number = "FT 7/2026",
        string? series = "A")
    {
        var invoice = new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SupplierId = supplierId,
            SupplierTaxIdSnapshot = "111000111",
            DocumentNumber = number,
            DocumentSeries = series,
            DocumentDate = new DateTime(2026, 8, 1),
            Currency = "AOA",
            GrossAmount = 114_000m,
            Status = status,
            AmountsEnteredManually = true,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            UploadedByUserId = seed.ActorId
        };
        ctx.OperationInvoices.Add(invoice);
        return invoice;
    }

    private static OperationInvoiceDto Body(IActionResult result) =>
        Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(result).Value);

    // ── Update: the happy path and its stamps ──

    [Fact]
    public async Task Finance_edits_a_pending_invoice_and_the_audit_names_what_moved()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).Update(seed.RequestId, invoice.Id,
            new SaveOperationInvoiceDto
            {
                DocumentNumber = "FT 7-B/2026",
                DueDate = new DateTime(2026, 10, 1),
                Notes = "ZZTEST corrigido o número"
            });

        var body = Body(result);
        Assert.Equal("FT 7-B/2026", body.DocumentNumber);
        Assert.Equal(new DateTime(2026, 10, 1), body.DueDate);
        Assert.Equal("ZZTEST corrigido o número", body.Notes);
        Assert.NotNull(body.UpdatedAtUtc);
        Assert.Equal(seed.ActorId, body.UpdatedByUserId);

        ctx.ChangeTracker.Clear();
        var history = await ctx.RequestStatusHistories
            .SingleAsync(h => h.ActionTaken == "FATURA_OPERACAO_ALTERADA");
        Assert.Contains("Número", history.Comment);
        Assert.Contains("Data de Vencimento", history.Comment);
    }

    [Fact]
    public async Task The_buyer_may_edit_an_editable_invoice_too()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        Body(await BuildController(ctx, seed.ActorId, RoleConstants.Buyer)
            .Update(seed.RequestId, invoice.Id, new SaveOperationInvoiceDto { Notes = "ZZTEST buyer" }));
    }

    [Theory]
    [InlineData(RoleConstants.Requester)]
    [InlineData(RoleConstants.Receiving)]
    public async Task Read_only_roles_cannot_edit(string role)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var forbidden = Assert.IsType<ObjectResult>(
            await BuildController(ctx, seed.ActorId, role)
                .Update(seed.RequestId, invoice.Id, new SaveOperationInvoiceDto { Notes = "x" }));
        Assert.Equal(403, forbidden.StatusCode);
    }

    [Fact]
    public async Task A_no_op_update_writes_no_history_and_no_stamp()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        Body(await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, invoice.Id, new SaveOperationInvoiceDto()));

        ctx.ChangeTracker.Clear();
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_ALTERADA"));
        Assert.Null((await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).UpdatedAtUtc);
    }

    // ── Update: the lifecycle wall ──

    [Theory]
    [InlineData(Doc.Validated)]
    [InlineData(Doc.Rejected)]
    [InlineData(Doc.Voided)]
    [InlineData(Doc.ReplacementRequested)]
    [InlineData(Doc.DivergenceDetected)]
    public async Task Nothing_past_pending_validation_can_be_edited(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, status: status);
        await ctx.SaveChangesAsync();

        var conflict = Assert.IsType<ConflictObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, invoice.Id, new SaveOperationInvoiceDto { Notes = "x" }));
        Assert.Equal(OperationInvoicesController.NotEditableCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Null((await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Notes);
    }

    [Theory]
    [InlineData(Req.WaitingPoCorrection)]
    [InlineData(Req.Completed)]
    [InlineData(Req.Draft)]
    public async Task A_blocked_request_status_blocks_update_and_void_alike(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: status);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        Assert.IsType<ConflictObjectResult>(await controller.Update(
            seed.RequestId, invoice.Id, new SaveOperationInvoiceDto { Notes = "x" }));
        Assert.IsType<ConflictObjectResult>(await controller.Void(
            seed.RequestId, invoice.Id, new VoidOperationInvoiceDto { Reason = "ZZTEST motivo" }));

        // Read stays available regardless of the mutation window.
        Assert.IsType<OkObjectResult>((await controller.Get(seed.RequestId, invoice.Id)).Result);
    }

    // ── Update: every Create gate re-runs ──

    [Fact]
    public async Task An_edit_to_an_internal_alpla_supplier_is_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaPLASTICO", TaxId = "5417567485", Code = "APA" });
        ctx.Suppliers.Add(new Supplier { Id = 500, Name = "ZZTEST Internal Clone", TaxId = "5417567485" });
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, invoice.Id, new SaveOperationInvoiceDto { SupplierId = 500 }));
        Assert.Equal(InternalCompanyPolicy.ViolationCode,
            Assert.IsType<ProblemDetails>(bad.Value).Extensions["code"]);
    }

    [Fact]
    public async Task An_edit_that_collides_with_another_effective_invoice_is_a_duplicate()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        AddInvoice(ctx, seed, number: "FT 100", series: "A");
        var second = AddInvoice(ctx, seed, number: "FT 200", series: "A");
        await ctx.SaveChangesAsync();

        var conflict = Assert.IsType<ConflictObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, second.Id, new SaveOperationInvoiceDto { DocumentNumber = "ft 100" }));
        Assert.Equal(OperationInvoicesController.DuplicateErrorCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal("FT 200",
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == second.Id)).DocumentNumber);
    }

    [Fact]
    public async Task An_edit_keeping_its_own_identity_is_not_its_own_duplicate()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        // Same number, only notes change — the self-exclusion makes this legal.
        Body(await BuildController(ctx, seed.ActorId).Update(seed.RequestId, invoice.Id,
            new SaveOperationInvoiceDto { DocumentNumber = "FT 7/2026", Notes = "ZZTEST" }));
    }

    [Fact]
    public async Task An_edit_that_breaks_amount_integrity_is_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        invoice.NetAmount = 100_000m;
        invoice.TaxAmount = 14_000m;
        await ctx.SaveChangesAsync();

        // Gross moves; net+tax stay — the merged state disagrees far beyond tolerance.
        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, invoice.Id, new SaveOperationInvoiceDto { GrossAmount = 999_999m }));
        Assert.Contains("GrossAmount",
            Assert.IsType<ValidationProblemDetails>(bad.Value).Errors.Keys);
    }

    [Fact]
    public async Task Replacing_the_attachment_moves_the_pointer_and_keeps_the_old_file()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var oldAttachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "fatura-original.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/original.pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        var replacement = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "fatura-corrigida.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/corrigida.pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow
        };
        ctx.RequestAttachments.AddRange(oldAttachment, replacement);
        var invoice = AddInvoice(ctx, seed);
        invoice.AttachmentId = oldAttachment.Id;
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId).Update(seed.RequestId, invoice.Id,
            new SaveOperationInvoiceDto { AttachmentId = replacement.Id }));

        Assert.Equal(replacement.Id, body.AttachmentId);
        Assert.Equal("fatura-corrigida.pdf", body.AttachmentFileName);

        ctx.ChangeTracker.Clear();
        // The old file row is untouched — historically accessible, never deleted or voided.
        var oldRow = await ctx.RequestAttachments.SingleAsync(a => a.Id == oldAttachment.Id);
        Assert.False(oldRow.IsDeleted);
        Assert.Null(oldRow.VoidedAtUtc);

        var history = await ctx.RequestStatusHistories
            .SingleAsync(h => h.ActionTaken == "FATURA_OPERACAO_ALTERADA");
        Assert.Contains("Anexo", history.Comment);
    }

    [Fact]
    public async Task A_stale_concurrency_token_is_a_typed_conflict_and_persists_nothing()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).Update(seed.RequestId, invoice.Id,
            new SaveOperationInvoiceDto
            {
                Notes = "ZZTEST não deve persistir",
                RowVersion = new byte[] { 1, 2, 3 }   // stale by construction
            });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(OperationInvoicesController.ConcurrencyCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Null((await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Notes);
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_ALTERADA"));
    }

    // ── Void ──

    [Fact]
    public async Task A_pending_invoice_voids_with_a_reason_and_stays_readable()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        var attachmentId = invoice.AttachmentId;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var body = Body(await controller.Void(seed.RequestId, invoice.Id,
            new VoidOperationInvoiceDto { Reason = "ZZTEST registada em duplicado" }));

        Assert.Equal(Doc.Voided, body.Status);
        Assert.Equal("ZZTEST registada em duplicado", body.VoidReason);
        Assert.NotNull(body.VoidedAtUtc);
        Assert.Equal(attachmentId, body.AttachmentId);   // the attachment stays

        // Terminal, but never hidden: list and detail keep showing it.
        var list = Assert.IsType<List<OperationInvoiceDto>>(Assert.IsType<OkObjectResult>(
            (await controller.List(seed.RequestId)).Result).Value);
        Assert.Equal(Doc.Voided, Assert.Single(list).Status);

        ctx.ChangeTracker.Clear();
        Assert.True(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_ANULADA"));
    }

    [Fact]
    public async Task A_void_without_a_reason_is_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Void(seed.RequestId, invoice.Id, new VoidOperationInvoiceDto { Reason = "   " }));
        Assert.Contains("Reason",
            Assert.IsType<ValidationProblemDetails>(bad.Value).Errors.Keys);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.PendingValidation,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Status);
    }

    [Theory]
    [InlineData(Doc.Validated)]
    [InlineData(Doc.Rejected)]
    [InlineData(Doc.ReplacementRequested)]
    // An already-VOIDED invoice is NOT here: re-voiding it is an idempotent retry (Phase 2d)
    // and returns the voided row — covered in OperationInvoiceDuplicateHardeningTests.
    public async Task Validated_and_terminal_invoices_cannot_be_voided(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, status: status);
        await ctx.SaveChangesAsync();

        var conflict = Assert.IsType<ConflictObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Void(seed.RequestId, invoice.Id, new VoidOperationInvoiceDto { Reason = "ZZTEST" }));
        Assert.Equal(OperationInvoicesController.NotVoidableCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
    }

    [Theory]
    [InlineData(RoleConstants.Requester)]
    [InlineData(RoleConstants.Receiving)]
    public async Task Read_only_roles_cannot_void(string role)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var forbidden = Assert.IsType<ObjectResult>(
            await BuildController(ctx, seed.ActorId, role)
                .Void(seed.RequestId, invoice.Id, new VoidOperationInvoiceDto { Reason = "ZZTEST" }));
        Assert.Equal(403, forbidden.StatusCode);
    }

    [Fact]
    public async Task A_void_frees_the_fiscal_identity_for_a_reissue()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        var reissueAttachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "fatura-reemitida.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/reemitida.pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow
        };
        ctx.RequestAttachments.Add(reissueAttachment);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Body(await controller.Void(seed.RequestId, invoice.Id,
            new VoidOperationInvoiceDto { Reason = "ZZTEST valores errados" }));

        // Same supplier + number + series as the voided one: now legal.
        Assert.IsType<OkObjectResult>(await controller.Create(seed.RequestId,
            new SaveOperationInvoiceDto
            {
                AttachmentId = reissueAttachment.Id,
                SupplierId = 10,
                DocumentNumber = "FT 7/2026",
                DocumentSeries = "A",
                DocumentDate = new DateTime(2026, 8, 2),
                Currency = "AOA",
                GrossAmount = 114_000m
            }));

        ctx.ChangeTracker.Clear();
        Assert.Equal(2, await ctx.OperationInvoices.CountAsync());   // both rows, forever
    }

    [Fact]
    public async Task Voiding_an_unallocated_invoice_changes_no_phase_1_state()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = 10,
            SupplierNameSnapshot = "ZZTEST Supplier A",
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
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        Body(await BuildController(ctx, seed.ActorId).Void(seed.RequestId, invoice.Id,
            new VoidOperationInvoiceDto { Reason = "ZZTEST anulada" }));

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(Agg.PendingUpload, persisted.OperationInvoiceStatus);
        Assert.Equal(114_000m, persisted.ExpectedOperationInvoiceTotal);

        var obligation = Assert.Single(OperationInvoiceObligationProjector.Project(new[]
        {
            new OperationInvoiceObligationGroupSnapshot
            {
                GroupId = persisted.Id,
                SourceDocumentType = persisted.SourceDocumentType,
                ExpectedTotal = persisted.ExpectedOperationInvoiceTotal,
                ExpectedCurrency = persisted.ExpectedOperationInvoiceCurrency,
                PersistedStatus = persisted.OperationInvoiceStatus
            }
        }).Obligations);
        Assert.Equal(Agg.PendingUpload, obligation.DerivedStatus);
        Assert.False(obligation.StatusDrift);
    }

    [Fact]
    public async Task A_stale_token_on_void_is_a_typed_conflict_and_the_invoice_stays_effective()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var conflict = Assert.IsType<ConflictObjectResult>(
            await BuildController(ctx, seed.ActorId).Void(seed.RequestId, invoice.Id,
                new VoidOperationInvoiceDto { Reason = "ZZTEST", RowVersion = new byte[] { 9 } }));
        Assert.Equal(OperationInvoicesController.ConcurrencyCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.PendingValidation,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Status);
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_ANULADA"));
    }
}
