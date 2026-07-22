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
        var controller = new FinanceController(
            ctx,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            NullLogger<FinanceController>.Instance,
            new Mock<IStatusAggregationService>().Object,
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
}
