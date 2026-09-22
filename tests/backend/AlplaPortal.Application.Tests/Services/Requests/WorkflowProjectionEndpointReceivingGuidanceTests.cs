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
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.245.6 — the REAL <c>GET {id}/workflow-projection</c> endpoint must feed the v2.245.3 receiving-guidance
/// rule with the receipt FACTS it consumes (item LineItemStatus, winning quotation items). Before this
/// release the endpoint loaded neither, so Request Details always showed "Resolver itens pendentes…" for a
/// fully received, unconfirmed group (observed in TEST after a reopen at 2/2). Also pins the audit status of
/// RECEIVING_REOPENED against the real aggregator's STATUS_SYNC.
/// </summary>
public class WorkflowProjectionEndpointReceivingGuidanceTests
{
    private const int STATUS_PAYMENT_COMPLETED_ID = 14;
    private const int STATUS_WAITING_RECEIPT_ID = 16;
    private const int STATUS_IN_FOLLOWUP_ID = 18;

    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ApplicationDbContext NewContext(DbContextOptions<ApplicationDbContext> options) => new(options);

    private static PostPaymentCompletionOptions Flags() => new()
    {
        Enabled = false, CompletionEnabled = false,
        EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
    };

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId, bool realAggregator = false)
    {
        var options = Flags();
        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(
                new Mock<IServiceScopeFactory>().Object,
                new Mock<IHttpContextAccessor>().Object,
                NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object,
            new Mock<IApprovalRoutingService>().Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object,
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(options));

        var services = new ServiceCollection();
        if (realAggregator)
            services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance, Options.Create(options)));
        else
            services.AddSingleton(new Mock<IStatusAggregationService>().Object);
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(
            ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, RoleConstants.Receiving)
                }, "Test")),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId);

    private static void SeedLookups(ApplicationDbContext ctx)
    {
        ctx.RequestTypes.AddRange(
            new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" },
            new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = STATUS_PAYMENT_COMPLETED_ID, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Concluído", DisplayOrder = 14 },
            new RequestStatus { Id = STATUS_WAITING_RECEIPT_ID, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 },
            new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 },
            new RequestStatus { Id = STATUS_IN_FOLLOWUP_ID, Code = RequestConstants.Statuses.InFollowup, Name = "Em Acompanhamento", DisplayOrder = 18 });
        ctx.LineItemStatuses.AddRange(
            new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" },
            new LineItemStatus { Id = 92, Code = "PARTIALLY_RECEIVED", Name = "Parcial" },
            new LineItemStatus { Id = 93, Code = "PENDING", Name = "Pendente" });
    }

    /// <summary>A PAYMENT request, one group in <paramref name="groupStatus"/>, items with the given received quantities (authorized 2).</summary>
    private static async Task<Seed> SeedPaymentAsync(ApplicationDbContext ctx, string groupStatus, params decimal[] receivedQuantities)
    {
        SeedLookups(ctx);
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Proj", Email = $"proj-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(actor);

        var scalar = groupStatus switch
        {
            RequestConstants.PoGroupStatuses.WaitingReceipt => STATUS_WAITING_RECEIPT_ID,
            RequestConstants.PoGroupStatuses.InFollowup => STATUS_IN_FOLLOWUP_ID,
            _ => STATUS_PAYMENT_COMPLETED_ID
        };
        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "ZZTEST-PROJ-" + Guid.NewGuid().ToString("N")[..8], Title = "ZZTEST projection",
            RequestTypeId = 2, StatusId = scalar, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        ctx.Requests.Add(request);
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "ZZTEST Proj Supplier", CurrencyCode = "AOA", TotalAmount = 10_000m,
            Status = groupStatus, SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied, RequiresOperationInvoice = true, RequiresSeparateFiscalReceipt = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5), CreatedByUserId = actor.Id,
            OperationalReceiptCompletedAtUtc = groupStatus == RequestConstants.PoGroupStatuses.WaitingReceipt ? DateTime.UtcNow.AddDays(-1) : null
        };
        ctx.RequestPoGroups.Add(group);
        var line = 1;
        foreach (var qty in receivedQuantities)
        {
            ctx.RequestLineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(), RequestId = request.Id, RequestPoGroupId = group.Id, LineNumber = line++, Description = "ZZTEST item",
                Quantity = 2m, ReceivedQuantity = qty, LineItemStatusId = qty >= 2m ? 91 : qty > 0 ? 92 : 93
            });
        }
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, actor.Id);
    }

    private static async Task<WorkflowUnit> ProjectSingleUnitAsync(DbContextOptions<ApplicationDbContext> options, Seed seed)
    {
        using var ctx = NewContext(options);
        var result = await BuildController(ctx, seed.ActorId).GetWorkflowProjection(seed.RequestId);
        var ok = Assert.IsType<OkObjectResult>(result);
        var projection = Assert.IsType<RequestWorkflowProjection>(ok.Value);
        return Assert.Single(projection.Units);
    }

    // ── §1 guidance through the REAL endpoint ──

    [Fact]
    public async Task Endpoint_InFollowup_AllReceived_GuidesToConfirmReceiving()
    {
        var options = NewOptions();
        var seed = await SeedPaymentAsync(NewContext(options), RequestConstants.PoGroupStatuses.InFollowup, 2m, 2m);

        var unit = await ProjectSingleUnitAsync(options, seed);

        Assert.Equal("CONFIRM_RECEIVING", unit.NextAction!.ActionType);
        Assert.Equal("Recebimento completo — confirmar recebimento", unit.NextAction.Label);
        Assert.Equal("Recebimento", unit.ResponsibleRole);
    }

    [Fact]
    public async Task Endpoint_InFollowup_OneIncomplete_KeepsPendingGuidance()
    {
        var options = NewOptions();
        var seed = await SeedPaymentAsync(NewContext(options), RequestConstants.PoGroupStatuses.InFollowup, 2m, 1m);

        var unit = await ProjectSingleUnitAsync(options, seed);

        Assert.Equal("RESOLVE_FOLLOWUP", unit.NextAction!.ActionType);
        Assert.Equal("Resolver itens pendentes e confirmar recebimento", unit.NextAction.Label);
    }

    [Fact]
    public async Task Endpoint_InFollowup_ResetToZero_KeepsPendingGuidance()
    {
        var options = NewOptions();
        var seed = await SeedPaymentAsync(NewContext(options), RequestConstants.PoGroupStatuses.InFollowup, 2m, 0m);

        var unit = await ProjectSingleUnitAsync(options, seed);

        Assert.Equal("RESOLVE_FOLLOWUP", unit.NextAction!.ActionType);
    }

    [Fact]
    public async Task Endpoint_PaymentCompleted_AllReceived_GuidesToConfirmReceiving()
    {
        var options = NewOptions();
        var seed = await SeedPaymentAsync(NewContext(options), RequestConstants.PoGroupStatuses.PaymentCompleted, 2m, 2m);

        var unit = await ProjectSingleUnitAsync(options, seed);

        Assert.Equal("CONFIRM_RECEIVING", unit.NextAction!.ActionType);
        Assert.Equal("Recebimento completo — confirmar recebimento", unit.NextAction.Label);
    }

    [Fact]
    public async Task Endpoint_PaymentCompleted_Partial_GuidesToReceive()
    {
        var options = NewOptions();
        var seed = await SeedPaymentAsync(NewContext(options), RequestConstants.PoGroupStatuses.PaymentCompleted, 0m, 0m);

        var unit = await ProjectSingleUnitAsync(options, seed);

        Assert.Equal("RECEIVE", unit.NextAction!.ActionType);
        Assert.Contains("conferir itens", unit.NextAction.Label);
    }

    [Fact]
    public async Task Endpoint_WaitingReceipt_GuidesToAttachSupplierReceipt()
    {
        var options = NewOptions();
        var seed = await SeedPaymentAsync(NewContext(options), RequestConstants.PoGroupStatuses.WaitingReceipt, 2m, 2m);

        var unit = await ProjectSingleUnitAsync(options, seed);

        Assert.Equal("ATTACH_RECEIPT", unit.NextAction!.ActionType);
    }

    /// <summary>
    /// QUOTATION request whose receipt lives on the WINNING QuotationItem (line-number fallback, v2.245.0):
    /// the endpoint must load the winning quotation items for the guidance to see it.
    /// </summary>
    [Fact]
    public async Task Endpoint_Quotation_ReceiptOnWinningQuotationItem_GuidesToConfirmReceiving()
    {
        var options = NewOptions();
        Seed seed;
        using (var ctx = NewContext(options))
        {
            SeedLookups(ctx);
            var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Q", Email = $"q-{Guid.NewGuid():N}@test.local" };
            ctx.Users.Add(actor);
            var quotationId = Guid.NewGuid();
            var request = new Request
            {
                Id = Guid.NewGuid(), RequestNumber = "ZZTEST-PROJQ", Title = "ZZTEST q", RequestTypeId = 1, StatusId = STATUS_IN_FOLLOWUP_ID,
                SelectedQuotationId = quotationId, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
            };
            ctx.Requests.Add(request);
            ctx.Quotations.Add(new Quotation { Id = quotationId, RequestId = request.Id, SupplierNameSnapshot = "ZZTEST QS", Currency = "AOA", DocumentType = RequestConstants.SourceDocumentTypes.Proforma });
            ctx.Set<QuotationItem>().Add(new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotationId, LineNumber = 1, Description = "win", Quantity = 1m, ReceivedQuantity = 1m, LineItemStatusId = 91 });
            var group = new RequestPoGroup
            {
                Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "ZZTEST QS", CurrencyCode = "AOA", TotalAmount = 1m,
                Status = RequestConstants.PoGroupStatuses.InFollowup, SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
                OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id
            };
            ctx.RequestPoGroups.Add(group);
            // own record still PENDING; the receipt is on the winning quotation item at the same line number
            ctx.RequestLineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, RequestPoGroupId = group.Id, LineNumber = 1, Description = "li", Quantity = 1m, ReceivedQuantity = 0m, LineItemStatusId = 93 });
            await ctx.SaveChangesAsync();
            seed = new Seed(request.Id, group.Id, actor.Id);
        }

        var unit = await ProjectSingleUnitAsync(options, seed);

        Assert.Equal("CONFIRM_RECEIVING", unit.NextAction!.ActionType);
    }

    // ── the observed TEST scenario: reopen at 2/2, then Request Details ──

    [Fact]
    public async Task Reopen_AllReceived_ThenProjection_GuidesToConfirmReceiving_QuantitiesPreserved()
    {
        var options = NewOptions();
        var seed = await SeedPaymentAsync(NewContext(options), RequestConstants.PoGroupStatuses.WaitingReceipt, 2m, 2m);

        using (var ctx = NewContext(options))
        {
            var result = await BuildController(ctx, seed.ActorId).ReopenReceiving(seed.RequestId, seed.GroupId, new ReopenReceivingDto { Reason = "ZZTEST confirmado cedo" });
            Assert.IsType<OkObjectResult>(result);
        }

        var unit = await ProjectSingleUnitAsync(options, seed);
        Assert.Equal(RequestConstants.PoGroupStatuses.InFollowup, unit.StatusCode);
        Assert.Equal("CONFIRM_RECEIVING", unit.NextAction!.ActionType);
        Assert.Equal("Recebimento completo — confirmar recebimento", unit.NextAction.Label);

        using (var ctx = NewContext(options))
            Assert.All(await ctx.RequestLineItems.AsNoTracking().Where(i => i.RequestPoGroupId == seed.GroupId).ToListAsync(), i => Assert.Equal(2m, i.ReceivedQuantity));
    }

    // ── §2 RECEIVING_REOPENED resulting status + the real aggregator's STATUS_SYNC ──

    [Fact]
    public async Task Reopen_AuditRecordsInFollowup_NotWaitingReceipt_AndRealAggregatorWritesStatusSync()
    {
        var options = NewOptions();
        var seed = await SeedPaymentAsync(NewContext(options), RequestConstants.PoGroupStatuses.WaitingReceipt, 2m, 2m);

        using (var ctx = NewContext(options))
        {
            var result = await BuildController(ctx, seed.ActorId, realAggregator: true)
                .ReopenReceiving(seed.RequestId, seed.GroupId, new ReopenReceivingDto { Reason = "ZZTEST motivo" });
            Assert.IsType<OkObjectResult>(result);
        }

        using (var ctx = NewContext(options))
        {
            var reopened = await ctx.RequestStatusHistories.Include(h => h.NewStatus).Include(h => h.PreviousStatus).AsNoTracking()
                .SingleAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "RECEIVING_REOPENED");
            Assert.Equal(STATUS_IN_FOLLOWUP_ID, reopened.NewStatusId);
            Assert.Equal("Em Acompanhamento", reopened.NewStatus.Name);          // what "Ação / Novo Status" renders
            Assert.NotEqual(STATUS_WAITING_RECEIPT_ID, reopened.NewStatusId);
            Assert.Equal(STATUS_WAITING_RECEIPT_ID, reopened.PreviousStatusId);  // where it came from
            Assert.Contains("WAITING_RECEIPT → IN_FOLLOWUP", reopened.Comment);

            // the aggregator remains the ONLY writer of the scalar and its STATUS_SYNC event is preserved
            var request = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
            Assert.Equal(RequestConstants.Statuses.InFollowup, request.Status!.Code);
            var sync = await ctx.RequestStatusHistories.AsNoTracking()
                .Where(h => h.RequestId == seed.RequestId && h.ActionTaken == "STATUS_SYNC").ToListAsync();
            var last = Assert.Single(sync);
            Assert.Equal(STATUS_WAITING_RECEIPT_ID, last.PreviousStatusId);
            Assert.Equal(STATUS_IN_FOLLOWUP_ID, last.NewStatusId);
            Assert.True(last.CreatedAtUtc >= reopened.CreatedAtUtc);
        }
    }
}
