using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Receiving;

/// <summary>
/// v2.245.4 — RUNTIME activation of the payment-group-item-linkage endpoint: the real
/// <see cref="AdminRepairsController"/> instantiates the service the way every other repair does and
/// resolves <see cref="IStatusAggregationService"/> from <c>HttpContext.RequestServices</c> (registered
/// scoped in Program.cs). Proves the SysAdmin guard, the reason gate, a zero-write preview and a real apply.
/// </summary>
public class AdminRepairsPaymentGroupItemLinkageEndpointTests
{
    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;

    private static AdminRepairsController BuildController(ApplicationDbContext ctx, Guid actorId, string role)
    {
        var services = new ServiceCollection();
        services.AddScoped<IStatusAggregationService>(_ =>
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));
        var controller = new AdminRepairsController(ctx)
        {
            ControllerContext = new ControllerContext
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
            }
        };
        return controller;
    }

    /// <summary>A single-group PAYMENT request at PAYMENT_COMPLETED whose two items are unlinked.</summary>
    private static async Task<(Guid requestId, Guid groupId, Guid actorId)> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "SysAdmin", Email = $"sa-{Guid.NewGuid():N}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = "PAYMENT", Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 14, Code = "PAYMENT_COMPLETED", Name = "Pagamento Realizado" });
        ctx.LineItemStatuses.Add(new LineItemStatus { Id = 93, Code = "PENDING", Name = "Pendente" });
        var reqId = Guid.NewGuid(); var groupId = Guid.NewGuid();
        ctx.Requests.Add(new Request
        {
            Id = reqId, RequestNumber = "ZZTEST-EP-" + Guid.NewGuid().ToString("N")[..6], Title = "ep", StatusId = 14,
            RequestTypeId = 2, DepartmentId = 1, CompanyId = 1, RequesterId = actor.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
        });
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = groupId, RequestId = reqId, Status = "PAYMENT_COMPLETED", SupplierNameSnapshot = "Forn", SupplierId = 7,
            CreatedByUserId = actor.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-9),
        });
        for (var i = 1; i <= 2; i++)
            ctx.RequestLineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(), RequestId = reqId, RequestPoGroupId = null, LineNumber = i, Description = $"i{i}",
                Quantity = 1, LineItemStatusId = 93,
            });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return (reqId, groupId, actor.Id);
    }

    [Fact]
    public async Task NonSysAdmin_IsForbidden()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (_, _, actorId) = await SeedAsync(ctx);
        var result = await BuildController(ctx, actorId, RoleConstants.Finance).PaymentGroupItemLinkage(confirm: false);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Preview_ResolvesAtRuntime_WritesNothing()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, _, actorId) = await SeedAsync(ctx);

        var result = await BuildController(ctx, actorId, RoleConstants.SystemAdministrator).PaymentGroupItemLinkage(confirm: false);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<PaymentGroupItemLinkageRepairResultDto>(ok.Value);
        Assert.Equal("PREVIEW", dto.Status);
        Assert.Equal(1, dto.WouldRepair);
        Assert.True(await ctx.RequestLineItems.AsNoTracking().AllAsync(li => li.RequestId != reqId || li.RequestPoGroupId == null));
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "PAYMENT_GROUP_ITEM_LINK_REPAIR"));
    }

    [Fact]
    public async Task Apply_WithoutReason_IsRefused_WritesNothing()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, _, actorId) = await SeedAsync(ctx);
        var controller = BuildController(ctx, actorId, RoleConstants.SystemAdministrator);

        Assert.IsType<BadRequestObjectResult>(await controller.PaymentGroupItemLinkage(confirm: true, body: null));
        Assert.IsType<BadRequestObjectResult>(await controller.PaymentGroupItemLinkage(confirm: true,
            body: new PaymentGroupItemLinkageRepairRequest { Reason = "   " }));
        Assert.True(await ctx.RequestLineItems.AsNoTracking().AllAsync(li => li.RequestId != reqId || li.RequestPoGroupId == null));
    }

    [Fact]
    public async Task Apply_WithReason_AndExplicitGlobalScope_RunsEndToEnd_LinksItems()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, groupId, actorId) = await SeedAsync(ctx);

        var result = await BuildController(ctx, actorId, RoleConstants.SystemAdministrator)
            .PaymentGroupItemLinkage(confirm: true, scope: "all", body: new PaymentGroupItemLinkageRepairRequest { Reason = "TEST" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<PaymentGroupItemLinkageRepairResultDto>(ok.Value);
        Assert.Equal("APPLIED", dto.Status);
        Assert.Equal("ALL", dto.Scope);
        Assert.Equal(1, dto.Repaired);
        Assert.Equal(0, dto.Errors);
        var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == reqId).ToListAsync();
        Assert.All(items, li => Assert.Equal(groupId, li.RequestPoGroupId));
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == reqId && h.ActionTaken == "PAYMENT_GROUP_ITEM_LINK_REPAIR"));
    }

    // ── v2.245.10: the global APPLY can no longer run by omission ──

    private static async Task AssertUntouchedAsync(ApplicationDbContext ctx, Guid reqId)
    {
        Assert.True(await ctx.RequestLineItems.AsNoTracking().AllAsync(li => li.RequestId != reqId || li.RequestPoGroupId == null));
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "PAYMENT_GROUP_ITEM_LINK_REPAIR"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("request")]
    [InlineData("ALL ")]
    public async Task GlobalApply_WithoutExplicitScopeAll_IsRefused_WritesNothing(string? scope)
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, _, actorId) = await SeedAsync(ctx);
        var result = await BuildController(ctx, actorId, RoleConstants.SystemAdministrator)
            .PaymentGroupItemLinkage(confirm: true, scope: scope, body: new PaymentGroupItemLinkageRepairRequest { Reason = "TEST" });
        Assert.IsType<BadRequestObjectResult>(result);
        await AssertUntouchedAsync(ctx, reqId);
    }

    [Fact]
    public async Task GlobalRoute_WithRequestIdQuery_IsRefused_NeverWidened_PreviewOrApply()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, _, actorId) = await SeedAsync(ctx);
        var controller = BuildController(ctx, actorId, RoleConstants.SystemAdministrator);

        Assert.IsType<BadRequestObjectResult>(await controller.PaymentGroupItemLinkage(confirm: false, requestId: reqId));
        Assert.IsType<BadRequestObjectResult>(await controller.PaymentGroupItemLinkage(
            confirm: true, scope: "all", requestId: reqId, body: new PaymentGroupItemLinkageRepairRequest { Reason = "TEST" }));
        Assert.IsType<BadRequestObjectResult>(await controller.PaymentGroupItemLinkage(
            confirm: true, scope: "all", requestId: Guid.NewGuid(), body: new PaymentGroupItemLinkageRepairRequest { Reason = "TEST" }));
        await AssertUntouchedAsync(ctx, reqId);
    }

    [Fact]
    public async Task GlobalPreview_StaysReadOnly_WithoutScope()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, _, actorId) = await SeedAsync(ctx);
        var ok = Assert.IsType<OkObjectResult>(await BuildController(ctx, actorId, RoleConstants.SystemAdministrator).PaymentGroupItemLinkage(confirm: false));
        Assert.Equal("ALL", Assert.IsType<PaymentGroupItemLinkageRepairResultDto>(ok.Value).Scope);
        await AssertUntouchedAsync(ctx, reqId);
    }

    // ── v2.245.10: the single-request route ──

    [Fact]
    public async Task ScopedRoute_IsGuidConstrained_SoAMalformedIdCanNeverReachTheGlobalAction()
    {
        var scoped = typeof(AdminRepairsController).GetMethod(nameof(AdminRepairsController.PaymentGroupItemLinkageForRequest))!;
        var template = Assert.Single(scoped.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>()).Template;
        Assert.Equal("payment-group-item-linkage/{requestId:guid}", template);

        var global = typeof(AdminRepairsController).GetMethod(nameof(AdminRepairsController.PaymentGroupItemLinkage))!;
        Assert.Equal("payment-group-item-linkage", Assert.Single(global.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>()).Template);
        // the global action binds requestId only to refuse it — never as a scope
        Assert.Contains(global.GetParameters(), p => p.Name == "requestId" && p.ParameterType == typeof(Guid?));
    }

    [Fact]
    public async Task Scoped_NonSysAdmin_IsForbidden_PreviewAndApply()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, groupId, actorId) = await SeedAsync(ctx);
        var controller = BuildController(ctx, actorId, RoleConstants.Finance);
        Assert.IsType<ForbidResult>(await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: false));
        Assert.IsType<ForbidResult>(await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: true,
            body: new PaymentGroupItemLinkageScopedRepairRequest { Reason = "x", ExpectedPoGroupId = groupId, ExpectedDecision = "REPAIR_LINK" }));
        await AssertUntouchedAsync(ctx, reqId);
    }

    [Fact]
    public async Task ScopedPreview_ReturnsOnlyThatRequest_WritesNothing_UnknownIs404()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, groupId, actorId) = await SeedAsync(ctx);
        var controller = BuildController(ctx, actorId, RoleConstants.SystemAdministrator);

        var ok = Assert.IsType<OkObjectResult>(await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: false));
        var dto = Assert.IsType<PaymentGroupItemLinkageRepairResultDto>(ok.Value);
        Assert.Equal("PREVIEW", dto.Status);
        Assert.Equal("REQUEST", dto.Scope);
        Assert.Equal(reqId.ToString(), dto.RequestId);
        Assert.Equal(1, dto.ScannedRequests);
        var row = Assert.Single(dto.Rows);
        Assert.Equal(groupId.ToString(), row.PoGroupId);
        Assert.Equal("REPAIR_LINK", row.Decision);

        Assert.IsType<NotFoundObjectResult>(await controller.PaymentGroupItemLinkageForRequest(Guid.NewGuid(), confirm: false));
        await AssertUntouchedAsync(ctx, reqId);
    }

    [Fact]
    public async Task ScopedApply_MissingReasonOrFacts_IsRefused_WritesNothing()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, groupId, actorId) = await SeedAsync(ctx);
        var controller = BuildController(ctx, actorId, RoleConstants.SystemAdministrator);

        Assert.IsType<BadRequestObjectResult>(await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: true, body: null));
        Assert.IsType<BadRequestObjectResult>(await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: true,
            body: new PaymentGroupItemLinkageScopedRepairRequest { Reason = "  ", ExpectedPoGroupId = groupId, ExpectedDecision = "REPAIR_LINK" }));
        Assert.IsType<BadRequestObjectResult>(await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: true,
            body: new PaymentGroupItemLinkageScopedRepairRequest { Reason = "r", ExpectedPoGroupId = null, ExpectedDecision = "REPAIR_LINK" }));
        Assert.IsType<BadRequestObjectResult>(await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: true,
            body: new PaymentGroupItemLinkageScopedRepairRequest { Reason = "r", ExpectedPoGroupId = groupId, ExpectedDecision = "" }));
        await AssertUntouchedAsync(ctx, reqId);
    }

    [Fact]
    public async Task ScopedApply_FactsMismatch_Is409_WritesNothing()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, groupId, actorId) = await SeedAsync(ctx);
        var controller = BuildController(ctx, actorId, RoleConstants.SystemAdministrator);

        var wrongGroup = await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: true,
            body: new PaymentGroupItemLinkageScopedRepairRequest { Reason = "r", ExpectedPoGroupId = Guid.NewGuid(), ExpectedDecision = "REPAIR_LINK" });
        var conflict = Assert.IsType<ConflictObjectResult>(wrongGroup);
        Assert.Equal("REFUSED", Assert.Single(Assert.IsType<PaymentGroupItemLinkageRepairResultDto>(conflict.Value).Rows).Decision);

        var wrongDecision = await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: true,
            body: new PaymentGroupItemLinkageScopedRepairRequest { Reason = "r", ExpectedPoGroupId = groupId, ExpectedDecision = "REPAIR_LINK_AND_DEMOTE" });
        Assert.IsType<ConflictObjectResult>(wrongDecision);
        await AssertUntouchedAsync(ctx, reqId);
    }

    [Fact]
    public async Task ScopedApply_WithPreviewFacts_RepairsOnlyThatRequest_AndIsIdempotent()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, groupId, actorId) = await SeedAsync(ctx);
        var controller = BuildController(ctx, actorId, RoleConstants.SystemAdministrator);
        var body = new PaymentGroupItemLinkageScopedRepairRequest { Reason = "REQ scoped", ExpectedPoGroupId = groupId, ExpectedDecision = "REPAIR_LINK" };

        var ok = Assert.IsType<OkObjectResult>(await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: true, body: body));
        var dto = Assert.IsType<PaymentGroupItemLinkageRepairResultDto>(ok.Value);
        Assert.Equal("APPLIED", dto.Status);
        Assert.Equal("REQUEST", dto.Scope);
        Assert.Equal(1, dto.Repaired);
        Assert.Equal("REPAIRED_LINK", Assert.Single(dto.Rows).Decision);
        var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == reqId).ToListAsync();
        Assert.All(items, li => Assert.Equal(groupId, li.RequestPoGroupId));
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == reqId && h.ActionTaken == "PAYMENT_GROUP_ITEM_LINK_REPAIR"));

        // repeated APPLY: 200, ALREADY_HEALTHY, no second audit
        var again = Assert.IsType<OkObjectResult>(await controller.PaymentGroupItemLinkageForRequest(reqId, confirm: true, body: body));
        var dto2 = Assert.IsType<PaymentGroupItemLinkageRepairResultDto>(again.Value);
        Assert.Equal(0, dto2.Repaired);
        Assert.Equal(1, dto2.AlreadyHealthy);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == reqId && h.ActionTaken == "PAYMENT_GROUP_ITEM_LINK_REPAIR"));

        Assert.IsType<NotFoundObjectResult>(await controller.PaymentGroupItemLinkageForRequest(Guid.NewGuid(), confirm: true, body: body));
    }
}
