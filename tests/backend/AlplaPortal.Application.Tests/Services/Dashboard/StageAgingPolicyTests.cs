using System;
using System.Linq;
using AlplaPortal.Application.DTOs.Dashboard;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

// Dashboard V2 B9.4 — pure Stage Aging policy: active taxonomy, thresholds, Africa/Luanda calendar age,
// severity classification, and the bottleneck ranking helper.
public class StageAgingPolicyTests
{
    // ── Active taxonomy ──
    [Fact]
    public void Active_taxonomy_excludes_buyer_finpaid_and_terminal_codes()
    {
        var codes = StageAgingCatalog.ActiveStageCodes;
        Assert.Equal(12, codes.Count);
        foreach (var buyer in new[] { PipelineStages.NeedsQuotation, PipelineStages.PartialCoverage, PipelineStages.ReadyForApproval })
            Assert.DoesNotContain(buyer, codes);
        Assert.DoesNotContain(PipelineStages.FinancePaid, codes);
        Assert.DoesNotContain(PipelineStages.Draft, codes);
        Assert.DoesNotContain(PipelineStages.Completed, codes);
        // The expected active set.
        Assert.Contains(PipelineStages.AreaApproval, codes);
        Assert.Contains(PipelineStages.ReceivingReady, codes);
        Assert.Contains(PipelineStages.Documentation, codes);
    }

    [Fact]
    public void Thresholds_are_closed_policy_and_labels_reuse_b6()
    {
        StageAgingStageMeta M(string code) => StageAgingCatalog.Meta(code)!;
        Assert.Equal(3, M(PipelineStages.AreaApproval).Threshold!.AttentionAfterDays);
        Assert.Equal(7, M(PipelineStages.AreaApproval).Threshold!.CriticalAfterDays);
        Assert.Equal(3, M(PipelineStages.PoWaiting).Threshold!.AttentionAfterDays);
        Assert.Equal(7, M(PipelineStages.PoWaiting).Threshold!.CriticalAfterDays);
        Assert.Equal(7, M(PipelineStages.ReceivingWaiting).Threshold!.AttentionAfterDays);
        Assert.Equal(14, M(PipelineStages.ReceivingWaiting).Threshold!.CriticalAfterDays);
        // Finance + Documentation carry no severity threshold.
        Assert.Null(M(PipelineStages.FinanceNeedsScheduling).Threshold);
        Assert.Null(M(PipelineStages.FinanceScheduled).Threshold);
        Assert.Null(M(PipelineStages.Documentation).Threshold);
        // Never a formal SLA.
        Assert.False(M(PipelineStages.AreaApproval).Threshold!.IsFormalSla);
        // Labels.
        Assert.Equal("Aprovação de Área", M(PipelineStages.AreaApproval).Label);
        Assert.Equal("Entrada em recebimento", M(PipelineStages.ReceivingReady).Label);
        Assert.Equal("Documentação fiscal", M(PipelineStages.Documentation).Label);
    }

    // ── Luanda calendar age (UTC+1, no DST) ──
    [Theory]
    // entered 23:30 UTC (= 00:30 Luanda next day); "today" Luanda same day → age 0
    [InlineData("2026-09-04T23:30:00Z", "2026-09-05T12:00:00Z", 0)]
    // entered 22:30 UTC (Luanda 23:30 same day); now 23:30 UTC (Luanda next-day 00:30) → 1 day boundary
    [InlineData("2026-09-05T22:30:00Z", "2026-09-05T23:30:00Z", 1)]
    // entered 23:30 UTC (Luanda next day 00:30); now same instant → 0
    [InlineData("2026-09-05T23:30:00Z", "2026-09-05T23:30:00Z", 0)]
    [InlineData("2026-09-01T10:00:00Z", "2026-09-05T10:00:00Z", 4)]
    public void Age_days_use_luanda_calendar(string entered, string now, int expected)
    {
        var e = DateTime.Parse(entered).ToUniversalTime();
        var n = DateTime.Parse(now).ToUniversalTime();
        Assert.Equal(expected, StageAgingPolicy.AgeDays(e, n));
    }

    [Fact]
    public void Future_or_corrupt_timestamp_clamps_to_zero_never_negative()
    {
        var now = new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc);
        var future = now.AddDays(3);
        Assert.Equal(0, StageAgingPolicy.AgeDays(future, now));
    }

    // ── Severity classification boundaries ──
    [Theory]
    [InlineData(0, StageAgingSeverity.Normal)]
    [InlineData(3, StageAgingSeverity.Normal)]
    [InlineData(4, StageAgingSeverity.Attention)]
    [InlineData(7, StageAgingSeverity.Attention)]
    [InlineData(8, StageAgingSeverity.Critical)]
    public void Approval_and_po_thresholds_3_7(int age, StageAgingSeverity expected)
        => Assert.Equal(expected, StageAgingPolicy.Classify(age, StageAgingCatalog.Meta(PipelineStages.AreaApproval)!.Threshold!));

    [Theory]
    [InlineData(0, StageAgingSeverity.Normal)]
    [InlineData(7, StageAgingSeverity.Normal)]
    [InlineData(8, StageAgingSeverity.Attention)]
    [InlineData(14, StageAgingSeverity.Attention)]
    [InlineData(15, StageAgingSeverity.Critical)]
    public void Receiving_thresholds_7_14(int age, StageAgingSeverity expected)
        => Assert.Equal(expected, StageAgingPolicy.Classify(age, StageAgingCatalog.Meta(PipelineStages.ReceivingWaiting)!.Threshold!));

    // ── Bottleneck ranking helper (not applied to canonical API order) ──
    [Fact]
    public void Ranking_is_critical_then_attention_then_oldest_then_sort_order()
    {
        var a = new DashboardV2StageAgingStageDto { StageCode = "A", SortOrder = 60, CriticalCount = 0, AttentionCount = 5, OldestAgeDays = 10 };
        var b = new DashboardV2StageAgingStageDto { StageCode = "B", SortOrder = 30, CriticalCount = 2, AttentionCount = 0, OldestAgeDays = 3 };
        var c = new DashboardV2StageAgingStageDto { StageCode = "C", SortOrder = 50, CriticalCount = 2, AttentionCount = 9, OldestAgeDays = 1 };
        var ranked = StageAgingPolicy.RankByBottleneck(new[] { a, b, c });
        Assert.Equal(new[] { "C", "B", "A" }, ranked.Select(s => s.StageCode).ToArray()); // C,B critical>A; C attention>B
    }
}
