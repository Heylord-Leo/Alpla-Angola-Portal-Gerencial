using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// v2.249.4 — the P.O. correction and the P.O. DOCUMENT through the real endpoints (InMemory), on the REQ-28/10/2026-628 shapes.
///
/// <para><b>Trust model.</b> The identification never comes from the client: <c>POST …/po-document-extract</c> persists what the
/// provider read (<see cref="PoDocumentExtraction"/>) bound to the request and to the SHA-256 the server computed; <c>register-po</c>
/// looks that evidence up by the registered attachment's own server-computed hash. Pinned here: tampered / omitted client facts
/// change nothing (the DTO carries none), another file's extraction is not evidence for this file, a file swapped after the
/// extraction has no evidence, evidence of another request is not used, a provider failure is "no usable evidence"; every one
/// of those is UNCERTAIN → explicit confirmation + justification, audited — while a positively identified quotation is refused
/// without any override. Then: number required on a correction, number change audited, document supersession (first correction of
/// a legacy group with two live files, repeated corrections, cancellation after a correction), the evidence-hash guard, duplicate
/// numbers, the stale-number divergence on the exact REQ-628 case, and the extraction endpoint itself.</para>
/// </summary>
public class RegisterPoDocumentIdentificationTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_podoc_" + Guid.NewGuid());
    public RegisterPoDocumentIdentificationTests() => Directory.CreateDirectory(_scratchDir);
    public void Dispose() { try { Directory.Delete(_scratchDir, true); } catch { } }

    private static RequestsController Controller(ApplicationDbContext ctx, Guid userId, string role = RoleConstants.Buyer, IDocumentExtractionService? extraction = null)
    {
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>(); orchestrator.Setup(o => o.EmitAsync(It.IsAny<Domain.Events.WorkflowEvent>())).Returns(Task.CompletedTask);
        var ctl = new RequestsController(ctx, extraction ?? new Mock<IDocumentExtractionService>().Object, S.AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()));
        ctl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, role) }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return ctl;
    }
    private static RequestsController Buyer(ApplicationDbContext ctx, Guid buyerId) => Controller(ctx, buyerId);

    private AttachmentsController Attachments(ApplicationDbContext ctx, Guid actorId)
    {
        var env = new Mock<IWebHostEnvironment>(); env.Setup(e => e.ContentRootPath).Returns(_scratchDir);
        var config = new Mock<IConfiguration>(); config.Setup(c => c[It.IsAny<string>()]).Returns(_scratchDir);
        var security = Options.Create(new SecurityOptions { Upload = new UploadOptions { AllowedExtensions = new List<string> { ".pdf" }, BlockedExtensions = new List<string>(), MaxFileSizeBytes = 10_000_000 } });
        var ctl = new AttachmentsController(ctx, env.Object, security, config.Object);
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")) } };
        return ctl;
    }

    private static readonly byte[] QuotationPdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 Cotacao_TEST_MAQSIS_9003_REQ628");
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));
    private static IFormFile Pdf(byte[] bytes, string name) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

    private const string HashA = "aaaa0000aaaa0000aaaa0000aaaa0000aaaa0000aaaa0000aaaa0000aaaa0000";   // po-a.pdf (the returned P.O. file)
    private const string HashB = "bbbb0000bbbb0000bbbb0000bbbb0000bbbb0000bbbb0000bbbb0000bbbb0000";   // the replacement file

    /// <summary>Group A returned for correction (WAITING_PO_CORRECTION) with its original P.O. "ECF10 2026/364" and file po-a.pdf (hash A).</summary>
    private static async Task<S.Seed> CorrectionSeedAsync(ApplicationDbContext ctx, string? currentNumber = "ECF10 2026/364")
    {
        var s = await S.SeedAsync(ctx, groupAStatus: "WAITING_PO_CORRECTION", requestStatus: "WAITING_PO_CORRECTION", legacyReturnReason: "P.O com número errado", groupAPoNumber: currentNumber);
        var a = await ctx.RequestAttachments.SingleAsync(x => x.Id == s.PoAttachmentA); a.FileHash = HashA; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return s;
    }

    private static RegisterPoActionDto Dto(Guid groupId, string? number) => new() { PoGroupId = groupId, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, PurchaseOrderNumber = number };

    private static async Task<Guid> AddFileAsync(ApplicationDbContext ctx, S.Seed s, string hash, string name = "PO_Ficticia_MAQSIS_9003_REQ628.pdf", Guid? groupId = null, DateTime? uploadedAt = null)
    {
        var id = Guid.NewGuid();
        ctx.RequestAttachments.Add(new RequestAttachment { Id = id, RequestId = s.RequestId, RequestPoGroupId = groupId ?? s.GroupA, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = name, FileExtension = ".pdf", StorageReference = name, FileHash = hash, UploadedByUserId = s.Owner, UploadedAtUtc = uploadedAt ?? DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return id;
    }

    /// <summary>Server-held evidence as POST …/po-document-extract would persist it (the tests never go through the client).</summary>
    private static async Task<Guid> EvidenceAsync(ApplicationDbContext ctx, Guid requestId, string hash, string? documentNumber, string? title = null, string? classification = null, decimal? confidence = null, bool success = true, string? supplier = "PRODUCTIVE SYSTEMS", decimal? total = 24133.06m, string? currency = "RND", Guid? actor = null)
    {
        var e = new PoDocumentExtraction
        {
            RequestId = requestId, FileHash = hash, FileName = "doc.pdf", ExtractedByUserId = actor ?? Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow, Success = success,
            DocumentNumber = documentNumber, DocumentTitle = title, ClassificationType = classification, ClassificationConfidence = confidence,
            SupplierName = supplier, CurrencyCode = currency, GrandTotal = total,
        };
        ctx.PoDocumentExtractions.Add(e); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return e.Id;
    }

    private static ProblemDetails Problem(IActionResult r) => Assert.IsType<ProblemDetails>(((ObjectResult)r).Value);
    private static string Code(IActionResult r) => (string)Problem(r).Extensions["code"]!;
    private static async Task<RequestStatusHistory> Rereg(ApplicationDbContext ctx, Guid requestId) => await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == requestId && h.ActionTaken == "REREGISTER_PO");

    // ── Trust model ──

    [Fact]
    public async Task Without_server_evidence_the_document_is_uncertain_and_the_client_cannot_supply_facts()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        // The DTO has no extraction fields any more: there is nothing a client could tamper with. Confirmation alone is refused without a justification.
        var refused = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF10 2026/9003"));
        Assert.Equal("DOCUMENT_TYPE_CONFIRMATION_REQUIRED", Code(refused));
        Assert.Equal("NO_EXTRACTION", Problem(refused).Extensions["reasonCode"]); Assert.Equal("NONE", Problem(refused).Extensions["evidence"]);
        var confirmedNoComment = Dto(s.GroupA, "ECF10 2026/9003"); confirmedNoComment.DocumentTypeConfirmed = true;
        Assert.Equal("DOCUMENT_TYPE_CONFIRMATION_REQUIRED", Code(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, confirmedNoComment)));
        Assert.Null(typeof(RegisterPoActionDto).GetProperty("ExtractedDocumentNumber"));   // no client-fact channel exists
        Assert.Null(typeof(RegisterPoActionDto).GetProperty("ExtractionOutcome"));
        var ok = Dto(s.GroupA, "ECF10 2026/9003"); ok.DocumentTypeConfirmed = true; ok.Comment = "OCR indisponível; P.O. conferida com o documento impresso.";
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, ok));
        ctx.ChangeTracker.Clear();
        Assert.Contains("sem extração registada para este ficheiro", (await Rereg(ctx, s.RequestId)).Comment);
    }

    [Fact]
    public async Task Evidence_of_another_file_or_of_another_request_is_never_used_for_this_file()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        // The Buyer extracted a genuine P.O. (hash B) but registers po-a.pdf (hash A): no evidence for A.
        await EvidenceAsync(ctx, s.RequestId, HashB, "ECF10 2026/9003", "Encomenda Mat Escritório/Diversos ECF10 2026/9003");
        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF10 2026/9003"));
        Assert.Equal("DOCUMENT_TYPE_CONFIRMATION_REQUIRED", Code(r)); Assert.Equal("NONE", Problem(r).Extensions["evidence"]);
        // Evidence for the SAME bytes produced under another request is not this request's evidence either.
        await EvidenceAsync(ctx, Guid.NewGuid(), HashA, "ECF10 2026/9003", "Encomenda ECF10 2026/9003");
        var r2 = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF10 2026/9003"));
        Assert.Equal("DOCUMENT_TYPE_CONFIRMATION_REQUIRED", Code(r2));
        // Evidence bound to A under this request → identified → registers.
        await EvidenceAsync(ctx, s.RequestId, HashA, "ECF10 2026/9003", "Encomenda ECF10 2026/9003");
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF10 2026/9003")));
    }

    [Fact]
    public async Task A_file_swapped_after_the_extraction_has_no_evidence_and_the_identified_file_keeps_its_own()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        await EvidenceAsync(ctx, s.RequestId, HashA, "ECF10 2026/9003", "Encomenda ECF10 2026/9003");   // extracted po-a.pdf
        var swapped = await AddFileAsync(ctx, s, HashB);                                                 // then uploaded another file
        var d = Dto(s.GroupA, "ECF10 2026/9003"); d.PoAttachmentId = swapped;
        Assert.Equal("DOCUMENT_TYPE_CONFIRMATION_REQUIRED", Code(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d)));
        var ok = Dto(s.GroupA, "ECF10 2026/9003"); ok.PoAttachmentId = s.PoAttachmentA;
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, ok));
    }

    [Fact]
    public async Task A_document_identified_as_a_quotation_by_the_server_evidence_is_refused_with_every_override()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        await EvidenceAsync(ctx, s.RequestId, HashA, "TEST-MAQSIS-2026-9003", "Cotação", "PROFORMA", 0.92m);
        var dto = Dto(s.GroupA, "TEST-MAQSIS-2026-9003");
        dto.OverrideConfirmed = true; dto.DocumentTypeConfirmed = true; dto.OverrideDuplicateConfirmed = true; dto.Comment = "Fornecedor e valor coincidem, registar na mesma.";
        dto.ExtractedSupplierName = "PRODUCTIVE SYSTEMS"; dto.ExtractedTotalAmount = 24133.06m;   // client business facts prove nothing
        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, dto);
        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Equal("DOCUMENT_NOT_PURCHASE_ORDER", Code(r));
        Assert.Contains("mesmo que o fornecedor e o valor coincidam", Problem(r).Detail);
        ctx.ChangeTracker.Clear();
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupA);
        Assert.Equal("WAITING_PO_CORRECTION", g.Status); Assert.Equal("ECF10 2026/364", g.PurchaseOrderNumber); Assert.Null(g.CurrentPoAttachmentId);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REREGISTER_PO"));
    }

    [Fact]
    public async Task Uncertain_evidence_synthetic_ECF99_needs_explicit_confirmation_and_justification_and_is_audited()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        var evidenceId = await EvidenceAsync(ctx, s.RequestId, HashA, "ECF99 2026/9003", "Encomenda a Fornecedor ECF99 2026/9003");
        var refused = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF99 2026/9003"));
        Assert.Equal("DOCUMENT_TYPE_CONFIRMATION_REQUIRED", Code(refused));
        Assert.Equal("UNRECOGNISED_IDENTIFIER", Problem(refused).Extensions["reasonCode"]); Assert.Equal("UNCERTAIN", Problem(refused).Extensions["evidence"]);
        var ok = Dto(s.GroupA, "ECF99 2026/9003"); ok.DocumentTypeConfirmed = true; ok.Comment = "Documento de teste emitido com a série fictícia ECF99; confirmado como P.O.";
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, ok));
        ctx.ChangeTracker.Clear();
        var rereg = await Rereg(ctx, s.RequestId);
        Assert.Contains("NÃO identificado automaticamente (UNRECOGNISED_IDENTIFIER; extração " + evidenceId.ToString().Substring(0, 8), rereg.Comment);
        Assert.Contains("confirmado manualmente pelo comprador", rereg.Comment);
    }

    [Fact]
    public async Task A_provider_failure_is_no_usable_evidence()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        await EvidenceAsync(ctx, s.RequestId, HashA, null, success: false);
        var refused = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF10 2026/9003"));
        Assert.Equal("DOCUMENT_TYPE_CONFIRMATION_REQUIRED", Code(refused)); Assert.Equal("NO_EXTRACTION", Problem(refused).Extensions["reasonCode"]); Assert.Equal("FAILED", Problem(refused).Extensions["evidence"]);
    }

    // ── Number ──

    [Fact]
    public async Task A_correction_without_a_number_is_refused_and_nothing_changes()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        await EvidenceAsync(ctx, s.RequestId, HashA, "ECF10 2026/364", "Encomenda ECF10 2026/364");
        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, null));
        Assert.Equal("PO_NUMBER_REQUIRED", Code(r));
        ctx.ChangeTracker.Clear();
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupA);
        Assert.Equal("WAITING_PO_CORRECTION", g.Status); Assert.Equal("ECF10 2026/364", g.PurchaseOrderNumber);
    }

    [Fact]
    public async Task REQ_628_the_stale_quotation_number_cannot_survive_a_correction_whose_document_reads_another_number()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx, currentNumber: "TEST-MAQSIS-2026-9003");
        var newFile = await AddFileAsync(ctx, s, HashB);
        await EvidenceAsync(ctx, s.RequestId, HashB, "ECF99 2026/9003", "Encomenda a Fornecedor ECF99 2026/9003");   // UNCERTAIN, but the number was read
        RegisterPoActionDto Keep()
        {
            var d = Dto(s.GroupA, "TEST-MAQSIS-2026-9003"); d.PoAttachmentId = newFile; d.DocumentTypeConfirmed = true; d.Comment = "PDF de teste com série fictícia ECF99; confirmado como P.O."; d.PurchaseOrderNumberSource = "CURRENT";
            return d;
        }
        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Keep());
        Assert.Equal("OCR_MISMATCH", Problem(r).Title);
        Assert.Contains("Número de P.O. divergente", Problem(r).Detail); Assert.Contains("Lido no documento: ECF99 2026/9003", Problem(r).Detail);
        ctx.ChangeTracker.Clear();
        Assert.Equal("TEST-MAQSIS-2026-9003", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupA)).PurchaseOrderNumber);   // nothing written

        var keptOnPurpose = Keep(); keptOnPurpose.OverrideConfirmed = true;
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, keptOnPurpose));   // explicit, audited override
        ctx.ChangeTracker.Clear();
        Assert.Contains("Divergência OCR confirmada", (await Rereg(ctx, s.RequestId)).Comment);
    }

    [Fact]
    public async Task REQ_628_the_detected_number_replaces_the_quotation_number_and_the_change_is_audited()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx, currentNumber: "TEST-MAQSIS-2026-9003");
        var newFile = await AddFileAsync(ctx, s, HashB);
        await EvidenceAsync(ctx, s.RequestId, HashB, "ECF99 2026/9003", "Encomenda a Fornecedor ECF99 2026/9003");
        var d = Dto(s.GroupA, "ECF99 2026/9003"); d.PoAttachmentId = newFile; d.DocumentTypeConfirmed = true; d.Comment = "PDF de teste com série fictícia ECF99; confirmado como P.O."; d.PurchaseOrderNumberSource = "EXTRACTED";
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d));
        ctx.ChangeTracker.Clear();
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupA);
        Assert.Equal("PO_ISSUED", g.Status); Assert.Equal("ECF99 2026/9003", g.PurchaseOrderNumber); Assert.Equal(newFile, g.CurrentPoAttachmentId);
        var change = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "PO_NUMBER_CORRECTED");
        Assert.Contains("'TEST-MAQSIS-2026-9003' → 'ECF99 2026/9003'", change.Comment); Assert.Contains("EXTRACTED", change.Comment);
        Assert.Contains("PO_Ficticia_MAQSIS_9003_REQ628.pdf", change.Comment); Assert.Contains("po-a.pdf", change.Comment);
        var old = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.PoAttachmentA);
        Assert.NotNull(old.SupersededAtUtc); Assert.Equal(newFile, old.SupersededByAttachmentId); Assert.Null(old.VoidedAtUtc); Assert.False(old.IsDeleted);
    }

    [Fact]
    public async Task A_stale_number_that_differs_from_an_IDENTIFIED_document_is_an_OCR_divergence_under_the_override_policy()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        await EvidenceAsync(ctx, s.RequestId, HashA, "ECF10 2026/9003", "Encomenda Mat Escritório/Diversos ECF10 2026/9003");
        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF10 2026/364"));
        Assert.Equal("OCR_MISMATCH", Problem(r).Title); Assert.Contains("Identificado no documento: ECF10 2026/9003", Problem(r).Detail);
        var overridden = Dto(s.GroupA, "ECF10 2026/364"); overridden.OverrideConfirmed = true; overridden.Comment = "Número mantido por decisão do comprador.";
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, overridden));
    }

    [Fact]
    public async Task A_correction_that_keeps_the_current_number_matching_the_document_writes_no_number_change()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        await EvidenceAsync(ctx, s.RequestId, HashA, "ECF10 2026/364", "Encomenda Mat Escritório/Diversos ECF10 2026/364");
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF10 2026/364")));
        ctx.ChangeTracker.Clear();
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "PO_NUMBER_CORRECTED"));
        Assert.Equal(s.PoAttachmentA, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupA)).CurrentPoAttachmentId);
        Assert.Contains("Documento identificado como P.O. ECF10 2026/364 (ECF10 — Material diverso / escritório) pela extração", (await Rereg(ctx, s.RequestId)).Comment);
    }

    [Fact]
    public async Task A_duplicate_number_on_a_correction_still_needs_the_duplicate_confirmation_and_justification()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        await EvidenceAsync(ctx, s.RequestId, HashA, "ECF10 2026/9003", "Encomenda ECF10 2026/9003");
        var b = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB); b.PurchaseOrderNumber = "ECF10 2026/9003"; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.Equal("DUPLICATE_PO", Problem(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, "ECF10 2026/9003"))).Title);
        var d = Dto(s.GroupA, "ECF10 2026/9003"); d.OverrideDuplicateConfirmed = true; d.DuplicateOverrideComment = "Número confirmado com o ERP; o grupo B tinha o número errado.";
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d));
    }

    // ── Supersession on legacy data ──

    [Fact]
    public async Task First_correction_of_a_legacy_group_with_two_live_files_and_no_pointer_supersedes_both_and_points_at_the_new_file()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx, currentNumber: "TEST-MAQSIS-2026-9003");
        // REQ-628 after deployment: two live P.O. files, no pointer, no supersession stamps.
        var olderQuotationAsPo = s.PoAttachmentA;   // uploaded first
        var ficticia = await AddFileAsync(ctx, s, "cccc0000cccc0000cccc0000cccc0000cccc0000cccc0000cccc0000cccc0000", "PO_Ficticia_MAQSIS_9003_REQ628.pdf", uploadedAt: DateTime.UtcNow.AddMinutes(-5));
        // The legacy fallback picks the newest live file (the same rule the request page / correction modal / Print View read).
        var files = await ctx.RequestAttachments.AsNoTracking().Where(a => a.RequestId == s.RequestId).ToListAsync();
        var candidates = files.Select(a => new PoDocumentSelection.Candidate(a.Id, a.RequestPoGroupId, a.AttachmentTypeCode, a.UploadedAtUtc, a.IsDeleted, a.VoidedAtUtc, a.SupersededAtUtc)).ToList();
        Assert.Equal(ficticia, PoDocumentSelection.ResolveCurrentPoAttachmentId(s.GroupA, null, candidates));
        Assert.Null(PoDocumentSelection.ResolveCurrentPoAttachmentId(s.GroupB, null, candidates));                       // never across groups
        Assert.Equal(olderQuotationAsPo, PoDocumentSelection.ResolveCurrentPoAttachmentId(s.GroupA, olderQuotationAsPo, candidates));
        Assert.Equal(ficticia, PoDocumentSelection.ResolveCurrentPoAttachmentId(s.GroupA, Guid.NewGuid(), candidates));  // a dangling pointer falls back

        // First correction with a third file: both legacy files are superseded, the pointer names the new file.
        var third = await AddFileAsync(ctx, s, HashB, "PO_ECF10_2026_9003.pdf");
        await EvidenceAsync(ctx, s.RequestId, HashB, "ECF10 2026/9003", "Encomenda Mat Escritório/Diversos ECF10 2026/9003");
        var d = Dto(s.GroupA, "ECF10 2026/9003"); d.PoAttachmentId = third;
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d));
        ctx.ChangeTracker.Clear();
        Assert.Equal(third, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).CurrentPoAttachmentId);
        foreach (var id in new[] { olderQuotationAsPo, ficticia })
        {
            var a = await ctx.RequestAttachments.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.NotNull(a.SupersededAtUtc); Assert.Equal(third, a.SupersededByAttachmentId); Assert.Null(a.VoidedAtUtc); Assert.False(a.IsDeleted);
        }
        Assert.Null((await ctx.RequestAttachments.AsNoTracking().SingleAsync(x => x.Id == third)).SupersededAtUtc);
    }

    [Fact]
    public async Task Repeated_corrections_chain_the_supersession_and_a_cancellation_after_a_correction_voids_the_files_and_removes_every_current_document()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        // Correction 1 → file 2
        var f2 = await AddFileAsync(ctx, s, HashB, "po-2.pdf");
        await EvidenceAsync(ctx, s.RequestId, HashB, "ECF10 2026/365", "Encomenda ECF10 2026/365");
        var d1 = Dto(s.GroupA, "ECF10 2026/365"); d1.PoAttachmentId = f2;
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d1));
        ctx.ChangeTracker.Clear();
        // Finance returns again (status only, as FinanceController does) → Correction 2 → file 3
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupA); g.Status = "WAITING_PO_CORRECTION"; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        const string hash3 = "dddd0000dddd0000dddd0000dddd0000dddd0000dddd0000dddd0000dddd0000";
        var f3 = await AddFileAsync(ctx, s, hash3, "po-3.pdf");
        await EvidenceAsync(ctx, s.RequestId, hash3, "ECF10 2026/366", "Encomenda ECF10 2026/366");
        var d2 = Dto(s.GroupA, "ECF10 2026/366"); d2.PoAttachmentId = f3;
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d2));
        ctx.ChangeTracker.Clear();
        var a1 = await ctx.RequestAttachments.AsNoTracking().SingleAsync(x => x.Id == s.PoAttachmentA);
        var a2 = await ctx.RequestAttachments.AsNoTracking().SingleAsync(x => x.Id == f2);
        var a3 = await ctx.RequestAttachments.AsNoTracking().SingleAsync(x => x.Id == f3);
        Assert.Equal(f2, a1.SupersededByAttachmentId);   // the first stamp is history: not rewritten by the second correction
        Assert.Equal(f3, a2.SupersededByAttachmentId);
        Assert.Null(a3.SupersededAtUtc);
        Assert.Equal(f3, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupA)).CurrentPoAttachmentId);
        Assert.Equal(2, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "PO_NUMBER_CORRECTED"));

        // Cancellation after the corrections (restart): the files are voided (existing behaviour), the pointer still exists but names
        // a voided file → no current document; the obligations read model treats the group as history regardless of any document.
        var (orch, _) = S.OrchestratorMock();
        var cancel = await S.Service(ctx, orch.Object).CancelAsync(S.Cancel(s, S.Buyer(s.Owner)));
        Assert.True(cancel.Succeeded, cancel.Detail);
        ctx.ChangeTracker.Clear();
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupA);
        Assert.Equal("CANCELLED", group.Status); Assert.Equal(f3, group.CurrentPoAttachmentId);
        var files = await ctx.RequestAttachments.AsNoTracking().Where(a => a.RequestId == s.RequestId && a.AttachmentTypeCode == RequestAttachment.TYPE_PO).ToListAsync();
        Assert.All(files, f => Assert.NotNull(f.VoidedAtUtc));
        Assert.Null(PoDocumentSelection.ResolveCurrentPoAttachmentId(s.GroupA, group.CurrentPoAttachmentId,
            files.Select(a => new PoDocumentSelection.Candidate(a.Id, a.RequestPoGroupId, a.AttachmentTypeCode, a.UploadedAtUtc, a.IsDeleted, a.VoidedAtUtc, a.SupersededAtUtc))));
        var obligation = Assert.Single(OperationInvoiceObligationProjector.Project(new[] { new OperationInvoiceObligationGroupSnapshot
        {
            GroupId = group.Id, SourceDocumentType = "PROFORMA", ExpectedTotal = group.TotalAmount, PersistedStatus = "PENDING_UPLOAD", GroupStatus = group.Status, CurrencyCode = group.CurrencyCode,
        } }).Obligations);
        Assert.True(obligation.GroupCancelled); Assert.False(obligation.RequiresOperationInvoice);
    }

    // ── Evidence-hash guard (independent of any extraction) ──

    [Fact]
    public async Task A_PO_file_identical_to_the_requests_quotation_document_is_refused_at_registration_and_at_upload()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        var hash = Sha256(QuotationPdfBytes);
        ctx.RequestAttachments.Add(new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, AttachmentTypeCode = RequestAttachment.TYPE_QUOTATION, FileName = "Cotacao_TEST_MAQSIS_9003_REQ628.pdf", FileExtension = ".pdf", StorageReference = "q.pdf", FileHash = hash, UploadedByUserId = s.Owner, UploadedAtUtc = DateTime.UtcNow.AddDays(-2) });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var twinId = await AddFileAsync(ctx, s, hash);
        await EvidenceAsync(ctx, s.RequestId, hash, "ECF10 2026/9003", "Encomenda ECF10 2026/9003");   // even an identified reading does not help: the bytes ARE the quotation
        var d = Dto(s.GroupA, "ECF10 2026/9003"); d.PoAttachmentId = twinId;
        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d);
        Assert.Equal("DOCUMENT_IS_QUOTATION_EVIDENCE", Code(r)); Assert.Contains("Cotacao_TEST_MAQSIS_9003_REQ628.pdf", Problem(r).Detail);
        var before = await ctx.RequestAttachments.CountAsync();
        var up = await Attachments(ctx, s.Owner).Upload(s.RequestId, new List<IFormFile> { Pdf(QuotationPdfBytes, "quotation-as-po.pdf") }, null, "PO", s.GroupA);
        Assert.Equal("DOCUMENT_IS_QUOTATION_EVIDENCE", Code(up));
        Assert.Equal(before, await ctx.RequestAttachments.CountAsync());
        Assert.IsType<OkObjectResult>(await Attachments(ctx, s.Owner).Upload(s.RequestId, new List<IFormFile> { Pdf(Encoding.UTF8.GetBytes("%PDF-1.4 ECF10 2026/9003"), "po.pdf") }, null, "PO", s.GroupA));
    }

    [Fact]
    public async Task An_unknown_attachment_id_is_refused_before_any_write()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        var d = Dto(s.GroupA, "ECF10 2026/9003"); d.PoAttachmentId = Guid.NewGuid();
        Assert.Equal("PO_ATTACHMENT_INVALID", Code(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d)));
    }

    // ── The extraction endpoint (where the evidence is born) ──

    private static Mock<IDocumentExtractionService> Provider(ExtractionResultDto? result, Exception? throws = null)
    {
        var m = new Mock<IDocumentExtractionService>();
        var setup = m.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()));
        if (throws != null) setup.ThrowsAsync(throws); else setup.ReturnsAsync(result!);
        return m;
    }

    [Fact]
    public async Task The_extract_endpoint_persists_the_servers_reading_bound_to_the_request_and_to_the_hash_it_computed()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 Encomenda Mat Escritório/Diversos ECF10 2026/9003");
        var provider = Provider(new ExtractionResultDto
        {
            Success = true, QualityScore = 0.95m,
            Header = new ExtractionHeaderDto { DocumentNumber = "ECF10 2026/9003", DocumentTitle = "Encomenda Mat Escritório/Diversos ECF10 2026/9003", SupplierName = "PRODUCTIVE SYSTEMS", Currency = "RND", GrandTotal = 24133.06m, ReferencedDocumentNumbers = new List<string> { "ECF10 2026/364" }, DocumentClassificationType = "OTHER", DocumentClassificationConfidence = 0.3m },
        });
        var result = await Controller(ctx, s.Owner, extraction: provider.Object).PoDocumentExtract(s.RequestId, Pdf(bytes, "PO_ECF10_2026_9003.pdf"));
        var body = Assert.IsType<OcrExtractionResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("IDENTIFIED_PO", body.Integration!.HeaderSuggestions!.PurchaseOrderIdentification!.Verdict);
        ctx.ChangeTracker.Clear();
        var row = await ctx.PoDocumentExtractions.AsNoTracking().SingleAsync();
        Assert.Equal(s.RequestId, row.RequestId); Assert.Equal(Sha256(bytes), row.FileHash); Assert.Equal(s.Owner, row.ExtractedByUserId); Assert.True(row.Success);
        Assert.Equal("ECF10 2026/9003", row.DocumentNumber); Assert.Equal("IDENTIFIED_PO", row.Verdict); Assert.Equal("ECF10", row.Family);
        Assert.Equal(new[] { "ECF10 2026/364" }, row.ReferencedDocumentNumbers()); Assert.Equal(24133.06m, row.GrandTotal); Assert.Equal("RND", row.CurrencyCode);
        // The persisted evidence drives the registration of a file with the SAME bytes (hash computed again by the upload).
        var upload = await Attachments(ctx, s.Owner).Upload(s.RequestId, new List<IFormFile> { Pdf(bytes, "PO_ECF10_2026_9003.pdf") }, null, "PO", s.GroupA);
        Assert.IsType<OkObjectResult>(upload);
        ctx.ChangeTracker.Clear();
        var uploaded = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.FileName == "PO_ECF10_2026_9003.pdf");
        Assert.Equal(row.FileHash, uploaded.FileHash);
        var d = Dto(s.GroupA, "ECF10 2026/9003"); d.PoAttachmentId = uploaded.Id;
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d));
        ctx.ChangeTracker.Clear();
        Assert.Contains("pela extração " + row.Id.ToString().Substring(0, 8), (await Rereg(ctx, s.RequestId)).Comment);
    }

    [Fact]
    public async Task The_extract_endpoint_is_scoped_Buyer_or_SysAdmin_only_and_persists_nothing_on_a_provider_failure()
    {
        await using var ctx = S.NewCtx();
        var s = await CorrectionSeedAsync(ctx);
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 x");
        var okProvider = Provider(new ExtractionResultDto { Success = true, Header = new ExtractionHeaderDto { DocumentNumber = "ECF10 2026/1" } });
        Assert.IsType<ObjectResult>((await Controller(ctx, s.Finance1, RoleConstants.Finance, okProvider.Object).PoDocumentExtract(s.RequestId, Pdf(bytes, "a.pdf"))).Result);
        Assert.Equal(403, ((ObjectResult)(await Controller(ctx, s.Finance1, RoleConstants.Finance, okProvider.Object).PoDocumentExtract(s.RequestId, Pdf(bytes, "a.pdf"))).Result!).StatusCode);
        Assert.IsType<NotFoundObjectResult>((await Controller(ctx, s.Owner, extraction: okProvider.Object).PoDocumentExtract(Guid.NewGuid(), Pdf(bytes, "a.pdf"))).Result);
        var failing = Provider(null, new InvalidOperationException("provider down"));
        var r = await Controller(ctx, s.Owner, extraction: failing.Object).PoDocumentExtract(s.RequestId, Pdf(bytes, "a.pdf"));
        Assert.Equal(500, ((ObjectResult)r.Result!).StatusCode);
        Assert.Equal(0, await ctx.PoDocumentExtractions.CountAsync());
        // An envelope with success=false IS persisted (as a failed reading) and never identifies a document.
        var empty = Provider(new ExtractionResultDto { Success = false, Header = new ExtractionHeaderDto() });
        Assert.IsType<OkObjectResult>((await Controller(ctx, s.Owner, extraction: empty.Object).PoDocumentExtract(s.RequestId, Pdf(bytes, "a.pdf"))).Result);
        Assert.False((await ctx.PoDocumentExtractions.AsNoTracking().SingleAsync()).Success);
    }
}
