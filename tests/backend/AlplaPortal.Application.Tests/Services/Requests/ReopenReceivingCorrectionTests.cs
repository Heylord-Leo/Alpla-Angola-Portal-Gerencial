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
/// v2.245.5 — receiving corrections after confirmation.
///
/// (1) Item registration/correction is a PRE-confirmation action: a confirmed group (WAITING_RECEIPT)
/// rejects direct quantity changes with 409, even when the endpoint is called manually.
/// (2) REABRIR RECEBIMENTO (<c>POST {id}/operational/groups/{groupId}/reopen-receiving</c>) returns ONE
/// confirmed group to IN_FOLLOWUP with a mandatory reason, preserving quantities and history, writing a
/// RECEIVING_REOPENED audit and recomputing the scalar through the aggregator; refused for terminal
/// requests, non-confirmed groups, duplicate calls and when an ACTIVE supplier RECEIPT exists.
/// (3) A decrease of the accumulated quantity is an auditable ITEM_RECEIVING_ADJUSTMENT fact.
/// </summary>
public class ReopenReceivingCorrectionTests
{
    private const int STATUS_PAYMENT_COMPLETED_ID = 14;
    private const int STATUS_WAITING_RECEIPT_ID = 16;
    private const int STATUS_COMPLETED_ID = 17;
    private const int STATUS_IN_FOLLOWUP_ID = 18;

    private static ApplicationDbContext NewContext(DbContextOptions<ApplicationDbContext>? options = null) =>
        new(options ?? NewOptions());

    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    // TEST/PROD configuration: post-payment completion disabled.
    private static PostPaymentCompletionOptions Flags() => new()
    {
        Enabled = false,
        CompletionEnabled = false,
        EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
    };

    private static ClaimsPrincipal UserWithRole(Guid actorId, string role) =>
        new(new ClaimsIdentity(new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, role)
        }, "Test"));

    private sealed record Harness(RequestsController Controller, Mock<IStatusAggregationService> Aggregator);

    private static Harness BuildRequestsController(ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.Receiving)
    {
        var options = Flags();
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

        var aggregator = new Mock<IStatusAggregationService>();
        var services = new ServiceCollection();
        services.AddSingleton(aggregator.Object);
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(
            ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = UserWithRole(actorId, role),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return new Harness(controller, aggregator);
    }

    private static LineItemsController BuildLineItemsController(ApplicationDbContext ctx, Guid actorId)
    {
        var options = Flags();
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
                User = UserWithRole(actorId, RoleConstants.Receiving),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid[] ItemIds, Guid ActorId);

    /// <summary>
    /// A PAYMENT request with one group (status configurable) and N line items with the given accumulated
    /// received quantities (authorized quantity 2 each). Request scalar mirrors the group.
    /// </summary>
    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx,
        string groupStatus = RequestConstants.PoGroupStatuses.WaitingReceipt,
        decimal[]? receivedQuantities = null,
        int? requestStatusId = null)
    {
        receivedQuantities ??= new[] { 2m, 2m };

        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Reopen", Email = $"reopen-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = STATUS_PAYMENT_COMPLETED_ID, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Concluído", DisplayOrder = 14 },
            new RequestStatus { Id = STATUS_WAITING_RECEIPT_ID, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 },
            new RequestStatus { Id = STATUS_COMPLETED_ID, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 },
            new RequestStatus { Id = STATUS_IN_FOLLOWUP_ID, Code = RequestConstants.Statuses.InFollowup, Name = "Em Acompanhamento", DisplayOrder = 18 });
        var received = new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" };
        var partial = new LineItemStatus { Id = 92, Code = "PARTIALLY_RECEIVED", Name = "Parcial" };
        var pending = new LineItemStatus { Id = 93, Code = "PENDING", Name = "Pendente" };
        ctx.LineItemStatuses.AddRange(received, partial, pending);

        var scalar = requestStatusId ?? groupStatus switch
        {
            RequestConstants.PoGroupStatuses.WaitingReceipt => STATUS_WAITING_RECEIPT_ID,
            RequestConstants.PoGroupStatuses.InFollowup => STATUS_IN_FOLLOWUP_ID,
            RequestConstants.PoGroupStatuses.Completed => STATUS_COMPLETED_ID,
            _ => STATUS_PAYMENT_COMPLETED_ID
        };

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REOPEN-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST reopen receiving",
            RequestTypeId = 2,
            StatusId = scalar,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST Reopen Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 100_000m,
            Status = groupStatus,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedByUserId = actor.Id,
            OperationalReceiptCompletedAtUtc = groupStatus == RequestConstants.PoGroupStatuses.WaitingReceipt ? DateTime.UtcNow.AddDays(-1) : null,
            OperationalReceiptCompletedByUserId = groupStatus == RequestConstants.PoGroupStatuses.WaitingReceipt ? actor.Id : null
        };
        ctx.RequestPoGroups.Add(group);

        var itemIds = new List<Guid>();
        var line = 1;
        foreach (var qty in receivedQuantities)
        {
            var item = new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                RequestPoGroupId = group.Id,
                LineNumber = line++,
                Description = "ZZTEST reopen item " + line,
                Quantity = 2m,
                ReceivedQuantity = qty,
                LineItemStatusId = qty >= 2m ? received.Id : qty > 0 ? partial.Id : pending.Id
            };
            ctx.RequestLineItems.Add(item);
            itemIds.Add(item.Id);
        }

        // A confirmed group carries the historical confirmation event (must survive a reopen).
        if (groupStatus == RequestConstants.PoGroupStatuses.WaitingReceipt)
        {
            ctx.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = request.Id, ActorUserId = actor.Id, ActionTaken = "CONFIRM_RECEIVING",
                PreviousStatusId = STATUS_PAYMENT_COMPLETED_ID, NewStatusId = STATUS_WAITING_RECEIPT_ID,
                Comment = $"[Grupo P.O.: {group.SupplierNameSnapshot} | GroupId: {group.Id.ToString().Substring(0, 8)}] ZZTEST",
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
            });
        }

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, itemIds.ToArray(), actor.Id);
    }

    private static async Task<Guid> AddSiblingGroupAsync(ApplicationDbContext ctx, Seed seed, string status)
    {
        var g = new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = seed.RequestId, SupplierNameSnapshot = "ZZTEST Sibling", CurrencyCode = "AOA",
            TotalAmount = 5_000m, Status = status, SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied, RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true, CreatedAtUtc = DateTime.UtcNow.AddDays(-10), CreatedByUserId = seed.ActorId,
            OperationalReceiptCompletedAtUtc = status == RequestConstants.PoGroupStatuses.WaitingReceipt ? DateTime.UtcNow.AddDays(-1) : null
        };
        ctx.RequestPoGroups.Add(g);
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = seed.RequestId, RequestPoGroupId = g.Id, LineNumber = 99,
            Description = "ZZTEST sibling item", Quantity = 1m, ReceivedQuantity = 1m, LineItemStatusId = 91
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return g.Id;
    }

    private static async Task AddAttachmentAsync(ApplicationDbContext ctx, Guid requestId, Guid actorId, string typeCode, bool deleted = false, bool voided = false)
    {
        ctx.RequestAttachments.Add(new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = requestId, AttachmentTypeCode = typeCode,
            FileName = "d.pdf", FileExtension = "pdf", FileSizeMBytes = 0.01m, StorageReference = "x/d.pdf",
            UploadedByUserId = actorId, UploadedAtUtc = DateTime.UtcNow, IsDeleted = deleted,
            VoidedAtUtc = voided ? DateTime.UtcNow : null
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    private static Task<IActionResult> ReopenAsync(RequestsController c, Seed seed, string? reason = "ZZTEST item errado", Guid? groupId = null) =>
        c.ReopenReceiving(seed.RequestId, groupId ?? seed.GroupId, reason == null ? null : new ReopenReceivingDto { Reason = reason });

    private static Task<IActionResult> RegisterAsync(LineItemsController c, Guid itemId, decimal qty) =>
        c.UpdateReceiving(itemId, new UpdateItemReceivingDto { ReceivedQuantity = qty, DivergenceNotes = null });

    private static Task<IActionResult> ConfirmAsync(RequestsController c, Seed seed) =>
        c.ConfirmReceiving(seed.RequestId, new ConfirmReceivingDto { RequestPoGroupId = seed.GroupId, Comment = "ZZTEST nova confirmação" });

    private static async Task<(string status, decimal[] qty, DateTime? stamp)> SnapshotAsync(ApplicationDbContext ctx, Seed seed)
    {
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == seed.GroupId);
        var items = await ctx.RequestLineItems.AsNoTracking().Where(i => i.RequestPoGroupId == seed.GroupId).OrderBy(i => i.LineNumber).ToListAsync();
        return (g.Status, items.Select(i => i.ReceivedQuantity).ToArray(), g.OperationalReceiptCompletedAtUtc);
    }

    // ═══════════════════════════ §1/§2 registration is PRE-confirmation only ═══════════════════════════

    [Fact]
    public async Task Registration_AtWaitingReceipt_Rejected409_NoWrites()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, RequestConstants.PoGroupStatuses.WaitingReceipt, new[] { 2m, 1m });
        var historyBefore = await ctx.RequestStatusHistories.CountAsync();

        var result = await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[1], 2m);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Contains("REABRIR RECEBIMENTO", problem.Detail);
        var (status, qty, _) = await SnapshotAsync(ctx, seed);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, status);
        Assert.Equal(new[] { 2m, 1m }, qty);
        Assert.Equal(historyBefore, await ctx.RequestStatusHistories.CountAsync());
    }

    [Theory]
    [InlineData(RequestConstants.PoGroupStatuses.PaymentCompleted)]
    [InlineData(RequestConstants.PoGroupStatuses.InFollowup)]
    [InlineData(RequestConstants.PoGroupStatuses.WaitingSupplierDelivery)]
    public async Task Registration_PreConfirmation_StillAllowed(string groupStatus)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, groupStatus, new[] { 0m, 0m });

        var result = await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[0], 1m);

        Assert.IsType<NoContentResult>(result);
        var (_, qty, _) = await SnapshotAsync(ctx, seed);
        Assert.Equal(1m, qty[0]);
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "ITEM_RECEIVING_REGISTRATION"));
    }

    [Fact]
    public async Task Registration_NegativeQuantity_Rejected400()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, RequestConstants.PoGroupStatuses.InFollowup, new[] { 1m });

        var result = await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[0], -1m);

        Assert.IsType<BadRequestObjectResult>(result);
        var (_, qty, _) = await SnapshotAsync(ctx, seed);
        Assert.Equal(1m, qty[0]);
    }

    // ── §1/§4 pre-confirmation correction = auditable adjustment fact ──

    [Fact]
    public async Task Correction_Decrease_WritesAdjustmentFact_PreservesRegistrationHistory()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, RequestConstants.PoGroupStatuses.InFollowup, new[] { 2m, 2m });

        // reduce 2 → 1 (partial), then reset 1 → 0 (pending)
        Assert.IsType<NoContentResult>(await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[0], 1m));
        Assert.IsType<NoContentResult>(await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[0], 0m));

        var item = await ctx.RequestLineItems.Include(i => i.LineItemStatus).AsNoTracking().SingleAsync(i => i.Id == seed.ItemIds[0]);
        Assert.Equal(0m, item.ReceivedQuantity);
        Assert.Equal("PENDING", item.LineItemStatus!.Code);

        var adjustments = await ctx.RequestStatusHistories.Where(h => h.ActionTaken == "ITEM_RECEIVING_ADJUSTMENT").OrderBy(h => h.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, adjustments.Count);
        Assert.All(adjustments, a => { Assert.Equal(seed.ActorId, a.ActorUserId); Assert.Contains("estorno/correção", a.Comment); });
        Assert.Contains("-1", adjustments[0].Comment);
        // no REGISTRATION event fabricated for a decrease
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "ITEM_RECEIVING_REGISTRATION"));
        // the sibling item is untouched
        Assert.Equal(2m, (await ctx.RequestLineItems.AsNoTracking().SingleAsync(i => i.Id == seed.ItemIds[1])).ReceivedQuantity);
    }

    [Fact]
    public async Task Correction_Increase_KeepsRegistrationEvent_NotAdjustment()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, RequestConstants.PoGroupStatuses.InFollowup, new[] { 1m });

        Assert.IsType<NoContentResult>(await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[0], 2m));

        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "ITEM_RECEIVING_REGISTRATION"));
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "ITEM_RECEIVING_ADJUSTMENT"));
    }

    // ═══════════════════════════ §2 REABRIR RECEBIMENTO — happy path ═══════════════════════════

    [Theory]
    [InlineData(RoleConstants.Receiving)]
    [InlineData(RoleConstants.SystemAdministrator)]
    public async Task Reopen_AuthorizedRole_ReturnsGroupToInFollowup_PreservesQuantitiesAndHistory_Audits_Aggregates(string role)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, RequestConstants.PoGroupStatuses.WaitingReceipt, new[] { 2m, 1m });
        var confirmHistoryBefore = await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == "CONFIRM_RECEIVING");
        var h = BuildRequestsController(ctx, seed.ActorId, role);

        var result = await ReopenAsync(h.Controller, seed, "  Item registado no grupo errado  ");

        Assert.IsType<OkObjectResult>(result);
        var (status, qty, stamp) = await SnapshotAsync(ctx, seed);
        Assert.Equal(RequestConstants.PoGroupStatuses.InFollowup, status);
        Assert.Equal(new[] { 2m, 1m }, qty);           // §4 quantities preserved — unlock only
        Assert.Null(stamp);                             // the operational-completion assertion no longer holds
        // history never deleted
        Assert.Equal(confirmHistoryBefore, await ctx.RequestStatusHistories.CountAsync(x => x.ActionTaken == "CONFIRM_RECEIVING"));
        var audit = await ctx.RequestStatusHistories.SingleAsync(x => x.ActionTaken == "RECEIVING_REOPENED");
        Assert.Equal(seed.ActorId, audit.ActorUserId);
        Assert.Equal(seed.RequestId, audit.RequestId);
        Assert.Contains($"GroupId: {seed.GroupId.ToString().Substring(0, 8)}", audit.Comment);
        Assert.Contains("Motivo: Item registado no grupo errado", audit.Comment);
        Assert.Contains("WAITING_RECEIPT → IN_FOLLOWUP", audit.Comment);
        // v2.245.6: the event's resulting status is the group's target (IN_FOLLOWUP), never the pre-reopen scalar
        Assert.Equal(STATUS_IN_FOLLOWUP_ID, audit.NewStatusId);
        Assert.Equal(STATUS_WAITING_RECEIPT_ID, audit.PreviousStatusId);
        // §2 the scalar is recomputed ONLY by the canonical aggregator
        h.Aggregator.Verify(a => a.AggregateRequestStatusAsync(seed.RequestId, seed.ActorId, It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    // ── §2 authorization / validation ──

    [Theory]
    [InlineData(RoleConstants.Buyer)]
    [InlineData(RoleConstants.Finance)]
    [InlineData(RoleConstants.Requester)]
    public async Task Reopen_UnauthorizedRole_403_NoWrites(string role)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var historyBefore = await ctx.RequestStatusHistories.CountAsync();
        var h = BuildRequestsController(ctx, seed.ActorId, role);

        var result = await ReopenAsync(h.Controller, seed);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, (await SnapshotAsync(ctx, seed)).status);
        Assert.Equal(historyBefore, await ctx.RequestStatusHistories.CountAsync());
        h.Aggregator.Verify(a => a.AggregateRequestStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Reopen_BlankReason_400_NoWrites(string? reason)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var historyBefore = await ctx.RequestStatusHistories.CountAsync();
        var h = BuildRequestsController(ctx, seed.ActorId);

        var result = await ReopenAsync(h.Controller, seed, reason);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, (await SnapshotAsync(ctx, seed)).status);
        Assert.Equal(historyBefore, await ctx.RequestStatusHistories.CountAsync());
    }

    [Fact]
    public async Task Reopen_UnknownRequest_404()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var h = BuildRequestsController(ctx, seed.ActorId);

        var result = await h.Controller.ReopenReceiving(Guid.NewGuid(), seed.GroupId, new ReopenReceivingDto { Reason = "x" });

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, (await SnapshotAsync(ctx, seed)).status);
    }

    [Fact]
    public async Task Reopen_GroupOfAnotherRequest_404_NoWrites()
    {
        var options = NewOptions();
        Seed seedA, seedB;
        using (var ctx = NewContext(options)) seedA = await SeedAsync(ctx);
        using (var ctx = NewContext(options))
        {
            // second request/group in the same store (lookups already seeded → add only request+group)
            var actor = await ctx.Users.FirstAsync();
            var req = new Request { Id = Guid.NewGuid(), RequestNumber = "ZZTEST-B", Title = "B", RequestTypeId = 2, StatusId = STATUS_WAITING_RECEIPT_ID, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1, CreatedAtUtc = DateTime.UtcNow };
            var grp = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, SupplierNameSnapshot = "B", CurrencyCode = "AOA", TotalAmount = 1, Status = RequestConstants.PoGroupStatuses.WaitingReceipt, SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma, OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id };
            ctx.Requests.Add(req); ctx.RequestPoGroups.Add(grp);
            await ctx.SaveChangesAsync();
            seedB = new Seed(req.Id, grp.Id, Array.Empty<Guid>(), actor.Id);
        }

        using (var ctx = NewContext(options))
        {
            var h = BuildRequestsController(ctx, seedA.ActorId);
            // request A + group of request B → mismatch
            var result = await h.Controller.ReopenReceiving(seedA.RequestId, seedB.GroupId, new ReopenReceivingDto { Reason = "x" });
            Assert.IsType<NotFoundObjectResult>(result);
        }
        using (var ctx = NewContext(options))
        {
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seedB.GroupId)).Status);
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seedA.GroupId)).Status);
            Assert.False(await ctx.RequestStatusHistories.AnyAsync(x => x.ActionTaken == "RECEIVING_REOPENED"));
        }
    }

    [Theory]
    [InlineData(RequestConstants.PoGroupStatuses.PaymentCompleted)]
    [InlineData(RequestConstants.PoGroupStatuses.InFollowup)]
    [InlineData(RequestConstants.PoGroupStatuses.WaitingSupplierDelivery)]
    [InlineData(RequestConstants.PoGroupStatuses.Completed)]
    public async Task Reopen_GroupNotWaitingReceipt_409_NoWrites(string groupStatus)
    {
        using var ctx = NewContext();
        // keep the request scalar non-terminal so the GROUP guard is what fires
        var seed = await SeedAsync(ctx, groupStatus, requestStatusId: STATUS_IN_FOLLOWUP_ID);
        var historyBefore = await ctx.RequestStatusHistories.CountAsync();
        var h = BuildRequestsController(ctx, seed.ActorId);

        var result = await ReopenAsync(h.Controller, seed);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(groupStatus, (await SnapshotAsync(ctx, seed)).status);
        Assert.Equal(historyBefore, await ctx.RequestStatusHistories.CountAsync());
        h.Aggregator.Verify(a => a.AggregateRequestStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }

    // ── §3 financial protections ──

    [Fact]
    public async Task Reopen_CompletedRequest_409_NeverReopened()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, RequestConstants.PoGroupStatuses.WaitingReceipt, requestStatusId: STATUS_COMPLETED_ID);
        var h = BuildRequestsController(ctx, seed.ActorId);

        var result = await ReopenAsync(h.Controller, seed);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("terminal", Assert.IsType<ProblemDetails>(conflict.Value).Detail);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, (await SnapshotAsync(ctx, seed)).status);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(x => x.ActionTaken == "RECEIVING_REOPENED"));
    }

    [Fact]
    public async Task Reopen_ActiveSupplierReceipt_409_WithFinanceGuidance_NoWrites()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        await AddAttachmentAsync(ctx, seed.RequestId, seed.ActorId, RequestAttachment.TYPE_RECEIPT);
        var h = BuildRequestsController(ctx, seed.ActorId);

        var result = await ReopenAsync(h.Controller, seed);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Contains("Financeiro", problem.Detail);
        Assert.Contains("remover ou invalidar", problem.Detail);
        var (status, _, stamp) = await SnapshotAsync(ctx, seed);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, status);
        Assert.NotNull(stamp);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(x => x.ActionTaken == "RECEIVING_REOPENED"));
        h.Aggregator.Verify(a => a.AggregateRequestStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false)]   // deleted RECEIPT
    [InlineData(false, true)]   // voided RECEIPT
    public async Task Reopen_InactiveSupplierReceipt_DoesNotBlock(bool deleted, bool voided)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        await AddAttachmentAsync(ctx, seed.RequestId, seed.ActorId, RequestAttachment.TYPE_RECEIPT, deleted: deleted, voided: voided);
        var h = BuildRequestsController(ctx, seed.ActorId);

        Assert.IsType<OkObjectResult>(await ReopenAsync(h.Controller, seed));
        Assert.Equal(RequestConstants.PoGroupStatuses.InFollowup, (await SnapshotAsync(ctx, seed)).status);
    }

    [Theory]
    [InlineData("FISCAL_RECEIPT")]
    [InlineData("RECEIVING_EVIDENCE")]
    [InlineData("PAYMENT_PROOF")]
    public async Task Reopen_OtherDocumentTypes_AreNotSupplierReceipts_DoNotBlock(string typeCode)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        await AddAttachmentAsync(ctx, seed.RequestId, seed.ActorId, typeCode);
        var h = BuildRequestsController(ctx, seed.ActorId);

        Assert.IsType<OkObjectResult>(await ReopenAsync(h.Controller, seed));
        Assert.Equal(RequestConstants.PoGroupStatuses.InFollowup, (await SnapshotAsync(ctx, seed)).status);
    }

    [Fact]
    public async Task Reopen_MultiGroup_TouchesOnlySelectedGroup()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var siblingId = await AddSiblingGroupAsync(ctx, seed, RequestConstants.PoGroupStatuses.WaitingReceipt);
        var h = BuildRequestsController(ctx, seed.ActorId);

        Assert.IsType<OkObjectResult>(await ReopenAsync(h.Controller, seed));

        Assert.Equal(RequestConstants.PoGroupStatuses.InFollowup, (await SnapshotAsync(ctx, seed)).status);
        var sibling = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == siblingId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, sibling.Status);
        Assert.NotNull(sibling.OperationalReceiptCompletedAtUtc);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(x => x.ActionTaken == "RECEIVING_REOPENED"));
    }

    // ── §4 duplicate / repeated reopen is safe ──

    [Fact]
    public async Task Reopen_Duplicate_SecondCallRejected409_SingleAudit()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var h = BuildRequestsController(ctx, seed.ActorId);

        Assert.IsType<OkObjectResult>(await ReopenAsync(h.Controller, seed));
        Assert.IsType<ConflictObjectResult>(await ReopenAsync(h.Controller, seed));

        Assert.Equal(RequestConstants.PoGroupStatuses.InFollowup, (await SnapshotAsync(ctx, seed)).status);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(x => x.ActionTaken == "RECEIVING_REOPENED"));
        h.Aggregator.Verify(a => a.AggregateRequestStatusAsync(seed.RequestId, seed.ActorId, It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    // ═══════════════════════════ §2/§4 the full correction loop ═══════════════════════════

    [Fact]
    public async Task Loop_Reopen_ReduceQuantity_CannotReachWaitingReceipt_WhileIncomplete()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options), RequestConstants.PoGroupStatuses.WaitingReceipt, new[] { 2m, 2m });

        using (var ctx = NewContext(options))
            Assert.IsType<OkObjectResult>(await ReopenAsync(BuildRequestsController(ctx, seed.ActorId).Controller, seed));

        // correction: item 1 was actually not received (2 → 0) — now allowed, audited
        using (var ctx = NewContext(options))
            Assert.IsType<NoContentResult>(await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[0], 0m));

        using (var ctx = NewContext(options))
        {
            Assert.True(await ctx.RequestStatusHistories.AnyAsync(x => x.ActionTaken == "ITEM_RECEIVING_ADJUSTMENT"));
            // confirming an incomplete group never yields WAITING_RECEIPT
            await ConfirmAsync(BuildRequestsController(ctx, seed.ActorId).Controller, seed);
        }
        using (var ctx = NewContext(options))
        {
            var (status, qty, stamp) = await SnapshotAsync(ctx, seed);
            Assert.NotEqual(RequestConstants.PoGroupStatuses.WaitingReceipt, status);
            Assert.Equal(new[] { 0m, 2m }, qty);
            Assert.Null(stamp);
        }
    }

    [Fact]
    public async Task Loop_Reopen_Correct_ConfirmAgain_ReturnsToWaitingReceipt_WithSecondConfirmation()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options), RequestConstants.PoGroupStatuses.WaitingReceipt, new[] { 2m, 2m });

        using (var ctx = NewContext(options))
            Assert.IsType<OkObjectResult>(await ReopenAsync(BuildRequestsController(ctx, seed.ActorId).Controller, seed));

        // correction down (2 → 1) then the real delivery completes (1 → 2)
        using (var ctx = NewContext(options))
            Assert.IsType<NoContentResult>(await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[1], 1m));
        using (var ctx = NewContext(options))
            Assert.IsType<NoContentResult>(await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[1], 2m));

        using (var ctx = NewContext(options))
            Assert.IsType<OkObjectResult>(await ConfirmAsync(BuildRequestsController(ctx, seed.ActorId).Controller, seed));

        using (var ctx = NewContext(options))
        {
            var (status, qty, _) = await SnapshotAsync(ctx, seed);
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, status);
            Assert.Equal(new[] { 2m, 2m }, qty);
            // original confirmation + the NEW one; the reopen and the adjustment are all on record
            Assert.Equal(2, await ctx.RequestStatusHistories.CountAsync(x => x.ActionTaken == "CONFIRM_RECEIVING"));
            Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(x => x.ActionTaken == "RECEIVING_REOPENED"));
            Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(x => x.ActionTaken == "ITEM_RECEIVING_ADJUSTMENT"));
            Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(x => x.ActionTaken == "ITEM_RECEIVING_REGISTRATION"));
            // and the group is frozen again
            Assert.IsType<ConflictObjectResult>(await RegisterAsync(BuildLineItemsController(ctx, seed.ActorId), seed.ItemIds[1], 1m));
        }
    }

    // ── the canonical evaluator itself ──

    [Fact]
    public void Evaluator_CanRegisterItemReceipt_IsPreConfirmationOnly()
    {
        Assert.True(ReceivingActionEvaluator.CanRegisterItemReceipt("PAYMENT_COMPLETED"));
        Assert.True(ReceivingActionEvaluator.CanRegisterItemReceipt("IN_FOLLOWUP"));
        Assert.True(ReceivingActionEvaluator.CanRegisterItemReceipt("WAITING_SUPPLIER_DELIVERY"));
        Assert.False(ReceivingActionEvaluator.CanRegisterItemReceipt("WAITING_RECEIPT"));
        Assert.False(ReceivingActionEvaluator.CanRegisterItemReceipt("WAITING_FISCAL_RECEIPT"));
        Assert.False(ReceivingActionEvaluator.CanRegisterItemReceipt("COMPLETED"));
        Assert.False(ReceivingActionEvaluator.CanRegisterItemReceipt("PENDING"));
        Assert.False(ReceivingActionEvaluator.CanRegisterItemReceipt(null));
        // still receiving-accessible (queue) but not registrable
        Assert.True(ReceivingActionEvaluator.IsReceivingActionable("WAITING_RECEIPT"));
    }
}
