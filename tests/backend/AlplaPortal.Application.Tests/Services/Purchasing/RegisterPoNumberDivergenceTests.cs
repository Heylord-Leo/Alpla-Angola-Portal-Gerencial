using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
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
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// v2.249.8 — finding 1 of the manual TEST validation of v2.249.7 on REQ-03/11/2026-630 (LUANDAY, P.O. document
/// PO_TEST_LUANDAY_ECF10_2026_9630_REQ630.pdf identified as ECF10 2026/9630, POST_PAID, 61.560 AOA): the Buyer replaced the
/// extracted number with "AAAAAA" and the wizard offered no divergence; the mismatched number was NOT submitted, so the server
/// enforcement was never exercised manually. These tests run the real <c>register-po</c> endpoint (InMemory) on server-held
/// extraction evidence bound to the EXACT file (same SHA-256), for the INITIAL registration (group B, WAITING_PO, no number, no
/// file) and for the CORRECTION (group A, WAITING_PO_CORRECTION, current number ECF10 2026/364):
/// <list type="bullet">
/// <item>a typed number that is not the document's own number → <c>400 OCR_MISMATCH</c> naming both numbers, nothing written
/// (group status / number / current-document pointer / payment condition / responsible buyer / history / files unchanged);</item>
/// <item>the acknowledgement without a justification → <c>400 Ação Bloqueada</c>, nothing written;</item>
/// <item>acknowledgement + justification → registered with the typed number and the divergence audited;</item>
/// <item>the extracted number (any formatting that parses to the same canonical identity) → registered without any divergence;</item>
/// <item>evidence of ANOTHER file is never used: a typed number against a file with no evidence is the UNCERTAIN path
/// (<c>DOCUMENT_TYPE_CONFIRMATION_REQUIRED</c>), not a number divergence.</item>
/// </list>
/// </summary>
public class RegisterPoNumberDivergenceTests
{
    private static RequestsController Buyer(ApplicationDbContext ctx, Guid userId)
    {
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>(); orchestrator.Setup(o => o.EmitAsync(It.IsAny<Domain.Events.WorkflowEvent>())).Returns(Task.CompletedTask);
        var ctl = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, S.AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()));
        ctl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return ctl;
    }

    private const string HashA = "aaaa0000aaaa0000aaaa0000aaaa0000aaaa0000aaaa0000aaaa0000aaaa0000";   // po-a.pdf (group A's returned P.O. file)
    private const string HashB = "bbbb0000bbbb0000bbbb0000bbbb0000bbbb0000bbbb0000bbbb0000bbbb0000";   // group A's replacement file
    private const string HashC = "cccc0000cccc0000cccc0000cccc0000cccc0000cccc0000cccc0000cccc0000";   // group B's first P.O. file (REQ-630 shape)
    private const string HashD = "dddd0000dddd0000dddd0000dddd0000dddd0000dddd0000dddd0000dddd0000";   // another file, never extracted

    private const string IdentifiedNumber = "ECF10 2026/9630";
    private const string IdentifiedTitle = "Encomenda Mat Escritório/Diversos ECF10 2026/9630";

    private static async Task<Guid> AddFileAsync(ApplicationDbContext ctx, S.Seed s, Guid groupId, string hash, string name)
    {
        var id = Guid.NewGuid();
        ctx.RequestAttachments.Add(new RequestAttachment { Id = id, RequestId = s.RequestId, RequestPoGroupId = groupId, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = name, FileExtension = ".pdf", StorageReference = name, FileHash = hash, UploadedByUserId = s.Owner, UploadedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return id;
    }

    /// <summary>Server-held evidence as POST …/po-document-extract persists it (bound to the request and to the file hash).</summary>
    private static async Task EvidenceAsync(ApplicationDbContext ctx, Guid requestId, string hash, string documentNumber, string title, string supplier, decimal total, string currency)
    {
        ctx.PoDocumentExtractions.Add(new PoDocumentExtraction
        {
            RequestId = requestId, FileHash = hash, FileName = "doc.pdf", ExtractedByUserId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow, Success = true,
            DocumentNumber = documentNumber, DocumentTitle = title, SupplierName = supplier, CurrencyCode = currency, GrandTotal = total,
        });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
    }

    private static RegisterPoActionDto Dto(Guid groupId, Guid fileId, string number, string source = "MANUAL") => new()
    {
        PoGroupId = groupId, PoAttachmentId = fileId, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid,
        PurchaseOrderNumber = number, PurchaseOrderNumberSource = source,
    };

    private static ProblemDetails Problem(IActionResult r) => Assert.IsType<ProblemDetails>(((ObjectResult)r).Value);

    private sealed record GroupState(string Status, string? Number, Guid? CurrentFile, string? PaymentCondition, Guid? ResponsibleBuyer, int RequestStatusId, int HistoryCount, int SupersededFiles, int VoidedFiles);

    private static async Task<GroupState> StateAsync(ApplicationDbContext ctx, S.Seed s, Guid groupId)
    {
        ctx.ChangeTracker.Clear();
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == groupId);
        var r = await ctx.Requests.AsNoTracking().SingleAsync(x => x.Id == s.RequestId);
        var files = await ctx.RequestAttachments.AsNoTracking().Where(a => a.RequestId == s.RequestId && a.AttachmentTypeCode == RequestAttachment.TYPE_PO).ToListAsync();
        return new GroupState(g.Status, g.PurchaseOrderNumber, g.CurrentPoAttachmentId, g.PaymentConditionCode, g.PoResponsibleBuyerId, r.StatusId,
            await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId), files.Count(a => a.SupersededAtUtc != null), files.Count(a => a.VoidedAtUtc != null));
    }

    private static async Task AssertNoHistoryAsync(ApplicationDbContext ctx, Guid requestId, params string[] actions)
    {
        foreach (var a in actions)
            Assert.False(await ctx.RequestStatusHistories.AsNoTracking().AnyAsync(h => h.RequestId == requestId && h.ActionTaken == a), $"unexpected history row {a}");
    }

    // ── Initial registration (REQ-630 shape: group WAITING_PO, no number, identified document) ──

    /// <summary>Group B of the seed (WAITING_PO, OUTRO FORNECEDOR, 500 AOA) with its P.O. file C extracted as ECF10 2026/9630 — supplier / amount / currency match, so the ONLY divergence is the number.</summary>
    private static async Task<(S.Seed s, Guid file)> InitialSeedAsync(ApplicationDbContext ctx)
    {
        var s = await S.SeedAsync(ctx);
        var file = await AddFileAsync(ctx, s, s.GroupB, HashC, "PO_TEST_LUANDAY_ECF10_2026_9630_REQ630.pdf");
        await EvidenceAsync(ctx, s.RequestId, HashC, IdentifiedNumber, IdentifiedTitle, "OUTRO FORNECEDOR", 500m, "AOA");
        return (s, file);
    }

    [Fact]
    public async Task Initial_registration_with_a_typed_number_that_is_not_the_documents_number_is_refused_and_nothing_is_written()
    {
        await using var ctx = S.NewCtx();
        var (s, file) = await InitialSeedAsync(ctx);
        var before = await StateAsync(ctx, s, s.GroupB);
        Assert.Equal("WAITING_PO", before.Status); Assert.Null(before.Number); Assert.Null(before.CurrentFile); Assert.Null(before.PaymentCondition);

        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, file, "AAAAAA"));

        var pd = Problem(r);
        Assert.Equal(400, pd.Status);
        Assert.Equal("OCR_MISMATCH", pd.Title);
        Assert.Equal("Número de P.O. divergente: introduzido \"AAAAAA\" (Identificado no documento: ECF10 2026/9630)", pd.Detail);   // the number, and ONLY the number
        Assert.Equal(before, await StateAsync(ctx, s, s.GroupB));
        await AssertNoHistoryAsync(ctx, s.RequestId, "REREGISTER_PO", "PO_NUMBER_CORRECTED");   // group A's seeded REGISTER_PO row is the only one (count unchanged above)
    }

    [Fact]
    public async Task Initial_registration_acknowledged_without_a_justification_is_refused_and_nothing_is_written()
    {
        await using var ctx = S.NewCtx();
        var (s, file) = await InitialSeedAsync(ctx);
        var before = await StateAsync(ctx, s, s.GroupB);

        var d = Dto(s.GroupB, file, "AAAAAA"); d.OverrideConfirmed = true; d.Comment = "   ";
        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d);

        Assert.Equal("Ação Bloqueada", Problem(r).Title);
        Assert.Contains("comentário justificativo é obrigatório", Problem(r).Detail);
        Assert.Equal(before, await StateAsync(ctx, s, s.GroupB));
        await AssertNoHistoryAsync(ctx, s.RequestId, "REREGISTER_PO", "PO_NUMBER_CORRECTED");   // group A's seeded REGISTER_PO row is the only one (count unchanged above)
    }

    [Fact]
    public async Task Initial_registration_acknowledged_and_justified_registers_the_typed_number_and_audits_the_divergence()
    {
        await using var ctx = S.NewCtx();
        var (s, file) = await InitialSeedAsync(ctx);

        var d = Dto(s.GroupB, file, "AAAAAA"); d.OverrideConfirmed = true; d.Comment = "Número da ECF confirmado com Finanças; o documento foi reimpresso com outra referência.";
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d));

        var after = await StateAsync(ctx, s, s.GroupB);
        Assert.Equal("PO_ISSUED", after.Status); Assert.Equal("AAAAAA", after.Number); Assert.Equal(file, after.CurrentFile);
        Assert.Equal(RequestConstants.PaymentConditions.PostPaid, after.PaymentCondition); Assert.Equal(s.Owner, after.ResponsibleBuyer);
        var reg = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO" && h.Comment!.Contains("OUTRO FORNECEDOR"));
        Assert.Contains("Divergência OCR confirmada pelo comprador", reg.Comment);
        Assert.Contains("Número de P.O. divergente: introduzido \"AAAAAA\" (Identificado no documento: ECF10 2026/9630)", reg.Comment);
        Assert.Contains("Justificativa: Número da ECF confirmado com Finanças", reg.Comment);
        Assert.Contains("Documento identificado como P.O. ECF10 2026/9630", reg.Comment);   // identification and number consistency are separate facts
    }

    [Theory]
    [InlineData("ECF10 2026/9630")]
    [InlineData("ecf10 2026-9630")]
    [InlineData("  ECF10   2026/9630 ")]
    public async Task Initial_registration_with_the_extracted_number_in_any_formatting_registers_without_a_divergence(string typed)
    {
        await using var ctx = S.NewCtx();
        var (s, file) = await InitialSeedAsync(ctx);

        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, file, typed, "EXTRACTED")));

        var after = await StateAsync(ctx, s, s.GroupB);
        Assert.Equal("PO_ISSUED", after.Status); Assert.Equal(typed, after.Number); Assert.Equal(file, after.CurrentFile);
        var reg = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO" && h.Comment!.Contains("OUTRO FORNECEDOR"));
        Assert.DoesNotContain("Divergência", reg.Comment);
        Assert.Contains("Documento identificado como P.O. ECF10 2026/9630 (ECF10 — Material diverso / escritório) pela extração", reg.Comment);
    }

    [Fact]
    public async Task Initial_registration_the_evidence_is_bound_to_the_exact_file_a_file_without_evidence_is_uncertain_not_a_number_divergence()
    {
        await using var ctx = S.NewCtx();
        var (s, _) = await InitialSeedAsync(ctx);                      // evidence exists for hash C only
        var other = await AddFileAsync(ctx, s, s.GroupB, HashD, "outro.pdf");
        var before = await StateAsync(ctx, s, s.GroupB);

        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, other, "AAAAAA"));

        var pd = Problem(r);
        Assert.Equal("DOCUMENT_TYPE_CONFIRMATION_REQUIRED", (string)pd.Extensions["code"]!);
        Assert.Equal("NONE", (string)pd.Extensions["evidence"]!);
        Assert.DoesNotContain("Número de P.O. divergente", pd.Detail);
        Assert.Equal(before, await StateAsync(ctx, s, s.GroupB));
    }

    // ── Correction (group A returned for correction, current number ECF10 2026/364, replacement file identified as ECF10 2026/9630) ──

    private static async Task<(S.Seed s, Guid file)> CorrectionSeedAsync(ApplicationDbContext ctx)
    {
        var s = await S.SeedAsync(ctx, groupAStatus: "WAITING_PO_CORRECTION", requestStatus: "WAITING_PO_CORRECTION", legacyReturnReason: "P.O com número errado");
        var a = await ctx.RequestAttachments.SingleAsync(x => x.Id == s.PoAttachmentA); a.FileHash = HashA; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var file = await AddFileAsync(ctx, s, s.GroupA, HashB, "PO_corrigida.pdf");
        await EvidenceAsync(ctx, s.RequestId, HashB, IdentifiedNumber, IdentifiedTitle, "PRODUCTIVE SYSTEMS", 24133.06m, "RND");
        return (s, file);
    }

    [Fact]
    public async Task Correction_with_a_manually_typed_number_that_is_not_the_documents_number_is_refused_and_nothing_is_written()
    {
        await using var ctx = S.NewCtx();
        var (s, file) = await CorrectionSeedAsync(ctx);
        var before = await StateAsync(ctx, s, s.GroupA);
        Assert.Equal("WAITING_PO_CORRECTION", before.Status); Assert.Equal("ECF10 2026/364", before.Number); Assert.Equal(0, before.SupersededFiles);

        var r = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, file, "AAAAAA"));

        Assert.Equal("OCR_MISMATCH", Problem(r).Title);
        Assert.Equal("Número de P.O. divergente: introduzido \"AAAAAA\" (Identificado no documento: ECF10 2026/9630)", Problem(r).Detail);
        Assert.Equal(before, await StateAsync(ctx, s, s.GroupA));                      // number, pointer, status, files, history
        await AssertNoHistoryAsync(ctx, s.RequestId, "REREGISTER_PO", "PO_NUMBER_CORRECTED");
        var old = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.PoAttachmentA);
        Assert.Null(old.SupersededAtUtc); Assert.Null(old.VoidedAtUtc);
    }

    [Fact]
    public async Task Correction_acknowledged_and_justified_registers_the_typed_number_supersedes_the_old_file_and_audits_both()
    {
        await using var ctx = S.NewCtx();
        var (s, file) = await CorrectionSeedAsync(ctx);

        var d = Dto(s.GroupA, file, "AAAAAA"); d.OverrideConfirmed = true; d.Comment = "Referência acordada com Finanças.";
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, d));

        var after = await StateAsync(ctx, s, s.GroupA);
        Assert.Equal("PO_ISSUED", after.Status); Assert.Equal("AAAAAA", after.Number); Assert.Equal(file, after.CurrentFile); Assert.Equal(1, after.SupersededFiles);
        var rereg = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REREGISTER_PO");
        Assert.Contains("Divergência OCR confirmada pelo comprador", rereg.Comment);
        Assert.Contains("Número de P.O. divergente: introduzido \"AAAAAA\" (Identificado no documento: ECF10 2026/9630)", rereg.Comment);
        var corrected = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "PO_NUMBER_CORRECTED");
        Assert.Contains("'ECF10 2026/364' → 'AAAAAA'", corrected.Comment); Assert.Contains("MANUAL", corrected.Comment);
    }

    [Fact]
    public async Task Correction_with_the_extracted_number_registers_without_a_divergence_and_audits_the_number_change()
    {
        await using var ctx = S.NewCtx();
        var (s, file) = await CorrectionSeedAsync(ctx);

        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupA, file, IdentifiedNumber, "EXTRACTED")));

        var after = await StateAsync(ctx, s, s.GroupA);
        Assert.Equal("PO_ISSUED", after.Status); Assert.Equal(IdentifiedNumber, after.Number); Assert.Equal(file, after.CurrentFile);
        var rereg = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REREGISTER_PO");
        Assert.DoesNotContain("Divergência", rereg.Comment);
        Assert.Contains("'ECF10 2026/364' → 'ECF10 2026/9630'", (await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "PO_NUMBER_CORRECTED")).Comment);
    }
}
