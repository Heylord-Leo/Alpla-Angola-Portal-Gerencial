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
using AlplaPortal.Domain.Services;
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
/// Release 4 Phase 4B: the LIVE operational-receipt writer inside ConfirmReceiving.
///
/// Pins the approved flag semantics — the stamp is a factual dimension record gated by
/// PostPaymentCompletion.Enabled (NOT CompletionEnabled), while the Phase-1 group transitions
/// stay behind CompletionEnabled — plus stamp idempotency, the normal-completion wording (never
/// the Phase-4A lazy-derivation wording on this path), and the receiving → Phase-1 handoff.
/// </summary>
public class ConfirmReceivingOperationalReceiptTests
{
    private static ApplicationDbContext NewContext(DbContextOptions<ApplicationDbContext>? options = null) =>
        new(options ?? NewOptions());

    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static RequestsController BuildController(
        ApplicationDbContext ctx, Guid actorId, PostPaymentCompletionOptions options)
    {
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

        // ConfirmReceiving resolves the aggregator and (Phase 4B) the completion service from
        // RequestServices — provide a mocked aggregator and the REAL Phase-1 engine.
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IStatusAggregationService>().Object);
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(
            ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.Receiving)
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    private static PostPaymentCompletionOptions Flags(bool enabled, bool completion) => new()
    {
        Enabled = enabled,
        CompletionEnabled = completion,
        EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
    };

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId);

    /// <summary>
    /// A PAYMENT request whose single group sits in PAYMENT_COMPLETED (a valid ConfirmReceiving
    /// entry state) with a classified PROFORMA identity, SATISFIED invoice obligation, and a
    /// separate fiscal receipt owed by default. Items are seeded per test.
    /// </summary>
    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx,
        string[] itemStatusCodes,
        Action<RequestPoGroup>? mutateGroup = null)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Receiving", Email = "recv4b@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);

        var paymentCompleted = new RequestStatus { Id = 14, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Concluído", DisplayOrder = 14 };
        var waitingReceipt = new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 };
        var inFollowup = new RequestStatus { Id = 18, Code = RequestConstants.Statuses.InFollowup, Name = "Em Acompanhamento", DisplayOrder = 18 };
        var completed = new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 };
        ctx.RequestStatuses.AddRange(paymentCompleted, waitingReceipt, inFollowup, completed);

        var received = new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" };
        var partial = new LineItemStatus { Id = 92, Code = "PARTIALLY_RECEIVED", Name = "Parcial" };
        var pending = new LineItemStatus { Id = 93, Code = "PENDING", Name = "Pendente" };
        ctx.LineItemStatuses.AddRange(received, partial, pending);
        var statusByCode = new Dictionary<string, int>
        {
            ["RECEIVED"] = received.Id,
            ["PARTIALLY_RECEIVED"] = partial.Id,
            ["PENDING"] = pending.Id
        };

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-P4B-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST phase 4B receiving",
            RequestTypeId = requestType.Id,
            StatusId = paymentCompleted.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-15)
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST Supplier 4B",
            CurrencyCode = "AOA",
            TotalAmount = 250_000m,
            Status = RequestConstants.PoGroupStatuses.PaymentCompleted,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-15),
            CreatedByUserId = actor.Id
        };
        mutateGroup?.Invoke(group);
        ctx.RequestPoGroups.Add(group);

        var line = 1;
        foreach (var code in itemStatusCodes)
        {
            ctx.RequestLineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                RequestPoGroupId = group.Id,
                LineNumber = line++,
                Description = "ZZTEST 4B item " + line,
                Quantity = 1,
                ReceivedQuantity = code == "RECEIVED" ? 1 : 0,
                LineItemStatusId = statusByCode[code]
            });
        }

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, actor.Id);
    }

    private static Task<IActionResult> ConfirmAsync(
        RequestsController controller, Seed seed) =>
        controller.ConfirmReceiving(seed.RequestId, new ConfirmReceivingDto
        {
            RequestPoGroupId = seed.GroupId,
            Comment = "ZZTEST confirmação"
        });

    // ── A: partial receipt — no stamp, no OR_DONE, legacy IN_FOLLOWUP preserved ──

    [Fact]
    public async Task A_partial_receipt_gets_no_stamp_and_keeps_in_followup()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, new[] { "RECEIVED", "PARTIALLY_RECEIVED" });
        var controller = BuildController(ctx, seed.ActorId, Flags(enabled: true, completion: false));

        var result = await ConfirmAsync(controller, seed);

        Assert.IsType<OkObjectResult>(result);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.InFollowup, group.Status);
        Assert.Null(group.OperationalReceiptCompletedAtUtc);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == WorkflowEventCodes.OperationalReceiptCompleted));
    }

    // ── B: full receipt — one stamp, one OR_DONE, live wording ──

    [Fact]
    public async Task B_full_receipt_stamps_once_with_live_event_wording()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, new[] { "RECEIVED", "RECEIVED" });
        var controller = BuildController(ctx, seed.ActorId, Flags(enabled: true, completion: false));

        var before = DateTime.UtcNow;
        var result = await ConfirmAsync(controller, seed);

        Assert.IsType<OkObjectResult>(result);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status);
        Assert.NotNull(group.OperationalReceiptCompletedAtUtc);
        Assert.True(group.OperationalReceiptCompletedAtUtc >= before.AddSeconds(-5));
        Assert.Equal(seed.ActorId, group.OperationalReceiptCompletedByUserId);

        var stamp = await ctx.RequestStatusHistories.SingleAsync(
            h => h.ActionTaken == WorkflowEventCodes.OperationalReceiptCompleted);
        Assert.Equal(PostPaymentIdempotencyKeys.OperationalReceiptCompleted(seed.GroupId), stamp.IdempotencyKey);
        Assert.Equal(seed.ActorId, stamp.ActorUserId);
        // Live receipt-completion wording — never the Phase-4A lazy-derivation wording.
        Assert.Contains("todos os itens do grupo foram recebidos", stamp.Comment);
        Assert.DoesNotContain("derivado dos registos", stamp.Comment);

        // Enabled=true / CompletionEnabled=false: the stamp exists but Phase 1 is a NoOp —
        // no antechamber, no completion, no Phase-4 transition history.
        Assert.Null(group.CompletedAtUtc);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == WorkflowEventCodes.FiscalReceiptUnlocked ||
                 h.ActionTaken == WorkflowEventCodes.GroupCompleted));
    }

    // ── C: retry — original stamp preserved, no duplicate history ──

    [Fact]
    public async Task C_retried_confirmation_preserves_the_stamp_and_writes_no_duplicate()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, new[] { "RECEIVED" });
        var controller = BuildController(ctx, seed.ActorId, Flags(enabled: true, completion: false));

        await ConfirmAsync(controller, seed);
        var first = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        var firstStampAt = first.OperationalReceiptCompletedAtUtc;

        await ConfirmAsync(controller, seed);

        var second = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(firstStampAt, second.OperationalReceiptCompletedAtUtc);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == WorkflowEventCodes.OperationalReceiptCompleted));
    }

    // ── D: CompletionEnabled=true — receiving hands the group to Phase 1 (antechamber) ──

    [Fact]
    public async Task D_full_receipt_with_completion_on_moves_group_to_waiting_fiscal_receipt()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, new[] { "RECEIVED" });
        var controller = BuildController(ctx, seed.ActorId, Flags(enabled: true, completion: true));

        await ConfirmAsync(controller, seed);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt, group.Status);
        Assert.Null(group.CompletedAtUtc);
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == WorkflowEventCodes.FiscalReceiptUnlocked));
    }

    // ── E: CompletionEnabled=true, no separate receipt owed — the FULL 4C chain ──

    [Fact]
    public async Task E_full_receipt_with_no_separate_fiscal_receipt_completes_group_and_parent()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, new[] { "RECEIVED" },
            mutateGroup: g => g.RequiresSeparateFiscalReceipt = false);
        var controller = BuildController(ctx, seed.ActorId, Flags(enabled: true, completion: true));

        await ConfirmAsync(controller, seed);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, group.Status);
        Assert.NotNull(group.CompletedAtUtc);

        var completion = await ctx.RequestStatusHistories.SingleAsync(
            h => h.ActionTaken == WorkflowEventCodes.GroupCompleted);
        Assert.Equal(
            PostPaymentIdempotencyKeys.GroupCompletedWithoutFiscalReceipt(seed.GroupId),
            completion.IdempotencyKey);

        // Phase 4C: receiving → Phase 1 group COMPLETED → commit → Phase 2 parent COMPLETED,
        // through the authoritative service (cycle id + RC history), exactly once.
        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.NotNull(request.CompletionCycleId);
        var rc = await ctx.RequestStatusHistories.SingleAsync(h => h.ActionTaken == "REQUEST_COMPLETED");
        Assert.Equal(
            PostPaymentIdempotencyKeys.RequestCompleted(seed.RequestId, request.CompletionCycleId!.Value),
            rc.IdempotencyKey);
        var completedStatus = await ctx.RequestStatuses.AsNoTracking()
            .SingleAsync(s => s.Code == RequestConstants.Statuses.Completed);
        Assert.Equal(completedStatus.Id, request.StatusId);
    }

    // ── F: Enabled=false — legacy behaviour byte-identical, no Phase-4 artifacts ──

    [Fact]
    public async Task F_feature_disabled_preserves_legacy_receiving_with_no_stamp()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, new[] { "RECEIVED" });
        var controller = BuildController(ctx, seed.ActorId, Flags(enabled: false, completion: false));

        var result = await ConfirmAsync(controller, seed);

        Assert.IsType<OkObjectResult>(result);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status);
        Assert.Null(group.OperationalReceiptCompletedAtUtc);
        Assert.Null(group.OperationalReceiptCompletedByUserId);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.IdempotencyKey != null));
    }
}
