using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Receiving;

/// <summary>
/// B4 — Dashboard Receiving entitlement contract (CLOSED product decision). The plane the server
/// returns is determined SOLELY by the caller's actual roles, never by the fact that the Receiving
/// workspace route also admits Local Manager:
///   Receiving                    → Shared only (operational)
///   Local Manager w/o Receiving  → Managerial only (view-only)
///   SysAdmin w/o Receiving       → Managerial only (view-only)
///   Receiving + Local Manager    → Shared only (operational ownership wins; NOT duplicated)
///   Unrelated (e.g. Requester)   → neither
///   Requester + Local Manager    → Managerial only
/// Operational Shared ownership requires the real RECEIVING role — it is NOT a "Receiving OR
/// LocalManager" rule. These tests exercise the full path: role claims → controller → DTO planes.
/// </summary>
public class ReceivingDashboardEntitlementTests
{
    private static ApplicationDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DashboardV2Controller BuildController(ApplicationDbContext ctx, params string[] roles)
    {
        var financeEligibility = new Mock<IFinancePaymentEligibilityService>();
        var controller = new DashboardV2Controller(ctx, financeEligibility.Object);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        return controller;
    }

    private static async Task<DashboardV2ReceivingSectionDto> InvokeAsync(params string[] roles)
    {
        var ctx = GetInMemoryDbContext();
        var controller = BuildController(ctx, roles);
        var result = await controller.GetReceivingSection();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<DashboardV2ReceivingSectionDto>(ok.Value);
    }

    [Fact]
    public async Task ReceivingOnly_SharedPopulated_ManagerialNull()
    {
        var dto = await InvokeAsync(RoleConstants.Receiving);
        Assert.NotNull(dto.Shared);
        Assert.Null(dto.Managerial);
    }

    [Fact]
    public async Task LocalManagerOnly_SharedNull_ManagerialPopulated()
    {
        var dto = await InvokeAsync(RoleConstants.LocalManager);
        Assert.Null(dto.Shared);
        Assert.NotNull(dto.Managerial);
    }

    [Fact]
    public async Task SystemAdministratorOnly_SharedNull_ManagerialPopulated()
    {
        var dto = await InvokeAsync(RoleConstants.SystemAdministrator);
        Assert.Null(dto.Shared);
        Assert.NotNull(dto.Managerial);
    }

    [Fact]
    public async Task ReceivingPlusLocalManager_SharedOnly_NotDuplicated()
    {
        var dto = await InvokeAsync(RoleConstants.Receiving, RoleConstants.LocalManager);
        Assert.NotNull(dto.Shared);
        Assert.Null(dto.Managerial); // operational ownership wins; never duplicate the payload
    }

    [Fact]
    public async Task UnrelatedUser_BothNull()
    {
        var dto = await InvokeAsync(RoleConstants.Requester);
        Assert.Null(dto.Shared);
        Assert.Null(dto.Managerial);
    }

    [Fact]
    public async Task RequesterPlusLocalManager_ManagerialOnly()
    {
        var dto = await InvokeAsync(RoleConstants.Requester, RoleConstants.LocalManager);
        Assert.Null(dto.Shared);
        Assert.NotNull(dto.Managerial);
    }

    [Fact]
    public async Task Regression_LocalManagerIsNotTreatedAsOperationalReceiving()
    {
        // The reported defect: a Local Manager (WITHOUT the Receiving role) must NEVER receive the
        // Shared operational plane. Guards against a "Receiving OR LocalManager" ownership rule.
        var dto = await InvokeAsync(RoleConstants.LocalManager, RoleConstants.Viewer);
        Assert.Null(dto.Shared);
        Assert.NotNull(dto.Managerial);
    }
}
