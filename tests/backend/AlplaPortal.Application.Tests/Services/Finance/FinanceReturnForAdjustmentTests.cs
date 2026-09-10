using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AlplaPortal.Application.Interfaces;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Covers the v2.230.0 GROUP-scoped Finance "Return for Adjustment". The key invariant: returning
/// one RequestPoGroup to WAITING_PO_CORRECTION must never touch a sibling group (e.g. one already
/// PAYMENT_COMPLETED), and the request scalar must be re-derived by StatusAggregationService rather
/// than hard-set. Same InMemory-EF direct-controller pattern as FinanceCancelScheduleTests.
/// </summary>
public class FinanceReturnForAdjustmentTests
{
    private const string ValidReason = "Número da P.O incorreto — corrigir e reenviar.";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            // v2.242.0: ReturnForAdjustment now opens an explicit transaction; InMemory treats it as
            // a no-op only when this warning is suppressed.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
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

    /// <summary>Seeds a comprehensive RequestStatus set so aggregation always finds its computed target.</summary>
    private static void SeedStatuses(ApplicationDbContext ctx)
    {
        var codes = new[]
        {
            RequestConstants.Statuses.WaitingQuotation, RequestConstants.Statuses.WaitingAreaApproval,
            RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.FinalApproved,
            RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PoPartiallyUploaded,
            RequestConstants.Statuses.PaymentRequestSent, RequestConstants.Statuses.PaymentScheduled,
            RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.WaitingReceipt, RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed, RequestConstants.Statuses.QuotationCompleted,
            RequestConstants.PoGroupStatuses.WaitingPo, RequestConstants.Statuses.WaitingPoCorrection,
            RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentScheduled,
            RequestConstants.Statuses.AdvancePaymentCompleted, RequestConstants.Statuses.WaitingSupplierDelivery,
            RequestConstants.Statuses.WaitingReconciliation, RequestConstants.Statuses.Cancelled,
            RequestConstants.Statuses.Rejected
        }.Distinct().ToArray();

        int id = 1;
        foreach (var code in codes)
            ctx.RequestStatuses.Add(new RequestStatus { Id = id++, Code = code, Name = code, DisplayOrder = id });
    }

    private static int StatusId(ApplicationDbContext ctx, string code) => ctx.RequestStatuses.Single(s => s.Code == code).Id;

    private sealed record TwoGroupSeed(Guid RequestId, Guid PaidGroupId, Guid ActionableGroupId, Guid ActorId);

    /// <summary>QUOTATION, two batchless groups: A PAYMENT_COMPLETED (paid) + B PO_ISSUED (actionable). Request scalar deliberately APPROVED to prove the guard is group-scoped, not parent-scoped.</summary>
    private static async Task<TwoGroupSeed> SeedTwoGroupsPaidAndActionableAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"fin-{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        SeedStatuses(ctx);
        await ctx.SaveChangesAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "REQ-20/07/2026-100",
            Title = "ZZTEST Two Groups Paid+Actionable",
            RequestTypeId = 2,
            StatusId = StatusId(ctx, RequestConstants.Statuses.FinalApproved),
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var paidGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "NCR ANGOLA INFORMATICA, LDA",
            CurrencyCode = "AOA",
            TotalAmount = 70341.42m,
            Status = RequestConstants.Statuses.PaymentCompleted,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedByUserId = actor.Id
        };
        var actionableGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ITEC LDA",
            CurrencyCode = "AOA",
            TotalAmount = 275139.00m,
            Status = RequestConstants.Statuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.AddRange(paidGroup, actionableGroup);
        await ctx.SaveChangesAsync();

        return new TwoGroupSeed(request.Id, paidGroup.Id, actionableGroup.Id, actor.Id);
    }

    [Fact]
    public async Task Return_GroupScoped_ReturnsOnlyTargetGroup_SiblingPaidGroupUntouched()
    {
        var ctx = NewContext();
        var seed = await SeedTwoGroupsPaidAndActionableAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto
        {
            RequestPoGroupId = seed.ActionableGroupId,
            Notes = ValidReason
        });

        Assert.IsType<OkResult>(result);

        var actionable = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.ActionableGroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, actionable.Status);

        // Sibling isolation — the paid group is byte-for-byte unchanged.
        var paid = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.PaidGroupId);
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, paid.Status);

        // Group-identifying audit row.
        var history = await ctx.RequestStatusHistories.AsNoTracking()
            .SingleAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "FINANCE_RETURN_ADJUSTMENT");
        Assert.Contains(seed.ActionableGroupId.ToString(), history.Comment);
        Assert.Contains(ValidReason, history.Comment);

        // Re-aggregation ran (scalar derived, not hard-set): a STATUS_SYNC row exists moving the
        // scalar away from the pre-return APPROVED value.
        var syncCount = await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "STATUS_SYNC");
        Assert.True(syncCount >= 1);
    }

    [Fact]
    public async Task Return_MultiGroup_WithoutGroupId_Rejected_NoMutation()
    {
        var ctx = NewContext();
        var seed = await SeedTwoGroupsPaidAndActionableAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto { Notes = ValidReason });

        Assert.IsType<BadRequestObjectResult>(result);
        var actionable = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.ActionableGroupId);
        Assert.Equal(RequestConstants.Statuses.PoIssued, actionable.Status); // untouched
        var paid = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.PaidGroupId);
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, paid.Status);
    }

    [Fact]
    public async Task Return_InvalidGroupForRequest_Rejected()
    {
        var ctx = NewContext();
        var seed = await SeedTwoGroupsPaidAndActionableAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto
        {
            RequestPoGroupId = Guid.NewGuid(),
            Notes = ValidReason
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Return_TargetGroupPaid_Rejected_NoMutation()
    {
        var ctx = NewContext();
        var seed = await SeedTwoGroupsPaidAndActionableAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        // Attempt to return the already-PAYMENT_COMPLETED group — must be rejected by CanReturnGroup.
        var result = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto
        {
            RequestPoGroupId = seed.PaidGroupId,
            Notes = ValidReason
        });

        Assert.IsType<BadRequestObjectResult>(result);
        var paid = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.PaidGroupId);
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, paid.Status);
    }

    [Fact]
    public async Task Return_SingleGroup_NoGroupId_UsesSoleGroup_BackwardCompatible()
    {
        var ctx = NewContext();
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"fin-{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        SeedStatuses(ctx);
        await ctx.SaveChangesAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "REQ-23/07/2026-500",
            Title = "ZZTEST Single Group Return",
            RequestTypeId = 1,
            StatusId = StatusId(ctx, RequestConstants.Statuses.PoIssued),
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "Fornecedor Único",
            CurrencyCode = "AOA",
            TotalAmount = 1000m,
            Status = RequestConstants.Statuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        // No RequestPoGroupId — backward-compatible single-group call.
        var result = await controller.ReturnForAdjustment(request.Id, new FinanceActionRequestDto { Notes = ValidReason });

        Assert.IsType<OkResult>(result);
        var refreshed = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, refreshed.Status);
    }

    [Fact]
    public async Task Return_Preserves_PoResponsibleBuyerId_SoOwnerStillOwnsTheCorrection()
    {
        var ctx = NewContext();
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"fin-{Guid.NewGuid()}@test.local" };
        var registrant = Guid.NewGuid(); // the Buyer who registered the P.O. (owns the correction)
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        SeedStatuses(ctx);
        await ctx.SaveChangesAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-14/08/2026-254", Title = "ZZTEST Preserve Owner",
            RequestTypeId = 1, StatusId = StatusId(ctx, RequestConstants.Statuses.PoIssued),
            RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1, CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "SIMOTECNICA", CurrencyCode = "AOA",
            TotalAmount = 1000m, Status = RequestConstants.Statuses.PoIssued, CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = actor.Id, PoResponsibleBuyerId = registrant
        });
        var groupId = ctx.RequestPoGroups.Local.Single().Id;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id); // Finance actor returns it — must NOT become the owner
        var result = await controller.ReturnForAdjustment(request.Id, new FinanceActionRequestDto { Notes = ValidReason });

        Assert.IsType<OkResult>(result);
        var refreshed = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, refreshed.Status);
        Assert.Equal(registrant, refreshed.PoResponsibleBuyerId); // preserved — Finance return never rewrites ownership
    }

    [Fact]
    public async Task Return_WrongRequestAssociation_NotFound()
    {
        var ctx = NewContext();
        var seed = await SeedTwoGroupsPaidAndActionableAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ReturnForAdjustment(Guid.NewGuid(), new FinanceActionRequestDto
        {
            RequestPoGroupId = seed.ActionableGroupId,
            Notes = ValidReason
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void FinanceController_IsGuarded_ForFinanceRole()
    {
        // Authorization is enforced by the class-level [Authorize] attribute (ASP.NET middleware,
        // not exercisable via direct controller instantiation). Assert the contract is present.
        var attr = typeof(FinanceController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Contains(RoleConstants.Finance, attr!.Roles ?? string.Empty);
    }

    // ── v2.242.0 — advance-pending Return to Purchasing ─────────────────────────────────────────

    private sealed record AdvSeed(Guid RequestId, Guid GroupId, Guid ActorId, Guid? SiblingGroupId);

    private static async Task<AdvSeed> SeedAdvanceAsync(
        ApplicationDbContext ctx,
        string groupStatus,
        (string status, decimal? actualPaid, bool scheduled)[] advancePayments,
        bool addSibling = false,
        bool addRegisterPoHistory = false,
        bool addScheduleAttachment = false)
    {
        var now = DateTime.UtcNow;
        var actor = new User { Id = Guid.NewGuid(), FullName = "Fin", Email = $"f-{Guid.NewGuid()}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        SeedStatuses(ctx);
        await ctx.SaveChangesAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-20/08/2026-275", Title = "ZZTEST Adv",
            RequestTypeId = 2, StatusId = StatusId(ctx, RequestConstants.Statuses.AdvancePaymentRequired),
            RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1, CreatedAtUtc = now
        };
        ctx.Requests.Add(request);
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "KRONES ANGOLA", CurrencyCode = "AOA",
            TotalAmount = 4616502.39m, Status = groupStatus, PurchaseOrderNumber = "ECF10 2026/283",
            PaymentConditionCode = "ADVANCE_FULL", AdvancePaymentPercent = 100m, CreatedAtUtc = now, CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);
        int seq = 1;
        foreach (var ap in advancePayments)
            ctx.RequestPayments.Add(new RequestPayment
            {
                RequestId = request.Id, RequestPoGroupId = group.Id, PaymentType = RequestPayment.PaymentTypes.Advance,
                PaymentSequence = seq++, PlannedPercent = 100m, PlannedAmount = 4616502.39m, CurrencyCode = "AOA",
                PaymentStatus = ap.status, ActualPaidAmount = ap.actualPaid,
                ScheduledDateUtc = ap.scheduled ? now : (DateTime?)null, ScheduledByUserId = ap.scheduled ? actor.Id : (Guid?)null,
                CreatedByUserId = actor.Id, CreatedAtUtc = now
            });
        Guid? sibling = null;
        if (addSibling)
        {
            var sib = new RequestPoGroup
            {
                Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "CIVIPARTS", CurrencyCode = "AOA",
                TotalAmount = 477000m, Status = RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
                PurchaseOrderNumber = "ECF10 2026/125", PaymentConditionCode = "ADVANCE_FULL", AdvancePaymentPercent = 100m,
                CreatedAtUtc = now, CreatedByUserId = actor.Id
            };
            ctx.RequestPoGroups.Add(sib);
            ctx.RequestPayments.Add(new RequestPayment
            {
                RequestId = request.Id, RequestPoGroupId = sib.Id, PaymentType = RequestPayment.PaymentTypes.Advance,
                PaymentSequence = 99, PlannedPercent = 100m, PlannedAmount = 477000m, CurrencyCode = "AOA",
                PaymentStatus = RequestPayment.PaymentStatuses.Planned, CreatedByUserId = actor.Id, CreatedAtUtc = now
            });
            sibling = sib.Id;
        }
        if (addRegisterPoHistory)
            ctx.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = request.Id, ActorUserId = actor.Id, ActionTaken = "REGISTER_PO",
                PreviousStatusId = request.StatusId, NewStatusId = request.StatusId,
                Comment = "Divergência OCR confirmada pelo comprador. Justificativa: Avançar.", CreatedAtUtc = now.AddDays(-1)
            });
        if (addScheduleAttachment)
            ctx.RequestAttachments.Add(new RequestAttachment
            {
                Id = Guid.NewGuid(), RequestId = request.Id, RequestPoGroupId = group.Id, FileName = "cronograma.pdf",
                FileExtension = ".pdf", AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SCHEDULE,
                StorageReference = "zz/sched-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
                UploadedByUserId = actor.Id, UploadedAtUtc = now
            });
        await ctx.SaveChangesAsync();
        return new AdvSeed(request.Id, group.Id, actor.Id, sibling);
    }

    private static string GroupStatus(ApplicationDbContext ctx, Guid gid) =>
        ctx.RequestPoGroups.AsNoTracking().Single(g => g.Id == gid).Status;

    [Fact]
    public async Task Return_AdvancePaymentRequired_Planned_Succeeds_And_Cancels_Advance()
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
            new[] { (RequestPayment.PaymentStatuses.Planned, (decimal?)null, false) }, addRegisterPoHistory: true);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto { RequestPoGroupId = seed.GroupId, Notes = ValidReason });

        Assert.IsType<OkResult>(result);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, GroupStatus(ctx, seed.GroupId));
        var adv = await ctx.RequestPayments.AsNoTracking().FirstAsync(p => p.RequestPoGroupId == seed.GroupId);
        Assert.Equal(RequestPayment.PaymentStatuses.Cancelled, adv.PaymentStatus);
        // Both audit rows appended; original REGISTER_PO preserved.
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "FINANCE_RETURN_ADJUSTMENT"));
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "ADVANCE_PAYMENT_CANCELLED_FOR_PO_CORRECTION"));
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "REGISTER_PO"));
    }

    [Fact]
    public async Task Return_AdvancePaymentScheduled_Cancels_Payment_ClearsSchedule_VoidsAttachment()
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, RequestConstants.PoGroupStatuses.AdvancePaymentScheduled,
            new[] { (RequestPayment.PaymentStatuses.Scheduled, (decimal?)null, true) }, addScheduleAttachment: true);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto { RequestPoGroupId = seed.GroupId, Notes = ValidReason });

        Assert.IsType<OkResult>(result);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, GroupStatus(ctx, seed.GroupId));
        var adv = await ctx.RequestPayments.AsNoTracking().FirstAsync(p => p.RequestPoGroupId == seed.GroupId);
        Assert.Equal(RequestPayment.PaymentStatuses.Cancelled, adv.PaymentStatus);
        Assert.Null(adv.ScheduledDateUtc);
        Assert.Null(adv.ScheduledByUserId);
        var att = await ctx.RequestAttachments.AsNoTracking().FirstAsync(a => a.RequestPoGroupId == seed.GroupId && a.AttachmentTypeCode == RequestAttachment.TYPE_PAYMENT_SCHEDULE);
        Assert.NotNull(att.VoidedAtUtc);
    }

    [Theory]
    [InlineData("COMPLETED", 100.0)]   // completed + paid
    [InlineData("COMPLETED", null)]    // completed, null paid → still blocked
    [InlineData("COMPLETED", 0.0)]     // completed, zero paid → still blocked
    [InlineData("PLANNED", 100.0)]     // anomalous: planned but paid amount present → blocked
    public async Task Return_Refused_When_Advance_Executed(string status, double? actualPaid)
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
            new[] { (status, (decimal?)(actualPaid.HasValue ? (decimal)actualPaid.Value : (decimal?)null), false) });
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto { RequestPoGroupId = seed.GroupId, Notes = ValidReason });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(RequestConstants.PoGroupStatuses.AdvancePaymentRequired, GroupStatus(ctx, seed.GroupId)); // untouched
        Assert.Equal(0, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "FINANCE_RETURN_ADJUSTMENT"));
    }

    [Fact]
    public async Task Return_Cancels_All_Active_Advances()
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
            new[]
            {
                (RequestPayment.PaymentStatuses.Planned, (decimal?)null, false),
                (RequestPayment.PaymentStatuses.Scheduled, (decimal?)null, true),
            });
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto { RequestPoGroupId = seed.GroupId, Notes = ValidReason });

        Assert.IsType<OkResult>(result);
        var advances = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestPoGroupId == seed.GroupId).ToListAsync();
        Assert.All(advances, a => Assert.Equal(RequestPayment.PaymentStatuses.Cancelled, a.PaymentStatus));
    }

    [Fact]
    public async Task Return_MultiGroup_Advance_LeavesSibling_And_ScalarPartiallyUploaded()
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
            new[] { (RequestPayment.PaymentStatuses.Planned, (decimal?)null, false) }, addSibling: true);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto { RequestPoGroupId = seed.GroupId, Notes = ValidReason });

        Assert.IsType<OkResult>(result);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, GroupStatus(ctx, seed.GroupId));
        // Sibling CIVIPARTS untouched (still advance-pending, its advance still PLANNED).
        Assert.Equal(RequestConstants.PoGroupStatuses.AdvancePaymentRequired, GroupStatus(ctx, seed.SiblingGroupId!.Value));
        var sibAdv = await ctx.RequestPayments.AsNoTracking().FirstAsync(p => p.RequestPoGroupId == seed.SiblingGroupId!.Value);
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, sibAdv.PaymentStatus);
        // Aggregate request scalar for {WAITING_PO_CORRECTION + ADVANCE_PAYMENT_REQUIRED} = PO_PARTIALLY_UPLOADED.
        var scalar = ctx.RequestStatuses.AsNoTracking().Single(s => s.Id == ctx.Requests.AsNoTracking().Single(r => r.Id == seed.RequestId).StatusId).Code;
        Assert.Equal(RequestConstants.Statuses.PoPartiallyUploaded, scalar);
    }

    [Fact]
    public async Task Return_DoubleReturn_Refused()
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
            new[] { (RequestPayment.PaymentStatuses.Planned, (decimal?)null, false) });
        var controller = BuildController(ctx, seed.ActorId);

        var first = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto { RequestPoGroupId = seed.GroupId, Notes = ValidReason });
        Assert.IsType<OkResult>(first);
        var second = await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto { RequestPoGroupId = seed.GroupId, Notes = ValidReason });
        Assert.IsType<BadRequestObjectResult>(second); // now WAITING_PO_CORRECTION — not returnable
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "FINANCE_RETURN_ADJUSTMENT"));
    }

    [Fact]
    public async Task Return_Advance_EmitsStageTransition_ToPoCorrection()
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
            new[] { (RequestPayment.PaymentStatuses.Planned, (decimal?)null, false) });
        var controller = BuildController(ctx, seed.ActorId);

        await controller.ReturnForAdjustment(seed.RequestId, new FinanceActionRequestDto { RequestPoGroupId = seed.GroupId, Notes = ValidReason });

        var stage = await ctx.Set<OperationalStageState>().AsNoTracking().FirstOrDefaultAsync(s => s.EntityId == seed.GroupId);
        Assert.NotNull(stage);
        Assert.Equal("PO_CORRECTION", stage!.StageCode);
        Assert.True(await ctx.Set<OperationalStageTransition>().AnyAsync(t => t.EntityId == seed.GroupId && t.ToStageCode == "PO_CORRECTION"));
    }
}
