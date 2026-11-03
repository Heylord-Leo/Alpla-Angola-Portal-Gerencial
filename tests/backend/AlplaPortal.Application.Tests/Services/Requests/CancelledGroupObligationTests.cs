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
using AlplaPortal.Domain.Services;
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

using Agg = RequestConstants.OperationInvoiceStatuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// v2.249.4 — a CANCELLED P.O. group (DEC-151) carries no active final-invoice obligation. TEST finding on REQ-628: the
/// cancelled TREVOTECH group (250,000 AOA) still appeared as "Aguardando Fatura Final" with its full remaining balance and
/// the "Propor Encerramento com Saldo" action, because the obligations read model never looked at the group status and the
/// cancellation (by design) does not rewrite the group's cached invoice columns. Pinned here: the projector reports the
/// group as history (no obligation, null expected / remaining, CANCELLED, no drift, historical total kept), the rollup
/// excludes it (active = MULTIBIZ 120,000 + MAQSIS 230,000 = 350,000), the endpoint carries the flags, and the
/// short-close endpoints refuse the group (OI_GROUP_CANCELLED) — propose, approve and reject alike.
/// </summary>
public class CancelledGroupObligationTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static OperationInvoiceObligationGroupSnapshot Group(Guid id, string status, decimal expected, string persisted = Agg.PendingUpload, int historicalLines = 0, IReadOnlyList<Guid>? lines = null) => new()
    {
        GroupId = id, SourceDocumentType = Types.Proforma, ExpectedTotal = expected, ExpectedCurrency = "AOA", CurrencyCode = "AOA",
        PersistedStatus = persisted, GroupStatus = status, HistoricalLineItemCount = historicalLines, LineItemIds = lines ?? Array.Empty<Guid>(),
    };

    // ── Projector ──

    [Fact]
    public void A_cancelled_group_is_history_not_an_obligation()
    {
        var id = Guid.NewGuid();
        var o = Assert.Single(OperationInvoiceObligationProjector.Project(new[] { Group(id, RequestConstants.PoGroupStatuses.Cancelled, 250_000m, historicalLines: 1) }).Obligations);
        Assert.True(o.GroupCancelled);
        Assert.False(o.RequiresOperationInvoice);
        Assert.Equal(Agg.Cancelled, o.DerivedStatus);
        Assert.Null(o.ExpectedAmount); Assert.Null(o.RemainingAmount);
        Assert.Equal(250_000m, o.HistoricalExpectedAmount);
        Assert.Equal(1, o.HistoricalLineItemCount);
        Assert.Equal(0, o.LineItemCount);
        Assert.False(o.StatusDrift);                                     // the cached column is evidence, not drift
        Assert.Equal(Agg.PendingUpload, o.PersistedStatus);
        Assert.Equal(OperationInvoiceObligationReasons.GroupCancelled, o.ReasonCode);
        Assert.False(Agg.IsBlocking(o.DerivedStatus)); Assert.False(Agg.IsSatisfied(o.DerivedStatus)); Assert.False(Agg.AcceptsUploadIn(o.DerivedStatus));
    }

    [Fact]
    public void REQ_628_shape_active_total_is_350000_and_the_cancelled_250000_never_enters_the_rollup()
    {
        var projection = OperationInvoiceObligationProjector.Project(new[]
        {
            Group(Guid.NewGuid(), RequestConstants.PoGroupStatuses.Cancelled, 250_000m),   // TREVOTECH, cancelled
            Group(Guid.NewGuid(), RequestConstants.PoGroupStatuses.PoIssued, 120_000m),    // MULTIBIZ
            Group(Guid.NewGuid(), RequestConstants.PoGroupStatuses.WaitingPo, 230_000m),   // MAQSIS replacement
        });
        var r = projection.Rollup;
        Assert.Equal(3, r.TotalGroups);
        Assert.Equal(1, r.CancelledGroupCount);
        Assert.Equal(2, r.RequiringOperationInvoiceCount);
        Assert.Equal(2, r.PendingActionCount);
        var aoa = Assert.Single(r.CurrencyTotals);
        Assert.Equal(350_000m, aoa.ExpectedTotal); Assert.Equal(350_000m, aoa.RemainingTotal);
        Assert.Equal(0, r.DriftCount);
    }

    [Fact]
    public void A_live_group_without_a_status_in_the_snapshot_is_still_an_obligation()
    {
        var o = Assert.Single(OperationInvoiceObligationProjector.Project(new[] { Group(Guid.NewGuid(), null!, 10m) with { GroupStatus = null } }).Obligations);
        Assert.False(o.GroupCancelled); Assert.True(o.RequiresOperationInvoice); Assert.Equal(10m, o.ExpectedAmount);
    }

    // ── Endpoint + write guards ──

    private sealed record Seed(Guid RequestId, Guid ActorId, Guid DeciderId);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Cancelled Obligation Tester", Email = "co1@test.local" };
        var decider = new User { Id = Guid.NewGuid(), FullName = "Cancelled Obligation Decider", Email = "co2@test.local" };
        ctx.Users.AddRange(actor, decider);
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 14, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida", DisplayOrder = 14 });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "TREVOTECH", TaxId = "5417000001" });
        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "ZZTEST-REQ-628-" + Guid.NewGuid().ToString("N")[..6], Title = "ZZTEST cancelled group",
            RequestTypeId = 1, StatusId = 14, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        ctx.Requests.Add(request);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = actor.Id, PlantId = 1 });
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id, decider.Id);
    }

    private static RequestPoGroup AddGroup(ApplicationDbContext ctx, Seed seed, string status, decimal expected, string name)
    {
        var g = new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = seed.RequestId, SupplierId = 10, SupplierNameSnapshot = name, CurrencyCode = "AOA", TotalAmount = expected,
            Status = status, SourceDocumentType = Types.Proforma, OperationInvoiceStatus = Agg.PendingUpload, RequiresOperationInvoice = true,
            ExpectedOperationInvoiceTotal = expected, ExpectedOperationInvoiceCurrency = "AOA", PurchaseOrderNumber = "ECF99 2026/9001",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(g);
        return g;
    }

    private static RequestsController Requests(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance, new Mock<INotificationService>().Object, new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object, new Mock<IRequestStatusSyncService>().Object,
            new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object, new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions { Enabled = true }));
        controller.ControllerContext = Context(actorId, RoleConstants.Finance);
        return controller;
    }

    private static OperationInvoiceShortClosesController ShortCloses(ApplicationDbContext ctx, Guid actorId, string role)
    {
        var c = new OperationInvoiceShortClosesController(ctx, NullLogger<OperationInvoiceShortClosesController>.Instance, new AlplaPortal.Infrastructure.Services.OperationInvoiceCoverageService(ctx));
        c.ControllerContext = Context(actorId, role);
        return c;
    }

    private static ControllerContext Context(Guid actorId, string role) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, role) }, "Test")),
            RequestServices = new ServiceCollection().BuildServiceProvider()
        }
    };

    private static string Code(IActionResult r) => (string)Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(r).Value).Extensions["code"]!;

    [Fact]
    public async Task The_obligations_endpoint_reports_the_cancelled_group_as_history_and_the_active_total_without_it()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var cancelled = AddGroup(ctx, seed, RequestConstants.PoGroupStatuses.Cancelled, 250_000m, "TREVOTECH");
        var live1 = AddGroup(ctx, seed, RequestConstants.PoGroupStatuses.PoIssued, 120_000m, "MULTIBIZ");
        var live2 = AddGroup(ctx, seed, RequestConstants.PoGroupStatuses.WaitingPo, 230_000m, "MAQSIS");
        await ctx.SaveChangesAsync();

        var result = await Requests(ctx, seed.ActorId).GetOperationInvoiceObligations(seed.RequestId);
        var body = Assert.IsType<OperationInvoiceObligationsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        var c = body.Obligations.Single(o => o.GroupId == cancelled.Id);
        Assert.True(c.GroupCancelled); Assert.Equal(RequestConstants.PoGroupStatuses.Cancelled, c.GroupStatus);
        Assert.False(c.RequiresOperationInvoice); Assert.Equal(Agg.Cancelled, c.DerivedStatus);
        Assert.Null(c.ExpectedAmount); Assert.Null(c.RemainingAmount); Assert.Null(c.CoveragePercent);
        Assert.Equal(250_000m, c.HistoricalExpectedAmount);
        Assert.True(body.Obligations.Single(o => o.GroupId == live1.Id).RequiresOperationInvoice);
        Assert.True(body.Obligations.Single(o => o.GroupId == live2.Id).RequiresOperationInvoice);
        Assert.Equal(1, body.Rollup.CancelledGroupCount); Assert.Equal(2, body.Rollup.PendingActionCount);
        Assert.Equal(350_000m, Assert.Single(body.Rollup.CurrencyTotals).ExpectedTotal);
    }

    [Fact]
    public async Task Short_close_propose_approve_and_reject_refuse_a_cancelled_group()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var cancelled = AddGroup(ctx, seed, RequestConstants.PoGroupStatuses.Cancelled, 250_000m, "TREVOTECH");
        var proposal = new OperationInvoiceShortClose
        {
            Id = Guid.NewGuid(), RequestPoGroupId = cancelled.Id, Status = RequestConstants.ShortCloseStatuses.Proposed,
            RemainingAmountAtProposal = 250_000m, ProposalJustification = "ZZTEST proposta anterior ao cancelamento do grupo",
            ProposedByUserId = seed.ActorId, ProposedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.OperationInvoiceShortCloses.Add(proposal);
        await ctx.SaveChangesAsync();

        var propose = await ShortCloses(ctx, seed.ActorId, RoleConstants.Buyer).Propose(seed.RequestId, cancelled.Id, new ProposeOperationInvoiceShortCloseDto { Justification = "ZZTEST fornecedor não emitirá o valor remanescente" });
        Assert.Equal(OperationInvoiceShortClosesController.GroupCancelledCode, Code(propose));
        var approve = await ShortCloses(ctx, seed.DeciderId, RoleConstants.Finance).Approve(seed.RequestId, cancelled.Id, proposal.Id, new DecideOperationInvoiceShortCloseDto());
        Assert.Equal(OperationInvoiceShortClosesController.GroupCancelledCode, Code(approve));
        var reject = await ShortCloses(ctx, seed.DeciderId, RoleConstants.Finance).Reject(seed.RequestId, cancelled.Id, proposal.Id, new DecideOperationInvoiceShortCloseDto { DecisionReason = "ZZTEST grupo cancelado, proposta sem objeto" });
        Assert.Equal(OperationInvoiceShortClosesController.GroupCancelledCode, Code(reject));

        ctx.ChangeTracker.Clear();
        Assert.Equal(RequestConstants.ShortCloseStatuses.Proposed, (await ctx.OperationInvoiceShortCloses.AsNoTracking().SingleAsync(p => p.Id == proposal.Id)).Status);   // untouched evidence
        Assert.Equal(Agg.PendingUpload, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == cancelled.Id)).OperationInvoiceStatus);                        // no data edit
    }

    [Fact]
    public async Task A_live_sibling_group_keeps_its_obligation_and_its_short_close_path()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        AddGroup(ctx, seed, RequestConstants.PoGroupStatuses.Cancelled, 250_000m, "TREVOTECH");
        var live = AddGroup(ctx, seed, RequestConstants.PoGroupStatuses.WaitingReceipt, 120_000m, "MULTIBIZ");
        await ctx.SaveChangesAsync();
        var propose = await ShortCloses(ctx, seed.ActorId, RoleConstants.Buyer).Propose(seed.RequestId, live.Id, new ProposeOperationInvoiceShortCloseDto { Justification = "ZZTEST fornecedor não emitirá o valor remanescente" });
        Assert.IsType<OkObjectResult>(propose);
    }
}
