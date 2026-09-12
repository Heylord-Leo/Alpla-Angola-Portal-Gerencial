using AlplaPortal.Domain.Approvals;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

// v2.244.0 Phase 3 — SLA pairing (§29 A–I). Proves PAYMENT scalar cycle pairing, single/multi-batch
// per-lote pairing, no double-counting, and repeated-cycle chronological pairing.
public class ApprovalSlaCalculatorTests
{
    private static readonly DateTime T0 = new(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AreaApprover = Guid.NewGuid();
    private static readonly Guid FinalApprover = Guid.NewGuid();

    private static SlaEvent Entry(string newStatus, double hours, string action = "SUBMIT") => new()
    { CreatedAtUtc = T0.AddHours(hours), ActionTaken = action, NewStatusCode = newStatus, ActorUserId = AreaApprover, ActorName = "sys" };

    private static SlaEvent ScalarDecision(ApprovalLevel level, ApprovalDecision decision, string prev, string @new, double hours, Guid actor) => new()
    {
        CreatedAtUtc = T0.AddHours(hours), ActionTaken = decision == ApprovalDecision.Approved ? "APPROVE" : decision == ApprovalDecision.Rejected ? "REJECT" : "REQUEST_ADJUSTMENT",
        PreviousStatusCode = prev, NewStatusCode = @new, Level = level, Decision = decision, IsApprovalDecision = true,
        ActorUserId = actor, ActorName = actor == FinalApprover ? "Final" : "Area",
    };

    private static SlaEvent Batch(string action, int lote, double hours, Guid actor) => new()
    { CreatedAtUtc = T0.AddHours(hours), ActionTaken = action, LoteNumber = lote, ActorUserId = actor, ActorName = "b" };

    private static long Sec(double hours) => (long)(hours * 3600);

    // ── A/B/C: PAYMENT scalar full flow ──
    [Fact]
    public void Payment_Area_Final_Total_ExactDurations()
    {
        var events = new[]
        {
            Entry("WAITING_AREA_APPROVAL", 0),
            ScalarDecision(ApprovalLevel.Area, ApprovalDecision.Approved, "WAITING_AREA_APPROVAL", "WAITING_FINAL_APPROVAL", 2, AreaApprover),
            ScalarDecision(ApprovalLevel.Final, ApprovalDecision.Approved, "WAITING_FINAL_APPROVAL", "APPROVED", 5, FinalApprover),
        };
        var s = ApprovalSlaCalculator.ComputeForRequest(events);

        var area = Assert.Single(s.Where(x => x.Stage == StageKind.Area));
        Assert.Equal(Sec(2), area.Seconds);
        Assert.Equal(AreaApprover, area.ApproverUserId);
        Assert.Equal(DurationQuality.Exact, area.Quality);

        var final = Assert.Single(s.Where(x => x.Stage == StageKind.Final));
        Assert.Equal(Sec(3), final.Seconds); // WAITING_FINAL entry at +2h → decision +5h
        Assert.Equal(FinalApprover, final.ApproverUserId);

        var total = Assert.Single(s.Where(x => x.Stage == StageKind.Total));
        Assert.Equal(Sec(5), total.Seconds); // first area entry (0) → final approved (+5h)
        Assert.Equal(DurationQuality.Exact, total.Quality);
    }

    // ── H: reject cycle produces an area sample and no total ──
    [Fact]
    public void Payment_AreaReject_NoTotal()
    {
        var events = new[]
        {
            Entry("WAITING_AREA_APPROVAL", 0),
            ScalarDecision(ApprovalLevel.Area, ApprovalDecision.Rejected, "WAITING_AREA_APPROVAL", "REJECTED", 2, AreaApprover),
        };
        var s = ApprovalSlaCalculator.ComputeForRequest(events);
        Assert.Equal(Sec(2), Assert.Single(s.Where(x => x.Stage == StageKind.Area)).Seconds);
        Assert.Empty(s.Where(x => x.Stage == StageKind.Total));
    }

    // ── G/I: repeated cycle (return → re-entry → approve) pairs chronologically, never merges ──
    [Fact]
    public void RepeatedCycle_PairsChronologically_NoMerge()
    {
        var events = new[]
        {
            Entry("WAITING_AREA_APPROVAL", 0),
            ScalarDecision(ApprovalLevel.Area, ApprovalDecision.Returned, "WAITING_AREA_APPROVAL", "AREA_ADJUSTMENT", 1, AreaApprover), // return, 1h
            Entry("WAITING_AREA_APPROVAL", 2, "SUBMIT"), // re-entry
            ScalarDecision(ApprovalLevel.Area, ApprovalDecision.Approved, "WAITING_AREA_APPROVAL", "WAITING_FINAL_APPROVAL", 3, AreaApprover), // approve, 1h after re-entry
        };
        var s = ApprovalSlaCalculator.ComputeForRequest(events);
        var area = s.Where(x => x.Stage == StageKind.Area).OrderBy(x => x.DecisionAtUtc).ToList();
        Assert.Equal(2, area.Count);
        Assert.Equal(Sec(1), area[0].Seconds); // entry 0 → return 1
        Assert.Equal(Sec(1), area[1].Seconds); // re-entry 2 → approve 3 (NOT 3h)
    }

    // ── D: single-batch QUOTATION per-lote pairing ──
    [Fact]
    public void SingleBatch_Quotation_AreaFinalTotal()
    {
        var b = FinalApprover;
        var events = new[]
        {
            Batch("BATCH_CREATED", 1, 0, AreaApprover),
            Batch("BATCH_AREA_APPROVED", 1, 1, AreaApprover),
            Batch("BATCH_FINAL_APPROVED", 1, 3, b),
        };
        var s = ApprovalSlaCalculator.ComputeForRequest(events);
        Assert.Equal(Sec(1), Assert.Single(s.Where(x => x.Stage == StageKind.Area)).Seconds);
        Assert.Equal(Sec(2), Assert.Single(s.Where(x => x.Stage == StageKind.Final)).Seconds);
        var total = Assert.Single(s.Where(x => x.Stage == StageKind.Total));
        Assert.Equal(Sec(3), total.Seconds);
        Assert.Equal(DurationQuality.RequestLevel, total.Quality); // batch total is request-level
    }

    // ── E/F: multi-batch aggregation + NO double-counting ──
    [Fact]
    public void MultiBatch_PerLoteSamples_NoDoubleCount_RequestLevelTotal()
    {
        var events = new[]
        {
            Batch("BATCH_CREATED", 1, 0, AreaApprover),
            Batch("BATCH_CREATED", 2, 0.5, AreaApprover),
            Batch("BATCH_AREA_APPROVED", 1, 1, AreaApprover),
            Batch("BATCH_AREA_APPROVED", 2, 2, AreaApprover),
            Batch("BATCH_FINAL_APPROVED", 1, 3, FinalApprover),
            Batch("BATCH_FINAL_APPROVED", 2, 4, FinalApprover),
        };
        var s = ApprovalSlaCalculator.ComputeForRequest(events);

        var area = s.Where(x => x.Stage == StageKind.Area).ToList();
        var final = s.Where(x => x.Stage == StageKind.Final).ToList();
        var total = s.Where(x => x.Stage == StageKind.Total).ToList();

        Assert.Equal(2, area.Count);   // one per lote — not 3, not 6
        Assert.Equal(2, final.Count);
        Assert.Single(total);          // request-level total is exactly ONE sample, never 5
        Assert.Equal(Sec(4), total[0].Seconds); // earliest created (0) → latest final (+4h)
        Assert.Contains(area, x => x.Seconds == Sec(1));   // lote1 area
        Assert.Contains(area, x => x.Seconds == Sec(1.5)); // lote2 area
    }

    // ambiguous lote (duplicate area-approved for same lote) → skipped, not double counted
    [Fact]
    public void AmbiguousLote_DuplicateEvents_Skipped()
    {
        var events = new[]
        {
            Batch("BATCH_CREATED", 1, 0, AreaApprover),
            Batch("BATCH_AREA_APPROVED", 1, 1, AreaApprover),
            Batch("BATCH_AREA_APPROVED", 1, 2, AreaApprover), // duplicate → ambiguous
        };
        var s = ApprovalSlaCalculator.ComputeForRequest(events);
        Assert.Empty(s.Where(x => x.Stage == StageKind.Area)); // not paired (ambiguous)
    }

    [Fact]
    public void BatchEvent_WithoutLoteNumber_Ignored()
    {
        var noLote = new SlaEvent { CreatedAtUtc = T0, ActionTaken = "BATCH_CREATED", LoteNumber = null, ActorUserId = AreaApprover, ActorName = "b" };
        var s = ApprovalSlaCalculator.ComputeForRequest(new[] { noLote });
        Assert.Empty(s);
    }
}
