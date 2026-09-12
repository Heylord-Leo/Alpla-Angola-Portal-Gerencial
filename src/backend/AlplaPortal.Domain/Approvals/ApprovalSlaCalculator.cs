namespace AlplaPortal.Domain.Approvals;

/// <summary>
/// v2.244.0 Approval Center V2 — Phase 3. Pure SLA duration pairing for ONE request's chronological
/// event stream. Two proven engines (see the Phase-3 gate):
///
///  • SCALAR (PAYMENT + legacy pre-batch QUOTATION): status-transition cycle pairing. A stage's ENTRY
///    is the most-recent prior event whose NewStatus is that stage's waiting status; the stage's
///    DECISION is the classified scalar decision leaving that waiting status. EXACT. Repeated cycles
///    (return → re-entry → decision) pair chronologically and never merge.
///
///  • BATCH (QUOTATION): per-lote action sequence keyed by (Lote #N), which every batch event carries.
///    Area = BATCH_CREATED[L] → BATCH_AREA_APPROVED[L]; Final = BATCH_AREA_APPROVED[L] →
///    BATCH_FINAL_APPROVED[L]. EXACT per lote; a lote with a missing number or duplicate events is
///    skipped (never fabricated, never double-counted).
///
/// TOTAL: scalar = first area-entry → terminal final APPROVED (EXACT); batch = earliest BATCH_CREATED
/// → latest BATCH_FINAL_APPROVED (REQUEST_LEVEL, one sample per request).
///
/// Every duration sample carries the CLOSING decision's actor so approver-speed metrics measure the
/// wait in that approver's stage until their decision — never time since request creation.
/// </summary>
public static class ApprovalSlaCalculator
{
    public const string WaitingArea = ApprovalHistoryClassifier.WaitingAreaApproval;
    public const string WaitingFinal = ApprovalHistoryClassifier.WaitingFinalApproval;

    public static List<DurationSample> ComputeForRequest(IEnumerable<SlaEvent> events)
    {
        var ordered = events.OrderBy(e => e.CreatedAtUtc).ToList();
        var samples = new List<DurationSample>();
        ComputeScalar(ordered, samples);
        ComputeBatch(ordered, samples);
        return samples;
    }

    private static void ComputeScalar(List<SlaEvent> ordered, List<DurationSample> outSamples)
    {
        DateTime? firstAreaEntry = null, lastAreaEntry = null, lastFinalEntry = null;

        foreach (var e in ordered)
        {
            // Track stage entries generically by the resulting status (SUBMIT, item-award, group
            // creation, re-submission … all count — we key on the status, not the action).
            if (e.NewStatusCode == WaitingArea)
            {
                lastAreaEntry = e.CreatedAtUtc;
                firstAreaEntry ??= e.CreatedAtUtc;
            }
            if (e.NewStatusCode == WaitingFinal)
                lastFinalEntry = e.CreatedAtUtc;

            // Only SCALAR decisions drive scalar durations (batch decisions are handled by the batch
            // engine). Scalar decision codes: APPROVE / REJECT / REQUEST_ADJUSTMENT.
            bool isScalarDecision = e.IsApprovalDecision && e.ActionTaken is "APPROVE" or "REJECT" or "REQUEST_ADJUSTMENT";
            if (!isScalarDecision) continue;

            if (e.Level == ApprovalLevel.Area && lastAreaEntry.HasValue)
            {
                outSamples.Add(Sample(StageKind.Area, lastAreaEntry.Value, e));
                lastAreaEntry = null;
            }
            else if (e.Level == ApprovalLevel.Final && lastFinalEntry.HasValue)
            {
                outSamples.Add(Sample(StageKind.Final, lastFinalEntry.Value, e));
                lastFinalEntry = null;
                // Terminal final APPROVED closes a total-approval cycle.
                if (e.Decision == ApprovalDecision.Approved && firstAreaEntry.HasValue)
                {
                    outSamples.Add(Sample(StageKind.Total, firstAreaEntry.Value, e));
                    firstAreaEntry = null;
                }
            }
        }
    }

    private static void ComputeBatch(List<SlaEvent> ordered, List<DurationSample> outSamples)
    {
        var batchEvents = ordered.Where(e =>
            e.ActionTaken is "BATCH_CREATED" or "BATCH_AREA_APPROVED" or "BATCH_FINAL_APPROVED"
            && e.LoteNumber.HasValue).ToList();
        if (batchEvents.Count == 0) return;

        foreach (var g in batchEvents.GroupBy(e => e.LoteNumber!.Value))
        {
            var created = g.Where(e => e.ActionTaken == "BATCH_CREATED").ToList();
            var areaApproved = g.Where(e => e.ActionTaken == "BATCH_AREA_APPROVED").ToList();
            var finalApproved = g.Where(e => e.ActionTaken == "BATCH_FINAL_APPROVED").ToList();

            // Area: require exactly one created + one area-approved for this lote (else ambiguous → skip).
            if (created.Count == 1 && areaApproved.Count == 1)
                outSamples.Add(Sample(StageKind.Area, created[0].CreatedAtUtc, areaApproved[0]));

            // Final: require exactly one area-approved + one final-approved.
            if (areaApproved.Count == 1 && finalApproved.Count == 1)
                outSamples.Add(Sample(StageKind.Final, areaApproved[0].CreatedAtUtc, finalApproved[0]));
        }

        // Request-level TOTAL: earliest created → latest final (one per request), REQUEST_LEVEL quality.
        var allCreated = batchEvents.Where(e => e.ActionTaken == "BATCH_CREATED").ToList();
        var allFinal = batchEvents.Where(e => e.ActionTaken == "BATCH_FINAL_APPROVED").ToList();
        if (allCreated.Count > 0 && allFinal.Count > 0)
        {
            var start = allCreated.Min(e => e.CreatedAtUtc);
            var end = allFinal.Max(e => e.CreatedAtUtc);
            if (end >= start)
            {
                var closing = allFinal.OrderBy(e => e.CreatedAtUtc).Last();
                outSamples.Add(new DurationSample
                {
                    Stage = StageKind.Total,
                    Seconds = (long)(end - start).TotalSeconds,
                    Quality = DurationQuality.RequestLevel,
                    ApproverUserId = closing.ActorUserId,
                    ApproverName = closing.ActorName,
                    DecisionAtUtc = end,
                });
            }
        }
    }

    private static DurationSample Sample(StageKind stage, DateTime entry, SlaEvent decision) => new()
    {
        Stage = stage,
        Seconds = Math.Max(0, (long)(decision.CreatedAtUtc - entry).TotalSeconds),
        Quality = DurationQuality.Exact,
        ApproverUserId = decision.ActorUserId,
        ApproverName = decision.ActorName,
        DecisionAtUtc = decision.CreatedAtUtc,
    };
}

public enum StageKind { Area, Final, Total }

public enum DurationQuality { Exact, RequestLevel, Unavailable }

/// <summary>One row fed to the calculator (already classified via ApprovalHistoryClassifier).</summary>
public sealed class SlaEvent
{
    public DateTime CreatedAtUtc { get; set; }
    public string ActionTaken { get; set; } = "";
    public string? NewStatusCode { get; set; }
    public string? PreviousStatusCode { get; set; }
    public int? LoteNumber { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorName { get; set; } = "---";
    public ApprovalLevel? Level { get; set; }
    public ApprovalDecision? Decision { get; set; }
    public bool IsApprovalDecision { get; set; }
}

public sealed class DurationSample
{
    public StageKind Stage { get; set; }
    public long Seconds { get; set; }
    public DurationQuality Quality { get; set; }
    public Guid ApproverUserId { get; set; }
    public string ApproverName { get; set; } = "---";
    public DateTime DecisionAtUtc { get; set; }
}
