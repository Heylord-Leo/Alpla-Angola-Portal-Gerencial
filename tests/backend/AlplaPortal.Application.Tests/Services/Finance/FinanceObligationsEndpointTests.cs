using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Covers GET /api/v1/finance/obligations end-to-end: Option-C container grouping, per-currency
/// summary cards, list filters (actionClass), search, and list/summary consistency. InMemory-EF
/// direct-controller pattern (same as FinanceCancelScheduleTests).
/// </summary>
public class FinanceObligationsEndpointTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FinanceController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new FinanceController(
            ctx,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance),
            new FinancePaymentEligibilityService());
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.SystemAdministrator)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private static async Task<(Guid mixedId, Guid eurId)> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"fin-{Guid.NewGuid()}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.Departments.Add(new Department { Id = 1, Name = "Compras" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 12, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 30 },
            new RequestStatus { Id = 9, Code = RequestConstants.Statuses.FinalApproved, Name = "Aprovado", DisplayOrder = 10 });
        await ctx.SaveChangesAsync();

        // Mixed multi-group QUOTATION (REQ-100 shape): NCR PAYMENT_COMPLETED (AOA) + ITEC PO_ISSUED (AOA).
        var mixed = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-20/07/2026-100", Title = "Mixed",
            RequestTypeId = 2, StatusId = 12, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(mixed);
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = mixed.Id, SupplierId = 1, SupplierNameSnapshot = "NCR ANGOLA", CurrencyCode = "AOA", TotalAmount = 70341.42m, Status = RequestConstants.Statuses.PaymentCompleted, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id },
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = mixed.Id, SupplierId = 2, SupplierNameSnapshot = "ITEC LDA", CurrencyCode = "AOA", TotalAmount = 275139.00m, Status = RequestConstants.Statuses.PoIssued, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id });

        // Single-group QUOTATION with a EUR PO_ISSUED group (currency separation).
        var eur = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-16/07/2026-095", Title = "EUR one",
            RequestTypeId = 2, StatusId = 12, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(eur);
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = eur.Id, SupplierId = 3, SupplierNameSnapshot = "RBC VIAGENS", CurrencyCode = "EUR", TotalAmount = 935m, Status = RequestConstants.Statuses.PoIssued, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id });

        await ctx.SaveChangesAsync();
        return (mixed.Id, eur.Id);
    }

    private static FinanceObligationsResponseDto Body(ActionResult<FinanceObligationsResponseDto> r) =>
        Assert.IsType<FinanceObligationsResponseDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

    [Fact]
    public async Task Obligations_MultiGroupContainer_ExposesBothObligations_ExpandedByDefault()
    {
        var ctx = NewContext();
        var (mixedId, _) = await SeedAsync(ctx);
        var controller = BuildController(ctx, Guid.NewGuid());

        var body = Body(await controller.GetObligations());

        var mixed = body.PagedResult.Items.Single(c => c.RequestId == mixedId);
        Assert.Equal(2, mixed.Obligations.Count);
        Assert.True(mixed.ExpandByDefault);
        Assert.Contains(mixed.Obligations, o => o.GroupStatusCode == RequestConstants.Statuses.PoIssued && o.ActionClass == FinanceActionClasses.NeedsScheduling && o.FinanceActions.Contains("SCHEDULE"));
        Assert.Contains(mixed.Obligations, o => o.GroupStatusCode == RequestConstants.Statuses.PaymentCompleted && o.ActionClass == FinanceActionClasses.PaidWaitingReceiving && o.FinanceActions.Count == 0);
    }

    [Fact]
    public async Task Obligations_Summary_CountsAndPerCurrencyTotals_NeverSumAcrossCurrencies()
    {
        var ctx = NewContext();
        await SeedAsync(ctx);
        var controller = BuildController(ctx, Guid.NewGuid());

        var body = Body(await controller.GetObligations());

        // NEEDS_SCHEDULING: ITEC (AOA 275,139) + RBC (EUR 935) = 2 obligations, split by currency.
        Assert.Equal(2, body.Summary.NeedsScheduling.Count);
        var byCcy = body.Summary.NeedsScheduling.AmountsByCurrency.ToDictionary(a => a.CurrencyCode, a => a.Amount);
        Assert.Equal(275139.00m, byCcy["AOA"]);
        Assert.Equal(935m, byCcy["EUR"]);
        Assert.Equal(2, byCcy.Count); // never merged into a single number

        // PAID_WAITING_RECEIVING: NCR only.
        Assert.Equal(1, body.Summary.PaidWaitingReceiving.Count);
        Assert.Equal(2, body.Summary.ActionableTotal); // ITEC + RBC
    }

    [Fact]
    public async Task Obligations_FilterByActionClass_IncludesContainerButKeepsSiblingContext()
    {
        var ctx = NewContext();
        var (mixedId, _) = await SeedAsync(ctx);
        var controller = BuildController(ctx, Guid.NewGuid());

        var body = Body(await controller.GetObligations(actionClass: FinanceActionClasses.NeedsScheduling));

        // The mixed container is included (ITEC matches) and still shows the paid sibling for context.
        var mixed = body.PagedResult.Items.Single(c => c.RequestId == mixedId);
        Assert.Equal(2, mixed.Obligations.Count);
        // Summary is unaffected by the list filter (cards always show the full picture).
        Assert.Equal(1, body.Summary.PaidWaitingReceiving.Count);
    }

    [Fact]
    public async Task Obligations_SearchBySupplier_NarrowsList()
    {
        var ctx = NewContext();
        await SeedAsync(ctx);
        var controller = BuildController(ctx, Guid.NewGuid());

        var body = Body(await controller.GetObligations(search: "RBC"));

        var container = Assert.Single(body.PagedResult.Items);
        Assert.Equal("REQ-16/07/2026-095", container.RequestNumber);
    }

    [Fact]
    public async Task Obligations_FilterByCurrency_OnlyMatchingObligations()
    {
        var ctx = NewContext();
        await SeedAsync(ctx);
        var controller = BuildController(ctx, Guid.NewGuid());

        var body = Body(await controller.GetObligations(currencyCode: "EUR"));

        var container = Assert.Single(body.PagedResult.Items);
        Assert.All(container.Obligations, o => Assert.Equal("EUR", o.CurrencyCode));
    }
}
