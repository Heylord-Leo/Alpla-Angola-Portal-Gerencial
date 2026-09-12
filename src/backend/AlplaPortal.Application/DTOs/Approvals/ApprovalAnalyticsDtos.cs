namespace AlplaPortal.Application.DTOs.Approvals;

// v2.244.0 Approval Center V2 — Phase 3. Read-only analytics response. All figures are server-computed
// (canonical statistics); NEVER persisted. Durations are in seconds; the frontend formats them.

public sealed class ApprovalAnalyticsDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string Resolution { get; set; } = "day";

    public ApprovalAnalyticsSummaryDto Summary { get; set; } = new();
    public DurationStatsDto Duration { get; set; } = new();      // total approval duration
    public DurationStatsDto AreaDuration { get; set; } = new();
    public DurationStatsDto FinalDuration { get; set; } = new();
    public ApprovalBottleneckDto Bottleneck { get; set; } = new();
    public List<ApproverStatsDto> Approvers { get; set; } = new();
    public List<ApprovalTrendPointDto> Trend { get; set; } = new();
    public ApprovalTypeSplitDto RequestTypes { get; set; } = new();
}

public sealed class ApprovalAnalyticsSummaryDto
{
    public int TotalDecisions { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Returned { get; set; }
    public int Resubmitted { get; set; }
    public double ApprovalRate { get; set; }   // 0..1
    public double RejectionRate { get; set; }
    public double ReturnRate { get; set; }
}

public sealed class DurationStatsDto
{
    public int SampleCount { get; set; }
    public double AverageSeconds { get; set; }
    public double MedianSeconds { get; set; }
    public long P90Seconds { get; set; }
    public long MinSeconds { get; set; }
    public long MaxSeconds { get; set; }
}

public sealed class ApprovalBottleneckDto
{
    public double AreaAverageSeconds { get; set; }
    public double FinalAverageSeconds { get; set; }
    public int AreaSampleCount { get; set; }
    public int FinalSampleCount { get; set; }
    /// <summary>"AREA" | "FINAL" | null (no data / tie).</summary>
    public string? DominantStage { get; set; }
}

public sealed class ApproverStatsDto
{
    public Guid ApproverUserId { get; set; }
    public string ApproverName { get; set; } = "---";
    public int DecisionCount { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Returned { get; set; }
    // Speed = time waited in this approver's stage until their decision (null when no clean pairing).
    public int SpeedSampleCount { get; set; }
    public double? AverageDecisionSeconds { get; set; }
    public double? MedianDecisionSeconds { get; set; }
    public long? P90DecisionSeconds { get; set; }
}

public sealed class ApprovalTrendPointDto
{
    public string Bucket { get; set; } = "";   // yyyy-MM-dd (day), ISO week, or yyyy-MM
    public int Decisions { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Returned { get; set; }
    public double? AvgDurationSeconds { get; set; }
}

public sealed class ApprovalTypeSplitDto
{
    public int Quotation { get; set; }
    public int Payment { get; set; }
}
