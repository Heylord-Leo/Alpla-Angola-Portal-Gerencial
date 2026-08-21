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
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Covers GetRequests' new group-aware DisplayStatusCode/DisplayStatusName wiring (request-100
/// consistency fix, real ADVANCE_PAYMENT_COMPLETED-vs-mixed scenario) and GetRequestTimeline's
/// "Pagamento" stage now including the advance-payment codes. No controller-level HTTP/auth test
/// framework exists in this repo (see FinanceMarkAsPaidTransitionTests) — instantiates
/// RequestsController directly against an EF Core InMemory ApplicationDbContext with a fake
/// ClaimsPrincipal carrying the SystemAdministrator role (bypasses Plant/Department scope seeding).
/// </summary>
public class RequestsExplorerDisplayStateTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new RequestsController(
            ctx,
            Mock.Of<IDocumentExtractionService>(),
            new AdminLogWriter(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            Mock.Of<INotificationService>(),
            Mock.Of<IWorkflowNotificationOrchestrator>(),
            Mock.Of<IPrimaveraRequestValidationService>(),
            Mock.Of<IGroupBuilderService>(),
            new RequestStatusSyncService(ctx, NullLogger<RequestStatusSyncService>.Instance),
            Mock.Of<IApprovalRoutingService>(),
            Mock.Of<ILineItemFactory>(),
            Mock.Of<IRequestLineItemSubmissionValidator>(),
            Mock.Of<IQuotationItemEligibilityService>(),
            Mock.Of<IBatchExtraItemDecisionService>(),
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Microsoft.Extensions.Options.Options.Create(new AlplaPortal.Domain.Configuration.PostPaymentCompletionOptions()));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.SystemAdministrator)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private static (User actor, RequestType quotationType, RequestType paymentType, Dictionary<string, RequestStatus> statuses) SeedCommon(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Explorer Tester", Email = $"{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);

        // EF Core InMemory drops a row entirely from any query touching a required reference
        // navigation (Department/Company) whose FK has no matching row — Department/CompanyId=1
        // must resolve to a real seeded row, not just an int, or GetRequests silently returns zero
        // items despite a correct TotalCount.
        ctx.Departments.Add(new Department { Id = 1, Name = "ZZTEST Department" });
        ctx.Companies.Add(new Company { Id = 1, Name = "ZZTEST Company" });

        var quotationType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        var paymentType = new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.AddRange(quotationType, paymentType);

        var statusCodes = new[]
        {
            ("PO_ISSUED", "P.O Emitida", 30),
            ("PAYMENT_SCHEDULED", "Pagamento Agendado", 50),
            ("ADVANCE_PAYMENT_REQUIRED", "Adiantamento Necessário", 23),
            ("ADVANCE_PAYMENT_COMPLETED", "Adiantamento Realizado", 24),
            ("APPROVED", "Aprovado", 20),
        };
        var statuses = new Dictionary<string, RequestStatus>();
        var id = 1;
        foreach (var (code, name, order) in statusCodes)
        {
            var s = new RequestStatus { Id = id++, Code = code, Name = name, DisplayOrder = order };
            statuses[code] = s;
            ctx.RequestStatuses.Add(s);
        }

        return (actor, quotationType, paymentType, statuses);
    }

    private static Request NewQuotationRequest(User actor, RequestType type, RequestStatus status, string number) => new()
    {
        Id = Guid.NewGuid(),
        RequestNumber = number,
        Title = "ZZTEST " + number,
        RequestTypeId = type.Id,
        StatusId = status.Id,
        RequesterId = actor.Id,
        DepartmentId = 1,
        CompanyId = 1,
        CreatedAtUtc = DateTime.UtcNow,
    };

    // ── GetRequests: DisplayStatusCode/DisplayStatusName wiring ──

    [Fact]
    public async Task GetRequests_Request100Shape_MixedGroups_ReturnsPaymentsInProgressOverride()
    {
        var ctx = NewContext();
        var (actor, quotationType, _, statuses) = SeedCommon(ctx);

        var request = NewQuotationRequest(actor, quotationType, statuses["ADVANCE_PAYMENT_COMPLETED"], "ZZTEST-EXPLORER-100");
        ctx.Requests.Add(request);
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "NCR ANGOLA INFORMATICA, LDA", TotalAmount = 70341.42m, Status = "PAYMENT_SCHEDULED", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id },
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "ITEC LDA", TotalAmount = 275139.00m, Status = "ADVANCE_PAYMENT_COMPLETED", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetRequests(sortBy: "createdatutc", isDescending: false, page: 1, pageSize: 20);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RequestListResponseDto>(ok.Value);
        var item = Assert.Single(dto.PagedResult.Items);

        Assert.Equal("ADVANCE_PAYMENT_COMPLETED", item.StatusCode); // persisted aggregate, unchanged
        Assert.Equal("PAYMENTS_IN_PROGRESS", item.DisplayStatusCode);
        Assert.Equal("Pagamentos em andamento", item.DisplayStatusName);
    }

    [Fact]
    public async Task GetRequests_OrdinarySingleGroupRequest_NoOverride_DisplayStatusNameNull()
    {
        var ctx = NewContext();
        var (actor, quotationType, _, statuses) = SeedCommon(ctx);

        var request = NewQuotationRequest(actor, quotationType, statuses["PO_ISSUED"], "ZZTEST-EXPLORER-ORDINARY");
        ctx.Requests.Add(request);
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "Fornecedor Único", TotalAmount = 1000m, Status = "PO_ISSUED", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetRequests(sortBy: "createdatutc", isDescending: false, page: 1, pageSize: 20);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RequestListResponseDto>(ok.Value);
        var item = Assert.Single(dto.PagedResult.Items);

        Assert.Null(item.DisplayStatusCode);
        Assert.Null(item.DisplayStatusName);
        Assert.Equal("P.O Emitida", item.StatusName); // caller falls back to this unchanged
    }

    [Fact]
    public async Task GetRequests_PaymentTypeRequest_NoGroupQuery_NoOverride()
    {
        var ctx = NewContext();
        var (actor, _, paymentType, statuses) = SeedCommon(ctx);

        var request = NewQuotationRequest(actor, paymentType, statuses["PO_ISSUED"], "ZZTEST-EXPLORER-PAYMENT");
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetRequests(sortBy: "createdatutc", isDescending: false, page: 1, pageSize: 20);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<RequestListResponseDto>(ok.Value);
        var item = Assert.Single(dto.PagedResult.Items);

        Assert.Null(item.DisplayStatusCode);
        Assert.Null(item.DisplayStatusName);
    }

    [Fact]
    public async Task GetRequests_Pagination_DisplayStateComputedOnlyForReturnedPageRows()
    {
        var ctx = NewContext();
        var (actor, quotationType, _, statuses) = SeedCommon(ctx);

        // Two distinct multi-group mixed-bucket requests, distinguishable by creation time.
        var earlier = NewQuotationRequest(actor, quotationType, statuses["ADVANCE_PAYMENT_COMPLETED"], "ZZTEST-PAGE-1");
        earlier.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        var later = NewQuotationRequest(actor, quotationType, statuses["APPROVED"], "ZZTEST-PAGE-2");
        later.CreatedAtUtc = DateTime.UtcNow;
        ctx.Requests.AddRange(earlier, later);

        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = earlier.Id, SupplierNameSnapshot = "A", TotalAmount = 1m, Status = "PAYMENT_SCHEDULED", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id },
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = earlier.Id, SupplierNameSnapshot = "B", TotalAmount = 1m, Status = "ADVANCE_PAYMENT_COMPLETED", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id },
            // "later" has no groups at all yet (still in approval) — must not affect page 1's result
            // and, on its own page, must resolve to "no override" without erroring.
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = later.Id, SupplierNameSnapshot = "C", TotalAmount = 1m, Status = "PO_ISSUED", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);

        var page1 = await controller.GetRequests(sortBy: "createdatutc", isDescending: false, page: 1, pageSize: 1);
        var page1Ok = Assert.IsType<OkObjectResult>(page1.Result);
        var page1Dto = Assert.IsType<RequestListResponseDto>(page1Ok.Value);
        var page1Item = Assert.Single(page1Dto.PagedResult.Items);
        Assert.Equal("ZZTEST-PAGE-1", page1Item.RequestNumber);
        Assert.Equal("PAYMENTS_IN_PROGRESS", page1Item.DisplayStatusCode);
        Assert.Equal(2, page1Dto.PagedResult.TotalCount);

        var page2 = await controller.GetRequests(sortBy: "createdatutc", isDescending: false, page: 2, pageSize: 1);
        var page2Ok = Assert.IsType<OkObjectResult>(page2.Result);
        var page2Dto = Assert.IsType<RequestListResponseDto>(page2Ok.Value);
        var page2Item = Assert.Single(page2Dto.PagedResult.Items);
        Assert.Equal("ZZTEST-PAGE-2", page2Item.RequestNumber);
        // v2.230.0 historical compatibility (Decision 2): a single-unit request whose scalar
        // (APPROVED) lags the group lifecycle (PO_ISSUED) now receives the unit's truthful
        // display override — resolved independently of page 1, which is this test's point.
        Assert.Equal("PO_ISSUED", page2Item.DisplayStatusCode);
        Assert.Equal("P.O Emitida", page2Item.DisplayStatusName);
    }

    // ── GetRequestTimeline: advance-payment codes added to QUOTATION's "Pagamento" stage ──

    private static void AddHistory(ApplicationDbContext ctx, Request request, RequestStatus newStatus, DateTime at)
    {
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = request.RequesterId,
            ActionTaken = "TEST",
            PreviousStatusId = newStatus.Id,
            NewStatusId = newStatus.Id,
            CreatedAtUtc = at
        });
    }

    [Fact]
    public async Task GetRequestTimeline_Request100Shape_PagamentoStageIsCurrent_NotPending()
    {
        var ctx = NewContext();
        var (actor, quotationType, _, statuses) = SeedCommon(ctx);

        // Extra statuses needed only for this history shape.
        var waitingQuotation = new RequestStatus { Id = 10, Code = "WAITING_QUOTATION", Name = "Aguardando Cotação", DisplayOrder = 5 };
        var waitingArea = new RequestStatus { Id = 11, Code = "WAITING_AREA_APPROVAL", Name = "Aguardando Aprovação de Área", DisplayOrder = 8 };
        var waitingFinal = new RequestStatus { Id = 12, Code = "WAITING_FINAL_APPROVAL", Name = "Aguardando Aprovação Final", DisplayOrder = 9 };
        var quotationCompleted = new RequestStatus { Id = 13, Code = "QUOTATION_COMPLETED", Name = "Cotação Concluída", DisplayOrder = 12 };
        ctx.RequestStatuses.AddRange(waitingQuotation, waitingArea, waitingFinal, quotationCompleted);

        var request = NewQuotationRequest(actor, quotationType, statuses["ADVANCE_PAYMENT_COMPLETED"], "ZZTEST-TIMELINE-100");
        ctx.Requests.Add(request);

        var t0 = DateTime.UtcNow.AddDays(-3);
        AddHistory(ctx, request, waitingQuotation, t0);
        AddHistory(ctx, request, waitingArea, t0.AddHours(1));
        AddHistory(ctx, request, waitingFinal, t0.AddHours(2));
        AddHistory(ctx, request, quotationCompleted, t0.AddHours(3));
        AddHistory(ctx, request, statuses["PO_ISSUED"], t0.AddHours(4));
        AddHistory(ctx, request, statuses["ADVANCE_PAYMENT_REQUIRED"], t0.AddHours(5));
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetRequestTimeline(request.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var timeline = Assert.IsType<RequestTimelineDto>(ok.Value);

        string StateOf(string label) => timeline.Steps.First(s => s.Label == label).State;

        // v2.229.9: the duplicate "Agendamento" stage is gone; the tail is
        // Recebimento / Execução → Documentação Fiscal → Concluído.
        Assert.Equal("completed", StateOf("P.O. / Contratação"));
        Assert.Equal("current", StateOf("Pagamento"));
        Assert.Equal("pending", StateOf("Recebimento / Execução"));
        Assert.Equal("pending", StateOf("Documentação Fiscal"));
        Assert.Equal("pending", StateOf("Concluído"));
        Assert.DoesNotContain(timeline.Steps, s => s.Label == "Agendamento");
    }

    [Fact]
    public async Task GetRequestTimeline_NormalPaymentScheduledQuotation_PagamentoStageStillCurrent()
    {
        var ctx = NewContext();
        var (actor, quotationType, _, statuses) = SeedCommon(ctx);

        var request = NewQuotationRequest(actor, quotationType, statuses["PAYMENT_SCHEDULED"], "ZZTEST-TIMELINE-NORMAL");
        ctx.Requests.Add(request);
        AddHistory(ctx, request, statuses["PO_ISSUED"], DateTime.UtcNow.AddHours(-2));
        AddHistory(ctx, request, statuses["PAYMENT_SCHEDULED"], DateTime.UtcNow.AddHours(-1));
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetRequestTimeline(request.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var timeline = Assert.IsType<RequestTimelineDto>(ok.Value);
        Assert.Equal("current", timeline.Steps.First(s => s.Label == "Pagamento").State);
    }

    [Fact]
    public async Task GetRequestTimeline_PaymentTypeRequest_UsesUnmodifiedPaymentStages()
    {
        var ctx = NewContext();
        var (actor, _, paymentType, statuses) = SeedCommon(ctx);

        var request = NewQuotationRequest(actor, paymentType, statuses["PO_ISSUED"], "ZZTEST-TIMELINE-PAYMENT-TYPE");
        ctx.Requests.Add(request);
        AddHistory(ctx, request, statuses["APPROVED"], DateTime.UtcNow.AddHours(-2));
        AddHistory(ctx, request, statuses["PO_ISSUED"], DateTime.UtcNow.AddHours(-1));
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetRequestTimeline(request.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var timeline = Assert.IsType<RequestTimelineDto>(ok.Value);
        // v2.229.9: the PAYMENT "Agendamento" stage became "P.O. / Contratação"
        // ({ APPROVED, PO_PARTIALLY_UPLOADED, PO_ISSUED, WAITING_PO_CORRECTION }).
        Assert.Equal("current", timeline.Steps.First(s => s.Label == "P.O. / Contratação").State);
        Assert.DoesNotContain(timeline.Steps, s => s.Label == "Agendamento");
    }

    // ── v2.229.9: the 8-stage Release 4 timeline (Recebimento / Execução × Documentação Fiscal) ──

    private static readonly string[] QuotationStageLabels =
    {
        "Rascunho", "Cotação", "Aprovações", "P.O. / Contratação",
        "Pagamento", "Recebimento / Execução", "Documentação Fiscal", "Concluído"
    };

    private static readonly string[] PaymentStageLabels =
    {
        "Rascunho", "Aprovação Área", "Aprovação Final", "P.O. / Contratação",
        "Pagamento", "Recebimento / Execução", "Documentação Fiscal", "Concluído"
    };

    private static RequestStatus AddStatus(ApplicationDbContext ctx, int id, string code)
    {
        var s = new RequestStatus { Id = id, Code = code, Name = "ZZTEST " + code, DisplayOrder = id };
        ctx.RequestStatuses.Add(s);
        return s;
    }

    private static async Task<RequestTimelineDto> TimelineOf(
        ApplicationDbContext ctx, User actor, Request request)
    {
        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetRequestTimeline(request.Id);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<RequestTimelineDto>(ok.Value);
    }

    [Fact]
    public async Task Timeline_A_quotation_has_the_eight_stage_order_with_no_duplicates()
    {
        var ctx = NewContext();
        var (actor, quotationType, _, statuses) = SeedCommon(ctx);
        var request = NewQuotationRequest(actor, quotationType, statuses["APPROVED"], "ZZTEST-TL-ORDER");
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();

        var timeline = await TimelineOf(ctx, actor, request);

        Assert.Equal(QuotationStageLabels, timeline.Steps.Select(s => s.Label).ToArray());
        // B: PO_ISSUED belongs to exactly ONE stage (the old duplicate "Agendamento" is gone).
        Assert.Single(timeline.Steps, s => s.Label == "P.O. / Contratação");
    }

    [Fact]
    public async Task Timeline_payment_flow_has_the_standardized_tail()
    {
        var ctx = NewContext();
        var (actor, _, paymentType, statuses) = SeedCommon(ctx);
        var request = NewQuotationRequest(actor, paymentType, statuses["APPROVED"], "ZZTEST-TL-PAY-TAIL");
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();

        var timeline = await TimelineOf(ctx, actor, request);

        Assert.Equal(PaymentStageLabels, timeline.Steps.Select(s => s.Label).ToArray());
    }

    [Theory]
    // C: operational receiving/execution states.
    [InlineData("WAITING_SUPPLIER_DELIVERY", "Recebimento / Execução")]
    [InlineData("IN_FOLLOWUP", "Recebimento / Execução")]
    // D: fiscal-document states.
    [InlineData("WAITING_RECEIPT", "Documentação Fiscal")]
    [InlineData("WAITING_FISCAL_RECEIPT", "Documentação Fiscal")]
    // E: reconciliation is a payment-family state.
    [InlineData("WAITING_RECONCILIATION", "Pagamento")]
    // F: PO-family states, including the previously unmapped ones.
    [InlineData("WAITING_PO_CORRECTION", "P.O. / Contratação")]
    [InlineData("PO_PARTIALLY_UPLOADED", "P.O. / Contratação")]
    // G: terminal completion.
    [InlineData("COMPLETED", "Concluído")]
    public async Task Timeline_CtoH_release4_statuses_map_to_their_stage_and_are_never_unmapped(
        string statusCode, string expectedStageLabel)
    {
        var ctx = NewContext();
        var (actor, quotationType, _, _) = SeedCommon(ctx);
        var status = AddStatus(ctx, 900, statusCode);
        var request = NewQuotationRequest(actor, quotationType, status, "ZZTEST-TL-" + statusCode);
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();

        var timeline = await TimelineOf(ctx, actor, request);

        // H: exactly one current stage — currentStageIndex is never -1 for these statuses.
        var current = Assert.Single(timeline.Steps, s => s.State == "current");
        Assert.Equal(expectedStageLabel, current.Label);
    }

    [Fact]
    public async Task Timeline_I_operational_receipt_stamp_is_preferred_over_the_history_date()
    {
        var ctx = NewContext();
        var (actor, quotationType, _, _) = SeedCommon(ctx);
        var wsd = AddStatus(ctx, 901, "WAITING_SUPPLIER_DELIVERY");
        var waitingReceipt = AddStatus(ctx, 902, "WAITING_RECEIPT");

        var request = NewQuotationRequest(actor, quotationType, waitingReceipt, "ZZTEST-TL-STAMP");
        ctx.Requests.Add(request);

        var historyInstant = DateTime.UtcNow.AddDays(-2);
        var stampInstant = DateTime.UtcNow.AddDays(-1);
        AddHistory(ctx, request, wsd, historyInstant);
        AddHistory(ctx, request, waitingReceipt, stampInstant.AddHours(1));
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST Stamp Supplier",
            Status = "WAITING_RECEIPT",
            OperationalReceiptCompletedAtUtc = stampInstant,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedByUserId = actor.Id
        });
        await ctx.SaveChangesAsync();

        var timeline = await TimelineOf(ctx, actor, request);

        var receiving = timeline.Steps.First(s => s.Label == "Recebimento / Execução");
        Assert.Equal("completed", receiving.State);
        // The persisted operational receipt (not the WSD history entry) is the stage instant.
        Assert.Equal(stampInstant, receiving.CompletedAt!.Value.UtcDateTime, TimeSpan.FromSeconds(1));

        var fiscal = timeline.Steps.First(s => s.Label == "Documentação Fiscal");
        Assert.Equal("current", fiscal.State);
    }

    [Fact]
    public async Task Timeline_I2_without_a_full_stamp_the_history_date_stands_and_nothing_is_fabricated()
    {
        var ctx = NewContext();
        var (actor, quotationType, _, _) = SeedCommon(ctx);
        var wsd = AddStatus(ctx, 903, "WAITING_SUPPLIER_DELIVERY");
        var waitingReceipt = AddStatus(ctx, 904, "WAITING_RECEIPT");

        var request = NewQuotationRequest(actor, quotationType, waitingReceipt, "ZZTEST-TL-NOSTAMP");
        ctx.Requests.Add(request);
        var historyInstant = DateTime.UtcNow.AddDays(-2);
        AddHistory(ctx, request, wsd, historyInstant);
        AddHistory(ctx, request, waitingReceipt, DateTime.UtcNow.AddDays(-1));
        // Two groups, only ONE stamped: the furthest-behind rule refuses the preferred instant.
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "ZZTEST A", Status = "WAITING_RECEIPT", OperationalReceiptCompletedAtUtc = DateTime.UtcNow.AddDays(-1), CreatedAtUtc = DateTime.UtcNow.AddDays(-3), CreatedByUserId = actor.Id },
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "ZZTEST B", Status = "WAITING_SUPPLIER_DELIVERY", OperationalReceiptCompletedAtUtc = null, CreatedAtUtc = DateTime.UtcNow.AddDays(-3), CreatedByUserId = actor.Id });
        await ctx.SaveChangesAsync();

        var timeline = await TimelineOf(ctx, actor, request);

        var receiving = timeline.Steps.First(s => s.Label == "Recebimento / Execução");
        Assert.Equal("completed", receiving.State); // WSD history exists
        Assert.Equal(historyInstant, receiving.CompletedAt!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Timeline_J_endpoint_is_read_only()
    {
        var ctx = NewContext();
        var (actor, quotationType, _, _) = SeedCommon(ctx);
        var wsd = AddStatus(ctx, 905, "WAITING_SUPPLIER_DELIVERY");
        var request = NewQuotationRequest(actor, quotationType, wsd, "ZZTEST-TL-READONLY");
        ctx.Requests.Add(request);
        AddHistory(ctx, request, wsd, DateTime.UtcNow.AddHours(-1));
        await ctx.SaveChangesAsync();
        var historyCountBefore = await ctx.RequestStatusHistories.CountAsync();
        var statusBefore = request.StatusId;

        await TimelineOf(ctx, actor, request);

        Assert.Equal(historyCountBefore, await ctx.RequestStatusHistories.CountAsync());
        var reloaded = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        Assert.Equal(statusBefore, reloaded.StatusId);
    }
}
