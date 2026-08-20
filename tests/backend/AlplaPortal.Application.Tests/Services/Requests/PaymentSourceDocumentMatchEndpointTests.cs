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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// The review-time candidate preflight (v2.229.10 L4 flow): the same assembly and rule engine the
/// persistence guard runs, exposed before anything exists — so a supplier-resolution failure can
/// no longer silence the search, and the UI can explain WHY a document is considered a match.
/// </summary>
public class PaymentSourceDocumentMatchEndpointTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static PaymentSourceDocumentMatchController BuildController(
        ApplicationDbContext ctx, Guid actorId, bool systemAdministrator = true)
    {
        var controller = new PaymentSourceDocumentMatchController(
            ctx, NullLogger<PaymentSourceDocumentMatchController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, systemAdministrator
                        ? RoleConstants.SystemAdministrator : RoleConstants.Finance)
                }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    /// <summary>The CONSULTIT reference document, registered on a live request.</summary>
    private static async Task<Guid> SeedReferenceDocumentAsync(ApplicationDbContext ctx, Guid actorId)
    {
        ctx.Users.Add(new User { Id = actorId, FullName = "Match Tester", Email = "match@test.local" });
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 6, Code = "PO_ISSUED", Name = "P.O.", DisplayOrder = 1 });
        ctx.Suppliers.Add(new Supplier
        {
            Id = 77, PortalCode = "F0077", Name = "CONSULTIT, LDA", TaxId = "5417049840", IsActive = true
        });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-MATCH-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST match reference",
            RequestTypeId = 2,
            StatusId = 6,
            RequesterId = actorId,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        ctx.Requests.Add(request);

        ctx.PaymentSourceDocuments.Add(new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            AttachmentId = Guid.NewGuid(),
            SupplierId = 77,
            SupplierNameSnapshot = "CONSULTIT, LDA",
            SupplierTaxIdSnapshot = "5417049840",
            DocumentNumber = "ONP_18910_v3",
            DocumentDate = new DateTime(2026, 7, 23),
            Currency = "AOA",
            GrossAmount = 1_492_231.88m,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedByUserId = actorId
        });

        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private static MatchSourceDocumentCandidatesDto NifMismatchProbe() => new()
    {
        SupplierId = null,                          // supplier did NOT resolve — the L4-1 shape
        SupplierName = "CONSULTIT, LDA",
        SupplierTaxId = "5000000000",
        DocumentNumber = "ONP 18910 V3",            // separator styling must not matter
        DocumentDate = new DateTime(2026, 7, 23),
        Currency = "AOA",
        GrossAmount = 1_492_231.88m
    };

    [Fact]
    public async Task An_unresolved_supplier_no_longer_hides_the_candidate()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        await SeedReferenceDocumentAsync(ctx, actorId);

        var result = await BuildController(ctx, actorId).MatchCandidates(NifMismatchProbe());

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<SourceDocumentCandidatesResultDto>(ok.Value);

        Assert.Equal("AMBIGUOUS_MATCH", body.TopClassification);
        var candidate = Assert.Single(body.Candidates);
        Assert.True(candidate.RequestVisible);
        Assert.StartsWith("ZZTEST-MATCH-", candidate.RequestNumber);
        Assert.Contains("SUPPLIER_NIF", candidate.ConflictingFields);
        Assert.Contains("SUPPLIER_NAME", candidate.MatchingFields);
        Assert.Equal("5417049840", candidate.Existing!.SupplierTaxId);
        Assert.Equal(1_492_231.88m, candidate.Existing.GrossAmount);
    }

    [Fact]
    public async Task The_same_commercial_identity_reports_a_strong_business_duplicate()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        await SeedReferenceDocumentAsync(ctx, actorId);

        var probe = NifMismatchProbe();
        probe.SupplierTaxId = "5417049840";                 // NIF agrees → strong supplier identity

        var result = await BuildController(ctx, actorId).MatchCandidates(probe);
        var body = Assert.IsType<SourceDocumentCandidatesResultDto>(
            Assert.IsType<OkObjectResult>(result).Value);

        Assert.Equal("STRONG_BUSINESS_DUPLICATE", body.TopClassification);
        Assert.Equal("AMBIGUOUS", body.Candidates[0].Verdict);   // justified override, never a wall
    }

    [Fact]
    public async Task A_different_total_is_only_a_related_document()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        await SeedReferenceDocumentAsync(ctx, actorId);

        var probe = NifMismatchProbe();
        probe.SupplierTaxId = "5417049840";
        probe.GrossAmount = 3_433_527.55m;                  // the approved CONSULTIT reuse case

        var result = await BuildController(ctx, actorId).MatchCandidates(probe);
        var body = Assert.IsType<SourceDocumentCandidatesResultDto>(
            Assert.IsType<OkObjectResult>(result).Value);

        Assert.Equal("RELATED_DOCUMENT", body.TopClassification);
        Assert.Equal("ALLOW", body.Candidates[0].Verdict);
    }

    [Fact]
    public async Task A_restricted_user_gets_the_signal_without_the_values()
    {
        using var ctx = NewContext();
        var owner = Guid.NewGuid();
        await SeedReferenceDocumentAsync(ctx, owner);

        var restricted = new User { Id = Guid.NewGuid(), FullName = "Restricted", Email = "r@test.local" };
        ctx.Users.Add(restricted);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = restricted.Id, PlantId = 99 });
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, restricted.Id, systemAdministrator: false)
            .MatchCandidates(NifMismatchProbe());
        var body = Assert.IsType<SourceDocumentCandidatesResultDto>(
            Assert.IsType<OkObjectResult>(result).Value);

        var candidate = Assert.Single(body.Candidates);
        Assert.Equal("AMBIGUOUS_MATCH", candidate.Classification);   // the signal survives
        Assert.False(candidate.RequestVisible);
        Assert.Null(candidate.RequestNumber);                        // nothing identifying leaks
        Assert.Null(candidate.RequestId);
        Assert.Null(candidate.DocumentId);
        Assert.Null(candidate.Existing);
    }

    // ── Source-evidence preference: the accepted draft value must never mask what the paper says ──

    [Fact]
    public async Task A_divergent_ocr_date_drives_the_match_not_the_retained_draft_date()
    {
        // The visual L4-3 defect: the draft kept 23/07 (equal to the existing document) while the
        // modified PDF reads 26/07 — comparing the draft value called it "the same identity".
        // The OCR evidence must win: date conflict → AMBIGUOUS_MATCH, never Strong.
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        await SeedReferenceDocumentAsync(ctx, actorId);

        var probe = NifMismatchProbe();
        probe.SupplierId = 77;
        probe.SupplierTaxId = "5417049840";
        probe.DocumentDate = new DateTime(2026, 7, 23);        // accepted draft value (matches)
        probe.OcrDocumentDate = new DateTime(2026, 7, 26);     // what the document actually says

        var body = Assert.IsType<SourceDocumentCandidatesResultDto>(
            Assert.IsType<OkObjectResult>(await BuildController(ctx, actorId).MatchCandidates(probe)).Value);

        Assert.Equal("AMBIGUOUS_MATCH", body.TopClassification);
        Assert.Contains("DOCUMENT_DATE", body.Candidates[0].ConflictingFields);
    }

    [Fact]
    public async Task A_divergent_ocr_total_drives_the_match_not_the_retained_draft_total()
    {
        // The visual L4-5 defect: draft kept 1.492.231,88 while the document reads 1.592.231,88 —
        // the evidence must classify RELATED_DOCUMENT (the approved frictionless L3), not Strong.
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        await SeedReferenceDocumentAsync(ctx, actorId);

        var probe = NifMismatchProbe();
        probe.SupplierId = 77;
        probe.SupplierTaxId = "5417049840";
        probe.GrossAmount = 1_492_231.88m;                     // accepted draft value (matches)
        probe.OcrGrossAmount = 1_592_231.88m;                  // what the document actually says

        var body = Assert.IsType<SourceDocumentCandidatesResultDto>(
            Assert.IsType<OkObjectResult>(await BuildController(ctx, actorId).MatchCandidates(probe)).Value);

        Assert.Equal("RELATED_DOCUMENT", body.TopClassification);
        Assert.Equal("ALLOW", body.Candidates[0].Verdict);
        Assert.Contains("GROSS_AMOUNT", body.Candidates[0].ConflictingFields);
    }

    [Fact]
    public async Task The_documentary_nif_survives_choosing_the_existing_supplier()
    {
        // "Usar fornecedor existente" selects supplier 77 — but the PAPER still says 5000000000.
        // Both identities must reach the engine: the NIF conflict stays visible even though the
        // selected supplier id matches the candidate's.
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        await SeedReferenceDocumentAsync(ctx, actorId);

        var probe = NifMismatchProbe();
        probe.SupplierId = 77;                                 // adopted existing supplier
        probe.SupplierTaxId = "5000000000";                    // documentary snapshot, preserved
        probe.OcrSupplierTaxId = "5000000000";

        var body = Assert.IsType<SourceDocumentCandidatesResultDto>(
            Assert.IsType<OkObjectResult>(await BuildController(ctx, actorId).MatchCandidates(probe)).Value);

        Assert.NotNull(body.TopClassification);
        Assert.Contains("SUPPLIER_NIF", body.Candidates[0].ConflictingFields);
        Assert.Contains("SUPPLIER", body.Candidates[0].MatchingFields);    // id identity still strong
    }

    [Fact]
    public async Task A_genuinely_new_document_produces_no_candidates()
    {
        // Control B: same supplier, new number/date/amount.
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        await SeedReferenceDocumentAsync(ctx, actorId);

        var result = await BuildController(ctx, actorId).MatchCandidates(new MatchSourceDocumentCandidatesDto
        {
            SupplierId = 77,
            SupplierName = "CONSULTIT, LDA",
            SupplierTaxId = "5417049840",
            DocumentNumber = "ONP_20001_v1",
            DocumentDate = new DateTime(2026, 8, 15),
            Currency = "AOA",
            GrossAmount = 250_000m
        });

        var body = Assert.IsType<SourceDocumentCandidatesResultDto>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(body.TopClassification);
        Assert.Empty(body.Candidates);
    }
}
