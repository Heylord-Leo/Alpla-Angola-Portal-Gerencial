using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Controller-level integration test for SaveQuotation with an IGNORED document line — the exact
/// runtime scenario that failed: the frontend previously DROPPED ignored lines from the payload,
/// so the Financial Integrity Gate compared the full-document OCR total against considered-only
/// totals (excludedIgnoredTotal = 0, false divergence equal to the ignored line's IVA-inclusive
/// value). The DTO here carries the ignored line exactly as the fixed frontend now emits it.
///
/// Runs against SQL Server (LocalDB) with the real controller pipeline (normalization →
/// validation → item construction with IVA → considered-only totals → integrity gate →
/// persistence). Skipped when LocalDB is unavailable; cleans up its own rows.
/// </summary>
public class SaveQuotationIgnoredLineIntegrationTests
{
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();

    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var adminLog = new AdminLogWriter(
            new Mock<IServiceScopeFactory>().Object,
            new Mock<IHttpContextAccessor>().Object,
            NullLogger<AdminLogWriter>.Instance);

        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            adminLog,
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object,
            new Mock<IApprovalRoutingService>().Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new AlplaPortal.Infrastructure.Services.Purchasing.QuotationItemEligibilityService(ctx),
            new AlplaPortal.Infrastructure.Services.Approvals.BatchExtraItemDecisionService(ctx),
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Microsoft.Extensions.Options.Options.Create(new AlplaPortal.Domain.Configuration.PostPaymentCompletionOptions()));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
            new Claim(ClaimTypes.Role, "System Administrator")
        }, "Test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private sealed record Seeded(Guid RequestId, Guid LineItemId, Guid Actor, int SupplierId, int IvaRate14Id);

    private static async Task<Seeded?> SeedAsync()
    {
        await using var ctx = new ApplicationDbContext(Options());
        var actor = await ctx.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
        if (actor == Guid.Empty) return null;
        // Own EXTERNAL supplier (removed by CleanupAsync). The previous "first supplier" pick broke
        // on a model-created sandbox whose first row is the internal "Alpla Global Services" —
        // InternalCompanyGuard then (correctly) rejects the quotation with 400. A ZZTEST name with
        // a unique NIF can never resolve as an internal company.
        var supplier = new Supplier
        {
            // Unique per seed — Suppliers.Name carries a unique index and tests may run in parallel.
            Name = "ZZTEST Fornecedor Externo " + Guid.NewGuid().ToString("N")[..8],
            TaxId = "ZZ" + Guid.NewGuid().ToString("N")[..9],
            IsActive = true
        };
        ctx.Suppliers.Add(supplier);
        var iva14 = await ctx.IvaRates.AsNoTracking().Where(i => i.RatePercent == 14m).Select(i => i.Id).FirstOrDefaultAsync();
        if (iva14 == 0) return null;

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_QUOTATION").Select(s => s.Id).FirstAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST_IGN_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-IGN-" + Guid.NewGuid().ToString("N")[..8],
            StatusId = statusId,
            RequestTypeId = typeId,
            DepartmentId = 4,
            CompanyId = 1,
            PlantId = 1,
            CurrencyId = 1,
            RequesterId = actor,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var li = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            LineNumber = 1,
            Description = "ZZTEST requested item",
            Quantity = 1,
            UnitPrice = 0,
            TotalAmount = 0,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor
        };
        ctx.RequestLineItems.Add(li);
        await ctx.SaveChangesAsync();
        return new Seeded(request.Id, li.Id, actor, supplier.Id, iva14);
    }

    private static async Task CleanupAsync(Guid requestId, int supplierId)
    {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id=qi.QuotationId WHERE q.RequestId={0};" +
            "DELETE FROM Quotations WHERE RequestId={0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId={0};" +
            "DELETE FROM RequestLineItems WHERE RequestId={0};" +
            "DELETE FROM Requests WHERE Id={0};" +
            // ONLY this seed's own ZZTEST supplier (id-scoped: name-pattern deletes raced against
            // the other SaveQuotation suite running in parallel and removed its just-seeded row).
            "DELETE FROM Suppliers WHERE Id={1} AND NOT EXISTS (SELECT 1 FROM Quotations q WHERE q.SupplierId = Suppliers.Id);", requestId, supplierId);
    }

    /// <summary>DTO items exactly as the FIXED frontend emits them (camel-cased over HTTP → these DTOs).</summary>
    private static SaveQuotationRequestDto BuildDto(Seeded s, string? ignoredJustification)
        => new()
        {
            SupplierId = s.SupplierId,
            SupplierNameSnapshot = "ZZTEST Supplier",
            DocumentNumber = "FP-ZZTEST",
            DocumentDate = DateTime.UtcNow,
            Currency = "AOA",
            DiscountAmount = 0,
            TotalAmount = 49875m,
            SourceType = "OCR",
            OcrTotal = 86811m, // full-document OCR total (includes the ignored line's IVA-inclusive value)
            Items = new List<SaveQuotationItemDto>
            {
                new()
                {
                    LineNumber = 1,
                    Description = "Linha mapeada",
                    Quantity = 1,
                    UnitPrice = 43750m,          // 43.750 × 1,14 = 49.875 (IVA 14%)
                    DiscountAmount = 0,
                    IvaRateId = s.IvaRate14Id,
                    // LineTotal is computed server-side
                    MappedRequestLineItemId = s.LineItemId,
                    ReconciliationStatus = "MAPPED",
                    ReconciliationJustification = null,
                    // An OCR line MUST carry its extraction baseline (enforced since the
                    // AddQuotationItemOcrBaseline wave; the real frontend always sends it).
                    // Baseline == entered values ⇒ no material financial edit ⇒ no per-line
                    // adjustment reason required — exactly the scenario under test.
                    LineOrigin = "OCR",
                    OcrOriginalQuantity = 1,
                    OcrOriginalUnitPrice = 43750m,
                    OcrOriginalDiscountAmount = 0,
                    OcrOriginalIvaRatePercent = 14m,
                    OcrOriginalLineTotal = 49875m
                },
                new()
                {
                    LineNumber = 2,
                    Description = "Linha ignorada",
                    Quantity = 1,
                    UnitPrice = 32400m,          // 32.400 × 1,14 = 36.936 (IVA 14%)
                    DiscountAmount = 0,
                    IvaRateId = s.IvaRate14Id,
                    // LineTotal is computed server-side
                    MappedRequestLineItemId = null,
                    ReconciliationStatus = "IGNORED",
                    ReconciliationJustification = ignoredJustification,
                    LineOrigin = "OCR",
                    OcrOriginalQuantity = 1,
                    OcrOriginalUnitPrice = 32400m,
                    OcrOriginalDiscountAmount = 0,
                    OcrOriginalIvaRatePercent = 14m,
                    OcrOriginalLineTotal = 36936m
                }
            }
        };

    [Fact] // Cenário runtime exato: ignorada com IVA + justificativa → gate passa, save OK, linha persistida
    public async Task SaveQuotation_WithJustifiedIgnoredLine_PassesGate_AndPersistsIgnoredLine()
    {
        if (!CanConnect()) return;
        var seeded = await SeedAsync();
        if (seeded == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var controller = BuildController(ctx, seeded.Actor);

            var result = await controller.SaveQuotation(seeded.RequestId, null,
                BuildDto(seeded, "JUSTIFICATIVA TESTE DE ITEM IGNORADO"));

            // Gate must NOT fire: comparable = 86.811 − 36.936 = 49.875 = considered total → variance 0.
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var saved = Assert.IsType<SavedQuotationDto>(ok.Value);
            Assert.Equal(49875m, saved.TotalAmount);            // IGNORED excluded from quotation totals
            Assert.Equal(2, saved.ItemCount);                    // both lines persisted

            await using var verify = new ApplicationDbContext(Options());
            var items = await verify.QuotationItems.AsNoTracking()
                .Where(qi => qi.QuotationId == saved.Id)
                .OrderBy(qi => qi.LineNumber)
                .ToListAsync();
            var ignored = Assert.Single(items, i => i.ReconciliationStatus == "IGNORED");
            Assert.Equal(36936m, ignored.LineTotal);             // IVA-inclusive value persisted
            Assert.Equal("JUSTIFICATIVA TESTE DE ITEM IGNORADO", ignored.ReconciliationJustification);

            var quotation = await verify.Quotations.AsNoTracking().FirstAsync(q => q.Id == saved.Id);
            Assert.Equal(49875m, quotation.TotalAmount);         // considered-only aggregate
        }
        finally { await CleanupAsync(seeded.RequestId, seeded.SupplierId); }
    }

    [Fact] // Ignorada com valor SEM justificativa → 400 (regra por linha), sem 409 genérico
    public async Task SaveQuotation_IgnoredLineWithValue_WithoutJustification_Returns400()
    {
        if (!CanConnect()) return;
        var seeded = await SeedAsync();
        if (seeded == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var controller = BuildController(ctx, seeded.Actor);

            var result = await controller.SaveQuotation(seeded.RequestId, null, BuildDto(seeded, null));

            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            var problem = Assert.IsType<ProblemDetails>(bad.Value);
            Assert.Equal("Justificativa Obrigatória", problem.Title);
        }
        finally { await CleanupAsync(seeded.RequestId, seeded.SupplierId); }
    }

    [Fact] // Status em caixa divergente ("ignored") é normalizado no boundary e tratado como IGNORED
    public async Task SaveQuotation_LowercaseIgnoredStatus_IsNormalized_AndGatePasses()
    {
        if (!CanConnect()) return;
        var seeded = await SeedAsync();
        if (seeded == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var controller = BuildController(ctx, seeded.Actor);

            var dto = BuildDto(seeded, "Justificativa para variante de caixa");
            dto.Items[1].ReconciliationStatus = " ignored ";     // variante com caixa/espaços

            var result = await controller.SaveQuotation(seeded.RequestId, null, dto);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var saved = Assert.IsType<SavedQuotationDto>(ok.Value);
            Assert.Equal(49875m, saved.TotalAmount);

            await using var verify = new ApplicationDbContext(Options());
            var ignored = await verify.QuotationItems.AsNoTracking()
                .FirstAsync(qi => qi.QuotationId == saved.Id && qi.LineNumber == 2);
            Assert.Equal("IGNORED", ignored.ReconciliationStatus); // persisted canonical
        }
        finally { await CleanupAsync(seeded.RequestId, seeded.SupplierId); }
    }
}
