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
/// Release 4 Phase 2b: POST/GET /api/v1/requests/{id}/operation-invoices.
///
/// <para>The properties pinned: Finance/Buyer register, Requester/Receiving read; the whole
/// post-approval window accepts an invoice INCLUDING after payment; the global fiscal-identity
/// duplicate rule; every refusal persists nothing; and — critical — an unallocated invoice
/// changes no Phase 1 obligation state whatsoever.</para>
/// </summary>
public class OperationInvoicesEndpointTests
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

    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx, string statusCode = Req.Paid, int plantId = 1)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Invoice Tester", Email = "oi@test.local" };
        ctx.Users.Add(actor);

        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 30, Code = statusCode, Name = statusCode, DisplayOrder = 30 });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "ZZTEST Supplier", TaxId = "123123123" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-OI-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST operation invoices",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = plantId,
            SubmittedAtUtc = DateTime.UtcNow.AddDays(-20),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-25)
        };
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static RequestAttachment AddInvoiceAttachment(
        ApplicationDbContext ctx, Seed seed,
        string typeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
        string? fileHash = null)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "fatura.pdf",
            FileExtension = ".pdf",
            FileHash = fileHash,
            AttachmentTypeCode = typeCode,
            StorageReference = "zztest/fatura-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        ctx.RequestAttachments.Add(attachment);
        return attachment;
    }

    private static SaveOperationInvoiceDto ValidDto(Guid attachmentId) => new()
    {
        AttachmentId = attachmentId,
        SupplierId = 10,
        DocumentNumber = "FT 7/2026",
        DocumentSeries = "A",
        DocumentDate = new DateTime(2026, 8, 1),
        DueDate = new DateTime(2026, 9, 1),
        Currency = "aoa",
        NetAmount = 100_000m,
        TaxAmount = 14_000m,
        GrossAmount = 114_000m,
        Notes = "ZZTEST regularização da proforma"
    };

    private static OperationInvoiceDto Created(IActionResult result) =>
        Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(result).Value);

    // ── Happy path: everything about the created row in one place ──

    [Fact]
    public async Task Finance_registers_an_invoice_that_lands_in_finances_queue()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var body = Created(await BuildController(ctx, seed.ActorId, RoleConstants.Finance)
            .Create(seed.RequestId, ValidDto(attachment.Id)));

        Assert.Equal(Doc.PendingValidation, body.Status);
        Assert.Equal("FT 7/2026", body.DocumentNumber);
        Assert.Equal("AOA", body.Currency);                        // normalized
        Assert.Equal(new DateTime(2026, 9, 1), body.DueDate);      // optional field round-trips
        Assert.Equal("ZZTEST regularização da proforma", body.Notes);
        Assert.Equal("123123123", body.SupplierTaxIdSnapshot);     // from the Supplier row
        Assert.True(body.AmountsEnteredManually);                  // manual creation IS typed numbers
        Assert.Equal("fatura.pdf", body.AttachmentFileName);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.OperationInvoices.SingleAsync();
        Assert.Equal(Doc.PendingValidation, persisted.Status);
        Assert.Equal(new DateTime(2026, 9, 1), persisted.DueDate);
        Assert.Equal(seed.ActorId, persisted.UploadedByUserId);
        Assert.Null(persisted.BilledCompanyNameRead);

        Assert.True(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "FATURA_OPERACAO_REGISTADA"));
    }

    [Fact]
    public async Task A_missing_due_date_is_accepted_the_field_is_optional()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var dto = ValidDto(attachment.Id);
        dto.DueDate = null;

        var body = Created(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, dto));

        Assert.Null(body.DueDate);
    }

    // ── Authorization ──

    [Fact]
    public async Task The_buyer_may_register_an_invoice_too()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId, RoleConstants.Buyer)
            .Create(seed.RequestId, ValidDto(attachment.Id));

        Assert.IsType<OkObjectResult>(result);
    }

    [Theory]
    [InlineData(RoleConstants.Requester)]
    [InlineData(RoleConstants.Receiving)]
    public async Task Requester_and_receiving_are_read_only(string role)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId, role)
            .Create(seed.RequestId, ValidDto(attachment.Id));

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbidden.StatusCode);

        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.OperationInvoices.CountAsync());
    }

    [Fact]
    public async Task An_out_of_scope_request_reads_exactly_like_a_nonexistent_one()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, plantId: 1);
        var attachment = AddInvoiceAttachment(ctx, seed);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = seed.ActorId, PlantId = 99 });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);

        Assert.IsType<NotFoundObjectResult>(
            await controller.Create(seed.RequestId, ValidDto(attachment.Id)));
        Assert.IsType<NotFoundObjectResult>(
            (await controller.List(seed.RequestId)).Result);
    }

    [Fact]
    public async Task An_in_scope_requester_can_read_what_they_cannot_create()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        Created(await BuildController(ctx, seed.ActorId, RoleConstants.Finance)
            .Create(seed.RequestId, ValidDto(attachment.Id)));

        var list = Assert.IsType<List<OperationInvoiceDto>>(Assert.IsType<OkObjectResult>(
            (await BuildController(ctx, seed.ActorId, RoleConstants.Requester)
                .List(seed.RequestId)).Result).Value);

        var invoice = Assert.Single(list);   // the unallocated invoice IS visible here
        var detail = Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(
            (await BuildController(ctx, seed.ActorId, RoleConstants.Requester)
                .Get(seed.RequestId, invoice.Id)).Result).Value);
        Assert.Equal(invoice.Id, detail.Id);
    }

    [Fact]
    public async Task Another_request_cannot_retrieve_someone_elses_invoice()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var created = Created(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ValidDto(attachment.Id)));

        // A second PAYMENT request of the same user: the invoice id is real, the request is wrong.
        var other = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-OI-OTHER",
            Title = "ZZTEST other",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = seed.ActorId,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(other);
        await ctx.SaveChangesAsync();

        Assert.IsType<NotFoundObjectResult>(
            (await BuildController(ctx, seed.ActorId).Get(other.Id, created.Id)).Result);
    }

    // ── Request type and status gates ──

    [Fact]
    public async Task A_quotation_request_takes_no_operation_invoice()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        var quotation = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-OI-Q",
            Title = "ZZTEST quotation",
            RequestTypeId = 1,
            StatusId = 30,
            RequesterId = seed.ActorId,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(quotation);
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = quotation.Id,
            FileName = "fatura.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/q.pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow
        };
        ctx.RequestAttachments.Add(attachment);
        await ctx.SaveChangesAsync();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId).Create(quotation.Id, ValidDto(attachment.Id)));
        var problem = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Contains("Pagamento", problem.Detail);
    }

    [Theory]
    [InlineData(Req.FinalApproved)]
    [InlineData(Req.PoIssued)]
    [InlineData(Req.Paid)]
    [InlineData(Req.WaitingReconciliation)]
    public async Task The_post_approval_window_accepts_an_invoice(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: status);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        Assert.IsType<OkObjectResult>(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ValidDto(attachment.Id)));
    }

    [Theory]
    [InlineData(Req.Draft)]
    [InlineData(Req.WaitingAreaApproval)]
    [InlineData(Req.AreaAdjustment)]
    [InlineData(Req.Rejected)]
    [InlineData(Req.Cancelled)]
    [InlineData(Req.Completed)]
    public async Task Outside_the_window_the_request_takes_nothing_and_nothing_persists(string status)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: status);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ValidDto(attachment.Id));

        Assert.IsType<ConflictObjectResult>(result);

        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(0, await ctx.RequestStatusHistories.CountAsync());
    }

    // ── Supplier integrity ──

    [Fact]
    public async Task An_internal_alpla_supplier_is_refused_with_the_existing_typed_error()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaPLASTICO", TaxId = "5417567485", Code = "APA" });
        ctx.Suppliers.Add(new Supplier { Id = 500, Name = "ZZTEST Internal Clone", TaxId = "5417567485" });
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var dto = ValidDto(attachment.Id);
        dto.SupplierId = 500;

        // SystemAdministrator on purpose: administrative permissions never bypass the
        // financial-integrity rule.
        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId, RoleConstants.SystemAdministrator)
                .Create(seed.RequestId, dto));
        var problem = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Equal(InternalCompanyPolicy.ViolationCode, problem.Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.OperationInvoices.CountAsync());
    }

    // ── Attachment gates ──

    [Fact]
    public async Task An_attachment_of_another_request_is_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var foreign = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),   // some other request
            FileName = "fatura.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/foreign.pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow
        };
        ctx.RequestAttachments.Add(foreign);
        await ctx.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ValidDto(foreign.Id)));
    }

    [Fact]
    public async Task An_attachment_of_the_wrong_type_is_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var wrongType = AddInvoiceAttachment(ctx, seed, typeCode: RequestAttachment.TYPE_PO);
        await ctx.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ValidDto(wrongType.Id)));
    }

    [Fact]
    public async Task Repeating_a_create_with_the_same_attachment_returns_the_existing_invoice()
    {
        // Phase 2d, the source-document Create precedent: one attachment is one invoice, so the
        // same attachment offered again IS the same create — a network retry gets the existing
        // row back, never an inexplicable conflict and never a twin.
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var first = Created(await controller.Create(seed.RequestId, ValidDto(attachment.Id)));

        var retry = ValidDto(attachment.Id);
        retry.DocumentNumber = "FT 8/2026";   // even a drifted body maps to the same invoice
        var second = Created(await controller.Create(seed.RequestId, retry));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("FT 7/2026", second.DocumentNumber);   // the persisted truth, not the retry body

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(1, await ctx.RequestStatusHistories
            .CountAsync(h => h.ActionTaken == "FATURA_OPERACAO_REGISTADA"));
    }

    [Fact]
    public async Task A_voided_attachment_is_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        attachment.VoidedAtUtc = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ValidDto(attachment.Id)));
    }

    // ── Duplicates: global fiscal identity ──

    [Fact]
    public async Task The_same_fiscal_identity_on_another_request_is_still_a_duplicate()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment1 = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        Created(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ValidDto(attachment1.Id)));

        var other = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-OI-2",
            Title = "ZZTEST second request",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = seed.ActorId,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(other);
        var attachment2 = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = other.Id,
            FileName = "fatura2.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/f2.pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow
        };
        ctx.RequestAttachments.Add(attachment2);
        await ctx.SaveChangesAsync();

        // A fiscal invoice must not be recognized as debt in two Portal requests.
        var conflict = Assert.IsType<ConflictObjectResult>(
            await BuildController(ctx, seed.ActorId).Create(other.Id, ValidDto(attachment2.Id)));
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(OperationInvoicesController.DuplicateErrorCode, problem.Extensions["code"]);
        Assert.Equal(seed.RequestId, problem.Extensions["existingRequestId"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.OperationInvoices.CountAsync());   // only the first exists
    }

    [Fact]
    public async Task A_different_series_is_a_different_invoice()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment1 = AddInvoiceAttachment(ctx, seed);
        var attachment2 = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Assert.IsType<OkObjectResult>(await controller.Create(seed.RequestId, ValidDto(attachment1.Id)));

        var dto = ValidDto(attachment2.Id);
        dto.DocumentSeries = "B";
        Assert.IsType<OkObjectResult>(await controller.Create(seed.RequestId, dto));
    }

    [Fact]
    public async Task A_voided_predecessor_frees_the_identity_for_a_reissue()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.OperationInvoices.Add(new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SupplierId = 10,
            DocumentNumber = "FT 7/2026",
            DocumentSeries = "A",
            Status = Doc.Voided,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            UploadedByUserId = seed.ActorId
        });
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        Assert.IsType<OkObjectResult>(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ValidDto(attachment.Id)));
    }

    [Fact]
    public async Task The_preflight_names_both_kinds_of_duplicate_without_enforcing_anything()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed, fileHash: "ZZHASH123");
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Created(await controller.Create(seed.RequestId, ValidDto(attachment.Id)));

        var result = Assert.IsType<OperationInvoiceDuplicateResultDto>(Assert.IsType<OkObjectResult>(
            (await controller.CheckDuplicate(seed.RequestId, new CheckOperationInvoiceDuplicateDto
            {
                ContentHash = "ZZHASH123",
                SupplierId = 10,
                DocumentNumber = "ft 7/2026",   // case-insensitive, trimmed
                DocumentSeries = "A"
            })).Result).Value);

        Assert.NotNull(result.SameFile);
        Assert.NotNull(result.SameBusinessDocument);
        Assert.Equal("FT 7/2026", result.SameBusinessDocument!.DocumentNumber);
    }

    // ── Amount integrity ──

    [Fact]
    public async Task A_non_positive_gross_is_refused_as_a_field_error()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var dto = ValidDto(attachment.Id);
        dto.GrossAmount = 0m;
        dto.NetAmount = null;
        dto.TaxAmount = null;

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId).Create(seed.RequestId, dto));
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        Assert.Contains("GrossAmount", problem.Errors.Keys);
    }

    [Fact]
    public async Task Net_and_tax_travel_together_or_not_at_all()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var dto = ValidDto(attachment.Id);
        dto.TaxAmount = null;   // net without tax

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId).Create(seed.RequestId, dto));
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        Assert.Contains("NetAmount", problem.Errors.Keys);
    }

    [Fact]
    public async Task A_gross_that_disagrees_with_net_plus_tax_is_refused_and_tolerance_is_honoured()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment1 = AddInvoiceAttachment(ctx, seed);
        var attachment2 = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        var wrong = ValidDto(attachment1.Id);
        wrong.GrossAmount = 120_000m;   // net+tax say 114,000 — far beyond tolerance
        var bad = Assert.IsType<BadRequestObjectResult>(await controller.Create(seed.RequestId, wrong));
        Assert.Contains("GrossAmount",
            Assert.IsType<ValidationProblemDetails>(bad.Value).Errors.Keys);

        var withinTolerance = ValidDto(attachment2.Id);
        withinTolerance.DocumentNumber = "FT 9/2026";
        withinTolerance.GrossAmount = 114_000m + 50m;   // tolerance = max(1, 0.1%) = 114.05
        Assert.IsType<OkObjectResult>(await controller.Create(seed.RequestId, withinTolerance));
    }

    [Fact]
    public async Task Gross_only_is_a_complete_valid_header()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var dto = ValidDto(attachment.Id);
        dto.NetAmount = null;
        dto.TaxAmount = null;

        var body = Created(await BuildController(ctx, seed.ActorId).Create(seed.RequestId, dto));
        Assert.Null(body.NetAmount);
        Assert.Equal(114_000m, body.GrossAmount);
    }

    [Fact]
    public async Task Missing_required_fields_are_reported_together_as_field_errors()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        await ctx.SaveChangesAsync();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await BuildController(ctx, seed.ActorId)
                .Create(seed.RequestId, new SaveOperationInvoiceDto()));
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);

        foreach (var key in new[]
                 { "AttachmentId", "SupplierId", "DocumentNumber", "DocumentDate", "Currency", "GrossAmount" })
        {
            Assert.Contains(key, problem.Errors.Keys);
        }

        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(0, await ctx.RequestStatusHistories.CountAsync());
    }

    // ── The Phase 1 non-interaction invariant ──

    [Fact]
    public async Task An_unallocated_invoice_changes_no_phase_1_obligation_state()
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
        var attachment = AddInvoiceAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        Created(await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ValidDto(attachment.Id)));

        // The PROFORMA group that was PENDING_UPLOAD is STILL PENDING_UPLOAD: an invoice that is
        // merely uploaded covers nothing until Phase 3 allocates it and Finance validates it.
        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(Agg.PendingUpload, persisted.OperationInvoiceStatus);
        Assert.Equal(114_000m, persisted.ExpectedOperationInvoiceTotal);
        Assert.True(persisted.RequiresOperationInvoice);

        var obligation = Assert.Single(OperationInvoiceObligationProjector.Project(new[]
        {
            new OperationInvoiceObligationGroupSnapshot
            {
                GroupId = persisted.Id,
                SourceDocumentType = persisted.SourceDocumentType,
                ExpectedTotal = persisted.ExpectedOperationInvoiceTotal,
                ExpectedCurrency = persisted.ExpectedOperationInvoiceCurrency,
                PersistedStatus = persisted.OperationInvoiceStatus
                // No allocations — exactly what the projection would receive from the database.
            }
        }).Obligations);

        Assert.Equal(Agg.PendingUpload, obligation.DerivedStatus);
        Assert.Equal(0m, obligation.ValidatedCoveredAmount);
        Assert.Equal(0m, obligation.PendingCoveredAmount);
        Assert.False(obligation.StatusDrift);
    }
}
