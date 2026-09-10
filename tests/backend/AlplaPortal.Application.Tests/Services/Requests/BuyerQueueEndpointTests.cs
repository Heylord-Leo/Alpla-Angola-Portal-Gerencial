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

    // ════════════════════ v2.242.0 — PO correction visibility ════════════════════

    private static async Task<(Guid reqId, Guid kronesGroupId)> SeedCorrectionRequestAsync(
        ApplicationDbContext ctx, Guid actorId, string number, string kronesStatus)
    {
        if (!ctx.RequestStatuses.Any(s => s.Id == 3))
            ctx.RequestStatuses.Add(new RequestStatus { Id = 3, Code = RequestConstants.Statuses.PoPartiallyUploaded, Name = "P.O Parcial" });
        var req = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = number, Title = $"Pedido {number}", Description = "t",
            RequestTypeId = QuotationTypeId, StatusId = 3, RequesterId = actorId, CreatedByUserId = actorId,
            BuyerId = actorId, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(req);
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 1, Description = "Item", Quantity = 1, UnitPrice = 10,
            TotalAmount = 10, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved
        });
        var krones = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, SupplierNameSnapshot = "KRONES ANGOLA", CurrencyCode = "AOA", TotalAmount = 100m, Status = kronesStatus, PurchaseOrderNumber = "ECF10 2026/283", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actorId };
        var civ = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, SupplierNameSnapshot = "CIVIPARTS", CurrencyCode = "AOA", TotalAmount = 50m, Status = RequestConstants.PoGroupStatuses.AdvancePaymentRequired, PurchaseOrderNumber = "ECF10 2026/125", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actorId };
        ctx.RequestPoGroups.AddRange(krones, civ);
        await ctx.SaveChangesAsync();
        return (req.Id, krones.Id);
    }

    private static BuyerQueuePageDto Page(Microsoft.AspNetCore.Mvc.ActionResult<BuyerQueuePageDto> r) =>
        (BuyerQueuePageDto)((OkObjectResult)r.Result!).Value!;

    [Fact]
    public async Task PoCorrection_MixedRequest_AppearsOnce_AsPoCorrection_WithSupplier()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        var (reqId, _) = await SeedCorrectionRequestAsync(ctx, actor, "REQ-20/08/2026-275", RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        var page = Page(await BuildQueueController(ctx, actor).GetQueue());
        var row = Assert.Single(page.Items.Where(i => i.RequestId == reqId));
        Assert.Equal(BuyerQueueConstants.OperationalStates.PoCorrection, row.OperationalState);
        Assert.True(row.RequiresAttention);
        Assert.Single(row.PoCorrectionGroups);
        Assert.Equal("KRONES ANGOLA", row.PoCorrectionGroups[0].SupplierName);
        Assert.Equal("ECF10 2026/283", row.PoCorrectionGroups[0].PurchaseOrderNumber);
        // v2.242.0 — a QUOTATION correction keeps Request.BuyerId semantics (not neutralized).
        Assert.Equal("QUOTATION", row.RequestTypeCode);
        Assert.Equal(actor, row.BuyerId);
    }

    [Fact]
    public async Task PoCorrection_Summary_Counts_Once_And_Search_Finds_It()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        await SeedCorrectionRequestAsync(ctx, actor, "REQ-20/08/2026-275", RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        var summary = (BuyerQueueSummaryDto)((OkObjectResult)(await BuildQueueController(ctx, actor).GetSummary()).Result!).Value!;
        Assert.Equal(1, summary.PoCorrections);
        Assert.True(summary.RequiresAttention >= 1);

        var searched = Page(await BuildQueueController(ctx, actor).GetQueue(query: "275"));
        Assert.Single(searched.Items);
    }

    [Fact]
    public async Task PoCorrection_Respects_MyOwnership()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var owner = await SeedActorAsync(ctx);
        var other = await SeedActorAsync(ctx);
        await SeedCorrectionRequestAsync(ctx, owner, "REQ-OWN-275", RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        // Assigned buyer sees it under "me"; a different actor does not.
        Assert.Single(Page(await BuildQueueController(ctx, owner).GetQueue(ownership: "me")).Items);
        Assert.Empty(Page(await BuildQueueController(ctx, other).GetQueue(ownership: "me")).Items);
    }

    [Fact]
    public async Task PoCorrection_Gone_After_Group_Leaves_Correction()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var actor = await SeedActorAsync(ctx);
        // Group already re-registered (PO_ISSUED) with a still-PO_PARTIALLY_UPLOADED scalar and no other
        // Buyer work → the request is not admitted to the queue.
        var (reqId, _) = await SeedCorrectionRequestAsync(ctx, actor, "REQ-DONE-275", RequestConstants.PoGroupStatuses.PoIssued);

        var page = Page(await BuildQueueController(ctx, actor).GetQueue());
        Assert.DoesNotContain(page.Items, i => i.RequestId == reqId);
    }

    // ════════════════════ v2.242.0 — footer-sticker ownership (summary endpoint) ════════════════════
    // The PO-correction footer sticker reads GetSummary(...).PoCorrections. It must be PERSONAL
    // (ownership=me) so only the assigned Buyer is notified — the incident where REQ-275 (Samuel's)
    // surfaced on Celestina's sticker because the hook fetched an unscoped ('all') summary.

    private static BuyerQueueSummaryDto Sum(Microsoft.AspNetCore.Mvc.ActionResult<BuyerQueueSummaryDto> r) =>
        (BuyerQueueSummaryDto)((OkObjectResult)r.Result!).Value!;

    [Fact]
    public async Task PoCorrection_Summary_MeScope_CountsOnlyOwnRequests()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var owner = await SeedActorAsync(ctx);   // Samuel — owns REQ-275
        var other = await SeedActorAsync(ctx);   // Celestina — same broad scope, owns nothing
        await SeedCorrectionRequestAsync(ctx, owner, "REQ-20/08/2026-275", RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        // ownership=me: owner sees 1 (personal), other sees 0 (not their work) — this is the fix.
        Assert.Equal(1, Sum(await BuildQueueController(ctx, owner, RoleConstants.Buyer).GetSummary(ownership: "me")).PoCorrections);
        Assert.Equal(0, Sum(await BuildQueueController(ctx, other, RoleConstants.Buyer).GetSummary(ownership: "me")).PoCorrections);

        // ownership=all (the previous behavior) leaks the org-scoped count to a non-owner Buyer whose
        // access scope contains the request (no plant/dept scope rows → unfiltered) — the old bug.
        Assert.Equal(1, Sum(await BuildQueueController(ctx, other, RoleConstants.Buyer).GetSummary(ownership: "all")).PoCorrections);
    }

    [Fact]
    public async Task PoCorrection_Summary_MeScope_ExcludesUnassigned()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = await SeedActorAsync(ctx);
        var (reqId, _) = await SeedCorrectionRequestAsync(ctx, buyer, "REQ-UNASSIGNED-275", RequestConstants.PoGroupStatuses.WaitingPoCorrection);
        // Unassign the correction request (BuyerId == null).
        var req = ctx.Requests.Single(r => r.Id == reqId);
        req.BuyerId = null;
        await ctx.SaveChangesAsync();

        // A personal (me) summary never counts unassigned work; it stays in the "Não Atribuídos" pool only.
        Assert.Equal(0, Sum(await BuildQueueController(ctx, buyer, RoleConstants.Buyer).GetSummary(ownership: "me")).PoCorrections);
        Assert.Equal(1, Sum(await BuildQueueController(ctx, buyer, RoleConstants.Buyer).GetSummary(ownership: "unassigned")).PoCorrections);
    }

    [Fact]
    public async Task PoCorrection_Summary_MeScope_PaymentCorrection_OwnedByAnotherBuyer_NotCounted()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var me = await SeedActorAsync(ctx);
        var otherBuyer = await SeedActorAsync(ctx);
        // v2.242.0 Phase 1 — a PAYMENT WAITING_PO_CORRECTION group (REQ-254 class) IS admitted to the
        // queue now, but its ownership is the group's PoResponsibleBuyerId (PAYMENT carries no BuyerId).
        // Owned by another Buyer → must NOT count toward my personal correction summary.
        await SeedPaymentCorrectionAsync(ctx, "REQ-14/08/2026-254", poResponsible: otherBuyer, RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        Assert.Equal(0, Sum(await BuildQueueController(ctx, me, RoleConstants.Buyer).GetSummary(ownership: "me")).PoCorrections);
        // And the actual owner DOES see it (confirms admission works, only ownership scopes it out above).
        Assert.Equal(1, Sum(await BuildQueueController(ctx, otherBuyer, RoleConstants.Buyer).GetSummary(ownership: "me")).PoCorrections);
    }

    [Fact]
    public async Task PoCorrection_Summary_MeScope_MultipleCorrectionGroups_CountsRequestOnce()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = await SeedActorAsync(ctx);
        var (reqId, _) = await SeedCorrectionRequestAsync(ctx, buyer, "REQ-MULTI-275", RequestConstants.PoGroupStatuses.WaitingPoCorrection);
        // Return the sibling (CIVIPARTS) group for correction too → two WAITING_PO_CORRECTION groups, one request.
        foreach (var g in ctx.RequestPoGroups.Where(g => g.RequestId == reqId))
            g.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection;
        await ctx.SaveChangesAsync();

        // Request-based count: two returned groups on one request still count as 1.
        Assert.Equal(1, Sum(await BuildQueueController(ctx, buyer, RoleConstants.Buyer).GetSummary(ownership: "me")).PoCorrections);
    }

    // ════════════════════ v2.242.0 — cross-type personal PO-correction count endpoint ════════════════════
    // RequestsController.GetPersonalPoCorrectionsCount — the footer sticker's source. QUOTATION owner =
    // Request.BuyerId; PAYMENT owner = RequestPoGroup.PoResponsibleBuyerId (no BuyerId on PAYMENT).

    private static void SeedPaymentType(ApplicationDbContext ctx)
    {
        if (!ctx.RequestTypes.Any(t => t.Id == 9))
            ctx.RequestTypes.Add(new RequestType { Id = 9, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        if (!ctx.RequestStatuses.Any(s => s.Id == 3))
            ctx.RequestStatuses.Add(new RequestStatus { Id = 3, Code = RequestConstants.Statuses.PoPartiallyUploaded, Name = "P.O Parcial" });
    }

    private static async Task<Guid> SeedPaymentCorrectionAsync(ApplicationDbContext ctx, string number, Guid? poResponsible, string groupStatus)
    {
        SeedPaymentType(ctx);
        var requester = Guid.NewGuid();
        ctx.Users.Add(new User { Id = requester, FullName = "Solicitante Pagamento", Email = $"req-{requester:N}@t.local", IsActive = true });
        var reqId = Guid.NewGuid();
        ctx.Requests.Add(new Request
        {
            Id = reqId, RequestNumber = number, Title = number, Description = "t", RequestTypeId = 9, StatusId = 3,
            RequesterId = requester, CreatedByUserId = requester, BuyerId = null,
            DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = reqId, SupplierNameSnapshot = "SIMOTECNICA", CurrencyCode = "AOA",
            TotalAmount = 100m, Status = groupStatus, PurchaseOrderNumber = "FAC2025/125",
            CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = Guid.NewGuid(), PoResponsibleBuyerId = poResponsible
        });
        await ctx.SaveChangesAsync();
        return reqId;
    }

    private static int CountVal(IActionResult r)
    {
        var val = ((OkObjectResult)r).Value!;
        return (int)val.GetType().GetProperty("count")!.GetValue(val)!;
    }

    [Fact]
    public async Task PersonalCount_Quotation_countsForAssignedBuyer_notOthers()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var samuel = await SeedActorAsync(ctx);
        var celestina = await SeedActorAsync(ctx);
        await SeedCorrectionRequestAsync(ctx, samuel, "REQ-275", RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        Assert.Equal(1, CountVal(await BuildRequestsController(ctx, samuel, RoleConstants.Buyer).GetPersonalPoCorrectionsCount()));
        Assert.Equal(0, CountVal(await BuildRequestsController(ctx, celestina, RoleConstants.Buyer).GetPersonalPoCorrectionsCount()));
    }

    [Fact]
    public async Task PersonalCount_Payment_countsForPoResponsibleBuyer_notOthers()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var celestina = await SeedActorAsync(ctx);
        var samuel = await SeedActorAsync(ctx);
        await SeedPaymentCorrectionAsync(ctx, "REQ-254", poResponsible: celestina, RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        // Celestina owns the PAYMENT P.O. (registrant) even though BuyerId is null; Samuel does not.
        Assert.Equal(1, CountVal(await BuildRequestsController(ctx, celestina, RoleConstants.Buyer).GetPersonalPoCorrectionsCount()));
        Assert.Equal(0, CountVal(await BuildRequestsController(ctx, samuel, RoleConstants.Buyer).GetPersonalPoCorrectionsCount()));
    }

    [Fact]
    public async Task PersonalCount_excludesStalePaymentGroup_and_unresolvedOwner()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var celestina = await SeedActorAsync(ctx);
        await SeedPaymentCorrectionAsync(ctx, "REQ-STALE", poResponsible: celestina, RequestConstants.PoGroupStatuses.PoIssued); // group already PO_ISSUED
        await SeedPaymentCorrectionAsync(ctx, "REQ-UNOWNED", poResponsible: null, RequestConstants.PoGroupStatuses.WaitingPoCorrection); // no owner

        Assert.Equal(0, CountVal(await BuildRequestsController(ctx, celestina, RoleConstants.Buyer).GetPersonalPoCorrectionsCount()));
    }

    [Fact]
    public async Task PersonalCount_isZeroForNonBuyerRole()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var user = await SeedActorAsync(ctx);
        await SeedPaymentCorrectionAsync(ctx, "REQ-254", poResponsible: user, RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        // Even though the SysAdmin would "see" it, personal ownership is Buyer-gated at the endpoint.
        Assert.Equal(0, CountVal(await BuildRequestsController(ctx, user, RoleConstants.SystemAdministrator).GetPersonalPoCorrectionsCount()));
    }

    // ════════════════════ v2.242.0 Phase 1 — BuyerQueue cross-type admission ════════════════════

    [Fact]
    public async Task Queue_Admits_PaymentCorrection_AsPoCorrection_ForOwner_NotOthers()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var celestina = await SeedActorAsync(ctx);
        var samuel = await SeedActorAsync(ctx);
        var reqId = await SeedPaymentCorrectionAsync(ctx, "REQ-14/08/2026-254", poResponsible: celestina, RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        // Todos (all): the PAYMENT correction is admitted and projects as PO_CORRECTION.
        var all = Page(await BuildQueueController(ctx, celestina, RoleConstants.Buyer).GetQueue());
        var row = Assert.Single(all.Items.Where(i => i.RequestId == reqId));
        Assert.Equal(BuyerQueueConstants.OperationalStates.PoCorrection, row.OperationalState);
        Assert.True(row.RequiresAttention);
        Assert.Equal("SIMOTECNICA", row.PoCorrectionGroups.Single().SupplierName);
        // v2.242.0 display fix — the Buyer comes from the WPC group's PoResponsibleBuyerId (Celestina),
        // NOT "Não atribuído"; ownership reads MINE for the owner; quotation progress is neutralized.
        Assert.Equal(celestina, row.BuyerId);
        Assert.NotNull(row.BuyerName);
        Assert.Equal(BuyerQueueConstants.OwnershipStates.Mine, row.OwnershipState);
        Assert.Equal("PAYMENT", row.RequestTypeCode);
        Assert.Equal(0, row.ActiveItemCount);
        Assert.Equal(0, row.CoveredCount);
        Assert.Equal(0, row.PendingCount);

        // Meus Pedidos: owner (Celestina) sees it via PoResponsibleBuyerId; a different Buyer does not.
        Assert.Single(Page(await BuildQueueController(ctx, celestina, RoleConstants.Buyer).GetQueue(ownership: "me")).Items.Where(i => i.RequestId == reqId));
        Assert.Empty(Page(await BuildQueueController(ctx, samuel, RoleConstants.Buyer).GetQueue(ownership: "me")).Items.Where(i => i.RequestId == reqId));
    }

    [Fact]
    public async Task Queue_Excludes_NormalPayment_WithoutLiveCorrection()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = await SeedActorAsync(ctx);
        var reqId = await SeedPaymentCorrectionAsync(ctx, "REQ-NORMAL-PAY", poResponsible: buyer, RequestConstants.PoGroupStatuses.PoIssued); // no WPC group

        var all = Page(await BuildQueueController(ctx, buyer, RoleConstants.Buyer).GetQueue());
        Assert.DoesNotContain(all.Items, i => i.RequestId == reqId);
    }

    [Fact]
    public async Task Queue_Summary_PaymentCorrection_FeedsPoCorrections_NotQuotationCounts()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var celestina = await SeedActorAsync(ctx);
        await SeedPaymentCorrectionAsync(ctx, "REQ-254", poResponsible: celestina, RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        var s = Sum(await BuildQueueController(ctx, celestina, RoleConstants.Buyer).GetSummary());
        Assert.Equal(1, s.PoCorrections);
        Assert.True(s.RequiresAttention >= 1);
        Assert.Equal(1, s.Total);
        // Must NOT inflate quotation-coverage counters.
        Assert.Equal(0, s.NeedsAction);       // NeedsQuotation/PartialCoverage/ReadyForApproval/AdjustmentRequired
        Assert.Equal(0, s.AwaitingApproval);
    }

    // ════════════════════ v2.242.0 Phase 1 — my-actions personal projection ════════════════════

    private static MyActionsResponseDto Actions(Microsoft.AspNetCore.Mvc.ActionResult<MyActionsResponseDto> r) =>
        (MyActionsResponseDto)((OkObjectResult)r.Result!).Value!;

    [Fact]
    public async Task MyActions_PaymentCorrection_ForOwnerOnly()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var celestina = await SeedActorAsync(ctx);
        var samuel = await SeedActorAsync(ctx);
        await SeedPaymentCorrectionAsync(ctx, "REQ-14/08/2026-254", poResponsible: celestina, RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        var cel = Actions(await BuildRequestsController(ctx, celestina, RoleConstants.Buyer).GetMyActions());
        var item = Assert.Single(cel.Items.Where(i => i.ActionType == "PO_CORRECTION"));
        Assert.Equal("REQ-14/08/2026-254", item.RequestNumber);
        Assert.Equal("SIMOTECNICA", item.SupplierName);
        Assert.Contains(cel.Categories, c => c.ActionType == "PO_CORRECTION" && c.Count == 1);
        Assert.Contains("action=PO_CORRECTION", item.Route);
        Assert.Contains("poGroupId=", item.Route);

        var sam = Actions(await BuildRequestsController(ctx, samuel, RoleConstants.Buyer).GetMyActions());
        Assert.DoesNotContain(sam.Items, i => i.ActionType == "PO_CORRECTION" && i.RequestNumber == "REQ-14/08/2026-254");
    }

    [Fact]
    public async Task MyActions_QuotationCorrection_ForBuyer()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var samuel = await SeedActorAsync(ctx);
        await SeedCorrectionRequestAsync(ctx, samuel, "REQ-20/08/2026-275", RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        var sam = Actions(await BuildRequestsController(ctx, samuel, RoleConstants.Buyer).GetMyActions(actionType: "PO_CORRECTION"));
        Assert.Single(sam.Items.Where(i => i.RequestNumber == "REQ-20/08/2026-275"));
    }

    [Fact]
    public async Task MyActions_ExcludesStalePaymentGroup()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var celestina = await SeedActorAsync(ctx);
        await SeedPaymentCorrectionAsync(ctx, "REQ-STALE", poResponsible: celestina, RequestConstants.PoGroupStatuses.PoIssued);

        var cel = Actions(await BuildRequestsController(ctx, celestina, RoleConstants.Buyer).GetMyActions());
        Assert.DoesNotContain(cel.Items, i => i.ActionType == "PO_CORRECTION");
    }

    [Fact]
    public async Task MyActions_TargetLookup_ReturnsItemBeyondFirstPage()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = await SeedActorAsync(ctx);
        // Seed several PAYMENT corrections owned by the buyer; page size 1 forces paging.
        Guid targetReq = Guid.Empty; Guid targetGroup = Guid.Empty;
        for (int i = 0; i < 4; i++)
        {
            var rid = await SeedPaymentCorrectionAsync(ctx, $"REQ-PC-{i:D2}", poResponsible: buyer, RequestConstants.PoGroupStatuses.WaitingPoCorrection);
            if (i == 3) { targetReq = rid; targetGroup = ctx.RequestPoGroups.Single(g => g.RequestId == rid).Id; }
        }

        var res = Actions(await BuildRequestsController(ctx, buyer, RoleConstants.Buyer)
            .GetMyActions(actionType: "PO_CORRECTION", page: 1, pageSize: 1, targetRequestId: targetReq, targetPoGroupId: targetGroup, targetActionType: "PO_CORRECTION"));
        Assert.Equal(4, res.TotalCount);
        Assert.Single(res.Items);
        Assert.Equal(targetReq, res.Items[0].RequestId); // target surfaced onto its own page
    }
}
