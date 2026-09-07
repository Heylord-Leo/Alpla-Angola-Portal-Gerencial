using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

// Dashboard V2 B9.2 — live capture behavior through ApplicationDbContext.SaveChanges (in-memory). Proves
// the snapshot/transition rules: enter, move-with-reset, no-reset on metadata/same-stage edits, one
// snapshot per entity, terminal removal with history, re-entry, and multi-entity single-save capture.
public class OperationalStageCaptureTests
{
    private static ApplicationDbContext Db()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RequestPoGroup Group(Guid id, Guid requestId, string status)
        => new() { Id = id, RequestId = requestId, Status = status };

    [Fact]
    public void First_active_stage_creates_snapshot_and_transition_sharing_one_timestamp()
    {
        using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.WaitingPo));
        db.SaveChanges();

        var snap = Assert.Single(db.OperationalStageStates.Where(s => s.EntityId == g).ToList());
        Assert.Equal(OperationalStageEntityTypes.PoGroup, snap.EntityType);
        Assert.Equal("PO_WAITING", snap.StageCode);
        Assert.Equal("PO", snap.Domain);
        Assert.Equal(r, snap.RequestId);
        Assert.NotNull(snap.StageEnteredAtUtc);
        Assert.Equal(OperationalStageSources.Live, snap.Source);
        Assert.False(snap.IsBackfilled);

        var t = Assert.Single(db.OperationalStageTransitions.Where(x => x.EntityId == g).ToList());
        Assert.Null(t.FromStageCode);
        Assert.Equal("PO_WAITING", t.ToStageCode);
        Assert.Equal(snap.StageEnteredAtUtc, t.OccurredAtUtc); // shared timestamp
    }

    [Fact]
    public void Real_stage_change_updates_one_snapshot_appends_one_event_and_resets_entry()
    {
        using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.WaitingPo));
        db.SaveChanges();
        var firstEntered = db.OperationalStageStates.Single(s => s.EntityId == g).StageEnteredAtUtc;

        var grp = db.RequestPoGroups.Single(x => x.Id == g);
        grp.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection;
        db.SaveChanges();

        var snap = Assert.Single(db.OperationalStageStates.Where(s => s.EntityId == g).ToList());
        Assert.Equal("PO_CORRECTION", snap.StageCode);
        Assert.True(snap.StageEnteredAtUtc >= firstEntered); // reset on real change

        var events = db.OperationalStageTransitions.Where(x => x.EntityId == g).OrderBy(x => x.OccurredAtUtc).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal("PO_WAITING", events[1].FromStageCode);
        Assert.Equal("PO_CORRECTION", events[1].ToStageCode);
    }

    [Fact]
    public void Metadata_only_edit_does_not_reset_or_append()
    {
        using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.WaitingPo));
        db.SaveChanges();
        var entered = db.OperationalStageStates.Single(s => s.EntityId == g).StageEnteredAtUtc;

        var grp = db.RequestPoGroups.Single(x => x.Id == g);
        grp.PurchaseOrderNumber = "PO-12345"; // non-status edit
        db.SaveChanges();

        Assert.Equal(entered, db.OperationalStageStates.Single(s => s.EntityId == g).StageEnteredAtUtc);
        Assert.Single(db.OperationalStageTransitions.Where(x => x.EntityId == g).ToList());
    }

    [Fact]
    public void Raw_status_change_within_same_canonical_stage_does_not_reset()
    {
        using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.PoIssued)); // → FIN_NEEDS_SCHEDULING
        db.SaveChanges();
        var entered = db.OperationalStageStates.Single(s => s.EntityId == g).StageEnteredAtUtc;

        var grp = db.RequestPoGroups.Single(x => x.Id == g);
        grp.Status = RequestConstants.PoGroupStatuses.PaymentRequestSent; // also → FIN_NEEDS_SCHEDULING
        db.SaveChanges();

        var snap = db.OperationalStageStates.Single(s => s.EntityId == g);
        Assert.Equal("FIN_NEEDS_SCHEDULING", snap.StageCode);
        Assert.Equal(entered, snap.StageEnteredAtUtc);                       // no reset
        Assert.Single(db.OperationalStageTransitions.Where(x => x.EntityId == g).ToList()); // no new event
    }

    [Fact]
    public void Terminal_exit_removes_snapshot_and_records_terminal_history()
    {
        using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.WaitingReceipt));
        db.SaveChanges();

        var grp = db.RequestPoGroups.Single(x => x.Id == g);
        grp.Status = RequestConstants.PoGroupStatuses.Completed;
        db.SaveChanges();

        Assert.Empty(db.OperationalStageStates.Where(s => s.EntityId == g).ToList()); // snapshot removed
        var events = db.OperationalStageTransitions.Where(x => x.EntityId == g).OrderBy(x => x.OccurredAtUtc).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal("REC_WAITING", events[1].FromStageCode);
        Assert.Equal(OperationalStageTerminalCodes.Completed, events[1].ToStageCode); // real terminal event, not silent delete
    }

    [Fact]
    public void Re_entry_into_same_stage_later_appends_another_event()
    {
        using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.WaitingPo));
        db.SaveChanges();
        var grp = db.RequestPoGroups.Single(x => x.Id == g);
        grp.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection; db.SaveChanges();
        grp.Status = RequestConstants.PoGroupStatuses.WaitingPo; db.SaveChanges();

        Assert.Equal(2, db.OperationalStageTransitions.Count(x => x.EntityId == g && x.ToStageCode == "PO_WAITING"));
        Assert.Single(db.OperationalStageStates.Where(s => s.EntityId == g).ToList()); // still one snapshot
    }

    [Fact]
    public void No_candidate_status_creates_no_rows()
    {
        using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.Pending)); // maps to null
        db.SaveChanges();
        Assert.Empty(db.OperationalStageStates.ToList());
        Assert.Empty(db.OperationalStageTransitions.ToList());
    }

    [Fact]
    public void Payment_completion_moves_the_aging_clock_to_receiving_not_finance()
    {
        using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.PaymentScheduled)); // FIN_SCHEDULED
        db.SaveChanges();
        var finEntered = db.OperationalStageStates.Single(s => s.EntityId == g).StageEnteredAtUtc;

        // Finance completes payment → group PAYMENT_COMPLETED. Exclusive aging: dwell owner becomes Receiving.
        db.RequestPoGroups.Single(x => x.Id == g).Status = RequestConstants.PoGroupStatuses.PaymentCompleted;
        db.SaveChanges();

        var snap = Assert.Single(db.OperationalStageStates.Where(s => s.EntityId == g).ToList());
        Assert.Equal("REC_READY", snap.StageCode);          // not FIN_PAID
        Assert.Equal("RECEBIMENTO", snap.Domain);
        Assert.True(snap.StageEnteredAtUtc >= finEntered);   // clock reset at the payment→receiving boundary

        var events = db.OperationalStageTransitions.Where(x => x.EntityId == g).OrderBy(x => x.OccurredAtUtc).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal("FIN_SCHEDULED", events[1].FromStageCode);
        Assert.Equal("REC_READY", events[1].ToStageCode);
        // No FIN_PAID aging snapshot is ever produced.
        Assert.DoesNotContain(db.OperationalStageStates.ToList(), s => s.StageCode == "FIN_PAID");
    }

    [Fact]
    public void Ready_to_receipt_then_move_to_receipt_advances_within_receiving()
    {
        using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.PaymentCompleted)); // REC_READY
        db.SaveChanges();
        Assert.Equal("REC_READY", db.OperationalStageStates.Single(s => s.EntityId == g).StageCode);

        db.RequestPoGroups.Single(x => x.Id == g).Status = RequestConstants.PoGroupStatuses.WaitingReceipt;
        db.SaveChanges();

        var snap = Assert.Single(db.OperationalStageStates.Where(s => s.EntityId == g).ToList());
        Assert.Equal("REC_WAITING", snap.StageCode);
        var events = db.OperationalStageTransitions.Where(x => x.EntityId == g).OrderBy(x => x.OccurredAtUtc).ToList();
        Assert.Equal("REC_READY", events[1].FromStageCode);
        Assert.Equal("REC_WAITING", events[1].ToStageCode);
    }

    [Fact]
    public async Task Async_save_path_captures_identically_to_sync()
    {
        await using var db = Db();
        var g = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g, r, RequestConstants.PoGroupStatuses.WaitingPo));
        await db.SaveChangesAsync();

        var snap = Assert.Single(db.OperationalStageStates.Where(s => s.EntityId == g).ToList());
        Assert.Equal("PO_WAITING", snap.StageCode);
        Assert.Equal(OperationalStageSources.Live, snap.Source);
        Assert.Single(db.OperationalStageTransitions.Where(x => x.EntityId == g).ToList());
    }

    [Fact]
    public void One_save_changing_multiple_entities_captures_all_of_them()
    {
        using var db = Db();
        var g1 = Guid.NewGuid(); var g2 = Guid.NewGuid(); var b = Guid.NewGuid(); var r = Guid.NewGuid();
        db.RequestPoGroups.Add(Group(g1, r, RequestConstants.PoGroupStatuses.WaitingPo));
        db.RequestPoGroups.Add(Group(g2, r, RequestConstants.PoGroupStatuses.PaymentScheduled));
        db.ApprovalBatches.Add(new ApprovalBatch { Id = b, RequestId = r, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval });
        db.SaveChanges();

        Assert.Equal(3, db.OperationalStageStates.Count());
        Assert.Equal(3, db.OperationalStageTransitions.Count());
        Assert.Equal("PO_WAITING", db.OperationalStageStates.Single(s => s.EntityId == g1).StageCode);
        Assert.Equal("FIN_SCHEDULED", db.OperationalStageStates.Single(s => s.EntityId == g2).StageCode);
        Assert.Equal("AREA_APPROVAL", db.OperationalStageStates.Single(s => s.EntityId == b).StageCode);
    }
}
