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

/// <summary>
/// The six-scenario L4 acceptance matrix, executed against the REAL controllers with the real
/// CONSULTIT values (ONP_18910_v3 / 23-07-2026 / 1.492.231,88 AOA) — the executable counterpart
/// of the manual DEV walkthrough: the preflight answers what the review UI renders, the create
/// endpoint answers what persistence enforces.
/// </summary>
public class L4ScenarioValidationTests
{
    private const string Reference = "ONP_18910_v3";
    private static readonly DateTime RefDate = new(2026, 7, 23);
    private const decimal RefGross = 1_492_231.88m;
    private const string RegisteredNif = "5417049840";
    private const string WrongNif = "5000000000";
    private const string OverrideReason = "ZZTEST verificado manualmente: documento distinto do existente.";

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ControllerContext Principal(Guid actorId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, actorId.ToString()),
                new(ClaimTypes.Role, RoleConstants.SystemAdministrator)
            }, "Test")),
            RequestServices = new ServiceCollection().BuildServiceProvider()
        }
    };

    private sealed record Bench(ApplicationDbContext Ctx, Guid ActorId, Guid DraftRequestId,
        PaymentSourceDocumentMatchController Preflight, PaymentSourceDocumentsController Documents);

    /// <summary>Reference document on a live request + an editable DRAFT request to upload into.</summary>
    private static async Task<Bench> SeedBenchAsync(ApplicationDbContext ctx)
    {
        var actorId = Guid.NewGuid();
        ctx.Users.Add(new User { Id = actorId, FullName = "L4 Tester", Email = "l4@test.local" });
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 5, Code = "DRAFT", Name = "Rascunho", DisplayOrder = 5 },
            new RequestStatus { Id = 6, Code = "PO_ISSUED", Name = "P.O.", DisplayOrder = 6 });
        ctx.Suppliers.AddRange(
            new Supplier { Id = 77, PortalCode = "F0077", Name = "CONSULTIT, LDA", TaxId = RegisteredNif, IsActive = true },
            new Supplier { Id = 88, PortalCode = "F0088", Name = "CONSULTIT, LDA", TaxId = WrongNif, IsActive = true });

        Request NewRequest(int statusId, string prefix) => new()
        {
            Id = Guid.NewGuid(),
            RequestNumber = prefix + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST L4",
            RequestTypeId = 2,
            StatusId = statusId,
            RequesterId = actorId,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };

        var referenceRequest = NewRequest(6, "ZZTEST-REF-");
        var draftRequest = NewRequest(5, "ZZTEST-DRAFT-");
        ctx.Requests.AddRange(referenceRequest, draftRequest);

        ctx.PaymentSourceDocuments.Add(new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = referenceRequest.Id,
            AttachmentId = Guid.NewGuid(),
            SupplierId = 77,
            SupplierNameSnapshot = "CONSULTIT, LDA",
            SupplierTaxIdSnapshot = RegisteredNif,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            DocumentNumber = Reference,
            DocumentDate = RefDate,
            Currency = "AOA",
            GrossAmount = RefGross,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedByUserId = actorId
        });

        await ctx.SaveChangesAsync();

        var preflight = new PaymentSourceDocumentMatchController(
            ctx, NullLogger<PaymentSourceDocumentMatchController>.Instance)
        { ControllerContext = Principal(actorId) };
        var documents = new PaymentSourceDocumentsController(
            ctx, NullLogger<PaymentSourceDocumentsController>.Instance,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx))
        { ControllerContext = Principal(actorId) };

        return new Bench(ctx, actorId, draftRequest.Id, preflight, documents);
    }

    private static RequestAttachment AddAttachment(Bench bench)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = bench.DraftRequestId,
            FileName = "l4.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_RECEIPT,
            StorageReference = "zztest/l4-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            FileHash = "l4-" + Guid.NewGuid().ToString("N"),          // always different bytes
            UploadedByUserId = bench.ActorId,
            UploadedAtUtc = DateTime.UtcNow
        };
        bench.Ctx.RequestAttachments.Add(attachment);
        return attachment;
    }

    private static SavePaymentSourceDocumentDto Incoming(
        Guid attachmentId, int? supplierId, string? nif,
        string number = Reference, DateTime? date = null, decimal? gross = RefGross,
        bool overrideAcknowledged = false) => new()
    {
        AttachmentId = attachmentId,
        SupplierId = supplierId,
        SupplierTaxIdSnapshot = nif,
        SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
        DocumentNumber = number,
        DocumentDate = date ?? RefDate,
        Currency = "AOA",
        GrossAmount = gross,
        DuplicateOverrideAcknowledged = overrideAcknowledged ? true : null,
        DuplicateOverrideReason = overrideAcknowledged ? OverrideReason : null
    };

    private static SourceDocumentCandidatesResultDto Body(IActionResult result) =>
        Assert.IsType<SourceDocumentCandidatesResultDto>(Assert.IsType<OkObjectResult>(result).Value);

    // ── L4-1: supplier NIF mismatch ─────────────────────────────────────────────────────────

    [Fact]
    public async Task L4_1_supplier_nif_mismatch_is_ambiguous_and_persistence_demands_the_override()
    {
        using var ctx = NewContext();
        var bench = await SeedBenchAsync(ctx);

        // Review: supplier unresolved (wrong NIF), everything else agreeing.
        var review = Body(await bench.Preflight.MatchCandidates(new MatchSourceDocumentCandidatesDto
        {
            SupplierId = null,
            SupplierName = "CONSULTIT, LDA",
            SupplierTaxId = WrongNif,
            DocumentNumber = Reference,
            DocumentDate = RefDate,
            Currency = "AOA",
            GrossAmount = RefGross
        }));

        Assert.Equal("AMBIGUOUS_MATCH", review.TopClassification);
        var candidate = Assert.Single(review.Candidates);
        Assert.Contains("SUPPLIER_NIF", candidate.ConflictingFields);
        Assert.Equal(RegisteredNif, candidate.Existing!.SupplierTaxId);    // both NIFs presentable

        // Persistence: the user pushed through with a wrong-NIF twin supplier (id 88). The old
        // logic saw a different SupplierId and called it new; now the evidence survives → 409.
        var attachment = AddAttachment(bench);
        await ctx.SaveChangesAsync();

        var refused = await bench.Documents.Create(bench.DraftRequestId,
            Incoming(attachment.Id, supplierId: 88, nif: WrongNif));
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(refused).Value);
        Assert.Equal("DUPLICATE_AMBIGUOUS", problem.Extensions["code"]);

        // Justified override completes, audited.
        Assert.IsType<OkObjectResult>(await bench.Documents.Create(bench.DraftRequestId,
            Incoming(attachment.Id, supplierId: 88, nif: WrongNif, overrideAcknowledged: true)));
        Assert.True(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "DOCUMENTO_DUPLICADO_POTENCIAL_CONFIRMADO"));

        // "Usar fornecedor existente" never edits master data — the flow only selects; prove the
        // registered supplier row is untouched end-to-end.
        var registered = await ctx.Suppliers.AsNoTracking().SingleAsync(s => s.Id == 77);
        Assert.Equal("CONSULTIT, LDA", registered.Name);
        Assert.Equal(RegisteredNif, registered.TaxId);
    }

    // ── L4-3: date mismatch ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task L4_3_date_mismatch_is_ambiguous_with_the_date_highlighted()
    {
        using var ctx = NewContext();
        var bench = await SeedBenchAsync(ctx);

        var review = Body(await bench.Preflight.MatchCandidates(new MatchSourceDocumentCandidatesDto
        {
            SupplierId = 77,
            SupplierTaxId = RegisteredNif,
            DocumentNumber = Reference,
            DocumentDate = new DateTime(2026, 7, 30),
            Currency = "AOA",
            GrossAmount = RefGross
        }));

        Assert.Equal("AMBIGUOUS_MATCH", review.TopClassification);
        Assert.Contains("DOCUMENT_DATE", review.Candidates[0].ConflictingFields);
        Assert.Contains("GROSS_AMOUNT", review.Candidates[0].MatchingFields);

        var attachment = AddAttachment(bench);
        await ctx.SaveChangesAsync();

        var refused = await bench.Documents.Create(bench.DraftRequestId,
            Incoming(attachment.Id, supplierId: 77, nif: RegisteredNif, date: new DateTime(2026, 7, 30)));
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(refused).Value);
        Assert.Equal("DUPLICATE_AMBIGUOUS", problem.Extensions["code"]);

        Assert.IsType<OkObjectResult>(await bench.Documents.Create(bench.DraftRequestId,
            Incoming(attachment.Id, supplierId: 77, nif: RegisteredNif,
                     date: new DateTime(2026, 7, 30), overrideAcknowledged: true)));
    }

    // ── L4-5: total mismatch — the approved L3 CONSULTIT rule, frictionless ─────────────────

    [Fact]
    public async Task L4_5_total_mismatch_stays_informational_and_persists_without_override()
    {
        using var ctx = NewContext();
        var bench = await SeedBenchAsync(ctx);

        var review = Body(await bench.Preflight.MatchCandidates(new MatchSourceDocumentCandidatesDto
        {
            SupplierId = 77,
            SupplierTaxId = RegisteredNif,
            DocumentNumber = Reference,
            DocumentDate = RefDate,
            Currency = "AOA",
            GrossAmount = 3_433_527.55m
        }));

        Assert.Equal("RELATED_DOCUMENT", review.TopClassification);
        Assert.Equal("ALLOW", review.Candidates[0].Verdict);
        Assert.Contains("GROSS_AMOUNT", review.Candidates[0].ConflictingFields);

        var attachment = AddAttachment(bench);
        await ctx.SaveChangesAsync();

        // No override fields, no 409 — accepted exactly like the CONSULTIT proposals.
        Assert.IsType<OkObjectResult>(await bench.Documents.Create(bench.DraftRequestId,
            Incoming(attachment.Id, supplierId: 77, nif: RegisteredNif, gross: 3_433_527.55m)));
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "DOCUMENTO_DUPLICADO_POTENCIAL_CONFIRMADO"));
    }

    // ── L4-6: same commercial identity, different physical file ─────────────────────────────

    [Fact]
    public async Task L4_6_same_commercial_identity_is_a_strong_duplicate_requiring_the_override()
    {
        using var ctx = NewContext();
        var bench = await SeedBenchAsync(ctx);

        var review = Body(await bench.Preflight.MatchCandidates(new MatchSourceDocumentCandidatesDto
        {
            SupplierId = 77,
            SupplierTaxId = RegisteredNif,
            DocumentNumber = "onp-18910-v3",           // representation noise must not matter
            DocumentDate = RefDate,
            Currency = "AOA",
            GrossAmount = RefGross
        }));

        Assert.Equal("STRONG_BUSINESS_DUPLICATE", review.TopClassification);
        Assert.Equal("AMBIGUOUS", review.Candidates[0].Verdict);   // justified override, not a wall

        var attachment = AddAttachment(bench);
        await ctx.SaveChangesAsync();

        var refused = await bench.Documents.Create(bench.DraftRequestId,
            Incoming(attachment.Id, supplierId: 77, nif: RegisteredNif));
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(refused).Value);
        Assert.Equal("DUPLICATE_AMBIGUOUS", problem.Extensions["code"]);
        Assert.Equal("StrongBusinessDuplicate", problem.Extensions["classification"]);
        Assert.Equal("Provável documento duplicado", problem.Title);

        Assert.IsType<OkObjectResult>(await bench.Documents.Create(bench.DraftRequestId,
            Incoming(attachment.Id, supplierId: 77, nif: RegisteredNif, overrideAcknowledged: true)));
    }

    // ── Control B: existing supplier, genuinely new document ────────────────────────────────

    [Fact]
    public async Task Control_B_a_genuinely_new_document_flows_without_any_duplicate_friction()
    {
        using var ctx = NewContext();
        var bench = await SeedBenchAsync(ctx);

        var review = Body(await bench.Preflight.MatchCandidates(new MatchSourceDocumentCandidatesDto
        {
            SupplierId = 77,
            SupplierTaxId = RegisteredNif,
            DocumentNumber = "ONP_20500_v1",
            DocumentDate = new DateTime(2026, 8, 18),
            Currency = "AOA",
            GrossAmount = 250_000m
        }));

        Assert.Null(review.TopClassification);
        Assert.Empty(review.Candidates);

        var attachment = AddAttachment(bench);
        await ctx.SaveChangesAsync();

        Assert.IsType<OkObjectResult>(await bench.Documents.Create(bench.DraftRequestId,
            Incoming(attachment.Id, supplierId: 77, nif: RegisteredNif,
                     number: "ONP_20500_v1", date: new DateTime(2026, 8, 18), gross: 250_000m)));
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "DOCUMENTO_DUPLICADO_POTENCIAL_CONFIRMADO"));
    }
}
