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

using Agg = RequestConstants.OperationInvoiceStatuses;
using Types = RequestConstants.SourceDocumentTypes;
using Reasons = PoGroupReclassificationBlockReasons;

/// <summary>
/// Release 4 Phase 1d: the FULL grouping-key invariant at the endpoint level — supplier, currency
/// and plant edits on a grouped document, not only the document type. Affected groups always come
/// from line ownership; the request header plays no part. The payment condition has no
/// document-side mutation path (documents carry none; the Buyer refines it per group at P.O.
/// registration), so its coverage lives in the pure planner tests.
/// </summary>
public class PaymentSourceDocumentGroupingKeyIntegrityTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static PaymentSourceDocumentsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new PaymentSourceDocumentsController(
            ctx,
            NullLogger<PaymentSourceDocumentsController>.Instance,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, RoleConstants.Finance)
                }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid ActorId);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string statusCode = "AREA_ADJUSTMENT")
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Key Tester", Email = "key@test.local" };
        ctx.Users.Add(actor);

        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 5, Code = statusCode, Name = statusCode, DisplayOrder = 5 });
        ctx.Suppliers.AddRange(
            new Supplier { Id = 10, Name = "ZZTEST Supplier A", TaxId = "111111111" },
            new Supplier { Id = 99, Name = "ZZTEST Supplier B", TaxId = "222222222" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-KEY-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST grouping key",
            RequestTypeId = 2,
            StatusId = 5,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            SubmittedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-12)
        };
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static PaymentSourceDocument AddDocument(
        ApplicationDbContext ctx, Seed seed, int sequence,
        string type = Types.Proforma, int? supplierId = 10, string currency = "AOA", int? plantId = 1)
    {
        var document = new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SourceDocumentType = type,
            SupplierId = supplierId,
            Currency = currency,
            PlantId = plantId,
            SequenceNumber = sequence,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-11),
            CreatedByUserId = seed.ActorId
        };
        ctx.PaymentSourceDocuments.Add(document);
        return document;
    }

    private static RequestPoGroup AddGroup(
        ApplicationDbContext ctx, Seed seed,
        string type = Types.Proforma, int? supplierId = 10, string currency = "AOA", int? plantId = 1,
        decimal? expected = 10_000_000m, string? poNumber = null)
    {
        var requires = type == Types.Proforma;
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = supplierId,
            SupplierNameSnapshot = "ZZTEST Supplier A",
            SupplierNifSnapshot = "111111111",
            CurrencyCode = currency,
            PlantId = plantId,
            TotalAmount = expected ?? 0m,
            Status = RequestConstants.PoGroupStatuses.WaitingPo,
            SourceDocumentType = type,
            OperationInvoiceStatus = requires ? Agg.PendingUpload : Agg.NotRequired,
            RequiresOperationInvoice = requires,
            ExpectedOperationInvoiceTotal = expected,
            ExpectedOperationInvoiceCurrency = expected.HasValue ? currency : null,
            PurchaseOrderNumber = poNumber,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-9),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        return group;
    }

    private static void Link(ApplicationDbContext ctx, Seed seed, Guid documentId, Guid groupId, int lineNumber)
    {
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            LineNumber = lineNumber,
            Description = "ZZTEST line " + lineNumber,
            Quantity = 1m,
            UnitPrice = 100m,
            TotalAmount = 100m,
            PaymentSourceDocumentId = documentId,
            RequestPoGroupId = groupId
        });
    }

    private static async Task<ProblemDetails> AssertBlockedAsync(
        Task<IActionResult> call, string expectedCode)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(await call);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(expectedCode, problem.Extensions["code"]);
        return problem;
    }

    // ── Supplier dimension ──

    [Fact]
    public async Task A_pre_group_supplier_edit_stays_allowed()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: "DRAFT");
        var document = AddDocument(ctx, seed, 1);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, new SavePaymentSourceDocumentDto { SupplierId = 99 });

        Assert.IsType<OkObjectResult>(result);
        ctx.ChangeTracker.Clear();
        Assert.Equal(99, (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == document.Id)).SupplierId);
    }

    [Fact]
    public async Task A_supplier_edit_that_splits_a_two_document_group_is_blocked_atomically()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var doc1 = AddDocument(ctx, seed, 1);
        var doc2 = AddDocument(ctx, seed, 2);
        var group = AddGroup(ctx, seed);
        Link(ctx, seed, doc1.Id, group.Id, 1);
        Link(ctx, seed, doc2.Id, group.Id, 2);
        await ctx.SaveChangesAsync();

        var problem = await AssertBlockedAsync(
            BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, doc2.Id, new SavePaymentSourceDocumentDto { SupplierId = 99 }),
            Reasons.GroupingKeyInvalidated);

        Assert.Contains(PoGroupReclassificationPlanner.DimensionNames.Supplier, problem.Detail);

        ctx.ChangeTracker.Clear();
        Assert.Equal(10, (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == doc2.Id)).SupplierId);
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(10, persisted.SupplierId);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "GRUPO_OBRIGACAO_REDERIVADA"));
    }

    [Fact]
    public async Task A_registered_po_blocks_a_coherent_whole_group_supplier_change()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1);
        var group = AddGroup(ctx, seed, poNumber: "45000123");
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        await AssertBlockedAsync(
            BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, document.Id, new SavePaymentSourceDocumentDto { SupplierId = 99 }),
            Reasons.FinancialEvidenceExists);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(10, persisted.SupplierId);
        Assert.Equal("45000123", persisted.PurchaseOrderNumber);
    }

    [Fact]
    public async Task A_coherent_supplier_change_with_no_evidence_restamps_identity_and_snapshots()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1);
        var group = AddGroup(ctx, seed);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, new SavePaymentSourceDocumentDto { SupplierId = 99 });

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(99, persisted.SupplierId);
        Assert.Equal("ZZTEST Supplier B", persisted.SupplierNameSnapshot);
        Assert.Equal("222222222", persisted.SupplierNifSnapshot);

        // Identity moved; the financial snapshot and the obligation did not.
        Assert.Equal(10_000_000m, persisted.ExpectedOperationInvoiceTotal);
        Assert.Equal(Agg.PendingUpload, persisted.OperationInvoiceStatus);
        Assert.Null(persisted.PurchaseOrderNumber);

        var history = await ctx.RequestStatusHistories
            .SingleAsync(h => h.ActionTaken == "GRUPO_OBRIGACAO_REDERIVADA");
        Assert.Contains(PoGroupReclassificationPlanner.DimensionNames.Supplier, history.Comment);
    }

    [Fact]
    public async Task The_internal_company_rule_still_fires_before_grouping_key_integrity()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaPLASTICO", TaxId = "5417567485", Code = "APA" });
        ctx.Suppliers.Add(new Supplier { Id = 500, Name = "ZZTEST Internal Clone", TaxId = "5417567485" });
        var document = AddDocument(ctx, seed, 1);
        var group = AddGroup(ctx, seed);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, new SavePaymentSourceDocumentDto { SupplierId = 500 });

        // The financial-integrity rule answers first, with its own contract (400, its own code) —
        // grouping-key integrity never dilutes it into a 409.
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(InternalCompanyPolicy.ViolationCode, problem.Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal(10, (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == document.Id)).SupplierId);
    }

    // ── Currency dimension ──

    [Fact]
    public async Task A_currency_edit_that_splits_a_group_is_blocked()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var doc1 = AddDocument(ctx, seed, 1);
        var doc2 = AddDocument(ctx, seed, 2);
        var group = AddGroup(ctx, seed);
        Link(ctx, seed, doc1.Id, group.Id, 1);
        Link(ctx, seed, doc2.Id, group.Id, 2);
        await ctx.SaveChangesAsync();

        await AssertBlockedAsync(
            BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, doc2.Id, new SavePaymentSourceDocumentDto { Currency = "USD" }),
            Reasons.GroupingKeyInvalidated);
    }

    [Fact]
    public async Task A_captured_expected_total_blocks_a_coherent_currency_change()
    {
        // The snapshot is denominated in AOA; relabelling the group USD would falsify it.
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1);
        var group = AddGroup(ctx, seed, expected: 10_000_000m);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        await AssertBlockedAsync(
            BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, document.Id, new SavePaymentSourceDocumentDto { Currency = "USD" }),
            Reasons.FinancialEvidenceExists);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal("AOA", persisted.CurrencyCode);
        Assert.Equal("AOA", persisted.ExpectedOperationInvoiceCurrency);
    }

    [Fact]
    public async Task A_coherent_currency_change_with_nothing_captured_restamps_the_group()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.Currencies.Add(new Currency { Id = 7, Code = "USD", Symbol = "$" });
        var document = AddDocument(ctx, seed, 1, type: Types.Invoice);
        var group = AddGroup(ctx, seed, type: Types.Invoice, expected: null);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, new SavePaymentSourceDocumentDto { Currency = "usd" });

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal("USD", persisted.CurrencyCode);            // normalized, canonical
        Assert.Equal(7, persisted.CurrencyId);
        Assert.Null(persisted.ExpectedOperationInvoiceTotal);   // still honestly unknown — no capture
    }

    // ── Plant dimension ──

    [Fact]
    public async Task A_plant_edit_that_splits_a_group_is_blocked()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var doc1 = AddDocument(ctx, seed, 1);
        var doc2 = AddDocument(ctx, seed, 2);
        var group = AddGroup(ctx, seed);
        Link(ctx, seed, doc1.Id, group.Id, 1);
        Link(ctx, seed, doc2.Id, group.Id, 2);
        await ctx.SaveChangesAsync();

        await AssertBlockedAsync(
            BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, doc2.Id, new SavePaymentSourceDocumentDto { PlantId = 2 }),
            Reasons.GroupingKeyInvalidated);
    }

    [Fact]
    public async Task A_payment_on_the_group_blocks_a_coherent_plant_change()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1);
        var group = AddGroup(ctx, seed);
        Link(ctx, seed, document.Id, group.Id, 1);
        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = seed.RequestId,
            RequestPoGroupId = group.Id,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = seed.ActorId
        });
        await ctx.SaveChangesAsync();

        await AssertBlockedAsync(
            BuildController(ctx, seed.ActorId)
                .Update(seed.RequestId, document.Id, new SavePaymentSourceDocumentDto { PlantId = 2 }),
            Reasons.FinancialEvidenceExists);
    }

    [Fact]
    public async Task A_coherent_plant_change_with_no_evidence_restamps_the_group()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1);
        var group = AddGroup(ctx, seed);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, new SavePaymentSourceDocumentDto { PlantId = 2 });

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(2, persisted.PlantId);
        Assert.Equal(10_000_000m, persisted.ExpectedOperationInvoiceTotal);
    }

    // ── Cross-cutting ──

    [Fact]
    public async Task An_edit_touching_no_key_dimension_never_engages_the_guard()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1);
        var group = AddGroup(ctx, seed, poNumber: "45000123");   // even with evidence present
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id,
                new SavePaymentSourceDocumentDto { GrossAmount = 123_456m, DocumentNumber = "FT 9/2026" });

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        Assert.Equal(123_456m,
            (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == document.Id)).GrossAmount);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "GRUPO_OBRIGACAO_REDERIVADA"));
    }

    [Fact]
    public async Task After_a_safe_restamp_the_projector_reports_no_artificial_drift()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1);
        var group = AddGroup(ctx, seed);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, new SavePaymentSourceDocumentDto { SupplierId = 99 });

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);

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

        Assert.False(obligation.StatusDrift);
    }

    [Fact]
    public async Task The_rowversion_token_path_still_accepts_a_current_token()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1);
        var group = AddGroup(ctx, seed);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        var current = (await ctx.PaymentSourceDocuments.AsNoTracking()
            .SingleAsync(d => d.Id == document.Id)).RowVersion;

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id,
                new SavePaymentSourceDocumentDto { SupplierId = 99, RowVersion = current });

        Assert.IsType<OkObjectResult>(result);
    }
}
