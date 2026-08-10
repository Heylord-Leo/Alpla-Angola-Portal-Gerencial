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
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Covers RequestsController.ConfirmAdvancePayment end-to-end against request 100's exact shape
/// (QUOTATION, two RequestPoGroups — NCR ANGOLA INFORMATICA, LDA at PO_ISSUED and ITEC LDA at
/// ADVANCE_PAYMENT_REQUIRED). No controller-level HTTP/auth test framework exists in this repo
/// (see FinanceMarkAsPaidTransitionTests for the established precedent) — this instantiates
/// RequestsController directly against an EF Core InMemory ApplicationDbContext, with every
/// constructor dependency ConfirmAdvancePayment does not touch mocked away, and a real
/// StatusAggregationService wired into HttpContext.RequestServices (the method resolves it via
/// HttpContext.RequestServices.GetRequiredService, not constructor injection).
/// </summary>
public class ConfirmAdvancePaymentTests
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
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
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
            // Post-Payment Completion defaults to disabled — existing behaviour must be unchanged.
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Microsoft.Extensions.Options.Options.Create(new AlplaPortal.Domain.Configuration.PostPaymentCompletionOptions()));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.Finance)
        };

        // ConfirmAdvancePayment resolves IStatusAggregationService via
        // HttpContext.RequestServices.GetRequiredService — a real instance is wired here (not a
        // mock) so the sibling-isolation/aggregation tests genuinely exercise it.
        var services = new ServiceCollection();
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));
        var serviceProvider = services.BuildServiceProvider();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
                RequestServices = serviceProvider
            }
        };
        return controller;
    }

    private sealed record Seed(
        Guid RequestId, Guid NcrGroupId, Guid ItecGroupId, Guid AttachmentId, Guid ActorId, decimal PlannedAdvanceAmount);

    /// <summary>Mirrors request 100 — see FinanceMarkAsPaidTransitionTests.SeedMultiGroupQuotationAsync.</summary>
    private static async Task<Seed> SeedMultiGroupQuotationAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = "finance-advance@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 3, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        ctx.RequestTypes.Add(requestType);

        var advanceRequired = new RequestStatus { Id = 30, Code = RequestConstants.Statuses.AdvancePaymentRequired, Name = "Adiantamento Necessário", DisplayOrder = 23 };
        var poIssued = new RequestStatus { Id = 31, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 13 };
        var advanceCompleted = new RequestStatus { Id = 32, Code = RequestConstants.Statuses.AdvancePaymentCompleted, Name = "Adiantamento Concluído", DisplayOrder = 24 };
        ctx.RequestStatuses.AddRange(advanceRequired, poIssued, advanceCompleted);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ-100-CLONE-B2P",
            Title = "ZZTEST Multi-group quotation (advance)",
            RequestTypeId = requestType.Id,
            StatusId = advanceRequired.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var ncrGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "NCR ANGOLA INFORMATICA, LDA",
            CurrencyCode = "AOA",
            TotalAmount = 70341.42m,
            Status = RequestConstants.Statuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = actor.Id
        };
        var itecGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ITEC LDA",
            CurrencyCode = "AOA",
            TotalAmount = 275139.00m,
            Status = RequestConstants.Statuses.AdvancePaymentRequired,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.AddRange(ncrGroup, itecGroup);

        var plannedAdvanceAmount = 82541.70m; // 30% of 275,139.00
        itecGroup.AdvancePaymentPercent = 30m;

        var itecAdvancePayment = new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = itecGroup.Id,
            PaymentType = RequestPayment.PaymentTypes.Advance,
            PlannedAmount = plannedAdvanceAmount,
            CurrencyCode = "AOA",
            PaymentStatus = RequestPayment.PaymentStatuses.Planned, // never scheduled — direct-pay path
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.RequestPayments.Add(itecAdvancePayment);

        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "comprovativo-itec.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = "PAYMENT_PROOF",
            IsDeleted = false
        };
        ctx.RequestAttachments.Add(attachment);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, ncrGroup.Id, itecGroup.Id, attachment.Id, actor.Id, plannedAdvanceAmount);
    }

    // Test matrix item 15: advance direct payment equal to PlannedAmount accepted.
    // Test matrix item 19 (advance side): sibling (NCR) remains unchanged.
    // Test matrix item 20 (advance side): parent status reflects the furthest-behind group via
    // StatusAggregationService — NCR (PO_ISSUED, priority 30) is now behind ITEC's new
    // ADVANCE_PAYMENT_COMPLETED (priority 26), so the parent becomes ADVANCE_PAYMENT_COMPLETED.
    [Fact]
    public async Task ConfirmAdvancePayment_EqualToPlannedAmount_Accepted_ResultsInAdvancePaymentCompleted_SiblingUnaffected()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);
        var paidDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);

        var result = await controller.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.ItecGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = seed.PlannedAdvanceAmount,
            PaidDate = paidDate
        });

        Assert.IsType<OkObjectResult>(result);

        var itecGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.ItecGroupId);
        var ncrGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.NcrGroupId);
        var request = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);

        // Preserves the existing result — never labeled as a full/final payment, and never
        // invents a transition to WAITING_SUPPLIER_DELIVERY (unreachable by any current code path).
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentCompleted, itecGroup.Status);
        Assert.NotEqual(RequestConstants.Statuses.PaymentCompleted, itecGroup.Status);

        // Sibling untouched.
        Assert.Equal(RequestConstants.Statuses.PoIssued, ncrGroup.Status);

        // Parent aggregation still ran (via StatusAggregationService) and picked the furthest-behind group.
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentCompleted, request.Status.Code);

        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == seed.ItecGroupId);
        Assert.Equal(seed.PlannedAdvanceAmount, payment.ActualPaidAmount);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, payment.PaymentStatus);
        Assert.False(payment.HasDivergence);
    }

    // Test matrix item 16: advance payment below PlannedAmount rejected, with no mutations.
    [Fact]
    public async Task ConfirmAdvancePayment_BelowPlannedAmount_RejectedWithNoMutations()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.ItecGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = seed.PlannedAdvanceAmount - 10000m, // below the planned advance
            PaidDate = DateTime.UtcNow
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Montante Insuficiente", problem.Title);

        var itecGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.ItecGroupId);
        var request = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == seed.ItecGroupId);
        var historyCount = await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId);

        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, itecGroup.Status); // unchanged
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, request.Status.Code); // unchanged (seeded value)
        Assert.Null(payment.ActualPaidAmount); // unchanged
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, payment.PaymentStatus); // unchanged
        Assert.Null(payment.PaymentProofAttachmentId); // unchanged
        Assert.Equal(0, historyCount); // no history row written
    }

    // Test matrix item 17: advance payment above PlannedAmount preserves existing divergence behavior.
    [Fact]
    public async Task ConfirmAdvancePayment_AbovePlannedAmount_AcceptedWithDivergenceRecorded()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);
        var overpaidAmount = seed.PlannedAdvanceAmount + 5000m;

        var result = await controller.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.ItecGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = overpaidAmount,
            PaidDate = DateTime.UtcNow
        });

        Assert.IsType<OkObjectResult>(result);

        var itecGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.ItecGroupId);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentCompleted, itecGroup.Status);

        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == seed.ItecGroupId);
        Assert.Equal(overpaidAmount, payment.ActualPaidAmount);
        Assert.True(payment.HasDivergence);
        Assert.Equal(5000m, payment.DivergenceAmount);
    }

    // ── Finance History event (request 100's missing "Adiantamento realizado" event) ──

    [Fact]
    public async Task ConfirmAdvancePayment_CreatesExactlyOneHistoryRow_WithAdvancePaymentCompletedActionTaken()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);
        var paidDate = new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc);

        var result = await controller.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.ItecGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = seed.PlannedAdvanceAmount,
            PaidDate = paidDate
        });
        Assert.IsType<OkObjectResult>(result);

        var history = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == seed.RequestId).ToListAsync();
        var row = Assert.Single(history);

        Assert.Equal("ADVANCE_PAYMENT_COMPLETED", row.ActionTaken);
        Assert.DoesNotContain(history, h => h.ActionTaken == "CONFIRM_ADVANCE");

        // Amount, percentage, and paid date all present in the comment.
        Assert.Contains("ITEC LDA", row.Comment);
        Assert.Contains($"Montante: {seed.PlannedAdvanceAmount:N2}", row.Comment);
        Assert.Contains("30", row.Comment); // AdvancePaymentPercent
        Assert.Contains("24/07/2026", row.Comment);

        // No ApprovalBatch was seeded for this group -> "Lote #" segment must be omitted safely,
        // never "Lote #0" or an empty placeholder.
        Assert.DoesNotContain("Lote #0", row.Comment);
        Assert.DoesNotContain("Lote #", row.Comment);
    }

    [Fact]
    public async Task ConfirmAdvancePayment_CommentIncludesLoteNumber_WhenApprovalBatchExists()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            BatchNumber = 1,
            Status = RequestConstants.ApprovalBatchStatuses.Approved,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
        };
        ctx.ApprovalBatches.Add(batch);
        var itecGroup = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.ItecGroupId);
        itecGroup.ApprovalBatchId = batch.Id;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var result = await controller.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.ItecGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = seed.PlannedAdvanceAmount,
            PaidDate = DateTime.UtcNow
        });
        Assert.IsType<OkObjectResult>(result);

        var row = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == seed.RequestId);
        Assert.Contains("Lote #1", row.Comment);
        Assert.DoesNotContain("GroupId", row.Comment);
    }

    [Fact]
    public async Task ConfirmAdvancePayment_RejectedInsufficientAmount_CreatesNoHistoryRow()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.ItecGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = seed.PlannedAdvanceAmount - 10000m,
            PaidDate = DateTime.UtcNow
        });
        Assert.IsType<BadRequestObjectResult>(result);

        var historyCount = await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId);
        Assert.Equal(0, historyCount);
    }

    [Fact]
    public async Task ConfirmAdvancePayment_RepeatedCall_DoesNotCreateDuplicateHistoryRow()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var first = await controller.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.ItecGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = seed.PlannedAdvanceAmount,
            PaidDate = DateTime.UtcNow
        });
        Assert.IsType<OkObjectResult>(first);

        // Group is now ADVANCE_PAYMENT_COMPLETED — the existing status guard must reject a repeat.
        var second = await controller.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.ItecGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = seed.PlannedAdvanceAmount,
            PaidDate = DateTime.UtcNow
        });
        Assert.IsType<BadRequestObjectResult>(second);

        var historyCount = await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "ADVANCE_PAYMENT_COMPLETED");
        Assert.Equal(1, historyCount);
    }
}
