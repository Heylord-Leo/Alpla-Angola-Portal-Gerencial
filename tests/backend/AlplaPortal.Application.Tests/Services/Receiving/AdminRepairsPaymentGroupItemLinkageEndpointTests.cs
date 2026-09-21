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
    public async Task Apply_WithReason_RunsEndToEnd_LinksItems()
    {
        await using var ctx = new ApplicationDbContext(NewDbOptions());
        var (reqId, groupId, actorId) = await SeedAsync(ctx);

        var result = await BuildController(ctx, actorId, RoleConstants.SystemAdministrator)
            .PaymentGroupItemLinkage(confirm: true, body: new PaymentGroupItemLinkageRepairRequest { Reason = "TEST" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<PaymentGroupItemLinkageRepairResultDto>(ok.Value);
        Assert.Equal("APPLIED", dto.Status);
        Assert.Equal(1, dto.Repaired);
        Assert.Equal(0, dto.Errors);
        var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == reqId).ToListAsync();
        Assert.All(items, li => Assert.Equal(groupId, li.RequestPoGroupId));
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == reqId && h.ActionTaken == "PAYMENT_GROUP_ITEM_LINK_REPAIR"));
    }
}
