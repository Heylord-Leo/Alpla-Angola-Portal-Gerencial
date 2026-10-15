using System;
using AlplaPortal.Infrastructure.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services;

/// <summary>
/// ProformaDeadlineAlertService scheduling arithmetic. The previous implementation slept 30 s and
/// then every 24 h from process start, ignoring CheckTimeUtcHour entirely. These tests pin the
/// corrected behaviour on startup, restart (catch-up), cancellation-neutral delays and the
/// drift-free next anchor.
/// </summary>
public class ProformaDeadlineScheduleTests
{
    private static readonly DateTime Day = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

    // ── startup before today's anchor ──
    [Fact]
    public void Startup_before_anchor_waits_until_todays_anchor()
    {
        var now = Day.AddHours(5).AddMinutes(30);                   // 05:30
        var delay = ProformaDeadlineSchedule.ComputeInitialDelay(now, 7, lastCycleUtc: null);
        Assert.Equal(TimeSpan.FromMinutes(90), delay);              // → 07:00 today
    }

    // ── restart after the anchor with no cycle today → catch up immediately ──
    [Fact]
    public void Startup_after_anchor_with_no_cycle_today_runs_immediately()
    {
        var now = Day.AddHours(13);                                 // 13:00
        Assert.Equal(TimeSpan.Zero, ProformaDeadlineSchedule.ComputeInitialDelay(now, 7, lastCycleUtc: null));
        Assert.Equal(TimeSpan.Zero, ProformaDeadlineSchedule.ComputeInitialDelay(now, 7, lastCycleUtc: Day.AddDays(-1).AddHours(7)));
    }

    // ── restart after the anchor when today's cycle already completed → wait for tomorrow ──
    [Fact]
    public void Startup_after_anchor_with_cycle_already_done_today_waits_for_tomorrow()
    {
        var now = Day.AddHours(13);
        var delay = ProformaDeadlineSchedule.ComputeInitialDelay(now, 7, lastCycleUtc: Day.AddHours(7).AddMinutes(2));
        Assert.Equal(TimeSpan.FromHours(18), delay);                // → 07:00 tomorrow
    }

    // ── a cycle recorded today but BEFORE the anchor (e.g. an old-style start-up run) does not count ──
    [Fact]
    public void Cycle_recorded_today_before_the_anchor_does_not_count_as_todays_run()
    {
        var now = Day.AddHours(9);
        var delay = ProformaDeadlineSchedule.ComputeInitialDelay(now, 7, lastCycleUtc: Day.AddHours(1));
        Assert.Equal(TimeSpan.Zero, delay);
    }

    // ── subsequent cycles: next anchor strictly after now ──
    [Theory]
    [InlineData(6, 0, 1.0)]     // 06:00 → 07:00 same day
    [InlineData(7, 0, 24.0)]    // exactly at the anchor → tomorrow (strictly after)
    [InlineData(7, 1, 23.983)]  // just after the anchor (cycle just ran) → tomorrow
    [InlineData(23, 30, 7.5)]   // late evening → 07:00 tomorrow
    public void Next_delay_is_the_next_anchor_strictly_after_now(int hour, int minute, double expectedHours)
    {
        var now = Day.AddHours(hour).AddMinutes(minute);
        var delay = ProformaDeadlineSchedule.ComputeNextDelay(now, 7);
        Assert.Equal(expectedHours, delay.TotalHours, 2);
    }

    [Fact]
    public void Anchor_hour_is_clamped_to_a_valid_range()
    {
        var now = Day.AddHours(12);
        Assert.Equal(TimeSpan.FromHours(11), ProformaDeadlineSchedule.ComputeNextDelay(now, 99));  // 23:00 today
        Assert.Equal(TimeSpan.FromHours(12), ProformaDeadlineSchedule.ComputeNextDelay(now, -5));  // 00:00 tomorrow
    }
}
