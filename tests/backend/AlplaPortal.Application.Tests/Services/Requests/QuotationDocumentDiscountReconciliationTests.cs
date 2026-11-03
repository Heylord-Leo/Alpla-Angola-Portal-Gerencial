using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Validation;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.249.7 — REQ-03/11/2026-630 (synthetic fixture K, LUANDAY NIF 5417010871, document TEST-K-2026-0630): a document-level
/// (summary-only) discount redistributed over the lines must reconcile to ZERO through the real endpoints (preview + SaveQuotation),
/// without a residual justification, while genuine discrepancies still answer 409. EF InMemory; no migration involved (the header
/// baseline travels in the DTO like OcrTotal and is not persisted).
/// </summary>
public class QuotationDocumentDiscountReconciliationTests
{
    private const int IVA14 = 14;
    private const int IVA5 = 5;
    private const int UNIT = 2;

    private static ApplicationDbContext NewCtx() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed record Seed(Guid RequestId, Guid Line1, Guid Line2, Guid ActorId, int SupplierId);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = Guid.NewGuid();
        ctx.Users.Add(new User { Id = actor, FullName = "Tester", Email = "t@t.co", IsActive = true });
        var type = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        var status = new RequestStatus { Id = 1, Code = "WAITING_QUOTATION", Name = "Aguardando" };
        ctx.RequestTypes.Add(type); ctx.RequestStatuses.Add(status);
        ctx.Suppliers.Add(new Supplier { Id = 7, Name = "LUANDAY - COMERCIO E SERVIÇOS, LDA", TaxId = "5417010871" });
        ctx.IvaRates.Add(new IvaRate { Id = IVA14, RatePercent = 14m, Name = "IVA 14%" });
        ctx.IvaRates.Add(new IvaRate { Id = IVA5, RatePercent = 5m, Name = "IVA 5%" });
        ctx.Units.Add(new Unit { Id = UNIT, Code = "UN", Name = "Unidade", AllowsDecimalQuantity = false });

        var request = new Request
        {
            Id = Guid.NewGuid(), Title = "TEST v2.249.0 – K – desconto só no resumo", StatusId = 1, Status = status, RequestTypeId = 1, RequestType = type,
            DepartmentId = 1, CompanyId = 1, PlantId = 1, RequesterId = actor, CreatedAtUtc = DateTime.UtcNow
        };
        var li1 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1, Description = "Resma de Papel A4", Quantity = 10, IsDeleted = false };
        var li2 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 2, Description = "Lapiseira azul", Quantity = 20, IsDeleted = false };
        request.LineItems.Add(li1); request.LineItems.Add(li2);
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, li1.Id, li2.Id, actor, 7);
    }

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var adminLog = new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance);
        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object, adminLog, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object,
            new Mock<ILineItemFactory>().Object, new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Microsoft.Extensions.Options.Options.Create(new AlplaPortal.Domain.Configuration.PostPaymentCompletionOptions()));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
            new Claim(ClaimTypes.Role, RoleConstants.SystemAdministrator)
        }, "Test");
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return controller;
    }

    /// <summary>An OCR line exactly as the wizard sends it: immutable baseline (document prints the NET line total, no line
    /// discount, extracted rate) plus the Buyer's final values.</summary>
    private static SaveQuotationItemDto OcrLine(int line, Guid mapped, string description, decimal qty, decimal price, decimal ocrNetLineTotal,
        int ivaId, decimal ocrIvaPercent, decimal finalDiscount, string? reason, decimal ocrDiscount = 0m)
        => new()
        {
            LineNumber = line, Description = description, Quantity = qty, UnitPrice = price, UnitId = UNIT, IvaRateId = ivaId,
            DiscountAmount = finalDiscount, MappedRequestLineItemId = mapped, ReconciliationStatus = "MAPPED",
            LineOrigin = QuotationLineOrigins.Ocr,
            OcrOriginalQuantity = qty, OcrOriginalUnitPrice = price, OcrOriginalDiscountAmount = ocrDiscount,
            OcrOriginalIvaRatePercent = ocrIvaPercent, OcrOriginalUnitId = UNIT, OcrOriginalLineTotal = ocrNetLineTotal,
            LineAdjustmentJustification = reason
        };

    private const string AllocationReason = "Desconto comercial global de 6.000,00 AOA distribuído proporcionalmente ao valor líquido da linha.";

    /// <summary>Fixture K payload in the frontend shape (no SourceType; `ocrTotal` + the v2.249.7 header-discount baseline).</summary>
    private static SaveQuotationRequestDto KDto(Seed s, decimal ocrTotal, decimal? ocrDocumentDiscount, decimal globalDiscount, decimal disc1, decimal disc2, bool reasons)
        => new()
        {
            SupplierId = s.SupplierId, SupplierNameSnapshot = "LUANDAY - COMERCIO E SERVIÇOS, LDA", Currency = "AOA",
            DocumentNumber = "TEST-K-2026-0630", DocumentType = "PROFORMA",
            OcrTotal = ocrTotal, OcrDocumentDiscountAmount = ocrDocumentDiscount, DiscountAmount = globalDiscount,
            Items = new List<SaveQuotationItemDto>
            {
                OcrLine(1, s.Line1, "Resma de Papel A4", 10, 5_000, 50_000, IVA14, 14, disc1, reasons ? AllocationReason : null),
                OcrLine(2, s.Line2, "Lapiseira azul", 20, 500, 10_000, IVA14, 14, disc2, reasons ? AllocationReason : null),
            }
        };

    private static async Task<QuotationReconciliationDto> PreviewAsync(RequestsController c, Seed s, SaveQuotationRequestDto dto)
    {
        var result = await c.PreviewQuotationReconciliation(s.RequestId, null, dto, CancellationToken.None);
        return Assert.IsType<QuotationReconciliationDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact] // The reproduced TEST case: allocation done, both lines mapped → residual 0, no justification, saved, line-resolved
    public async Task FixtureK_AllocatedToLines_PreviewResidualZero_SavesWithoutJustification()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var dto = KDto(s, ocrTotal: 61_560m, ocrDocumentDiscount: 6_000m, globalDiscount: 0m, disc1: 5_000m, disc2: 1_000m, reasons: true);

        var preview = await PreviewAsync(controller, s, dto);
        Assert.Equal(61_560m, preview.OcrHeaderTotal);
        Assert.Equal(61_560m, preview.FinalConsideredTotal);
        Assert.Equal(60_000m, preview.OcrLineSumTotal);
        Assert.Equal(68_400m, preview.ReconstructedOcrLineSum);
        Assert.Equal(1_560m, preview.StructuralHeaderDifference);
        Assert.Equal(-8_400m, preview.OcrLineComponentDifference);
        Assert.Equal(-6_840m, preview.DiscountImpact);
        Assert.Equal(-6_840m, preview.ExplainedLineAdjustments);
        Assert.Equal(6_000m, preview.OcrDocumentDiscount);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.DocumentLevel, preview.OcrDocumentDiscountKind);
        Assert.Equal(-6_840m, preview.DocumentSummaryDiscountCredit);
        Assert.Equal(0m, preview.ResidualVariance);
        Assert.False(preview.ResidualExceedsTolerance);
        Assert.All(preview.Lines, l => { Assert.True(l.DiscountChanged); Assert.True(l.HasAdjustmentReason); });
        // Purity: the preview wrote nothing.
        Assert.Empty(await ctx.Quotations.ToListAsync());
        Assert.Empty(await ctx.RequestStatusHistories.ToListAsync());

        // A second preview ("Recalcular resumo") is the same pure computation.
        var again = await PreviewAsync(controller, s, dto);
        Assert.Equal(preview.ResidualVariance, again.ResidualVariance);
        Assert.Equal(preview.DocumentSummaryDiscountCredit, again.DocumentSummaryDiscountCredit);

        // Save: no 409, no residual justification, totals from the lines, pricing line-resolved for approval.
        var save = await controller.SaveQuotation(s.RequestId, null, dto);
        var saved = Assert.IsType<SavedQuotationDto>(Assert.IsType<OkObjectResult>(save.Result).Value);
        Assert.Equal(61_560m, saved.TotalAmount);
        Assert.Equal(0m, saved.DiscountAmount);
        Assert.True(saved.IsLinePricingResolved);
        var q = await ctx.Quotations.Include(x => x.Items).SingleAsync();
        Assert.Equal(new[] { 51_300m, 10_260m }, q.Items.OrderBy(i => i.LineNumber).Select(i => i.LineTotal).ToArray());
        Assert.Equal(new[] { 5_000m, 1_000m }, q.Items.OrderBy(i => i.LineNumber).Select(i => i.DiscountAmount).ToArray());
        Assert.All(q.Items, i => Assert.Equal(0m, i.OcrOriginalDiscountAmount));         // immutable baseline kept
        Assert.All(q.Items, i => Assert.Equal(AllocationReason, i.LineAdjustmentJustification)); // the allocation's audit
        Assert.Empty(await ctx.RequestStatusHistories.Where(h => h.ActionTaken == "QUOTATION_RESIDUAL_JUSTIFIED").ToListAsync());
    }

    [Fact] // The discount kept at quotation level reconciles too, but stays NOT line-resolved (submission block untouched)
    public async Task FixtureK_GlobalKept_ResidualZero_ButNotLineResolved()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var dto = KDto(s, 61_560m, 6_000m, globalDiscount: 6_000m, disc1: 0m, disc2: 0m, reasons: false);

        var preview = await PreviewAsync(controller, s, dto);
        Assert.Equal(-6_840m, preview.GlobalDiscountImpact);
        Assert.Equal(-6_840m, preview.DocumentSummaryDiscountCredit);
        Assert.Equal(0m, preview.ResidualVariance);
        Assert.All(preview.Lines, l => Assert.False(l.RequiresAdjustmentReason));

        var save = await controller.SaveQuotation(s.RequestId, null, dto);
        var saved = Assert.IsType<SavedQuotationDto>(Assert.IsType<OkObjectResult>(save.Result).Value);
        Assert.Equal(61_560m, saved.TotalAmount);
        Assert.Equal(6_000m, saved.DiscountAmount);
        Assert.False(saved.IsLinePricingResolved);
        Assert.Equal(QuotationLinePricingPolicy.Reasons.GlobalDiscountUnallocated, saved.LinePricingReasonCode);
    }

    [Fact] // Pre-fix payload (no header-discount baseline) still answers the observed 409 −6.840 — the baseline is what explains it
    public async Task FixtureK_WithoutHeaderDiscountBaseline_Returns409_Minus6840_PersistsNothing()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var dto = KDto(s, 61_560m, ocrDocumentDiscount: null, globalDiscount: 0m, disc1: 5_000m, disc2: 1_000m, reasons: true);

        var preview = await PreviewAsync(controller, s, dto);
        Assert.Equal(-6_840m, preview.ResidualVariance);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.None, preview.OcrDocumentDiscountKind);

        var save = await controller.SaveQuotation(s.RequestId, null, dto);
        var pd = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(save.Result).Value);
        Assert.Equal("DOCUMENT_RESIDUAL_UNEXPLAINED", pd.Extensions["code"]?.ToString());
        Assert.Equal(-6_840m, Assert.IsType<QuotationReconciliationDto>(pd.Extensions["reconciliation"]).ResidualVariance);
        Assert.Empty(await ctx.Quotations.ToListAsync());
    }

    [Fact] // A "discount" the header total does not reflect is a real inconsistency: 409 (+6.840), never dropped to fit
    public async Task FixtureK_HeaderNotNetOfTheExtractedDiscount_Returns409()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var dto = KDto(s, ocrTotal: 68_400m, ocrDocumentDiscount: 6_000m, globalDiscount: 6_000m, disc1: 0m, disc2: 0m, reasons: false);

        var save = await controller.SaveQuotation(s.RequestId, null, dto);
        var pd = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(save.Result).Value);
        Assert.Equal("DOCUMENT_RESIDUAL_UNEXPLAINED", pd.Extensions["code"]?.ToString());
        Assert.Equal(6_840m, Assert.IsType<QuotationReconciliationDto>(pd.Extensions["reconciliation"]).ResidualVariance);
        Assert.Empty(await ctx.Quotations.ToListAsync());

        // Justified, the audit row records the document discount, its credit and the signed residual (never zeroed).
        dto.FinancialIntegrityOverride = true;
        dto.OverrideJustification = "Documento sem desconto no total; valor lido como desconto confirmado com o fornecedor.";
        Assert.IsType<OkObjectResult>((await controller.SaveQuotation(s.RequestId, null, dto)).Result);
        var audit = Assert.Single(await ctx.RequestStatusHistories.Where(h => h.ActionTaken == "QUOTATION_RESIDUAL_JUSTIFIED").ToListAsync());
        Assert.Contains("desconto do resumo do documento=6", audit.Comment);
        Assert.Contains("(DOCUMENT_LEVEL)", audit.Comment);
        Assert.Contains("crédito do desconto do resumo=-6", audit.Comment);
        Assert.Contains("residual (com sinal)=6", audit.Comment);
    }

    [Fact] // An OCR line the extraction omitted is still a residual equal to its value (not reconciled away by matching totals)
    public async Task FixtureK_OmittedLine_Returns409()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var dto = KDto(s, 61_560m, 6_000m, globalDiscount: 6_000m, disc1: 0m, disc2: 0m, reasons: false);
        dto.Items.RemoveAt(1);   // only "Resma de Papel A4" extracted; Buyer keeps the 6.000 global on it → final 50.160

        var preview = await PreviewAsync(controller, s, dto);
        Assert.Equal(50_160m, preview.FinalConsideredTotal);
        Assert.Equal(11_400m, preview.ResidualVariance);
        Assert.True(preview.ResidualExceedsTolerance);
        var save = await controller.SaveQuotation(s.RequestId, null, dto);
        Assert.IsType<ConflictObjectResult>(save.Result);
    }

    [Fact] // Fixture J (LUANDAY): printed per-line discounts + summary aggregate 67.690 → aggregate, not credited twice, saves at 345.775,50
    public async Task FixtureJ_PrintedLineDiscounts_WithAggregateSummary_RemainsCorrect()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var dto = new SaveQuotationRequestDto
        {
            SupplierId = s.SupplierId, SupplierNameSnapshot = "LUANDAY - COMERCIO E SERVIÇOS, LDA", Currency = "AOA",
            DocumentNumber = "TEST-J-2026-0629", DocumentType = "PROFORMA",
            OcrTotal = 345_775.50m, OcrDocumentDiscountAmount = 67_690m, DiscountAmount = 0m,
            Items = new List<SaveQuotationItemDto>
            {
                OcrLine(1, s.Line1, "AGUA MESA PURA MB 6X1,5L", 150, 1_390, 179_518.50m, IVA5, 5, finalDiscount: 37_530m, reason: null, ocrDiscount: 37_530m),
                OcrLine(2, s.Line2, "AGUA MESA PURA MB 16X0,5L", 100, 1_885, 166_257m, IVA5, 5, finalDiscount: 30_160m, reason: null, ocrDiscount: 30_160m),
            }
        };

        var preview = await PreviewAsync(controller, s, dto);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.AggregateOfLines, preview.OcrDocumentDiscountKind);
        Assert.Equal(67_690m, preview.OcrDocumentDiscount);
        Assert.Equal(0m, preview.DocumentSummaryDiscountCredit);
        Assert.Equal(0m, preview.DiscountImpact);
        Assert.Equal(0m, preview.ResidualVariance);
        Assert.All(preview.Lines, l => Assert.False(l.RequiresAdjustmentReason));

        var save = await controller.SaveQuotation(s.RequestId, null, dto);
        var saved = Assert.IsType<SavedQuotationDto>(Assert.IsType<OkObjectResult>(save.Result).Value);
        Assert.Equal(345_775.50m, saved.TotalAmount);
        Assert.True(saved.IsLinePricingResolved);
        var q = await ctx.Quotations.Include(x => x.Items).SingleAsync();
        Assert.Equal(new[] { 179_518.50m, 166_257m }, q.Items.OrderBy(i => i.LineNumber).Select(i => i.LineTotal).ToArray());
    }

    [Fact] // Mixed VAT rates (14 % / 5 %) through the real endpoint: proportional allocation of a summary-only discount reconciles exactly
    public async Task MixedVat_SummaryDiscountAllocated_ResidualZero()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var dto = new SaveQuotationRequestDto
        {
            SupplierId = s.SupplierId, SupplierNameSnapshot = "LUANDAY - COMERCIO E SERVIÇOS, LDA", Currency = "AOA",
            DocumentNumber = "TEST-K2", DocumentType = "PROFORMA",
            OcrTotal = 16_593.75m, OcrDocumentDiscountAmount = 1_000m, DiscountAmount = 0m,
            Items = new List<SaveQuotationItemDto>
            {
                OcrLine(1, s.Line1, "ARTIGO K1", 10, 1_000, 10_000, IVA14, 14, finalDiscount: 625m, reason: AllocationReason),
                OcrLine(2, s.Line2, "ARTIGO K2", 3, 2_000, 6_000, IVA5, 5, finalDiscount: 375m, reason: AllocationReason),
            }
        };
        var preview = await PreviewAsync(controller, s, dto);
        Assert.Equal(-1_106.25m, preview.DocumentSummaryDiscountCredit);
        Assert.Equal(16_593.75m, preview.FinalConsideredTotal);
        Assert.Equal(0m, preview.ResidualVariance);

        var save = await controller.SaveQuotation(s.RequestId, null, dto);
        var saved = Assert.IsType<SavedQuotationDto>(Assert.IsType<OkObjectResult>(save.Result).Value);
        Assert.Equal(16_593.75m, saved.TotalAmount);
        Assert.Equal(1_593.75m, saved.TotalIvaAmount);
        Assert.True(saved.IsLinePricingResolved);
    }

    [Fact] // A negative or absurd baseline never becomes a credit; a manual quotation (no OcrTotal) stays exempt whatever the field says
    public async Task HeaderDiscountBaseline_NeverNegative_AndIgnoredWithoutOcrTotal()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);

        var negative = KDto(s, 61_560m, ocrDocumentDiscount: -6_000m, globalDiscount: 0m, disc1: 5_000m, disc2: 1_000m, reasons: true);
        var preview = await PreviewAsync(controller, s, negative);
        Assert.Equal(0m, preview.OcrDocumentDiscount);
        Assert.Equal(-6_840m, preview.ResidualVariance);   // treated as "none"

        var manual = KDto(s, ocrTotal: 0m, ocrDocumentDiscount: 6_000m, globalDiscount: 0m, disc1: 5_000m, disc2: 1_000m, reasons: true);
        manual.OcrTotal = null;
        var exempt = await PreviewAsync(controller, s, manual);
        Assert.Empty(exempt.Lines);
        Assert.Equal(0m, exempt.OcrDocumentDiscount);
    }
    // ════ v2.249.7 review — trust boundary of the client-supplied header baseline, and the same discount applied twice ════

    [Fact] // Declaring the figure can only TIGHTEN the gate: the same discount applied twice is a residual on preview, save AND update
    public async Task FixtureK_SameDiscountAppliedTwice_Returns409_OnPreviewSaveAndUpdate_PersistsNothing()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);

        // (a) re-entered as global on top of the allocated lines → final 54.720, residual +6.840.
        var reentered = KDto(s, 61_560m, 6_000m, globalDiscount: 6_000m, disc1: 5_000m, disc2: 1_000m, reasons: true);
        var preview = await PreviewAsync(controller, s, reentered);
        Assert.Equal(54_720m, preview.FinalConsideredTotal);
        Assert.Equal(-13_680m, preview.DocumentDiscountCarried);
        Assert.Equal(-6_840m, preview.DocumentDiscountExpected);
        Assert.Equal(-6_840m, preview.DocumentDiscountDeviation);
        Assert.Equal(6_840m, preview.ResidualVariance);
        Assert.True(preview.ResidualExceedsTolerance);
        var save = await controller.SaveQuotation(s.RequestId, null, reentered);
        var pd = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(save.Result).Value);
        Assert.Equal("DOCUMENT_RESIDUAL_UNEXPLAINED", pd.Extensions["code"]?.ToString());
        Assert.Equal(6_840m, Assert.IsType<QuotationReconciliationDto>(pd.Extensions["reconciliation"]).ResidualVariance);
        Assert.Empty(await ctx.Quotations.ToListAsync());
        Assert.Empty(await ctx.RequestStatusHistories.ToListAsync());

        // (b) distributed twice into the lines (10.000 / 2.000, global 0): line-resolved for approval, but the same +6.840 here.
        var twice = KDto(s, 61_560m, 6_000m, globalDiscount: 0m, disc1: 10_000m, disc2: 2_000m, reasons: true);
        Assert.Equal(6_840m, (await PreviewAsync(controller, s, twice)).ResidualVariance);
        Assert.IsType<ConflictObjectResult>((await controller.SaveQuotation(s.RequestId, null, twice)).Result);
        Assert.Empty(await ctx.Quotations.ToListAsync());

        // (c) UPDATE parity: the correct allocation saves; the same quotation updated to the doubled shape is refused, values unchanged.
        var correct = KDto(s, 61_560m, 6_000m, globalDiscount: 0m, disc1: 5_000m, disc2: 1_000m, reasons: true);
        var saved = Assert.IsType<SavedQuotationDto>(Assert.IsType<OkObjectResult>((await controller.SaveQuotation(s.RequestId, null, correct)).Result).Value);
        Assert.Equal(61_560m, saved.TotalAmount);
        var update = await controller.UpdateQuotation(s.RequestId, saved.Id, reentered);
        Assert.Equal("DOCUMENT_RESIDUAL_UNEXPLAINED", Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(update.Result).Value).Extensions["code"]?.ToString());
        var persisted = await ctx.Quotations.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.Id == saved.Id);
        Assert.Equal(61_560m, persisted.TotalAmount);
        Assert.Equal(0m, persisted.DiscountAmount);
        Assert.Equal(new[] { 5_000m, 1_000m }, persisted.Items.OrderBy(i => i.LineNumber).Select(i => i.DiscountAmount).ToArray());
        // The same update with the figure kept reconciles (idempotent).
        Assert.IsType<OkObjectResult>((await controller.UpdateQuotation(s.RequestId, saved.Id, correct)).Result);
    }

    [Fact] // The subtle variant is an INTENTIONAL Buyer adjustment only with the explicit residual justification — and it is audited as a deviation
    public async Task FixtureK_DistributedTwiceIntoLines_IsSavedOnlyWithTheExplicitResidualJustification_AuditedAsDeviation()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var twice = KDto(s, 61_560m, 6_000m, globalDiscount: 0m, disc1: 10_000m, disc2: 2_000m, reasons: true);
        twice.FinancialIntegrityOverride = true;
        twice.OverrideJustification = "Desconto adicional de 6.000,00 negociado com o fornecedor em 03/11, além do desconto do documento.";
        var saved = Assert.IsType<SavedQuotationDto>(Assert.IsType<OkObjectResult>((await controller.SaveQuotation(s.RequestId, null, twice)).Result).Value);
        Assert.Equal(54_720m, saved.TotalAmount);
        Assert.True(saved.IsLinePricingResolved);                      // the pricing policy is satisfied: the lines carry the total
        var audit = Assert.Single(await ctx.RequestStatusHistories.Where(h => h.ActionTaken == "QUOTATION_RESIDUAL_JUSTIFIED").ToListAsync());
        Assert.Contains("desconto transportado pela cotação=-13", audit.Comment);
        Assert.Contains("desconto concedido pelo documento=-6", audit.Comment);
        Assert.Contains("desvio de desconto face ao documento=-6", audit.Comment);
        Assert.Contains("residual (com sinal)=6", audit.Comment);
        Assert.Contains("negociado com o fornecedor", audit.Comment);
    }

    [Fact] // Trust boundary: the figure is the same client-supplied class as OcrTotal and the line baselines — it cannot remove a genuine
           // discrepancy BY ITSELF; what removes it is the Buyer carrying a discount that makes the quotation land on the header, which an
           // API client could already achieve by omitting or altering OcrTotal (pre-existing). The UI never fabricates it (captured once).
    public async Task HeaderDiscountBaseline_CannotRemoveAGenuineDiscrepancyByItself_AndIsTheSameTrustClassAsOcrTotal()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        // Genuine discrepancy: the provider read line 1 at 5.000 while the document (no discount at all) totals 61.560 — e.g. a mis-read price.

        // (1) Honest payload: no figure, no Buyer discount, final 68.400 → residual −6.840 → 409.
        var honest = KDto(s, 61_560m, null, globalDiscount: 0m, disc1: 0m, disc2: 0m, reasons: false);
        Assert.Equal(-6_840m, (await PreviewAsync(controller, s, honest)).ResidualVariance);
        Assert.IsType<ConflictObjectResult>((await controller.SaveQuotation(s.RequestId, null, honest)).Result);

        // (2) A fabricated figure ALONE (final still 68.400) does not reconcile: the deviation (+6.840) cancels the credit → 409 −6.840.
        var figureOnly = KDto(s, 61_560m, 6_000m, globalDiscount: 0m, disc1: 0m, disc2: 0m, reasons: false);
        var p2 = await PreviewAsync(controller, s, figureOnly);
        Assert.Equal(-6_840m, p2.DocumentSummaryDiscountCredit);
        Assert.Equal(6_840m, p2.DocumentDiscountDeviation);
        Assert.Equal(-6_840m, p2.ResidualVariance);
        Assert.IsType<ConflictObjectResult>((await controller.SaveQuotation(s.RequestId, null, figureOnly)).Result);

        // (3) Inflated (100.000) or aggregate-like figures do not help either: the final must still equal the header.
        Assert.Equal(-6_840m, (await PreviewAsync(controller, s, KDto(s, 61_560m, 100_000m, 0m, 0m, 0m, false))).ResidualVariance);
        Assert.Equal(-6_840m, (await PreviewAsync(controller, s, KDto(s, 61_560m, -6_000m, 0m, 0m, 0m, false))).ResidualVariance);

        // (4) What an API client CAN do: fabricate the figure AND carry a matching discount (final 61.560) → residual 0 → saved.
        //     This is indistinguishable from fixture K by arithmetic (a plausible value is not proof the document contains it)…
        var fabricated = KDto(s, 61_560m, 6_000m, globalDiscount: 6_000m, disc1: 0m, disc2: 0m, reasons: false);
        Assert.Equal(0m, (await PreviewAsync(controller, s, fabricated)).ResidualVariance);
        Assert.IsType<OkObjectResult>((await controller.SaveQuotation(s.RequestId, null, fabricated)).Result);
        Assert.Equal(1, await ctx.Quotations.CountAsync());

        // (5) …and it is the SAME trust class as the pre-existing inputs: the same client saves the undiscounted 68.400 unchallenged by
        //     omitting OcrTotal (gate exempt) or by setting OcrTotal to the line reconstruction — no figure involved at all.
        var noOcrTotal = KDto(s, 0m, null, globalDiscount: 0m, disc1: 0m, disc2: 0m, reasons: false);
        noOcrTotal.OcrTotal = null;
        Assert.IsType<OkObjectResult>((await controller.SaveQuotation(s.RequestId, null, noOcrTotal)).Result);
        var alteredOcrTotal = KDto(s, 68_400m, null, globalDiscount: 0m, disc1: 0m, disc2: 0m, reasons: false);
        Assert.IsType<OkObjectResult>((await controller.SaveQuotation(s.RequestId, null, alteredOcrTotal)).Result);
        Assert.Equal(3, await ctx.Quotations.CountAsync());
        // No server-held header evidence exists for quotations (OcrExtractedItems holds lines only): closing (4)/(5) needs persisted
        // header evidence bound to the attachment — documented as the architectural follow-up, not done here.
        Assert.Empty(await ctx.OcrExtractedItems.ToListAsync());
    }

    [Fact] // The figure the wizard now sends for an AGGREGATE summary (fixture J: 67.690 = 37.530 + 30.160) tightens J too: a re-entered global is a residual
    public async Task FixtureJ_AggregateFigure_ReenteredGlobal_Returns409()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var dto = new SaveQuotationRequestDto
        {
            SupplierId = s.SupplierId, SupplierNameSnapshot = "LUANDAY - COMERCIO E SERVIÇOS, LDA", Currency = "AOA",
            DocumentNumber = "TEST-J-2026-0629", DocumentType = "PROFORMA",
            OcrTotal = 345_775.50m, OcrDocumentDiscountAmount = 67_690m, DiscountAmount = 67_690m,   // re-entered on top of the lines
            Items = new List<SaveQuotationItemDto>
            {
                OcrLine(1, s.Line1, "AGUA MESA PURA MB 6X1,5L", 150, 1_390, 179_518.50m, IVA5, 5, finalDiscount: 37_530m, reason: null, ocrDiscount: 37_530m),
                OcrLine(2, s.Line2, "AGUA MESA PURA MB 16X0,5L", 100, 1_885, 166_257m, IVA5, 5, finalDiscount: 30_160m, reason: null, ocrDiscount: 30_160m),
            }
        };
        var preview = await PreviewAsync(controller, s, dto);
        Assert.Equal(QuotationReconciliationCalculator.DocumentDiscountKinds.AggregateOfLines, preview.OcrDocumentDiscountKind);
        Assert.Equal(274_701m, preview.FinalConsideredTotal);
        Assert.Equal(-71_074.50m, preview.DocumentDiscountDeviation);
        Assert.Equal(71_074.50m, preview.ResidualVariance);
        Assert.IsType<ConflictObjectResult>((await controller.SaveQuotation(s.RequestId, null, dto)).Result);
        Assert.Empty(await ctx.Quotations.ToListAsync());
    }
}
