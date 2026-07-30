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
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
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
/// In-memory controller coverage for the financial-reconciliation gate, the pure preview endpoint,
/// and OCR-baseline immutability. Uses the EF in-memory provider (the model already includes the new
/// nullable columns) so no migration needs to be applied to any database.
/// </summary>
public class QuotationReconciliationControllerTests
{
    private static ApplicationDbContext NewCtx() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed record Seed(Guid RequestId, Guid LineItemId, Guid ActorId, int SupplierId, int IvaId, ApplicationDbContext Ctx);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = Guid.NewGuid();
        ctx.Users.Add(new User { Id = actor, FullName = "Tester", Email = "t@t.co", IsActive = true });
        var type = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        var status = new RequestStatus { Id = 1, Code = "WAITING_QUOTATION", Name = "Aguardando" };
        ctx.RequestTypes.Add(type); ctx.RequestStatuses.Add(status);
        ctx.Suppliers.Add(new Supplier { Id = 7, Name = "Fornecedor" });
        ctx.IvaRates.Add(new IvaRate { Id = 3, RatePercent = 0m, Name = "Isento" });
        ctx.Units.Add(new Unit { Id = 2, Code = "UN", Name = "Unidade", AllowsDecimalQuantity = false });

        var request = new Request
        {
            Id = Guid.NewGuid(), Title = "R", StatusId = 1, Status = status, RequestTypeId = 1, RequestType = type,
            DepartmentId = 1, CompanyId = 1, PlantId = 1, RequesterId = actor, CreatedAtUtc = DateTime.UtcNow
        };
        var li = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1, Description = "Item", Quantity = 1, IsDeleted = false };
        request.LineItems.Add(li);
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, li.Id, actor, 7, 3, ctx);
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
            Microsoft.Extensions.Options.Options.Create(new AlplaPortal.Domain.Configuration.PostPaymentCompletionOptions()));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
            new Claim(ClaimTypes.Role, RoleConstants.SystemAdministrator)
        }, "Test");
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return controller;
    }

    private static SaveQuotationItemDto OcrItem(int line, Guid? mapped, decimal qty, decimal price,
        decimal? oQty, decimal? oPrice, decimal? oLineTotal, string status = "MAPPED", string? adjReason = null)
        => new()
        {
            LineNumber = line, Description = "L" + line, Quantity = qty, UnitPrice = price, UnitId = 2, IvaRateId = 3,
            DiscountAmount = 0, MappedRequestLineItemId = mapped, ReconciliationStatus = status,
            LineOrigin = QuotationLineOrigins.Ocr,
            OcrOriginalQuantity = oQty, OcrOriginalUnitPrice = oPrice, OcrOriginalDiscountAmount = 0,
            OcrOriginalIvaRatePercent = 0, OcrOriginalUnitId = 2, OcrOriginalLineTotal = oLineTotal,
            LineAdjustmentJustification = adjReason
        };

    private static SaveQuotationRequestDto Dto(Seed s, decimal ocrTotal, params SaveQuotationItemDto[] items)
        => new()
        {
            SupplierId = s.SupplierId, SupplierNameSnapshot = "Fornecedor", Currency = "AOA", SourceType = "OCR",
            OcrTotal = ocrTotal, Items = items.ToList()
        };

    /// <summary>Builds a DTO with the EXACT frontend shape: SourceType is deliberately UNSET (the
    /// frontend sends the key `source`, which does not bind to the DTO's `SourceType`), so applicability
    /// must be driven by OcrTotal alone.</summary>
    private static SaveQuotationRequestDto FrontendShapeDto(Seed s, decimal ocrTotal, params SaveQuotationItemDto[] items)
        => new()
        {
            SupplierId = s.SupplierId, SupplierNameSnapshot = "Fornecedor", Currency = "AOA",
            // SourceType intentionally omitted → null (reproduces the live payload).
            OcrTotal = ocrTotal, Items = items.ToList()
        };

    [Fact] // Regression: real frontend payload (no SourceType) reconciles from the DTO — not all-zero.
    public async Task Preview_FrontendShape_NoSourceType_ReconcilesFromPayload()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);

        // REQ-075 shape (clean IVA-0 numbers): MAPPED qty 6→1, EXTRA_ITEM unchanged, valued IGNORED.
        var dto = FrontendShapeDto(s, 940000m,
            OcrItem(1, s.LineItemId, qty: 1, price: 100000, oQty: 6, oPrice: 100000, oLineTotal: 600000, status: "MAPPED"),
            OcrItem(2, null, qty: 2, price: 50000, oQty: 2, oPrice: 50000, oLineTotal: 100000, status: "EXTRA_ITEM"),
            OcrItem(3, null, qty: 3, price: 80000, oQty: 3, oPrice: 80000, oLineTotal: 240000, status: "IGNORED"));

        var result = await controller.PreviewQuotationReconciliation(s.RequestId, null, dto, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var r = Assert.IsType<QuotationReconciliationDto>(ok.Value);

        // The defect returned an all-zero DTO with lines: []. It must now compute from the payload.
        Assert.Equal(3, r.Lines.Count);
        Assert.Equal(940000m, r.OcrHeaderTotal);
        Assert.Equal(940000m, r.OcrLineSumTotal);
        Assert.Equal(940000m, r.ReconstructedOcrLineSum);
        Assert.Equal(-240000m, r.IgnoredImpact);
        Assert.Equal(-500000m, r.QuantityImpact);
        Assert.Equal(200000m, r.FinalConsideredTotal);
        Assert.Equal(0m, r.ResidualVariance);
        Assert.NotEqual(0m, r.OcrHeaderTotal); // never the empty/all-zero result
    }

    [Fact] // Save/Update gate now fires on the frontend shape too (was silently skipped via SourceType).
    public async Task SaveQuotation_FrontendShape_NoSourceType_EnforcesResidualGate()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        // header 150 but the single line totals 100 → structural residual 50 → 409 (no SourceType set).
        var dto = FrontendShapeDto(s, 150,
            OcrItem(1, s.LineItemId, 1, 100, oQty: 1, oPrice: 100, oLineTotal: 100));
        var result = await controller.SaveQuotation(s.RequestId, null, dto);
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("DOCUMENT_RESIDUAL_UNEXPLAINED", Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]?.ToString());
    }

    [Fact] // A genuinely manual quotation (no OcrTotal) stays exempt — all-zero is correct there.
    public async Task Preview_ManualQuotation_NoOcrTotal_ReturnsAllZero()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var dto = new SaveQuotationRequestDto
        {
            SupplierId = s.SupplierId, SupplierNameSnapshot = "Fornecedor", Currency = "AOA",
            Items = new List<SaveQuotationItemDto> { new() { LineNumber = 1, Description = "M", Quantity = 1, UnitPrice = 100, UnitId = 2, IvaRateId = 3, ReconciliationStatus = "MAPPED", MappedRequestLineItemId = s.LineItemId } }
        };
        var result = await controller.PreviewQuotationReconciliation(s.RequestId, null, dto, CancellationToken.None);
        var r = Assert.IsType<QuotationReconciliationDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(r.Lines);
        Assert.Equal(0m, r.OcrHeaderTotal);
    }

    [Fact] // Missing baseline on a new OCR line → structured 400 (never silently exempted)
    public async Task SaveQuotation_OcrLineWithoutBaseline_Returns400()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        // OCR line but OcrOriginalLineTotal omitted.
        var item = OcrItem(1, s.LineItemId, 1, 100, oQty: 1, oPrice: 100, oLineTotal: null);
        var result = await controller.SaveQuotation(s.RequestId, null, Dto(s, 100, item));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var pd = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Equal("OCR_LINE_BASELINE_MISSING", pd.Extensions["code"]?.ToString());
    }

    [Fact] // Structural header difference beyond tolerance → 409 with reconciliation breakdown; nothing persisted
    public async Task SaveQuotation_StructuralResidual_Returns409_AndPersistsNothing()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        // header 150 but the single line totals 100 → structural residual 50.
        var item = OcrItem(1, s.LineItemId, 1, 100, oQty: 1, oPrice: 100, oLineTotal: 100);
        var result = await controller.SaveQuotation(s.RequestId, null, Dto(s, 150, item));

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var pd = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("DOCUMENT_RESIDUAL_UNEXPLAINED", pd.Extensions["code"]?.ToString());
        var recon = Assert.IsType<QuotationReconciliationDto>(pd.Extensions["reconciliation"]);
        Assert.Equal(50m, recon.ResidualVariance);
        Assert.Empty(await ctx.Quotations.ToListAsync()); // nothing persisted
    }

    [Fact] // Providing the residual justification saves, records history, and does NOT zero the residual
    public async Task SaveQuotation_ResidualWithOverride_Saves_AndAuditKeepsSignedResidual()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var item = OcrItem(1, s.LineItemId, 1, 100, oQty: 1, oPrice: 100, oLineTotal: 100);
        var dto = Dto(s, 150, item);
        dto.FinancialIntegrityOverride = true;
        dto.OverrideJustification = "Frete não itemizado pelo OCR, confirmado com o fornecedor.";
        var result = await controller.SaveQuotation(s.RequestId, null, dto);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(await ctx.Quotations.ToListAsync());
        var audit = await ctx.RequestStatusHistories.Where(h => h.ActionTaken == "QUOTATION_RESIDUAL_JUSTIFIED").ToListAsync();
        Assert.Single(audit);
        Assert.Contains("residual (com sinal)=50", audit[0].Comment);      // signed residual recorded, not zeroed
        Assert.Contains("diferença estrutural=50", audit[0].Comment);
    }

    [Fact] // Quantity change without a line reason → 400
    public async Task SaveQuotation_QuantityChangeNoReason_Returns400()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        // OCR qty 6 → final 1, header/line consistent (600), but no LineAdjustmentJustification.
        var item = OcrItem(1, s.LineItemId, 1, 100, oQty: 6, oPrice: 100, oLineTotal: 600);
        var result = await controller.SaveQuotation(s.RequestId, null, Dto(s, 600, item));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var pd = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Equal("LINE_ADJUSTMENT_REASON_REQUIRED", pd.Extensions["code"]?.ToString());
    }

    [Fact] // Quantity 6→1 WITH a valid reason and consistent header → saves (residual 0)
    public async Task SaveQuotation_QuantityChangeWithReason_Saves()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var item = OcrItem(1, s.LineItemId, 1, 100, oQty: 6, oPrice: 100, oLineTotal: 600,
            adjReason: "Apenas parte da quantidade será adquirida neste momento.");
        var result = await controller.SaveQuotation(s.RequestId, null, Dto(s, 600, item));
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact] // Integer-unit fractional final quantity is rejected as invalid input
    public async Task SaveQuotation_IntegerUnitFractionalQuantity_Returns400()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var item = OcrItem(1, s.LineItemId, 1.5m, 100, oQty: 1.5m, oPrice: 100, oLineTotal: 150);
        var result = await controller.SaveQuotation(s.RequestId, null, Dto(s, 150, item));
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var pd = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Equal("Quantidade Inválida", pd.Title);
    }

    [Fact] // Preview has ZERO persistence side effects and matches what Save would compute (parity)
    public async Task Preview_IsPure_AndMatchesSave()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var item = OcrItem(1, s.LineItemId, 1, 100, oQty: 1, oPrice: 100, oLineTotal: 100);

        var preview = await controller.PreviewQuotationReconciliation(s.RequestId, null, Dto(s, 150, item), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(preview.Result);
        var previewDto = Assert.IsType<QuotationReconciliationDto>(ok.Value);

        // Purity: no quotations/items/history written by the preview.
        Assert.Empty(await ctx.Quotations.ToListAsync());
        Assert.Empty(await ctx.QuotationItems.ToListAsync());
        Assert.Empty(await ctx.RequestStatusHistories.ToListAsync());

        // Parity: the Save 409 carries the identical residual for the same payload.
        var save = await controller.SaveQuotation(s.RequestId, null, Dto(s, 150, item));
        var conflict = Assert.IsType<ConflictObjectResult>(save.Result);
        var saveRecon = Assert.IsType<QuotationReconciliationDto>(Assert.IsType<ProblemDetails>(conflict.Value).Extensions["reconciliation"]);
        Assert.Equal(previewDto.ResidualVariance, saveRecon.ResidualVariance);
        Assert.Equal(previewDto.StructuralHeaderDifference, saveRecon.StructuralHeaderDifference);
        Assert.Equal(previewDto.FinalConsideredTotal, saveRecon.FinalConsideredTotal);
    }

    [Fact] // Out-of-scope preview (request not visible to the actor) → NotFound
    public async Task Preview_OutOfScopeRequest_Returns404()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);
        var item = OcrItem(1, s.LineItemId, 1, 100, 1, 100, 100);
        var result = await controller.PreviewQuotationReconciliation(Guid.NewGuid(), null, Dto(s, 100, item), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact] // UpdateQuotation never overwrites the persisted OCR baseline (immutability)
    public async Task UpdateQuotation_DoesNotOverwriteBaseline()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        var controller = BuildController(ctx, s.ActorId);

        // Create an OCR quotation with baseline qty 6 (residual 0, consistent header/line = 600).
        var createItem = OcrItem(1, s.LineItemId, 6, 100, oQty: 6, oPrice: 100, oLineTotal: 600);
        var created = await controller.SaveQuotation(s.RequestId, null, Dto(s, 600, createItem));
        var saved = Assert.IsType<SavedQuotationDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        // Now edit to qty 1 WITH a reason, and (maliciously) send a DIFFERENT OCR baseline in the DTO.
        var editItem = OcrItem(1, s.LineItemId, 1, 100, oQty: 999, oPrice: 999, oLineTotal: 99999,
            adjReason: "Quantidade corrigida conforme o documento original do fornecedor.");
        var editDto = Dto(s, 600, editItem);
        var updated = await controller.UpdateQuotation(s.RequestId, saved.Id, editDto);
        Assert.IsType<OkObjectResult>(updated.Result);

        // The persisted baseline is still the ORIGINAL (6 / 100 / 600), NOT the DTO's tampered values.
        var persisted = await ctx.QuotationItems.AsNoTracking().SingleAsync();
        Assert.Equal(6m, persisted.OcrOriginalQuantity);
        Assert.Equal(100m, persisted.OcrOriginalUnitPrice);
        Assert.Equal(600m, persisted.OcrOriginalLineTotal);
        Assert.Equal(1m, persisted.Quantity); // final value updated
    }

    [Fact] // Legacy UpdateQuotation (null baseline, no OcrTotal) is exempt from reconciliation
    public async Task UpdateQuotation_LegacyNullBaseline_NoOcrTotal_IsExempt()
    {
        using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        // Seed a legacy manual quotation directly (no OcrOriginal*).
        var q = new Quotation { Id = Guid.NewGuid(), RequestId = s.RequestId, SupplierId = s.SupplierId, SupplierNameSnapshot = "Fornecedor", Currency = "AOA", SourceType = "MANUAL", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.ActorId };
        q.Items.Add(new QuotationItem { Id = Guid.NewGuid(), QuotationId = q.Id, LineNumber = 1, Description = "Legacy", Quantity = 5, UnitPrice = 100, UnitId = 2, IvaRatePercent = 0, GrossSubtotal = 500, IvaAmount = 0, LineTotal = 500, MappedRequestLineItemId = s.LineItemId, ReconciliationStatus = "MAPPED" });
        ctx.Quotations.Add(q);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, s.ActorId);
        // Manual quotation update (no OcrTotal, no LineOrigin) — must not trip the gate despite a big change.
        var dto = new SaveQuotationRequestDto
        {
            SupplierId = s.SupplierId, SupplierNameSnapshot = "Fornecedor", Currency = "AOA", SourceType = "MANUAL",
            Items = new List<SaveQuotationItemDto> { new() { LineNumber = 1, Description = "Legacy", Quantity = 1, UnitPrice = 100, UnitId = 2, IvaRateId = 3, DiscountAmount = 0, MappedRequestLineItemId = s.LineItemId, ReconciliationStatus = "MAPPED" } }
        };
        var result = await controller.UpdateQuotation(s.RequestId, q.Id, dto);
        Assert.IsType<OkObjectResult>(result.Result);
    }
}
