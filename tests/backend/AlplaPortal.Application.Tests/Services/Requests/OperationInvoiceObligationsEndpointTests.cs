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

using Doc = RequestConstants.OperationInvoiceDocumentStatuses;
using Agg = RequestConstants.OperationInvoiceStatuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// Release 4 Phase 1b: GET /api/v1/requests/{id}/operation-invoice-obligations.
///
/// <para>Follows the established controller-test precedent (FinalizeRequestPostPaymentGuardTests):
/// the controller instantiated directly over an EF Core InMemory context. The properties pinned
/// here are the endpoint's, not the projector's — gating, visibility scope, faithful assembly of
/// the projector inputs from persisted data, and above all that a GET changes nothing.</para>
/// </summary>
public class OperationInvoiceObligationsEndpointTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static readonly PostPaymentCompletionOptions Enabled = new() { Enabled = true };

    private static RequestsController BuildController(
        ApplicationDbContext ctx, Guid actorId, PostPaymentCompletionOptions options,
        string role = RoleConstants.Finance)
    {
        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(
                new Mock<IServiceScopeFactory>().Object,
                new Mock<IHttpContextAccessor>().Object,
                NullLogger<AdminLogWriter>.Instance),
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
            Options.Create(options));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, role)
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid ActorId);

    private static async Task<Seed> SeedRequestAsync(ApplicationDbContext ctx, int plantId = 1)
    {
        var actor = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Obligations Tester",
            Email = "obligations@test.local"
        };
        ctx.Users.Add(actor);

        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus
        {
            Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17
        });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-OBLIG-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST obligations endpoint",
            RequestTypeId = 2,
            StatusId = 16,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = plantId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static RequestPoGroup AddGroup(
        ApplicationDbContext ctx, Seed seed,
        string? type = Types.Proforma,
        decimal? expected = 10_000_000m,
        string persisted = Agg.PendingUpload,
        string? currency = "AOA",
        string? poNumber = null)
    {
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = currency,
            TotalAmount = expected ?? 0m,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = type,
            OperationInvoiceStatus = persisted,
            ExpectedOperationInvoiceTotal = expected,
            ExpectedOperationInvoiceCurrency = expected.HasValue ? currency : null,
            PurchaseOrderNumber = poNumber,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        return group;
    }

    private static void AddLine(
        ApplicationDbContext ctx, Seed seed, Guid groupId, int lineNumber, Guid? sourceDocumentId)
    {
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            LineNumber = lineNumber,
            Description = "ZZTEST item " + lineNumber,
            Quantity = 1m,
            UnitPrice = 100m,
            TotalAmount = 100m,
            RequestPoGroupId = groupId,
            PaymentSourceDocumentId = sourceDocumentId
        });
    }

    private static OperationInvoice AddInvoiceWithAllocation(
        ApplicationDbContext ctx, Seed seed, Guid groupId,
        string invoiceStatus, decimal gross, int sequence,
        Guid? supersededByInvoiceId = null)
    {
        var invoice = new OperationInvoice
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            Status = invoiceStatus,
            GrossAmount = gross,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            UploadedByUserId = seed.ActorId,
            SupersededByOperationInvoiceId = supersededByInvoiceId
        };
        ctx.OperationInvoices.Add(invoice);

        ctx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
        {
            Id = Guid.NewGuid(),
            OperationInvoiceId = invoice.Id,
            RequestPoGroupId = groupId,
            AllocatedGrossAmount = gross,
            SequenceNumber = sequence
        });
        return invoice;
    }

    private static OperationInvoiceObligationsDto Body(
        ActionResult<OperationInvoiceObligationsDto> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<OperationInvoiceObligationsDto>(ok.Value);
    }

    // ── Gating ──

    [Fact]
    public async Task While_the_feature_is_disabled_the_endpoint_does_not_exist()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        AddGroup(ctx, seed);
        await ctx.SaveChangesAsync();

        // Default options: Enabled = false — the state shipped in every committed configuration.
        var controller = BuildController(ctx, seed.ActorId, new PostPaymentCompletionOptions());

        var result = await controller.GetOperationInvoiceObligations(seed.RequestId);

        // The same 404 an unknown request produces: a disabled feature is indistinguishable
        // from a route that never shipped.
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ── Visibility scope ──

    [Fact]
    public async Task A_caller_scoped_to_another_plant_cannot_read_the_request()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx, plantId: 1);
        AddGroup(ctx, seed);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = seed.ActorId, PlantId = 99 });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId, Enabled);

        var result = await controller.GetOperationInvoiceObligations(seed.RequestId);

        // Knowing the id is not enough: out of scope reads exactly like not existing.
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task An_in_scope_caller_receives_the_projection()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx, plantId: 1);
        AddGroup(ctx, seed);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = seed.ActorId, PlantId = 1 });
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId, Enabled);

        var body = Body(await controller.GetOperationInvoiceObligations(seed.RequestId));

        Assert.Equal(seed.RequestId, body.RequestId);
        Assert.Single(body.Obligations);
    }

    // ── Obligation states through the endpoint ──

    [Fact]
    public async Task A_required_group_with_no_allocations_awaits_its_upload()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        AddGroup(ctx, seed, poNumber: "45000123");
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Equal(Agg.PendingUpload, o.DerivedStatus);
        Assert.True(o.RequiresOperationInvoice);
        Assert.Equal(10_000_000m, o.ExpectedAmount);
        Assert.Equal(10_000_000m, o.RemainingAmount);
        Assert.Equal("45000123", o.PurchaseOrderNumber);
        Assert.Equal(OperationInvoiceObligationReasons.AwaitingOperationInvoice, o.ReasonCode);
    }

    [Fact]
    public async Task A_factura_group_owes_nothing_and_never_counts_as_pending_action()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        AddGroup(ctx, seed, type: Types.Invoice, persisted: Agg.NotRequired);
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Equal(Agg.NotRequired, o.DerivedStatus);
        Assert.False(o.RequiresOperationInvoice);
        Assert.False(o.StatusDrift);
        Assert.Equal(0, body.Rollup.PendingActionCount);
        Assert.Empty(body.Rollup.CurrencyTotals);
    }

    [Fact]
    public async Task Partial_coverage_reports_the_validated_amount_and_the_remainder()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        var group = AddGroup(ctx, seed, persisted: Agg.PartiallyInvoiced);
        AddInvoiceWithAllocation(ctx, seed, group.Id, Doc.Validated, 6_000_000m, sequence: 1);
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Equal(Agg.PartiallyInvoiced, o.DerivedStatus);
        Assert.Equal(6_000_000m, o.ValidatedCoveredAmount);
        Assert.Equal(4_000_000m, o.RemainingAmount);
        Assert.False(o.StatusDrift);
    }

    [Fact]
    public async Task An_invoice_awaiting_finance_reads_as_pending_validation()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        var group = AddGroup(ctx, seed, persisted: Agg.PendingValidation);
        AddInvoiceWithAllocation(ctx, seed, group.Id, Doc.PendingValidation, 3_000_000m, sequence: 1);
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Equal(Agg.PendingValidation, o.DerivedStatus);
        Assert.Equal(3_000_000m, o.PendingCoveredAmount);
        Assert.Equal(0m, o.ValidatedCoveredAmount);
    }

    [Fact]
    public async Task Full_validated_coverage_satisfies_the_obligation()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        var group = AddGroup(ctx, seed, persisted: Agg.Satisfied);
        AddInvoiceWithAllocation(ctx, seed, group.Id, Doc.Validated, 10_000_000m, sequence: 1);
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Equal(Agg.Satisfied, o.DerivedStatus);
        Assert.False(o.ClosedShort);
        Assert.Equal(0m, o.RemainingAmount);
        Assert.Equal(1, body.Rollup.SatisfiedCount);
    }

    [Fact]
    public async Task An_approved_short_close_satisfies_below_the_expected_total()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        var group = AddGroup(ctx, seed, persisted: Agg.Satisfied);
        AddInvoiceWithAllocation(ctx, seed, group.Id, Doc.Validated, 6_000_000m, sequence: 1);
        ctx.OperationInvoiceShortCloses.Add(new OperationInvoiceShortClose
        {
            RequestPoGroupId = group.Id,
            Status = RequestConstants.ShortCloseStatuses.Approved,
            ProposedByUserId = seed.ActorId,
            ProposedAtUtc = DateTime.UtcNow.AddDays(-1),
            ProposalJustification = "ZZTEST partial delivery accepted, remainder cancelled",
            RemainingAmountAtProposal = 4_000_000m,
            DecidedByUserId = seed.ActorId,
            DecidedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Equal(Agg.Satisfied, o.DerivedStatus);
        Assert.True(o.ClosedShort);
        Assert.Equal(OperationInvoiceObligationReasons.SatisfiedByShortClose, o.ReasonCode);
    }

    [Fact]
    public async Task A_superseded_invoice_contributes_nothing_to_coverage()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        var group = AddGroup(ctx, seed, persisted: Agg.PartiallyInvoiced);

        var replacement = AddInvoiceWithAllocation(ctx, seed, group.Id, Doc.Validated, 4_000_000m, sequence: 2);
        AddInvoiceWithAllocation(ctx, seed, group.Id, Doc.Validated, 10_000_000m, sequence: 1,
            supersededByInvoiceId: replacement.Id);
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Equal(4_000_000m, o.ValidatedCoveredAmount);
        Assert.Equal(Agg.PartiallyInvoiced, o.DerivedStatus);
    }

    // ── Honesty about the unknown ──

    [Fact]
    public async Task An_unknown_expected_total_is_returned_as_null_never_zero()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        AddGroup(ctx, seed, expected: null);
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Null(o.ExpectedAmount);
        Assert.Null(o.RemainingAmount);
        Assert.Equal(OperationInvoiceObligationReasons.ExpectedTotalUnknown, o.ReasonCode);
        Assert.Equal(1, body.Rollup.GroupsWithUnknownExpectedTotal);
    }

    [Fact]
    public async Task A_legacy_pre_flag_group_stays_honestly_unclassified()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        AddGroup(ctx, seed, type: null, expected: null, persisted: Agg.Unclassified, currency: null);
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Equal(Agg.Unclassified, o.DerivedStatus);
        Assert.Null(o.SourceDocumentType);
        Assert.Null(o.ExpectedAmount);
        Assert.False(o.StatusDrift);
        Assert.Equal(1, body.Rollup.UnclassifiedCount);
    }

    // ── Traceability ──

    [Fact]
    public async Task Source_documents_are_traced_from_the_group_lines_deduplicated_in_line_order()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        var group = AddGroup(ctx, seed);

        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        AddLine(ctx, seed, group.Id, lineNumber: 1, docA);
        AddLine(ctx, seed, group.Id, lineNumber: 2, docB);
        AddLine(ctx, seed, group.Id, lineNumber: 3, docA);   // same document again — one id, not two
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.Equal(new[] { docA, docB }, o.PaymentSourceDocumentIds);
        Assert.Equal(3, o.LineItemCount);
    }

    // ── Rollup ──

    [Fact]
    public async Task Mixed_groups_roll_up_by_state_and_currencies_are_never_summed_together()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        AddGroup(ctx, seed);                                                    // AOA, pending upload
        AddGroup(ctx, seed, expected: 5_000m, currency: "EUR");                 // EUR, pending upload
        AddGroup(ctx, seed, type: Types.Invoice, persisted: Agg.NotRequired);   // owes nothing
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        Assert.Equal(3, body.Rollup.TotalGroups);
        Assert.Equal(2, body.Rollup.RequiringOperationInvoiceCount);
        Assert.Equal(2, body.Rollup.PendingActionCount);
        Assert.Equal(1, body.Rollup.NotRequiredCount);

        Assert.Equal(2, body.Rollup.CurrencyTotals.Count);
        var aoa = Assert.Single(body.Rollup.CurrencyTotals, c => c.CurrencyCode == "AOA");
        Assert.Equal(10_000_000m, aoa.ExpectedTotal);
        var eur = Assert.Single(body.Rollup.CurrencyTotals, c => c.CurrencyCode == "EUR");
        Assert.Equal(5_000m, eur.ExpectedTotal);
    }

    // ── Drift is diagnosed, never repaired ──

    [Fact]
    public async Task Drift_is_reported_while_the_database_stays_untouched()
    {
        using var ctx = NewContext();
        var seed = await SeedRequestAsync(ctx);
        // The restamping-gap shape: identity says Factura (nothing owed), cached column disagrees.
        var group = AddGroup(ctx, seed, type: Types.Invoice, persisted: Agg.PendingUpload);
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, Enabled)
            .GetOperationInvoiceObligations(seed.RequestId));

        var o = Assert.Single(body.Obligations);
        Assert.True(o.StatusDrift);
        Assert.Equal(Agg.NotRequired, o.DerivedStatus);
        Assert.Equal(Agg.PendingUpload, o.PersistedStatus);
        Assert.True(body.Rollup.HasStatusDrift);
        Assert.Equal(1, body.Rollup.DriftCount);

        // The GET wrote nothing: the cached status is still wrong, and no operation-invoice
        // row of any kind appeared. Repair belongs to the classification write path (Phase 1c).
        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id);
        Assert.Equal(Agg.PendingUpload, persisted.OperationInvoiceStatus);
        Assert.Equal(10_000_000m, persisted.ExpectedOperationInvoiceTotal);
        Assert.Equal(0, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(0, await ctx.OperationInvoiceAllocations.CountAsync());
        Assert.Equal(0, await ctx.OperationInvoiceShortCloses.CountAsync());
        Assert.Equal(0, await ctx.OperationInvoiceReconciliations.CountAsync());
    }
}
