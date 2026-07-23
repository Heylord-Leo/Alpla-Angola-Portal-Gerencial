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
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Covers FinanceController.CancelSchedule end-to-end: the correction/reversal workflow for a
/// payment schedule created in error (e.g. attached to the wrong RequestPoGroup). Normal
/// (FINAL_BALANCE) and advance (ADVANCE) cancellations deliberately diverge in how they mutate
/// the underlying RequestPayment row — see the endpoint's own comments — so both are covered
/// independently, plus the shared idempotency/conflict/attachment-voiding/history rules.
/// Same InMemory-EF direct-controller-instantiation pattern as FinanceMarkAsPaidTransitionTests.
/// </summary>
public class FinanceCancelScheduleTests
{
    private const string ValidReason = "Agendamento enviado para o pedido errado por engano.";

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

    /// <summary>
    /// RequestsController.ScheduleAdvancePayment lives outside FinanceController — built the same
    /// way ConfirmAdvancePaymentTests does, since that method resolves IStatusAggregationService via
    /// HttpContext.RequestServices.GetRequiredService rather than constructor injection.
    /// </summary>
    private static RequestsController BuildRequestsController(ApplicationDbContext ctx, Guid actorId)
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
            new Mock<IQuotationItemEligibilityService>().Object);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.Finance)
        };

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

    private sealed record NormalSeed(Guid RequestId, Guid GroupId, int PaymentId, Guid ActorId, DateTime ScheduledDateUtc);

    /// <summary>PAYMENT type, single group at PAYMENT_SCHEDULED, one Scheduled FINAL_BALANCE RequestPayment, one PAYMENT_SCHEDULE attachment.</summary>
    private static async Task<NormalSeed> SeedNormalScheduledAsync(ApplicationDbContext ctx, bool withAttachment = true)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"finance-{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);

        var poIssued = new RequestStatus { Id = 1, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 30 };
        var paymentScheduled = new RequestStatus { Id = 2, Code = RequestConstants.Statuses.PaymentScheduled, Name = "Pagamento Agendado", DisplayOrder = 50 };
        var paymentCompleted = new RequestStatus { Id = 3, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Concluído", DisplayOrder = 90 };
        ctx.RequestStatuses.AddRange(poIssued, paymentScheduled, paymentCompleted);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "REQ-23/07/2026-001",
            Title = "ZZTEST Normal Scheduled",
            RequestTypeId = requestType.Id,
            StatusId = paymentScheduled.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var scheduledDate = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "Fornecedor Teste",
            CurrencyCode = "AOA",
            TotalAmount = 1000m,
            Status = RequestConstants.Statuses.PaymentScheduled,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        var payment = new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = group.Id,
            PaymentType = RequestPayment.PaymentTypes.FinalBalance,
            PaymentSequence = 1,
            PlannedAmount = 1000m,
            CurrencyCode = "AOA",
            PaymentStatus = RequestPayment.PaymentStatuses.Scheduled,
            ScheduledDateUtc = scheduledDate,
            ScheduledByUserId = actor.Id,
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.RequestPayments.Add(payment);

        if (withAttachment)
        {
            ctx.RequestAttachments.Add(new RequestAttachment
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                RequestPoGroupId = group.Id,
                FileName = "cronograma.pdf",
                FileExtension = ".pdf",
                AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SCHEDULE,
                UploadedByUserId = actor.Id,
                UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
                IsDeleted = false
            });
        }

        await ctx.SaveChangesAsync();
        return new NormalSeed(request.Id, group.Id, payment.Id, actor.Id, scheduledDate);
    }

    /// <summary>
    /// Reproduces the confirmed browser-evidence bug on REQ-13/07/2026-054 (HOTEL STATION): a
    /// PAYMENT-type group whose own SupplierNameSnapshot/CurrencyCode are null (legacy, never
    /// actively synced), even though the parent Request always knows its own Supplier/Currency.
    /// When knownSupplierAndCurrency is false, the request itself also has no Supplier/Currency —
    /// the truly-unknown-everywhere case that must still safely fall back to "---".
    /// </summary>
    private static async Task<NormalSeed> SeedLegacyNullGroupSnapshotAsync(ApplicationDbContext ctx, bool knownSupplierAndCurrency)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"finance-{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);

        var poIssued = new RequestStatus { Id = 1, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 30 };
        var paymentScheduled = new RequestStatus { Id = 2, Code = RequestConstants.Statuses.PaymentScheduled, Name = "Pagamento Agendado", DisplayOrder = 50 };
        ctx.RequestStatuses.AddRange(poIssued, paymentScheduled);

        Supplier? supplier = null;
        Currency? currency = null;
        if (knownSupplierAndCurrency)
        {
            supplier = new Supplier { Id = 1, Name = "HOTEL STATION", IsActive = true };
            currency = new Currency { Id = 1, Code = "AOA", Symbol = "Kz", IsActive = true };
            ctx.Suppliers.Add(supplier);
            ctx.Currencies.Add(currency);
        }

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "REQ-13/07/2026-054",
            Title = "ZZTEST Legacy Null Group Snapshot",
            RequestTypeId = requestType.Id,
            StatusId = poIssued.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            SupplierId = supplier?.Id,
            CurrencyId = currency?.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = null, // the confirmed legacy-null scenario
            CurrencyCode = null,
            TotalAmount = 1953000.00m,
            Status = RequestConstants.Statuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        var payment = new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = group.Id,
            PaymentType = RequestPayment.PaymentTypes.FinalBalance,
            PaymentSequence = 1,
            PlannedAmount = 1953000.00m,
            CurrencyCode = "AOA",
            PaymentStatus = RequestPayment.PaymentStatuses.Planned,
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.RequestPayments.Add(payment);

        await ctx.SaveChangesAsync();
        return new NormalSeed(request.Id, group.Id, payment.Id, actor.Id, DateTime.UtcNow);
    }

    private sealed record AdvanceSeed(Guid RequestId, Guid AdvanceGroupId, Guid SiblingGroupId, int PaymentId, Guid ActorId, decimal PlannedAmount, decimal PlannedPercent, DateTime ScheduledDateUtc);

    /// <summary>QUOTATION type, two groups: one ADVANCE_PAYMENT_SCHEDULED (target), one PO_ISSUED sibling — proves sibling isolation.</summary>
    private static async Task<AdvanceSeed> SeedAdvanceScheduledAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"finance-{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 2, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        ctx.RequestTypes.Add(requestType);

        var advanceScheduled = new RequestStatus { Id = 10, Code = RequestConstants.Statuses.AdvancePaymentScheduled, Name = "Adiantamento Agendado", DisplayOrder = 25 };
        var poIssued = new RequestStatus { Id = 11, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 13 };
        ctx.RequestStatuses.AddRange(advanceScheduled, poIssued);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "REQ-23/07/2026-002",
            Title = "ZZTEST Advance Scheduled Multi-group",
            RequestTypeId = requestType.Id,
            StatusId = advanceScheduled.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var advanceGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "Fornecedor Adiantamento",
            CurrencyCode = "AOA",
            TotalAmount = 275139.00m,
            AdvancePaymentPercent = 30m,
            Status = RequestConstants.Statuses.AdvancePaymentScheduled,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = actor.Id
        };
        var siblingGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "Fornecedor Sibling",
            CurrencyCode = "AOA",
            TotalAmount = 5000m,
            Status = RequestConstants.Statuses.PoIssued,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.AddRange(advanceGroup, siblingGroup);

        var scheduledDate = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);
        var plannedAmount = 82541.70m; // 30% of 275,139.00
        var payment = new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = advanceGroup.Id,
            PaymentType = RequestPayment.PaymentTypes.Advance,
            PaymentSequence = 1,
            PlannedPercent = 30m,
            PlannedAmount = plannedAmount,
            CurrencyCode = "AOA",
            PaymentStatus = RequestPayment.PaymentStatuses.Scheduled,
            ScheduledDateUtc = scheduledDate,
            ScheduledByUserId = actor.Id,
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.RequestPayments.Add(payment);

        await ctx.SaveChangesAsync();
        return new AdvanceSeed(request.Id, advanceGroup.Id, siblingGroup.Id, payment.Id, actor.Id, plannedAmount, 30m, scheduledDate);
    }

    // ── 1/3/7: normal cancellation ──

    [Fact]
    public async Task CancelSchedule_Normal_CancelsToPoIssued_PaymentCancelled_HistoryCapturesPreviousDate()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });

        Assert.IsType<OkResult>(result);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.Statuses.PoIssued, group.Status);

        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.Id == seed.PaymentId);
        Assert.Equal(RequestPayment.PaymentStatuses.Cancelled, payment.PaymentStatus);
        Assert.Equal(1, payment.PaymentSequence); // preserved
        Assert.Equal(seed.ScheduledDateUtc, payment.ScheduledDateUtc); // preserved for audit (normal path keeps it)

        var history = await ctx.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == seed.RequestId && h.ActionTaken == "PAYMENT_SCHEDULE_CANCELLED")
            .ToListAsync();
        var record = Assert.Single(history);
        Assert.Contains("30/07/2026", record.Comment); // previous scheduled date captured before clearing
        Assert.Contains(ValidReason, record.Comment);

        // Original scheduling history event type is untouched by this test (none was written by this
        // seed, which constructs the RequestPayment/group directly) — the cancellation event alone
        // must exist exactly once, proven above via Assert.Single.
    }

    [Fact]
    public async Task CancelSchedule_Normal_SchedulingAgainCreatesNextSequence_NoCollision()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx, withAttachment: false);
        var controller = BuildController(ctx, seed.ActorId);

        var cancelResult = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });
        Assert.IsType<OkResult>(cancelResult);

        var rescheduleResult = await controller.SchedulePayment(seed.RequestId, new SchedulePaymentDto
        {
            RequestPoGroupId = seed.GroupId,
            ScheduledDate = DateTime.UtcNow.AddDays(10)
        });
        Assert.IsType<OkResult>(rescheduleResult);

        var payments = await ctx.RequestPayments.AsNoTracking()
            .Where(p => p.RequestPoGroupId == seed.GroupId && p.PaymentType == RequestPayment.PaymentTypes.FinalBalance)
            .OrderBy(p => p.PaymentSequence)
            .ToListAsync();
        Assert.Equal(2, payments.Count);
        Assert.Equal(1, payments[0].PaymentSequence);
        Assert.Equal(RequestPayment.PaymentStatuses.Cancelled, payments[0].PaymentStatus);
        Assert.Equal(2, payments[1].PaymentSequence);
        Assert.Equal(RequestPayment.PaymentStatuses.Scheduled, payments[1].PaymentStatus);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.Statuses.PaymentScheduled, group.Status);
    }

    // ── 2/4/5/6/7: advance cancellation ──

    [Fact]
    public async Task CancelSchedule_Advance_CancelsToAdvancePaymentRequired_PaymentReturnsToPlanned_AmountsPreserved()
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.AdvanceGroupId, Reason = ValidReason });

        Assert.IsType<OkResult>(result);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.AdvanceGroupId);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, group.Status);

        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.Id == seed.PaymentId);
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, payment.PaymentStatus); // NOT Cancelled
        Assert.Null(payment.ScheduledDateUtc); // cleared
        Assert.Equal(1, payment.PaymentSequence); // still the same row — no new row created
        Assert.Equal(seed.PlannedAmount, payment.PlannedAmount);
        Assert.Equal(seed.PlannedPercent, payment.PlannedPercent);

        var siblingGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.SiblingGroupId);
        Assert.Equal(RequestConstants.Statuses.PoIssued, siblingGroup.Status); // untouched

        var history = await ctx.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == seed.RequestId && h.ActionTaken == "ADVANCE_PAYMENT_SCHEDULE_CANCELLED")
            .ToListAsync();
        var record = Assert.Single(history);
        Assert.Contains("05/08/2026", record.Comment); // previous scheduled date captured before clearing
        Assert.Contains("Agendamento de adiantamento cancelado.", record.Comment);
    }

    [Fact]
    public async Task CancelSchedule_Advance_CanBeScheduledAgain_ReusingSamePlannedRow()
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var cancelResult = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.AdvanceGroupId, Reason = ValidReason });
        Assert.IsType<OkResult>(cancelResult);

        var requestsController = BuildRequestsController(ctx, seed.ActorId);
        var newScheduledDate = DateTime.UtcNow.AddDays(20);
        var rescheduleResult = await requestsController.ScheduleAdvancePayment(seed.RequestId, new ScheduleAdvancePaymentDto
        {
            RequestPoGroupId = seed.AdvanceGroupId,
            ScheduledDate = newScheduledDate,
            Comment = "Reagendado via teste"
        });

        Assert.IsType<OkObjectResult>(rescheduleResult);

        var paymentsForGroup = await ctx.RequestPayments.AsNoTracking()
            .Where(p => p.RequestPoGroupId == seed.AdvanceGroupId && p.PaymentType == RequestPayment.PaymentTypes.Advance)
            .ToListAsync();
        var payment = Assert.Single(paymentsForGroup); // same row reused — never a second row
        Assert.Equal(RequestPayment.PaymentStatuses.Scheduled, payment.PaymentStatus);
        Assert.Equal(newScheduledDate, payment.ScheduledDateUtc);
        Assert.Equal(seed.PlannedAmount, payment.PlannedAmount); // still preserved

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.AdvanceGroupId);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentScheduled, group.Status);
    }

    // ── Attachment voiding ──

    [Fact]
    public async Task CancelSchedule_VoidsMostRecentAttachment_OlderOnesUntouched_LogsWarningPathCovered()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx, withAttachment: false);

        var olderAttachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            RequestPoGroupId = seed.GroupId,
            FileName = "cronograma-antigo.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SCHEDULE,
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-5),
            IsDeleted = false
        };
        var newerAttachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            RequestPoGroupId = seed.GroupId,
            FileName = "cronograma-recente.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SCHEDULE,
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            IsDeleted = false
        };
        ctx.RequestAttachments.AddRange(olderAttachment, newerAttachment);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });
        Assert.IsType<OkResult>(result);

        var refreshedNewer = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == newerAttachment.Id);
        var refreshedOlder = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == olderAttachment.Id);

        Assert.NotNull(refreshedNewer.VoidedAtUtc);
        Assert.Equal(ValidReason, refreshedNewer.VoidReason);
        Assert.Equal(seed.ActorId, refreshedNewer.VoidedByUserId);

        Assert.Null(refreshedOlder.VoidedAtUtc); // untouched
        Assert.False(refreshedOlder.IsDeleted); // still returned by normal queries
    }

    [Fact]
    public async Task CancelSchedule_NoAttachmentExists_StillSucceeds()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx, withAttachment: false);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task CancelSchedule_WrongGroupAttachment_NeverVoided()
    {
        var ctx = NewContext();
        var seed = await SeedAdvanceScheduledAsync(ctx);

        // A PAYMENT_SCHEDULE attachment on the SIBLING group (not the one being cancelled).
        var siblingAttachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            RequestPoGroupId = seed.SiblingGroupId,
            FileName = "cronograma-sibling.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SCHEDULE,
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            IsDeleted = false
        };
        ctx.RequestAttachments.Add(siblingAttachment);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.AdvanceGroupId, Reason = ValidReason });
        Assert.IsType<OkResult>(result);

        var refreshed = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == siblingAttachment.Id);
        Assert.Null(refreshed.VoidedAtUtc);
    }

    // ── Idempotency / conflict ──

    [Fact]
    public async Task CancelSchedule_SecondAttempt_Rejected_NoDuplicateHistory()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var first = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });
        Assert.IsType<OkResult>(first);

        var second = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });
        Assert.IsType<BadRequestObjectResult>(second);

        var historyCount = await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "PAYMENT_SCHEDULE_CANCELLED");
        Assert.Equal(1, historyCount);
    }

    [Fact]
    public async Task CancelSchedule_GroupAlreadyPaymentCompleted_Rejected_NoMutation()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        group.Status = RequestConstants.Statuses.PaymentCompleted;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });

        Assert.IsType<BadRequestObjectResult>(result);

        var refreshedGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, refreshedGroup.Status); // unchanged

        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.Id == seed.PaymentId);
        Assert.Equal(RequestPayment.PaymentStatuses.Scheduled, payment.PaymentStatus); // unchanged

        var historyCount = await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId);
        Assert.Equal(0, historyCount);
    }

    [Theory]
    [InlineData(RequestConstants.Statuses.WaitingReceipt)]
    [InlineData(RequestConstants.Statuses.Completed)]
    [InlineData(RequestConstants.Statuses.AdvancePaymentCompleted)]
    public async Task CancelSchedule_GroupInNonScheduledTerminalState_Rejected(string groupStatus)
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        group.Status = groupStatus;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CancelSchedule_MissingScheduledPaymentRow_ReturnsConflict_NotGeneric500()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        // Simulate the data-inconsistency scenario: group says scheduled, but the payment row
        // itself is not in Scheduled state (e.g. lost/never-created).
        var payment = await ctx.RequestPayments.SingleAsync(p => p.Id == seed.PaymentId);
        payment.PaymentStatus = RequestPayment.PaymentStatuses.Planned;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
    }

    [Fact]
    public async Task CancelSchedule_WrongRequestAssociation_NotFound()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.CancelSchedule(Guid.NewGuid(), new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CancelSchedule_WrongGroupForRequest_BadRequest_NoMutation()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = Guid.NewGuid(), Reason = ValidReason });

        Assert.IsType<BadRequestObjectResult>(result);
        var historyCount = await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == seed.RequestId);
        Assert.Equal(0, historyCount);
    }

    // ── Reason validation (after Trim) ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("motivo curto")] // 12 chars — too short
    public async Task CancelSchedule_ReasonValidation_BoundaryTheory(string? reason)
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = reason! });

        var trimmedLength = (reason ?? string.Empty).Trim().Length;
        if (trimmedLength < 20)
        {
            Assert.IsType<BadRequestObjectResult>(result);
        }
        else
        {
            Assert.IsType<OkResult>(result);
        }
    }

    [Fact]
    public async Task CancelSchedule_ReasonExactly20TrimmedChars_Accepted()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);
        var reason = "  12345678901234567890  "; // 20 chars once trimmed

        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = reason });

        Assert.IsType<OkResult>(result);
        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == seed.RequestId);
        Assert.Contains("12345678901234567890", history.Comment); // stored trimmed
        Assert.DoesNotContain("  12345678901234567890  ", history.Comment);
    }

    [Fact]
    public async Task CancelSchedule_ReasonWith19TrimmedChars_Rejected()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = "1234567890123456789" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── Legacy self-healed PAYMENT group ──

    [Fact]
    public async Task CancelSchedule_LegacySelfHealedPaymentGroup_CancelsSafely()
    {
        var ctx = NewContext();
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = "legacy@test.local" };
        ctx.Users.Add(actor);
        var requestType = new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);
        var poIssued = new RequestStatus { Id = 1, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 30 };
        ctx.RequestStatuses.Add(poIssued);
        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "REQ-23/07/2026-003",
            Title = "ZZTEST Legacy Self-heal",
            RequestTypeId = requestType.Id,
            StatusId = poIssued.Id,
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
            TotalAmount = 500m,
            Status = RequestConstants.PoGroupStatuses.Pending, // legacy, never actively synced
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);

        // Step 1: self-heals via the existing SchedulePayment legacy-fallback (DEC-149 pattern).
        var scheduleResult = await controller.SchedulePayment(request.Id, new SchedulePaymentDto
        {
            RequestPoGroupId = group.Id,
            ScheduledDate = DateTime.UtcNow.AddDays(5)
        });
        Assert.IsType<OkResult>(scheduleResult);

        var healedGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id);
        Assert.Equal(RequestConstants.Statuses.PaymentScheduled, healedGroup.Status);

        // Step 2: cancel — must succeed and land on PO_ISSUED (never back to the legacy PENDING value).
        var cancelResult = await controller.CancelSchedule(request.Id, new CancelSchedulePaymentDto { RequestPoGroupId = group.Id, Reason = ValidReason });
        Assert.IsType<OkResult>(cancelResult);

        var finalGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id);
        Assert.Equal(RequestConstants.Statuses.PoIssued, finalGroup.Status);
        Assert.NotEqual(RequestConstants.PoGroupStatuses.Pending, finalGroup.Status);
    }

    // ── History/CSV surfacing ──

    [Fact]
    public async Task CancelSchedule_AppearsInGetHistory_WithCorrectActionTaken()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });

        var historyResult = await controller.GetHistory(actionType: "PAYMENT_SCHEDULE_CANCELLED");
        var ok = Assert.IsType<ActionResult<Application.DTOs.Common.PagedResult<Application.DTOs.Finance.FinanceHistoryItemDto>>>(historyResult);
        var page = Assert.IsType<Application.DTOs.Common.PagedResult<Application.DTOs.Finance.FinanceHistoryItemDto>>(Assert.IsAssignableFrom<OkObjectResult>(ok.Result).Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("PAYMENT_SCHEDULE_CANCELLED", item.ActionTaken);
    }

    // ── Supplier/currency fallback (2026-07-26 browser evidence: REQ-13/07/2026-054, HOTEL STATION) ──

    [Fact]
    public async Task SchedulePayment_LegacyGroupNullSupplierAndCurrency_HistoryUsesRequestSupplierAndCurrency()
    {
        var ctx = NewContext();
        var seed = await SeedLegacyNullGroupSnapshotAsync(ctx, knownSupplierAndCurrency: true);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.SchedulePayment(seed.RequestId, new SchedulePaymentDto
        {
            RequestPoGroupId = seed.GroupId,
            ScheduledDate = DateTime.UtcNow.AddDays(5)
        });
        Assert.IsType<OkResult>(result);

        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == seed.RequestId);
        Assert.Contains("HOTEL STATION", history.Comment);
        Assert.Contains("Moeda: AOA", history.Comment);
        Assert.DoesNotContain("[--- |", history.Comment);
        Assert.DoesNotContain("Moeda: --- ", history.Comment);
        // Batchless PAYMENT request: never invent "Lote #0" when ApprovalBatch is absent.
        Assert.DoesNotContain("Lote #0", history.Comment);
        Assert.DoesNotContain("Lote #", history.Comment);
    }

    [Fact]
    public async Task CancelSchedule_LegacyGroupNullSupplierAndCurrency_HistoryUsesRequestSupplierAndCurrency()
    {
        var ctx = NewContext();
        var seed = await SeedLegacyNullGroupSnapshotAsync(ctx, knownSupplierAndCurrency: true);
        var controller = BuildController(ctx, seed.ActorId);
        await controller.SchedulePayment(seed.RequestId, new SchedulePaymentDto { RequestPoGroupId = seed.GroupId, ScheduledDate = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc) });

        var result = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });
        Assert.IsType<OkResult>(result);

        var cancelHistory = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "PAYMENT_SCHEDULE_CANCELLED");
        Assert.Contains("HOTEL STATION", cancelHistory.Comment);
        Assert.Contains("Moeda: AOA", cancelHistory.Comment);
        Assert.DoesNotContain("[--- |", cancelHistory.Comment);
        Assert.Contains("Data anteriormente agendada: 23/07/2026.", cancelHistory.Comment);
    }

    [Fact]
    public async Task SchedulePayment_TrulyUnknownSupplierAndCurrency_HistoryStillSafelyFallsBackToDashes()
    {
        var ctx = NewContext();
        var seed = await SeedLegacyNullGroupSnapshotAsync(ctx, knownSupplierAndCurrency: false);
        var controller = BuildController(ctx, seed.ActorId);

        var result = await controller.SchedulePayment(seed.RequestId, new SchedulePaymentDto
        {
            RequestPoGroupId = seed.GroupId,
            ScheduledDate = DateTime.UtcNow.AddDays(5)
        });
        Assert.IsType<OkResult>(result);

        var history = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == seed.RequestId);
        Assert.Contains("[--- | Moeda: --- |", history.Comment);
    }

    // ── Finance History attachment void indicator (2026-07-26 browser evidence) ──

    [Fact]
    public async Task GetHistory_VoidedScheduleAttachment_DocumentoAdicionadoRow_IsMarkedVoided_WithReason()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx, withAttachment: true);
        var controller = BuildController(ctx, seed.ActorId);

        // Mirrors AttachmentsController.Upload's exact comment shape for a single-file upload.
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            ActorUserId = seed.ActorId,
            ActionTaken = "DOCUMENTO ADICIONADO",
            NewStatusId = 2,
            Comment = "Documento \"cronograma.pdf\" (Cronograma de Pagamento) adicionado ao pedido por Finance Tester.",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        await ctx.SaveChangesAsync();

        var cancelResult = await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });
        Assert.IsType<OkResult>(cancelResult);

        var historyResult = await controller.GetHistory(actionType: "DOCUMENTO ADICIONADO");
        var page = Assert.IsType<Application.DTOs.Common.PagedResult<Application.DTOs.Finance.FinanceHistoryItemDto>>(
            Assert.IsAssignableFrom<OkObjectResult>(historyResult.Result).Value);
        var item = Assert.Single(page.Items);
        Assert.True(item.IsVoided);
        Assert.Equal(ValidReason, item.VoidReason);
    }

    [Fact]
    public async Task GetHistory_NonVoidedScheduleAttachment_DocumentoAdicionadoRow_NotMarkedVoided()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx, withAttachment: true);
        var controller = BuildController(ctx, seed.ActorId);

        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            ActorUserId = seed.ActorId,
            ActionTaken = "DOCUMENTO ADICIONADO",
            NewStatusId = 2,
            Comment = "Documento \"cronograma.pdf\" (Cronograma de Pagamento) adicionado ao pedido por Finance Tester.",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        await ctx.SaveChangesAsync();

        // No cancellation happens — the attachment stays active.
        var historyResult = await controller.GetHistory(actionType: "DOCUMENTO ADICIONADO");
        var page = Assert.IsType<Application.DTOs.Common.PagedResult<Application.DTOs.Finance.FinanceHistoryItemDto>>(
            Assert.IsAssignableFrom<OkObjectResult>(historyResult.Result).Value);
        var item = Assert.Single(page.Items);
        Assert.False(item.IsVoided);
        Assert.Null(item.VoidReason);
    }

    [Fact]
    public async Task CancelSchedule_VoidedAttachment_RemainsReturnedByNormalAttachmentQueries_NeverFilteredLikeDeleted()
    {
        var ctx = NewContext();
        var seed = await SeedNormalScheduledAsync(ctx, withAttachment: true);
        var controller = BuildController(ctx, seed.ActorId);

        await controller.CancelSchedule(seed.RequestId, new CancelSchedulePaymentDto { RequestPoGroupId = seed.GroupId, Reason = ValidReason });

        // Same query shape RequestsController's attachment projection uses (Where !IsDeleted) —
        // a voided (but not deleted) attachment must still come back, i.e. remain downloadable.
        var stillReturned = await ctx.RequestAttachments.AsNoTracking()
            .Where(a => a.RequestId == seed.RequestId && !a.IsDeleted)
            .ToListAsync();
        var attachment = Assert.Single(stillReturned);
        Assert.NotNull(attachment.VoidedAtUtc);
        Assert.False(attachment.IsDeleted);
    }

    [Fact]
    public async Task LegacySchedulePayment_VoidedOnlyAttachment_DoesNotSatisfyDocumentGate()
    {
        var ctx = NewContext();
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = "legacy-gate@test.local" };
        ctx.Users.Add(actor);
        var requestType = new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);
        var poIssued = new RequestStatus { Id = 1, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 30 };
        ctx.RequestStatuses.Add(poIssued);
        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "REQ-23/07/2026-999",
            Title = "ZZTEST Legacy Gate",
            RequestTypeId = requestType.Id,
            StatusId = poIssued.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);
        // Only a VOIDED PAYMENT_SCHEDULE attachment exists — must not satisfy the "has attachment" gate.
        ctx.RequestAttachments.Add(new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "cronograma-cancelado.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SCHEDULE,
            UploadedByUserId = actor.Id,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-2),
            IsDeleted = false,
            VoidedAtUtc = DateTime.UtcNow.AddDays(-1),
            VoidedByUserId = actor.Id,
            VoidReason = ValidReason
        });
        await ctx.SaveChangesAsync();

        var requestsController = BuildRequestsController(ctx, actor.Id);
        var result = await requestsController.SchedulePayment(request.Id, new ApprovalActionDto { Comment = "tentativa" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Ação Bloqueada", problem.Title);
    }
}
