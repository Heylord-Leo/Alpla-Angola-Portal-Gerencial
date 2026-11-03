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
using AlplaPortal.Infrastructure.Services.Purchasing;
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
/// v2.249.2 (review round 3) — request-line ownership of quotation payloads. Every <c>MappedRequestLineItemId</c> carried by
/// <c>POST …/quotations</c> (SaveQuotation) or <c>PUT …/quotations/{id}</c> (UpdateQuotation) must be a non-deleted line of
/// THE request addressed by the route, whatever the reconciliation status. A foreign, unknown or deleted reference refuses
/// the whole operation (400 <c>REQUEST_LINE_REFERENCE_INVALID</c>) before any tracked mutation: no quotation, line,
/// attachment link or history row is written, nothing is remapped or dropped, and the message does not say whether the id
/// exists elsewhere. Same-request payloads keep working unchanged.
/// </summary>
public class QuotationLineOwnershipValidationTests
{
    private const int QuotationTypeId = 1, WaitingQuotationId = 2, SupplierId = 10;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed record Seed(Guid Buyer, Guid RequestA, Guid A1, Guid A2Deleted, Guid RequestB, Guid B1);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        ctx.RequestTypes.Add(new RequestType { Id = QuotationTypeId, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = WaitingQuotationId, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Ag. Cotação" });
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaSOPRO" });
        ctx.Departments.Add(new Department { Id = 1, Name = "Manutenção" });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 3", CompanyId = 1 });
        ctx.Suppliers.Add(new Supplier { Id = SupplierId, Name = "FORNECEDOR TESTE", TaxId = "5400000009", RegistrationStatus = "ACTIVE", PortalCode = "SUP-T", IsActive = true });
        var buyer = Guid.NewGuid();
        ctx.Users.Add(new User { Id = buyer, FullName = "Comprador", Email = "b@t.local", IsActive = true });

        Request NewReq(string number) => new()
        {
            Id = Guid.NewGuid(), RequestNumber = number, Title = number, Description = "d", RequestTypeId = QuotationTypeId, StatusId = WaitingQuotationId,
            RequesterId = buyer, CreatedByUserId = buyer, BuyerId = buyer, DepartmentId = 1, CompanyId = 1, PlantId = 1, CurrencyId = 1, CreatedAtUtc = DateTime.UtcNow,
        };
        var a = NewReq("REQ-A"); var b = NewReq("REQ-B");
        ctx.Requests.AddRange(a, b);
        var a1 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = a.Id, LineNumber = 1, Description = "Item A1", Quantity = 2, UnitPrice = 0, TotalAmount = 0, QuotationLifecycleStatus = null };
        var a2 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = a.Id, LineNumber = 2, Description = "Item A2 (apagado)", Quantity = 1, UnitPrice = 0, TotalAmount = 0, QuotationLifecycleStatus = null, IsDeleted = true };
        var b1 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = b.Id, LineNumber = 1, Description = "Item B1", Quantity = 3, UnitPrice = 0, TotalAmount = 0, QuotationLifecycleStatus = null };
        ctx.RequestLineItems.AddRange(a1, a2, b1);
        await ctx.SaveChangesAsync();
        return new Seed(buyer, a.Id, a1.Id, a2.Id, b.Id, b1.Id);
    }

    private static RequestsController Controller(ApplicationDbContext ctx, Guid actorId)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(new ApprovalRoutingResultDto());
        var statusSync = new Mock<IRequestStatusSyncService>();
        statusSync.Setup(s => s.ComputeDisplayWorkflowStateAsync(It.IsAny<Guid>())).ReturnsAsync(string.Empty);
        var c = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            statusSync.Object,
            routing.Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new QuotationItemEligibilityService(ctx),
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(new PostPaymentCompletionOptions()));
        c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return c;
    }

    private static SaveQuotationItemDto Line(int number, Guid? mapped, string status = "MAPPED", decimal price = 1000m, decimal qty = 2) => new()
    {
        LineNumber = number, Description = $"Linha {number}", Quantity = qty, UnitPrice = price, MappedRequestLineItemId = mapped, ReconciliationStatus = status,
    };

    private static SaveQuotationRequestDto Dto(params SaveQuotationItemDto[] items) => new()
    {
        SupplierId = SupplierId, SupplierNameSnapshot = "FORNECEDOR TESTE", Currency = "AOA", SourceType = "MANUAL", Items = items.ToList(),
    };

    private sealed record Snapshot(int Quotations, int QuotationItems, int Histories, int Attachments);
    private static async Task<Snapshot> SnapAsync(ApplicationDbContext ctx, Guid requestId) => new(
        await ctx.Quotations.CountAsync(q => q.RequestId == requestId),
        await ctx.QuotationItems.CountAsync(qi => qi.Quotation.RequestId == requestId),
        await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == requestId),
        await ctx.RequestAttachments.CountAsync(a => a.RequestId == requestId));

    private static void AssertRefused(ActionResult<SavedQuotationDto> result, params int[] lineNumbers)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var pd = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Equal("Validação de Item", pd.Title);
        Assert.Equal("REQUEST_LINE_REFERENCE_INVALID", pd.Extensions["code"]?.ToString());
        foreach (var n in lineNumbers) Assert.Contains(n.ToString(), pd.Detail);
        Assert.DoesNotContain("REQ-B", pd.Detail ?? string.Empty); // never discloses the other request
    }

    [Fact]
    public async Task Foreign_request_line_is_refused_and_nothing_is_persisted()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before = await SnapAsync(ctx, s.RequestA);
        var result = await Controller(ctx, s.Buyer).SaveQuotation(s.RequestA, null, Dto(Line(1, s.B1)));
        AssertRefused(result, 1);
        Assert.Equal(before, await SnapAsync(ctx, s.RequestA));
        Assert.Equal(0, await ctx.Quotations.CountAsync(q => q.RequestId == s.RequestB)); // nothing leaked onto request B either
    }

    [Fact]
    public async Task Mixed_valid_and_foreign_payload_is_refused_as_a_whole()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before = await SnapAsync(ctx, s.RequestA);
        var result = await Controller(ctx, s.Buyer).SaveQuotation(s.RequestA, null, Dto(Line(1, s.A1), Line(2, s.B1)));
        AssertRefused(result, 2);
        Assert.Equal(before, await SnapAsync(ctx, s.RequestA)); // the valid line was NOT saved alone
    }

    [Fact]
    public async Task Unknown_and_deleted_line_references_are_refused_whatever_the_reconciliation_status()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var c = Controller(ctx, s.Buyer);
        var before = await SnapAsync(ctx, s.RequestA);
        AssertRefused(await c.SaveQuotation(s.RequestA, null, Dto(Line(1, Guid.NewGuid()))), 1);
        AssertRefused(await c.SaveQuotation(s.RequestA, null, Dto(Line(1, s.A2Deleted))), 1);
        // A reference is a reference: a NOT_QUOTED line mapped onto a foreign item is refused too.
        AssertRefused(await c.SaveQuotation(s.RequestA, null, Dto(Line(1, s.A1), Line(2, s.B1, status: "NOT_QUOTED", price: 0, qty: 0))), 2);
        Assert.Equal(before, await SnapAsync(ctx, s.RequestA));
    }

    [Fact]
    public async Task Same_request_payloads_save_and_update_normally()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var c = Controller(ctx, s.Buyer);
        var saved = await c.SaveQuotation(s.RequestA, null, Dto(Line(1, s.A1)));
        Assert.IsType<OkObjectResult>(saved.Result);
        ctx.ChangeTracker.Clear();
        var q = await ctx.Quotations.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.RequestId == s.RequestA);
        Assert.Equal(s.A1, Assert.Single(q.Items).MappedRequestLineItemId);

        var updated = await Controller(ctx, s.Buyer).UpdateQuotation(s.RequestA, q.Id, Dto(Line(1, s.A1, price: 900m)));
        Assert.IsType<OkObjectResult>(updated.Result);
        ctx.ChangeTracker.Clear();
        Assert.Equal(900m, (await ctx.QuotationItems.AsNoTracking().SingleAsync(qi => qi.QuotationId == q.Id)).UnitPrice);
    }

    [Fact]
    public async Task Update_with_a_foreign_reference_is_refused_and_the_quotation_is_untouched()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        Assert.IsType<OkObjectResult>((await Controller(ctx, s.Buyer).SaveQuotation(s.RequestA, null, Dto(Line(1, s.A1)))).Result);
        ctx.ChangeTracker.Clear();
        var q = await ctx.Quotations.AsNoTracking().SingleAsync(x => x.RequestId == s.RequestA);
        var before = await SnapAsync(ctx, s.RequestA);

        var foreign = await Controller(ctx, s.Buyer).UpdateQuotation(s.RequestA, q.Id, Dto(Line(1, s.B1, price: 1m)));
        AssertRefused(foreign, 1);
        var mixed = await Controller(ctx, s.Buyer).UpdateQuotation(s.RequestA, q.Id, Dto(Line(1, s.A1, price: 1m), Line(2, s.A2Deleted)));
        AssertRefused(mixed, 2);

        ctx.ChangeTracker.Clear();
        Assert.Equal(before, await SnapAsync(ctx, s.RequestA));
        var item = await ctx.QuotationItems.AsNoTracking().SingleAsync(qi => qi.QuotationId == q.Id);
        Assert.Equal(s.A1, item.MappedRequestLineItemId);
        Assert.Equal(1000m, item.UnitPrice); // the valid-looking price change of the mixed payload was not applied either
    }

    [Fact]
    public async Task Request_B_line_remains_valid_for_request_B_itself()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var result = await Controller(ctx, s.Buyer).SaveQuotation(s.RequestB, null, Dto(Line(1, s.B1)));
        Assert.IsType<OkObjectResult>(result.Result);
    }
}
