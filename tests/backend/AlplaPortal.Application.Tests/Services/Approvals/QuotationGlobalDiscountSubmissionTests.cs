using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// v2.249.6 — REQ-21/10/2026-595 (LUANDAY proforma PP ZL972200ZM2026/5044066758) through the REAL endpoints on the InMemory
/// provider. The synthetic fixture is the document the operator reported: 150 CX × 1.390,00 with 37.530,00 commercial discount
/// and 100 CX × 1.885,00 with 30.160,00, IVA 5 %: before discounts 397.000,00; discounts 67.690,00; taxable 329.310,00; IVA
/// 16.465,50; payable 345.775,50.
///
/// <para>What is pinned: (1) the persisted TEST shape — lines WITHOUT discount and the 67.690,00 as the quotation-level discount —
/// is exactly what SaveQuotation writes from such a payload (line totals 218.925,00 / 197.925,00 = qty × price × 1,05; quotation
/// total 345.775,50), and the write path itself cannot explain how the line discounts reached 0 (the read-only diagnostic does);
/// (2) the candidate snapshot has no place for a global discount, so without a guard approval would freeze 416.850,00; (3) the
/// batch endpoints now refuse such a quotation before any write, whole or partial batch, create or rework; (4) the supported shape
/// (per-line discounts) flows unchanged through candidates, groups and the approved total; (5) a legacy line-sum mismatch is
/// refused with its own reason; (6) the details read exposes the verdict.</para>
/// </summary>
public class QuotationGlobalDiscountSubmissionTests
{
    private const int IVA5 = 905, IVA14 = 914, UNIT_CX = 907, SUPPLIER = 9595, DEPT = 959, COMPANY = 959, PLANT = 1;
    private const string DOC = "PP ZL972200ZM2026/5044066758";

    private sealed class Seed
    {
        public DbContextOptions<ApplicationDbContext> Options = null!;
        public Guid RequestId, Actor, Line1, Line2, Line3;
    }

    private static async Task<Seed> SeedAsync()
    {
        var s = new Seed
        {
            Options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            RequestId = Guid.NewGuid(), Actor = Guid.NewGuid(), Line1 = Guid.NewGuid(), Line2 = Guid.NewGuid(), Line3 = Guid.NewGuid()
        };
        await using var ctx = new ApplicationDbContext(s.Options);
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        var id = 1;
        foreach (var code in new[] { RequestConstants.Statuses.WaitingQuotation, RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.WaitingFinalApproval,
                     RequestConstants.Statuses.FinalApproved, RequestConstants.Statuses.PoRequested, RequestConstants.Statuses.AreaAdjustment, RequestConstants.Statuses.Draft })
            ctx.RequestStatuses.Add(new RequestStatus { Id = id++, Code = code, Name = code, DisplayOrder = id });
        ctx.Currencies.Add(new Currency { Id = 900, Code = "AOA", Symbol = "Kz" });
        ctx.Departments.Add(new Department { Id = DEPT, Name = "ZZ Logística", IsActive = true });
        ctx.Companies.Add(new Company { Id = COMPANY, Name = "ZZ Companhia", IsActive = true });
        ctx.Plants.Add(new Plant { Id = PLANT, Name = "Viana 3", CompanyId = COMPANY });
        ctx.Users.Add(new User { Id = s.Actor, FullName = "ZZ Comprador", Email = "zz.comprador@test.local", IsActive = true });
        ctx.Suppliers.Add(new Supplier { Id = SUPPLIER, Name = "LUANDAY - COMERCIO E SERVIÇOS, LDA", TaxId = "5417010871", RegistrationStatus = "ACTIVE", PortalCode = "SUP-ZZ595", IsActive = true });
        ctx.IvaRates.Add(new IvaRate { Id = IVA5, Code = "IVA5", Name = "IVA 5%", RatePercent = 5m, IsActive = true });
        ctx.IvaRates.Add(new IvaRate { Id = IVA14, Code = "IVA14", Name = "IVA 14%", RatePercent = 14m, IsActive = true });
        ctx.Units.Add(new Unit { Id = UNIT_CX, Code = "CX", Name = "Caixa", IsActive = true, AllowsDecimalQuantity = false });
        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = "REQ-ZZ/10/2026-595", Title = "ZZTEST água de mesa", RequestTypeId = 1, StatusId = 1,
            RequesterId = s.Actor, BuyerId = s.Actor, DepartmentId = DEPT, CompanyId = COMPANY, PlantId = PLANT, CurrencyId = 900,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3), UpdatedAtUtc = DateTime.UtcNow
        });
        ctx.RequestLineItems.AddRange(
            new RequestLineItem { Id = s.Line1, RequestId = s.RequestId, LineNumber = 1, Description = "AGUA MESA PURA MB 6X1,5L", Quantity = 150, UnitId = UNIT_CX, UnitPrice = 0, TotalAmount = 0, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Actor },
            new RequestLineItem { Id = s.Line2, RequestId = s.RequestId, LineNumber = 2, Description = "AGUA MESA PURA MB 16X0,5L", Quantity = 100, UnitId = UNIT_CX, UnitPrice = 0, TotalAmount = 0, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Actor },
            new RequestLineItem { Id = s.Line3, RequestId = s.RequestId, LineNumber = 3, Description = "PORTES / ENTREGA", Quantity = 1, UnitId = UNIT_CX, UnitPrice = 0, TotalAmount = 0, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Actor });
        await ctx.SaveChangesAsync();
        return s;
    }

    private static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    private static RequestsController Requests(ApplicationDbContext ctx, Guid userId)
    {
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>(); orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Returns(Task.CompletedTask);
        var extraItems = new Mock<IBatchExtraItemDecisionService>(); extraItems.Setup(x => x.GetInformationalLinesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new BatchInformationalLinesDto());
        var statusSync = new Mock<IRequestStatusSyncService>(); statusSync.Setup(x => x.ComputeDisplayWorkflowStateAsync(It.IsAny<Guid>())).ReturnsAsync(string.Empty);
        var ctl = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            statusSync.Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new QuotationItemEligibilityService(ctx), extraItems.Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()));
        ctl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return ctl;
    }

    private static ApprovalBatchController Batches(ApplicationDbContext ctx, Guid actorId)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto { Managers = { new AreaManagerDto { UserId = actorId, FullName = "ZZ Manager" } } });
        routing.Setup(r => r.IsAreaManagerAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(false);
        var ctl = new ApprovalBatchController(ctx, NullLogger<ApprovalBatchController>.Instance, new Mock<IRequestStatusSyncService>().Object,
            new GroupBuilderService(ctx), routing.Object, new QuotationItemEligibilityService(ctx), new BatchExtraItemDecisionService(ctx),
            new AdjustmentCycleService(ctx), new Mock<IWorkflowNotificationOrchestrator>().Object);
        ctl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.SystemAdministrator), new(ClaimTypes.Role, RoleConstants.FinalApprover) }, "Test"))
            }
        };
        return ctl;
    }

    private static ProblemDetails Problem(IActionResult r) => Assert.IsType<ProblemDetails>(((ObjectResult)r).Value);
    private static string Code(IActionResult r) => (string)Problem(r).Extensions["code"]!;

    /// <summary>The LUANDAY document as the wizard submits it. <paramref name="lineDiscounts"/> = per-line commercial discounts
    /// (document: 37.530,00 / 30.160,00); <paramref name="globalDiscount"/> = the quotation-level field ("Desconto Comercial Global").</summary>
    private static SaveQuotationRequestDto LuandayDto(Seed s, decimal[] lineDiscounts, decimal globalDiscount, bool residualOverride = false, bool withOcrTotal = true)
    {
        SaveQuotationItemDto Line(int n, string description, decimal qty, decimal unitPrice, decimal discount, Guid mapped) => new()
        {
            LineNumber = n, Description = description, Quantity = qty, UnitPrice = unitPrice, DiscountAmount = discount, IvaRateId = IVA5, UnitId = UNIT_CX,
            MappedRequestLineItemId = mapped, ReconciliationStatus = "MAPPED", LineOrigin = QuotationLineOrigins.Ocr,
            OcrOriginalQuantity = qty, OcrOriginalUnitPrice = unitPrice, OcrOriginalDiscountAmount = discount, OcrOriginalIvaRatePercent = 5m,
            OcrOriginalUnitText = "CX", OcrOriginalUnitId = UNIT_CX, OcrOriginalLineTotal = Math.Round(qty * unitPrice - discount, 2)
        };
        return new SaveQuotationRequestDto
        {
            SupplierId = SUPPLIER, SupplierNameSnapshot = "LUANDAY - COMERCIO E SERVIÇOS, LDA", DocumentNumber = DOC, DocumentDate = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
            DocumentType = "PROFORMA", Currency = "AOA", SourceType = "OCR", SourceFileName = "20261021_104430-LUANDAY.pdf",
            DiscountAmount = globalDiscount, TotalAmount = 345_775.50m, OcrTotal = withOcrTotal ? 345_775.50m : null,
            FinancialIntegrityOverride = residualOverride,
            OverrideJustification = residualOverride ? "Desconto comercial do documento aplicado como desconto global; diferença residual aceite pelo comprador." : null,
            Items = new List<SaveQuotationItemDto>
            {
                Line(1, "AGUA MESA PURA MB 6X1,5L", 150m, 1_390m, lineDiscounts[0], s.Line1),
                Line(2, "AGUA MESA PURA MB 16X0,5L", 100m, 1_885m, lineDiscounts[1], s.Line2)
            }
        };
    }

    /// <summary>Mixed-VAT fixture (manual source, no OCR total): line A 10 × 1.000,00 IVA 14 %, line B 3 × 2.000,00 IVA 5 %,
    /// line C 1 × 0,00 IVA 14 % (zero-value line). <paramref name="lineDiscounts"/> per line; <paramref name="globalDiscount"/> at quotation level.</summary>
    private static SaveQuotationRequestDto MixedDto(Seed s, decimal[] lineDiscounts, decimal globalDiscount)
    {
        SaveQuotationItemDto Line(int n, string description, decimal qty, decimal unitPrice, decimal discount, int iva, Guid mapped) => new()
        {
            LineNumber = n, Description = description, Quantity = qty, UnitPrice = unitPrice, DiscountAmount = discount, IvaRateId = iva, UnitId = UNIT_CX,
            MappedRequestLineItemId = mapped, ReconciliationStatus = "MAPPED", LineOrigin = QuotationLineOrigins.Manual
        };
        return new SaveQuotationRequestDto
        {
            SupplierId = SUPPLIER, SupplierNameSnapshot = "LUANDAY - COMERCIO E SERVIÇOS, LDA", DocumentNumber = "MIX-" + globalDiscount.ToString("0.##"),
            DocumentDate = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc), DocumentType = "PROFORMA", Currency = "AOA", SourceType = "MANUAL",
            DiscountAmount = globalDiscount,
            Items = new List<SaveQuotationItemDto>
            {
                Line(1, "AGUA MESA PURA MB 6X1,5L", 10m, 1_000m, lineDiscounts[0], IVA14, s.Line1),
                Line(2, "AGUA MESA PURA MB 16X0,5L", 3m, 2_000m, lineDiscounts[1], IVA5, s.Line2),
                Line(3, "PORTES / ENTREGA", 1m, 0m, lineDiscounts[2], IVA14, s.Line3)
            }
        };
    }

    private static async Task<SavedQuotationDto> SaveAsync(Seed s, SaveQuotationRequestDto dto)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        var result = await Requests(ctx, s.Actor).SaveQuotation(s.RequestId, null, dto);
        if (result.Result is ObjectResult { Value: ProblemDetails failure } && result.Result is not OkObjectResult)
            Assert.Fail($"SaveQuotation refused: {failure.Status} {failure.Title} — {failure.Detail} [code={(failure.Extensions.TryGetValue("code", out var c) ? c : null)}]");
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<SavedQuotationDto>(ok.Value);
    }

    /// <summary>Directly persisted quotation (no endpoint): the REQ-595 shape or any other.</summary>
    private static async Task<(Guid QuotationId, Guid FirstItem, Guid SecondItem)> SeedQuotationAsync(Seed s, decimal total, decimal globalDiscount, decimal line1Total, decimal line2Total, string supplierName = "LUANDAY - COMERCIO E SERVIÇOS, LDA", int supplierId = SUPPLIER)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        if (supplierId != SUPPLIER && !await ctx.Suppliers.AnyAsync(x => x.Id == supplierId))
            ctx.Suppliers.Add(new Supplier { Id = supplierId, Name = supplierName, TaxId = "54" + supplierId, RegistrationStatus = "ACTIVE", PortalCode = "SUP-" + supplierId, IsActive = true });
        var q = new Quotation { Id = Guid.NewGuid(), RequestId = s.RequestId, SupplierId = supplierId, SupplierNameSnapshot = supplierName, DocumentNumber = DOC, Currency = "AOA", SourceType = "OCR",
            TotalAmount = total, DiscountAmount = globalDiscount, CreatedAtUtc = DateTime.UtcNow.AddHours(-1), CreatedByUserId = s.Actor };
        var i1 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = q.Id, LineNumber = 1, Description = "AGUA MESA PURA MB 6X1,5L", Quantity = 150, UnitPrice = 1_390m, GrossSubtotal = 208_500m, IvaRatePercent = 5m, IvaAmount = line1Total - (line1Total / 1.05m), LineTotal = line1Total, MappedRequestLineItemId = s.Line1, ReconciliationStatus = "MAPPED", UnitId = UNIT_CX, IvaRateId = IVA5 };
        var i2 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = q.Id, LineNumber = 2, Description = "AGUA MESA PURA MB 16X0,5L", Quantity = 100, UnitPrice = 1_885m, GrossSubtotal = 188_500m, IvaRatePercent = 5m, IvaAmount = line2Total - (line2Total / 1.05m), LineTotal = line2Total, MappedRequestLineItemId = s.Line2, ReconciliationStatus = "MAPPED", UnitId = UNIT_CX, IvaRateId = IVA5 };
        ctx.Quotations.Add(q); ctx.QuotationItems.AddRange(i1, i2);
        await ctx.SaveChangesAsync();
        return (q.Id, i1.Id, i2.Id);
    }

    private static CreateApprovalBatchDto BatchOf(params (Guid line, Guid[] candidates)[] items) => new()
    {
        Items = items.Select(i => new BatchItemDto { RequestLineItemId = i.line, Candidates = i.candidates.Select(c => new BatchCandidateInputDto { QuotationItemId = c }).ToList() }).ToList()
    };

    // ── 1. The persisted TEST shape is what SaveQuotation writes; approval is refused before any write ──

    [Fact]
    public async Task The_save_gate_flags_the_REQ_595_shape_so_it_cannot_be_written_from_the_OCR_flow_without_a_human_decision()
    {
        var s = await SeedAsync();
        await using var ctx = new ApplicationDbContext(s.Options);
        // OCR header 345.775,50 against a line baseline WITHOUT discounts (reconstructed 416.850,00): the document is internally
        // inconsistent by 71.074,50 and the reconciliation gate refuses the save. The persisted TEST state therefore required one of:
        // a residual override (QUOTATION_RESIDUAL_JUSTIFIED history row), per-line edits with a LineAdjustmentJustification (baseline
        // carrying the discounts), or no OCR header total at all (gate not applicable) — exactly what the read-only diagnostic reads.
        var refused = await Requests(ctx, s.Actor).SaveQuotation(s.RequestId, null, LuandayDto(s, new[] { 0m, 0m }, 67_690m));
        var conflict = Assert.IsType<ConflictObjectResult>(refused.Result);
        var pd = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("DOCUMENT_RESIDUAL_UNEXPLAINED", pd.Extensions["code"]);
        Assert.Contains("-71,074.50", pd.Detail!.Replace("−", "-"));
        Assert.Equal(0, await ctx.Quotations.CountAsync());
    }

    [Fact]
    public async Task Without_an_OCR_header_total_the_REQ_595_shape_is_saved_unchallenged_and_is_refused_at_submission()
    {
        var s = await SeedAsync();
        var saved = await SaveAsync(s, LuandayDto(s, lineDiscounts: new[] { 0m, 0m }, globalDiscount: 67_690m, withOcrTotal: false));
        Assert.Equal(345_775.50m, saved.TotalAmount);
        Assert.Equal(416_850m, saved.ConsideredLineTotalSum);
        Assert.False(saved.IsLinePricingResolved);
        await using var ctx = new ApplicationDbContext(s.Options);
        Assert.Equal(0, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "QUOTATION_RESIDUAL_JUSTIFIED"));
        var items = saved.Items.OrderBy(i => i.LineNumber).Select(i => i.Id).ToArray();
        var r = await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { items[0] }), (s.Line2, new[] { items[1] })));
        Assert.Equal(QuotationLinePricingPolicy.ConflictCode, Code(r));
    }

    [Fact]
    public async Task REQ_595_shape_lines_without_discount_plus_global_discount_persists_the_TEST_figures_and_is_refused_at_submission()
    {
        var s = await SeedAsync();
        // The human decision that lets this shape through the OCR flow: the residual override (audited as QUOTATION_RESIDUAL_JUSTIFIED).
        var saved = await SaveAsync(s, LuandayDto(s, lineDiscounts: new[] { 0m, 0m }, globalDiscount: 67_690m, residualOverride: true));

        // The quotation card figure (right) and the per-line figures approval would freeze (wrong) — both from the write path.
        Assert.Equal(345_775.50m, saved.TotalAmount);
        Assert.Equal(67_690m, saved.DiscountAmount);
        Assert.Equal(397_000m, saved.TotalGrossAmount);
        Assert.Equal(329_310m, saved.TotalTaxableBase);
        Assert.Equal(16_465.50m, saved.TotalIvaAmount);
        Assert.Equal(new[] { 218_925m, 197_925m }, saved.Items.OrderBy(i => i.LineNumber).Select(i => i.LineTotal).ToArray());   // qty × price × 1,05
        Assert.All(saved.Items, i => Assert.Equal(0m, i.DiscountAmount));
        Assert.False(saved.IsLinePricingResolved);
        Assert.Equal(QuotationLinePricingPolicy.Reasons.GlobalDiscountUnallocated, saved.LinePricingReasonCode);
        Assert.Equal(416_850m, saved.ConsideredLineTotalSum);

        await using var verify = new ApplicationDbContext(s.Options);
        var q = await verify.Quotations.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == saved.Id);
        Assert.Equal(345_775.50m, q.TotalAmount);
        Assert.Equal(416_850m, q.Items.Sum(i => i.LineTotal));
        var residual = await verify.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "QUOTATION_RESIDUAL_JUSTIFIED");
        Assert.Contains("desconto global=", residual.Comment);   // the override audit recorded the global-discount bucket and the residual

        // Submission (both lines) → 409, nothing written: no batch, no candidate, lifecycle untouched.
        await using var ctx = new ApplicationDbContext(s.Options);
        var items = q.Items.OrderBy(i => i.LineNumber).ToList();
        var r = await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { items[0].Id }), (s.Line2, new[] { items[1].Id })));
        Assert.IsType<ConflictObjectResult>(r);
        Assert.Equal(QuotationLinePricingPolicy.ConflictCode, Code(r));
        var pd = Problem(r);
        var digits = new string(pd.Detail!.Where(char.IsDigit).ToArray());   // culture-independent: the three figures, whatever the separators
        Assert.Contains("6769000", digits);
        Assert.Contains("41685000", digits);
        Assert.Contains("34577550", digits);
        Assert.Contains("Nada foi enviado", pd.Detail);
        Assert.Equal(0, await verify.ApprovalBatches.CountAsync());
        Assert.Equal(0, await verify.ApprovalBatchItemCandidates.CountAsync());
        Assert.All(await verify.RequestLineItems.AsNoTracking().Where(l => l.RequestId == s.RequestId).ToListAsync(), l => Assert.Null(l.QuotationLifecycleStatus));
        Assert.Equal(0, await verify.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "BATCH_CANDIDATES_SUBMITTED"));
    }

    // ── 2. The mechanism: the frozen candidate snapshot has no field for a quotation-level discount ──

    [Fact]
    public void The_candidate_snapshot_freezes_line_totals_only_so_a_global_discount_would_be_approved_away()
    {
        var quotation = new Quotation { Id = Guid.NewGuid(), SupplierNameSnapshot = "LUANDAY", Currency = "AOA", TotalAmount = 345_775.50m, DiscountAmount = 67_690m, TotalTaxableBase = 329_310m, TotalIvaAmount = 16_465.50m };
        var items = new[]
        {
            new QuotationItem { Id = Guid.NewGuid(), Quotation = quotation, Description = "AGUA 6X1,5L", Quantity = 150, UnitPrice = 1_390m, GrossSubtotal = 208_500m, IvaRatePercent = 5m, IvaAmount = 10_425m, LineTotal = 218_925m },
            new QuotationItem { Id = Guid.NewGuid(), Quotation = quotation, Description = "AGUA 16X0,5L", Quantity = 100, UnitPrice = 1_885m, GrossSubtotal = 188_500m, IvaRatePercent = 5m, IvaAmount = 9_425m, LineTotal = 197_925m },
        };
        var snapshots = items.Select(i => ApprovalBatchCandidateSnapshotFactory.Create(i, Guid.NewGuid(), null, Guid.NewGuid(), DateTime.UtcNow)).ToList();

        Assert.Equal(416_850m, snapshots.Sum(c => c.LineTotal));          // what the approval history showed (218.925,00 + 197.925,00)
        Assert.Equal(345_775.50m, quotation.TotalAmount);                 // what the quotation summary showed
        Assert.DoesNotContain(typeof(ApprovalBatchItemCandidate).GetProperties(BindingFlags.Public | BindingFlags.Instance), p => p.Name.Contains("Global", StringComparison.Ordinal) || p.Name.Contains("QuotationDiscount", StringComparison.Ordinal));
    }

    // ── 3. The supported shape flows unchanged to candidates, groups and the approved total ──

    [Fact]
    public async Task Per_line_discounts_are_line_resolved_and_approval_freezes_the_payable_total()
    {
        var s = await SeedAsync();
        var saved = await SaveAsync(s, LuandayDto(s, lineDiscounts: new[] { 37_530m, 30_160m }, globalDiscount: 0m));

        Assert.Equal(345_775.50m, saved.TotalAmount);
        Assert.Equal(0m, saved.DiscountAmount);
        Assert.Equal(67_690m, saved.TotalDiscountAmount);
        Assert.Equal(new[] { 179_518.50m, 166_257m }, saved.Items.OrderBy(i => i.LineNumber).Select(i => i.LineTotal).ToArray());
        Assert.True(saved.IsLinePricingResolved);
        Assert.Null(saved.LinePricingReasonCode);
        Assert.Equal(345_775.50m, saved.ConsideredLineTotalSum);
        var ids = saved.Items.OrderBy(i => i.LineNumber).Select(i => i.Id).ToArray();

        Guid batchId;
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var created = Assert.IsType<OkObjectResult>(await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { ids[0] }), (s.Line2, new[] { ids[1] }))));
            batchId = Assert.IsType<ApprovalBatchDto>(created.Value).Id;
        }
        await using (var verify = new ApplicationDbContext(s.Options))
        {
            var candidates = await verify.ApprovalBatchItemCandidates.AsNoTracking().OrderBy(c => c.QuotedDescription).ToListAsync();
            Assert.Equal(new[] { 37_530m, 30_160m }, candidates.OrderBy(c => c.LineTotal == 179_518.50m ? 0 : 1).Select(c => c.DiscountAmount).ToArray());
            Assert.Equal(345_775.50m, candidates.Sum(c => c.LineTotal));
        }

        // Area approval: one candidate per line → winners; groups built from the frozen snapshots.
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var batchItems = await ctx.ApprovalBatchItems.AsNoTracking().Include(bi => bi.Candidates).Where(bi => bi.ApprovalBatchId == batchId).ToListAsync();
            var dto = new BatchApprovalActionDto
            {
                BudgetJustification = "Justificativa orçamental de teste com tamanho suficiente para o gate.",
                Selections = batchItems.Select(bi => new BatchWinnerSelectionDto { ApprovalBatchItemId = bi.Id, SelectedCandidateId = bi.Candidates.Single().Id }).ToList(),
                ItemAssignments = batchItems.ToDictionary(bi => bi.RequestLineItemId, _ => new ItemApprovalAssignmentDto { PlantId = PLANT, CostCenterId = 1 })
            };
            Assert.IsType<OkObjectResult>(await Batches(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto));
        }
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await Batches(ctx, s.Actor).BatchFinalApprove(s.RequestId, batchId, new BatchApprovalActionDto { Comment = "Aprovado." }));
        }

        await using var final = new ApplicationDbContext(s.Options);
        var group = Assert.Single(await final.RequestPoGroups.AsNoTracking().Where(g => g.ApprovalBatchId == batchId).ToListAsync());
        Assert.Equal(345_775.50m, group.TotalAmount);
        var batch = await final.ApprovalBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
        Assert.Equal(345_775.50m, batch.ApprovedTotalAmount);
        var request = await final.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId);
        Assert.Equal(345_775.50m, request.ApprovedTotalAmount);
        var quotation = await final.Quotations.AsNoTracking().SingleAsync(q => q.Id == saved.Id);
        Assert.Equal(345_775.50m, quotation.TotalAmount);   // summary, approval and group agree
    }

    // ── 4. Partial batches: the share of a global discount is undefined → refused; a line-resolved partial batch stays coherent ──

    [Fact]
    public async Task A_partial_batch_from_a_quotation_with_a_global_discount_is_refused_and_a_line_resolved_partial_batch_is_coherent()
    {
        var s = await SeedAsync();
        var (_, globalItem1, _) = await SeedQuotationAsync(s, total: 345_775.50m, globalDiscount: 67_690m, line1Total: 218_925m, line2Total: 197_925m);
        var (_, resolvedItem1, _) = await SeedQuotationAsync(s, total: 345_775.50m, globalDiscount: 0m, line1Total: 179_518.50m, line2Total: 166_257m, supplierName: "OUTRO FORNECEDOR ZZ", supplierId: 9596);

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var r = await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { globalItem1 })));
            Assert.IsType<ConflictObjectResult>(r);
            Assert.Equal(QuotationLinePricingPolicy.ConflictCode, Code(r));
            var quotations = Assert.IsAssignableFrom<System.Collections.IEnumerable>(Problem(r).Extensions["quotations"]).Cast<object>().ToList();
            Assert.Single(quotations);
        }
        // Mixed payload (one resolved + one unresolved candidate for the same line) is refused as a whole.
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var r = await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { resolvedItem1, globalItem1 })));
            Assert.Equal(QuotationLinePricingPolicy.ConflictCode, Code(r));
            Assert.Equal(0, await ctx.ApprovalBatches.CountAsync());
        }
        // The line-resolved quotation alone: a partial batch (line 1 only) is accepted and the group total is that line's total.
        Guid batchId;
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var ok = Assert.IsType<OkObjectResult>(await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { resolvedItem1 }))));
            batchId = Assert.IsType<ApprovalBatchDto>(ok.Value).Id;
        }
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var bi = await ctx.ApprovalBatchItems.AsNoTracking().Include(x => x.Candidates).SingleAsync(x => x.ApprovalBatchId == batchId);
            var dto = new BatchApprovalActionDto
            {
                BudgetJustification = "Justificativa orçamental de teste com tamanho suficiente para o gate.",
                Selections = new List<BatchWinnerSelectionDto> { new() { ApprovalBatchItemId = bi.Id, SelectedCandidateId = bi.Candidates.Single().Id } },
                ItemAssignments = new Dictionary<Guid, ItemApprovalAssignmentDto> { [s.Line1] = new() { PlantId = PLANT, CostCenterId = 1 } }
            };
            Assert.IsType<OkObjectResult>(await Batches(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto));
        }
        await using var verify = new ApplicationDbContext(s.Options);
        var group = Assert.Single(await verify.RequestPoGroups.AsNoTracking().Where(g => g.ApprovalBatchId == batchId).ToListAsync());
        Assert.Equal(179_518.50m, group.TotalAmount);
    }

    // ── 5. Legacy drift without a global discount has its own reason ──

    [Fact]
    public async Task A_legacy_line_sum_mismatch_without_global_discount_is_refused_with_LINE_TOTAL_MISMATCH()
    {
        var s = await SeedAsync();
        // The v2.226.2 reproduction figures: client total 1.404.060 persisted while the lines add up to 1.508.220.
        var (_, item1, item2) = await SeedQuotationAsync(s, total: 1_404_060m, globalDiscount: 0m, line1Total: 753_080m, line2Total: 755_140m);
        await using var ctx = new ApplicationDbContext(s.Options);
        var r = await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { item1 }), (s.Line2, new[] { item2 })));
        Assert.Equal(QuotationLinePricingPolicy.ConflictCode, Code(r));
        Assert.Contains("não coincide com o total da cotação", Problem(r).Detail);
        Assert.Equal(0, await ctx.ApprovalBatches.CountAsync());
    }

    // ── 6. Rework (UpdateBatch) cannot re-admit a quotation-level discount ──

    [Fact]
    public async Task UpdateBatch_in_area_adjustment_refuses_a_candidate_from_a_quotation_with_a_global_discount()
    {
        var s = await SeedAsync();
        var (_, resolvedItem1, _) = await SeedQuotationAsync(s, total: 345_775.50m, globalDiscount: 0m, line1Total: 179_518.50m, line2Total: 166_257m);
        var (_, globalItem1, _) = await SeedQuotationAsync(s, total: 345_775.50m, globalDiscount: 67_690m, line1Total: 218_925m, line2Total: 197_925m, supplierName: "OUTRO FORNECEDOR ZZ", supplierId: 9597);

        Guid batchId;
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var ok = Assert.IsType<OkObjectResult>(await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { resolvedItem1 }))));
            batchId = Assert.IsType<ApprovalBatchDto>(ok.Value).Id;
            var batch = await ctx.ApprovalBatches.SingleAsync(b => b.Id == batchId);
            batch.Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment;   // returned by the Area approver for rework
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var r = await Batches(ctx, s.Actor).UpdateBatch(s.RequestId, batchId, new UpdateApprovalBatchDto
            {
                Items = new List<BatchItemDto> { new() { RequestLineItemId = s.Line1, Candidates = { new BatchCandidateInputDto { QuotationItemId = resolvedItem1 }, new BatchCandidateInputDto { QuotationItemId = globalItem1 } } } }
            });
            Assert.IsType<ConflictObjectResult>(r);
            Assert.Equal(QuotationLinePricingPolicy.ConflictCode, Code(r));
        }
        await using var verify = new ApplicationDbContext(s.Options);
        var candidates = await verify.ApprovalBatchItemCandidates.AsNoTracking().ToListAsync();
        Assert.Single(candidates);                                         // the rework wrote nothing
        Assert.Equal(resolvedItem1, candidates[0].QuotationItemId);
    }

    // ── 7. The details read exposes the verdict per quotation ──

    [Fact]
    public async Task The_request_details_expose_the_line_pricing_verdict_and_the_global_discount_of_each_quotation()
    {
        var s = await SeedAsync();
        var (globalQ, _, _) = await SeedQuotationAsync(s, total: 345_775.50m, globalDiscount: 67_690m, line1Total: 218_925m, line2Total: 197_925m);
        var (resolvedQ, _, _) = await SeedQuotationAsync(s, total: 345_775.50m, globalDiscount: 0m, line1Total: 179_518.50m, line2Total: 166_257m, supplierName: "OUTRO FORNECEDOR ZZ", supplierId: 9598);

        await using var ctx = new ApplicationDbContext(s.Options);
        var details = Assert.IsType<RequestDetailsDto>(Assert.IsType<OkObjectResult>((await Requests(ctx, s.Actor).GetRequest(s.RequestId)).Result).Value);

        var g = details.Quotations.Single(q => q.Id == globalQ);
        Assert.False(g.IsLinePricingResolved);
        Assert.Equal(QuotationLinePricingPolicy.Reasons.GlobalDiscountUnallocated, g.LinePricingReasonCode);
        Assert.Equal(67_690m, g.DiscountAmount);
        Assert.Equal(416_850m, g.ConsideredLineTotalSum);
        Assert.Equal(345_775.50m, g.TotalAmount);

        var r = details.Quotations.Single(q => q.Id == resolvedQ);
        Assert.True(r.IsLinePricingResolved);
        Assert.Null(r.LinePricingReasonCode);
        Assert.Equal(345_775.50m, r.ConsideredLineTotalSum);
    }

    // ── 8. Write-path boundaries (follow-up): the server computes and validates the line pricing itself ──

    [Fact]
    public async Task A_zero_or_omitted_DTO_header_cannot_bypass_the_rule_because_the_server_computes_the_totals_from_the_lines()
    {
        // Per-line discounts, every DTO header total 0 (omitted), no OCR total: the persisted total is still 345.775,50.
        var s = await SeedAsync();
        var dto = LuandayDto(s, new[] { 37_530m, 30_160m }, 0m, withOcrTotal: false);
        dto.TotalAmount = 0m; dto.TotalGrossAmount = 0m; dto.TotalIvaAmount = 0m;
        var saved = await SaveAsync(s, dto);
        Assert.Equal(345_775.50m, saved.TotalAmount);
        Assert.Equal(397_000m, saved.TotalGrossAmount);
        Assert.Equal(16_465.50m, saved.TotalIvaAmount);
        Assert.Equal(QuotationLinePricingPolicy.Kinds.LineResolved, saved.LinePricingKind);
        Assert.True(saved.IsLinePricingResolved);

        // The discount at quotation level with a zero DTO header: the total is still computed (never 0) and the verdict is
        // unresolved — a zero/omitted header cannot turn an unresolved quotation into a "never computed" one.
        var s2 = await SeedAsync();
        var dto2 = LuandayDto(s2, new[] { 0m, 0m }, 67_690m, withOcrTotal: false);
        dto2.TotalAmount = 0m; dto2.TotalGrossAmount = 0m; dto2.TotalIvaAmount = 0m;
        var saved2 = await SaveAsync(s2, dto2);
        Assert.Equal(345_775.50m, saved2.TotalAmount);
        Assert.False(saved2.IsLinePricingResolved);
        Assert.Equal(QuotationLinePricingPolicy.Kinds.GlobalDiscountUnallocated, saved2.LinePricingKind);
        await using var verify = new ApplicationDbContext(s2.Options);
        Assert.Equal(345_775.50m, (await verify.Quotations.AsNoTracking().SingleAsync(q => q.Id == saved2.Id)).TotalAmount);

        // The DTO carries no line-level total at all: nothing line-level from the client can be trusted or persisted as-is.
        Assert.DoesNotContain(typeof(SaveQuotationItemDto).GetProperties(BindingFlags.Public | BindingFlags.Instance), p => p.Name is "LineTotal" or "TotalPrice" or "IvaAmount" or "GrossSubtotal");
    }

    [Fact]
    public async Task A_global_discount_above_the_remaining_net_is_refused_on_save_update_and_preview_never_clamped_and_a_line_discount_above_gross_on_save()
    {
        var s = await SeedAsync();
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            // Net after the line discounts = 329.310,00; a global discount of 329.310,01 → 400, nothing written (no zero taxable base).
            var r = await Requests(ctx, s.Actor).SaveQuotation(s.RequestId, null, LuandayDto(s, new[] { 37_530m, 30_160m }, 329_310.01m, withOcrTotal: false));
            var pd = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(r.Result).Value);
            Assert.Equal(QuotationLinePricingPolicy.GlobalDiscountExceedsNetCode, pd.Extensions["code"]);
            Assert.Equal(329_310m, pd.Extensions["netAfterItemDiscounts"]);
            Assert.Contains("Nada foi guardado", pd.Detail);
            Assert.Equal(0, await ctx.Quotations.CountAsync());
            Assert.Equal(0, await ctx.QuotationItems.CountAsync());

            // The read-only preview answers the same refusal (it mirrors Save/Update arithmetic).
            var p = await Requests(ctx, s.Actor).PreviewQuotationReconciliation(s.RequestId, null, LuandayDto(s, new[] { 37_530m, 30_160m }, 329_310.01m), default);
            Assert.Equal(QuotationLinePricingPolicy.GlobalDiscountExceedsNetCode, Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(p.Result).Value).Extensions["code"]);

            // A line discount above the line's gross is refused on save too (same rule UpdateQuotation already had).
            var r2 = await Requests(ctx, s.Actor).SaveQuotation(s.RequestId, null, LuandayDto(s, new[] { 208_500.01m, 0m }, 0m, withOcrTotal: false));
            Assert.Contains("não pode exceder o subtotal bruto", Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(r2.Result).Value).Detail);
            Assert.Equal(0, await ctx.Quotations.CountAsync());
        }

        // UpdateQuotation: a saved line-resolved quotation cannot be edited into an excessive global discount; persisted values unchanged.
        var saved = await SaveAsync(s, LuandayDto(s, new[] { 37_530m, 30_160m }, 0m, withOcrTotal: false));
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var r = await Requests(ctx, s.Actor).UpdateQuotation(s.RequestId, saved.Id, LuandayDto(s, new[] { 37_530m, 30_160m }, 400_000m, withOcrTotal: false));
            Assert.Equal(QuotationLinePricingPolicy.GlobalDiscountExceedsNetCode, Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(r.Result).Value).Extensions["code"]);
        }
        await using var verify = new ApplicationDbContext(s.Options);
        var q = await verify.Quotations.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == saved.Id);
        Assert.Equal(0m, q.DiscountAmount);
        Assert.Equal(345_775.50m, q.TotalAmount);
        Assert.Equal(new[] { 179_518.50m, 166_257m }, q.Items.OrderBy(i => i.LineNumber).Select(i => i.LineTotal).ToArray());
    }

    [Fact]
    public async Task Mixed_VAT_rates_an_existing_line_discount_and_a_zero_value_line_are_recomputed_per_line_by_the_server_and_the_allocated_shape_equals_the_global_discount_total()
    {
        // Line A 10 × 1.000,00 (desc 500,00, IVA 14 %) → 9.500,00 + 1.330,00 = 10.830,00; line B 3 × 2.000,00 (IVA 5 %) → 6.300,00;
        // line C 1 × 0,00 → 0. Global discount 1.000,00 → taxable 14.500,00, IVA 1.630,00 × 14.500/15.500 = 1.524,84, total 16.024,84.
        var s = await SeedAsync();
        var global = await SaveAsync(s, MixedDto(s, new[] { 500m, 0m, 0m }, 1_000m));
        Assert.Equal(16_000m, global.TotalGrossAmount);
        Assert.Equal(1_500m, global.TotalDiscountAmount);       // 500 line + 1.000 global
        Assert.Equal(14_500m, global.TotalTaxableBase);
        Assert.Equal(1_524.84m, global.TotalIvaAmount);
        Assert.Equal(16_024.84m, global.TotalAmount);
        Assert.Equal(new[] { 10_830m, 6_300m, 0m }, global.Items.OrderBy(i => i.LineNumber).Select(i => i.LineTotal).ToArray());
        Assert.False(global.IsLinePricingResolved);
        Assert.Equal(17_130m, global.ConsideredLineTotalSum);   // what approval would have frozen: 1.105,16 above the quotation total

        // The wizard's proportional allocation of that 1.000,00 (by net 9.500 / 6.000; the zero-value line receives nothing):
        // A 500 + 612,90 → 8.887,10 + 1.244,19 = 10.131,29; B 387,10 → 5.612,90 + 280,65 = 5.893,55; C 0 → Σ 16.024,84.
        // Same total as the global-discount arithmetic (proportional-to-net shares scale every line's IVA by the same ratio); the
        // server recomputes every line itself (the DTO has no line totals) and the quotation becomes line-resolved.
        var s2 = await SeedAsync();
        var allocated = await SaveAsync(s2, MixedDto(s2, new[] { 1_112.90m, 387.10m, 0m }, 0m));
        var lines = allocated.Items.OrderBy(i => i.LineNumber).ToArray();
        Assert.Equal(new[] { 1_244.19m, 280.65m, 0m }, lines.Select(i => i.IvaAmount).ToArray());
        Assert.Equal(new[] { 10_131.29m, 5_893.55m, 0m }, lines.Select(i => i.LineTotal).ToArray());
        Assert.Equal(1_500m, allocated.TotalDiscountAmount);
        Assert.Equal(14_500m, allocated.TotalTaxableBase);
        Assert.Equal(1_524.84m, allocated.TotalIvaAmount);
        Assert.Equal(16_024.84m, allocated.TotalAmount);
        Assert.True(allocated.IsLinePricingResolved);
        Assert.Equal(QuotationLinePricingPolicy.Kinds.LineResolved, allocated.LinePricingKind);
        Assert.Equal(16_024.84m, allocated.ConsideredLineTotalSum);

        // A NON-proportional document (the whole 1.000,00 printed on the 14 % line) has a different IVA: 1.190,00 + 300,00 = 1.490,00,
        // total 15.990,00 — which is why an allocation must be reconciled against the document's own VAT/total, not only its discount.
        var s3 = await SeedAsync();
        var printed = await SaveAsync(s3, MixedDto(s3, new[] { 1_500m, 0m, 0m }, 0m));
        Assert.Equal(1_490m, printed.TotalIvaAmount);
        Assert.Equal(15_990m, printed.TotalAmount);
        Assert.True(printed.IsLinePricingResolved);
    }

    [Fact]
    public async Task An_EXTRA_ITEM_line_of_an_unresolved_quotation_is_refused_before_the_extra_item_decision_and_before_any_snapshot()
    {
        var s = await SeedAsync();
        async Task<(Guid Q, Guid Mapped, Guid Extra)> SeedWithExtraAsync(decimal globalDiscount, string doc, int supplierId, string supplierName)
        {
            await using var ctx = new ApplicationDbContext(s.Options);
            if (!await ctx.Suppliers.AnyAsync(x => x.Id == supplierId))
                ctx.Suppliers.Add(new Supplier { Id = supplierId, Name = supplierName, TaxId = "54" + supplierId, RegistrationStatus = "ACTIVE", PortalCode = "SUP-" + supplierId, IsActive = true });
            var q = new Quotation { Id = Guid.NewGuid(), RequestId = s.RequestId, SupplierId = supplierId, SupplierNameSnapshot = supplierName, DocumentNumber = doc, Currency = "AOA", SourceType = "OCR",
                TotalAmount = 223_925m - globalDiscount, DiscountAmount = globalDiscount, CreatedAtUtc = DateTime.UtcNow.AddHours(-1), CreatedByUserId = s.Actor };
            var mapped = new QuotationItem { Id = Guid.NewGuid(), QuotationId = q.Id, LineNumber = 1, Description = "AGUA MESA PURA MB 6X1,5L", Quantity = 150, UnitPrice = 1_390m, GrossSubtotal = 208_500m, IvaRatePercent = 5m, IvaAmount = 10_425m, LineTotal = 218_925m, MappedRequestLineItemId = s.Line1, ReconciliationStatus = "MAPPED", UnitId = UNIT_CX, IvaRateId = IVA5 };
            var extra = new QuotationItem { Id = Guid.NewGuid(), QuotationId = q.Id, LineNumber = 2, Description = "GARRAFAS DE OFERTA", Quantity = 1, UnitPrice = 5_000m, GrossSubtotal = 5_000m, IvaRatePercent = 0m, IvaAmount = 0m, LineTotal = 5_000m, MappedRequestLineItemId = null, ReconciliationStatus = "EXTRA_ITEM", ReconciliationJustification = "Linha adicional do documento sem correspondência no pedido.", UnitId = UNIT_CX };
            ctx.Quotations.Add(q); ctx.QuotationItems.AddRange(mapped, extra);
            await ctx.SaveChangesAsync();
            return (q.Id, mapped.Id, extra.Id);
        }

        var (_, unresolvedMapped, _) = await SeedWithExtraAsync(globalDiscount: 1_000m, doc: DOC, supplierId: SUPPLIER, supplierName: "LUANDAY - COMERCIO E SERVIÇOS, LDA");
        var (_, resolvedMapped, resolvedExtra) = await SeedWithExtraAsync(globalDiscount: 0m, doc: DOC + "-R", supplierId: 9600, supplierName: "OUTRO FORNECEDOR ZZ");

        // Unresolved quotation, EXTRA_ITEM line without a decision: the pricing refusal comes FIRST — not EXTRA_ITEMS_PENDING_DECISION —
        // and nothing is written (no batch, no candidate, no extra-item decision, no generated request line).
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var r = await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { unresolvedMapped })));
            Assert.Equal(QuotationLinePricingPolicy.ConflictCode, Code(r));
            Assert.Equal(0, await ctx.ApprovalBatches.CountAsync());
            Assert.Equal(0, await ctx.ApprovalBatchItemCandidates.CountAsync());
            Assert.Equal(0, await ctx.ApprovalBatchExtraItemDecisions.CountAsync());
            Assert.Equal(3, await ctx.RequestLineItems.CountAsync(l => l.RequestId == s.RequestId));
        }
        // Line-resolved quotation, same payload: the extra-item decision is the next gate (proves the pricing rule sits upstream of the
        // only path that freezes an extra line's snapshot — IBatchExtraItemDecisionService.ApplyAsync inside CreateBatch/UpdateBatch).
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var r = await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { resolvedMapped })));
            Assert.Equal("EXTRA_ITEMS_PENDING_DECISION", Code(r));
            Assert.Equal(0, await ctx.ApprovalBatches.CountAsync());
        }
        // With the INCLUDE decision the extra line is frozen at its own line total (5.000,00), the mapped one at 218.925,00.
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var dto = BatchOf((s.Line1, new[] { resolvedMapped }));
            dto.ExtraItemDecisions = new Dictionary<Guid, ExtraItemDecisionDto> { [resolvedExtra] = new() { Decision = "INCLUDE", Comment = "Incluir a linha adicional da proforma no lote." } };
            Assert.IsType<OkObjectResult>(await Batches(ctx, s.Actor).CreateBatch(s.RequestId, dto));
            var candidates = await ctx.ApprovalBatchItemCandidates.AsNoTracking().ToListAsync();
            Assert.Equal(new[] { 5_000m, 218_925m }, candidates.Select(c => c.LineTotal).OrderBy(x => x).ToArray());
        }
    }

    [Fact]
    public async Task A_legacy_zero_total_with_positive_lines_and_a_zero_value_quotation_are_classified_explicitly_and_the_legacy_one_stays_submittable()
    {
        var s = await SeedAsync();
        var (legacyQ, legacyItem1, _) = await SeedQuotationAsync(s, total: 0m, globalDiscount: 0m, line1Total: 218_925m, line2Total: 197_925m);
        var (zeroQ, _, _) = await SeedQuotationAsync(s, total: 0m, globalDiscount: 0m, line1Total: 0m, line2Total: 0m, supplierName: "OUTRO FORNECEDOR ZZ", supplierId: 9601);

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var details = Assert.IsType<RequestDetailsDto>(Assert.IsType<OkObjectResult>((await Requests(ctx, s.Actor).GetRequest(s.RequestId)).Result).Value);
            var legacy = details.Quotations.Single(q => q.Id == legacyQ);
            Assert.True(legacy.IsLinePricingResolved);
            Assert.Equal(QuotationLinePricingPolicy.Kinds.LegacyTotalNotComputed, legacy.LinePricingKind);
            Assert.Equal(416_850m, legacy.ConsideredLineTotalSum);
            var zero = details.Quotations.Single(q => q.Id == zeroQ);
            Assert.True(zero.IsLinePricingResolved);
            Assert.Equal(QuotationLinePricingPolicy.Kinds.ZeroValue, zero.LinePricingKind);
        }
        // Compatibility: the legacy row is submittable and approval values the (well-defined) line total.
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await Batches(ctx, s.Actor).CreateBatch(s.RequestId, BatchOf((s.Line1, new[] { legacyItem1 }))));
            Assert.Equal(218_925m, (await ctx.ApprovalBatchItemCandidates.AsNoTracking().SingleAsync()).LineTotal);
        }
    }
}
