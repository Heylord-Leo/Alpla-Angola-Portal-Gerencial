using AlplaPortal.Domain.Approvals;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

// v2.244.0 Phase 3 — canonical statistics (§20 K/L/M/N). P90 = nearest-rank.
public class ApprovalStatisticsTests
{
    [Fact]
    public void Mean_Basic() => Assert.Equal(2.0, ApprovalStatistics.Mean(new long[] { 1, 2, 3 }));

    [Fact]
    public void Median_Odd() => Assert.Equal(2.0, ApprovalStatistics.Median(new long[] { 3, 1, 2 }));

    [Fact]
    public void Median_Even() => Assert.Equal(2.5, ApprovalStatistics.Median(new long[] { 1, 2, 3, 4 }));

    [Fact]
    public void P90_NearestRank_TenValues() // ceil(0.9*10)=9 → 9th value
        => Assert.Equal(9, ApprovalStatistics.Percentile(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 0.90));

    [Fact]
    public void P90_FiveValues() // ceil(0.9*5)=5 → max
        => Assert.Equal(5, ApprovalStatistics.Percentile(new long[] { 5, 4, 3, 2, 1 }, 0.90));

    [Fact]
    public void P90_SingleSample_ReturnsThatValue()
        => Assert.Equal(7, ApprovalStatistics.Percentile(new long[] { 7 }, 0.90));

    [Fact]
    public void Empty_YieldsZeroedStats()
    {
        var s = ApprovalStatistics.Compute(System.Array.Empty<long>());
        Assert.Equal(0, s.SampleCount);
        Assert.Equal(0, s.AverageSeconds);
        Assert.Equal(0, s.P90Seconds);
    }

    [Fact]
    public void Outliers_MedianResistant_MeanNot()
    {
        var xs = new long[] { 1, 1, 1, 1, 100 };
        var s = ApprovalStatistics.Compute(xs);
        Assert.Equal(5, s.SampleCount);
        Assert.Equal(1, s.MedianSeconds);          // resistant
        Assert.Equal(20.8, s.AverageSeconds, 3);   // pulled up
        Assert.Equal(100, s.P90Seconds);           // ceil(4.5)=5 → max
        Assert.Equal(1, s.MinSeconds);
        Assert.Equal(100, s.MaxSeconds);
    }
}
