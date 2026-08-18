using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 4 Phase 4C: the competing-writer consolidation (§29) and the recovery sweep.
///
/// After 4C, the ONLY first-writers of Request.StatusId = COMPLETED are: the completion service
/// (grouped classified requests, CompletionEnabled=true), legacy FinalizeRequest (groupless),
/// and the not-quoted auto-close (zero active groups). These tests pin that the two former
/// competitors — the LineItems last-item shortcut and the status aggregation — can no longer be
/// first, while their legacy behaviour under CompletionEnabled=false stays byte-identical.
/// </summary>
public class CompletionWriterConsolidationTests
{
    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static PostPaymentCompletionOptions Flags(bool completion) => new()
    {
        Enabled = true,
        CompletionEnabled = completion,
        EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
    };

    private static RequestCompletionService CompletionService(
        ApplicationDbContext ctx, bool completion = true) =>
        new(ctx, Options.Create(Flags(completion)), NullLogger<RequestCompletionService>.Instance);

    // ═══════════════ LineItemsController legacy auto-complete (§29 A/B) ═══════════════

    private static LineItemsController BuildLineItemsController(
        ApplicationDbContext ctx, Guid actorId, PostPaymentCompletionOptions? options,
        IRequestCompletionService? completion)
    {
        var controller = new LineItemsController(
            ctx, NullLogger<LineItemsController>.Instance,
            new Mock<AlplaPortal.Application.Interfaces.IApprovalRoutingService>().Object,
            options == null ? null : Options.Create(options),
            completion);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, RoleConstants.Buyer)
                }, "Test"))
            }
        };
        return controller;
    }

    private sealed record ItemSeed(Guid RequestId, Guid GroupId, Guid ItemId, Guid ActorId);

    /// <summary>
    /// A grouped PAYMENT request in WAITING_RECEIPT with ONE pending item — patching it RECEIVED
    /// makes it the "last item" the legacy shortcut used to complete the request on.
    /// </summary>
    private static async Task<ItemSeed> SeedLastItemAsync(
        ApplicationDbContext ctx, Action<RequestPoGroup>? mutateGroup = null)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Buyer 4C", Email = "li4c@test.local" };
        ctx.Users.Add(actor);
        var requestType = new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);

        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 },
            new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 },
            new RequestStatus { Id = 18, Code = RequestConstants.Statuses.InFollowup, Name = "Em Acompanhamento", DisplayOrder = 18 });

        var received = new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" };
        var pending = new LineItemStatus { Id = 93, Code = "PENDING", Name = "Pendente" };
        ctx.LineItemStatuses.AddRange(received, pending);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-LI4C-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST lineitems writer",
            RequestTypeId = requestType.Id,
            StatusId = 16,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST LI Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 1000m,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.PendingUpload,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedByUserId = actor.Id
        };
        mutateGroup?.Invoke(group);
        ctx.RequestPoGroups.Add(group);

        var item = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            RequestPoGroupId = group.Id,
            LineNumber = 1,
            Description = "ZZTEST last item",
            Quantity = 1,
            LineItemStatusId = pending.Id
        };
        ctx.RequestLineItems.Add(item);

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new ItemSeed(request.Id, group.Id, item.Id, actor.Id);
    }

    [Fact]
    public async Task A_legacy_last_item_shortcut_is_preserved_while_completion_is_off()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedLastItemAsync(ctx);
        var controller = BuildLineItemsController(ctx, seed.ActorId, Flags(completion: false), null);

        var result = await controller.UpdateStatus(seed.ItemId,
            new UpdateLineItemStatusDto { StatusCode = "RECEIVED", Comment = "ZZTEST recebido" });

        Assert.IsType<NoContentResult>(result);
        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(17, request.StatusId); // legacy COMPLETED write, byte-identical behaviour
        Assert.Null(request.CompletionCycleId);
    }

    [Fact]
    public async Task B_grouped_request_with_completion_on_is_never_completed_by_the_legacy_shortcut()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        // Invoice obligation deliberately open: nothing may complete this request.
        var seed = await SeedLastItemAsync(ctx);
        var controller = BuildLineItemsController(
            ctx, seed.ActorId, Flags(completion: true), CompletionService(ctx));

        var result = await controller.UpdateStatus(seed.ItemId,
            new UpdateLineItemStatusDto { StatusCode = "RECEIVED", Comment = "ZZTEST recebido" });

        Assert.IsType<NoContentResult>(result);
        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.NotEqual(17, request.StatusId); // the shortcut wrote nothing
        Assert.Null(request.CompletionCycleId);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));

        // Delegation happened: the item records proved full receipt, so Phase 1 stamped the
        // operational receipt through the shared engine — the ONE receipt rulebook (§17).
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.NotNull(group.OperationalReceiptCompletedAtUtc);
    }

    [Fact]
    public async Task B2_delegated_path_completes_through_the_authoritative_service_when_everything_is_satisfied()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedLastItemAsync(ctx, g =>
        {
            g.OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied;
            g.RequiresSeparateFiscalReceipt = false;
        });
        var controller = BuildLineItemsController(
            ctx, seed.ActorId, Flags(completion: true), CompletionService(ctx));

        await controller.UpdateStatus(seed.ItemId,
            new UpdateLineItemStatusDto { StatusCode = "RECEIVED", Comment = "ZZTEST recebido total" });

        // The parent DID complete — but through Phase 2, with the full identity and audit that
        // the legacy shortcut never produced.
        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(17, request.StatusId);
        Assert.NotNull(request.CompletionCycleId);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, group.Status);
    }

    // ═══════════════ StatusAggregationService safeguard (§29 C/D) ═══════════════

    private sealed record AggSeed(Guid RequestId, Guid ActorId);

    private static async Task<AggSeed> SeedAggregationAsync(
        ApplicationDbContext ctx, string groupStatus = RequestConstants.PoGroupStatuses.Completed)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Agg", Email = "agg4c@test.local" };
        ctx.Users.Add(actor);
        var requestType = new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 },
            new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-AGG4C-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST aggregation writer",
            RequestTypeId = requestType.Id,
            StatusId = 16,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        ctx.Requests.Add(request);
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST AGG Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 1m,
            Status = groupStatus,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedByUserId = actor.Id
        });

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new AggSeed(request.Id, actor.Id);
    }

    [Fact]
    public async Task C_aggregation_never_becomes_the_first_completing_writer_while_completion_is_on()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAggregationAsync(ctx);

        var aggregator = new StatusAggregationService(
            ctx, NullLogger<StatusAggregationService>.Instance, Options.Create(Flags(completion: true)));
        await aggregator.AggregateRequestStatusAsync(seed.RequestId);

        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(16, request.StatusId); // deferred — the completion service owns the transition
        Assert.Null(request.CompletionCycleId);
    }

    [Fact]
    public async Task C2_aggregation_keeps_writing_completed_while_the_lifecycle_is_off()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAggregationAsync(ctx);

        // Legacy pin: with CompletionEnabled=false (or options absent) nothing changes about the
        // pre-Phase-4 aggregation behaviour.
        var aggregator = new StatusAggregationService(
            ctx, NullLogger<StatusAggregationService>.Instance, Options.Create(Flags(completion: false)));
        await aggregator.AggregateRequestStatusAsync(seed.RequestId);

        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(17, request.StatusId);
    }

    [Fact]
    public async Task D_aggregation_reaffirms_a_service_completed_request_without_duplicates()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAggregationAsync(ctx);

        // The authoritative service completes first…
        var parent = await CompletionService(ctx).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);
        Assert.True(parent.RequestCompleted);

        // …then aggregation runs and changes nothing: no second transition, no extra history.
        var aggregator = new StatusAggregationService(
            ctx, NullLogger<StatusAggregationService>.Instance, Options.Create(Flags(completion: true)));
        await aggregator.AggregateRequestStatusAsync(seed.RequestId);

        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(17, request.StatusId);
        Assert.Equal(parent.CompletionCycleId, request.CompletionCycleId);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));
    }

    // ═══════════════ WAITING_FISCAL_RECEIPT aggregation priority (§23) ═══════════════

    private static Request CalculatorRequest(params string[] groupStatuses)
    {
        var request = new Request
        {
            Id = Guid.NewGuid(),
            Status = new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "x" },
            StatusId = 16
        };
        foreach (var status in groupStatuses)
            request.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = status });
        return request;
    }

    [Fact]
    public void Priority_A_completed_plus_antechamber_projects_waiting_fiscal_receipt()
    {
        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(CalculatorRequest(
            RequestConstants.PoGroupStatuses.Completed,
            RequestConstants.PoGroupStatuses.WaitingFiscalReceipt));

        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt, result.StatusCode);
    }

    [Fact]
    public void Priority_B_waiting_receipt_stays_furthest_behind_the_antechamber()
    {
        // 70 (WAITING_RECEIPT) < 95 (WAITING_FISCAL_RECEIPT): the repository's furthest-behind
        // philosophy — the parent reflects the group with the most work left.
        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(CalculatorRequest(
            RequestConstants.PoGroupStatuses.WaitingReceipt,
            RequestConstants.PoGroupStatuses.WaitingFiscalReceipt));

        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, result.StatusCode);
    }

    // ═══════════════ Recovery sweep (§24/§25) ═══════════════

    private static ParentCompletionSweepController BuildSweepController(
        ApplicationDbContext ctx, Guid actorId, string role, bool completion = true) =>
        new(ctx, NullLogger<ParentCompletionSweepController>.Instance,
            CompletionService(ctx, completion), Options.Create(Flags(completion)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, actorId.ToString()),
                        new(ClaimTypes.Role, role)
                    }, "Test"))
                }
            }
        };

    private const string SweepReason =
        "Recuperação de conclusão após falha técnica do Phase 2 verificada em produção.";

    [Fact]
    public async Task Sweep_preview_lists_candidates_and_skip_reasons()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAggregationAsync(ctx); // all groups COMPLETED, request open

        // A skipped case: same shape but with an active request-level reconciliation.
        var blockedSeed = await AddSecondCandidateAsync(ctx, withActiveReconciliation: true);

        var controller = BuildSweepController(ctx, seed.ActorId, RoleConstants.Finance);
        var result = await controller.Preview();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"EligibleCount\":1", payload);
        Assert.Contains("\"SkippedCount\":1", payload);
        Assert.Contains(ParentCompletionSweepController.SkipActiveReconciliation, payload);
        _ = blockedSeed;
    }

    [Fact]
    public async Task Sweep_apply_completes_through_the_authoritative_service_and_is_idempotent()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAggregationAsync(ctx);
        var controller = BuildSweepController(ctx, seed.ActorId, RoleConstants.SystemAdministrator);

        var first = await controller.Apply(new ParentCompletionSweepController.ApplySweepDto { Reason = SweepReason });
        Assert.IsType<OkObjectResult>(first);

        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(17, request.StatusId);
        Assert.NotNull(request.CompletionCycleId); // service identity — never a direct write
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));

        // Idempotent: a recovered request stops being a candidate.
        var second = await controller.Apply(new ParentCompletionSweepController.ApplySweepDto { Reason = SweepReason });
        var payload = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)second).Value);
        Assert.Contains("\"CompletedCount\":0", payload);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));
    }

    [Fact]
    public async Task Sweep_apply_fails_closed_while_completion_is_disabled()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAggregationAsync(ctx);
        var controller = BuildSweepController(
            ctx, seed.ActorId, RoleConstants.SystemAdministrator, completion: false);

        var result = await controller.Apply(new ParentCompletionSweepController.ApplySweepDto { Reason = SweepReason });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(ParentCompletionSweepController.CompletionDisabledCode, problem.Extensions["code"]?.ToString());

        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(16, request.StatusId); // nothing activated implicitly
    }

    [Fact]
    public async Task Sweep_apply_is_sysadmin_only()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAggregationAsync(ctx);
        var controller = BuildSweepController(ctx, seed.ActorId, RoleConstants.Finance);

        var result = await controller.Apply(new ParentCompletionSweepController.ApplySweepDto { Reason = SweepReason });

        Assert.Equal(403, ((ObjectResult)result).StatusCode);
    }

    private static async Task<Guid> AddSecondCandidateAsync(
        ApplicationDbContext ctx, bool withActiveReconciliation)
    {
        var requestId = Guid.NewGuid();
        ctx.Requests.Add(new Request
        {
            Id = requestId,
            RequestNumber = "ZZTEST-SWEEP2-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST sweep skipped",
            RequestTypeId = 2,
            StatusId = 16,
            RequesterId = Guid.NewGuid(),
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        });
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            SupplierNameSnapshot = "ZZTEST Sweep Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 1m,
            Status = RequestConstants.PoGroupStatuses.Completed,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        });
        if (withActiveReconciliation)
        {
            ctx.RequestReconciliations.Add(new RequestReconciliation
            {
                RequestId = requestId,
                ReconciliationStatus = RequestReconciliation.ReconciliationStatuses.InProgress
            });
        }
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return requestId;
    }
}
