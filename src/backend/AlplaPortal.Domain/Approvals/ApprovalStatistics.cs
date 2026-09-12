namespace AlplaPortal.Domain.Approvals;

/// <summary>
/// v2.244.0 Approval Center V2 — Phase 3. Canonical, server-side duration statistics. All analytics
/// numbers come from here so the frontend never recomputes a metric.
///
/// P90 uses the NEAREST-RANK method: sort ascending, rank = ceil(0.90 · n), take the value at that
/// 1-based rank (clamped). It needs no interpolation, is stable for tiny samples, and for n=1 returns
/// the single value. Mean is the arithmetic average; Median is the middle value (average of the two
/// middle values for an even count). Empty input yields a zeroed result with Count = 0.
/// </summary>
public static class ApprovalStatistics
{
    public static double Mean(IReadOnlyList<long> xs)
    {
        if (xs.Count == 0) return 0;
        double sum = 0;
        foreach (var x in xs) sum += x;
        return sum / xs.Count;
    }

    public static double Median(IReadOnlyList<long> xs)
    {
        if (xs.Count == 0) return 0;
        var s = xs.OrderBy(x => x).ToArray();
        int n = s.Length;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0;
    }

    public static long Percentile(IReadOnlyList<long> xs, double p)
    {
        if (xs.Count == 0) return 0;
        var s = xs.OrderBy(x => x).ToArray();
        int rank = (int)Math.Ceiling(p * s.Length);      // nearest-rank, 1-based
        if (rank < 1) rank = 1;
        if (rank > s.Length) rank = s.Length;
        return s[rank - 1];
    }

    public static DurationStats Compute(IReadOnlyList<long> secondsSamples)
    {
        if (secondsSamples.Count == 0) return DurationStats.Empty;
        return new DurationStats
        {
            SampleCount = secondsSamples.Count,
            AverageSeconds = Mean(secondsSamples),
            MedianSeconds = Median(secondsSamples),
            P90Seconds = Percentile(secondsSamples, 0.90),
            MinSeconds = secondsSamples.Min(),
            MaxSeconds = secondsSamples.Max(),
        };
    }
}

public sealed class DurationStats
{
    public int SampleCount { get; set; }
    public double AverageSeconds { get; set; }
    public double MedianSeconds { get; set; }
    public long P90Seconds { get; set; }
    public long MinSeconds { get; set; }
    public long MaxSeconds { get; set; }

    public static DurationStats Empty => new();
}
