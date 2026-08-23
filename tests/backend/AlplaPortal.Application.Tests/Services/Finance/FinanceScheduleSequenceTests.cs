using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
/// Regression guard for the confirmed multi-group scheduling defect: FinanceController.SchedulePayment
/// computed the next RequestPayment.PaymentSequence PER GROUP, but the unique index is REQUEST-scoped
/// (RequestId, PaymentType, PaymentSequence). Scheduling a second same-type group therefore restarted
/// at 1 and collided. The fix computes the next sequence across ALL groups of the request, per type.
///
/// InMemory EF does not enforce unique indexes, so these tests assert the COMPUTED sequence value
/// (the fix's logic) rather than relying on a duplicate-key throw. The real DB-level behaviour is
/// proven end-to-end by the ZZTEST-FIN mutable acceptance battery against the SQL Server clone.
/// Same InMemory-EF direct-controller pattern as FinanceReturnForAdjustmentTests.
/// </summary>
public class FinanceScheduleSequenceTests
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
            new(ClaimTypes.Role, RoleConstants.Finance)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private static void SeedStatuses(ApplicationDbContext ctx)
    {
        var codes = new[]
        {
            RequestConstants.Statuses.FinalApproved, RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.PoPartiallyUploaded, RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.Paid, RequestConstants.Statuses.WaitingReceipt,
            RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.Completed,
            RequestConstants.PoGroupStatuses.WaitingPo, RequestConstants.Statuses.WaitingPoCorrection,
            RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentScheduled,
            RequestConstants.Statuses.AdvancePaymentCompleted, RequestConstants.Statuses.WaitingSupplierDelivery,
            RequestConstants.Statuses.WaitingReconciliation, RequestConstants.Statuses.Cancelled
        }.Distinct().ToArray();

        int id = 1;
        foreach (var code in codes)
            ctx.RequestStatuses.Add(new RequestStatus { Id = id++, Code = code, Name = code, DisplayOrder = id });
    }

    private static int StatusId(ApplicationDbContext ctx, string code) => ctx.RequestStatuses.Single(s => s.Code == code).Id;

    /// <summary>Seeds a QUOTATION request with one group per (status, amount) tuple. Returns request id, ordered group ids, actor id.</summary>
    private static async Task<(Guid reqId, List<Guid> groupIds, Guid actorId)> SeedQuotationAsync(
        ApplicationDbContext ctx, params (string status, decimal amount)[] groups)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"fin-{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        SeedStatuses(ctx);
        await ctx.SaveChangesAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = $"ZZTEST-SEQ-{Guid.NewGuid().ToString()[..8]}",
            Title = "ZZTEST Schedule Sequence",
            RequestTypeId = 2,
            StatusId = StatusId(ctx, RequestConstants.Statuses.PoIssued),
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var ids = new List<Guid>();
        foreach (var (status, amount) in groups)
        {
            var g = new RequestPoGroup
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                SupplierNameSnapshot = $"ZZTEST Supplier {ids.Count + 1}",
                CurrencyCode = "AOA",
                TotalAmount = amount,
                Status = status,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
                CreatedByUserId = actor.Id
            };
            ctx.RequestPoGroups.Add(g);
            ids.Add(g.Id);
        }
        await ctx.SaveChangesAsync();
        return (request.Id, ids, actor.Id);
    }

    private static void AddPayment(ApplicationDbContext ctx, Guid reqId, Guid groupId, string type, string status, int seq, decimal amount)
    {
        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = reqId,
            RequestPoGroupId = groupId,
            PaymentType = type,
            PaymentStatus = status,
            PaymentSequence = seq,
            PlannedAmount = amount,
            CurrencyCode = "AOA",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        ctx.SaveChanges();
    }

    private static RequestPayment ScheduledPaymentFor(ApplicationDbContext ctx, Guid groupId) =>
        ctx.RequestPayments.AsNoTracking()
            .Where(p => p.RequestPoGroupId == groupId && p.PaymentStatus == RequestPayment.PaymentStatuses.Scheduled)
            .OrderByDescending(p => p.PaymentSequence)
            .First();

    // 1. Paid sibling (FINAL_BALANCE seq1) + PO_ISSUED sibling → scheduling the 2nd yields seq2, sibling untouched.
    [Fact]
    public async Task Schedule_SecondGroup_WhenPaidSiblingHasSeq1_GetsSeq2_SiblingUntouched()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx,
            (RequestConstants.PoGroupStatuses.PaymentCompleted, 300000m),
            (RequestConstants.PoGroupStatuses.PoIssued, 175000m));
        var paidGroup = ids[0];
        var actionableGroup = ids[1];
        AddPayment(ctx, reqId, paidGroup, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Completed, 1, 300000m);

        var controller = BuildController(ctx, actorId);
        var result = await controller.SchedulePayment(reqId, new SchedulePaymentDto
        {
            RequestPoGroupId = actionableGroup,
            ScheduledDate = DateTime.UtcNow.Date.AddDays(7)
        });

        Assert.IsType<OkResult>(result);
        var newPayment = ScheduledPaymentFor(ctx, actionableGroup);
        Assert.Equal(RequestPayment.PaymentTypes.FinalBalance, newPayment.PaymentType);
        Assert.Equal(2, newPayment.PaymentSequence); // request-scoped: max(FINAL_BALANCE seq)=1, +1

        var actionable = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == actionableGroup);
        Assert.Equal(RequestConstants.PoGroupStatuses.PaymentScheduled, actionable.Status);
        var paid = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == paidGroup);
        Assert.Equal(RequestConstants.PoGroupStatuses.PaymentCompleted, paid.Status); // unchanged
    }

    // 2. Two fresh PO_ISSUED groups → seq1 then seq2.
    [Fact]
    public async Task Schedule_TwoFreshGroups_YieldSeq1ThenSeq2()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx,
            (RequestConstants.PoGroupStatuses.PoIssued, 100000m),
            (RequestConstants.PoGroupStatuses.PoIssued, 200000m));
        var controller = BuildController(ctx, actorId);

        Assert.IsType<OkResult>(await controller.SchedulePayment(reqId, new SchedulePaymentDto { RequestPoGroupId = ids[0], ScheduledDate = DateTime.UtcNow.Date.AddDays(5) }));
        Assert.IsType<OkResult>(await controller.SchedulePayment(reqId, new SchedulePaymentDto { RequestPoGroupId = ids[1], ScheduledDate = DateTime.UtcNow.Date.AddDays(6) }));

        Assert.Equal(1, ScheduledPaymentFor(ctx, ids[0]).PaymentSequence);
        Assert.Equal(2, ScheduledPaymentFor(ctx, ids[1]).PaymentSequence);
    }

    // 3. Sequences are independent PER TYPE: an existing ADVANCE seq1 does not push a fresh FINAL_BALANCE to seq2.
    [Fact]
    public async Task Schedule_SequencesIndependentPerType()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx,
            (RequestConstants.PoGroupStatuses.AdvancePaymentRequired, 500000m),
            (RequestConstants.PoGroupStatuses.PoIssued, 175000m));
        // Group 0 already carries an ADVANCE seq1 (as RegisterPo would create).
        AddPayment(ctx, reqId, ids[0], RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Planned, 1, 150000m);

        var controller = BuildController(ctx, actorId);
        // Schedule the FINAL_BALANCE for the PO_ISSUED group.
        Assert.IsType<OkResult>(await controller.SchedulePayment(reqId, new SchedulePaymentDto { RequestPoGroupId = ids[1], ScheduledDate = DateTime.UtcNow.Date.AddDays(5) }));

        var fb = ScheduledPaymentFor(ctx, ids[1]);
        Assert.Equal(RequestPayment.PaymentTypes.FinalBalance, fb.PaymentType);
        Assert.Equal(1, fb.PaymentSequence); // FINAL_BALANCE starts at 1 despite the ADVANCE seq1
    }

    // 4. A CANCELLED sibling sequence is preserved and never reused: rescheduling lands on the next sequence.
    [Fact]
    public async Task Schedule_AfterCancelledSequence_DoesNotReuse_LandsOnNext()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx,
            (RequestConstants.PoGroupStatuses.PoIssued, 90000m));
        var group = ids[0];
        // A previously-cancelled schedule attempt for this same group/type (audit-preserved seq1).
        AddPayment(ctx, reqId, group, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Cancelled, 1, 90000m);

        var controller = BuildController(ctx, actorId);
        Assert.IsType<OkResult>(await controller.SchedulePayment(reqId, new SchedulePaymentDto { RequestPoGroupId = group, ScheduledDate = DateTime.UtcNow.Date.AddDays(4) }));

        var scheduled = ScheduledPaymentFor(ctx, group);
        Assert.Equal(2, scheduled.PaymentSequence); // cancelled seq1 counted, not reused

        // Audit preserved: the CANCELLED row still exists at seq1.
        var cancelled = await ctx.RequestPayments.AsNoTracking()
            .SingleAsync(p => p.RequestPoGroupId == group && p.PaymentStatus == RequestPayment.PaymentStatuses.Cancelled);
        Assert.Equal(1, cancelled.PaymentSequence);
    }

    // 5. The uniqueness contract is unchanged: a unique index on (RequestId, PaymentType, PaymentSequence) still exists.
    [Fact]
    public void UniqueIndex_RequestId_PaymentType_PaymentSequence_StillDefined()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(RequestPayment))!;
        var match = entity.GetIndexes().FirstOrDefault(ix =>
            ix.IsUnique &&
            ix.Properties.Select(p => p.Name).SequenceEqual(new[] { "RequestId", "PaymentType", "PaymentSequence" }));
        Assert.NotNull(match);
    }
}
