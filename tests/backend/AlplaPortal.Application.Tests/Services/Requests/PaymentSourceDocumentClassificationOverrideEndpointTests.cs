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
/// The classification-override contract at the endpoint level, for BOTH document lifecycles.
///
/// <para>The TEST regression behind these tests: the create-time composer lost the user's
/// acknowledgement client-side, so Create received <c>classificationConflictAcknowledged: false</c>
/// and correctly refused. These tests pin the backend half of the contract: evidence in the DTO is
/// sufficient — a valid override is accepted and audited atomically with the document write, an
/// invalid one persists NOTHING, and the Phase 1c/1d grouping-key guard still runs in the same
/// transaction as an override.</para>
/// </summary>
public class PaymentSourceDocumentClassificationOverrideEndpointTests
{
    private const string ValidJustification =
        "ZZTEST o fornecedor confirmou por email que emitirá proforma."; // ≥ 20 chars

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

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string statusCode = "DRAFT")
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Override Tester", Email = "override@test.local" };
        ctx.Users.Add(actor);

        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 5, Code = statusCode, Name = statusCode, DisplayOrder = 5 });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-OVR-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST classification override",
            RequestTypeId = 2,
            StatusId = 5,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
        };
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static RequestAttachment AddAttachment(ApplicationDbContext ctx, Seed seed)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "documento.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_RECEIPT,
            StorageReference = "zztest/documento-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        ctx.RequestAttachments.Add(attachment);
        return attachment;
    }

    /// <summary>A genuine contradiction: OCR read a fiscal Factura, the user selects Pró-forma.</summary>
    private static SavePaymentSourceDocumentDto ConflictingCreateDto(
        Guid attachmentId, bool acknowledged, string? justification) => new()
    {
        AttachmentId = attachmentId,
        SourceDocumentType = Types.Proforma,
        OcrSuggestion = Types.Invoice,
        OcrConfidence = 0.9m,
        ClassificationSuggestionSource = "OCR",
        ClassificationConflictAcknowledged = acknowledged,
        ClassificationJustification = justification
    };

    // ── A0. The retired conflict: Orçamento/Cotação vs Pró-forma is agreement (v2.229.10) ──

    [Fact]
    public async Task Create_with_ocr_estimate_and_selected_proforma_is_agreement_not_a_conflict()
    {
        // The PROD walkthrough defect: OCR reads "Orçamento / Cotação", the payment screen only
        // offers "Factura Pró-forma", and the system manufactured a contradiction between two names
        // for the same payable origin. Canonicalized, the pair is agreement: no acknowledgement, no
        // justification, no override row, no divergence history — the ordinary OCR-confirmed path.
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).Create(seed.RequestId,
            new SavePaymentSourceDocumentDto
            {
                AttachmentId = attachment.Id,
                SourceDocumentType = Types.Proforma,
                OcrSuggestion = Types.Estimate,
                OcrConfidence = 0.9m,
                OcrTitleFound = "PROPOSTA COMERCIAL",
                ClassificationSuggestionSource = "OCR"
                // Deliberately NO acknowledgement and NO justification — none is owed.
            });

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var document = await ctx.PaymentSourceDocuments.SingleAsync(d => d.RequestId == seed.RequestId);
        Assert.Equal(Types.Proforma, document.SourceDocumentType);
        Assert.Equal(Types.Proforma, document.OcrSuggestion);            // canonicalized suggestion
        Assert.Equal("PROPOSTA COMERCIAL", document.OcrTitleFound);      // raw evidence preserved
        Assert.Equal(0, await ctx.DocumentClassificationOverrides.CountAsync());
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "CLASSIFICACAO_DOCUMENTO_DIVERGENTE"));
    }

    // ── A. Create-time override with complete evidence ──

    [Fact]
    public async Task Create_accepts_a_confirmed_override_and_persists_document_and_audit_together()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ConflictingCreateDto(attachment.Id, acknowledged: true, ValidJustification));

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var document = await ctx.PaymentSourceDocuments.SingleAsync(d => d.RequestId == seed.RequestId);
        Assert.Equal(Types.Proforma, document.SourceDocumentType);       // the user's decision
        Assert.Equal(Types.Invoice, document.OcrSuggestion);             // the reading, preserved
        Assert.True(document.ClassificationConflictAcknowledged);

        var overrideRow = await ctx.DocumentClassificationOverrides.SingleAsync();
        Assert.Equal(Types.Proforma, overrideRow.SelectedType);
        Assert.Equal(Types.Invoice, overrideRow.SuggestedType);
        Assert.True(overrideRow.Acknowledged);
        Assert.Equal(ValidJustification, overrideRow.Justification);
        Assert.Equal(seed.ActorId, overrideRow.ActorUserId);

        Assert.True(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "CLASSIFICACAO_DOCUMENTO_DIVERGENTE"));
    }

    // ── B. Missing confirmation ──

    [Fact]
    public async Task Create_without_the_explicit_confirmation_rejects_and_persists_nothing()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ConflictingCreateDto(attachment.Id, acknowledged: false, ValidJustification));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Contains("confirmar explicitamente", problem.Detail);

        // Atomic refusal: no document, no override row, no history — nothing half-created.
        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.PaymentSourceDocuments.CountAsync());
        Assert.Equal(0, await ctx.DocumentClassificationOverrides.CountAsync());
        Assert.Equal(0, await ctx.RequestStatusHistories.CountAsync());
    }

    // ── C. Missing/short justification ──

    [Fact]
    public async Task Create_with_a_short_justification_rejects_and_persists_nothing()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, ConflictingCreateDto(attachment.Id, acknowledged: true, "curto demais"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Contains("justificar por escrito", problem.Detail);

        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.PaymentSourceDocuments.CountAsync());
        Assert.Equal(0, await ctx.DocumentClassificationOverrides.CountAsync());
    }

    // ── D. No conflict, no override owed ──

    [Fact]
    public async Task Create_agreeing_with_the_reading_needs_no_override_and_records_none()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var attachment = AddAttachment(ctx, seed);
        await ctx.SaveChangesAsync();

        var dto = new SavePaymentSourceDocumentDto
        {
            AttachmentId = attachment.Id,
            SourceDocumentType = Types.Estimate,   // stray legacy code — canonicalizes on write
            OcrSuggestion = Types.Estimate,
            OcrConfidence = 0.9m,
            ClassificationSuggestionSource = "OCR"
            // No acknowledgement, no justification — none is owed.
        };

        var result = await BuildController(ctx, seed.ActorId).Create(seed.RequestId, dto);

        Assert.IsType<OkObjectResult>(result);
        ctx.ChangeTracker.Clear();
        var document = await ctx.PaymentSourceDocuments.SingleAsync();
        Assert.Equal(Types.Proforma, document.SourceDocumentType);   // ESTIMATE never persisted again
        Assert.Equal(0, await ctx.DocumentClassificationOverrides.CountAsync());
    }

    // ── E. Persisted-document update path ──

    [Fact]
    public async Task Update_accepts_a_confirmed_override_on_a_persisted_document()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SourceDocumentType = Types.Invoice,
            OcrSuggestion = Types.Invoice,
            OcrConfidence = 0.9m,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        };
        ctx.PaymentSourceDocuments.Add(document);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).Update(seed.RequestId, document.Id,
            new SavePaymentSourceDocumentDto
            {
                SourceDocumentType = Types.Proforma,
                ClassificationConflictAcknowledged = true,
                ClassificationJustification = ValidJustification
            });

        Assert.IsType<OkObjectResult>(result);
        ctx.ChangeTracker.Clear();
        Assert.Equal(Types.Proforma,
            (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == document.Id)).SourceDocumentType);
        Assert.Equal(1, await ctx.DocumentClassificationOverrides.CountAsync());
    }

    /// <summary>A stray persisted ESTIMATE updated to Pró-forma is canonical agreement, not an override.</summary>
    [Fact]
    public async Task Update_from_stray_estimate_to_proforma_needs_no_acknowledgement()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var document = new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SourceDocumentType = Types.Estimate,   // legacy value persisted before v2.229.10
            OcrSuggestion = Types.Estimate,
            OcrConfidence = 0.9m,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        };
        ctx.PaymentSourceDocuments.Add(document);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).Update(seed.RequestId, document.Id,
            new SavePaymentSourceDocumentDto { SourceDocumentType = Types.Proforma });

        Assert.IsType<OkObjectResult>(result);
        ctx.ChangeTracker.Clear();
        Assert.Equal(Types.Proforma,
            (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == document.Id)).SourceDocumentType);
        Assert.Equal(0, await ctx.DocumentClassificationOverrides.CountAsync());
    }

    // ── F. Override + grouping-key guard in one transaction ──

    [Fact]
    public async Task A_grouped_document_override_still_restamps_the_group_in_the_same_transaction()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: "AREA_ADJUSTMENT");
        var document = new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SourceDocumentType = Types.Proforma,
            OcrSuggestion = Types.Proforma,
            OcrConfidence = 0.9m,
            Currency = "AOA",
            SupplierId = 10,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        };
        ctx.PaymentSourceDocuments.Add(document);
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = 10,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 10_000m,
            Status = RequestConstants.PoGroupStatuses.WaitingPo,
            SourceDocumentType = Types.Proforma,
            OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true,
            ExpectedOperationInvoiceTotal = 10_000m,
            ExpectedOperationInvoiceCurrency = "AOA",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            LineNumber = 1,
            Description = "ZZTEST line",
            Quantity = 1m,
            UnitPrice = 100m,
            TotalAmount = 100m,
            PaymentSourceDocumentId = document.Id,
            RequestPoGroupId = group.Id
        });
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).Update(seed.RequestId, document.Id,
            new SavePaymentSourceDocumentDto
            {
                SourceDocumentType = Types.Invoice,
                ClassificationConflictAcknowledged = true,
                ClassificationJustification = ValidJustification
            });

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var persistedGroup = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(Types.Invoice, persistedGroup.SourceDocumentType);
        Assert.Equal(Agg.NotRequired, persistedGroup.OperationInvoiceStatus);
        Assert.Equal(10_000m, persistedGroup.ExpectedOperationInvoiceTotal);   // snapshot preserved

        Assert.Equal(1, await ctx.DocumentClassificationOverrides.CountAsync());
        Assert.True(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "GRUPO_OBRIGACAO_REDERIVADA"));
    }

    // ── G. Atomicity when the grouping guard refuses ──

    [Fact]
    public async Task A_blocked_grouping_change_never_persists_the_override_alone()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, statusCode: "AREA_ADJUSTMENT");

        PaymentSourceDocument Doc(int sequence) => new()
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SourceDocumentType = Types.Proforma,
            OcrSuggestion = Types.Proforma,
            OcrConfidence = 0.9m,
            Currency = "AOA",
            SupplierId = 10,
            SequenceNumber = sequence,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        };

        var doc1 = Doc(1);
        var doc2 = Doc(2);
        ctx.PaymentSourceDocuments.AddRange(doc1, doc2);
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = 10,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 20_000m,
            Status = RequestConstants.PoGroupStatuses.WaitingPo,
            SourceDocumentType = Types.Proforma,
            OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        foreach (var (doc, line) in new[] { (doc1, 1), (doc2, 2) })
        {
            ctx.RequestLineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = seed.RequestId,
                LineNumber = line,
                Description = "ZZTEST line " + line,
                Quantity = 1m,
                UnitPrice = 100m,
                TotalAmount = 100m,
                PaymentSourceDocumentId = doc.Id,
                RequestPoGroupId = group.Id
            });
        }
        await ctx.SaveChangesAsync();

        // The override evidence is COMPLETE — the refusal comes from the grouping key alone.
        var result = await BuildController(ctx, seed.ActorId).Update(seed.RequestId, doc2.Id,
            new SavePaymentSourceDocumentDto
            {
                SourceDocumentType = Types.Invoice,
                ClassificationConflictAcknowledged = true,
                ClassificationJustification = ValidJustification
            });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PoGroupReclassificationBlockReasons.GroupingKeyInvalidated, problem.Extensions["code"]);

        // The staged override never reached the database: classification, override audit and
        // group re-stamp commit together or not at all.
        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.DocumentClassificationOverrides.CountAsync());
        Assert.Equal(Types.Proforma,
            (await ctx.PaymentSourceDocuments.SingleAsync(d => d.Id == doc2.Id)).SourceDocumentType);
        Assert.Equal(Agg.PendingUpload,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "CLASSIFICACAO_DOCUMENTO_DIVERGENTE"));
    }
}
