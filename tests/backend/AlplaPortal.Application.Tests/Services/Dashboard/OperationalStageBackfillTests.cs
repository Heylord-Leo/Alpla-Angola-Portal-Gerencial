using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

// Dashboard V2 B9.3 — honest legacy backfill. Behavior on in-memory EF: current-snapshot only, reliable
// timestamp where provable else null, Source=BACKFILL, idempotent, LIVE always wins, never writes history,
// Buyer never present. Because seeding via SaveChanges triggers B9.2 live capture, the helpers wipe the
// stage tables after seeding to reproduce a LEGACY baseline (entities that predate capture).
public class OperationalStageBackfillTests
{
    private static ApplicationDbContext Db()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // Seed entities, then clear the snapshots/history that live capture produced → clean legacy baseline.
    private static void WipeStageTables(ApplicationDbContext db)
    {
        db.OperationalStageStates.RemoveRange(db.OperationalStageStates);
        db.OperationalStageTransitions.RemoveRange(db.OperationalStageTransitions);
        db.SaveChanges();
    }

    private static RequestPoGroup Group(Guid id, Guid r, string status, DateTime? opReceipt = null) => new()
    {
        Id = id, RequestId = r, Status = status,
        CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        OperationalReceiptCompletedAtUtc = opReceipt,
    };

    // ── Pure evidence rules ──
    [Fact]
    public void Evidence_area_approval_uses_batch_created_final_and_adjustment_are_null()
    {
        var t = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(t, OperationalStageBackfillEvidence.ForApprovalBatch(PipelineStages.AreaApproval, t));
        Assert.Null(OperationalStageBackfillEvidence.ForApprovalBatch(PipelineStages.FinalApproval, t));
        Assert.Null(OperationalStageBackfillEvidence.ForApprovalBatch(PipelineStages.Adjustment, t));
    }

    [Fact]
    public void Evidence_po_group_only_fin_scheduled_and_documentation_yield_timestamps()
    {
        var sched = new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc);
        var receipt = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(sched, OperationalStageBackfillEvidence.ForPoGroup(PipelineStages.FinanceScheduled, sched, null));
        Assert.Equal(receipt, OperationalStageBackfillEvidence.ForPoGroup(PipelineStages.Documentation, null, receipt));
        // Everything else is unknown regardless of what dates are available.
        foreach (var s in new[] { PipelineStages.PoWaiting, PipelineStages.PoCorrection, PipelineStages.FinanceNeedsScheduling,
                     PipelineStages.ReceivingReady, PipelineStages.ReceivingWaiting, PipelineStages.ReceivingFollowup, PipelineStages.ReceivingSupplier })
            Assert.Null(OperationalStageBackfillEvidence.ForPoGroup(s, sched, receipt));
        // FIN_SCHEDULED with no scheduled payment → null (honest).
        Assert.Null(OperationalStageBackfillEvidence.ForPoGroup(PipelineStages.FinanceScheduled, null, null));
    }

    // ── Service: creates honest snapshots ──
    [Fact]
    public async Task Backfill_creates_backfill_snapshots_with_reliable_or_null_entry()
    {
        using var db = Db();
        var r = Guid.NewGuid();
        var areaBatch = Guid.NewGuid();
        var batchCreated = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        db.ApprovalBatches.Add(new ApprovalBatch { Id = areaBatch, RequestId = r, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = batchCreated });

        var finGroup = Guid.NewGuid();
        var poGroup = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(finGroup, r, RequestConstants.PoGroupStatuses.PaymentScheduled));
        db.RequestPoGroups.Add(Group(poGroup, r, RequestConstants.PoGroupStatuses.WaitingPo));
        var payCreated = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
        db.RequestPayments.Add(new RequestPayment
        {
            RequestId = r, RequestPoGroupId = finGroup,
            PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, PaymentType = RequestPayment.PaymentTypes.FinalBalance,
            ScheduledDateUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), // deadline — must NOT be used
            CreatedAtUtc = payCreated,
        });
        db.SaveChanges();
        WipeStageTables(db);

        var result = await new OperationalStageBackfillService(db).BackfillAsync(dryRun: false);

        var area = db.OperationalStageStates.Single(s => s.EntityId == areaBatch);
        Assert.Equal("AREA_APPROVAL", area.StageCode);
        Assert.Equal(batchCreated, area.StageEnteredAtUtc);      // reliable (batch created at AREA)
        Assert.Equal(OperationalStageSources.Backfill, area.Source);
        Assert.True(area.IsBackfilled);

        var fin = db.OperationalStageStates.Single(s => s.EntityId == finGroup);
        Assert.Equal("FIN_SCHEDULED", fin.StageCode);
        Assert.Equal(payCreated, fin.StageEnteredAtUtc);         // reliable = SCHEDULED payment creation, NOT the deadline

        var po = db.OperationalStageStates.Single(s => s.EntityId == poGroup);
        Assert.Equal("PO_WAITING", po.StageCode);
        Assert.Null(po.StageEnteredAtUtc);                       // honest unknown (group created at PENDING)

        Assert.Empty(db.OperationalStageTransitions.ToList());   // NEVER writes history
        Assert.Equal(2, result.ReliableTimestampCount);
        Assert.Equal(1, result.UnknownTimestampCount);
        Assert.Equal(3, result.Created);
    }

    [Fact]
    public async Task Honesty_stages_without_evidence_are_null_even_when_other_dates_exist()
    {
        using var db = Db();
        var r = Guid.NewGuid();
        // A REC_READY group carrying CreatedAtUtc + UpdatedAtUtc must still get a NULL entry time.
        var g = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.PaymentCompleted));
        db.SaveChanges();
        WipeStageTables(db);

        await new OperationalStageBackfillService(db).BackfillAsync(dryRun: false);
        var snap = db.OperationalStageStates.Single(s => s.EntityId == g);
        Assert.Equal("REC_READY", snap.StageCode);
        Assert.Null(snap.StageEnteredAtUtc); // never CreatedAtUtc/UpdatedAtUtc fallback
    }

    [Fact]
    public async Task Out_of_scope_and_pending_produce_no_snapshot_and_buyer_is_absent()
    {
        using var db = Db();
        var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(Guid.NewGuid(), r, RequestConstants.PoGroupStatuses.Pending));   // pre-active
        db.RequestPoGroups.Add(Group(Guid.NewGuid(), r, RequestConstants.PoGroupStatuses.Completed));  // terminal
        db.SaveChanges();
        WipeStageTables(db);

        var result = await new OperationalStageBackfillService(db).BackfillAsync(dryRun: false);
        Assert.Empty(db.OperationalStageStates.ToList());
        // No REQUEST/Buyer snapshot is ever produced by backfill.
        Assert.DoesNotContain(db.OperationalStageStates.ToList(), s => s.EntityType == OperationalStageEntityTypes.Request);
        Assert.Equal(0, result.Created);
    }

    [Fact]
    public async Task Dry_run_writes_nothing_but_reports_proposed_counts()
    {
        using var db = Db();
        var r = Guid.NewGuid();
        db.ApprovalBatches.Add(new ApprovalBatch { Id = Guid.NewGuid(), RequestId = r, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = DateTime.UtcNow });
        db.SaveChanges();
        WipeStageTables(db);

        var result = await new OperationalStageBackfillService(db).BackfillAsync(dryRun: true);
        Assert.True(result.DryRun);
        Assert.Equal(1, result.Created);            // proposed
        Assert.Empty(db.OperationalStageStates.ToList()); // but nothing written
    }

    // ── Idempotency & precedence ──
    [Fact]
    public async Task Rerun_is_idempotent_and_never_overwrites_live()
    {
        using var db = Db();
        var r = Guid.NewGuid();
        var g = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.WaitingPo));
        db.SaveChanges();
        // Keep the LIVE snapshot that capture produced (do NOT wipe) — simulates an entity captured live.
        var live = db.OperationalStageStates.Single(s => s.EntityId == g);
        Assert.Equal(OperationalStageSources.Live, live.Source);
        var liveEntered = live.StageEnteredAtUtc;

        var result = await new OperationalStageBackfillService(db).BackfillAsync(dryRun: false);
        var after = db.OperationalStageStates.Single(s => s.EntityId == g);
        Assert.Equal(OperationalStageSources.Live, after.Source);   // untouched
        Assert.Equal(liveEntered, after.StageEnteredAtUtc);
        Assert.Equal(1, result.SkippedLive);
        Assert.Equal(0, result.Created);
    }

    // The improve (null→reliable) and stale-stage-correction DECISIONS are asserted here via dry-run (no
    // writes); the actual conditional MUTATION and the LIVE-race precedence are proven against a real
    // relational DB in OperationalStageBackfillConcurrencyTests (ExecuteUpdate is relational-only).
    [Fact]
    public async Task Improve_decision_is_taken_when_backfill_null_meets_reliable_evidence()
    {
        using var db = Db();
        var r = Guid.NewGuid();
        var g = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.PaymentScheduled)); // FIN_SCHEDULED
        db.RequestPayments.Add(new RequestPayment
        {
            RequestId = r, RequestPoGroupId = g, PaymentStatus = RequestPayment.PaymentStatuses.Scheduled,
            PaymentType = RequestPayment.PaymentTypes.FinalBalance, CreatedAtUtc = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc),
        });
        db.SaveChanges();
        WipeStageTables(db);
        db.OperationalStageStates.Add(new OperationalStageState
        {
            Id = Guid.NewGuid(), EntityType = OperationalStageEntityTypes.PoGroup, EntityId = g, RequestId = r,
            Domain = "FINANCAS", StageCode = "FIN_SCHEDULED", StageEnteredAtUtc = null,
            Source = OperationalStageSources.Backfill, IsBackfilled = true, CreatedAtUtc = DateTime.UtcNow,
        });
        db.SaveChanges();

        var result = await new OperationalStageBackfillService(db).BackfillAsync(dryRun: true);
        Assert.Equal(1, result.Improved);
        Assert.Equal(0, result.Created);
    }

    [Fact]
    public async Task Stale_backfill_stage_correction_is_decided_when_canonical_stage_differs()
    {
        using var db = Db();
        var r = Guid.NewGuid();
        var g = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.WaitingPo));
        db.SaveChanges();
        WipeStageTables(db);
        db.OperationalStageStates.Add(new OperationalStageState
        {
            Id = Guid.NewGuid(), EntityType = OperationalStageEntityTypes.PoGroup, EntityId = g, RequestId = r,
            Domain = "FINANCAS", StageCode = "FIN_SCHEDULED", StageEnteredAtUtc = null,
            Source = OperationalStageSources.Backfill, IsBackfilled = true, CreatedAtUtc = DateTime.UtcNow,
        });
        db.SaveChanges();

        var result = await new OperationalStageBackfillService(db).BackfillAsync(dryRun: true);
        Assert.Equal(1, result.Updated);
    }

    // Honesty is proven behaviorally above: FIN_SCHEDULED uses the SCHEDULED payment's CreatedAtUtc and
    // explicitly NOT its far-future ScheduledDateUtc; stages without evidence stay null even when the entity
    // carries CreatedAtUtc/UpdatedAtUtc; and no OperationalStageTransition row is ever written.
}
