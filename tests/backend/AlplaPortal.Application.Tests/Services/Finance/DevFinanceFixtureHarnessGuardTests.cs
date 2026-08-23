using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Safety gates for the Finance DEV Regression Harness (DevFinanceFixtureController). Beyond the
/// compile-time #if DEBUG (which makes this a NotFound stub in Release), the harness must be a 404
/// unless BOTH the runtime environment is Development AND DevFixtures:FinanceEnabled is true — and
/// its reset must only ever touch synthetic ZZTEST-FIN-* rows, never historical requests.
/// </summary>
public class DevFinanceFixtureHarnessGuardTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()).Options);

    private static IConfiguration Config(bool financeEnabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DevFixtures:FinanceEnabled"] = financeEnabled.ToString() })
            .Build();

    private static IWebHostEnvironment Env(string environmentName)
    {
        var m = new Mock<IWebHostEnvironment>();
        m.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        return m.Object;
    }

    private static DevFinanceFixtureController Build(ApplicationDbContext ctx, string env, bool flag) =>
        new(ctx, new Mock<IJwtService>().Object, Env(env), Config(flag));

    [Fact]
    public async Task Endpoints_AreNotFound_WhenNotDevelopment_EvenIfFlagEnabled()
    {
        var c = Build(NewContext(), "Production", flag: true);
        Assert.IsType<NotFoundResult>(await c.Reset());
        Assert.IsType<NotFoundResult>(await c.Token());
        Assert.IsType<NotFoundResult>(await c.State());
        Assert.IsType<NotFoundResult>(await c.Seed());
    }

    [Fact]
    public async Task Endpoints_AreNotFound_InDevelopment_WhenFlagDisabled()
    {
        var c = Build(NewContext(), "Development", flag: false);
        Assert.IsType<NotFoundResult>(await c.Reset());
        Assert.IsType<NotFoundResult>(await c.Token());
        Assert.IsType<NotFoundResult>(await c.State());
        Assert.IsType<NotFoundResult>(await c.Seed());
    }

    [Fact]
    public async Task Endpoints_AreNotFound_InStaging()
    {
        var c = Build(NewContext(), "Staging", flag: true);
        Assert.IsType<NotFoundResult>(await c.Reset());
    }

    [Fact]
    public async Task Reset_Development_Enabled_RemovesOnlySyntheticRows_NeverHistorical()
    {
        var ctx = NewContext();
        var historical = new Request { Id = Guid.NewGuid(), RequestNumber = "REQ-01/01/2026-001", Title = "Pedido real histórico", RequestTypeId = 1, StatusId = 1, RequesterId = Guid.NewGuid(), CreatedByUserId = Guid.NewGuid(), CompanyId = 1, DepartmentId = 1, CreatedAtUtc = DateTime.UtcNow };
        var synthetic = new Request { Id = Guid.NewGuid(), RequestNumber = "ZZTEST-FIN-A", Title = "[ZZTEST-FIN] Cenário A", RequestTypeId = 1, StatusId = 1, RequesterId = Guid.NewGuid(), CreatedByUserId = Guid.NewGuid(), CompanyId = 1, DepartmentId = 1, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.AddRange(historical, synthetic);
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, "Development", flag: true).Reset();
        Assert.IsType<OkObjectResult>(result);

        var remaining = await ctx.Requests.AsNoTracking().Select(r => r.RequestNumber).ToListAsync();
        Assert.Contains("REQ-01/01/2026-001", remaining);   // historical untouched
        Assert.DoesNotContain("ZZTEST-FIN-A", remaining);   // synthetic removed
    }
}
