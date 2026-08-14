using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 4 Phase 4C: the REAL Phase 2 (parent completion) — the single authoritative writer
/// that transitions a grouped, classified request to COMPLETED. Matrix A–R of the Phase 4C
/// instruction: flag gating, every blocker, the exactly-once identity/history/notification,
/// terminal idempotency, and the retry-once concurrency semantics.
/// </summary>
public class RequestCompletionServiceParentTests
{
    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static PostPaymentCompletionOptions Flags(bool completion = true) => new()
    {
        Enabled = true,
        CompletionEnabled = completion,
        EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
    };

    private static RequestCompletionService Service(
        ApplicationDbContext ctx,
        bool completion = true,
        IWorkflowNotificationOrchestrator? orchestrator = null) =>
        new(ctx, Options.Create(Flags(completion)),
            NullLogger<RequestCompletionService>.Instance, orchestrator);

    /// <summary>Simulates optimistic-concurrency losses: throws on the first N SaveChanges.</summary>
    private sealed class ConcurrencyThrowingContext : ApplicationDbContext
    {
        public int ThrowsRemaining { get; set; }
        public Action? OnThrow { get; set; }

        public ConcurrencyThrowingContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowsRemaining > 0)
            {
                ThrowsRemaining--;
                OnThrow?.Invoke();
                throw new DbUpdateConcurrencyException("ZZTEST simulated RowVersion conflict.");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed record Seed(Guid RequestId, Guid ActorId, int OpenStatusId);

    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx,
        string requestStatusCode = RequestConstants.Statuses.WaitingReceipt,
        params (string Status, bool Classified)[] groups)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST P2 Actor", Email = "p2@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);

        var open = new RequestStatus { Id = 16, Code = requestStatusCode, Name = "ZZTEST Open", DisplayOrder = 17 };
        var completed = new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 };
        ctx.RequestStatuses.AddRange(open, completed);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-P2-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST parent completion",
            RequestTypeId = requestType.Id,
            StatusId = open.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
        };
        ctx.Requests.Add(request);

        foreach (var (status, classified) in groups)
        {
            ctx.RequestPoGroups.Add(new RequestPoGroup
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                SupplierNameSnapshot = "ZZTEST P2 Supplier",
                CurrencyCode = "AOA",
                TotalAmount = 10_000m,
                Status = status,
                SourceDocumentType = classified ? RequestConstants.SourceDocumentTypes.Proforma : null,
                OperationInvoiceStatus = classified
                    ? RequestConstants.OperationInvoiceStatuses.Satisfied
                    : RequestConstants.OperationInvoiceStatuses.Unclassified,
                RequiresOperationInvoice = classified,
                CompletedAtUtc = status == RequestConstants.PoGroupStatuses.Completed ? DateTime.UtcNow : null,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
                CreatedByUserId = actor.Id
            });
        }

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, actor.Id, open.Id);
    }

    private static (string, bool) CompletedGroup => (RequestConstants.PoGroupStatuses.Completed, true);

    // ── A: flag off → exact NoOp ──

    [Fact]
    public async Task A_completion_disabled_is_an_exact_no_op()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, groups: CompletedGroup);

        var result = await Service(ctx, completion: false)
            .EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.False(result.RequestCompleted);
        Assert.False(result.AlreadyCompleted);
        Assert.Empty(ctx.ChangeTracker.Entries());

        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(seed.OpenStatusId, request.StatusId);
        Assert.Null(request.CompletionCycleId);
    }

    // ── B: zero groups → NoOp (groupless legacy flow owns completion) ──

    [Fact]
    public async Task B_groupless_request_is_never_phase4_completed()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx);

        var result = await Service(ctx).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.False(result.RequestCompleted);
        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(seed.OpenStatusId, request.StatusId);
    }

    // ── C / §21: even one UNCLASSIFIED group fails closed ──

    [Fact]
    public async Task C_unclassified_sibling_blocks_completion_even_when_its_status_reads_completed()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, groups: new[]
        {
            CompletedGroup,
            (RequestConstants.PoGroupStatuses.Completed, false) // UNCLASSIFIED — must fail closed
        });

        var result = await Service(ctx).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.False(result.RequestCompleted);
        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(seed.OpenStatusId, request.StatusId);
        Assert.Null(request.CompletionCycleId);
    }

    // ── D / N / §22: incomplete sibling blocks; completing it completes the request once ──

    [Fact]
    public async Task D_N_mixed_groups_block_until_the_last_one_completes()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, groups: new[]
        {
            CompletedGroup,
            (RequestConstants.PoGroupStatuses.WaitingReceipt, true)
        });
        var service = Service(ctx);

        var blocked = await service.EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);
        Assert.False(blocked.RequestCompleted);

        // Group B completes (its Phase-1 commitment) → the next Phase 2 completes the parent.
        var groupB = await ctx.RequestPoGroups
            .SingleAsync(g => g.RequestId == seed.RequestId &&
                              g.Status == RequestConstants.PoGroupStatuses.WaitingReceipt);
        groupB.Status = RequestConstants.PoGroupStatuses.Completed;
        groupB.CompletedAtUtc = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        var completedResult = await service.EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);
        Assert.True(completedResult.RequestCompleted);

        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(17, request.StatusId);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));
    }

    // ── E/F/G/H: the winning transition — status, identity, history and notification ONCE ──

    [Fact]
    public async Task E_to_H_completion_writes_identity_history_and_notification_exactly_once()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        // Two groups: one satisfied by full invoice coverage, one by an approved short-close —
        // Phase 2 trusts the COMPLETED commitment either way (§22).
        var seed = await SeedAsync(ctx, groups: new[] { CompletedGroup, CompletedGroup });

        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Returns(Task.CompletedTask);
        var service = Service(ctx, orchestrator: orchestrator.Object);

        var first = await service.EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);
        var second = await service.EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        // E: completed. F: one cycle id, never regenerated. I: terminal idempotency.
        Assert.True(first.RequestCompleted);
        Assert.NotNull(first.CompletionCycleId);
        Assert.True(second.AlreadyCompleted);
        Assert.False(second.RequestCompleted);
        Assert.Equal(first.CompletionCycleId, second.CompletionCycleId);

        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(17, request.StatusId);
        Assert.Equal(first.CompletionCycleId, request.CompletionCycleId);

        // G: REQUEST_COMPLETED history once, keyed RC:{RequestId}:{CycleId}.
        var history = await ctx.RequestStatusHistories.SingleAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED");
        Assert.Equal(
            AlplaPortal.Domain.Services.PostPaymentIdempotencyKeys.RequestCompleted(
                seed.RequestId, first.CompletionCycleId!.Value),
            history.IdempotencyKey);
        Assert.Contains("2 grupo(s)", history.Comment);

        // H: RequestFinalized once, correlated by the completion cycle.
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e =>
            e.EventCode == WorkflowEventCodes.RequestFinalized &&
            e.CorrelationId == first.CompletionCycleId)), Times.Once);
        orchestrator.VerifyNoOtherCalls();
    }

    // ── J / §20: any active reconciliation of the request blocks — including request-level ──

    [Fact]
    public async Task J_active_request_level_reconciliation_blocks_completion()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, groups: CompletedGroup);
        ctx.RequestReconciliations.Add(new RequestReconciliation
        {
            RequestId = seed.RequestId,
            RequestPoGroupId = null, // request-level: attributable to no group
            ReconciliationStatus = RequestReconciliation.ReconciliationStatuses.InProgress
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await Service(ctx).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.False(result.RequestCompleted);
        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(seed.OpenStatusId, request.StatusId);
    }

    // ── K / L: terminal request states are NoOps ──

    [Theory]
    [InlineData(RequestConstants.Statuses.Rejected)]
    [InlineData(RequestConstants.Statuses.Cancelled)]
    public async Task K_L_rejected_and_cancelled_requests_are_no_ops(string statusCode)
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, requestStatusCode: statusCode, groups: CompletedGroup);

        var result = await Service(ctx).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.False(result.RequestCompleted);
        Assert.False(result.AlreadyCompleted);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "REQUEST_COMPLETED"));
    }

    // ── M: all groups cancelled → nothing was fulfilled ──

    [Fact]
    public async Task M_all_groups_cancelled_never_completes()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, groups: new[]
        {
            (RequestConstants.PoGroupStatuses.Cancelled, true),
            (RequestConstants.PoGroupStatuses.Cancelled, true)
        });

        var result = await Service(ctx).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.False(result.RequestCompleted);
        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(seed.OpenStatusId, request.StatusId);
    }

    // ── O: two evaluators — exactly one winner, the loser reads the winner's identity ──

    [Fact]
    public async Task O_second_evaluator_sees_already_completed_with_the_winning_cycle_id()
    {
        var options = NewOptions();
        using var ctx1 = new ApplicationDbContext(options);
        var seed = await SeedAsync(ctx1, groups: CompletedGroup);

        using var ctx2 = new ApplicationDbContext(options);

        var winner = await Service(ctx1).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);
        var loser = await Service(ctx2).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.True(winner.RequestCompleted);
        Assert.True(loser.AlreadyCompleted);
        Assert.Equal(winner.CompletionCycleId, loser.CompletionCycleId);

        using var verify = new ApplicationDbContext(options);
        Assert.Equal(1, await verify.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));
    }

    // ── P: a concurrency loss whose winner completed → retry returns AlreadyCompleted ──

    [Fact]
    public async Task P_retry_after_conflict_reuses_the_winners_persisted_cycle_id()
    {
        var options = NewOptions();
        Guid? winnerCycleId = null;

        using var throwing = new ConcurrencyThrowingContext(options);
        var seed = await SeedAsync(throwing, groups: CompletedGroup);

        throwing.ThrowsRemaining = 1;
        throwing.OnThrow = () =>
        {
            // The "winner": a second evaluator completes the request while our first attempt
            // is losing its RowVersion race.
            using var winnerCtx = new ApplicationDbContext(options);
            var winnerResult = new RequestCompletionService(
                    winnerCtx, Options.Create(Flags()),
                    NullLogger<RequestCompletionService>.Instance)
                .EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId)
                .GetAwaiter().GetResult();
            winnerCycleId = winnerResult.CompletionCycleId;
        };

        var result = await Service(throwing).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.True(result.AlreadyCompleted);
        Assert.False(result.RequestCompleted);
        Assert.NotNull(winnerCycleId);
        Assert.Equal(winnerCycleId, result.CompletionCycleId); // never a second identity

        using var verify = new ApplicationDbContext(options);
        Assert.Equal(1, await verify.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));
    }

    // ── R: two consecutive unresolved conflicts → ConflictUnresolved, nothing half-written ──

    [Fact]
    public async Task R_second_unresolved_conflict_returns_conflict_unresolved()
    {
        var options = NewOptions();
        using var throwing = new ConcurrencyThrowingContext(options);
        var seed = await SeedAsync(throwing, groups: CompletedGroup);
        throwing.ThrowsRemaining = 2;

        var result = await Service(throwing).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.True(result.ConflictUnresolved);
        Assert.False(result.RequestCompleted);
        Assert.Null(result.CompletionCycleId);

        using var verify = new ApplicationDbContext(options);
        var request = await verify.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(seed.OpenStatusId, request.StatusId);
        Assert.Null(request.CompletionCycleId);
        Assert.False(await verify.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));
    }

    // ── R2: a single conflict against an unchanged state simply retries and wins ──

    [Fact]
    public async Task R2_single_conflict_retries_once_and_completes()
    {
        var options = NewOptions();
        using var throwing = new ConcurrencyThrowingContext(options);
        var seed = await SeedAsync(throwing, groups: CompletedGroup);
        throwing.ThrowsRemaining = 1;

        var result = await Service(throwing).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);

        Assert.True(result.RequestCompleted);
        Assert.NotNull(result.CompletionCycleId);
    }

    // ── Q: the ambient-transaction guard is pinned on a REAL provider by the pre-existing
    // RequestCompletionServiceTransactionGuardTests (LocalDB) — the InMemory provider does not
    // surface CurrentTransaction, so the pin deliberately lives there, unchanged since R1. ──
}
