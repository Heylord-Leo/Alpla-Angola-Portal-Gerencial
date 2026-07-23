using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Covers the request-100 "0,00 Kz" fix end-to-end through the real controller: GetPayments'
/// parent Amount fallback (ApprovedTotalAmount -> SelectedQuotation -> EstimatedTotalAmount) and
/// GetHistory/ExportHistory's event-aware amount resolution (FinanceHistoryAmountResolver, unit
/// tested in isolation by FinanceHistoryAmountResolverTests — these tests instead prove the
/// controller actually wires the request's real fallback ingredients into the resolver and that
/// ExportHistory's CSV renders a null Amount as an empty field, not "0.00").
/// </summary>
public class FinanceHistoryAndPaymentsProjectionTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FinanceController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new FinanceController(
            ctx,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance),
            new FinancePaymentEligibilityService());

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

    private static (User actor, RequestType quotationType, RequestStatus poIssued) SeedCommon(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);

        var quotationType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        ctx.RequestTypes.Add(quotationType);

        var poIssued = new RequestStatus { Id = 1, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 30 };
        ctx.RequestStatuses.Add(poIssued);

        return (actor, quotationType, poIssued);
    }

    // ── GetPayments Amount fallback (spec tests 1-3) ──

    [Fact]
    public async Task GetPayments_Request100Shape_ApprovedTotalAmountPositive_SelectedQuotationNull_UsesApprovedTotalAmount()
    {
        var ctx = NewContext();
        var (actor, quotationType, poIssued) = SeedCommon(ctx);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ-100-AMOUNT",
            Title = "ZZTEST multi-group amount",
            RequestTypeId = quotationType.Id,
            StatusId = poIssued.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            SelectedQuotationId = null,
            ApprovedTotalAmount = 345480.42m,
            EstimatedTotalAmount = 0m,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            TotalAmount = 345480.42m,
            Status = RequestConstants.PoGroupStatuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor.Id
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetPayments();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<AlplaPortal.Application.DTOs.Finance.FinanceListResponseDto>(ok.Value);
        var item = Assert.Single(dto.PagedResult.Items);
        Assert.Equal(345480.42m, item.Amount);
    }

    [Fact]
    public async Task GetPayments_ApprovedTotalAmountAbsent_SelectedQuotationPresent_UsesSelectedQuotationTotal()
    {
        var ctx = NewContext();
        var (actor, quotationType, poIssued) = SeedCommon(ctx);

        var quotationId = Guid.NewGuid();
        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ-100-AMOUNT-2",
            Title = "ZZTEST selected quotation fallback",
            RequestTypeId = quotationType.Id,
            StatusId = poIssued.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            SelectedQuotationId = quotationId,
            ApprovedTotalAmount = null,
            EstimatedTotalAmount = 111m,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);
        ctx.Quotations.Add(new Quotation
        {
            Id = quotationId,
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST Supplier",
            TotalAmount = 22222.22m,
            Currency = "AOA",
            CreatedAtUtc = DateTime.UtcNow
        });
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            TotalAmount = 22222.22m,
            Status = RequestConstants.PoGroupStatuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor.Id
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetPayments();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<AlplaPortal.Application.DTOs.Finance.FinanceListResponseDto>(ok.Value);
        var item = Assert.Single(dto.PagedResult.Items);
        Assert.Equal(22222.22m, item.Amount);
    }

    [Fact]
    public async Task GetPayments_ApprovedTotalAmountAndSelectedQuotationAbsent_UsesEstimatedTotalAmount()
    {
        var ctx = NewContext();
        var (actor, quotationType, poIssued) = SeedCommon(ctx);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ-100-AMOUNT-3",
            Title = "ZZTEST estimated fallback",
            RequestTypeId = quotationType.Id,
            StatusId = poIssued.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            SelectedQuotationId = null,
            ApprovedTotalAmount = null,
            EstimatedTotalAmount = 5555.55m,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            TotalAmount = 5555.55m,
            Status = RequestConstants.PoGroupStatuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor.Id
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetPayments();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<AlplaPortal.Application.DTOs.Finance.FinanceListResponseDto>(ok.Value);
        var item = Assert.Single(dto.PagedResult.Items);
        Assert.Equal(5555.55m, item.Amount);
    }

    // ── GetHistory / ExportHistory event-aware amount wiring (spec tests 9-10) ──

    private static Request SeedHistoryRequest(ApplicationDbContext ctx, User actor, int statusId, decimal? approvedTotalAmount)
    {
        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ-100-HISTORY",
            Title = "ZZTEST history amount wiring",
            RequestTypeId = 1,
            StatusId = statusId,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            SelectedQuotationId = null,
            ApprovedTotalAmount = approvedTotalAmount,
            EstimatedTotalAmount = 0m,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);
        return request;
    }

    [Fact]
    public async Task GetHistory_GenuineTransition_NoParseableComment_FallsBackToApprovedTotalAmount()
    {
        var ctx = NewContext();
        var (actor, _, poIssued) = SeedCommon(ctx);
        var request = SeedHistoryRequest(ctx, actor, poIssued.Id, approvedTotalAmount: 345480.42m);
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actor.Id,
            ActionTaken = "PAYMENT_COMPLETED",
            PreviousStatusId = poIssued.Id,
            NewStatusId = poIssued.Id,
            Comment = "Pagamento realizado na totalidade.", // no "Montante:" pattern — legacy-shaped comment
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetHistory();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<AlplaPortal.Application.DTOs.Common.PagedResult<AlplaPortal.Application.DTOs.Finance.FinanceHistoryItemDto>>(ok.Value);
        var item = Assert.Single(page.Items);
        Assert.Equal(345480.42m, item.Amount);
    }

    [Fact]
    public async Task GetHistory_DocumentoAdicionado_AmountIsNull_NotZero()
    {
        var ctx = NewContext();
        var (actor, _, poIssued) = SeedCommon(ctx);
        var request = SeedHistoryRequest(ctx, actor, poIssued.Id, approvedTotalAmount: 345480.42m);
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actor.Id,
            ActionTaken = "DOCUMENTO ADICIONADO",
            PreviousStatusId = poIssued.Id,
            NewStatusId = poIssued.Id, // echoes current status — must not be mistaken for a transition
            Comment = "Documento \"po_ncr.pdf\" (P.O) adicionado ao pedido por Finance Tester.",
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetHistory();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<AlplaPortal.Application.DTOs.Common.PagedResult<AlplaPortal.Application.DTOs.Finance.FinanceHistoryItemDto>>(ok.Value);
        var item = Assert.Single(page.Items);
        Assert.Null(item.Amount);
    }

    [Fact]
    public async Task GetHistory_StructuredScheduleComment_ResolvesGroupAmount_NotRequestAmount()
    {
        var ctx = NewContext();
        var (actor, _, poIssued) = SeedCommon(ctx);
        // Request-level amount deliberately different from the group amount embedded in the
        // comment, so a passing test proves the structured-comment amount wins, not the fallback.
        var request = SeedHistoryRequest(ctx, actor, poIssued.Id, approvedTotalAmount: 999999.99m);
        var groupId = Guid.NewGuid();
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actor.Id,
            ActionTaken = "PAYMENT_SCHEDULED",
            PreviousStatusId = poIssued.Id,
            NewStatusId = poIssued.Id,
            Comment = $"[Grupo P.O.: NCR ANGOLA INFORMATICA, LDA | Moeda: AOA | Total: 70,341.42 | GroupId: {groupId}] Pagamento agendado. ",
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetHistory();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<AlplaPortal.Application.DTOs.Common.PagedResult<AlplaPortal.Application.DTOs.Finance.FinanceHistoryItemDto>>(ok.Value);
        var item = Assert.Single(page.Items);
        Assert.Equal(70341.42m, item.Amount);
    }

    [Fact]
    public async Task ExportHistory_NullAmountEvent_RendersEmptyCsvField_NotZero()
    {
        var ctx = NewContext();
        var (actor, _, poIssued) = SeedCommon(ctx);
        var request = SeedHistoryRequest(ctx, actor, poIssued.Id, approvedTotalAmount: 345480.42m);
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actor.Id,
            ActionTaken = "DOCUMENTO ADICIONADO",
            PreviousStatusId = poIssued.Id,
            NewStatusId = poIssued.Id,
            Comment = "Documento \"po_ncr.pdf\" (P.O) adicionado ao pedido por Finance Tester.",
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.ExportHistory();

        var fileResult = Assert.IsType<FileContentResult>(result);
        var csv = Encoding.UTF8.GetString(fileResult.FileContents);
        var dataLine = csv.Split('\n').Skip(1).First(l => l.Contains("ZZTEST-REQ-100-HISTORY"));
        var fields = dataLine.Split(';');
        // Header: Data/Hora;Acao;Responsavel;Ref. Pedido;Titulo;Moeda;Montante;Cond. Pagamento;% Adiant.;Detalhes
        var montanteField = fields[6];
        Assert.Equal(string.Empty, montanteField);
        Assert.DoesNotContain("0,00", montanteField);
        Assert.DoesNotContain("0.00", montanteField);
    }

    // ── ADVANCE_PAYMENT_COMPLETED inclusion, categorization, and CSV (request-100 missing event) ──

    [Fact]
    public async Task GetHistory_IncludesAdvancePaymentCompletedEvent_WithItecAmount()
    {
        var ctx = NewContext();
        var (actor, _, poIssued) = SeedCommon(ctx);
        var request = SeedHistoryRequest(ctx, actor, poIssued.Id, approvedTotalAmount: null);
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actor.Id,
            ActionTaken = "ADVANCE_PAYMENT_COMPLETED",
            PreviousStatusId = poIssued.Id,
            NewStatusId = poIssued.Id,
            Comment = "[Lote #1 | ITEC LDA | Moeda: AOA | Montante: 275,139.00] Adiantamento de 100% realizado. Pago em 24/07/2026. ",
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetHistory();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<AlplaPortal.Application.DTOs.Common.PagedResult<AlplaPortal.Application.DTOs.Finance.FinanceHistoryItemDto>>(ok.Value);
        var item = Assert.Single(page.Items);

        Assert.Equal("ADVANCE_PAYMENT_COMPLETED", item.ActionTaken);
        Assert.Equal(275139.00m, item.Amount);
        Assert.Contains("Lote #1", item.Comment);
        Assert.Contains("ITEC LDA", item.Comment);
    }

    [Fact]
    public async Task GetHistory_PagosFilter_IncludesBothPaymentCompletedAndAdvancePaymentCompleted()
    {
        var ctx = NewContext();
        var (actor, _, poIssued) = SeedCommon(ctx);
        var request = SeedHistoryRequest(ctx, actor, poIssued.Id, approvedTotalAmount: null);
        ctx.RequestStatusHistories.AddRange(
            new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actor.Id,
                ActionTaken = "PAYMENT_COMPLETED",
                PreviousStatusId = poIssued.Id,
                NewStatusId = poIssued.Id,
                Comment = "[Lote #2 | NCR ANGOLA INFORMATICA, LDA | Moeda: AOA | Montante: 70,341.42] Pagamento realizado. Pago em 24/07/2026. ",
                CreatedAtUtc = DateTime.UtcNow
            },
            new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actor.Id,
                ActionTaken = "ADVANCE_PAYMENT_COMPLETED",
                PreviousStatusId = poIssued.Id,
                NewStatusId = poIssued.Id,
                Comment = "[Lote #1 | ITEC LDA | Moeda: AOA | Montante: 275,139.00] Adiantamento de 100% realizado. Pago em 24/07/2026. ",
                CreatedAtUtc = DateTime.UtcNow
            },
            new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actor.Id,
                ActionTaken = "PAYMENT_SCHEDULED",
                PreviousStatusId = poIssued.Id,
                NewStatusId = poIssued.Id,
                Comment = "[Lote #3 | Other Supplier | Moeda: AOA | Total: 1,000.00] Pagamento agendado. ",
                CreatedAtUtc = DateTime.UtcNow
            });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetHistory(actionType: "PAYMENT_COMPLETED");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<AlplaPortal.Application.DTOs.Common.PagedResult<AlplaPortal.Application.DTOs.Finance.FinanceHistoryItemDto>>(ok.Value);

        Assert.Equal(2, page.Items.Count());
        Assert.Contains(page.Items, i => i.ActionTaken == "PAYMENT_COMPLETED");
        Assert.Contains(page.Items, i => i.ActionTaken == "ADVANCE_PAYMENT_COMPLETED");
        Assert.DoesNotContain(page.Items, i => i.ActionTaken == "PAYMENT_SCHEDULED");
    }

    [Fact]
    public async Task GetHistory_TodosFilter_AutomaticallyIncludesAdvancePaymentCompleted()
    {
        var ctx = NewContext();
        var (actor, _, poIssued) = SeedCommon(ctx);
        var request = SeedHistoryRequest(ctx, actor, poIssued.Id, approvedTotalAmount: null);
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actor.Id,
            ActionTaken = "ADVANCE_PAYMENT_COMPLETED",
            PreviousStatusId = poIssued.Id,
            NewStatusId = poIssued.Id,
            Comment = "[Lote #1 | ITEC LDA | Moeda: AOA | Montante: 275,139.00] Adiantamento de 100% realizado. Pago em 24/07/2026. ",
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.GetHistory(); // no actionType -> "Todos"

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<AlplaPortal.Application.DTOs.Common.PagedResult<AlplaPortal.Application.DTOs.Finance.FinanceHistoryItemDto>>(ok.Value);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task ExportHistory_IncludesAdvancePaymentCompleted_WithFriendlyActionLabel()
    {
        var ctx = NewContext();
        var (actor, _, poIssued) = SeedCommon(ctx);
        var request = SeedHistoryRequest(ctx, actor, poIssued.Id, approvedTotalAmount: null);
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actor.Id,
            ActionTaken = "ADVANCE_PAYMENT_COMPLETED",
            PreviousStatusId = poIssued.Id,
            NewStatusId = poIssued.Id,
            Comment = "[Lote #1 | ITEC LDA | Moeda: AOA | Montante: 275,139.00] Adiantamento de 100% realizado. Pago em 24/07/2026. ",
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.ExportHistory();

        var fileResult = Assert.IsType<FileContentResult>(result);
        var csv = Encoding.UTF8.GetString(fileResult.FileContents);
        var dataLine = csv.Split('\n').Skip(1).First(l => l.Contains("ZZTEST-REQ-100-HISTORY"));

        Assert.Contains("Adiantamento Realizado", dataLine);
        Assert.Contains("275139", dataLine.Replace(",", "").Replace(".", ""));
    }

    [Fact]
    public async Task GetHistory_DoesNotMutateStoredComment_LegacyRowUnchangedAfterRead()
    {
        var ctx = NewContext();
        var (actor, _, poIssued) = SeedCommon(ctx);
        var request = SeedHistoryRequest(ctx, actor, poIssued.Id, approvedTotalAmount: null);
        var originalComment = "[Grupo P.O.: NCR ANGOLA INFORMATICA, LDA | Moeda: AOA | Total: 70,341.42 | GroupId: 11111111-1111-1111-1111-111111111111] Pagamento agendado. ";
        var historyId = Guid.NewGuid();
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = historyId,
            RequestId = request.Id,
            ActorUserId = actor.Id,
            ActionTaken = "PAYMENT_SCHEDULED",
            PreviousStatusId = poIssued.Id,
            NewStatusId = poIssued.Id,
            Comment = originalComment,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        await controller.GetHistory();
        await controller.ExportHistory();

        var storedComment = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.Id == historyId).Select(h => h.Comment).SingleAsync();
        Assert.Equal(originalComment, storedComment);
    }

    // ── SchedulePayment / MarkAsPaid: new comments use "Lote #n", never "GroupId" (spec tests 17-18) ──

    private static async Task<(Request request, RequestPoGroup group, Guid actorId)> SeedSingleGroupWithBatchAsync(ApplicationDbContext ctx, string groupStatus)
    {
        var (actor, quotationType, poIssued) = SeedCommon(ctx);
        ctx.RequestStatuses.Add(new RequestStatus { Id = 2, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Concluído", DisplayOrder = 60 });
        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-LOTE-COMMENT",
            Title = "ZZTEST lote comment",
            RequestTypeId = quotationType.Id,
            StatusId = poIssued.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            BatchNumber = 2,
            Status = RequestConstants.ApprovalBatchStatuses.Approved,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.ApprovalBatches.Add(batch);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ApprovalBatchId = batch.Id,
            SupplierNameSnapshot = "NCR ANGOLA INFORMATICA, LDA",
            CurrencyCode = "AOA",
            TotalAmount = 70341.42m,
            Status = groupStatus,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        await ctx.SaveChangesAsync();
        return (request, group, actor.Id);
    }

    [Fact]
    public async Task SchedulePayment_NewComment_UsesLoteNumber_NotGroupId()
    {
        var ctx = NewContext();
        var (request, group, actorId) = await SeedSingleGroupWithBatchAsync(ctx, RequestConstants.PoGroupStatuses.PoIssued);
        var controller = BuildController(ctx, actorId);

        var result = await controller.SchedulePayment(request.Id, new SchedulePaymentDto
        {
            RequestPoGroupId = group.Id,
            ScheduledDate = DateTime.UtcNow.AddDays(3)
        });
        Assert.IsType<OkResult>(result);

        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == request.Id);
        Assert.Contains("Lote #2", history.Comment);
        Assert.Contains("NCR ANGOLA INFORMATICA, LDA", history.Comment);
        Assert.DoesNotContain("GroupId", history.Comment);
    }

    [Fact]
    public async Task MarkAsPaid_NewComment_UsesLoteNumber_AndContainsAmount()
    {
        var ctx = NewContext();
        var (request, group, actorId) = await SeedSingleGroupWithBatchAsync(ctx, RequestConstants.PoGroupStatuses.PoIssued);

        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "comprovativo.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = "PAYMENT_PROOF",
            IsDeleted = false
        };
        ctx.RequestAttachments.Add(attachment);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actorId);
        var paidDate = new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc);
        var result = await controller.MarkAsPaid(request.Id, new ConfirmPaymentDto
        {
            RequestPoGroupId = group.Id,
            PaymentProofAttachmentId = attachment.Id,
            ActualPaidAmount = group.TotalAmount,
            PaidDate = paidDate
        });
        Assert.IsType<OkResult>(result);

        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == request.Id && h.ActionTaken == "PAYMENT_COMPLETED");
        Assert.Contains("Lote #2", history.Comment);
        Assert.Contains($"Montante: {group.TotalAmount:N2}", history.Comment);
        Assert.Contains("24/07/2026", history.Comment);
        Assert.DoesNotContain("GroupId", history.Comment);
    }
}
