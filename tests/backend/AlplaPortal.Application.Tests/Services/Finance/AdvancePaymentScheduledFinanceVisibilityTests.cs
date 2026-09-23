using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
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
/// v2.245.11 — Finance visibility of PAYMENT-type requests at ADVANCE_PAYMENT_SCHEDULED.
///
/// Root cause: the parent-scalar Finance population (obligations projection, legacy /payments, /summary)
/// never listed ADVANCE_PAYMENT_SCHEDULED, and FinancePaymentEligibilityService offered no PAY for a
/// PAYMENT request at that parent status. A PAYMENT-type advance therefore disappeared from Finance the
/// moment it was scheduled (the QUOTATION population is group-status-driven and was never affected).
///
/// InMemory-EF direct-controller pattern (FinanceObligationsEndpointTests / ConfirmAdvancePaymentTests).
/// </summary>
public class AdvancePaymentScheduledFinanceVisibilityTests
{
    private const int PaymentTypeId = 1;
    private const int QuotationTypeId = 2;

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ClaimsPrincipal Principal(Guid actorId, string role) =>
        new(new ClaimsIdentity(new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, role)
        }, "Test"));

    private static FinanceController BuildFinanceController(ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.SystemAdministrator)
    {
        var controller = new FinanceController(
            ctx,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance),
            new FinancePaymentEligibilityService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = Principal(actorId, role) }
        };
        return controller;
    }

    /// <summary>Same wiring as ConfirmAdvancePaymentTests — the advance endpoints resolve the aggregator from RequestServices.</summary>
    private static RequestsController BuildRequestsController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
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
            Microsoft.Extensions.Options.Options.Create(new AlplaPortal.Domain.Configuration.PostPaymentCompletionOptions()));

        var services = new ServiceCollection();
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = Principal(actorId, RoleConstants.Finance),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    // ── Seed ──────────────────────────────────────────────────────────────────────────────────

    private sealed class Seed
    {
        public Guid ActorId;
        public Dictionary<string, int> StatusIds = new();
    }

    private static async Task<Seed> SeedBaseAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"fin-{Guid.NewGuid()}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.AddRange(
            new RequestType { Id = PaymentTypeId, Code = RequestConstants.Types.Payment, Name = "Pagamento" },
            new RequestType { Id = QuotationTypeId, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.Departments.Add(new Department { Id = 1, Name = "Compras" });
        ctx.Plants.AddRange(
            new Plant { Id = 1, Name = "Planta 1", CompanyId = 1 },
            new Plant { Id = 2, Name = "Planta 2", CompanyId = 1 });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        ctx.Suppliers.Add(new Supplier { Id = 1, Name = "FORNECEDOR A" });

        var seed = new Seed { ActorId = actor.Id };
        var codes = new[]
        {
            RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentScheduled,
            RequestConstants.Statuses.AdvancePaymentCompleted, RequestConstants.Statuses.WaitingSupplierDelivery,
            RequestConstants.Statuses.Cancelled, RequestConstants.Statuses.Rejected, RequestConstants.Statuses.Completed
        };
        var id = 10;
        foreach (var code in codes)
        {
            id++;
            ctx.RequestStatuses.Add(new RequestStatus { Id = id, Code = code, Name = code, DisplayOrder = id });
            seed.StatusIds[code] = id;
        }
        await ctx.SaveChangesAsync();
        return seed;
    }

    private sealed record GroupSpec(string Status, decimal Total, DateTime? ScheduledDate = null, string Supplier = "FORNECEDOR A", decimal? AdvancePercent = null);

    private static async Task<(Request request, List<RequestPoGroup> groups)> AddRequestAsync(
        ApplicationDbContext ctx, Seed seed, string number, int typeId, string parentStatus,
        IEnumerable<GroupSpec> groupSpecs, int plantId = 1, DateTime? createdAt = null, bool withPoAttachment = true)
    {
        var specs = groupSpecs.ToList();
        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = number, Title = $"Pedido {number}",
            RequestTypeId = typeId, StatusId = seed.StatusIds[parentStatus], RequesterId = seed.ActorId,
            DepartmentId = 1, CompanyId = 1, PlantId = plantId, CurrencyId = 1, SupplierId = 1,
            EstimatedTotalAmount = specs.Sum(s => s.Total),
            CreatedAtUtc = createdAt ?? DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        if (withPoAttachment)
        {
            ctx.RequestAttachments.Add(new RequestAttachment
            {
                Id = Guid.NewGuid(), RequestId = request.Id, FileName = "po.pdf", FileExtension = ".pdf",
                AttachmentTypeCode = AttachmentConstants.Types.PurchaseOrder, IsDeleted = false
            });
        }

        var groups = new List<RequestPoGroup>();
        var sequence = 0;
        foreach (var spec in specs)
        {
            var group = new RequestPoGroup
            {
                Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = 1, SupplierNameSnapshot = spec.Supplier,
                CurrencyCode = "AOA", TotalAmount = spec.Total, Status = spec.Status,
                PurchaseOrderNumber = "ECF11 2026/520", AdvancePaymentPercent = spec.AdvancePercent,
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = seed.ActorId
            };
            ctx.RequestPoGroups.Add(group);
            groups.Add(group);

            var advanceAmount = spec.AdvancePercent.HasValue ? Math.Round(spec.Total * spec.AdvancePercent.Value / 100m, 2) : spec.Total;
            switch (spec.Status)
            {
                case RequestConstants.Statuses.AdvancePaymentRequired:
                    ctx.RequestPayments.Add(new RequestPayment
                    {
                        RequestId = request.Id, RequestPoGroupId = group.Id, PaymentType = RequestPayment.PaymentTypes.Advance,
                        PaymentSequence = ++sequence, PlannedAmount = advanceAmount, CurrencyCode = "AOA",
                        PaymentStatus = RequestPayment.PaymentStatuses.Planned, CreatedByUserId = seed.ActorId, CreatedAtUtc = DateTime.UtcNow
                    });
                    break;
                case RequestConstants.Statuses.AdvancePaymentScheduled:
                    ctx.RequestPayments.Add(new RequestPayment
                    {
                        RequestId = request.Id, RequestPoGroupId = group.Id, PaymentType = RequestPayment.PaymentTypes.Advance,
                        PaymentSequence = ++sequence, PlannedAmount = advanceAmount, CurrencyCode = "AOA",
                        ScheduledDateUtc = spec.ScheduledDate ?? Today.AddDays(3), ScheduledByUserId = seed.ActorId,
                        PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, CreatedByUserId = seed.ActorId, CreatedAtUtc = DateTime.UtcNow
                    });
                    break;
                case RequestConstants.Statuses.WaitingSupplierDelivery:
                case RequestConstants.Statuses.AdvancePaymentCompleted:
                    ctx.RequestPayments.Add(new RequestPayment
                    {
                        RequestId = request.Id, RequestPoGroupId = group.Id, PaymentType = RequestPayment.PaymentTypes.Advance,
                        PaymentSequence = ++sequence, PlannedAmount = advanceAmount, ActualPaidAmount = advanceAmount, CurrencyCode = "AOA",
                        ScheduledDateUtc = Today.AddDays(-2), PaidDateUtc = Today.AddDays(-1), PaidByUserId = seed.ActorId,
                        PaymentStatus = RequestPayment.PaymentStatuses.Completed, CreatedByUserId = seed.ActorId, CreatedAtUtc = DateTime.UtcNow
                    });
                    break;
                case RequestConstants.Statuses.PaymentScheduled:
                    ctx.RequestPayments.Add(new RequestPayment
                    {
                        RequestId = request.Id, RequestPoGroupId = group.Id, PaymentType = RequestPayment.PaymentTypes.FinalBalance,
                        PaymentSequence = ++sequence, PlannedAmount = spec.Total, CurrencyCode = "AOA",
                        ScheduledDateUtc = spec.ScheduledDate ?? Today.AddDays(3), ScheduledByUserId = seed.ActorId,
                        PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, CreatedByUserId = seed.ActorId, CreatedAtUtc = DateTime.UtcNow
                    });
                    break;
            }
        }

        await ctx.SaveChangesAsync();
        return (request, groups);
    }

    private static FinanceObligationsResponseDto Obligations(ActionResult<FinanceObligationsResponseDto> r) =>
        Assert.IsType<FinanceObligationsResponseDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

    private static FinanceListResponseDto Payments(ActionResult<FinanceListResponseDto> r) =>
        Assert.IsType<FinanceListResponseDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

    private static FinanceSummaryDto Summary(ActionResult<FinanceSummaryDto> r) =>
        Assert.IsType<FinanceSummaryDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

    private static void AssertPayableAdvance(FinanceObligationDto o, Guid groupId)
    {
        Assert.Equal(groupId, o.RequestPoGroupId);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentScheduled, o.GroupStatusCode);
        Assert.Equal(FinanceActionClasses.NeedsPayment, o.ActionClass);
        Assert.Contains("PAY", o.FinanceActions);
        Assert.DoesNotContain("SCHEDULE", o.FinanceActions);
        Assert.Contains("CANCEL_SCHEDULE", o.FinanceActions);
        Assert.Equal(RequestPayment.PaymentTypes.Advance, o.PaymentType);
        Assert.Equal(FinanceResponsibleRoles.Finance, o.ResponsibleRole);
    }

    // ── Population: single group ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PaymentType_AdvanceScheduled_SingleGroup_AppearsExactlyOnce_WithPayNotSchedule()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        // REQ-16/09/2026-424 shape: PAYMENT, one group, 100% advance scheduled.
        var (request, groups) = await AddRequestAsync(ctx, seed, "REQ-16/09/2026-424", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 1_000_000m, Today.AddDays(5), AdvancePercent: 100m) });

        var body = Obligations(await BuildFinanceController(ctx, seed.ActorId).GetObligations());

        var container = Assert.Single(body.PagedResult.Items);
        Assert.Equal(request.Id, container.RequestId);
        Assert.Equal(1, body.PagedResult.TotalCount);
        var o = Assert.Single(container.Obligations);
        AssertPayableAdvance(o, groups[0].Id);
        Assert.Equal(Today.AddDays(5), o.DueDate);
        Assert.Equal(1_000_000m, o.ObligationAmount);
        Assert.False(o.IsOverdue);
        Assert.False(o.IsDueToday);
        Assert.Equal("Efetuar adiantamento", o.NextActionLabel);
        // Aggregation: exactly one payable obligation, counted once.
        Assert.Equal(1, body.Summary.NeedsPayment.Count);
        Assert.Equal(0, body.Summary.NeedsScheduling.Count);
        Assert.Equal(1, body.Summary.ActionableTotal);
        Assert.Equal(1_000_000m, Assert.Single(body.Summary.ActionableAmountsByCurrency).Amount);
    }

    [Fact]
    public async Task PaymentType_AdvanceRequired_ExposesSchedule_BeforeScheduling()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (_, groups) = await AddRequestAsync(ctx, seed, "REQ-A", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentRequired,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentRequired, 500m, AdvancePercent: 50m) });

        var body = Obligations(await BuildFinanceController(ctx, seed.ActorId).GetObligations());

        var o = Assert.Single(Assert.Single(body.PagedResult.Items).Obligations);
        Assert.Equal(groups[0].Id, o.RequestPoGroupId);
        Assert.Equal(FinanceActionClasses.NeedsScheduling, o.ActionClass);
        Assert.Contains("SCHEDULE", o.FinanceActions);
        Assert.DoesNotContain("PAY", o.FinanceActions);
        Assert.Null(o.DueDate);
        Assert.Equal("Agendar adiantamento", o.NextActionLabel);
    }

    [Theory]
    [InlineData(5, false, false)]   // future
    [InlineData(0, false, true)]    // due today
    [InlineData(-4, true, false)]   // overdue
    public async Task PaymentType_AdvanceScheduled_FutureDueTodayOverdue_AllActionable(int dayOffset, bool overdue, bool dueToday)
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (_, groups) = await AddRequestAsync(ctx, seed, "REQ-B", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 800m, Today.AddDays(dayOffset), AdvancePercent: 100m) });
        var controller = BuildFinanceController(ctx, seed.ActorId);

        var body = Obligations(await controller.GetObligations(actionableOnly: true));

        var o = Assert.Single(Assert.Single(body.PagedResult.Items).Obligations);
        AssertPayableAdvance(o, groups[0].Id);
        Assert.Equal(overdue, o.IsOverdue);
        Assert.Equal(dueToday, o.IsDueToday);
        Assert.Equal(overdue ? -dayOffset : 0, o.OverdueDays);
        Assert.Equal(overdue ? 1 : 0, body.Summary.Overdue.Count);
        Assert.Equal(dueToday ? 1 : 0, body.Summary.DueToday.Count);
        Assert.Equal(1, body.Summary.NeedsPayment.Count);

        // The card filters reach the same row.
        if (overdue) Assert.Single(Obligations(await controller.GetObligations(overdueOnly: true)).PagedResult.Items);
        if (dueToday) Assert.Single(Obligations(await controller.GetObligations(dueTodayOnly: true)).PagedResult.Items);
        Assert.Single(Obligations(await controller.GetObligations(actionClass: FinanceActionClasses.NeedsPayment)).PagedResult.Items);
    }

    // ── Population: multi-group ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PaymentType_TwoAdvanceScheduledGroups_TwoObligations_NoDuplicates()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        // REQ-27/08/2026-333 shape: two supplier groups, each with its own scheduled advance.
        var (request, groups) = await AddRequestAsync(ctx, seed, "REQ-27/08/2026-333", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            new[]
            {
                new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 300m, Today.AddDays(2), "FORNECEDOR A", 50m),
                new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 700m, Today.AddDays(9), "FORNECEDOR B", 30m)
            });

        var body = Obligations(await BuildFinanceController(ctx, seed.ActorId).GetObligations());

        var container = Assert.Single(body.PagedResult.Items);
        Assert.Equal(request.Id, container.RequestId);
        Assert.True(container.ExpandByDefault);
        Assert.Equal(2, container.Obligations.Count);
        Assert.Equal(2, container.Obligations.Select(o => o.RequestPoGroupId).Distinct().Count());
        foreach (var g in groups)
            AssertPayableAdvance(Assert.Single(container.Obligations, o => o.RequestPoGroupId == g.Id), g.Id);
        // Each obligation carries ITS OWN advance amount and due date (group-specific, never merged).
        Assert.Equal(150m, container.Obligations.Single(o => o.RequestPoGroupId == groups[0].Id).ObligationAmount);
        Assert.Equal(210m, container.Obligations.Single(o => o.RequestPoGroupId == groups[1].Id).ObligationAmount);
        Assert.Equal(Today.AddDays(2), container.Obligations.Single(o => o.RequestPoGroupId == groups[0].Id).DueDate);
        Assert.Equal(Today.AddDays(9), container.Obligations.Single(o => o.RequestPoGroupId == groups[1].Id).DueDate);
        Assert.Equal(2, body.Summary.NeedsPayment.Count);
        Assert.Equal(2, body.Summary.ActionableTotal);
        Assert.Equal(360m, Assert.Single(body.Summary.NeedsPayment.AmountsByCurrency).Amount);
    }

    [Fact]
    public async Task PaymentType_MixedGroups_AdvanceScheduledAndPoIssued_OneObligationEach_CorrectActions()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        // Parent follows the furthest-behind group (ADVANCE_PAYMENT_SCHEDULED, priority 25 < PO_ISSUED 30).
        var (_, groups) = await AddRequestAsync(ctx, seed, "REQ-C", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            new[]
            {
                new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 400m, Today.AddDays(1), "FORNECEDOR A", 100m),
                new GroupSpec(RequestConstants.Statuses.PoIssued, 600m, null, "FORNECEDOR B")
            });

        var body = Obligations(await BuildFinanceController(ctx, seed.ActorId).GetObligations());

        var container = Assert.Single(body.PagedResult.Items);
        Assert.Equal(2, container.Obligations.Count);
        AssertPayableAdvance(container.Obligations.Single(o => o.RequestPoGroupId == groups[0].Id), groups[0].Id);
        var poIssued = container.Obligations.Single(o => o.RequestPoGroupId == groups[1].Id);
        Assert.Equal(FinanceActionClasses.NeedsScheduling, poIssued.ActionClass);
        Assert.Contains("SCHEDULE", poIssued.FinanceActions);
        Assert.DoesNotContain("CANCEL_SCHEDULE", poIssued.FinanceActions);
        Assert.Equal(1, body.Summary.NeedsPayment.Count);
        Assert.Equal(1, body.Summary.NeedsScheduling.Count);
        Assert.Equal(2, body.Summary.ActionableTotal);
    }

    // ── Preserved behaviour ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QuotationType_AdvanceScheduledGroup_Unchanged_StillOnceWithPay()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (request, groups) = await AddRequestAsync(ctx, seed, "REQ-Q", QuotationTypeId,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 900m, Today.AddDays(4), AdvancePercent: 40m) },
            withPoAttachment: false); // QUOTATION population is group-driven, no PO attachment needed

        var body = Obligations(await BuildFinanceController(ctx, seed.ActorId).GetObligations());

        var container = Assert.Single(body.PagedResult.Items);
        Assert.Equal(request.Id, container.RequestId);
        var o = Assert.Single(container.Obligations);
        AssertPayableAdvance(o, groups[0].Id);
        Assert.Equal(RequestConstants.Types.Quotation, o.RequestTypeCode);
        Assert.Equal(1, body.PagedResult.TotalCount); // matched by ONE predicate — never listed twice
    }

    [Fact]
    public async Task PaymentType_ConfirmedAdvance_NotAPendingObligation()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        // Parent still at ADVANCE_PAYMENT_COMPLETED (historical rows) with the group already past the advance.
        var (_, groups) = await AddRequestAsync(ctx, seed, "REQ-D", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentCompleted,
            new[] { new GroupSpec(RequestConstants.Statuses.WaitingSupplierDelivery, 500m, AdvancePercent: 100m) });
        // Parent already synced to WAITING_SUPPLIER_DELIVERY (current b2p behaviour) — outside the PAYMENT Finance population.
        await AddRequestAsync(ctx, seed, "REQ-E", PaymentTypeId,
            RequestConstants.Statuses.WaitingSupplierDelivery,
            new[] { new GroupSpec(RequestConstants.Statuses.WaitingSupplierDelivery, 500m, AdvancePercent: 100m) });

        var body = Obligations(await BuildFinanceController(ctx, seed.ActorId).GetObligations());

        var container = Assert.Single(body.PagedResult.Items);
        Assert.Equal("REQ-D", container.RequestNumber);
        var o = Assert.Single(container.Obligations);
        Assert.Equal(groups[0].Id, o.RequestPoGroupId);
        Assert.Equal(FinanceActionClasses.PaidWaitingReceiving, o.ActionClass);
        Assert.DoesNotContain("PAY", o.FinanceActions);
        Assert.DoesNotContain("SCHEDULE", o.FinanceActions);
        Assert.Equal(0, body.Summary.NeedsPayment.Count);
        Assert.Equal(0, body.Summary.ActionableTotal);
        Assert.Empty(Obligations(await BuildFinanceController(ctx, seed.ActorId).GetObligations(actionableOnly: true)).PagedResult.Items);
    }

    [Fact]
    public async Task CancelledRejectedCompleted_Requests_NeverBecomeAdvanceObligations()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var advance = new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 100m, Today.AddDays(1), AdvancePercent: 100m) };
        await AddRequestAsync(ctx, seed, "REQ-CANCELLED", PaymentTypeId, RequestConstants.Statuses.Cancelled, advance);
        await AddRequestAsync(ctx, seed, "REQ-REJECTED", PaymentTypeId, RequestConstants.Statuses.Rejected, advance);
        await AddRequestAsync(ctx, seed, "REQ-COMPLETED", PaymentTypeId, RequestConstants.Statuses.Completed,
            new[] { new GroupSpec(RequestConstants.Statuses.Completed, 100m) });
        var (live, _) = await AddRequestAsync(ctx, seed, "REQ-LIVE", PaymentTypeId, RequestConstants.Statuses.AdvancePaymentScheduled, advance);
        var controller = BuildFinanceController(ctx, seed.ActorId);

        var body = Obligations(await controller.GetObligations());

        Assert.DoesNotContain(body.PagedResult.Items, c => c.RequestNumber is "REQ-CANCELLED" or "REQ-REJECTED");
        Assert.Equal(1, body.Summary.NeedsPayment.Count);
        Assert.Equal(1, body.Summary.ActionableTotal);
        var actionable = Obligations(await controller.GetObligations(actionableOnly: true));
        Assert.Equal(live.Id, Assert.Single(actionable.PagedResult.Items).RequestId);

        var payments = Payments(await controller.GetPayments());
        Assert.DoesNotContain(payments.PagedResult.Items, i => i.RequestNumber is "REQ-CANCELLED" or "REQ-REJECTED");
    }

    // ── Alignment: legacy /payments and /summary ───────────────────────────────────────────────

    [Fact]
    public async Task LegacyPayments_And_Obligations_Aligned_ForAdvanceScheduled()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (request, _) = await AddRequestAsync(ctx, seed, "REQ-F", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 250m, Today.AddDays(2), AdvancePercent: 100m) });
        var controller = BuildFinanceController(ctx, seed.ActorId);

        var payments = Payments(await controller.GetPayments());
        var scheduled = Payments(await controller.GetPayments(filter: "scheduled"));
        var obligations = Obligations(await controller.GetObligations());

        Assert.Equal(1, payments.PagedResult.TotalCount);
        Assert.Equal(request.Id, Assert.Single(payments.PagedResult.Items).Id);
        Assert.Equal(1, scheduled.PagedResult.TotalCount);
        Assert.Equal(1, obligations.PagedResult.TotalCount);
        Assert.Equal(request.Id, Assert.Single(obligations.PagedResult.Items).RequestId);
    }

    [Fact]
    public async Task Summary_CountsPaymentTypeAdvances_RequiredAsWaiting_ScheduledAsScheduled()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        await AddRequestAsync(ctx, seed, "REQ-G1", PaymentTypeId, RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 1_000m, Today.AddDays(2), AdvancePercent: 100m) });
        await AddRequestAsync(ctx, seed, "REQ-G2", PaymentTypeId, RequestConstants.Statuses.AdvancePaymentRequired,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentRequired, 2_000m, AdvancePercent: 50m) });
        await AddRequestAsync(ctx, seed, "REQ-G3", PaymentTypeId, RequestConstants.Statuses.PaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.PaymentScheduled, 4_000m, Today.AddDays(2)) });
        var controller = BuildFinanceController(ctx, seed.ActorId);

        var summary = Summary(await controller.GetSummary());

        Assert.Equal(1, summary.WaitingFinanceAction);               // ADVANCE_PAYMENT_REQUIRED
        Assert.Equal(2, summary.ScheduledPayments);                  // PAYMENT_SCHEDULED + ADVANCE_PAYMENT_SCHEDULED
        Assert.Equal(0, summary.CompletedThisMonth);
        var pending = Assert.Single(summary.PendingValues);
        Assert.Equal(7_000m, pending.TotalAmount);                    // all three still pending
        var scheduledValue = Assert.Single(summary.ScheduledValues);
        Assert.Equal(5_000m, scheduledValue.TotalAmount);

        // The list agrees with the KPI: filter=scheduled returns the same two requests.
        Assert.Equal(2, Payments(await controller.GetPayments(filter: "scheduled")).PagedResult.TotalCount);
        Assert.Equal(1, Payments(await controller.GetPayments(filter: "action")).PagedResult.TotalCount);
    }

    // ── Pagination and scope ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Obligations_Pagination_Boundary_Stable_NoOverlap()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var (r, _) = await AddRequestAsync(ctx, seed, $"REQ-P{i}", PaymentTypeId, RequestConstants.Statuses.AdvancePaymentScheduled,
                new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 100m + i, Today.AddDays(1), AdvancePercent: 100m) },
                createdAt: new DateTime(2026, 9, 1 + i, 0, 0, 0, DateTimeKind.Utc));
            ids.Add(r.Id);
        }
        var controller = BuildFinanceController(ctx, seed.ActorId);

        var page1 = Obligations(await controller.GetObligations(page: 1, pageSize: 2));
        var page2 = Obligations(await controller.GetObligations(page: 2, pageSize: 2));
        var page3 = Obligations(await controller.GetObligations(page: 3, pageSize: 2));

        Assert.Equal(3, page1.PagedResult.TotalCount);
        Assert.Equal(2, page1.PagedResult.Items.Count());
        Assert.Single(page2.PagedResult.Items);
        Assert.Empty(page3.PagedResult.Items);
        var seen = page1.PagedResult.Items.Concat(page2.PagedResult.Items).Select(c => c.RequestId).ToList();
        Assert.Equal(3, seen.Distinct().Count());
        Assert.Equal(ids.OrderBy(x => x).ToList(), seen.OrderBy(x => x).ToList());
        // Default order is newest first — deterministic across pages.
        Assert.Equal("REQ-P2", page1.PagedResult.Items.First().RequestNumber);
        Assert.Equal("REQ-P0", page2.PagedResult.Items.Single().RequestNumber);
        // Summary counts the whole population regardless of the page.
        Assert.Equal(3, page2.Summary.NeedsPayment.Count);
    }

    [Fact]
    public async Task Obligations_OrgScope_PlantScopedFinanceUser_And_PlantFilter()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (inPlant1, _) = await AddRequestAsync(ctx, seed, "REQ-S1", PaymentTypeId, RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 100m, Today.AddDays(1), AdvancePercent: 100m) }, plantId: 1);
        var (inPlant2, _) = await AddRequestAsync(ctx, seed, "REQ-S2", PaymentTypeId, RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 100m, Today.AddDays(1), AdvancePercent: 100m) }, plantId: 2);
        var financeUser = new User { Id = Guid.NewGuid(), FullName = "Finance Plant 1", Email = $"fin1-{Guid.NewGuid()}@t.local" };
        ctx.Users.Add(financeUser);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = financeUser.Id, PlantId = 1 });
        await ctx.SaveChangesAsync();

        // Plant-scoped Finance user sees only plant 1.
        var scoped = Obligations(await BuildFinanceController(ctx, financeUser.Id, RoleConstants.Finance).GetObligations());
        Assert.Equal(inPlant1.Id, Assert.Single(scoped.PagedResult.Items).RequestId);
        Assert.Equal(1, scoped.Summary.NeedsPayment.Count);

        // SysAdmin sees both; the explicit plant filter narrows to plant 2.
        var admin = BuildFinanceController(ctx, seed.ActorId);
        Assert.Equal(2, Obligations(await admin.GetObligations()).PagedResult.TotalCount);
        var filtered = Obligations(await admin.GetObligations(plantId: 2));
        Assert.Equal(inPlant2.Id, Assert.Single(filtered.PagedResult.Items).RequestId);
        Assert.Equal(1, filtered.Summary.NeedsPayment.Count);

        // Legacy /payments honours the same scope.
        var scopedPayments = Payments(await BuildFinanceController(ctx, financeUser.Id, RoleConstants.Finance).GetPayments());
        Assert.Equal(inPlant1.Id, Assert.Single(scopedPayments.PagedResult.Items).Id);
    }

    // ── Execution routing invariant: ADVANCE → b2p/confirm-advance only; STANDARD → MarkAsPaid only ──

    private static async Task<RequestAttachment> AddProofAsync(ApplicationDbContext ctx, Guid requestId)
    {
        var proof = new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = requestId, FileName = "comprovativo.pdf", FileExtension = ".pdf",
            AttachmentTypeCode = AttachmentConstants.Types.PaymentProof, IsDeleted = false
        };
        ctx.RequestAttachments.Add(proof);
        await ctx.SaveChangesAsync();
        return proof;
    }

    private sealed record Snapshot(string GroupStatus, int RequestStatusId, DateTime? RequestPaidAt, decimal? RequestPaidAmount,
        int PaymentCount, string PaymentStatus, decimal? ActualPaid, DateTime? PaidDate, Guid? Proof, int HistoryCount, Guid? AttachmentGroup);

    private static async Task<Snapshot> SnapshotAsync(ApplicationDbContext ctx, Guid requestId, Guid groupId, Guid attachmentId)
    {
        ctx.ChangeTracker.Clear();
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == groupId);
        var r = await ctx.Requests.AsNoTracking().SingleAsync(x => x.Id == requestId);
        var payments = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestId == requestId).ToListAsync();
        var p = payments.Single(x => x.RequestPoGroupId == groupId);
        var a = await ctx.RequestAttachments.AsNoTracking().SingleAsync(x => x.Id == attachmentId);
        return new Snapshot(g.Status, r.StatusId, r.ActualPaidAtUtc, r.ActualPaidAmount, payments.Count, p.PaymentStatus,
            p.ActualPaidAmount, p.PaidDateUtc, p.PaymentProofAttachmentId,
            await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == requestId), a.RequestPoGroupId);
    }

    [Fact]
    public async Task Obligation_PaymentFlow_IsAdvanceForAdvanceGroup_StandardForNormalPayment()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (_, adv) = await AddRequestAsync(ctx, seed, "REQ-FLOW-A", PaymentTypeId, RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 100m, Today.AddDays(1), AdvancePercent: 100m) });
        var (_, req) = await AddRequestAsync(ctx, seed, "REQ-FLOW-R", PaymentTypeId, RequestConstants.Statuses.AdvancePaymentRequired,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentRequired, 100m, AdvancePercent: 50m) });
        var (_, std) = await AddRequestAsync(ctx, seed, "REQ-FLOW-S", PaymentTypeId, RequestConstants.Statuses.PaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.PaymentScheduled, 100m, Today.AddDays(1)) });
        var (_, po) = await AddRequestAsync(ctx, seed, "REQ-FLOW-P", PaymentTypeId, RequestConstants.Statuses.PoIssued,
            new[] { new GroupSpec(RequestConstants.Statuses.PoIssued, 100m) });

        var all = Obligations(await BuildFinanceController(ctx, seed.ActorId).GetObligations()).PagedResult.Items
            .SelectMany(c => c.Obligations).ToDictionary(o => o.RequestPoGroupId);

        Assert.Equal(FinancePaymentFlows.Advance, all[adv[0].Id].PaymentFlow);
        Assert.Equal(FinancePaymentFlows.Advance, all[req[0].Id].PaymentFlow);
        Assert.Equal(FinancePaymentFlows.Standard, all[std[0].Id].PaymentFlow);
        Assert.Equal(FinancePaymentFlows.Standard, all[po[0].Id].PaymentFlow);
        // The flow is carried next to the actions the client renders — never inferred from labels.
        Assert.Contains("PAY", all[adv[0].Id].FinanceActions);
        Assert.Contains("PAY", all[std[0].Id].FinanceActions);
    }

    [Fact]
    public async Task MarkAsPaid_DirectCall_OnPaymentTypeScheduledAdvance_Rejected409_NothingWritten_ThenConfirmAdvanceSucceeds()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (request, groups) = await AddRequestAsync(ctx, seed, "REQ-16/09/2026-424", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 1_000m, Today.AddDays(2), AdvancePercent: 100m) });
        var group = groups[0];
        var proof = await AddProofAsync(ctx, request.Id);
        var before = await SnapshotAsync(ctx, request.Id, group.Id, proof.Id);
        var finance = BuildFinanceController(ctx, seed.ActorId);

        // Visible and payable (PAY) — the Finance row the UI renders.
        var o = Assert.Single(Assert.Single(Obligations(await finance.GetObligations()).PagedResult.Items).Obligations);
        AssertPayableAdvance(o, group.Id);
        Assert.Equal(FinancePaymentFlows.Advance, o.PaymentFlow);

        // A direct API call to the NORMAL payment endpoint for the same advance is refused deterministically.
        var result = await finance.MarkAsPaid(request.Id, new ConfirmPaymentDto
        {
            RequestPoGroupId = group.Id, PaymentProofAttachmentId = proof.Id, ActualPaidAmount = 1_000m, PaidDate = Today, Comment = "direct"
        });
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Adiantamento Não Liquidável Por Esta Ação", problem.Title);
        Assert.Equal(FinanceController.AdvanceRequiresConfirmAdvanceCode, problem.Extensions["code"]);
        Assert.Equal(FinancePaymentFlows.Advance, problem.Extensions["paymentFlow"]);
        Assert.Equal(group.Id, problem.Extensions["requestPoGroupId"]);
        Assert.Contains("b2p/confirm-advance", problem.Detail);

        // No writes: group, request, payment row, proof attachment, history, paid timestamp all untouched.
        var after = await SnapshotAsync(ctx, request.Id, group.Id, proof.Id);
        Assert.Equal(before, after);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentScheduled, after.GroupStatus);
        Assert.Equal(RequestPayment.PaymentStatuses.Scheduled, after.PaymentStatus);
        Assert.Null(after.ActualPaid);
        Assert.Null(after.PaidDate);
        Assert.Null(after.Proof);
        Assert.Null(after.RequestPaidAt);
        Assert.Equal(1, after.PaymentCount);   // no FINAL_BALANCE row fabricated
        Assert.Equal(0, after.HistoryCount);
        Assert.Null(after.AttachmentGroup);    // proof not linked to the group
        Assert.DoesNotContain(await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestId == request.Id).ToListAsync(),
            p => p.PaymentType == RequestPayment.PaymentTypes.FinalBalance);

        // The same advance completes through the dedicated flow.
        var requests = BuildRequestsController(ctx, seed.ActorId);
        Assert.IsType<OkObjectResult>(await requests.ConfirmAdvancePayment(request.Id, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = group.Id, PaymentProofAttachmentId = proof.Id, ActualPaidAmount = 1_000m, PaidDate = Today
        }));
        var done = await SnapshotAsync(ctx, request.Id, group.Id, proof.Id);
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, done.GroupStatus);
        Assert.Equal(seed.StatusIds[RequestConstants.Statuses.WaitingSupplierDelivery], done.RequestStatusId);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, done.PaymentStatus);
        Assert.Equal(1_000m, done.ActualPaid);
        Assert.Equal(proof.Id, done.Proof);
        Assert.Equal(1, done.PaymentCount);
        Assert.Equal(group.Id, done.AttachmentGroup);
    }

    [Fact]
    public async Task MarkAsPaid_DirectCall_OnQuotationAdvanceGroup_Rejected409_NothingWritten()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        // The invariant holds for every request type: a QUOTATION advance group (direct-pay path) is refused too.
        var (request, groups) = await AddRequestAsync(ctx, seed, "REQ-Q-ADV", QuotationTypeId,
            RequestConstants.Statuses.AdvancePaymentRequired,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentRequired, 275_139m, AdvancePercent: 30m) }, withPoAttachment: false);
        var proof = await AddProofAsync(ctx, request.Id);
        var before = await SnapshotAsync(ctx, request.Id, groups[0].Id, proof.Id);

        var result = await BuildFinanceController(ctx, seed.ActorId).MarkAsPaid(request.Id, new ConfirmPaymentDto
        {
            RequestPoGroupId = groups[0].Id, PaymentProofAttachmentId = proof.Id, ActualPaidAmount = 275_139m, PaidDate = Today
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(FinanceController.AdvanceRequiresConfirmAdvanceCode, problem.Extensions["code"]);
        Assert.Equal(before, await SnapshotAsync(ctx, request.Id, groups[0].Id, proof.Id));
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, before.GroupStatus);
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, before.PaymentStatus);
    }

    [Fact]
    public async Task MarkAsPaid_NormalScheduledPayment_StillSucceeds_And_ConfirmAdvanceRefusesIt()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (request, groups) = await AddRequestAsync(ctx, seed, "REQ-STD", PaymentTypeId,
            RequestConstants.Statuses.PaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.PaymentScheduled, 500m, Today.AddDays(1)) });
        var group = groups[0];
        var proof = await AddProofAsync(ctx, request.Id);
        var before = await SnapshotAsync(ctx, request.Id, group.Id, proof.Id);
        Assert.Equal(RequestPayment.PaymentStatuses.Scheduled, before.PaymentStatus);

        // A normal payment cannot be submitted to the advance endpoint — refused, nothing written.
        var requests = BuildRequestsController(ctx, seed.ActorId);
        var refused = await requests.ConfirmAdvancePayment(request.Id, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = group.Id, PaymentProofAttachmentId = proof.Id, ActualPaidAmount = 500m, PaidDate = Today
        });
        var bad = Assert.IsType<BadRequestObjectResult>(refused);
        Assert.Equal("Ação Inválida", Assert.IsType<ProblemDetails>(bad.Value).Title);
        Assert.Equal(before, await SnapshotAsync(ctx, request.Id, group.Id, proof.Id));

        // The STANDARD flow (MarkAsPaid) still settles it.
        var finance = BuildFinanceController(ctx, seed.ActorId);
        var ok = await finance.MarkAsPaid(request.Id, new ConfirmPaymentDto
        {
            RequestPoGroupId = group.Id, PaymentProofAttachmentId = proof.Id, ActualPaidAmount = 500m, PaidDate = Today, Comment = "ok"
        });
        Assert.IsType<OkResult>(ok);

        var after = await SnapshotAsync(ctx, request.Id, group.Id, proof.Id);
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, after.GroupStatus);
        Assert.Equal(seed.StatusIds[RequestConstants.Statuses.PaymentCompleted], after.RequestStatusId);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, after.PaymentStatus);
        Assert.Equal(500m, after.ActualPaid);
        Assert.Equal(Today, after.PaidDate);
        Assert.Equal(proof.Id, after.Proof);
        Assert.Equal(1, after.PaymentCount);   // the scheduled FINAL_BALANCE row was completed, none fabricated
        Assert.Equal(Today, after.RequestPaidAt);
        Assert.Equal(group.Id, after.AttachmentGroup);
        var history = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == request.Id).ToListAsync();
        Assert.Equal("PAYMENT_COMPLETED", Assert.Single(history).ActionTaken);
        // Gone from the pending Finance population (paid → receiving), never listed as payable again.
        var obligation = Assert.Single(Assert.Single(Obligations(await finance.GetObligations()).PagedResult.Items).Obligations);
        Assert.Equal(FinanceActionClasses.PaidWaitingReceiving, obligation.ActionClass);
        Assert.DoesNotContain("PAY", obligation.FinanceActions);
    }

    [Fact]
    public async Task ConfirmAdvance_Repeated_FailsDeterministically_NoSecondWrite_AggregationIntact()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (request, groups) = await AddRequestAsync(ctx, seed, "REQ-IDEMP", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentScheduled, 1_000m, Today.AddDays(2), AdvancePercent: 100m) });
        var group = groups[0];
        var proof = await AddProofAsync(ctx, request.Id);
        var requests = BuildRequestsController(ctx, seed.ActorId);
        var dto = new ConfirmAdvancePaymentDto { RequestPoGroupId = group.Id, PaymentProofAttachmentId = proof.Id, ActualPaidAmount = 1_000m, PaidDate = Today };

        Assert.IsType<OkObjectResult>(await requests.ConfirmAdvancePayment(request.Id, dto));
        var first = await SnapshotAsync(ctx, request.Id, group.Id, proof.Id);

        // Documented contract: once confirmed the group is no longer in an advance status → 400, nothing written.
        var again = await requests.ConfirmAdvancePayment(request.Id, dto);
        var bad = Assert.IsType<BadRequestObjectResult>(again);
        Assert.Equal("Ação Inválida", Assert.IsType<ProblemDetails>(bad.Value).Title);
        var second = await SnapshotAsync(ctx, request.Id, group.Id, proof.Id);
        Assert.Equal(first, second);
        Assert.Equal(1, second.PaymentCount);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, second.PaymentStatus);
        var history = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == request.Id).ToListAsync();
        Assert.Single(history, h => h.ActionTaken == "ADVANCE_PAYMENT_COMPLETED");

        // Aggregation: group and request both at WAITING_SUPPLIER_DELIVERY; MarkAsPaid on the delivered group is
        // no longer an advance case, but the request status forbids it — still nothing written.
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, second.GroupStatus);
        Assert.Equal(seed.StatusIds[RequestConstants.Statuses.WaitingSupplierDelivery], second.RequestStatusId);
        var late = await BuildFinanceController(ctx, seed.ActorId).MarkAsPaid(request.Id, new ConfirmPaymentDto
        {
            RequestPoGroupId = group.Id, PaymentProofAttachmentId = proof.Id, ActualPaidAmount = 1_000m, PaidDate = Today
        });
        Assert.IsType<BadRequestObjectResult>(late);
        Assert.Equal(second, await SnapshotAsync(ctx, request.Id, group.Id, proof.Id));
    }

    // ── Full lifecycle: ADVANCE_PAYMENT_REQUIRED → schedule → ADVANCE_PAYMENT_SCHEDULED → confirm ──

    [Fact]
    public async Task Lifecycle_PaymentType_ScheduleAdvance_ThenVisibleAndPayable_ThenConfirm_ThenNotOutstanding()
    {
        var ctx = NewContext();
        var seed = await SeedBaseAsync(ctx);
        var (request, groups) = await AddRequestAsync(ctx, seed, "REQ-LC", PaymentTypeId,
            RequestConstants.Statuses.AdvancePaymentRequired,
            new[] { new GroupSpec(RequestConstants.Statuses.AdvancePaymentRequired, 1_000m, AdvancePercent: 100m) });
        var group = groups[0];
        var proof = new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = request.Id, FileName = "comprovativo.pdf", FileExtension = ".pdf",
            AttachmentTypeCode = AttachmentConstants.Types.PaymentProof, IsDeleted = false
        };
        ctx.RequestAttachments.Add(proof);
        await ctx.SaveChangesAsync();

        var finance = BuildFinanceController(ctx, seed.ActorId);
        var requests = BuildRequestsController(ctx, seed.ActorId);

        // 1. Before scheduling: Finance sees the advance and offers SCHEDULE.
        var before = Obligations(await finance.GetObligations());
        var o0 = Assert.Single(Assert.Single(before.PagedResult.Items).Obligations);
        Assert.Contains("SCHEDULE", o0.FinanceActions);
        Assert.DoesNotContain("PAY", o0.FinanceActions);

        // 2. Schedule the advance (b2p/schedule-advance) — the group AND the parent move to ADVANCE_PAYMENT_SCHEDULED.
        var scheduledDate = Today.AddDays(3);
        Assert.IsType<OkObjectResult>(await requests.ScheduleAdvancePayment(request.Id,
            new ScheduleAdvancePaymentDto { RequestPoGroupId = group.Id, ScheduledDate = scheduledDate, Comment = "Agendado (teste)" }));
        ctx.ChangeTracker.Clear();
        var afterSchedule = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == request.Id);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentScheduled, afterSchedule.Status!.Code);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentScheduled, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id)).Status);

        // 3. Now visible exactly once, payable (PAY, not SCHEDULE), due on the scheduled date — the v2.245.11 fix.
        var during = Obligations(await finance.GetObligations());
        var container = Assert.Single(during.PagedResult.Items);
        Assert.Equal(request.Id, container.RequestId);
        var o1 = Assert.Single(container.Obligations);
        AssertPayableAdvance(o1, group.Id);
        Assert.Equal(FinancePaymentFlows.Advance, o1.PaymentFlow); // the client routes PAY to b2p/confirm-advance
        Assert.Equal(scheduledDate, o1.DueDate);
        Assert.Equal(1_000m, o1.ObligationAmount);
        Assert.Equal(1, during.Summary.NeedsPayment.Count);
        Assert.Equal(1, during.Summary.ActionableTotal);
        Assert.Equal(1, Payments(await finance.GetPayments(filter: "scheduled")).PagedResult.TotalCount);
        Assert.Equal(1, Summary(await finance.GetSummary()).ScheduledPayments);
        // Not prematurely paid/received.
        Assert.Equal(RequestPayment.PaymentStatuses.Scheduled,
            (await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == group.Id)).PaymentStatus);

        // 4. Confirm the advance through the existing b2p/confirm-advance flow (the PAY action's target).
        var paidDate = Today;
        Assert.IsType<OkObjectResult>(await requests.ConfirmAdvancePayment(request.Id, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = group.Id, PaymentProofAttachmentId = proof.Id, ActualPaidAmount = 1_000m, PaidDate = paidDate, Comment = "Pago (teste)"
        }));
        ctx.ChangeTracker.Clear();

        // 5. Exactly ONE payment row, ONE transition to COMPLETED, no FINAL_BALANCE fabricated.
        var payments = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestId == request.Id).ToListAsync();
        var payment = Assert.Single(payments);
        Assert.Equal(RequestPayment.PaymentTypes.Advance, payment.PaymentType);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, payment.PaymentStatus);
        Assert.Equal(1_000m, payment.ActualPaidAmount);
        Assert.Equal(paidDate, payment.PaidDateUtc);
        Assert.Equal(proof.Id, payment.PaymentProofAttachmentId);
        Assert.False(payment.HasDivergence);

        // 6. Group handed to supplier delivery; parent follows via the canonical aggregator. No premature receiving/completion.
        var finalGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id);
        var finalRequest = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == request.Id);
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, finalGroup.Status);
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, finalRequest.Status!.Code);
        Assert.NotEqual(RequestConstants.Statuses.PaymentCompleted, finalGroup.Status);
        Assert.NotEqual(RequestConstants.Statuses.Completed, finalRequest.Status.Code);

        // 7. History/audit: SCHEDULE_ADVANCE then ADVANCE_PAYMENT_COMPLETED (each exactly once), plus the
        //    aggregator's own STATUS_SYNC rows for the two parent transitions.
        var history = await ctx.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == request.Id).OrderBy(h => h.CreatedAtUtc).ToListAsync();
        var actions = history.Where(h => h.ActionTaken != "STATUS_SYNC").Select(h => h.ActionTaken).ToList();
        Assert.Equal(new[] { "SCHEDULE_ADVANCE", "ADVANCE_PAYMENT_COMPLETED" }, actions);
        Assert.Equal(2, history.Count(h => h.ActionTaken == "STATUS_SYNC"));
        Assert.Contains("FORNECEDOR A", history.Single(h => h.ActionTaken == "ADVANCE_PAYMENT_COMPLETED").Comment);

        // 8. No longer an outstanding Finance obligation anywhere (obligations, legacy list, KPIs).
        var after = Obligations(await finance.GetObligations());
        Assert.DoesNotContain(after.PagedResult.Items, c => c.RequestId == request.Id);
        Assert.Equal(0, after.Summary.NeedsPayment.Count);
        Assert.Equal(0, after.Summary.ActionableTotal);
        Assert.Equal(0, Payments(await finance.GetPayments()).PagedResult.TotalCount);
        var summary = Summary(await finance.GetSummary());
        Assert.Equal(0, summary.ScheduledPayments);
        Assert.Equal(0, summary.WaitingFinanceAction);
    }
}
