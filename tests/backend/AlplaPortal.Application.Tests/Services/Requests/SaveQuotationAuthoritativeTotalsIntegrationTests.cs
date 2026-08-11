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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.226.2 pin: persisted quotation financial values come EXCLUSIVELY from components
/// (Quantity, UnitPrice, DiscountAmount, IvaRateId) — never from any client-computed total.
///
/// <para>The structural half is pinned by reflection: <see cref="SaveQuotationItemDto"/> carries
/// no line-total member at all, so a stale frontend total is unrepresentable on the wire. The
/// behavioural half runs the exact reproduction document through the real SaveQuotation pipeline
/// with a deliberately WRONG client header total (1,404,060 — the broken screen's value) and
/// asserts the server persists the component-computed truth (1,508,220 and the four gross line
/// totals). The document is the summary-IVA shape, so this also re-pins the v2.226.1
/// DocumentSummaryIvaCredit inside the save gate itself.</para>
///
/// <para>Runs against SQL Server (LocalDB) like the other SaveQuotation integration tests;
/// skipped when LocalDB is unavailable; cleans up its own rows.</para>
/// </summary>
public class SaveQuotationAuthoritativeTotalsIntegrationTests
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

    private sealed record Seeded(Guid RequestId, List<Guid> LineItemIds, Guid Actor, int SupplierId, int IvaRate14Id);

    private static async Task<Seeded?> SeedAsync()
    {
        await using var ctx = new ApplicationDbContext(Options());
        var actor = await ctx.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
        if (actor == Guid.Empty) return null;
        var supplierId = await ctx.Suppliers.AsNoTracking().Select(s => s.Id).FirstOrDefaultAsync();
        if (supplierId == 0) return null;
        var iva14 = await ctx.IvaRates.AsNoTracking().Where(i => i.RatePercent == 14m).Select(i => i.Id).FirstOrDefaultAsync();
        if (iva14 == 0) return null;

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_QUOTATION").Select(s => s.Id).FirstAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST_AUTH_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-AUTH-" + Guid.NewGuid().ToString("N")[..8],
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

        var lineItemIds = new List<Guid>();
        for (var n = 1; n <= 4; n++)
        {
            var li = new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                LineNumber = n,
                Description = "ZZTEST requested item " + n,
                Quantity = 1,
                UnitPrice = 0,
                TotalAmount = 0,
                IsDeleted = false,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actor
            };
            ctx.RequestLineItems.Add(li);
            lineItemIds.Add(li.Id);
        }
        await ctx.SaveChangesAsync();
        return new Seeded(request.Id, lineItemIds, actor, supplierId, iva14);
    }

    private static async Task CleanupAsync(Guid requestId)
    {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id=qi.QuotationId WHERE q.RequestId={0};" +
            "DELETE FROM Quotations WHERE RequestId={0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId={0};" +
            "DELETE FROM RequestLineItems WHERE RequestId={0};" +
            "DELETE FROM Requests WHERE Id={0};", requestId);
    }

    private static SaveQuotationItemDto OcrLine(
        Seeded s, int line, string description, decimal qty, decimal unitPrice, Guid mappedId) => new()
    {
        LineNumber = line,
        Description = description,
        Quantity = qty,
        UnitPrice = unitPrice,
        DiscountAmount = 0,
        IvaRateId = s.IvaRate14Id,
        MappedRequestLineItemId = mappedId,
        ReconciliationStatus = "MAPPED",
        // The summary-IVA document shape: OCR extracted net components, NO per-line rate.
        LineOrigin = QuotationLineOrigins.Ocr,
        OcrOriginalQuantity = qty,
        OcrOriginalUnitPrice = unitPrice,
        OcrOriginalDiscountAmount = 0,
        OcrOriginalIvaRatePercent = null,
        OcrOriginalLineTotal = qty * unitPrice
    };

    [Fact] // Structural pin: a client-computed line total is UNREPRESENTABLE on the wire.
    public void The_save_item_dto_carries_no_client_computed_total()
    {
        var members = typeof(SaveQuotationItemDto).GetProperties().Select(p => p.Name).ToList();

        foreach (var forbidden in new[] { "TotalPrice", "LineTotal", "GrossSubtotal", "IvaAmount", "TaxableBase" })
        {
            Assert.DoesNotContain(forbidden, members);
        }
    }

    [Fact] // Behavioural pin: wrong client header total; components win; the exact reproduction values persist.
    public async Task SaveQuotation_IgnoresClientTotals_AndPersistsComponentComputedValues()
    {
        if (!CanConnect()) return;
        var seeded = await SeedAsync();
        if (seeded == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var controller = BuildController(ctx, seeded.Actor);

            var dto = new SaveQuotationRequestDto
            {
                SupplierId = seeded.SupplierId,
                SupplierNameSnapshot = "ZZTEST Supplier",
                DocumentNumber = "FP-ZZTEST-AUTH",
                DocumentDate = DateTime.UtcNow,
                Currency = "AOA",
                DiscountAmount = 0,
                // The broken screen's stale value, on purpose: the server must not trust it.
                TotalAmount = 1_404_060m,
                SourceType = "OCR",
                OcrTotal = 1_508_220m,   // the document's own header (net lines + summary IVA)
                Items = new List<SaveQuotationItemDto>
                {
                    OcrLine(seeded, 1, "Rolamento industrial 6205-2RS", 12, 18_500m, seeded.LineItemIds[0]),
                    OcrLine(seeded, 2, "Sensor fotoelétrico M18 24VDC", 6, 96_500m, seeded.LineItemIds[1]),
                    OcrLine(seeded, 3, "Kit de conectores industriais M12", 8, 29_250m, seeded.LineItemIds[2]),
                    OcrLine(seeded, 4, "Serviço de calibração de sensores", 4, 72_000m, seeded.LineItemIds[3])
                }
            };

            var result = await controller.SaveQuotation(seeded.RequestId, null, dto);

            // The save gate itself re-runs the v2.226.1 reconciliation: net baseline 1,323,000 +
            // summary-IVA credit 185,220 reconcile the 1,508,220 header → residual 0 → no 409.
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var saved = Assert.IsType<SavedQuotationDto>(ok.Value);
            Assert.Equal(1_508_220m, saved.TotalAmount);   // NOT the client's 1,404,060

            await using var verify = new ApplicationDbContext(Options());
            var items = await verify.QuotationItems.AsNoTracking()
                .Where(qi => qi.QuotationId == saved.Id)
                .OrderBy(qi => qi.LineNumber)
                .ToListAsync();

            Assert.Equal(4, items.Count);
            Assert.All(items, i => Assert.Equal(14m, i.IvaRatePercent));
            Assert.Equal(253_080m, items[0].LineTotal);   // 12 × 18,500 × 1.14
            Assert.Equal(660_060m, items[1].LineTotal);   //  6 × 96,500 × 1.14
            Assert.Equal(266_760m, items[2].LineTotal);   //  8 × 29,250 × 1.14
            Assert.Equal(328_320m, items[3].LineTotal);   //  4 × 72,000 × 1.14

            var quotation = await verify.Quotations.AsNoTracking().FirstAsync(q => q.Id == saved.Id);
            Assert.Equal(1_508_220m, quotation.TotalAmount);
        }
        finally { await CleanupAsync(seeded.RequestId); }
    }
}
