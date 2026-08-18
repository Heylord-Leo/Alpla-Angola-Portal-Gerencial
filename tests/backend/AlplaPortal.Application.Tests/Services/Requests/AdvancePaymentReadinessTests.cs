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
using AlplaPortal.Infrastructure.Services.Purchasing;
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
/// v2.229.2/v2.229.3 (REQ-17/08/2026-232): the REAL advance-payment chain — ConfirmAdvancePayment
/// hands the group to WAITING_SUPPLIER_DELIVERY (the state every Receiving surface speaks),
/// completion readiness reads the authoritative payment evidence, receiving eligibility and
/// payment readiness intentionally diverge for partial advances, and ConfirmReceiving stamps the
/// operational receipt — all with CompletionEnabled=false and zero Phase-4 transitions.
/// </summary>
public class AdvancePaymentReadinessTests
{
    /// <summary>The request statuses the Receiving workspace queries (ReceivingWorkspace.tsx).
    /// The pin below asserts the post-confirmation aggregate lands INSIDE this set.</summary>
    private static readonly string[] ReceivingWorkspaceStatusCodes =
    {
        "PAYMENT_COMPLETED", "PAG_REALIZADO", "WAITING_RECEIPT", "AG_RECIBO",
        "IN_FOLLOWUP", "COMPLETED", "FINALIZADO", "WAITING_SUPPLIER_DELIVERY"
    };

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static PostPaymentCompletionOptions TestFlags() => new()
    {
        Enabled = true,
        CompletionEnabled = false, // the exact TEST configuration
        EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
    };

    private static RequestsController BuildController(
        ApplicationDbContext ctx, Guid actorId, string role)
    {
        var options = TestFlags();
        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(
                new Mock<IServiceScopeFactory>().Object,
                new Mock<IHttpContextAccessor>().Object,
                NullLogger<AdminLogWriter>.Instance),
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
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(
            ctx, NullLogger<StatusAggregationService>.Instance, Options.Create(options)));
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(
            ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, role)
                }, "Test")),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ProofAttachmentId, Guid ActorId, decimal PlannedAdvance);

    /// <summary>
    /// REQ-232's shape: single classified group, TotalAmount 100.000, advance PLANNED, group
    /// ADVANCE_PAYMENT_REQUIRED, one line item (receivable), receipt/invoice pending, separate
    /// fiscal receipt owed.
    /// </summary>
    private static async Task<Seed> SeedAdvanceAsync(
        ApplicationDbContext ctx, decimal advancePercent, decimal plannedAdvance)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Finance 232", Email = "adv232@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 3, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 23, Code = RequestConstants.Statuses.AdvancePaymentRequired, Name = "Adiantamento Necessário", DisplayOrder = 23 },
            new RequestStatus { Id = 24, Code = RequestConstants.Statuses.AdvancePaymentCompleted, Name = "Adiantamento Realizado", DisplayOrder = 24 },
            new RequestStatus { Id = 25, Code = RequestConstants.Statuses.WaitingSupplierDelivery, Name = "Ag. Entrega/Serviço", DisplayOrder = 25 },
            new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 },
            new RequestStatus { Id = 21, Code = RequestConstants.Statuses.InFollowup, Name = "Em Acompanhamento", DisplayOrder = 18 },
            new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 });
        ctx.LineItemStatuses.Add(new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ232-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST advance readiness",
            RequestTypeId = 3,
            StatusId = 23,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "KWANZA INDUSTRIAL & SERVICOS, LDA (ZZTEST)",
            CurrencyCode = "AOA",
            TotalAmount = 100_000m,
            Status = RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
            PurchaseOrderNumber = "PO-R4A-001-ZZTEST",
            PaymentConditionCode = advancePercent >= 100m
                ? RequestConstants.PaymentConditions.AdvanceFull
                : RequestConstants.PaymentConditions.AdvancePartial,
            AdvancePaymentPercent = advancePercent,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.PendingUpload,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true,
            RequiresAdvanceRegularization = false,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        // One receivable line item — required for the ConfirmReceiving chain pins.
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            RequestPoGroupId = group.Id,
            LineNumber = 1,
            Description = "ZZTEST advance item",
            Quantity = 1
        });

        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = group.Id,
            PaymentType = RequestPayment.PaymentTypes.Advance,
            PaymentStatus = RequestPayment.PaymentStatuses.Planned,
            PlannedPercent = advancePercent,
            PlannedAmount = plannedAdvance,
            CurrencyCode = "AOA",
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        });

        var proof = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "comprovativo-adiantamento.pdf",
            FileExtension = ".pdf",
            StorageReference = Guid.NewGuid() + ".pdf",
            AttachmentTypeCode = AttachmentConstants.Types.PaymentProof,
            UploadedByUserId = actor.Id,
            UploadedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        };
        ctx.RequestAttachments.Add(proof);

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, proof.Id, actor.Id, plannedAdvance);
    }

    private static async Task ConfirmAdvanceAsync(ApplicationDbContext ctx, Seed seed, decimal amount)
    {
        var finance = BuildController(ctx, seed.ActorId, RoleConstants.Finance);
        var result = await finance.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.GroupId,
            PaymentProofAttachmentId = seed.ProofAttachmentId,
            ActualPaidAmount = amount,
            PaidDate = new DateTime(2026, 8, 17),
            Comment = "ZZTEST adiantamento"
        });
        Assert.IsType<OkObjectResult>(result);
    }

    private static async Task<CompletionReadinessGroupDto> ReadinessAsync(
        ApplicationDbContext ctx, Seed seed)
    {
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);
        var action = await controller.GetCompletionReadiness(seed.RequestId);
        var dto = Assert.IsType<CompletionReadinessDto>(((OkObjectResult)action.Result!).Value);
        return Assert.Single(dto.Groups);
    }

    // ── §14: REQ-232 full-advance regression — WSD handoff + Receiving discoverability ──

    [Fact]
    public async Task Full_advance_hands_group_to_supplier_delivery_and_is_receiving_discoverable()
    {
        using var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, advancePercent: 100m, plannedAdvance: 100_000m);

        await ConfirmAdvanceAsync(ctx, seed, 100_000m);

        var payment = await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.RequestPoGroupId == seed.GroupId);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, payment.PaymentStatus);
        Assert.Equal(100_000m, payment.ActualPaidAmount);

        // v2.229.3: the group lands in the supplier-delivery stage, never parked in
        // ADVANCE_PAYMENT_COMPLETED.
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, group.Status);

        // Aggregation projects the same state on the request ("Ag. Entrega/Serviço")…
        var request = await ctx.Requests.Include(r => r.Status).AsNoTracking()
            .SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, request.Status!.Code);

        // …which is INSIDE the Receiving workspace's status filter — REQ-232 becomes
        // discoverable with zero workspace-query changes.
        Assert.Contains(request.Status.Code, ReceivingWorkspaceStatusCodes);

        // Readiness: payment satisfied by evidence, receipt honestly pending.
        var readiness = await ReadinessAsync(ctx, seed);
        Assert.True(readiness.PaymentSatisfied);
        Assert.False(readiness.ReceiptSatisfied);
        Assert.False(readiness.Complete);
        Assert.DoesNotContain("PAYMENT_PENDING", readiness.BlockingReasons.Select(r => r.Code));
        Assert.Contains("RECEIPT_PENDING", readiness.BlockingReasons.Select(r => r.Code));
    }

    // ── §15: partial advance — receivable, yet payment stays honestly pending ──

    [Fact]
    public async Task Partial_advance_is_receivable_while_payment_remains_pending()
    {
        using var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, advancePercent: 30m, plannedAdvance: 30_000m);

        await ConfirmAdvanceAsync(ctx, seed, 30_000m);

        // Receiving eligibility != PaymentSatisfied — the intentional divergence: delivery
        // precedes reconciliation/final balance in the B2P design.
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, group.Status);

        var request = await ctx.Requests.Include(r => r.Status).AsNoTracking()
            .SingleAsync(r => r.Id == seed.RequestId);
        Assert.Contains(request.Status!.Code, ReceivingWorkspaceStatusCodes);

        var readiness = await ReadinessAsync(ctx, seed);
        Assert.False(readiness.PaymentSatisfied);
        Assert.Contains("PAYMENT_PENDING", readiness.BlockingReasons.Select(r => r.Code));
    }

    // ── §16: full-advance receiving chain — receipt stamp, no Phase-4 completion ──

    [Fact]
    public async Task Full_advance_receiving_chain_stamps_the_receipt_without_any_completion()
    {
        using var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, advancePercent: 100m, plannedAdvance: 100_000m);
        await ConfirmAdvanceAsync(ctx, seed, 100_000m);

        // Receiving registers the item then confirms — the WSD group is accepted with zero
        // endpoint-policy changes.
        var received = await ctx.LineItemStatuses.SingleAsync(s => s.Code == "RECEIVED");
        var item = await ctx.RequestLineItems.SingleAsync(li => li.RequestPoGroupId == seed.GroupId);
        item.LineItemStatusId = received.Id;
        item.ReceivedQuantity = 1;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var receiving = BuildController(ctx, seed.ActorId, RoleConstants.Receiving);
        var confirm = await receiving.ConfirmReceiving(seed.RequestId, new ConfirmReceivingDto
        {
            RequestPoGroupId = seed.GroupId,
            Comment = "ZZTEST recebimento integral"
        });
        Assert.IsType<OkObjectResult>(confirm);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status);
        Assert.NotNull(group.OperationalReceiptCompletedAtUtc);
        Assert.Equal(seed.ActorId, group.OperationalReceiptCompletedByUserId);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == WorkflowEventCodes.OperationalReceiptCompleted));

        // CompletionEnabled=false: no Phase-4 group/parent completion of any kind.
        Assert.Null(group.CompletedAtUtc);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == WorkflowEventCodes.GroupCompleted ||
                 h.ActionTaken == "REQUEST_COMPLETED"));

        var readiness = await ReadinessAsync(ctx, seed);
        Assert.True(readiness.PaymentSatisfied);
        Assert.True(readiness.ReceiptSatisfied);
        Assert.False(readiness.Complete);
    }

    // ── §17: partial-advance receiving chain — receipt satisfied, payment still owed ──

    [Fact]
    public async Task Partial_advance_receiving_chain_keeps_payment_pending_and_never_concludes()
    {
        using var ctx = NewContext();
        var seed = await SeedAdvanceAsync(ctx, advancePercent: 30m, plannedAdvance: 30_000m);
        await ConfirmAdvanceAsync(ctx, seed, 30_000m);

        var received = await ctx.LineItemStatuses.SingleAsync(s => s.Code == "RECEIVED");
        var item = await ctx.RequestLineItems.SingleAsync(li => li.RequestPoGroupId == seed.GroupId);
        item.LineItemStatusId = received.Id;
        item.ReceivedQuantity = 1;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var receiving = BuildController(ctx, seed.ActorId, RoleConstants.Receiving);
        var confirm = await receiving.ConfirmReceiving(seed.RequestId, new ConfirmReceivingDto
        {
            RequestPoGroupId = seed.GroupId,
            Comment = "ZZTEST recebimento parcial-adiantamento"
        });
        Assert.IsType<OkObjectResult>(confirm);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.NotNull(group.OperationalReceiptCompletedAtUtc);
        Assert.Null(group.CompletedAtUtc); // never concluded — money is still owed

        var readiness = await ReadinessAsync(ctx, seed);
        Assert.True(readiness.ReceiptSatisfied);
        Assert.False(readiness.PaymentSatisfied); // the existing reconciliation flow remains required
        Assert.Contains("PAYMENT_PENDING", readiness.BlockingReasons.Select(r => r.Code));
        Assert.False(readiness.Complete);
    }
}
