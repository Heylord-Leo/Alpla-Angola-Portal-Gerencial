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

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B7.1 — Financial Summary visibility gate (PD-B7-02). Only Finance / Local Manager / System
/// Administrator are entitled; everyone else gets a null CurrentExposure (frontend hides it). The gate is
/// exercised end-to-end through the controller (role claims → DTO). Uses an empty in-memory DB (the money
/// aggregation over real data is verified in the LocalDB integration test).
/// </summary>
public class FinancialSummaryAuthTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<DashboardV2FinancialDto> InvokeAsync(params string[] roles)
    {
        var ctx = NewDb();
        var eligibility = new Mock<IFinancePaymentEligibilityService>();
        eligibility.Setup(e => e.EvaluateGroupActions(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new List<string>());
        var controller = new DashboardV2Controller(ctx, eligibility.Object);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) },
        };
        var result = await controller.GetFinancialSummary();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<DashboardV2FinancialDto>(ok.Value);
    }

    [Fact]
    public async Task Finance_is_entitled()
    {
        var dto = await InvokeAsync(RoleConstants.Finance);
        Assert.NotNull(dto.CurrentExposure);
        Assert.NotNull(dto.PaidHistory); // B7.3: paid history present for entitled users
    }

    [Fact]
    public async Task Unauthorized_user_gets_no_paid_history()
    {
        var dto = await InvokeAsync(RoleConstants.Buyer);
        Assert.Null(dto.CurrentExposure);
        Assert.Null(dto.PaidHistory);
    }

    [Fact]
    public async Task LocalManager_is_entitled()
    {
        var dto = await InvokeAsync(RoleConstants.LocalManager);
        Assert.NotNull(dto.CurrentExposure);
    }

    [Fact]
    public async Task SystemAdministrator_is_entitled()
    {
        var dto = await InvokeAsync(RoleConstants.SystemAdministrator);
        Assert.NotNull(dto.CurrentExposure);
    }

    [Fact]
    public async Task Multi_role_user_with_any_entitled_role_is_entitled()
    {
        var dto = await InvokeAsync(RoleConstants.Buyer, RoleConstants.Finance);
        Assert.NotNull(dto.CurrentExposure);
    }

    [Fact]
    public async Task Viewer_management_alone_is_denied()
    {
        var dto = await InvokeAsync(RoleConstants.Viewer);
        Assert.Null(dto.CurrentExposure);
    }

    [Fact]
    public async Task Ordinary_buyer_is_denied()
    {
        var dto = await InvokeAsync(RoleConstants.Buyer);
        Assert.Null(dto.CurrentExposure);
    }

    [Fact]
    public async Task Receiving_and_approver_and_requester_are_denied()
    {
        Assert.Null((await InvokeAsync(RoleConstants.Receiving)).CurrentExposure);
        Assert.Null((await InvokeAsync(RoleConstants.FinalApprover)).CurrentExposure);
        Assert.Null((await InvokeAsync(RoleConstants.Requester)).CurrentExposure);
    }

    [Fact]
    public async Task Entitled_user_receives_the_four_current_exposure_categories()
    {
        var dto = await InvokeAsync(RoleConstants.Finance);
        Assert.NotNull(dto.CurrentExposure);
        Assert.Contains(dto.CurrentExposure!, c => c.Code == FinancialCategories.EmAprovacao);
        Assert.Contains(dto.CurrentExposure!, c => c.Code == FinancialCategories.AguardandoPo);
        Assert.Contains(dto.CurrentExposure!, c => c.Code == FinancialCategories.EmProcessamentoFinanceiro);
        Assert.Contains(dto.CurrentExposure!, c => c.Code == FinancialCategories.PagoAguardandoRecebimento);
        // No completed/history category in current exposure (PD-B7-09/10).
        Assert.DoesNotContain(dto.CurrentExposure!, c => c.Code.Contains("HISTOR") || c.Code.Contains("COMPLET") || c.Code.Contains("PAID_HISTORY"));
    }
}
