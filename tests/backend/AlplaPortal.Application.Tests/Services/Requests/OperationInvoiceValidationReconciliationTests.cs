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
using Agg = RequestConstants.OperationInvoiceStatuses;
using Req = RequestConstants.Statuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// Release 4 Phase 3A: validation as the moment coverage becomes EFFECTIVE.
///
/// <para>Pinned: the allocation-completeness gate; cumulative coverage moving a group
/// PARTIALLY_INVOICED → SATISFIED across two invoices; the explicit divergence decision for
/// over-expected groups (never inferred, never capped); the immutable reconciliation snapshot —
/// one per allocation, duplicated by no retry; and rejection re-deriving the group back in the
/// same transaction.</para>
/// </summary>
public class OperationInvoiceValidationReconciliationTests
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

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Reconciliation Tester", Email = "recon@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 30, Code = Req.Paid, Name = Req.Paid, DisplayOrder = 30 });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "ZZTEST Supplier", TaxId = "111000111" });
        ctx.Companies.Add(new Company { Id = 1, Name = "ALPLA ZZTEST" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-RECON-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST reconciliation",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-20)
        };
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static OperationInvoice AddInvoice(
        ApplicationDbContext ctx, Seed seed, decimal gross, string number,
        string status = Doc.PendingValidation, string? taxId = "111000111",
        string? billedCompany = null)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "fatura.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/r-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow.AddHours(-3)
        };
        ctx.RequestAttachments.Add(attachment);

        var invoice = new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = attachment.Id,
            SupplierId = 10,
            SupplierTaxIdSnapshot = taxId,
            BilledCompanyNameRead = billedCompany,
            DocumentNumber = number,
            DocumentSeries = "A",
            DocumentDate = new DateTime(2026, 8, 1),
            Currency = "AOA",
            GrossAmount = gross,
            Status = status,
            AmountsEnteredManually = true,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            UploadedByUserId = seed.ActorId
        };
        ctx.OperationInvoices.Add(invoice);
        return invoice;
    }

    private static RequestPoGroup AddGroup(
        ApplicationDbContext ctx, Seed seed, decimal? expected,
        string? nif = "111000111")
    {
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = 10,
            SupplierNameSnapshot = "ZZTEST Supplier",
            SupplierNifSnapshot = nif,
            CurrencyCode = "AOA",
            TotalAmount = expected ?? 0m,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = Types.Proforma,
            OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true,
            ExpectedOperationInvoiceTotal = expected,
            ExpectedOperationInvoiceCurrency = "AOA",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        return group;
    }

    private static void Allocate(
        ApplicationDbContext ctx, OperationInvoice invoice, RequestPoGroup group,
        decimal gross, Guid actorId, int sequence = 1)
    {
        ctx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
        {
            OperationInvoiceId = invoice.Id,
            RequestPoGroupId = group.Id,
            AllocatedGrossAmount = gross,
            SequenceNumber = sequence,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
            CreatedByUserId = actorId
        });
    }

    private static OperationInvoiceDto Body(IActionResult result) =>
        Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(result).Value);

    private static void AssertCode(IActionResult result, string expectedCode)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(expectedCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
    }

    // ── The allocation-completeness gate ──

    [Fact]
    public async Task A_partially_allocated_invoice_cannot_be_validated()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, expected: 1_000_000m);
        var invoice = AddInvoice(ctx, seed, gross: 1_000_000m, number: "FT 400");
        await ctx.SaveChangesAsync();
        Allocate(ctx, invoice, group, 600_000m, seed.ActorId);   // 400k unexplained
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).Validate(
            seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto());

        AssertCode(result, OperationInvoicesController.ValidateAllocationIncompleteCode);
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(result).Value);
        Assert.Equal(1_000_000m, problem.Extensions["invoiceGross"]);
        Assert.Equal(600_000m, problem.Extensions["allocatedTotal"]);
    }

    // ── Cumulative coverage across two invoices ──

    [Fact]
    public async Task Coverage_accumulates_partially_invoiced_then_satisfied()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, expected: 1_000_000m);
        var first = AddInvoice(ctx, seed, gross: 600_000m, number: "FT 401");
        var second = AddInvoice(ctx, seed, gross: 400_000m, number: "FT 402");
        await ctx.SaveChangesAsync();
        Allocate(ctx, first, group, 600_000m, seed.ActorId, sequence: 1);
        Allocate(ctx, second, group, 400_000m, seed.ActorId, sequence: 2);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        Body(await controller.Validate(seed.RequestId, first.Id, new ValidateOperationInvoiceDto()));
        ctx.ChangeTracker.Clear();
        // 600k of 1M validated — but the second invoice's draft keeps the group in Finance's court.
        Assert.Equal(Agg.PendingValidation,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);

        Body(await controller.Validate(seed.RequestId, second.Id, new ValidateOperationInvoiceDto()));
        ctx.ChangeTracker.Clear();
        Assert.Equal(Agg.Satisfied,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);

        // Two invoices, one allocation each: two immutable snapshots, cumulative-before recorded.
        var snapshots = await ctx.OperationInvoiceReconciliations
            .OrderBy(r => r.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(0m, snapshots[0].CumulativeValidatedTotalBefore);
        Assert.Equal(600_000m, snapshots[1].CumulativeValidatedTotalBefore);
        Assert.All(snapshots, s => Assert.False(s.DivergenceDetected));
    }

    [Fact]
    public async Task A_partial_allocation_of_a_full_invoice_leaves_the_group_partially_invoiced()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, expected: 1_000_000m);
        var invoice = AddInvoice(ctx, seed, gross: 600_000m, number: "FT 403");
        await ctx.SaveChangesAsync();
        Allocate(ctx, invoice, group, 600_000m, seed.ActorId);
        await ctx.SaveChangesAsync();

        Body(await BuildController(ctx, seed.ActorId).Validate(
            seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));

        ctx.ChangeTracker.Clear();
        Assert.Equal(Agg.PartiallyInvoiced,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "GROUP_OI_STATUS"));
    }

    // ── The explicit divergence decision ──

    [Fact]
    public async Task An_over_expected_group_blocks_validation_until_the_divergence_is_accepted()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, expected: 500_000m);
        var invoice = AddInvoice(ctx, seed, gross: 620_000m, number: "FT 404");
        await ctx.SaveChangesAsync();
        Allocate(ctx, invoice, group, 620_000m, seed.ActorId);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        // No acceptance → refused with the full numeric context.
        var refused = await controller.Validate(seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto());
        AssertCode(refused, OperationInvoicesController.ValidateDivergenceRequiredCode);

        // Accepted=false → still refused; acceptance is never inferred from the entry existing.
        AssertCode(await controller.Validate(seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto
        {
            DivergenceAcceptances = new List<OperationInvoiceDivergenceAcceptanceDto>
            {
                new() { RequestPoGroupId = group.Id, Accepted = false,
                        Justification = "ZZTEST justificativa com tamanho suficiente" }
            }
        }), OperationInvoicesController.ValidateDivergenceRequiredCode);

        // Accepted with a meaningful justification → validated, snapshot records the decision.
        var body = Body(await controller.Validate(seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto
        {
            DivergenceAcceptances = new List<OperationInvoiceDivergenceAcceptanceDto>
            {
                new() { RequestPoGroupId = group.Id, Accepted = true,
                        Justification = "ZZTEST frete e taxas alfandegárias adicionais" }
            }
        }));
        Assert.Equal(Doc.Validated, body.Status);

        ctx.ChangeTracker.Clear();
        var snapshot = Assert.Single(await ctx.OperationInvoiceReconciliations.ToListAsync());
        Assert.True(snapshot.DivergenceDetected);
        Assert.True(snapshot.DivergenceAccepted);
        Assert.Equal("ZZTEST frete e taxas alfandegárias adicionais", snapshot.DivergenceJustification);
        Assert.Equal(120_000m, snapshot.ResidualVariance);
        Assert.Equal(500_000m, snapshot.ExpectedTotalAtComparison);

        Assert.Equal(Agg.Satisfied,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);
    }

    // ── The immutable snapshot ──

    [Fact]
    public async Task The_snapshot_records_identity_matches_and_a_retry_never_duplicates_it()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, expected: 300_000m, nif: "111000111");
        var invoice = AddInvoice(ctx, seed, gross: 300_000m, number: "FT 405",
            taxId: "111000111", billedCompany: "ALPLA ZZTEST LDA");
        await ctx.SaveChangesAsync();
        Allocate(ctx, invoice, group, 300_000m, seed.ActorId);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Body(await controller.Validate(seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));
        Body(await controller.Validate(seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));

        ctx.ChangeTracker.Clear();
        var snapshot = Assert.Single(await ctx.OperationInvoiceReconciliations.ToListAsync());
        Assert.True(snapshot.NifMatched);
        Assert.True(snapshot.SupplierMatched);
        Assert.True(snapshot.CurrencyMatched);
        Assert.True(snapshot.CompanyMatched);
        Assert.Equal(300_000m, snapshot.AllocatedTotal);
        Assert.Equal(300_000m, snapshot.InvoiceTotal);
        Assert.Equal(invoice.AttachmentId, snapshot.OperationInvoiceAttachmentId);
        Assert.Equal(seed.ActorId, snapshot.CreatedByUserId);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ReconciliationDataJson));
    }

    [Fact]
    public async Task Mismatched_identities_are_recorded_honestly_not_blocked()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, expected: 300_000m, nif: "999888777");
        var invoice = AddInvoice(ctx, seed, gross: 300_000m, number: "FT 406",
            taxId: "111000111", billedCompany: null);
        await ctx.SaveChangesAsync();
        Allocate(ctx, invoice, group, 300_000m, seed.ActorId);
        await ctx.SaveChangesAsync();

        Body(await BuildController(ctx, seed.ActorId).Validate(
            seed.RequestId, invoice.Id, new ValidateOperationInvoiceDto()));

        ctx.ChangeTracker.Clear();
        var snapshot = Assert.Single(await ctx.OperationInvoiceReconciliations.ToListAsync());
        Assert.False(snapshot.NifMatched);       // different NIFs — recorded, decision was Finance's
        Assert.False(snapshot.CompanyMatched);   // nothing read from the document — no match claimed
        Assert.True(snapshot.SupplierMatched);
    }

    // ── Rejection and the aggregate ──

    [Fact]
    public async Task Rejecting_the_invoice_returns_the_group_to_pending_upload_in_the_same_transaction()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, expected: 1_000_000m);
        var invoice = AddInvoice(ctx, seed, gross: 1_000_000m, number: "FT 407");
        await ctx.SaveChangesAsync();
        Allocate(ctx, invoice, group, 1_000_000m, seed.ActorId);
        group.OperationInvoiceStatus = Agg.PendingValidation;   // as the draft write left it
        await ctx.SaveChangesAsync();

        Body(await BuildController(ctx, seed.ActorId).Reject(
            seed.RequestId, invoice.Id,
            new RejectOperationInvoiceDto { Reason = "ZZTEST documento ilegível" }));

        ctx.ChangeTracker.Clear();
        Assert.Equal(Agg.PendingUpload,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);
        Assert.False(await ctx.OperationInvoiceReconciliations.AnyAsync());   // rejection mints nothing
    }
}
