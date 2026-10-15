using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// docs §G.20 D1 — the Finance-return notification reaches the Buyer who OWNS the returned P.O.: PAYMENT → the
/// acted-on group's PoResponsibleBuyerId (never Request.BuyerId, never a sibling group's owner, never every Buyer);
/// QUOTATION → Request.BuyerId as before. Real FinanceController + real WorkflowNotificationOrchestrator on InMemory;
/// assertions read the ACTUAL recipients (EmailOutbox rows and in-app creations) and their correlation to the
/// FINANCE_RETURN_ADJUSTMENT history row.
/// </summary>
public class FinanceReturnNotificationRecipientTests
{
    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private sealed class Seed
    {
        public Guid RequestId, GroupA, GroupB, Finance, OwnerA, OwnerB, RequestBuyer, InactiveOwner;
        public string OwnerAEmail = "", OwnerBEmail = "", RequestBuyerEmail = "";
    }

    private static void SeedLookups(ApplicationDbContext ctx)
    {
        ctx.RequestTypes.AddRange(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" }, new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        var codes = new[]
        {
            RequestConstants.Statuses.WaitingQuotation, RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.WaitingFinalApproval,
            RequestConstants.Statuses.FinalApproved, RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PoPartiallyUploaded,
            RequestConstants.Statuses.PaymentRequestSent, RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.Paid, RequestConstants.Statuses.WaitingReceipt, RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.Completed,
            RequestConstants.Statuses.QuotationCompleted, RequestConstants.PoGroupStatuses.WaitingPo, RequestConstants.Statuses.WaitingPoCorrection,
            RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentScheduled, RequestConstants.Statuses.AdvancePaymentCompleted,
            RequestConstants.Statuses.WaitingSupplierDelivery, RequestConstants.Statuses.WaitingReconciliation, RequestConstants.Statuses.Cancelled, RequestConstants.Statuses.Rejected
        }.Distinct().ToArray();
        var id = 1;
        foreach (var code in codes) ctx.RequestStatuses.Add(new RequestStatus { Id = id++, Code = code, Name = code, DisplayOrder = id });
        ctx.Companies.Add(new Company { Id = 1, Name = "Alpla", IsActive = true });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Planta 1", CompanyId = 1 });
    }

    /// <summary>PAYMENT (or QUOTATION) request with two PO_ISSUED groups: A owned by OwnerA, B owned by <paramref name="groupBOwner"/> (default OwnerB).</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string type = "PAYMENT", bool requestBuyerSet = false, Func<Seed, Guid?>? groupBOwner = null)
    {
        SeedLookups(ctx);
        var s = new Seed { RequestId = Guid.NewGuid(), GroupA = Guid.NewGuid(), GroupB = Guid.NewGuid(), Finance = Guid.NewGuid(), OwnerA = Guid.NewGuid(), OwnerB = Guid.NewGuid(), RequestBuyer = Guid.NewGuid(), InactiveOwner = Guid.NewGuid() };
        s.OwnerAEmail = "owner.a@test.local"; s.OwnerBEmail = "owner.b@test.local"; s.RequestBuyerEmail = "request.buyer@test.local";
        ctx.Users.AddRange(
            new User { Id = s.Finance, FullName = "Finanças", Email = "finance@test.local", IsActive = true },
            new User { Id = s.OwnerA, FullName = "Comprador A", Email = s.OwnerAEmail, IsActive = true },
            new User { Id = s.OwnerB, FullName = "Comprador B", Email = s.OwnerBEmail, IsActive = true },
            new User { Id = s.RequestBuyer, FullName = "Comprador do Pedido", Email = s.RequestBuyerEmail, IsActive = true },
            new User { Id = s.InactiveOwner, FullName = "Comprador Inativo", Email = "inactive@test.local", IsActive = false });
        var poIssued = ctx.RequestStatuses.Local.Single(x => x.Code == RequestConstants.Statuses.PoIssued).Id;
        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = "REQ-429", Title = "Devolução", RequestTypeId = type == "PAYMENT" ? 2 : 1, StatusId = poIssued,
            RequesterId = s.Finance, BuyerId = requestBuyerSet ? s.RequestBuyer : null, DepartmentId = 7, CompanyId = 1, PlantId = 1,
            PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, EstimatedTotalAmount = 100m, CreatedAtUtc = DateTime.UtcNow.AddDays(-10), UpdatedAtUtc = DateTime.UtcNow
        });
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = s.GroupA, RequestId = s.RequestId, SupplierNameSnapshot = "Fornecedor A", Status = RequestConstants.PoGroupStatuses.PoIssued, TotalAmount = 60m, CurrencyCode = "AOA", PurchaseOrderNumber = "PO-A", PoResponsibleBuyerId = s.OwnerA, CreatedByUserId = s.OwnerA, CreatedAtUtc = DateTime.UtcNow.AddDays(-5) },
            new RequestPoGroup { Id = s.GroupB, RequestId = s.RequestId, SupplierNameSnapshot = "Fornecedor B", Status = RequestConstants.PoGroupStatuses.PoIssued, TotalAmount = 40m, CurrencyCode = "AOA", PurchaseOrderNumber = "PO-B", PoResponsibleBuyerId = groupBOwner != null ? groupBOwner(s) : s.OwnerB, CreatedByUserId = s.OwnerB, CreatedAtUtc = DateTime.UtcNow.AddDays(-5) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return s;
    }

    private static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    private static (WorkflowNotificationOrchestrator Orchestrator, Mock<INotificationService> InApp) RealOrchestrator(ApplicationDbContext ctx)
    {
        var inApp = new Mock<INotificationService>();
        inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        var email = new Mock<IEmailService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var routing = new Mock<IApprovalRoutingService>();
        return (new WorkflowNotificationOrchestrator(ctx, inApp.Object, email.Object, config, NullLogger<WorkflowNotificationOrchestrator>.Instance, AdminLog().Object, routing.Object), inApp);
    }

    private static FinanceController FinanceCtl(ApplicationDbContext ctx, Guid actorId, IWorkflowNotificationOrchestrator orchestrator)
    {
        var controller = new FinanceController(ctx, orchestrator, NullLogger<FinanceController>.Instance, new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance), new FinancePaymentEligibilityService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.Finance), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")) }
        };
        return controller;
    }

    private static async Task<Guid> ReturnAsync(ApplicationDbContext ctx, Seed s, Guid groupId, IWorkflowNotificationOrchestrator orchestrator)
    {
        var res = await FinanceCtl(ctx, s.Finance, orchestrator).ReturnForAdjustment(s.RequestId, new FinanceActionRequestDto { RequestPoGroupId = groupId, Notes = "NIF do fornecedor errado" });
        Assert.True(res is OkObjectResult or OkResult or NoContentResult, $"return refused: {(res as ObjectResult)?.Value}");
        ctx.ChangeTracker.Clear();
        var returnRow = await ctx.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == s.RequestId && h.ActionTaken == "FINANCE_RETURN_ADJUSTMENT")
            .OrderByDescending(h => h.CreatedAtUtc).FirstAsync();
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupId)).Status);
        return returnRow.Id;
    }

    private static async Task<List<EmailOutboxEntry>> OutboxAsync(ApplicationDbContext ctx, Guid requestId)
        => await ctx.EmailOutbox.AsNoTracking().Where(e => e.RequestId == requestId).ToListAsync();

    private static void AssertInAppOnlyFor(Mock<INotificationService> inApp, Guid userId, Guid correlationId)
    {
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), correlationId, It.IsAny<string?>()), Times.Once);
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.Is<Guid>(u => u != userId), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Payment_return_notifies_the_owner_of_the_acted_on_group_only_with_the_return_row_as_correlation()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "PAYMENT");
        var (orchestrator, inApp) = RealOrchestrator(ctx);

        var returnB = await ReturnAsync(ctx, s, s.GroupB, orchestrator);
        var outbox = await OutboxAsync(ctx, s.RequestId);
        var row = Assert.Single(outbox);
        Assert.Equal(s.OwnerBEmail, row.RecipientEmail);
        Assert.Equal(returnB, row.CorrelationId);
        Assert.Equal(WorkflowEventCodes.FinanceReturned, row.EventCode);
        Assert.Contains($"/requests/{s.RequestId}", row.ActionUrl);   // the detail, where CORRIGIR P.O for the returned group lives
        AssertInAppOnlyFor(inApp, s.OwnerB, returnB);

        // Distinct owners: returning the sibling afterwards reaches ITS owner, with its own correlation.
        inApp.Invocations.Clear();
        var returnA = await ReturnAsync(ctx, s, s.GroupA, orchestrator);
        outbox = await OutboxAsync(ctx, s.RequestId);
        Assert.Equal(2, outbox.Count);
        var rowA = Assert.Single(outbox, e => e.CorrelationId == returnA);
        Assert.Equal(s.OwnerAEmail, rowA.RecipientEmail);
        Assert.NotEqual(returnA, returnB);
        AssertInAppOnlyFor(inApp, s.OwnerA, returnA);
    }

    [Fact]
    public async Task Payment_group_owner_wins_over_a_request_level_buyer_when_they_disagree()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "PAYMENT", requestBuyerSet: true);
        var (orchestrator, inApp) = RealOrchestrator(ctx);
        var returnB = await ReturnAsync(ctx, s, s.GroupB, orchestrator);
        var row = Assert.Single(await OutboxAsync(ctx, s.RequestId));
        Assert.Equal(s.OwnerBEmail, row.RecipientEmail);
        Assert.DoesNotContain(await OutboxAsync(ctx, s.RequestId), e => e.RecipientEmail == s.RequestBuyerEmail);
        AssertInAppOnlyFor(inApp, s.OwnerB, returnB);
    }

    [Fact]
    public async Task Quotation_return_keeps_notifying_the_request_buyer_and_ignores_the_group_owner()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "QUOTATION", requestBuyerSet: true);
        var (orchestrator, inApp) = RealOrchestrator(ctx);
        var returnB = await ReturnAsync(ctx, s, s.GroupB, orchestrator);
        var row = Assert.Single(await OutboxAsync(ctx, s.RequestId));
        Assert.Equal(s.RequestBuyerEmail, row.RecipientEmail);
        Assert.Equal(returnB, row.CorrelationId);
        AssertInAppOnlyFor(inApp, s.RequestBuyer, returnB);
    }

    [Theory]
    [InlineData("unassigned")]   // PAYMENT group without PoResponsibleBuyerId
    [InlineData("inactive")]     // owner exists but IsActive = false
    [InlineData("quotationNoBuyer")] // QUOTATION without Request.BuyerId (pre-existing behaviour preserved)
    public async Task No_eligible_owner_means_no_recipient_and_nothing_is_invented(string shape)
    {
        await using var ctx = NewCtx();
        var s = shape switch
        {
            "unassigned" => await SeedAsync(ctx, "PAYMENT", requestBuyerSet: true, groupBOwner: _ => null),
            "inactive" => await SeedAsync(ctx, "PAYMENT", groupBOwner: x => x.InactiveOwner),
            _ => await SeedAsync(ctx, "QUOTATION", requestBuyerSet: false)
        };
        var (orchestrator, inApp) = RealOrchestrator(ctx);
        await ReturnAsync(ctx, s, s.GroupB, orchestrator);
        Assert.Empty(await OutboxAsync(ctx, s.RequestId));
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
        // The sibling's owner (OwnerA) and the request-level buyer were never used as a fallback.
    }

    [Fact]
    public async Task Invalid_or_missing_group_context_on_a_payment_return_resolves_nobody()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, "PAYMENT", requestBuyerSet: true);
        // A second request whose group belongs to someone else.
        var otherRequest = Guid.NewGuid(); var otherGroup = Guid.NewGuid();
        var poIssued = (await ctx.RequestStatuses.AsNoTracking().SingleAsync(x => x.Code == RequestConstants.Statuses.PoIssued)).Id;
        ctx.Requests.Add(new Request { Id = otherRequest, RequestNumber = "REQ-254", Title = "Outro", RequestTypeId = 2, StatusId = poIssued, RequesterId = s.Finance, DepartmentId = 4, CompanyId = 1, PlantId = 1, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = otherGroup, RequestId = otherRequest, SupplierNameSnapshot = "X", Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection, TotalAmount = 1m, CurrencyCode = "AOA", PoResponsibleBuyerId = s.OwnerA, CreatedByUserId = s.OwnerA, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var (orchestrator, inApp) = RealOrchestrator(ctx);

        WorkflowEvent Evt(Guid? poGroupId) => new()
        {
            EventCode = WorkflowEventCodes.FinanceReturned, RequestId = s.RequestId, RequestNumber = "REQ-429", RequestTitle = "t",
            TargetStatusCode = "WAITING_PO_CORRECTION", ActionTaken = "FINANCE_RETURN_ADJUSTMENT", ActorUserId = s.Finance, ActorName = "Finanças",
            CorrelationId = Guid.NewGuid(), RequesterId = s.Finance, BuyerId = s.RequestBuyer, PlantId = 1, PoGroupId = poGroupId
        };
        await orchestrator.EmitAsync(Evt(otherGroup));   // group of ANOTHER request → refused, not OwnerA
        await orchestrator.EmitAsync(Evt(null));         // no group context on a PAYMENT → nobody (Request.BuyerId is not a fallback)
        Assert.Empty(await OutboxAsync(ctx, s.RequestId));
        inApp.Verify(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }
}
