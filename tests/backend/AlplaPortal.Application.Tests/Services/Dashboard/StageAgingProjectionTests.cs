using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

// Dashboard V2 B9.4 — Stage Aging projection (in-memory). Entitlement, scope, aggregates, known/unknown,
// thresholds, thresholdless nulls, exclusions (Buyer / FIN_PAID), Luanda age, invariants, and the real
// B9.3→B9.4 chain (backfill create path is in-memory compatible).
public class StageAgingProjectionTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext Db()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Guid Req(ApplicationDbContext db)
    {
        var id = Guid.NewGuid();
        db.Requests.Add(new Request { Id = id });
        return id;
    }

    private static void Snap(ApplicationDbContext db, string entityType, Guid requestId, string stage, DateTime? enteredAtUtc)
        => db.OperationalStageStates.Add(new OperationalStageState
        {
            Id = Guid.NewGuid(), EntityType = entityType, EntityId = Guid.NewGuid(), RequestId = requestId,
            Domain = "X", StageCode = stage, StageEnteredAtUtc = enteredAtUtc,
            Source = OperationalStageSources.Backfill, IsBackfilled = true, CreatedAtUtc = Now,
        });

    private static Task<DashboardV2StageAgingDto> Build(ApplicationDbContext db, bool entitled = true)
        => new StageAgingProjection(db).BuildAsync(db.Requests, entitled, Now, CancellationToken.None);

    private static DashboardV2StageAgingStageDto Stage(DashboardV2StageAgingDto d, string code) => d.Stages.Single(s => s.StageCode == code);

    [Fact]
    public async Task Not_entitled_returns_null_summary()
    {
        using var db = Db();
        var r = Req(db); Snap(db, OperationalStageEntityTypes.PoGroup, r, PipelineStages.PoWaiting, Now.AddDays(-2)); db.SaveChanges();
        var d = await Build(db, entitled: false);
        Assert.Null(d.Summary);
        Assert.Empty(d.Stages);
    }

    [Fact]
    public async Task Aggregates_entity_request_and_known_unknown_counts()
    {
        using var db = Db();
        var rA = Req(db); var rB = Req(db);
        Snap(db, OperationalStageEntityTypes.PoGroup, rA, PipelineStages.FinanceScheduled, Now.AddDays(-2));
        Snap(db, OperationalStageEntityTypes.PoGroup, rA, PipelineStages.FinanceScheduled, Now.AddDays(-5)); // same request
        Snap(db, OperationalStageEntityTypes.PoGroup, rB, PipelineStages.FinanceScheduled, null);            // unknown age
        db.SaveChanges();

        var d = await Build(db);
        var fin = Stage(d, PipelineStages.FinanceScheduled);
        Assert.Equal(3, fin.EntityCount);
        Assert.Equal(2, fin.RequestCount);          // distinct requests
        Assert.Equal(2, fin.KnownAgeEntityCount);
        Assert.Equal(1, fin.UnknownAgeEntityCount);
        Assert.Equal(3, d.Summary!.TotalActiveEntities);
        Assert.Equal(2, d.Summary.TotalActiveRequests);
        Assert.Equal(2, d.Summary.KnownAgeEntities);
        Assert.Equal(1, d.Summary.UnknownAgeEntities);
    }

    [Fact]
    public async Task Threshold_counts_use_known_age_only_unknown_is_not_normal()
    {
        using var db = Db();
        var r = Req(db);
        Snap(db, OperationalStageEntityTypes.ApprovalBatch, r, PipelineStages.AreaApproval, Now.AddDays(-2));  // age 2 → Normal
        Snap(db, OperationalStageEntityTypes.ApprovalBatch, r, PipelineStages.AreaApproval, Now.AddDays(-5));  // age 5 → Attention
        Snap(db, OperationalStageEntityTypes.ApprovalBatch, r, PipelineStages.AreaApproval, Now.AddDays(-10)); // age 10 → Critical
        Snap(db, OperationalStageEntityTypes.ApprovalBatch, r, PipelineStages.AreaApproval, null);             // unknown
        db.SaveChanges();

        var s = Stage(await Build(db), PipelineStages.AreaApproval);
        Assert.Equal(4, s.EntityCount);
        Assert.Equal(3, s.KnownAgeEntityCount);
        Assert.Equal(1, s.UnknownAgeEntityCount);
        Assert.Equal(1, s.NormalCount);
        Assert.Equal(1, s.AttentionCount);
        Assert.Equal(1, s.CriticalCount);            // unknown is NOT normal
        Assert.Equal(10, s.OldestAgeDays);
    }

    [Fact]
    public async Task Thresholdless_stage_has_null_severity_and_null_profile()
    {
        using var db = Db();
        var r = Req(db);
        Snap(db, OperationalStageEntityTypes.PoGroup, r, PipelineStages.FinanceScheduled, Now.AddDays(-100)); db.SaveChanges();
        var s = Stage(await Build(db), PipelineStages.FinanceScheduled);
        Assert.Null(s.NormalCount);
        Assert.Null(s.AttentionCount);
        Assert.Null(s.CriticalCount);
        Assert.Null(s.ThresholdProfile);            // 100 days but no severity for Finance
        Assert.Equal(100, s.OldestAgeDays);          // age still shown
    }

    [Fact]
    public async Task Buyer_request_and_fin_paid_snapshots_are_excluded()
    {
        using var db = Db();
        var r = Req(db);
        Snap(db, OperationalStageEntityTypes.Request, r, PipelineStages.NeedsQuotation, Now.AddDays(-3)); // Buyer/REQUEST
        Snap(db, OperationalStageEntityTypes.PoGroup, r, PipelineStages.FinancePaid, Now.AddDays(-3));    // FIN_PAID
        Snap(db, OperationalStageEntityTypes.PoGroup, r, PipelineStages.ReceivingReady, Now.AddDays(-1)); // valid
        db.SaveChanges();

        var d = await Build(db);
        Assert.DoesNotContain(d.Stages, s => s.StageCode == PipelineStages.NeedsQuotation);
        Assert.DoesNotContain(d.Stages, s => s.StageCode == PipelineStages.FinancePaid);
        Assert.Single(d.Stages);
        Assert.Equal(PipelineStages.ReceivingReady, d.Stages[0].StageCode);
        Assert.Equal(1, d.Summary!.TotalActiveEntities); // only the REC_READY row counts
    }

    [Fact]
    public async Task Scope_excludes_out_of_scope_requests()
    {
        using var db = Db();
        var inScope = Req(db); var outScope = Req(db);
        Snap(db, OperationalStageEntityTypes.PoGroup, inScope, PipelineStages.PoWaiting, Now.AddDays(-1));
        Snap(db, OperationalStageEntityTypes.PoGroup, outScope, PipelineStages.PoWaiting, Now.AddDays(-1));
        db.SaveChanges();

        var scoped = db.Requests.Where(x => x.Id == inScope);
        var d = await new StageAgingProjection(db).BuildAsync(scoped, entitled: true, Now, CancellationToken.None);
        Assert.Equal(1, d.Summary!.TotalActiveEntities); // out-of-scope snapshot excluded
    }

    [Fact]
    public async Task Canonical_order_is_pipeline_sort_and_navigation_is_off()
    {
        using var db = Db();
        var r = Req(db);
        Snap(db, OperationalStageEntityTypes.PoGroup, r, PipelineStages.ReceivingWaiting, Now.AddDays(-1)); // sort 71
        Snap(db, OperationalStageEntityTypes.ApprovalBatch, r, PipelineStages.AreaApproval, Now.AddDays(-1)); // sort 30
        Snap(db, OperationalStageEntityTypes.PoGroup, r, PipelineStages.PoWaiting, Now.AddDays(-1)); // sort 50
        db.SaveChanges();

        var d = await Build(db);
        Assert.Equal(new[] { PipelineStages.AreaApproval, PipelineStages.PoWaiting, PipelineStages.ReceivingWaiting },
            d.Stages.Select(s => s.StageCode).ToArray());
        Assert.All(d.Stages, s => { Assert.False(s.CanNavigate); Assert.Null(s.TargetPath); });
    }

    [Fact]
    public async Task Future_timestamp_ages_zero_and_classifies_normal()
    {
        using var db = Db();
        var r = Req(db);
        Snap(db, OperationalStageEntityTypes.ApprovalBatch, r, PipelineStages.AreaApproval, Now.AddDays(3)); db.SaveChanges();
        var s = Stage(await Build(db), PipelineStages.AreaApproval);
        Assert.Equal(0, s.OldestAgeDays);
        Assert.Equal(1, s.NormalCount);
    }

    [Fact]
    public async Task Summary_invariants_hold()
    {
        using var db = Db();
        var r = Req(db);
        Snap(db, OperationalStageEntityTypes.ApprovalBatch, r, PipelineStages.AreaApproval, Now.AddDays(-10)); // critical
        Snap(db, OperationalStageEntityTypes.ApprovalBatch, r, PipelineStages.AreaApproval, null);            // unknown
        Snap(db, OperationalStageEntityTypes.PoGroup, r, PipelineStages.FinanceScheduled, Now.AddDays(-2));   // thresholdless
        db.SaveChanges();

        var d = await Build(db);
        Assert.Equal(d.Stages.Sum(s => s.EntityCount), d.Summary!.TotalActiveEntities);
        Assert.Equal(d.Summary.TotalActiveEntities, d.Summary.KnownAgeEntities + d.Summary.UnknownAgeEntities);
        var area = Stage(d, PipelineStages.AreaApproval);
        Assert.Equal(area.KnownAgeEntityCount, (area.NormalCount ?? 0) + (area.AttentionCount ?? 0) + (area.CriticalCount ?? 0));
        Assert.Equal(1, d.Summary.CriticalEntities); // only the area critical; finance thresholdless contributes none
    }

    // ── B9.3 → B9.4 chain (in-memory: backfill create path is in-memory compatible) ──
    [Fact]
    public async Task Backfill_then_projection_reflects_reliable_and_unknown_ages()
    {
        using var db = Db();
        var r = Guid.NewGuid();
        db.Requests.Add(new Request { Id = r });
        var finGroup = Guid.NewGuid(); var recGroup = Guid.NewGuid();
        db.RequestPoGroups.Add(new RequestPoGroup { Id = finGroup, RequestId = r, Status = RequestConstants.PoGroupStatuses.PaymentScheduled, CreatedAtUtc = Now });
        db.RequestPoGroups.Add(new RequestPoGroup { Id = recGroup, RequestId = r, Status = RequestConstants.PoGroupStatuses.PaymentCompleted, CreatedAtUtc = Now });
        db.RequestPayments.Add(new RequestPayment
        {
            RequestId = r, RequestPoGroupId = finGroup, PaymentStatus = RequestPayment.PaymentStatuses.Scheduled,
            PaymentType = RequestPayment.PaymentTypes.FinalBalance, CreatedAtUtc = Now.AddDays(-6),
        });
        db.SaveChanges();
        // Reproduce a legacy baseline (wipe capture output), then run the real backfill create path.
        db.OperationalStageStates.RemoveRange(db.OperationalStageStates);
        db.OperationalStageTransitions.RemoveRange(db.OperationalStageTransitions);
        db.SaveChanges();
        await new OperationalStageBackfillService(db).BackfillAsync(dryRun: false);

        var d = await Build(db);
        var fin = Stage(d, PipelineStages.FinanceScheduled);
        Assert.Equal(6, fin.OldestAgeDays);          // reliable = scheduled payment created 6 Luanda-days ago
        var rec = Stage(d, PipelineStages.ReceivingReady);
        Assert.Equal(0, rec.KnownAgeEntityCount);    // REC_READY has no reliable backfill evidence → unknown
        Assert.Equal(1, rec.UnknownAgeEntityCount);
        Assert.DoesNotContain(d.Stages, s => s.StageCode == PipelineStages.FinancePaid); // never FIN_PAID
    }

    // ── No legacy projection dependency (source guard) ──
    [Fact]
    public void Projection_does_not_touch_any_legacy_projection_or_cockpit()
    {
        var src = File.ReadAllText(SourcePath("Infrastructure", "Services", "Dashboard", "StageAgingProjection.cs"));
        foreach (var forbidden in new[] { "BuyerQueueProjection", "FinanceObligationSummaryProjection",
                     "ReceivingQueueProjection", "OperationalPipelineProjection", "cockpit", "CockpitSummary" })
            Assert.DoesNotContain(forbidden, src);
    }

    private static string SourcePath(params string[] parts)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(ThisFile())!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "backend"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName, "src", "backend", "AlplaPortal." + parts[0] }.Concat(parts.Skip(1)).ToArray());
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
