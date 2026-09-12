using System.Globalization;
using AlplaPortal.Application.DTOs.Approvals;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Approvals;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Approvals;

/// <summary>
/// v2.244.0 Approval Center V2 — Phase 3. Read-only approval analytics. Loads the SLA-relevant rows
/// for the caller-scoped requests (decisions + BATCH_CREATED + stage-entry rows), runs the pure
/// <see cref="ApprovalSlaCalculator"/> per request over the FULL history (so a decision's entry is
/// never lost to the date window), then filters the resulting decisions/samples by the period and
/// aggregates canonical statistics. Classification is delegated to <see cref="ApprovalHistoryClassifier"/>
/// (no duplicated mapping). No persistence, no writes.
/// </summary>
public sealed class ApprovalAnalyticsService : IApprovalAnalyticsService
{
    private readonly ApplicationDbContext _context;

    public ApprovalAnalyticsService(ApplicationDbContext context)
    {
        _context = context;
    }

    private sealed record Row(
        Guid RequestId, string RequestTypeCode, string ActionTaken, string? PrevCode, string? NewCode,
        string? Comment, Guid ActorUserId, string ActorName, DateTime CreatedAtUtc);

    private sealed record Decision(Guid ActorUserId, string ActorName, string DecisionCode, string? Level, DateTime AtUtc, string RequestTypeCode);

    public async Task<ApprovalAnalyticsDto> GetAnalyticsAsync(
        IReadOnlyCollection<Guid> scopedRequestIds, ApprovalHistoryFilter filter, string resolution, CancellationToken ct = default)
    {
        var dto = new ApprovalAnalyticsDto { DateFrom = filter.DateFrom, DateTo = filter.DateTo, Resolution = Normalize(resolution) };
        if (scopedRequestIds.Count == 0) return dto;

        var decisionCodes = ApprovalHistoryClassifier.DecisionActionCodes;
        const string wArea = ApprovalHistoryClassifier.WaitingAreaApproval;
        const string wFinal = ApprovalHistoryClassifier.WaitingFinalApproval;

        var q = _context.RequestStatusHistories
            .AsNoTracking()
            .Include(sh => sh.Request!).ThenInclude(r => r.RequestType)
            .Include(sh => sh.ActorUser)
            .Include(sh => sh.PreviousStatus)
            .Include(sh => sh.NewStatus)
            .Where(sh => scopedRequestIds.Contains(sh.RequestId) && (
                decisionCodes.Contains(sh.ActionTaken)
                || sh.ActionTaken == "BATCH_CREATED"
                || (sh.NewStatus != null && (sh.NewStatus.Code == wArea || sh.NewStatus.Code == wFinal))));

        // Request-level filters (safe to push down — they don't affect pairing correctness).
        if (!string.IsNullOrWhiteSpace(filter.RequestType)) q = q.Where(sh => sh.Request!.RequestType!.Code == filter.RequestType);
        if (filter.DepartmentId.HasValue) q = q.Where(sh => sh.Request!.DepartmentId == filter.DepartmentId.Value);
        if (filter.CompanyId.HasValue) q = q.Where(sh => sh.Request!.CompanyId == filter.CompanyId.Value);
        if (filter.PlantId.HasValue) q = q.Where(sh => sh.Request!.PlantId == filter.PlantId.Value);

        var rows = await q
            .Select(sh => new Row(
                sh.RequestId,
                sh.Request!.RequestType!.Code,
                sh.ActionTaken ?? "",
                sh.PreviousStatus != null ? sh.PreviousStatus.Code : null,
                sh.NewStatus != null ? sh.NewStatus.Code : null,
                sh.Comment,
                sh.ActorUserId,
                sh.ActorUser!.FullName ?? "---",
                sh.CreatedAtUtc))
            .ToListAsync(ct);

        // Per request: classify, build SLA events + decisions.
        var allSamples = new List<DurationSample>();
        var allDecisions = new List<Decision>();

        foreach (var g in rows.GroupBy(r => r.RequestId))
        {
            var slaEvents = new List<SlaEvent>();
            foreach (var r in g)
            {
                var c = ApprovalHistoryClassifier.Classify(r.ActionTaken, r.NewCode, r.PrevCode);
                slaEvents.Add(new SlaEvent
                {
                    CreatedAtUtc = r.CreatedAtUtc,
                    ActionTaken = r.ActionTaken,
                    NewStatusCode = r.NewCode,
                    PreviousStatusCode = r.PrevCode,
                    LoteNumber = ApprovalHistoryClassifier.ParseLoteNumber(r.Comment),
                    ActorUserId = r.ActorUserId,
                    ActorName = r.ActorName,
                    Level = c.Level,
                    Decision = c.Decision,
                    IsApprovalDecision = c.IsApprovalDecision,
                });
                if (c.IsApprovalDecision)
                    allDecisions.Add(new Decision(r.ActorUserId, r.ActorName, c.DecisionCode!, c.LevelCode, r.CreatedAtUtc, r.RequestTypeCode));
            }
            allSamples.AddRange(ApprovalSlaCalculator.ComputeForRequest(slaEvents));
        }

        // Period + approver filter (applied to decisions and samples by their decision timestamp/actor).
        bool InPeriod(DateTime t) =>
            (!filter.DateFrom.HasValue || t >= filter.DateFrom.Value) &&
            (!filter.DateTo.HasValue || t <= filter.DateTo.Value);
        bool ByApprover(Guid actor) => !filter.ApproverId.HasValue || actor == filter.ApproverId.Value;

        var decisions = allDecisions.Where(d => InPeriod(d.AtUtc) && ByApprover(d.ActorUserId)).ToList();
        var samples = allSamples.Where(s => InPeriod(s.DecisionAtUtc) && ByApprover(s.ApproverUserId)).ToList();

        // ── Summary ──
        int total = decisions.Count;
        int approved = decisions.Count(d => d.DecisionCode == "APPROVED");
        int rejected = decisions.Count(d => d.DecisionCode == "REJECTED");
        int returned = decisions.Count(d => d.DecisionCode == "RETURNED");
        int resubmitted = decisions.Count(d => d.DecisionCode == "RESUBMITTED");
        dto.Summary = new ApprovalAnalyticsSummaryDto
        {
            TotalDecisions = total, Approved = approved, Rejected = rejected, Returned = returned, Resubmitted = resubmitted,
            ApprovalRate = total == 0 ? 0 : (double)approved / total,
            RejectionRate = total == 0 ? 0 : (double)rejected / total,
            ReturnRate = total == 0 ? 0 : (double)returned / total,
        };

        // ── Durations ──
        List<long> Secs(StageKind k) => samples.Where(s => s.Stage == k).Select(s => s.Seconds).ToList();
        var areaSecs = Secs(StageKind.Area);
        var finalSecs = Secs(StageKind.Final);
        dto.Duration = ToDto(ApprovalStatistics.Compute(Secs(StageKind.Total)));
        dto.AreaDuration = ToDto(ApprovalStatistics.Compute(areaSecs));
        dto.FinalDuration = ToDto(ApprovalStatistics.Compute(finalSecs));

        double areaAvg = areaSecs.Count == 0 ? 0 : ApprovalStatistics.Mean(areaSecs);
        double finalAvg = finalSecs.Count == 0 ? 0 : ApprovalStatistics.Mean(finalSecs);
        dto.Bottleneck = new ApprovalBottleneckDto
        {
            AreaAverageSeconds = areaAvg, FinalAverageSeconds = finalAvg,
            AreaSampleCount = areaSecs.Count, FinalSampleCount = finalSecs.Count,
            DominantStage = (areaSecs.Count == 0 && finalSecs.Count == 0) ? null
                : areaAvg > finalAvg ? "AREA" : finalAvg > areaAvg ? "FINAL" : null,
        };

        // ── Approvers (stage samples only for speed; §12) ──
        var stageSamples = samples.Where(s => s.Stage != StageKind.Total).ToList();
        dto.Approvers = decisions
            .GroupBy(d => d.ActorUserId)
            .Select(grp =>
            {
                var speed = stageSamples.Where(s => s.ApproverUserId == grp.Key).Select(s => s.Seconds).ToList();
                var stats = ApprovalStatistics.Compute(speed);
                return new ApproverStatsDto
                {
                    ApproverUserId = grp.Key,
                    ApproverName = grp.First().ActorName,
                    DecisionCount = grp.Count(),
                    Approved = grp.Count(d => d.DecisionCode == "APPROVED"),
                    Rejected = grp.Count(d => d.DecisionCode == "REJECTED"),
                    Returned = grp.Count(d => d.DecisionCode == "RETURNED"),
                    SpeedSampleCount = speed.Count,
                    AverageDecisionSeconds = speed.Count == 0 ? null : stats.AverageSeconds,
                    MedianDecisionSeconds = speed.Count == 0 ? null : stats.MedianSeconds,
                    P90DecisionSeconds = speed.Count == 0 ? null : stats.P90Seconds,
                };
            })
            .OrderByDescending(a => a.DecisionCount)
            .ToList();

        // ── Trend ──
        var res = dto.Resolution;
        dto.Trend = decisions
            .GroupBy(d => BucketKey(d.AtUtc, res))
            .OrderBy(grp => grp.Key)
            .Select(grp =>
            {
                var bucketSamples = stageSamples.Where(s => BucketKey(s.DecisionAtUtc, res) == grp.Key).Select(s => s.Seconds).ToList();
                return new ApprovalTrendPointDto
                {
                    Bucket = grp.Key,
                    Decisions = grp.Count(),
                    Approved = grp.Count(d => d.DecisionCode == "APPROVED"),
                    Rejected = grp.Count(d => d.DecisionCode == "REJECTED"),
                    Returned = grp.Count(d => d.DecisionCode == "RETURNED"),
                    AvgDurationSeconds = bucketSamples.Count == 0 ? null : ApprovalStatistics.Mean(bucketSamples),
                };
            })
            .ToList();

        // ── Type split ──
        dto.RequestTypes = new ApprovalTypeSplitDto
        {
            Quotation = decisions.Count(d => d.RequestTypeCode == "QUOTATION"),
            Payment = decisions.Count(d => d.RequestTypeCode == "PAYMENT"),
        };

        return dto;
    }

    private static DurationStatsDto ToDto(DurationStats s) => new()
    {
        SampleCount = s.SampleCount, AverageSeconds = s.AverageSeconds, MedianSeconds = s.MedianSeconds,
        P90Seconds = s.P90Seconds, MinSeconds = s.MinSeconds, MaxSeconds = s.MaxSeconds,
    };

    private static string Normalize(string? r) => r?.ToLowerInvariant() switch { "week" => "week", "month" => "month", _ => "day" };

    private static string BucketKey(DateTime t, string resolution)
    {
        switch (resolution)
        {
            case "month": return t.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            case "week":
                var iso = ISOWeek.GetWeekOfYear(t);
                var year = ISOWeek.GetYear(t);
                return $"{year:D4}-W{iso:D2}";
            default: return t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
