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
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Approvals;
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
/// Phase 3E.1 follow-up — the close-not-quoted endpoint. AUTHORIZATION: System Administrator admin
/// override + Buyer ownership (assigned or unassigned); everyone else denied. PARENT STATUS: closing a
/// line item must NOT arbitrarily COMPLETE the request — it completes only in the true terminal case (all
/// items closed-not-quoted, no active batches, no active PO groups), using the REAL status sync service.
/// </summary>
public class CloseNotQuotedEndpointTests
{
    private const int QuotationTypeId = 1;
    private static readonly string ValidJustification = new string('x', 25);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static void SeedLookups(ApplicationDbContext ctx)
    {
        ctx.RequestTypes.Add(new RequestType { Id = QuotationTypeId, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 1, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Aguardando Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 2, Code = RequestConstants.Statuses.Completed, Name = "Concluído" });
        ctx.Companies.Add(new Company { Id = 1, Name = "ALPLA" });
        ctx.Departments.Add(new Department { Id = 1, Name = "TI" });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 1", CompanyId = 1 });
    }

    private static async Task<Guid> SeedUserAsync(ApplicationDbContext ctx, string name = "User")
    {
        var id = Guid.NewGuid();
        ctx.Users.Add(new User { Id = id, FullName = name, Email = $"{Guid.NewGuid():N}@t.local", IsActive = true });
        await ctx.SaveChangesAsync();
        return id;
    }

    private static Request SeedRequest(ApplicationDbContext ctx, Guid? buyerId, Guid creator)
    {
        var req = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = $"REQ-{Guid.NewGuid():N}".Substring(0, 12), Title = "t", Description = "d",
            RequestTypeId = QuotationTypeId, StatusId = 1, RequesterId = creator, CreatedByUserId = creator,
            BuyerId = buyerId, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(req);
        return req;
    }

    private static RequestLineItem SeedItem(ApplicationDbContext ctx, Request req, int line, string? lifecycle)
    {
        var li = new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = line, Description = $"Item {line}", Quantity = 1, UnitPrice = 10, TotalAmount = 10, QuotationLifecycleStatus = lifecycle };
        ctx.RequestLineItems.Add(li);
        return li;
    }

    private static RequestsController Build(ApplicationDbContext ctx, Guid actorId, string role)
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
            new RequestStatusSyncService(ctx, NullLogger<RequestStatusSyncService>.Instance), // REAL — genuine recalc
            new Mock<IApprovalRoutingService>().Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object,
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(new PostPaymentCompletionOptions { Enabled = true, CompletionEnabled = false, EffectiveDateUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) }));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, role)
                }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private static CloseNotQuotedDto Dto() => new() { ReasonCode = "Item não é mais necessário", Justification = ValidJustification };

    private static async Task<string> StatusCodeAsync(ApplicationDbContext ctx, Guid requestId) =>
        (await ctx.Requests.Include(r => r.Status).AsNoTracking().FirstAsync(r => r.Id == requestId)).Status!.Code;

    // ──────────────── authorization ────────────────

    [Fact]
    public async Task AssignedBuyer_CanClose()
    {
        using var ctx = NewContext(); SeedLookups(ctx);
        var buyer = await SeedUserAsync(ctx, "Buyer");
        var req = SeedRequest(ctx, buyer, buyer);
        var li = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, buyer, RoleConstants.Buyer).CloseNotQuoted(req.Id, li.Id, Dto());
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted,
            (await ctx.RequestLineItems.AsNoTracking().FirstAsync(x => x.Id == li.Id)).QuotationLifecycleStatus);
    }

    [Fact]
    public async Task SystemAdministrator_CanClose_OnAnotherBuyersRequest_AdminOverride()
    {
        using var ctx = NewContext(); SeedLookups(ctx);
        var owner = await SeedUserAsync(ctx, "Owner");
        var admin = await SeedUserAsync(ctx, "Admin");
        var req = SeedRequest(ctx, owner, owner); // assigned to someone else
        var li = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, admin, RoleConstants.SystemAdministrator).CloseNotQuoted(req.Id, li.Id, Dto());
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task NonAssignedBuyer_IsDenied()
    {
        using var ctx = NewContext(); SeedLookups(ctx);
        var owner = await SeedUserAsync(ctx, "Owner");
        var other = await SeedUserAsync(ctx, "Other");
        var req = SeedRequest(ctx, owner, owner);
        var li = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, other, RoleConstants.Buyer).CloseNotQuoted(req.Id, li.Id, Dto());
        Assert.Equal(403, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task NonBuyerNonAdmin_IsDenied()
    {
        using var ctx = NewContext(); SeedLookups(ctx);
        var actor = await SeedUserAsync(ctx, "Finance");
        var req = SeedRequest(ctx, null, actor);
        var li = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, actor, RoleConstants.Finance).CloseNotQuoted(req.Id, li.Id, Dto());
        Assert.Equal(403, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task Justification_TooShort_IsRejected()
    {
        using var ctx = NewContext(); SeedLookups(ctx);
        var buyer = await SeedUserAsync(ctx, "Buyer");
        var req = SeedRequest(ctx, buyer, buyer);
        var li = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, buyer, RoleConstants.Buyer)
            .CloseNotQuoted(req.Id, li.Id, new CloseNotQuotedDto { ReasonCode = "Outro", Justification = "curta" });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ──────────────── parent status (no over-completion) ────────────────

    [Fact]
    public async Task ClosingOnePending_ManyRemain_DoesNotComplete()
    {
        using var ctx = NewContext(); SeedLookups(ctx);
        var buyer = await SeedUserAsync(ctx, "Buyer");
        var req = SeedRequest(ctx, buyer, buyer);
        var target = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        for (int i = 2; i <= 19; i++) SeedItem(ctx, req, i, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        await Build(ctx, buyer, RoleConstants.Buyer).CloseNotQuoted(req.Id, target.Id, Dto());

        Assert.NotEqual(RequestConstants.Statuses.Completed, await StatusCodeAsync(ctx, req.Id)); // still needs quotation
        Assert.Equal(18, await ctx.RequestLineItems.CountAsync(x => x.RequestId == req.Id && x.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending));
    }

    [Fact]
    public async Task ClosingLastPending_WithApprovedSibling_DoesNotComplete()
    {
        using var ctx = NewContext(); SeedLookups(ctx);
        var buyer = await SeedUserAsync(ctx, "Buyer");
        var req = SeedRequest(ctx, buyer, buyer);
        var pending = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        SeedItem(ctx, req, 2, RequestConstants.QuotationLifecycleStatuses.QuotationApproved); // downstream sibling
        await ctx.SaveChangesAsync();

        await Build(ctx, buyer, RoleConstants.Buyer).CloseNotQuoted(req.Id, pending.Id, Dto());

        Assert.NotEqual(RequestConstants.Statuses.Completed, await StatusCodeAsync(ctx, req.Id));
    }

    [Fact]
    public async Task ClosingLastPending_WithActivePoGroupSibling_DoesNotComplete()
    {
        using var ctx = NewContext(); SeedLookups(ctx);
        var buyer = await SeedUserAsync(ctx, "Buyer");
        var req = SeedRequest(ctx, buyer, buyer);
        var pending = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        SeedItem(ctx, req, 2, RequestConstants.QuotationLifecycleStatuses.QuotationApproved);
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, Status = RequestConstants.PoGroupStatuses.PoIssued, CurrencyCode = "AOA", TotalAmount = 100, CreatedByUserId = buyer, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        await Build(ctx, buyer, RoleConstants.Buyer).CloseNotQuoted(req.Id, pending.Id, Dto());

        Assert.NotEqual(RequestConstants.Statuses.Completed, await StatusCodeAsync(ctx, req.Id));
    }

    [Fact]
    public async Task ClosingLastItem_AllClosed_NoDownstream_Completes()
    {
        using var ctx = NewContext(); SeedLookups(ctx);
        var buyer = await SeedUserAsync(ctx, "Buyer");
        var req = SeedRequest(ctx, buyer, buyer);
        var only = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        await Build(ctx, buyer, RoleConstants.Buyer).CloseNotQuoted(req.Id, only.Id, Dto());

        Assert.Equal(RequestConstants.Statuses.Completed, await StatusCodeAsync(ctx, req.Id)); // true terminal case
    }
}
