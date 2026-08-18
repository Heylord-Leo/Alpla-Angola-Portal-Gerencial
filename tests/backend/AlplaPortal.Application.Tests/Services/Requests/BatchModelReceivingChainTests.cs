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
/// v2.229.5 (REQ-17/08/2026-232, fifth STATE 1 finding): the REAL two-step receiving chain of
/// the batch/candidate QUOTATION model, end to end through the production endpoints.
///
/// The shape under test: the batch award stamped <c>SelectedQuotationItemId</c> on the request
/// line item as a compatibility pointer, the request carries NO request-level
/// <c>SelectedQuotationId</c>, so the receiving UI registers quantities on the
/// <c>RequestLineItem</c> (the winning <c>QuotationItem</c> is never touched). Step A is the
/// real <c>LineItemsController.UpdateReceiving</c> writer; Step B is the real
/// <c>RequestsController.ConfirmReceiving</c>. Before v2.229.5 the rulebook read only the
/// quotation side of the pointer and this exact chain parked the group in IN_FOLLOWUP with no
/// operational receipt stamp.
/// </summary>
public class BatchModelReceivingChainTests
{
    private static ApplicationDbContext NewContext(DbContextOptions<ApplicationDbContext>? options = null) =>
        new(options ?? NewOptions());

    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static PostPaymentCompletionOptions Flags(bool enabled = true, bool completion = false) => new()
    {
        Enabled = enabled,
        CompletionEnabled = completion,
        EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
    };

    private static ClaimsPrincipal ReceivingUser(Guid actorId) =>
        new(new ClaimsIdentity(new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.Receiving)
        }, "Test"));

    private static RequestsController BuildRequestsController(
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

        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IStatusAggregationService>().Object);
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(
            ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = ReceivingUser(actorId),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    private static LineItemsController BuildLineItemsController(
        ApplicationDbContext ctx, Guid actorId, PostPaymentCompletionOptions options)
    {
        var controller = new LineItemsController(
            ctx,
            NullLogger<LineItemsController>.Instance,
            new Mock<AlplaPortal.Application.Interfaces.IApprovalRoutingService>().Object,
            Options.Create(options),
            new RequestCompletionService(ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = ReceivingUser(actorId),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid LineItemId, Guid QuotationItemId, Guid ActorId);

    /// <summary>
    /// A batch/candidate QUOTATION request: one group in WAITING_SUPPLIER_DELIVERY (the exact
    /// REQ-232 entry state), one line item with the compatibility pointer to the winning
    /// quotation item, request-level SelectedQuotationId left NULL (per the batch model), all
    /// receiving statuses PENDING on both sides.
    /// </summary>
    private static async Task<Seed> SeedBatchShapeAsync(
        ApplicationDbContext ctx,
        decimal authorizedQuantity = 1m,
        string groupStatus = RequestConstants.Statuses.WaitingSupplierDelivery)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Batch Receiving", Email = "recv2295@test.local" };
        ctx.Users.Add(actor);

        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });

        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 14, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Concluído", DisplayOrder = 14 },
            new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 },
            new RequestStatus { Id = 18, Code = RequestConstants.Statuses.InFollowup, Name = "Em Acompanhamento", DisplayOrder = 18 },
            new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 },
            new RequestStatus { Id = 33, Code = RequestConstants.Statuses.WaitingSupplierDelivery, Name = "Ag. Entrega/Serviço", DisplayOrder = 16 });

        var received = new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" };
        var partial = new LineItemStatus { Id = 92, Code = "PARTIALLY_RECEIVED", Name = "Parcial" };
        var pending = new LineItemStatus { Id = 93, Code = "PENDING", Name = "Pendente" };
        ctx.LineItemStatuses.AddRange(received, partial, pending);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-R2295-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST batch receiving chain",
            RequestTypeId = 1,
            StatusId = 33,
            SelectedQuotationId = null, // the batch model: per-item winners, no request-level award
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-20)
        };
        ctx.Requests.Add(request);

        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST Batch Supplier",
            Currency = "AOA",
            DocumentType = RequestConstants.SourceDocumentTypes.Proforma
        };
        ctx.Quotations.Add(quotation);

        var quotationItem = new QuotationItem
        {
            Id = Guid.NewGuid(),
            QuotationId = quotation.Id,
            LineNumber = 1,
            Description = "ZZTEST winning quotation item",
            Quantity = authorizedQuantity,
            LineItemStatusId = pending.Id // NEVER updated by the batch-model receiving UI
        };
        ctx.Set<QuotationItem>().Add(quotationItem);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST Batch Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 100_000m,
            Status = groupStatus,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.PendingUpload,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-20),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        var lineItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            RequestPoGroupId = group.Id,
            SelectedQuotationItemId = quotationItem.Id, // the batch compatibility pointer
            LineNumber = 1,
            Description = "ZZTEST batch item",
            Quantity = authorizedQuantity,
            ReceivedQuantity = 0m,
            LineItemStatusId = pending.Id
        };
        ctx.RequestLineItems.Add(lineItem);

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, lineItem.Id, quotationItem.Id, actor.Id);
    }

    private static Task<IActionResult> RegisterAsync(
        LineItemsController controller, Seed seed, decimal receivedQuantity) =>
        controller.UpdateReceiving(seed.LineItemId, new UpdateItemReceivingDto
        {
            ReceivedQuantity = receivedQuantity,
            DivergenceNotes = null
        });

    private static Task<IActionResult> ConfirmAsync(
        RequestsController controller, Seed seed, string comment = "ZZTEST atestação de recebimento") =>
        controller.ConfirmReceiving(seed.RequestId, new ConfirmReceivingDto
        {
            RequestPoGroupId = seed.GroupId,
            Comment = comment
        });

    // ── A (§7): the full two-step chain — register 1/1, then confirm ──

    [Fact]
    public async Task A_full_two_step_chain_stamps_and_moves_to_waiting_receipt()
    {
        var options = NewOptions();
        var flags = Flags();
        var seed = await SeedBatchShapeAsync(NewContext(options));

        // Step A: the REAL registration writer — quantity reaches the authorized total.
        using (var ctx = NewContext(options))
        {
            var result = await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId, flags), seed, 1m);
            Assert.IsType<NoContentResult>(result);
        }

        // Persisted intermediate state: own record RECEIVED, quotation side untouched.
        using (var ctx = NewContext(options))
        {
            var li = await ctx.RequestLineItems.Include(l => l.LineItemStatus).AsNoTracking()
                .SingleAsync(l => l.Id == seed.LineItemId);
            Assert.Equal("RECEIVED", li.LineItemStatus!.Code);
            Assert.Equal(1m, li.ReceivedQuantity);
            var qi = await ctx.Set<QuotationItem>().Include(q => q.LineItemStatus).AsNoTracking()
                .SingleAsync(q => q.Id == seed.QuotationItemId);
            Assert.Equal("PENDING", qi.LineItemStatus!.Code); // unchanged by design
        }

        // Step B: the REAL ConfirmReceiving, as a separate request.
        using (var ctx = NewContext(options))
        {
            var result = await ConfirmAsync(BuildRequestsController(ctx, seed.ActorId, flags), seed);
            Assert.IsType<OkObjectResult>(result);
        }

        using (var ctx = NewContext(options))
        {
            var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status);
            Assert.NotNull(group.OperationalReceiptCompletedAtUtc);
            Assert.Equal(seed.ActorId, group.OperationalReceiptCompletedByUserId);

            var orKey = PostPaymentIdempotencyKeys.OperationalReceiptCompleted(seed.GroupId);
            Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.IdempotencyKey == orKey));
            Assert.True(await ctx.RequestStatusHistories.AnyAsync(
                h => h.RequestId == seed.RequestId && h.ActionTaken == "CONFIRM_RECEIVING"));

            // CompletionEnabled=false: NOTHING completed automatically.
            Assert.NotEqual(RequestConstants.PoGroupStatuses.Completed, group.Status);
            var request = await ctx.Requests.Include(r => r.Status).AsNoTracking()
                .SingleAsync(r => r.Id == seed.RequestId);
            Assert.NotEqual(RequestConstants.Statuses.Completed, request.Status!.Code);
            Assert.False(await ctx.RequestStatusHistories.AnyAsync(
                h => h.ActionTaken == WorkflowEventCodes.GroupCompleted
                  || h.ActionTaken == "REQUEST_COMPLETED"));
        }
    }

    // ── B (§8): partial two-step chain — register 1 of 2, then confirm ──

    [Fact]
    public async Task B_partial_two_step_chain_keeps_followup_and_never_stamps()
    {
        var options = NewOptions();
        var flags = Flags();
        var seed = await SeedBatchShapeAsync(NewContext(options), authorizedQuantity: 2m);

        using (var ctx = NewContext(options))
        {
            Assert.IsType<NoContentResult>(
                await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId, flags), seed, 1m));
        }

        using (var ctx = NewContext(options))
        {
            var li = await ctx.RequestLineItems.Include(l => l.LineItemStatus).AsNoTracking()
                .SingleAsync(l => l.Id == seed.LineItemId);
            Assert.Equal("PARTIALLY_RECEIVED", li.LineItemStatus!.Code); // the real writer's partial status
        }

        using (var ctx = NewContext(options))
        {
            Assert.IsType<OkObjectResult>(
                await ConfirmAsync(BuildRequestsController(ctx, seed.ActorId, flags), seed));
        }

        using (var ctx = NewContext(options))
        {
            var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
            Assert.Equal(RequestConstants.PoGroupStatuses.InFollowup, group.Status);
            Assert.Null(group.OperationalReceiptCompletedAtUtc);
            Assert.Null(group.OperationalReceiptCompletedByUserId);
            Assert.False(await ctx.RequestStatusHistories.AnyAsync(
                h => h.ActionTaken == WorkflowEventCodes.OperationalReceiptCompleted));

            var projection = await ProjectAsync(ctx, seed);
            Assert.False(projection.ReceiptSatisfied);
        }
    }

    // ── C (§9): the REQ-232 healing path — group already parked IN_FOLLOWUP, re-confirm ──

    [Fact]
    public async Task C_reconfirming_a_parked_followup_group_heals_it_through_the_front_door()
    {
        var options = NewOptions();
        var flags = Flags();
        // The EXACT deployed REQ-232 state: group IN_FOLLOWUP (the defective first confirmation),
        // own record already RECEIVED 1/1, quotation side PENDING, no stamp, no OR_DONE.
        var seed = await SeedBatchShapeAsync(NewContext(options), groupStatus: RequestConstants.PoGroupStatuses.InFollowup);
        using (var ctx = NewContext(options))
        {
            var li = await ctx.RequestLineItems.SingleAsync(l => l.Id == seed.LineItemId);
            li.ReceivedQuantity = 1m;
            li.LineItemStatusId = 91; // RECEIVED
            await ctx.SaveChangesAsync();
        }

        using (var ctx = NewContext(options))
        {
            var result = await ConfirmAsync(BuildRequestsController(ctx, seed.ActorId, flags), seed,
                comment: "Atesto que os bens ou serviços deste grupo foram efetivamente recebidos ou executados.");
            Assert.IsType<OkObjectResult>(result);
        }

        using (var ctx = NewContext(options))
        {
            var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status);
            Assert.NotNull(group.OperationalReceiptCompletedAtUtc);
            Assert.Equal(seed.ActorId, group.OperationalReceiptCompletedByUserId);

            var orKey = PostPaymentIdempotencyKeys.OperationalReceiptCompleted(seed.GroupId);
            Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.IdempotencyKey == orKey));

            var projection = await ProjectAsync(ctx, seed);
            Assert.True(projection.ReceiptSatisfied);
        }
    }

    // ── D (§10): idempotency — a second confirmation never duplicates or restamps ──

    [Fact]
    public async Task D_repeated_confirmation_preserves_the_original_stamp_and_single_or_done()
    {
        var options = NewOptions();
        var flags = Flags();
        var seed = await SeedBatchShapeAsync(NewContext(options));

        using (var ctx = NewContext(options))
        {
            Assert.IsType<NoContentResult>(
                await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId, flags), seed, 1m));
        }
        using (var ctx = NewContext(options))
        {
            Assert.IsType<OkObjectResult>(
                await ConfirmAsync(BuildRequestsController(ctx, seed.ActorId, flags), seed));
        }

        DateTime? firstStamp;
        using (var ctx = NewContext(options))
        {
            firstStamp = (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId))
                .OperationalReceiptCompletedAtUtc;
            Assert.NotNull(firstStamp);
        }

        // WAITING_RECEIPT is an allowed ConfirmReceiving entry state — replay the confirmation.
        using (var ctx = NewContext(options))
        {
            Assert.IsType<OkObjectResult>(
                await ConfirmAsync(BuildRequestsController(ctx, seed.ActorId, flags), seed));
        }

        using (var ctx = NewContext(options))
        {
            var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status);
            Assert.Equal(firstStamp, group.OperationalReceiptCompletedAtUtc); // original instant preserved
            var orKey = PostPaymentIdempotencyKeys.OperationalReceiptCompleted(seed.GroupId);
            Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.IdempotencyKey == orKey));
            Assert.False(await ctx.RequestStatusHistories.AnyAsync(
                h => h.ActionTaken == WorkflowEventCodes.GroupCompleted));
        }
    }

    // ── E (§11): the readiness endpoint reads the batch shape without any stamp ──

    [Fact]
    public async Task E_readiness_endpoint_reads_receipt_satisfied_from_the_line_item_record_alone()
    {
        var options = NewOptions();
        var flags = Flags();
        var seed = await SeedBatchShapeAsync(NewContext(options));
        using (var ctx = NewContext(options))
        {
            var li = await ctx.RequestLineItems.SingleAsync(l => l.Id == seed.LineItemId);
            li.ReceivedQuantity = 1m;
            li.LineItemStatusId = 91; // RECEIVED — no stamp exists, no confirmation ran
            await ctx.SaveChangesAsync();
        }

        using (var ctx = NewContext(options))
        {
            var controller = BuildRequestsController(ctx, seed.ActorId, flags);
            var action = await controller.GetCompletionReadiness(seed.RequestId);
            var ok = Assert.IsType<OkObjectResult>(action.Result);
            var dto = Assert.IsType<CompletionReadinessDto>(ok.Value);

            var groupDto = Assert.Single(dto.Groups);
            Assert.True(groupDto.ReceiptSatisfied);
            Assert.False(groupDto.FiscalReceiptSatisfied); // still honestly owed
        }

        using (var ctx = NewContext(options))
        {
            var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
            Assert.Null(group.OperationalReceiptCompletedAtUtc); // reading readiness wrote nothing
        }
    }

    private static async Task<GroupCompletionProjection> ProjectAsync(ApplicationDbContext ctx, Seed seed)
    {
        var group = await ctx.RequestPoGroups.AsNoTracking()
            .Include(g => g.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(g => g.LineItems).ThenInclude(li => li.SelectedQuotationItem)
                .ThenInclude(qi => qi!.LineItemStatus)
            .SingleAsync(g => g.Id == seed.GroupId);
        var payments = await ctx.RequestPayments.AsNoTracking()
            .Where(p => p.RequestId == seed.RequestId).ToListAsync();
        var reconciliations = await ctx.RequestReconciliations.AsNoTracking()
            .Where(r => r.RequestId == seed.RequestId).ToListAsync();
        return GroupCompletionProjector.Project(group, payments, reconciliations, hasApprovedShortClose: false);
    }
}
