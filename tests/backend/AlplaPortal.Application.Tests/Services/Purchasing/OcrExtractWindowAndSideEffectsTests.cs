using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
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
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Purchasing;
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
/// v2.249.2 — <c>POST …/ocr-extract</c> on the restart-quotation shape (REQ-28/10/2026-628: group A cancelled "voltar a
/// cotar", line 1 released, line 2 approved and still bought, Finance confirmation PENDING). The v2.249.1 note said this
/// endpoint honoured the item-level quotation window; the committed gate was still scalar-only, so TEST answered
/// 409 "Ação Bloqueada" to the responsible Buyer. Now: Buyer/SysAdmin only; scalar window OR item-level window; the
/// reconciliation suggestions target the OPEN line only; nothing committed or historical is touched; a provider failure
/// leaves no partial extraction state.
/// </summary>
public class OcrExtractWindowAndSideEffectsTests
{
    private static RequestsController Controller(ApplicationDbContext ctx, Guid actorId, IDocumentExtractionService extraction, params string[] roles)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new ApprovalRoutingResultDto());
        var statusSync = new Mock<IRequestStatusSyncService>();
        statusSync.Setup(s => s.ComputeDisplayWorkflowStateAsync(It.IsAny<Guid>())).ReturnsAsync(string.Empty);
        var c = new RequestsController(
            ctx,
            extraction,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            statusSync.Object,
            routing.Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new QuotationItemEligibilityService(ctx),
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(new PostPaymentCompletionOptions()));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()) };
        claims.AddRange((roles.Length == 0 ? new[] { RoleConstants.Buyer } : roles).Select(r => new Claim(ClaimTypes.Role, r)));
        c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() }
        };
        return c;
    }

    private static IDocumentExtractionService Provider(ExtractionResultDto? result = null, Exception? throws = null)
    {
        var m = new Mock<IDocumentExtractionService>();
        var setup = m.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()));
        if (throws != null) setup.ThrowsAsync(throws);
        else setup.ReturnsAsync(result ?? Maqsis());
        return m.Object;
    }

    /// <summary>The TEST document: MAQSIS, 10 UN of "Rolo de filme stretch 500 mm - TEST" at 23 000,00 AOA.</summary>
    private static ExtractionResultDto Maqsis() => new()
    {
        Success = true, ProviderName = "TEST-PROVIDER", QualityScore = 0.92m,
        Header = new ExtractionHeaderDto { SupplierName = "MAQSIS - Maquinações Técnicas de Angola, Lda", SupplierTaxId = "5417093050", DocumentNumber = "9003", Currency = "AOA", TotalAmount = 230_000m },
        Items = new List<ExtractionLineItemDto> { new() { LineNumber = 1, Description = "Rolo de filme stretch 500 mm - TEST", Quantity = 10, Unit = "UN", UnitPrice = 23_000m, TotalPrice = 230_000m } },
    };

    private static IFormFile Pdf()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% TEST ONLY - Cotacao_TEST_MAQSIS_9003_REQ628\n%%EOF\n");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "Cotacao_TEST_MAQSIS_9003_REQ628.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    }

    private sealed record Snapshot(int OcrItems, int ReconRecords, int Histories, string? Line2Lifecycle, int Q1Items, decimal Qi1Price, int RequestStatusId, string FinanceStatus);
    private static async Task<Snapshot> SnapAsync(ApplicationDbContext ctx, S.Seed s) => new(
        await ctx.OcrExtractedItems.CountAsync(o => o.RequestId == s.RequestId),
        await ctx.ReconciliationRecords.CountAsync(r => r.RequestId == s.RequestId),
        await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId),
        (await ctx.RequestLineItems.AsNoTracking().SingleAsync(l => l.Id == s.Line2)).QuotationLifecycleStatus,
        await ctx.QuotationItems.CountAsync(qi => qi.QuotationId == s.Q1),
        (await ctx.QuotationItems.AsNoTracking().SingleAsync(qi => qi.Id == s.Qi1)).UnitPrice,
        (await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).StatusId,
        (await ctx.RequestPoGroupCancellations.AsNoTracking().SingleAsync(c => c.RequestPoGroupId == s.GroupA)).FinanceConfirmationStatus);

    private static async Task<S.Seed> SeedAndRestartAsync(ApplicationDbContext ctx)
    {
        var s = await S.SeedAsync(ctx);
        var (orch, _) = S.OrchestratorMock();
        Assert.True((await S.Service(ctx, orch.Object).CancelAsync(S.Cancel(s, S.Buyer(s.Owner)))).Succeeded);
        ctx.ChangeTracker.Clear();
        return s;
    }

    [Fact]
    public async Task Req628_shape_with_pending_confirmation_extracts_and_suggests_only_the_released_line()
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);
        var before = await SnapAsync(ctx, s);
        Assert.Equal(PoGroupCancellationConstants.FinanceConfirmationStatuses.Pending, before.FinanceStatus);

        var result = await Controller(ctx, s.Owner, Provider()).OcrExtract(s.RequestId, Pdf());
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<OcrExtractionResultDto>(ok.Value);
        Assert.True(dto.Success);

        ctx.ChangeTracker.Clear();
        var after = await SnapAsync(ctx, s);
        Assert.Equal(1, after.OcrItems);
        var records = await ctx.ReconciliationRecords.AsNoTracking().Where(r => r.RequestId == s.RequestId).ToListAsync();
        Assert.NotEmpty(records);
        Assert.All(records, r => Assert.NotEqual(s.Line2, r.RequesterItemId));      // never the committed sibling line
        Assert.Contains(records, r => r.RequesterItemId == s.Line1);                  // the released line is the target
        // Committed and historical state untouched; the pending confirmation is not consumed; scalar unchanged.
        Assert.Equal(before.Line2Lifecycle, after.Line2Lifecycle);
        Assert.Equal(before.Q1Items, after.Q1Items);
        Assert.Equal(before.Qi1Price, after.Qi1Price);
        Assert.Equal(before.RequestStatusId, after.RequestStatusId);
        Assert.Equal(PoGroupCancellationConstants.FinanceConfirmationStatuses.Pending, after.FinanceStatus);
        Assert.Equal(before.Histories + 1, after.Histories);                          // one OCR_EXTRACTION_RECONCILED row
    }

    [Fact]
    public async Task Request_without_an_eligible_window_is_refused_409_and_nothing_is_written()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx, requestStatus: "PO_ISSUED"); // both lines approved and bought, nothing released
        var before = await ctx.OcrExtractedItems.CountAsync(o => o.RequestId == s.RequestId);
        var result = await Controller(ctx, s.Owner, Provider()).OcrExtract(s.RequestId, Pdf());
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("Não é possível realizar extração OCR neste status do pedido.", ((ProblemDetails)conflict.Value!).Detail);
        Assert.Equal(before, await ctx.OcrExtractedItems.CountAsync(o => o.RequestId == s.RequestId));
    }

    [Theory]
    [InlineData(RoleConstants.Finance)]
    [InlineData(RoleConstants.Requester)]
    public async Task Non_buyer_actors_are_refused_403(string role)
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);
        var result = await Controller(ctx, s.Owner, Provider(), role).OcrExtract(s.RequestId, Pdf());
        var forbidden = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Equal(0, await ctx.OcrExtractedItems.CountAsync(o => o.RequestId == s.RequestId));
    }

    [Fact]
    public async Task SysAdmin_is_allowed_like_the_Buyer()
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);
        var result = await Controller(ctx, s.Admin, Provider(), RoleConstants.SystemAdministrator).OcrExtract(s.RequestId, Pdf());
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Provider_failure_returns_PORTAL_ERROR_and_leaves_no_partial_extraction_state()
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);
        var before = await SnapAsync(ctx, s);
        var result = await Controller(ctx, s.Owner, Provider(throws: new InvalidOperationException("provider down"))).OcrExtract(s.RequestId, Pdf());
        var failed = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(500, failed.StatusCode);
        var dto = Assert.IsType<OcrExtractionResultDto>(failed.Value);
        Assert.False(dto.Success);
        Assert.Equal("PORTAL_ERROR", dto.Status.Code);
        ctx.ChangeTracker.Clear();
        var after = await SnapAsync(ctx, s);
        Assert.Equal(before.OcrItems, after.OcrItems);
        Assert.Equal(before.ReconRecords, after.ReconRecords);
        Assert.Equal(before.Histories, after.Histories);
        Assert.Equal(before.Line2Lifecycle, after.Line2Lifecycle);
    }

    [Fact]
    public async Task Provider_result_without_success_is_returned_as_is_and_creates_no_suggestions()
    {
        await using var ctx = S.NewCtx();
        var s = await SeedAndRestartAsync(ctx);
        var empty = new ExtractionResultDto { Success = false, ProviderName = "TEST-PROVIDER", Items = new List<ExtractionLineItemDto>() };
        var result = await Controller(ctx, s.Owner, Provider(empty)).OcrExtract(s.RequestId, Pdf());
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<OcrExtractionResultDto>(ok.Value);
        Assert.False(dto.Success); // the wizard treats this envelope as a provider failure (no draft, retry allowed)
        Assert.Equal(0, await ctx.OcrExtractedItems.CountAsync(o => o.RequestId == s.RequestId));
    }
}
