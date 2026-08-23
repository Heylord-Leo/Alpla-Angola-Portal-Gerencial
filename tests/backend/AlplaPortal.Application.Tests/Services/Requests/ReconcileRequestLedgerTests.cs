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
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
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
/// ReconcileRequest remaining-balance ledger writer. Guards two fixes:
///  • the group-less FINAL_BALANCE remaining-balance row is created with a valid CreatedByUserId
///    (authenticated actor) and a REQUEST-scoped PaymentSequence (2 when a sibling already owns 1);
///  • it is added via the DbSet, not the un-loaded request.StatusHistories navigation — so the
///    PostPaymentCompletion Phase-1 hook that runs before the single SaveChanges cannot mis-track the
///    new history row (which produced a DbUpdateConcurrencyException in SQL).
/// Runs the REAL Phase-1 completion engine with completion enabled AND disabled.
/// </summary>
public class ReconcileRequestLedgerTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static PostPaymentCompletionOptions Flags(bool enabled, bool completion) => new()
    {
        Enabled = enabled,
        CompletionEnabled = completion,
        EffectiveDateUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId, PostPaymentCompletionOptions options)
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
            Options.Create(options));

        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IStatusAggregationService>().Object);
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(
            ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new Claim(ClaimTypes.Role, RoleConstants.Finance)
                }, "Test")),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId);

    /// <summary>QUOTATION request in WAITING_RECONCILIATION, one (unclassified) group that already
    /// owns a COMPLETED FINAL_BALANCE seq1 of <paramref name="paidAmount"/>.</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, decimal paidAmount)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Finance", Email = $"fin-{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 26, Code = RequestConstants.Statuses.WaitingReconciliation, Name = "Ag. Reconciliação", DisplayOrder = 26 },
            new RequestStatus { Id = 13, Code = RequestConstants.Statuses.PaymentRequestSent, Name = "Solicitação Pagamento Enviada", DisplayOrder = 13 },
            new RequestStatus { Id = 15, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Realizado", DisplayOrder = 15 });
        await ctx.SaveChangesAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = $"ZZTEST-RECON-{Guid.NewGuid().ToString()[..8]}",
            Title = "ZZTEST Reconcile Ledger",
            RequestTypeId = 2,
            StatusId = 26,
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
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA",
            TotalAmount = paidAmount,
            Status = RequestConstants.PoGroupStatuses.WaitingReconciliation, // unclassified → Phase-1 skips it
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = group.Id,
            PaymentType = RequestPayment.PaymentTypes.FinalBalance,
            PaymentStatus = RequestPayment.PaymentStatuses.Completed,
            PaymentSequence = 1,
            PlannedAmount = paidAmount,
            ActualPaidAmount = paidAmount,
            CurrencyCode = "AOA",
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, group.Id, actor.Id);
    }

    private static SubmitReconciliationDto NoDifference(decimal finalInvoice) => new()
    {
        FinalInvoiceAmount = finalInvoice,
        FinalAcceptedAmount = finalInvoice,
        DeliveredAcceptedAmount = finalInvoice,
        ReconciliationDecision = RequestReconciliation.ReconciliationDecisions.NoDifference,
        ReconciliationNotes = "ZZTEST"
    };

    private static List<RequestPayment> FinalBalances(ApplicationDbContext ctx, Guid requestId) =>
        ctx.RequestPayments.AsNoTracking()
            .Where(p => p.RequestId == requestId && p.PaymentType == RequestPayment.PaymentTypes.FinalBalance)
            .OrderBy(p => p.PaymentSequence).ToList();

    // Completion ENABLED: no DbUpdateConcurrencyException; remaining balance created as group-less
    // FINAL_BALANCE seq2 with the actor; sibling seq1 unchanged; request → PAYMENT_REQUEST_SENT.
    [Fact]
    public async Task Reconcile_RemainingBalance_CompletionEnabled_CreatesGrouplessSeq2WithActor_NoConcurrencyError()
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx, 200000m);
        var controller = BuildController(ctx, seed.ActorId, Flags(enabled: true, completion: true));

        var result = await controller.ReconcileRequest(seed.RequestId, NoDifference(250000m));
        Assert.IsType<OkObjectResult>(result);

        var rows = FinalBalances(ctx, seed.RequestId);
        Assert.Equal(2, rows.Count);

        var sibling = rows.Single(p => p.PaymentSequence == 1);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, sibling.PaymentStatus);
        Assert.Equal(seed.GroupId, sibling.RequestPoGroupId);

        var remaining = rows.Single(p => p.PaymentSequence == 2);
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, remaining.PaymentStatus);
        Assert.Null(remaining.RequestPoGroupId);          // group-less remaining balance
        Assert.Equal(50000m, remaining.PlannedAmount);    // 250000 - 200000
        Assert.Equal(seed.ActorId, remaining.CreatedByUserId);

        var req = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(13, req.StatusId); // PAYMENT_REQUEST_SENT

        var history = await ctx.RequestStatusHistories.AsNoTracking()
            .SingleAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "Reconciled_Balance_Created");
        Assert.Equal(seed.ActorId, history.ActorUserId);
    }

    // Completion DISABLED: identical reconcile outcome, unchanged.
    [Fact]
    public async Task Reconcile_RemainingBalance_CompletionDisabled_SameResult()
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx, 200000m);
        var controller = BuildController(ctx, seed.ActorId, Flags(enabled: false, completion: false));

        var result = await controller.ReconcileRequest(seed.RequestId, NoDifference(250000m));
        Assert.IsType<OkObjectResult>(result);

        var remaining = FinalBalances(ctx, seed.RequestId).Single(p => p.PaymentSequence == 2);
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, remaining.PaymentStatus);
        Assert.Null(remaining.RequestPoGroupId);
        Assert.Equal(50000m, remaining.PlannedAmount);
        Assert.Equal(seed.ActorId, remaining.CreatedByUserId);
    }

    // No remaining balance (invoice == paid): no extra FINAL_BALANCE row; still no concurrency error.
    [Fact]
    public async Task Reconcile_NoRemainingBalance_CreatesNoExtraRow()
    {
        var ctx = NewContext();
        var seed = await SeedAsync(ctx, 200000m);
        var controller = BuildController(ctx, seed.ActorId, Flags(enabled: true, completion: true));

        var result = await controller.ReconcileRequest(seed.RequestId, NoDifference(200000m));
        Assert.IsType<OkObjectResult>(result);

        Assert.Single(FinalBalances(ctx, seed.RequestId)); // only the original seq1
    }
}
