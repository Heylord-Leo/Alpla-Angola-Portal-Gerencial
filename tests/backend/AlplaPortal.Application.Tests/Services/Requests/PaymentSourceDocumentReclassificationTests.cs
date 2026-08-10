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

/// <summary>
/// Release 4 Phase 1c: the classification → group obligation contract at the endpoint level.
///
/// <para>The invariant pinned here: document classification and group obligation cache commit
/// TOGETHER or not at all. A refused reclassification leaves the document, the group and the
/// history exactly as they were — the controller has a single SaveChanges, so a 409 before it
/// persists nothing.</para>
/// </summary>
public class PaymentSourceDocumentReclassificationTests
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

    /// <summary>
    /// A PAYMENT request in AREA_ADJUSTMENT — the editable-while-grouped shape the guard exists
    /// for. Groups only exist post-approval, so this is the defensive scenario, seeded directly.
    /// </summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string statusCode = "AREA_ADJUSTMENT")
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Reclass Tester", Email = "reclass@test.local" };
        ctx.Users.Add(actor);

        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 5, Code = statusCode, Name = statusCode, DisplayOrder = 5 });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-RECLASS-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST reclassification",
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
        ApplicationDbContext ctx, Seed seed, int sequence, string type)
    {
        var document = new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SourceDocumentType = type,
            SupplierId = 10,
            // The group was built FROM these documents, so the commercial dimensions agree —
            // exactly what makes these scenarios type-only changes.
            Currency = "AOA",
            SequenceNumber = sequence,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-11),
            CreatedByUserId = seed.ActorId
        };
        ctx.PaymentSourceDocuments.Add(document);
        return document;
    }

    private static RequestPoGroup AddGroup(
        ApplicationDbContext ctx, Seed seed, string type, string status,
        decimal? expected, bool requires)
    {
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = 10,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA",
            TotalAmount = expected ?? 0m,
            Status = RequestConstants.PoGroupStatuses.WaitingPo,
            SourceDocumentType = type,
            OperationInvoiceStatus = status,
            RequiresOperationInvoice = requires,
            ExpectedOperationInvoiceTotal = expected,
            ExpectedOperationInvoiceCurrency = expected.HasValue ? "AOA" : null,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-9),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        return group;
    }

    private static void Link(
        ApplicationDbContext ctx, Seed seed, Guid documentId, Guid groupId, int lineNumber)
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

    private static SavePaymentSourceDocumentDto Reclassify(string type) => new()
    {
        SourceDocumentType = type
    };

    // ── Case A: the group stays internally consistent ──

    [Fact]
    public async Task Proforma_to_factura_restamps_the_group_and_preserves_the_expected_total()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1, Types.Proforma);
        var group = AddGroup(ctx, seed, Types.Proforma, Agg.PendingUpload, expected: 10_000_000m, requires: true);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, Reclassify(Types.Invoice));

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(Types.Invoice, persisted.SourceDocumentType);
        Assert.Equal(Agg.NotRequired, persisted.OperationInvoiceStatus);
        Assert.False(persisted.RequiresOperationInvoice);

        // The captured financial snapshot survives the correction — never recalculated, never
        // cleared by NOT_REQUIRED.
        Assert.Equal(10_000_000m, persisted.ExpectedOperationInvoiceTotal);
        Assert.Equal("AOA", persisted.ExpectedOperationInvoiceCurrency);

        // One audit row explains both facts.
        var history = await ctx.RequestStatusHistories
            .SingleAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "GRUPO_OBRIGACAO_REDERIVADA");
        Assert.Contains("Factura Pró-forma", history.Comment);
        Assert.Contains("Factura", history.Comment);
        Assert.Contains("não exigida", history.Comment);
    }

    [Fact]
    public async Task Factura_to_proforma_reopens_the_obligation_without_inventing_a_total()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1, Types.Invoice);
        // INVOICE at creation → nothing was owed, so no expected total was ever captured.
        var group = AddGroup(ctx, seed, Types.Invoice, Agg.NotRequired, expected: null, requires: false);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, Reclassify(Types.Proforma));

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(Types.Proforma, persisted.SourceDocumentType);
        Assert.Equal(Agg.PendingUpload, persisted.OperationInvoiceStatus);
        Assert.True(persisted.RequiresOperationInvoice);

        // No invention: the finish line stays unknown and the projection reports it as such.
        Assert.Null(persisted.ExpectedOperationInvoiceTotal);
    }

    [Fact]
    public async Task After_a_valid_restamp_the_projector_reports_no_drift()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1, Types.Proforma);
        var group = AddGroup(ctx, seed, Types.Proforma, Agg.PendingUpload, expected: 5_000m, requires: true);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, Reclassify(Types.Invoice));

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
        Assert.Equal(Agg.NotRequired, obligation.DerivedStatus);
    }

    // ── Case B: the change would break the grouping key ──

    [Fact]
    public async Task Reclassifying_one_of_two_agreeing_documents_is_blocked_and_nothing_moves()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var doc1 = AddDocument(ctx, seed, 1, Types.Proforma);
        var doc2 = AddDocument(ctx, seed, 2, Types.Proforma);
        var group = AddGroup(ctx, seed, Types.Proforma, Agg.PendingUpload, expected: 20_000m, requires: true);
        Link(ctx, seed, doc1.Id, group.Id, 1);
        Link(ctx, seed, doc2.Id, group.Id, 2);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, doc2.Id, Reclassify(Types.Invoice));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PoGroupReclassificationBlockReasons.GroupingKeyInvalidated, problem.Extensions["code"]);

        // Atomicity: the refused change persisted NOTHING — not the document, not the group,
        // not a history row.
        ctx.ChangeTracker.Clear();
        Assert.Equal(Types.Proforma,
            (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == doc2.Id)).SourceDocumentType);
        var persistedGroup = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(Types.Proforma, persistedGroup.SourceDocumentType);
        Assert.Equal(Agg.PendingUpload, persistedGroup.OperationInvoiceStatus);
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "GRUPO_OBRIGACAO_REDERIVADA"));
    }

    [Fact]
    public async Task A_group_with_operation_invoice_activity_refuses_reclassification()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1, Types.Proforma);
        var group = AddGroup(ctx, seed, Types.Proforma, Agg.PartiallyInvoiced, expected: 10_000m, requires: true);
        Link(ctx, seed, document.Id, group.Id, 1);

        var invoice = new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            Status = RequestConstants.OperationInvoiceDocumentStatuses.Validated,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = seed.ActorId
        };
        ctx.OperationInvoices.Add(invoice);
        ctx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
        {
            OperationInvoiceId = invoice.Id,
            RequestPoGroupId = group.Id,
            AllocatedGrossAmount = 4_000m,
            SequenceNumber = 1
        });
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, Reclassify(Types.Invoice));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PoGroupReclassificationBlockReasons.PostPaymentActivityStarted,
            problem.Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Types.Proforma,
            (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == document.Id)).SourceDocumentType);
    }

    // ── The normal (pre-group) path stays untouched ──

    [Fact]
    public async Task A_draft_reclassification_with_no_groups_needs_no_restamp_and_writes_no_restamp_history()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: "DRAFT");
        var document = AddDocument(ctx, seed, 1, Types.Proforma);
        // A line not yet grouped — the DRAFT shape.
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            LineNumber = 1,
            Description = "ZZTEST ungrouped line",
            Quantity = 1m,
            UnitPrice = 100m,
            TotalAmount = 100m,
            PaymentSourceDocumentId = document.Id
        });
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, Reclassify(Types.Invoice));

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        Assert.Equal(Types.Invoice,
            (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == document.Id)).SourceDocumentType);
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "GRUPO_OBRIGACAO_REDERIVADA"));
    }

    [Fact]
    public async Task A_legacy_group_unrelated_to_the_document_is_never_opportunistically_repaired()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: "DRAFT");
        var document = AddDocument(ctx, seed, 1, Types.Proforma);
        // A drifted legacy group with NO lines from this document: identity says INVOICE, cache
        // says PENDING_UPLOAD. The reclassification of an unrelated document must not "fix" it.
        var legacy = AddGroup(ctx, seed, Types.Invoice, Agg.PendingUpload, expected: null, requires: true);
        await ctx.SaveChangesAsync();

        await BuildController(ctx, seed.ActorId)
            .Update(seed.RequestId, document.Id, Reclassify(Types.Invoice));

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == legacy.Id);
        Assert.Equal(Agg.PendingUpload, persisted.OperationInvoiceStatus);   // still drifted, on purpose
        Assert.True(persisted.RequiresOperationInvoice);
    }

    // ── Void guard ──

    [Fact]
    public async Task A_document_feeding_a_group_cannot_be_voided()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = AddDocument(ctx, seed, 1, Types.Proforma);
        var group = AddGroup(ctx, seed, Types.Proforma, Agg.PendingUpload, expected: 10_000m, requires: true);
        Link(ctx, seed, document.Id, group.Id, 1);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Remove(seed.RequestId, document.Id, new VoidPaymentSourceDocumentDto { Reason = "ZZTEST" });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PoGroupReclassificationBlockReasons.DocumentContributesToGroups,
            problem.Extensions["code"]);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == document.Id);
        Assert.False(persisted.IsVoided);
    }
}
