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
/// Phase 2 — the Buyer queue endpoint contract: Request-level counting/pagination, note-metadata
/// projection (page-slice only), notes never influencing operational state, and the generic
/// request note endpoint's auth/scope. Complements the pure-builder characterization
/// (<see cref="BuyerQueueProjectionBuilderTests"/>).
/// </summary>
public class BuyerQueueEndpointTests
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

    private static Request SeedQuotationRequest(ApplicationDbContext ctx, Guid actorId, string number, int itemCount, string? lifecycle,
        int companyId = 1, int plantId = 1, Guid? requesterId = null)
    {
        var req = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = number,
            Title = $"Pedido {number}",
            Description = "test",
            RequestTypeId = QuotationTypeId,
            StatusId = WaitingQuotationStatusId,
            RequesterId = requesterId ?? actorId,
            CreatedByUserId = actorId,
            BuyerId = actorId,
            DepartmentId = 1,
            CompanyId = companyId,
            PlantId = plantId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(req);
        for (var i = 0; i < itemCount; i++)
            ctx.RequestLineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = i + 1,
                Description = $"Item {i + 1}", Quantity = 1, UnitPrice = 10, TotalAmount = 10,
                QuotationLifecycleStatus = lifecycle
            });
        return req;
    }

    private static void AddNoteHistory(ApplicationDbContext ctx, Guid requestId, Guid actorId, string text, DateTime at)
        => ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(), RequestId = requestId, ActorUserId = actorId,
            ActionTaken = RequestConstants.StatusHistoryActions.Note,
            PreviousStatusId = WaitingQuotationStatusId, NewStatusId = WaitingQuotationStatusId,
            Comment = text, CreatedAtUtc = at
        });

    private static BuyerQueueController BuildQueueController(ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.SystemAdministrator)
    {
        var controller = new BuyerQueueController(ctx);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, role)
                }, "Test"))
            }
        };
        return controller;
    }

    private static async Task<Guid> SeedActorAsync(ApplicationDbContext ctx)
    {
        var id = Guid.NewGuid();
        ctx.Users.Add(new User { Id = id, FullName = "Comprador Teste", Email = "buyer@test.local", IsActive = true });
        await ctx.SaveChangesAsync();
        return id;
    }

    // ════════════════════ need-level filter (Phase 3E.2) ════════════════════

    [Fact]
    public async Task NeedLevelFilter_ScopesListAndSummary_Consistently()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        ctx.NeedLevels.Add(new NeedLevel { Id = 1, Code = "CRITICO", Name = "Crítico" });
        ctx.NeedLevels.Add(new NeedLevel { Id = 2, Code = "NORMAL", Name = "Normal" });
        var actor = await SeedActorAsync(ctx);
        var critical = SeedQuotationRequest(ctx, actor, "REQ-CRIT", 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        critical.NeedLevelId = 1;
        var normal = SeedQuotationRequest(ctx, actor, "REQ-NORM", 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        normal.NeedLevelId = 2;
        await ctx.SaveChangesAsync();

        // No filter → both; needLevel=CRITICO → only the critical one, and the summary Total agrees.
        var all = (BuyerQueuePageDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetQueue()).Result!).Value!;
        Assert.Equal(2, all.TotalCount);

        var critList = (BuyerQueuePageDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetQueue(needLevel: "CRITICO")).Result!).Value!;
        Assert.Equal(1, critList.TotalCount);
        Assert.Equal("REQ-CRIT", critList.Items.Single().RequestNumber);

        var critSummary = (BuyerQueueSummaryDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetSummary(needLevel: "CRITICO")).Result!).Value!;
        Assert.Equal(1, critSummary.Total); // cards share the same need-level scope as the list
    }

    // ════════════════════ note-metadata projection ════════════════════

    [Fact]
    public async Task Queue_Projects_LatestNote_And_Count()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var other = Guid.NewGuid();
        ctx.Users.Add(new User { Id = other, FullName = "Outro Ator", Email = "o@test.local", IsActive = true });
        var req = SeedQuotationRequest(ctx, actor, "REQ-N1", 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        AddNoteHistory(ctx, req.Id, actor, "primeira observação", DateTime.UtcNow.AddHours(-2));
        AddNoteHistory(ctx, req.Id, other, "observação mais recente", DateTime.UtcNow.AddMinutes(-5));
        await ctx.SaveChangesAsync();

        var result = await BuildQueueController(ctx, actor).GetQueue();
        var page = Assert.IsType<BuyerQueuePageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var row = page.Items.Single(i => i.RequestNumber == "REQ-N1");

        Assert.True(row.HasNotes);
        Assert.Equal(2, row.NoteCount);
        Assert.Equal("observação mais recente", row.LatestNoteText);
        Assert.Equal("Outro Ator", row.LatestNoteActorName);
    }

    [Fact]
    public async Task Queue_Row_Without_Notes_Has_NoNoteIndicator()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        SeedQuotationRequest(ctx, actor, "REQ-N0", 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var result = await BuildQueueController(ctx, actor).GetQueue();
        var page = (BuyerQueuePageDto)((OkObjectResult)result.Result!).Value!;
        var row = page.Items.Single();
        Assert.False(row.HasNotes);
        Assert.Equal(0, row.NoteCount);
        Assert.Null(row.LatestNoteText);
    }

    [Fact]
    public async Task Notes_Do_Not_Change_OperationalState()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedQuotationRequest(ctx, actor, "REQ-S1", 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var before = (BuyerQueuePageDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetQueue()).Result!).Value!;
        var stateBefore = before.Items.Single().OperationalState;

        AddNoteHistory(ctx, req.Id, actor, "n1", DateTime.UtcNow.AddMinutes(-3));
        AddNoteHistory(ctx, req.Id, actor, "n2", DateTime.UtcNow.AddMinutes(-2));
        await ctx.SaveChangesAsync();

        var after = (BuyerQueuePageDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetQueue()).Result!).Value!;
        var afterRow = after.Items.Single();
        Assert.Equal(stateBefore, afterRow.OperationalState);
        Assert.Equal(BuyerQueueConstants.OperationalStates.NeedsQuotation, afterRow.OperationalState);
        Assert.Equal(2, afterRow.NoteCount);
    }

    // ════════════════════ Request-level counting / pagination ════════════════════

    [Fact]
    public async Task Summary_Counts_Requests_Not_LineItems()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        SeedQuotationRequest(ctx, actor, "REQ-M1", 3, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        var result = await BuildQueueController(ctx, actor).GetSummary();
        var summary = (BuyerQueueSummaryDto)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(1, summary.Total); // one Request, despite 3 line items
    }

    [Fact]
    public async Task MultiItem_Request_Appears_Once_And_Never_Splits_Across_Pages()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        SeedQuotationRequest(ctx, actor, "REQ-BIG", 5, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
        await ctx.SaveChangesAsync();

        // pageSize=1 would split a line-item-paginated list into 5 pages; Request-level keeps it as 1.
        var result = await BuildQueueController(ctx, actor).GetQueue(pageSize: 1, page: 1);
        var page = (BuyerQueuePageDto)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(1, page.TotalCount);      // ONE Request
        Assert.Single(page.Items);             // the whole Request on one page
        Assert.Equal(5, page.Items[0].ActiveItemCount); // all 5 items hydrated onto the single row
    }

    // ════════════════════ requester projection ════════════════════

    [Fact]
    public async Task Queue_Projects_RequesterName_From_Canonical_Relation()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = await SeedActorAsync(ctx);
        var requester = Guid.NewGuid();
        ctx.Users.Add(new User { Id = requester, FullName = "Ana Solicitante", Email = "ana@test.local", IsActive = true });
        SeedQuotationRequest(ctx, buyer, "REQ-REQ", 1, RequestConstants.QuotationLifecycleStatuses.QuotationPending, requesterId: requester);
        await ctx.SaveChangesAsync();

        var page = (BuyerQueuePageDto)((OkObjectResult)(await BuildQueueController(ctx, buyer).GetQueue()).Result!).Value!;
        var row = page.Items.Single(i => i.RequestNumber == "REQ-REQ");
        Assert.Equal("Ana Solicitante", row.RequesterName);
        Assert.Equal(requester, row.RequesterId);
        Assert.Equal("Comprador Teste", row.BuyerName); // buyer distinct from requester
    }

    // ════════════════════ company filter (list + summary, no cross-company leak) ════════════════════

    private static void SeedTwoCompanies(ApplicationDbContext ctx)
    {
        ctx.Companies.Add(new Company { Id = 2, Name = "ALPLA SOPRO" });
        ctx.Plants.Add(new Plant { Id = 2, Name = "Viana 3", CompanyId = 2 });
    }

    [Fact]
    public async Task Company_Filter_Scopes_List_And_Never_Leaks_CrossCompany()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        SeedTwoCompanies(ctx);
        var actor = await SeedActorAsync(ctx);
        SeedQuotationRequest(ctx, actor, "REQ-C1a", 1, null, companyId: 1, plantId: 1);
        SeedQuotationRequest(ctx, actor, "REQ-C1b", 1, null, companyId: 1, plantId: 1);
        SeedQuotationRequest(ctx, actor, "REQ-C2", 1, null, companyId: 2, plantId: 2);
        await ctx.SaveChangesAsync();

        var c1 = (BuyerQueuePageDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetQueue(company: 1)).Result!).Value!;
        Assert.Equal(2, c1.TotalCount);
        Assert.All(c1.Items, i => Assert.StartsWith("REQ-C1", i.RequestNumber));
        Assert.DoesNotContain(c1.Items, i => i.RequestNumber == "REQ-C2");

        var c2 = (BuyerQueuePageDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetQueue(company: 2)).Result!).Value!;
        Assert.Equal(1, c2.TotalCount);
        Assert.Equal("REQ-C2", c2.Items.Single().RequestNumber);
    }

    [Fact]
    public async Task Company_Filter_Scopes_Summary_Identically_To_List()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        SeedTwoCompanies(ctx);
        var actor = await SeedActorAsync(ctx);
        SeedQuotationRequest(ctx, actor, "REQ-S1a", 1, null, companyId: 1, plantId: 1);
        SeedQuotationRequest(ctx, actor, "REQ-S1b", 1, null, companyId: 1, plantId: 1);
        SeedQuotationRequest(ctx, actor, "REQ-S2", 1, null, companyId: 2, plantId: 2);
        await ctx.SaveChangesAsync();

        var s1 = (BuyerQueueSummaryDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetSummary(company: 1)).Result!).Value!;
        Assert.Equal(2, s1.Total); // matches the list scope, never the whole set
        var s2 = (BuyerQueueSummaryDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetSummary(company: 2)).Result!).Value!;
        Assert.Equal(1, s2.Total);
    }

    [Fact]
    public async Task Company_And_Plant_Combined_Narrow_Together()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        SeedTwoCompanies(ctx);
        ctx.Plants.Add(new Plant { Id = 3, Name = "Viana 1b", CompanyId = 1 });
        var actor = await SeedActorAsync(ctx);
        SeedQuotationRequest(ctx, actor, "REQ-P1", 1, null, companyId: 1, plantId: 1);
        SeedQuotationRequest(ctx, actor, "REQ-P3", 1, null, companyId: 1, plantId: 3);
        SeedQuotationRequest(ctx, actor, "REQ-P2", 1, null, companyId: 2, plantId: 2);
        await ctx.SaveChangesAsync();

        var r = (BuyerQueuePageDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetQueue(company: 1, plant: 1)).Result!).Value!;
        Assert.Equal(1, r.TotalCount);
        Assert.Equal("REQ-P1", r.Items.Single().RequestNumber);
    }

    // ════════════════════ generic request note endpoint (auth/scope) ════════════════════

    private static RequestsController BuildRequestsController(ApplicationDbContext ctx, Guid actorId, string role)
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
            new Mock<IRequestStatusSyncService>().Object,
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

    [Fact]
    public async Task AddNote_Persists_Observacao_History_InScope()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedQuotationRequest(ctx, actor, "REQ-ADD", 1, null);
        await ctx.SaveChangesAsync();

        var result = await BuildRequestsController(ctx, actor, RoleConstants.SystemAdministrator)
            .AddNote(req.Id, new RequestNoteDto { Text = "  minha observação  " });

        Assert.IsType<OkObjectResult>(result);
        var hist = ctx.RequestStatusHistories.Single(h => h.RequestId == req.Id && h.ActionTaken == RequestConstants.StatusHistoryActions.Note);
        Assert.Equal("minha observação", hist.Comment); // trimmed
        Assert.Equal(actor, hist.ActorUserId);
    }

    [Fact]
    public async Task AddNote_Empty_Text_Is_BadRequest()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var req = SeedQuotationRequest(ctx, actor, "REQ-EMPTY", 1, null);
        await ctx.SaveChangesAsync();

        var result = await BuildRequestsController(ctx, actor, RoleConstants.SystemAdministrator)
            .AddNote(req.Id, new RequestNoteDto { Text = "   " });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(ctx.RequestStatusHistories.Where(h => h.ActionTaken == RequestConstants.StatusHistoryActions.Note));
    }

    [Fact]
    public async Task AddNote_OutOfScope_Is_NotFound_And_Persists_Nothing()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        // A second plant the actor is NOT scoped to.
        ctx.Plants.Add(new Plant { Id = 2, Name = "Viana 3", CompanyId = 1 });
        var owner = await SeedActorAsync(ctx);
        var outsider = Guid.NewGuid();
        ctx.Users.Add(new User { Id = outsider, FullName = "Fora do Escopo", Email = "x@test.local", IsActive = true });
        // Outsider (non-admin) is scoped ONLY to plant 2; the request lives in plant 1.
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = outsider, PlantId = 2 });
        var req = SeedQuotationRequest(ctx, owner, "REQ-SCOPE", 1, null); // PlantId = 1
        await ctx.SaveChangesAsync();

        var result = await BuildRequestsController(ctx, outsider, RoleConstants.Buyer)
            .AddNote(req.Id, new RequestNoteDto { Text = "não deveria persistir" });

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(ctx.RequestStatusHistories.Where(h => h.ActionTaken == RequestConstants.StatusHistoryActions.Note));
    }
}
