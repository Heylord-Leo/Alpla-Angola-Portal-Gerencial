using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Phase 3A — the read-only Buyer Workspace endpoint: authorization/scope, coverage consistency with
/// the shared BuyerQueueProjectionBuilder, contextual supplier involvement + per-currency (no
/// aggregation) + NIF dedup, and batch kind classification.
/// </summary>
public class BuyerWorkspaceEndpointTests
{
    private const int QuotationTypeId = 1;
    private const int WaitingQuotationStatusId = 2;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static void SeedLookups(ApplicationDbContext ctx)
    {
        ctx.RequestTypes.Add(new RequestType { Id = QuotationTypeId, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = WaitingQuotationStatusId, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Aguardando Cotação" });
        ctx.Companies.Add(new Company { Id = 1, Name = "ALPLA" });
        ctx.Departments.Add(new Department { Id = 1, Name = "TI" });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 1", CompanyId = 1 });
    }

    private static async Task<Guid> SeedActorAsync(ApplicationDbContext ctx)
    {
        var id = Guid.NewGuid();
        ctx.Users.Add(new User { Id = id, FullName = "Comprador Teste", Email = "b@t.local", IsActive = true });
        await ctx.SaveChangesAsync();
        return id;
    }

    private static Request SeedRequest(ApplicationDbContext ctx, Guid actor, string number, int plantId = 1)
    {
        var req = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = number, Title = $"Pedido {number}", Description = "d",
            RequestTypeId = QuotationTypeId, StatusId = WaitingQuotationStatusId,
            RequesterId = actor, CreatedByUserId = actor, BuyerId = actor,
            DepartmentId = 1, CompanyId = 1, PlantId = plantId, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
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

    private static Supplier SeedSupplier(ApplicationDbContext ctx, int id, string name, string? nif, bool active = true)
    {
        var s = new Supplier { Id = id, Name = name, TaxId = nif, IsActive = active, RegistrationStatus = "ACTIVE", Origin = "MANUAL" };
        ctx.Suppliers.Add(s);
        return s;
    }

    private static Quotation SeedQuotation(ApplicationDbContext ctx, Request req, int supplierId, string name, string currency, decimal total, bool selected, Guid actor, RequestLineItem? mapTo = null)
    {
        var q = new Quotation
        {
            Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = supplierId, SupplierNameSnapshot = name,
            Currency = currency, TotalAmount = total, IsSelected = selected, SourceType = "MANUAL",
            CreatedByUserId = actor, CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Quotations.Add(q);
        if (mapTo != null)
            ctx.QuotationItems.Add(new QuotationItem { Id = Guid.NewGuid(), QuotationId = q.Id, Description = mapTo.Description, ReconciliationStatus = RequestConstants.ReconciliationStatuses.Mapped, MappedRequestLineItemId = mapTo.Id });
        return q;
    }

    private static void SeedIssuedPo(ApplicationDbContext ctx, Request req, int supplierId, string po, string currency, decimal amount, Guid actor)
        => ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = supplierId, PurchaseOrderNumber = po,
            Status = RequestConstants.PoGroupStatuses.PoIssued, CurrencyCode = currency, TotalAmount = amount,
            CreatedByUserId = actor, CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        });

    private static ApprovalBatch SeedBatch(ApplicationDbContext ctx, Request req, int num, string status, Guid actor, params RequestLineItem[] items)
    {
        var b = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = req.Id, BatchNumber = num, Status = status, CreatedByUserId = actor, CreatedAtUtc = DateTime.UtcNow };
        ctx.ApprovalBatches.Add(b);
        foreach (var it in items)
            ctx.Set<ApprovalBatchItem>().Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = b.Id, RequestLineItemId = it.Id, CreatedAtUtc = DateTime.UtcNow });
        return b;
    }

    private static T Ctrl<T>(ApplicationDbContext ctx, Guid actor, Func<ApplicationDbContext, T> make, string role = "System Administrator") where T : ControllerBase
    {
        var c = make(ctx);
        c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actor.ToString()),
                    new(ClaimTypes.Role, role)
                }, "Test"))
            }
        };
        return c;
    }

    private static async Task<BuyerWorkspaceDto> Workspace(ApplicationDbContext ctx, Guid actor, Guid requestId, string role = "System Administrator")
    {
        var result = await Ctrl(ctx, actor, c => new BuyerWorkspaceController(c), role).GetWorkspace(requestId);
        return (BuyerWorkspaceDto)((OkObjectResult)((ActionResult<BuyerWorkspaceDto>)result).Result!).Value!;
    }

    // ════════════════════ auth / scope ════════════════════

    [Fact]
    public async Task Workspace_OutOfScope_Is_NotFound()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        ctx.Plants.Add(new Plant { Id = 2, Name = "Viana 3", CompanyId = 1 });
        var owner = await SeedActorAsync(ctx);
        var outsider = Guid.NewGuid();
        ctx.Users.Add(new User { Id = outsider, FullName = "Fora", Email = "x@t.local", IsActive = true });
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = outsider, PlantId = 2 });
        var req = SeedRequest(ctx, owner, "REQ-SC", plantId: 1);
        await ctx.SaveChangesAsync();

        var result = await Ctrl(ctx, outsider, c => new BuyerWorkspaceController(c), "Buyer").GetWorkspace(req.Id);
        Assert.IsType<NotFoundObjectResult>(((ActionResult<BuyerWorkspaceDto>)result).Result);
    }

    // ════════════════════ close-not-quoted eligibility flag (Phase 3E.1) ════════════════════

    [Fact]
    public async Task CanCloseNotQuoted_True_ForPendingItem_NotInBatch()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedRequest(ctx, actor, "REQ-CNQ1");
        SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        SeedItem(ctx, req, 2, null); // legacy/uninitialized is also eligible
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, actor, req.Id);
        Assert.All(ws.Items, i => Assert.True(i.CanCloseNotQuoted));
    }

    [Theory]
    [InlineData("BATCH_ASSIGNED")]
    [InlineData("QUOTATION_APPROVED")]
    [InlineData("CLOSED_NOT_QUOTED")]
    [InlineData("NOT_QUOTED_ACCEPTED")]
    public async Task CanCloseNotQuoted_False_ForNonPendingLifecycle(string lifecycle)
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedRequest(ctx, actor, "REQ-CNQ2");
        SeedItem(ctx, req, 1, lifecycle);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, actor, req.Id);
        Assert.False(ws.Items.Single().CanCloseNotQuoted);
    }

    [Fact]
    public async Task CanCloseNotQuoted_False_WhenPendingItemIsInAnActiveBatch()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedRequest(ctx, actor, "REQ-CNQ3");
        var pending = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        SeedBatch(ctx, req, 1, RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, actor, pending);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, actor, req.Id);
        Assert.False(ws.Items.Single().CanCloseNotQuoted);
    }

    // Actor eligibility (Follow-up): the flag must mirror the endpoint's actor rule so the Workspace never
    // offers an action guaranteed to fail — SysAdmin override, Buyer only when owning (assigned/unassigned).

    [Fact]
    public async Task CanCloseNotQuoted_False_ForBuyer_NotAssignedToTheRequest()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var owner = await SeedActorAsync(ctx);           // request is assigned to this buyer
        var otherBuyer = Guid.NewGuid();
        ctx.Users.Add(new User { Id = otherBuyer, FullName = "Outro", Email = "o@t.local", IsActive = true });
        var req = SeedRequest(ctx, owner, "REQ-CNQ4"); // BuyerId = owner
        SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, otherBuyer, req.Id, "Buyer"); // a different buyer views it
        Assert.False(ws.Items.Single().CanCloseNotQuoted);
    }

    [Fact]
    public async Task CanCloseNotQuoted_True_ForAssignedBuyer()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var owner = await SeedActorAsync(ctx);
        var req = SeedRequest(ctx, owner, "REQ-CNQ5"); // BuyerId = owner
        SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, owner, req.Id, "Buyer");
        Assert.True(ws.Items.Single().CanCloseNotQuoted);
    }

    [Fact]
    public async Task CanCloseNotQuoted_True_ForSystemAdministrator_AdminOverride()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var owner = await SeedActorAsync(ctx);
        var admin = Guid.NewGuid();
        ctx.Users.Add(new User { Id = admin, FullName = "Admin", Email = "a@t.local", IsActive = true });
        var req = SeedRequest(ctx, owner, "REQ-CNQ6"); // assigned to someone else
        SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, admin, req.Id, "System Administrator");
        Assert.True(ws.Items.Single().CanCloseNotQuoted);
    }

    [Fact]
    public async Task CanCloseNotQuoted_True_ForAnyBuyer_WhenRequestUnassigned()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var owner = await SeedActorAsync(ctx);
        var req = SeedRequest(ctx, owner, "REQ-CNQ7");
        req.BuyerId = null; // unassigned
        SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, owner, req.Id, "Buyer");
        Assert.True(ws.Items.Single().CanCloseNotQuoted);
    }

    // ════════════════════ coverage consistency with the queue builder ════════════════════

    [Fact]
    public async Task Workspace_Coverage_Matches_Queue_Projection()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedRequest(ctx, actor, "REQ-COV");
        SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationApproved);
        var ready = SeedItem(ctx, req, 2, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        SeedItem(ctx, req, 3, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        SeedSupplier(ctx, 10, "Forn A", "500100");
        SeedQuotation(ctx, req, 10, "Forn A", "AOA", 100, false, actor, mapTo: ready); // makes item 2 READY
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, actor, req.Id);
        var qResult = await Ctrl(ctx, actor, c => new BuyerQueueController(c)).GetQueue(query: "REQ-COV");
        var qpage = (BuyerQueuePageDto)((OkObjectResult)((ActionResult<BuyerQueuePageDto>)qResult).Result!).Value!;
        var row = qpage.Items.Single();

        Assert.Equal(row.ActiveItemCount, ws.Coverage.TotalItems);
        Assert.Equal(row.CoveredCount, ws.Coverage.Treated);
        Assert.Equal(row.PendingCount, ws.Coverage.Pending);
        Assert.Equal(row.CoverageStatus, ws.Coverage.CoverageStatus);
        // per-item buckets align: item 1 approved, item 2 ready, item 3 pending
        Assert.Equal(1, ws.Coverage.Approved);
        Assert.Equal(1, ws.Coverage.ReadyForBatch);
        Assert.Equal(1, ws.Coverage.Pending);
        Assert.Equal(BuyerQueueConstants.CoverageBuckets.QuotedReadyForBatch, ws.Items.Single(i => i.LineNumber == 2).CoverageBucket);
    }

    // ════════════════════ supplier involvement + per-currency (no aggregation) ════════════════════

    [Fact]
    public async Task Suppliers_Are_Only_Those_Involved_And_Currencies_Are_Not_Summed()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedRequest(ctx, actor, "REQ-SUP");
        var it = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        SeedSupplier(ctx, 20, "Envolvido", "500200");
        SeedSupplier(ctx, 21, "Não Envolvido", "500201"); // not quoted on this request
        SeedQuotation(ctx, req, 20, "Envolvido", "AOA", 500, true, actor, mapTo: it);
        // global track record for supplier 20: two issued POs in DIFFERENT currencies
        SeedIssuedPo(ctx, req, 20, "PO-1", "AOA", 1000, actor);
        SeedIssuedPo(ctx, req, 20, "PO-2", "EUR", 200, actor);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, actor, req.Id);
        var sup = Assert.Single(ws.Suppliers);
        Assert.Equal("Envolvido", sup.Name);
        Assert.Equal(2, sup.PurchaseCount);
        Assert.True(sup.InvolvedSelected);
        Assert.False(sup.CanOpenSheet); // Supplier Sheet deferred (INVASIVE)
        // currencies kept separate, never summed
        Assert.Equal(2, sup.TotalsByCurrency.Count);
        Assert.Equal(1000, sup.TotalsByCurrency.Single(c => c.Currency == "AOA").Amount);
        Assert.Equal(200, sup.TotalsByCurrency.Single(c => c.Currency == "EUR").Amount);
    }

    [Fact]
    public async Task Suppliers_Are_Deduped_By_Normalized_Nif()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedRequest(ctx, actor, "REQ-DEDUP");
        var it = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        // Two supplier records, SAME NIF in different raw formats → one dedup group.
        SeedSupplier(ctx, 30, "Forn (record A)", "5003 00");
        SeedSupplier(ctx, 31, "Forn (record B)", "500300");
        SeedQuotation(ctx, req, 30, "Forn A", "AOA", 100, false, actor, mapTo: it);
        SeedQuotation(ctx, req, 31, "Forn B", "AOA", 110, false, actor);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, actor, req.Id);
        Assert.Single(ws.Suppliers); // collapsed by normalized NIF 500300
        Assert.Equal(2, ws.Suppliers[0].QuotationsReceived); // metrics merged across both records
    }

    // ════════════════════ batch kind classification ════════════════════

    [Fact]
    public async Task Batches_Are_Classified_By_Kind()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedRequest(ctx, actor, "REQ-BATCH");
        var i1 = SeedItem(ctx, req, 1, RequestConstants.QuotationLifecycleStatuses.BatchAssigned);
        var i2 = SeedItem(ctx, req, 2, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        SeedBatch(ctx, req, 1, RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, actor, i1);
        SeedBatch(ctx, req, 2, RequestConstants.ApprovalBatchStatuses.Rejected, actor, i2);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, actor, req.Id);
        Assert.Equal("ACTIVE", ws.Batches.Single(b => b.BatchNumber == 1).Kind);
        Assert.Equal("REJECTED", ws.Batches.Single(b => b.BatchNumber == 2).Kind);
        Assert.Contains(1, ws.Batches.Single(b => b.BatchNumber == 1).ItemLineNumbers);
    }
}
