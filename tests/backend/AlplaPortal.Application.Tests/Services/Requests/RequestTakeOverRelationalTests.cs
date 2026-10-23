using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// RELATIONAL (SQL Server LocalDB sandbox) proof for §8 takeover: ownership + audit + notification intent commit in ONE
/// transaction (outbox and in-app rows exist after success, nothing exists after a failure inside the transaction),
/// the RowVersion race between two simultaneous takeovers selects exactly one winner (loser 409 TAKEOVER_CONCURRENT,
/// nothing written), and a stale confirmation after a completed takeover is refused (409 TAKEOVER_STALE). Delivery is
/// left to the outbox worker: rows are PENDING with the worker's retry budget; the e-mail service is never called here.
/// </summary>
[Collection("IntegrationTests")]
public class RequestTakeOverRelationalTests
{
    static RequestTakeOverRelationalTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var source = ctx.Database.SqlQueryRaw<int>("SELECT CAST(ISNULL(COL_LENGTH('dbo.Quotations', 'FirstSubmittedSource'), 0) AS int) AS [Value]").AsEnumerable().First();
                if (source == 0)
                {
                    Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                    ctx.Database.ExecuteSqlRaw("ALTER DATABASE CURRENT SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                    ctx.Database.EnsureDeleted();
                }
            }
            ctx.Database.EnsureCreated();
        }
        catch (Exception ex) { Console.Error.WriteLine($"[RequestTakeOverRelationalTests] sandbox bootstrap: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());
    private static ApplicationDbContext NewCtxWithRace(Func<Task> beforeFirstSave)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(IntegrationTestDatabase.GetConnectionString()).AddInterceptors(new RaceInterceptor(beforeFirstSave)).Options);

    private sealed class RaceInterceptor : SaveChangesInterceptor
    {
        private readonly Func<Task> _run; private int _fired;
        public RaceInterceptor(Func<Task> run) => _run = run;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0) await _run();
            return result;
        }
    }

    /// <summary>Fails the SaveChanges that carries the FIRST in-app notification row — the in-app insert of the real
    /// NotificationService on the controller's own context, inside the takeover transaction.</summary>
    private sealed class InAppInsertFailureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<InformationalNotification>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("injected: in-app notification store unavailable");
            return ValueTask.FromResult(result);
        }
    }
    private static ApplicationDbContext NewCtxFailingInApp()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(IntegrationTestDatabase.GetConnectionString()).AddInterceptors(new InAppInsertFailureInterceptor()).Options);

    private sealed record Seed(Guid RequestId, Guid Owner, Guid BuyerB, Guid BuyerC, Guid Requester);

    private static async Task CleanupAsync(ApplicationDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE o FROM EmailOutbox o INNER JOIN Requests r ON r.Id=o.RequestId WHERE r.RequestNumber LIKE 'ZZT-TO-%';" +
            "DELETE n FROM InformationalNotifications n WHERE EXISTS (SELECT 1 FROM Requests r WHERE r.RequestNumber LIKE 'ZZT-TO-%' AND n.TargetPath LIKE '%' + CAST(r.Id AS varchar(36)) + '%');" +
            "DELETE h FROM RequestFieldChangeHistories h INNER JOIN Requests r ON r.Id=h.RequestId WHERE r.RequestNumber LIKE 'ZZT-TO-%';" +
            "DELETE h FROM RequestStatusHistories h INNER JOIN Requests r ON r.Id=h.RequestId WHERE r.RequestNumber LIKE 'ZZT-TO-%';" +
            "DELETE FROM Requests WHERE RequestNumber LIKE 'ZZT-TO-%';" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-to-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId=Users.Id OR r.BuyerId=Users.Id);");
    }

    private static async Task<Seed?> SeedAsync()
    {
        await using var ctx = NewCtx();
        await CleanupAsync(ctx);
        var statusId = await ctx.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.WaitingQuotation).Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == RequestConstants.Types.Quotation).Select(t => t.Id).FirstOrDefaultAsync();
        if (statusId == 0 || typeId == 0) return null;
        var tag = Guid.NewGuid().ToString("N")[..8];
        var owner = new User { Id = Guid.NewGuid(), FullName = "ZZ Owner", Email = $"zztest-to-owner-{tag}@test.local", IsActive = true };
        var b = new User { Id = Guid.NewGuid(), FullName = "ZZ Buyer B", Email = $"zztest-to-b-{tag}@test.local", IsActive = true };
        var c = new User { Id = Guid.NewGuid(), FullName = "ZZ Buyer C", Email = $"zztest-to-c-{tag}@test.local", IsActive = true };
        var req = new User { Id = Guid.NewGuid(), FullName = "ZZ Requester", Email = $"zztest-to-req-{tag}@test.local", IsActive = true };
        ctx.Users.AddRange(owner, b, c, req);
        var request = new Request { Id = Guid.NewGuid(), Title = "ZZTEST_TAKEOVER", RequestNumber = "ZZT-TO-" + tag, StatusId = statusId, RequestTypeId = typeId, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1, RequesterId = req.Id, BuyerId = owner.Id, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, owner.Id, b.Id, c.Id, req.Id);
    }

    private static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    /// <summary>Controller with the REAL orchestrator (real in-app service, mocked e-mail transport) on the SAME context — the production wiring (all scoped).</summary>
    private static (RequestsController Ctl, Mock<IEmailService> Email) RealCtl(ApplicationDbContext ctx, Guid actorId)
    {
        var email = new Mock<IEmailService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var orchestrator = new WorkflowNotificationOrchestrator(ctx, new NotificationService(ctx, NullLogger<NotificationService>.Instance), email.Object, config,
            NullLogger<WorkflowNotificationOrchestrator>.Instance, AdminLog().Object, new ApprovalRoutingService(ctx));
        return (Build(ctx, actorId, orchestrator), email);
    }

    private static RequestsController MockCtl(ApplicationDbContext ctx, Guid actorId, IWorkflowNotificationOrchestrator orchestrator) => Build(ctx, actorId, orchestrator);

    private static RequestsController Build(ApplicationDbContext ctx, Guid actorId, IWorkflowNotificationOrchestrator orchestrator)
    {
        var controller = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() } };
        return controller;
    }

    private static TakeOverRequestDto Dto(Guid expected, string justification = "Comprador anterior indisponível; assumo o pedido.")
        => new() { Justification = justification, ExpectedBuyerId = JsonDocument.Parse($"\"{expected}\"").RootElement.Clone() };

    private sealed record Snapshot(Guid? BuyerId, int Transfers, int FieldChanges, int Outbox, int InApp);
    private static async Task<Snapshot> SnapshotAsync(Seed s, Guid? correlation = null)
    {
        await using var ctx = NewCtx();
        var histories = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == s.RequestId && h.ActionTaken == BuyerTakeOverPolicy.HistoryAction).Select(h => h.Id).ToListAsync();
        return new Snapshot((await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).BuyerId, histories.Count,
            await ctx.RequestFieldChangeHistories.CountAsync(h => h.RequestId == s.RequestId),
            await ctx.EmailOutbox.CountAsync(e => e.RequestId == s.RequestId),
            await ctx.InformationalNotifications.CountAsync(n => histories.Contains(n.EventCorrelationId ?? Guid.Empty)));
    }

    [Fact]
    public async Task Successful_takeover_commits_ownership_audit_in_app_and_outbox_rows_atomically_and_sends_nothing_itself()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var ctx = NewCtx();
        var (ctl, email) = RealCtl(ctx, s.BuyerB);
        var ok = Assert.IsType<OkObjectResult>(await ctl.TakeOverRequest(s.RequestId, Dto(s.Owner)));
        var result = Assert.IsType<TakeOverRequestResultDto>(ok.Value);

        var snap = await SnapshotAsync(s);
        Assert.Equal(s.BuyerB, snap.BuyerId); Assert.Equal(1, snap.Transfers); Assert.Equal(1, snap.FieldChanges);
        await using var verify = NewCtx();
        var outbox = await verify.EmailOutbox.AsNoTracking().Where(e => e.RequestId == s.RequestId).ToListAsync();
        Assert.Equal(3, outbox.Count);   // previous Buyer, new Buyer, requester (no other persisted participants on this request)
        Assert.All(outbox, o => { Assert.Equal(result.HistoryId, o.CorrelationId); Assert.Equal("PENDING", o.Status); Assert.Equal(0, o.RetryCount); Assert.True(o.MaxRetries >= 1); Assert.Equal(WorkflowEventCodes.BuyerTransferred, o.EventCode); });
        Assert.Equal(3, await verify.InformationalNotifications.CountAsync(n => n.EventCorrelationId == result.HistoryId));
        email.VerifyNoOtherCalls();   // delivery belongs to the outbox worker (retry/dead-letter policy of EmailOutboxProcessor)
        Assert.Null(ctx.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Failure_while_recording_the_notification_intent_rolls_back_ownership_and_audit()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var ctx = NewCtx();
        var failing = new Mock<IWorkflowNotificationOrchestrator>();
        failing.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).ThrowsAsync(new InvalidOperationException("outbox unavailable"));
        var ctl = MockCtl(ctx, s.BuyerB, failing.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctl.TakeOverRequest(s.RequestId, Dto(s.Owner)));
        var snap = await SnapshotAsync(s);
        Assert.Equal(s.Owner, snap.BuyerId); Assert.Equal(0, snap.Transfers); Assert.Equal(0, snap.FieldChanges); Assert.Equal(0, snap.Outbox);
    }

    [Fact]
    public async Task Failure_persisting_an_in_app_notification_rolls_back_ownership_audit_and_outbox()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var ctx = NewCtxFailingInApp();
        var (ctl, email) = RealCtl(ctx, s.BuyerB);   // REAL orchestrator + REAL in-app service; the DB-level insert of the in-app row fails
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctl.TakeOverRequest(s.RequestId, Dto(s.Owner)));
        var snap = await SnapshotAsync(s);
        Assert.Equal(s.Owner, snap.BuyerId); Assert.Equal(0, snap.Transfers); Assert.Equal(0, snap.FieldChanges); Assert.Equal(0, snap.Outbox); Assert.Equal(0, snap.InApp);
        await using var verify = NewCtx();
        Assert.Equal(0, await verify.InformationalNotifications.CountAsync(n => n.TargetPath.Contains(s.RequestId.ToString())));
        email.VerifyNoOtherCalls();
        Assert.Null(ctx.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Replay_is_idempotent_and_can_never_overwrite_a_subsequent_transfer()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using var ctxB = NewCtx(); var (ctlB, _) = RealCtl(ctxB, s.BuyerB);
        Assert.IsType<OkObjectResult>(await ctlB.TakeOverRequest(s.RequestId, Dto(s.Owner)));
        var after1 = await SnapshotAsync(s);
        Assert.Equal(s.BuyerB, after1.BuyerId); Assert.Equal(1, after1.Transfers); Assert.Equal(3, after1.Outbox); Assert.Equal(3, after1.InApp);

        // Replays by the CURRENT owner — the very same confirmation (old expected owner, e.g. a retried request after a
        // lost response) and one with the fresh owner — are idempotent 200 TAKEOVER_ALREADY_OWNER: nothing new is written.
        foreach (var expected in new[] { s.Owner, s.BuyerB })
        {
            var replay = Assert.IsType<OkObjectResult>(await ctlB.TakeOverRequest(s.RequestId, Dto(expected)));
            Assert.Equal(BuyerTakeOverPolicy.CodeAlreadyOwner, Assert.IsType<TakeOverRequestResultDto>(replay.Value).Code);
        }
        var after2 = await SnapshotAsync(s);
        Assert.Equal(after1, after2);

        // A subsequent transfer to C; B's replays (old or own owner) can never bring ownership back. Each HTTP request
        // runs on its own scoped DbContext in production, so B's replays use a fresh context here as well.
        await using var ctxC = NewCtx(); var (ctlC, _) = RealCtl(ctxC, s.BuyerC);
        Assert.IsType<OkObjectResult>(await ctlC.TakeOverRequest(s.RequestId, Dto(s.BuyerB)));
        await using var ctxB2 = NewCtx(); var (ctlB2, _) = RealCtl(ctxB2, s.BuyerB);
        foreach (var expected in new[] { s.Owner, s.BuyerB })
        {
            var refused = Assert.IsType<ConflictObjectResult>(await ctlB2.TakeOverRequest(s.RequestId, Dto(expected)));
            var pd = Assert.IsType<ProblemDetails>(refused.Value);
            Assert.Equal(BuyerTakeOverPolicy.CodeStale, pd.Extensions["code"]); Assert.Equal(s.BuyerC, pd.Extensions["currentBuyerId"]);
        }
        var after3 = await SnapshotAsync(s);
        Assert.Equal(s.BuyerC, after3.BuyerId); Assert.Equal(2, after3.Transfers); Assert.Equal(2, after3.FieldChanges); Assert.Equal(6, after3.Outbox); Assert.Equal(6, after3.InApp);
    }

    [Fact]
    public async Task Simultaneous_takeovers_with_the_same_expected_owner_select_exactly_one_winner()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        IActionResult? resultC = null;
        // Buyer B's controller: right before its first SaveChanges, Buyer C's takeover commits on its own connection.
        await using var racingB = NewCtxWithRace(async () =>
        {
            await using var ctxC = NewCtx();
            var (ctlC, _) = RealCtl(ctxC, s.BuyerC);
            resultC = await ctlC.TakeOverRequest(s.RequestId, Dto(s.Owner));
        });
        var (ctlB, _) = RealCtl(racingB, s.BuyerB);
        var resultB = await ctlB.TakeOverRequest(s.RequestId, Dto(s.Owner));

        Assert.IsType<OkObjectResult>(resultC);
        var conflict = Assert.IsType<ConflictObjectResult>(resultB);
        Assert.Equal(BuyerTakeOverPolicy.CodeConcurrent, Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
        var snap = await SnapshotAsync(s);
        Assert.Equal(s.BuyerC, snap.BuyerId); Assert.Equal(1, snap.Transfers); Assert.Equal(1, snap.FieldChanges); Assert.Equal(3, snap.Outbox);   // only the winner's rows
        Assert.Null(racingB.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Stale_confirmation_after_a_completed_takeover_is_refused_and_never_overwrites_the_newer_owner()
    {
        if (!CanConnect()) return; var s = await SeedAsync(); if (s == null) return;
        await using (var ctxB = NewCtx()) { var (ctlB, _) = RealCtl(ctxB, s.BuyerB); Assert.IsType<OkObjectResult>(await ctlB.TakeOverRequest(s.RequestId, Dto(s.Owner))); }
        await using var ctxC = NewCtx();
        var (ctlC, _) = RealCtl(ctxC, s.BuyerC);
        var stale = Assert.IsType<ConflictObjectResult>(await ctlC.TakeOverRequest(s.RequestId, Dto(s.Owner /* the screen C still shows */)));
        var pd = Assert.IsType<ProblemDetails>(stale.Value);
        Assert.Equal(BuyerTakeOverPolicy.CodeStale, pd.Extensions["code"]); Assert.Equal(s.BuyerB, pd.Extensions["currentBuyerId"]);
        var snap = await SnapshotAsync(s);
        Assert.Equal(s.BuyerB, snap.BuyerId); Assert.Equal(1, snap.Transfers); Assert.Equal(3, snap.Outbox);
        // With the fresh owner the second takeover proceeds (C takes over from B).
        Assert.IsType<OkObjectResult>(await ctlC.TakeOverRequest(s.RequestId, Dto(s.BuyerB)));
        Assert.Equal(s.BuyerC, (await SnapshotAsync(s)).BuyerId);
    }
}
