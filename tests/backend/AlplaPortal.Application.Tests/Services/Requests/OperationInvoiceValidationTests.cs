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
/// Release 4 Phase 2e: POST …/{id}/validate and …/{id}/reject — the Finance decision.
///
/// <para>Pinned: Finance-only; PENDING_VALIDATION is the only decidable state; every integrity
/// boundary re-runs against the persisted row at validation (the last gate before the document
/// can ever carry financial weight); rejection is terminal and releases both duplicate
/// identities; and a VALIDATED unallocated invoice still covers NOTHING — validation makes the
/// document trustworthy, Phase 3 allocation makes it count.</para>
/// </summary>
public class OperationInvoiceValidationTests
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
        var actor = new User { Id = Guid.NewGuid(), FullName = "Validation Tester", Email = "val@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 30, Code = statusCode, Name = statusCode, DisplayOrder = 30 });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "ZZTEST Supplier", TaxId = "111000111" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-VAL-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST validation",
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
            StorageReference = "zztest/v-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = actorId,
            UploadedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        ctx.RequestAttachments.Add(attachment);
        return attachment;
    }

    /// <summary>A complete pending invoice WITH its attachment row, ready for the decision.</summary>
    private static OperationInvoice AddPendingInvoice(
        ApplicationDbContext ctx, Seed seed,
        string number = "FT 100", string? series = "A",
        string? fileHash = null, int supplierId = 10)
    {
        var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash);
        var invoice = new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = attachment.Id,
            SupplierId = supplierId,
            SupplierTaxIdSnapshot = "111000111",
            DocumentNumber = number,
            DocumentSeries = series,
            DocumentDate = new DateTime(2026, 8, 1),
            Currency = "AOA",
            NetAmount = 100_000m,
            TaxAmount = 14_000m,
            GrossAmount = 114_000m,
            Status = Doc.PendingValidation,
            AmountsEnteredManually = true,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            UploadedByUserId = seed.ActorId
        };
        ctx.OperationInvoices.Add(invoice);
        return invoice;
    }

    private static OperationInvoiceDto Body(IActionResult result) =>
        Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(result).Value);

    private static void AssertCode(IActionResult result, string expectedCode)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(expectedCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
    }

    /// <summary>
    /// Phase 3A: validation now requires a fully-attributed document. An eligible group with the
    /// invoice's full gross allocated to it is the minimal shape that passes the allocation gate.
    /// </summary>
    private static RequestPoGroup AddFullyAllocatedGroup(
        ApplicationDbContext ctx, Seed seed, OperationInvoice invoice, decimal? expectedOverride = null)
    {
        var gross = invoice.GrossAmount!.Value;
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = invoice.SupplierId,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = invoice.Currency,
            TotalAmount = expectedOverride ?? gross,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = Types.Proforma,
            OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true,
            ExpectedOperationInvoiceTotal = expectedOverride ?? gross,
            ExpectedOperationInvoiceCurrency = invoice.Currency,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        ctx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
        {
            Id = Guid.NewGuid(),
            OperationInvoiceId = invoice.Id,
            RequestPoGroupId = group.Id,
            AllocatedNetAmount = invoice.NetAmount ?? 0m,
            AllocatedTaxAmount = invoice.TaxAmount ?? 0m,
            AllocatedGrossAmount = gross,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
            CreatedByUserId = seed.ActorId
        });
        return group;
    }

    // ── Validate: happy path, stamps, retry ──

    [Fact]
    public async Task Finance_validates_a_pending_invoice_and_a_retry_is_idempotent()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        AddFullyAllocatedGroup(ctx, seed, invoice);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var body = Body(await controller.Validate(
            seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));

        Assert.Equal(Doc.Validated, body.Status);
        Assert.NotNull(body.ValidatedAtUtc);
        Assert.Equal(seed.ActorId, body.ValidatedByUserId);
        Assert.Null(body.DueDate);   // optional by approved decision — never blocks

        // Untouched by the decision: what Finance saw is what stays.
        Assert.Equal(114_000m, body.GrossAmount);
        Assert.Equal(invoice.AttachmentId, body.AttachmentId);

        // Exact retry: the same decision arriving twice, one history row.
        var retry = Body(await controller.Validate(
            seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));
        Assert.Equal(Doc.Validated, retry.Status);

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.RequestStatusHistories
            .CountAsync(h => h.ActionTaken == "FATURA_OPERACAO_VALIDADA"));
    }

    // ── Validate: who ──

    [Theory]
    [InlineData(RoleConstants.Buyer)]
    [InlineData(RoleConstants.Requester)]
    [InlineData(RoleConstants.Receiving)]
    public async Task Only_finance_decides(string role)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId, role);

        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Validate(
            seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto())).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Reject(
            seed.RequestId, invoice.Id, new RejectOperationInvoiceDto { Reason = "ZZTEST" })).StatusCode);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.PendingValidation,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Status);
    }

    [Fact]
    public async Task An_out_of_scope_request_hides_its_invoices_from_the_decision_routes_too()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = seed.ActorId, PlantId = 99 });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Assert.IsType<NotFoundObjectResult>(await controller.Validate(
            seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));
    }

    [Fact]
    public async Task Even_the_administrator_cannot_validate_an_internal_alpla_supplier()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaPLASTICO", TaxId = "5417567485", Code = "APA" });
        ctx.Suppliers.Add(new Supplier { Id = 500, Name = "ZZTEST Internal Clone", TaxId = "5417567485" });
        var invoice = AddPendingInvoice(ctx, seed, supplierId: 500);
        await ctx.SaveChangesAsync();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId, RoleConstants.SystemAdministrator)
                .Validate(seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));
        Assert.Equal(InternalCompanyPolicy.ViolationCode,
            Assert.IsType<ProblemDetails>(bad.Value).Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.PendingValidation,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Status);
    }

    // ── Validate: when ──

    [Theory]
    [InlineData(Req.WaitingPoCorrection)]
    [InlineData(Req.Completed)]
    [InlineData(Req.Draft)]
    public async Task A_blocked_request_status_blocks_both_decisions(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: status);
        var invoice = AddPendingInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Assert.IsType<ConflictObjectResult>(await controller.Validate(
            seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));
        Assert.IsType<ConflictObjectResult>(await controller.Reject(
            seed.RequestId, invoice.Id, new RejectOperationInvoiceDto { Reason = "ZZTEST" }));
    }

    [Theory]
    [InlineData(Doc.Uploaded)]              // future OCR intake never skips the queue
    [InlineData(Doc.Rejected)]              // a conflicting decision, never idempotent success
    [InlineData(Doc.Voided)]
    [InlineData(Doc.ReplacementRequested)]
    [InlineData(Doc.DivergenceDetected)]
    public async Task Only_the_pending_queue_can_be_validated(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        invoice.Status = status;
        await ctx.SaveChangesAsync();

        AssertCode(await BuildController(ctx, seed.ActorId).Validate(
                seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()),
            OperationInvoicesController.NotValidatableCode);
    }

    // ── Validate: the final integrity boundary ──

    [Fact]
    public async Task Broken_amounts_on_the_persisted_row_block_validation()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        invoice.NetAmount = 1m;   // net+tax no longer reconcile with gross
        await ctx.SaveChangesAsync();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Validate(seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));
        Assert.Contains("GrossAmount",
            Assert.IsType<ValidationProblemDetails>(bad.Value).Errors.Keys);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.PendingValidation,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Status);
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_VALIDADA"));
    }

    [Fact]
    public async Task A_missing_document_date_on_the_persisted_row_blocks_validation()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        invoice.DocumentDate = null;
        await ctx.SaveChangesAsync();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Validate(seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));
        Assert.Contains("DocumentDate",
            Assert.IsType<ValidationProblemDetails>(bad.Value).Errors.Keys);
    }

    [Fact]
    public async Task A_voided_attachment_blocks_validation()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var attachment = await ctx.RequestAttachments.SingleAsync(a => a.Id == invoice.AttachmentId);
        attachment.VoidedAtUtc = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await BuildController(ctx, seed.ActorId)
            .Validate(seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));
    }

    [Fact]
    public async Task A_business_duplicate_that_appeared_after_create_blocks_validation()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var pending = AddPendingInvoice(ctx, seed, number: "FT 100");
        // Another effective invoice claimed the identity between create and the decision
        // (seeded directly — the API itself would have refused it).
        ctx.OperationInvoices.Add(new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SupplierId = 10,
            DocumentNumber = "FT 100",
            DocumentSeries = "A",
            Status = Doc.Validated,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = seed.ActorId
        });
        await ctx.SaveChangesAsync();

        AssertCode(await BuildController(ctx, seed.ActorId).Validate(
                seed.RequestId, pending.Id, new ValidateOperationInvoiceDto()),
            OperationInvoicesController.DuplicateErrorCode);

        // Validation must never mint a second effective VALIDATED invoice.
        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.PendingValidation,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == pending.Id)).Status);
    }

    [Fact]
    public async Task A_file_duplicate_that_appeared_after_create_blocks_validation()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var pending = AddPendingInvoice(ctx, seed, number: "FT 100", fileHash: "ZZHASH-V");
        var competitorFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-V");
        ctx.OperationInvoices.Add(new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = competitorFile.Id,
            SupplierId = 10,
            DocumentNumber = "FT 999",
            Status = Doc.Validated,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = seed.ActorId
        });
        await ctx.SaveChangesAsync();

        AssertCode(await BuildController(ctx, seed.ActorId).Validate(
                seed.RequestId, pending.Id, new ValidateOperationInvoiceDto()),
            OperationInvoicesController.FileDuplicateErrorCode);
    }

    [Fact]
    public async Task A_stale_token_refuses_the_validation_atomically()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        AssertCode(await BuildController(ctx, seed.ActorId).Validate(
                seed.RequestId, invoice.Id,
                new ValidateOperationInvoiceDto { RowVersion = new byte[] { 5 } }),
            OperationInvoicesController.ConcurrencyCode);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.PendingValidation,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Status);
    }

    // ── Reject ──

    [Fact]
    public async Task Finance_rejects_with_a_reason_and_a_retry_returns_the_persisted_decision()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var body = Body(await controller.Reject(seed.RequestId, invoice.Id,
            new RejectOperationInvoiceDto { Reason = "ZZTEST dados não conferem com o documento" }));

        Assert.Equal(Doc.Rejected, body.Status);
        Assert.Equal("ZZTEST dados não conferem com o documento", body.RejectionReason);
        Assert.Null(body.ValidatedAtUtc);    // never validated
        Assert.Null(body.VoidedAtUtc);       // rejection is not a void

        // Retry with a DIFFERENT reason: the persisted decision comes back unrewritten.
        var retry = Body(await controller.Reject(seed.RequestId, invoice.Id,
            new RejectOperationInvoiceDto { Reason = "ZZTEST outro motivo" }));
        Assert.Equal("ZZTEST dados não conferem com o documento", retry.RejectionReason);

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.RequestStatusHistories
            .CountAsync(h => h.ActionTaken == "FATURA_OPERACAO_REJEITADA"));

        // Terminal but never hidden.
        var list = Assert.IsType<List<OperationInvoiceDto>>(Assert.IsType<OkObjectResult>(
            (await controller.List(seed.RequestId)).Result).Value);
        Assert.Equal(Doc.Rejected, Assert.Single(list).Status);
    }

    [Fact]
    public async Task A_rejection_without_a_reason_is_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Reject(seed.RequestId, invoice.Id, new RejectOperationInvoiceDto { Reason = " " }));
        Assert.Contains("Reason",
            Assert.IsType<ValidationProblemDetails>(bad.Value).Errors.Keys);
    }

    [Theory]
    [InlineData(Doc.Validated)]   // conflicting decision: correction is replacement, never reject
    [InlineData(Doc.Voided)]
    [InlineData(Doc.ReplacementRequested)]
    public async Task Nothing_outside_the_pending_queue_can_be_rejected(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        invoice.Status = status;
        await ctx.SaveChangesAsync();

        AssertCode(await BuildController(ctx, seed.ActorId).Reject(
                seed.RequestId, invoice.Id, new RejectOperationInvoiceDto { Reason = "ZZTEST" }),
            OperationInvoicesController.NotRejectableCode);
    }

    [Fact]
    public async Task A_rejection_releases_both_identities_for_the_corrected_resubmission()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed, number: "FT 100", fileHash: "ZZHASH-R");
        var resubmitFile = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "ZZHASH-R");
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Body(await controller.Reject(seed.RequestId, invoice.Id,
            new RejectOperationInvoiceDto { Reason = "ZZTEST metadados errados" }));

        // Same fiscal identity AND the same physical file — approved: rejection may concern the
        // metadata, not the document itself.
        Assert.IsType<OkObjectResult>(await controller.Create(seed.RequestId,
            new SaveOperationInvoiceDto
            {
                AttachmentId = resubmitFile.Id,
                SupplierId = 10,
                DocumentNumber = "FT 100",
                DocumentSeries = "A",
                DocumentDate = new DateTime(2026, 8, 3),
                Currency = "AOA",
                GrossAmount = 114_000m
            }));

        ctx.ChangeTracker.Clear();
        Assert.Equal(2, await ctx.OperationInvoices.CountAsync());
    }

    [Fact]
    public async Task A_stale_token_refuses_the_rejection_atomically()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddPendingInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        AssertCode(await BuildController(ctx, seed.ActorId).Reject(
                seed.RequestId, invoice.Id,
                new RejectOperationInvoiceDto { Reason = "ZZTEST", RowVersion = new byte[] { 6 } }),
            OperationInvoicesController.ConcurrencyCode);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.PendingValidation,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Status);
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_REJEITADA"));
    }

    // ── Phase 3A allocation gate: an unallocated invoice can no longer be validated at all ──

    [Fact]
    public async Task Validation_refuses_an_unallocated_invoice_and_nothing_moves()
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
            TotalAmount = 100_000m,
            Status = RequestConstants.PoGroupStatuses.WaitingPo,
            SourceDocumentType = Types.Proforma,
            OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true,
            ExpectedOperationInvoiceTotal = 100_000m,
            ExpectedOperationInvoiceCurrency = "AOA",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        var invoice = AddPendingInvoice(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).Validate(
            seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto());
        AssertCode(result, OperationInvoicesController.ValidateAllocationIncompleteCode);

        // Nothing moved: the document still awaits its decision, the group still awaits upload,
        // and no reconciliation snapshot was minted for a refused validation.
        ctx.ChangeTracker.Clear();
        Assert.Equal(Doc.PendingValidation,
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Status);
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(Agg.PendingUpload, persisted.OperationInvoiceStatus);
        Assert.Equal(100_000m, persisted.ExpectedOperationInvoiceTotal);
        Assert.False(await ctx.OperationInvoiceReconciliations.AnyAsync());
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_VALIDADA"));
    }

    // ── Replacement lifecycle through the decision routes ──

    [Fact]
    public async Task A_replacement_passes_the_same_validation_and_the_predecessor_never_moves()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoiceA = AddPendingInvoice(ctx, seed, number: "FT 100");
        invoiceA.Status = Doc.Validated;
        var newFile = AddAttachment(ctx, seed.RequestId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var invoiceB = Body(await controller.Replace(seed.RequestId, invoiceA.Id,
            new ReplaceOperationInvoiceDto
            {
                AttachmentId = newFile.Id,
                SupplierId = 10,
                DocumentNumber = "FT 100",
                DocumentSeries = "A",
                DocumentDate = new DateTime(2026, 8, 5),
                Currency = "AOA",
                GrossAmount = 120_000m,
                ReplacementReason = "ZZTEST correção"
            }));
        Assert.Equal(Doc.PendingValidation, invoiceB.Status);

        // Phase 3A: the replacement passes the same allocation gate as any other validation.
        ctx.ChangeTracker.Clear();
        var invoiceBEntity = await ctx.OperationInvoices.SingleAsync(i => i.Id == invoiceB.Id);
        AddFullyAllocatedGroup(ctx, seed, invoiceBEntity);
        await ctx.SaveChangesAsync();

        Body(await controller.Validate(seed.RequestId, invoiceB.Id, new ValidateOperationInvoiceDto()));

        ctx.ChangeTracker.Clear();
        var a = await ctx.OperationInvoices.SingleAsync(i => i.Id == invoiceA.Id);
        var b = await ctx.OperationInvoices.SingleAsync(i => i.Id == invoiceB.Id);
        Assert.Equal(Doc.ReplacementRequested, a.Status);   // untouched by B's validation
        Assert.Equal(Doc.Validated, b.Status);
        Assert.Equal(b.Id, a.SupersededByOperationInvoiceId);
    }

    [Fact]
    public async Task A_rejected_replacement_never_resurrects_its_predecessor_and_recovery_is_a_new_create()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoiceA = AddPendingInvoice(ctx, seed, number: "FT 100");
        invoiceA.Status = Doc.Validated;
        var fileB = AddAttachment(ctx, seed.RequestId, seed.ActorId);
        var fileC = AddAttachment(ctx, seed.RequestId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var invoiceB = Body(await controller.Replace(seed.RequestId, invoiceA.Id,
            new ReplaceOperationInvoiceDto
            {
                AttachmentId = fileB.Id,
                SupplierId = 10,
                DocumentNumber = "FT 100",
                DocumentSeries = "A",
                DocumentDate = new DateTime(2026, 8, 5),
                Currency = "AOA",
                GrossAmount = 120_000m,
                ReplacementReason = "ZZTEST correção"
            }));

        Body(await controller.Reject(seed.RequestId, invoiceB.Id,
            new RejectOperationInvoiceDto { Reason = "ZZTEST correção também errada" }));

        ctx.ChangeTracker.Clear();
        var a = await ctx.OperationInvoices.SingleAsync(i => i.Id == invoiceA.Id);
        Assert.Equal(Doc.ReplacementRequested, a.Status);   // the recorded decision is never rewritten

        // B is terminal — the replacement route refuses to operate from it…
        AssertCode(await controller.Replace(seed.RequestId, invoiceB.Id,
                new ReplaceOperationInvoiceDto
                {
                    AttachmentId = fileC.Id,
                    SupplierId = 10,
                    DocumentNumber = "FT 100",
                    DocumentSeries = "A",
                    DocumentDate = new DateTime(2026, 8, 6),
                    Currency = "AOA",
                    GrossAmount = 121_000m,
                    ReplacementReason = "ZZTEST terceira via"
                }),
            OperationInvoicesController.NotReplaceableCode);

        // …and the legal recovery path is a plain Create: both A and B are non-effective, so the
        // fiscal identity is free. C starts its own life in the queue, unlinked to the dead chain.
        var invoiceC = Body(await controller.Create(seed.RequestId, new SaveOperationInvoiceDto
        {
            AttachmentId = fileC.Id,
            SupplierId = 10,
            DocumentNumber = "FT 100",
            DocumentSeries = "A",
            DocumentDate = new DateTime(2026, 8, 6),
            Currency = "AOA",
            GrossAmount = 121_000m
        }));
        Assert.Equal(Doc.PendingValidation, invoiceC.Status);

        ctx.ChangeTracker.Clear();
        Assert.Equal(3, await ctx.OperationInvoices.CountAsync());
    }
}
