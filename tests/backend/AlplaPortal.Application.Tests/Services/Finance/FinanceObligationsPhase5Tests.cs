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
/// Phase-5 GET /finance/obligations: newest/oldest ordering (before pagination), NIF search
/// (snapshot + canonical TaxId), Company/Plant/Department filters, and request-level note metadata.
/// </summary>
public class FinanceObligationsPhase5Tests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()).Options;
        return new ApplicationDbContext(options);
    }

    private static FinanceController BuildController(ApplicationDbContext ctx)
    {
        var controller = new FinanceController(ctx, new Mock<IWorkflowNotificationOrchestrator>().Object,
            NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance),
            new FinancePaymentEligibilityService());
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        return controller;
    }

    private static FinanceObligationsResponseDto Body(ActionResult<FinanceObligationsResponseDto> r) =>
        Assert.IsType<FinanceObligationsResponseDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

    /// <summary>Seeds N QUOTATION requests, one PO_ISSUED group each, distinct createdAt/plant/dept/company/supplier.</summary>
    private static async Task<User> SeedBaseAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"f-{Guid.NewGuid()}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.Departments.AddRange(new Department { Id = 10, Name = "TI" }, new Department { Id = 11, Name = "Recursos Humanos" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 12, Code = RequestConstants.Statuses.PoIssued, Name = "P.O Emitida", DisplayOrder = 30 });
        ctx.Suppliers.Add(new Supplier { Id = 81, Name = "ZEPA", TaxId = "5401126913", IsActive = true });
        await ctx.SaveChangesAsync();
        return actor;
    }

    private static Request AddRequest(ApplicationDbContext ctx, Guid actorId, string number, DateTime createdAt,
        int deptId = 10, int companyId = 1, int plantId = 1, int? supplierId = null, string? nifSnapshot = null, string supplierName = "Fornecedor X")
    {
        var req = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = number, Title = "T " + number, RequestTypeId = 2, StatusId = 12,
            RequesterId = actorId, DepartmentId = deptId, CompanyId = companyId, PlantId = plantId, CreatedAtUtc = createdAt
        };
        ctx.Requests.Add(req);
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = supplierId, SupplierNameSnapshot = supplierName,
            SupplierNifSnapshot = nifSnapshot, CurrencyCode = "AOA", TotalAmount = 1000m,
            Status = RequestConstants.Statuses.PoIssued, CreatedAtUtc = createdAt, CreatedByUserId = actorId
        });
        return req;
    }

    [Fact]
    public async Task Sort_NewestFirst_IsDefault_AndOrdersBeforePagination()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        AddRequest(ctx, actor.Id, "REQ-A-OLD", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        AddRequest(ctx, actor.Id, "REQ-B-MID", new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));
        AddRequest(ctx, actor.Id, "REQ-C-NEW", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        // Default (no sortBy) = newest first; pageSize 1 proves ordering happens BEFORE pagination.
        var page1 = Body(await controller.GetObligations(page: 1, pageSize: 1));
        Assert.Equal("REQ-C-NEW", page1.PagedResult.Items.Single().RequestNumber);
        Assert.Equal(3, page1.PagedResult.TotalCount);

        var page3 = Body(await controller.GetObligations(page: 3, pageSize: 1));
        Assert.Equal("REQ-A-OLD", page3.PagedResult.Items.Single().RequestNumber);
    }

    [Fact]
    public async Task Sort_OldestFirst_ReversesOrder()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        AddRequest(ctx, actor.Id, "REQ-A-OLD", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        AddRequest(ctx, actor.Id, "REQ-C-NEW", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        var body = Body(await controller.GetObligations(sortBy: "oldest", page: 1, pageSize: 1));
        Assert.Equal("REQ-A-OLD", body.PagedResult.Items.Single().RequestNumber);
    }

    [Fact]
    public async Task Search_ByNif_MatchesSnapshot_DigitsNormalized()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        AddRequest(ctx, actor.Id, "REQ-NIF-SNAP", DateTime.UtcNow, nifSnapshot: "5417162353", supplierName: "Super Hello");
        AddRequest(ctx, actor.Id, "REQ-OTHER", DateTime.UtcNow, nifSnapshot: "9999999999", supplierName: "Outro");
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        var body = Body(await controller.GetObligations(search: "5417 162 353")); // punctuation ignored
        var c = Assert.Single(body.PagedResult.Items);
        Assert.Equal("REQ-NIF-SNAP", c.RequestNumber);
    }

    [Fact]
    public async Task Search_ByNif_MatchesCanonicalTaxId_WhenSnapshotAbsent()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        // No snapshot NIF, but the group points at canonical Supplier 81 (TaxId 5401126913).
        AddRequest(ctx, actor.Id, "REQ-CANON", DateTime.UtcNow, supplierId: 81, nifSnapshot: null, supplierName: "ZEPA");
        AddRequest(ctx, actor.Id, "REQ-OTHER", DateTime.UtcNow, nifSnapshot: "1111111111", supplierName: "Outro");
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        var body = Body(await controller.GetObligations(search: "5401126913"));
        var c = Assert.Single(body.PagedResult.Items);
        Assert.Equal("REQ-CANON", c.RequestNumber);
    }

    [Fact]
    public async Task Filters_Company_Plant_Department_ScopeListAndSummary()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        AddRequest(ctx, actor.Id, "REQ-TI-C1-P1", DateTime.UtcNow, deptId: 10, companyId: 1, plantId: 1);
        AddRequest(ctx, actor.Id, "REQ-RH-C2-P2", DateTime.UtcNow, deptId: 11, companyId: 2, plantId: 2);
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        var byDept = Body(await controller.GetObligations(departmentId: 11));
        Assert.Equal("REQ-RH-C2-P2", Assert.Single(byDept.PagedResult.Items).RequestNumber);
        Assert.Equal(1, byDept.Summary.NeedsScheduling.Count); // summary scoped by org filter too

        var byCompany = Body(await controller.GetObligations(companyId: 1));
        Assert.Equal("REQ-TI-C1-P1", Assert.Single(byCompany.PagedResult.Items).RequestNumber);

        var byPlant = Body(await controller.GetObligations(plantId: 2));
        Assert.Equal("REQ-RH-C2-P2", Assert.Single(byPlant.PagedResult.Items).RequestNumber);

        var combined = Body(await controller.GetObligations(companyId: 2, plantId: 2, departmentId: 11, actionClass: FinanceActionClasses.NeedsScheduling));
        Assert.Single(combined.PagedResult.Items);
    }

    [Fact]
    public async Task CompanyFilter_ReturnsOnlyThatCompany_NoLeakAcrossPagination()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        // 3 company-1 requests + 2 company-2 requests.
        for (int i = 0; i < 3; i++) AddRequest(ctx, actor.Id, $"REQ-C1-{i}", DateTime.UtcNow.AddDays(-i), companyId: 1, plantId: 1);
        for (int i = 0; i < 2; i++) AddRequest(ctx, actor.Id, $"REQ-C2-{i}", DateTime.UtcNow.AddDays(-i), deptId: 11, companyId: 2, plantId: 2);
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        // Company 1 → exactly the 3 company-1 requests, none from company 2, across pages.
        var c1p1 = Body(await controller.GetObligations(companyId: 1, page: 1, pageSize: 2));
        var c1p2 = Body(await controller.GetObligations(companyId: 1, page: 2, pageSize: 2));
        Assert.Equal(3, c1p1.PagedResult.TotalCount);
        var seen = c1p1.PagedResult.Items.Concat(c1p2.PagedResult.Items).Select(c => c.RequestNumber).ToList();
        Assert.Equal(3, seen.Count);
        Assert.All(seen, n => Assert.StartsWith("REQ-C1-", n));
        Assert.Equal(3, c1p1.Summary.NeedsScheduling.Count); // summary scoped to company 1

        // Company 2 → exactly the 2 company-2 requests.
        var c2 = Body(await controller.GetObligations(companyId: 2, pageSize: 50));
        Assert.Equal(2, c2.PagedResult.TotalCount);
        Assert.All(c2.PagedResult.Items, c => Assert.StartsWith("REQ-C2-", c.RequestNumber));
    }

    [Fact]
    public async Task CompanyPlus_IncompatiblePlant_ReturnsEmpty()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        AddRequest(ctx, actor.Id, "REQ-C1-P1", DateTime.UtcNow, companyId: 1, plantId: 1);
        AddRequest(ctx, actor.Id, "REQ-C2-P2", DateTime.UtcNow, deptId: 11, companyId: 2, plantId: 2);
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        // Company 2 + Plant 1 (which belongs to company 1) → no request matches both.
        var body = Body(await controller.GetObligations(companyId: 2, plantId: 1));
        Assert.Empty(body.PagedResult.Items);
        Assert.Equal(0, body.Summary.ActionableTotal);
    }

    [Fact]
    public async Task CompanyPlusDepartment_Combined()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        AddRequest(ctx, actor.Id, "REQ-C1-TI", DateTime.UtcNow, deptId: 10, companyId: 1);
        AddRequest(ctx, actor.Id, "REQ-C1-RH", DateTime.UtcNow, deptId: 11, companyId: 1);
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        var body = Body(await controller.GetObligations(companyId: 1, departmentId: 11));
        Assert.Equal("REQ-C1-RH", Assert.Single(body.PagedResult.Items).RequestNumber);
    }

    [Fact]
    public async Task Notes_None_NoIndicator()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        AddRequest(ctx, actor.Id, "REQ-NO-NOTE", DateTime.UtcNow);
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        var c = Assert.Single(Body(await controller.GetObligations()).PagedResult.Items);
        Assert.False(c.HasNotes);
        Assert.Equal(0, c.NoteCount);
        Assert.Null(c.LatestNoteText);
    }

    [Fact]
    public async Task Notes_Multiple_UsesLatest_StripsPrefix_CountsAll()
    {
        var ctx = NewContext();
        var actor = await SeedBaseAsync(ctx);
        var req = AddRequest(ctx, actor.Id, "REQ-NOTES", DateTime.UtcNow);
        await ctx.SaveChangesAsync();

        ctx.RequestStatusHistories.AddRange(
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = req.Id, ActorUserId = actor.Id, ActionTaken = "NOTA_FINANCEIRA", NewStatusId = 12, Comment = "Nota de Finanças: primeira observação", CreatedAtUtc = DateTime.UtcNow.AddHours(-2) },
            new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = req.Id, ActorUserId = actor.Id, ActionTaken = "NOTA_FINANCEIRA", NewStatusId = 12, Comment = "Nota de Finanças: observação mais recente", CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var controller = BuildController(ctx);

        var c = Assert.Single(Body(await controller.GetObligations()).PagedResult.Items);
        Assert.True(c.HasNotes);
        Assert.Equal(2, c.NoteCount);
        Assert.Equal("observação mais recente", c.LatestNoteText); // latest + prefix stripped
        Assert.Equal("Finance Tester", c.LatestNoteActorName);
    }
}
