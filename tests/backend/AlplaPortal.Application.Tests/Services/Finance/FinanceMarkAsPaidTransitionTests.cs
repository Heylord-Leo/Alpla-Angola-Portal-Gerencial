using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Purchasing;
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
/// Covers the actual MarkAsPaid state transition end-to-end (FinanceController.MarkAsPaid), not just
/// the eligibility predicate already covered by FinancePaymentEligibilityServiceTests. This exercises
/// the exact DEC-149 self-healing scenario: a PAYMENT request whose parent is PO_ISSUED but whose
/// only RequestPoGroup is stuck at the legacy PENDING status. No controller-level HTTP/auth test
/// framework exists in this repo (confirmed — see QuotationReuseAuthorizationIntegrationTests for the
/// closest precedent), so this instantiates FinanceController directly against an EF Core InMemory
/// ApplicationDbContext with a fake ClaimsPrincipal carrying the SystemAdministrator role, which
/// BaseController.GetScopedRequestsQuery() special-cases to bypass Plant/Department scope seeding.
/// </summary>
public class FinanceMarkAsPaidTransitionTests
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
        // A real StatusAggregationService (not a mock) is used so the QUOTATION multi-group tests
        // below genuinely exercise parent-status aggregation, not just an unverified interaction.
        // The pre-existing PAYMENT-type test never reaches this call (MarkAsPaid only aggregates
        // for QUOTATION requests), so this substitution does not change its behavior.
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

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid AttachmentId, Guid ActorId, int PoIssuedStatusId, int PaymentCompletedStatusId);

    /// <summary>
    /// Mirrors the confirmed bug cohort: PAYMENT type, parent Request.Status = PO_ISSUED,
    /// RequestPoGroup.Status = PENDING (legacy, pre-finance-status backfill row).
    /// </summary>
    private static async Task<Seed> SeedLegacyPendingGroupAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = "finance@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);

        var poIssued = new RequestStatus { Id = 1, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 30 };
        var paymentCompleted = new RequestStatus { Id = 2, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Concluído", DisplayOrder = 90 };
        ctx.RequestStatuses.AddRange(poIssued, paymentCompleted);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "REQ-14/07/2026-059",
            Title = "ZZTEST Legacy Payment",
            RequestTypeId = requestType.Id,
            StatusId = poIssued.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            ApprovedTotalAmount = 1000m,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            TotalAmount = 1000m,
            Status = RequestConstants.PoGroupStatuses.Pending, // the legacy stuck value under test
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        var payment = new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = group.Id,
            PaymentType = RequestPayment.PaymentTypes.FinalBalance,
            PlannedAmount = 1000m,
            CurrencyCode = "AOA",
            PaymentStatus = RequestPayment.PaymentStatuses.Planned,
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
        };
        ctx.RequestPayments.Add(payment);

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
        return new Seed(request.Id, group.Id, attachment.Id, actor.Id, poIssued.Id, paymentCompleted.Id);
    }

    [Fact]
    public async Task MarkAsPaid_Payment_ParentPoIssued_GroupPending_TransitionsBothToPaymentCompleted_AndPersistsPaymentDetails()
    {
        var ctx = NewContext();
        var seed = await SeedLegacyPendingGroupAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);
        var paidDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);

        var result = await controller.MarkAsPaid(seed.RequestId, new ConfirmPaymentDto
        {
            RequestPoGroupId = seed.GroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = 1000m,
            PaidDate = paidDate
        });

        Assert.IsType<OkResult>(result);

        var request = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);

        // The "impossible state" (Request=PAYMENT_COMPLETED / Group=PENDING) is unreachable:
        // MarkAsPaid unconditionally advances the group to the same paid status as the request.
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, request.Status.Code);
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, group.Status);
        Assert.NotEqual(RequestConstants.PoGroupStatuses.Pending, group.Status);

        Assert.Equal(1000m, request.ActualPaidAmount);
        Assert.Equal(paidDate, request.ActualPaidAtUtc);

        var history = await ctx.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == seed.RequestId && h.ActionTaken == "PAYMENT_COMPLETED")
            .ToListAsync();
        var record = Assert.Single(history);
        Assert.Equal(seed.PoIssuedStatusId, record.PreviousStatusId);
        Assert.Equal(seed.PaymentCompletedStatusId, record.NewStatusId);

        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == seed.GroupId);
        Assert.Equal(1000m, payment.ActualPaidAmount);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, payment.PaymentStatus);
    }

    // ── SchedulePayment: the same legacy-PENDING-group scenario, now closing the "Agendar
    // pagamento" asymmetry (2026-07-25 Finance Payments UI regression) ──

    [Fact]
    public async Task SchedulePayment_Payment_ParentPoIssued_GroupPending_Succeeds_AndSelfHealsGroupToPaymentScheduled()
    {
        var ctx = NewContext();
        var seed = await SeedLegacyPendingGroupAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);
        var scheduledDate = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

        var groupCountBefore = await ctx.RequestPoGroups.CountAsync(g => g.RequestId == seed.RequestId);

        var result = await controller.SchedulePayment(seed.RequestId, new SchedulePaymentDto
        {
            RequestPoGroupId = seed.GroupId,
            ScheduledDate = scheduledDate,
            Comment = "Agendado via teste"
        });

        Assert.IsType<OkResult>(result);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.Statuses.PaymentScheduled, group.Status);
        Assert.NotEqual(RequestConstants.PoGroupStatuses.Pending, group.Status);

        // No duplicate group was created for this request.
        var groupCountAfter = await ctx.RequestPoGroups.CountAsync(g => g.RequestId == seed.RequestId);
        Assert.Equal(groupCountBefore, groupCountAfter);
        Assert.Equal(1, groupCountAfter);

        // SchedulePayment creates a new RequestPayment row alongside the seed's original "Planned"
        // one (never reused/mutated) — assert on the newly-created "Scheduled" row specifically.
        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == seed.GroupId && p.PaymentStatus == RequestPayment.PaymentStatuses.Scheduled);
        Assert.Equal(scheduledDate, payment.ScheduledDateUtc);

        var history = await ctx.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == seed.RequestId && h.ActionTaken == RequestConstants.Statuses.PaymentScheduled)
            .ToListAsync();
        Assert.Single(history);
    }

    [Fact]
    public async Task SchedulePayment_Payment_ParentPaymentCompleted_GroupPending_Rejected_FallbackDoesNotOverreach()
    {
        // The narrow parent fallback set excludes PAYMENT_COMPLETED — scheduling must still be
        // rejected even though the group itself carries no information (PENDING).
        var ctx = NewContext();
        var seed = await SeedLegacyPendingGroupAsync(ctx);
        var request = await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId);
        request.StatusId = seed.PaymentCompletedStatusId;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var result = await controller.SchedulePayment(seed.RequestId, new SchedulePaymentDto
        {
            RequestPoGroupId = seed.GroupId,
            ScheduledDate = DateTime.UtcNow.AddDays(3)
        });

        Assert.IsType<BadRequestObjectResult>(result);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Pending, group.Status); // unchanged
    }

    // ── QUOTATION, multi-group direct payment (request 100's exact shape) ──

    private sealed record QuotationSeed(
        Guid RequestId, Guid NcrGroupId, Guid ItecGroupId, Guid AttachmentId, Guid ActorId,
        int AdvancePaymentRequiredStatusId, int PoIssuedStatusId, int PaymentCompletedStatusId, int AdvancePaymentCompletedStatusId);

    /// <summary>
    /// Mirrors request 100: QUOTATION request, two RequestPoGroups — NCR ANGOLA INFORMATICA, LDA
    /// (PO_ISSUED, 70,341.42) and ITEC LDA (ADVANCE_PAYMENT_REQUIRED, 275,139.00, with a Planned
    /// advance RequestPayment already auto-created at PO registration, per RegisterPo). No
    /// ApprovalBatch is seeded — this exercises RequestStatusCalculator's batchless-group path,
    /// which is sufficient for aggregation over already-active PoGroups.
    /// </summary>
    private static async Task<QuotationSeed> SeedMultiGroupQuotationAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = "finance-quotation@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 2, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        ctx.RequestTypes.Add(requestType);

        var advanceRequired = new RequestStatus { Id = 20, Code = RequestConstants.Statuses.AdvancePaymentRequired, Name = "Adiantamento Necessário", DisplayOrder = 23 };
        var poIssued = new RequestStatus { Id = 21, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 13 };
        var paymentCompleted = new RequestStatus { Id = 22, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Concluído", DisplayOrder = 16 };
        var advanceCompleted = new RequestStatus { Id = 23, Code = RequestConstants.Statuses.AdvancePaymentCompleted, Name = "Adiantamento Concluído", DisplayOrder = 24 };
        ctx.RequestStatuses.AddRange(advanceRequired, poIssued, paymentCompleted, advanceCompleted);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ-100-CLONE",
            Title = "ZZTEST Multi-group quotation",
            RequestTypeId = requestType.Id,
            StatusId = advanceRequired.Id, // furthest-behind of {PO_ISSUED, ADVANCE_PAYMENT_REQUIRED} today
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

        var ncrFinalBalancePayment = new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = ncrGroup.Id,
            PaymentType = RequestPayment.PaymentTypes.FinalBalance,
            PlannedAmount = ncrGroup.TotalAmount,
            CurrencyCode = "AOA",
            PaymentStatus = RequestPayment.PaymentStatuses.Planned,
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        var itecAdvancePayment = new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = itecGroup.Id,
            PaymentType = RequestPayment.PaymentTypes.Advance,
            PlannedAmount = 82541.70m, // 30% of 275,139.00 — the domain-calculated required advance
            CurrencyCode = "AOA",
            PaymentStatus = RequestPayment.PaymentStatuses.Planned, // never scheduled — direct-pay path
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.RequestPayments.AddRange(ncrFinalBalancePayment, itecAdvancePayment);

        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "comprovativo-ncr.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = "PAYMENT_PROOF",
            IsDeleted = false
        };
        ctx.RequestAttachments.Add(attachment);

        await ctx.SaveChangesAsync();
        return new QuotationSeed(request.Id, ncrGroup.Id, itecGroup.Id, attachment.Id, actor.Id,
            advanceRequired.Id, poIssued.Id, paymentCompleted.Id, advanceCompleted.Id);
    }

    // Test matrix item 12: normal direct full payment accepted.
    // Test matrix item 19: sibling RequestPoGroup (ITEC) remains unchanged.
    // Test matrix item 20: parent status remains at the furthest-behind group via StatusAggregationService.
    [Fact]
    public async Task MarkAsPaid_Quotation_NcrGroup_FullAmount_Accepted_SiblingAndParentUnaffectedByItsOwnStatus()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);
        var paidDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);

        var result = await controller.MarkAsPaid(seed.RequestId, new ConfirmPaymentDto
        {
            RequestPoGroupId = seed.NcrGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = 70341.42m, // exactly the group's TotalAmount
            PaidDate = paidDate
        });

        Assert.IsType<OkResult>(result);

        var ncrGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.NcrGroupId);
        var itecGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.ItecGroupId);
        var request = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);

        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, ncrGroup.Status);

        // Sibling untouched.
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, itecGroup.Status);

        // Parent must NOT read as globally paid — ITEC (priority 24) is still further behind
        // than NCR's new PAYMENT_COMPLETED (priority 60), so the parent stays at ITEC's status.
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, request.Status.Code);
        Assert.NotEqual(RequestConstants.Statuses.PaymentCompleted, request.Status.Code);
    }

    // Test matrix item 13: normal payment below required amount rejected, with no mutations.
    [Fact]
    public async Task MarkAsPaid_Quotation_NcrGroup_BelowRequiredAmount_RejectedWithNoMutations()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.MarkAsPaid(seed.RequestId, new ConfirmPaymentDto
        {
            RequestPoGroupId = seed.NcrGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = 50000m, // below the group's 70,341.42 total
            PaidDate = DateTime.UtcNow
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Montante Insuficiente", problem.Title);

        var ncrGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.NcrGroupId);
        var request = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == seed.NcrGroupId);
        var historyCount = await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId);

        Assert.Equal(RequestConstants.Statuses.PoIssued, ncrGroup.Status); // unchanged
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, request.Status.Code); // unchanged (seeded value)
        Assert.Null(payment.ActualPaidAmount); // unchanged
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, payment.PaymentStatus); // unchanged
        Assert.Equal(0, historyCount); // no history row written
    }

    // Test matrix item 14: normal overpayment preserves existing divergence behavior (accepted, logged).
    [Fact]
    public async Task MarkAsPaid_Quotation_NcrGroup_Overpayment_AcceptedWithDivergenceLogged()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.MarkAsPaid(seed.RequestId, new ConfirmPaymentDto
        {
            RequestPoGroupId = seed.NcrGroupId,
            PaymentProofAttachmentId = seed.AttachmentId,
            ActualPaidAmount = 75000m, // above the group's 70,341.42 total
            PaidDate = DateTime.UtcNow
        });

        Assert.IsType<OkResult>(result);

        var ncrGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.NcrGroupId);
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, ncrGroup.Status);

        var divergence = await ctx.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == seed.RequestId && h.ActionTaken == "PAYMENT_DIVERGENCE_DETECTED")
            .ToListAsync();
        Assert.Single(divergence);
    }

    // Test matrix item 18: no proof attachment remains rejected by existing validation (QUOTATION path).
    [Fact]
    public async Task MarkAsPaid_Quotation_NcrGroup_NoProofAttachment_Rejected()
    {
        var ctx = NewContext();
        var seed = await SeedMultiGroupQuotationAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.MarkAsPaid(seed.RequestId, new ConfirmPaymentDto
        {
            RequestPoGroupId = seed.NcrGroupId,
            PaymentProofAttachmentId = Guid.Empty,
            ActualPaidAmount = 70341.42m,
            PaidDate = DateTime.UtcNow
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Comprovativo Obrigatório", problem.Title);
    }
}
