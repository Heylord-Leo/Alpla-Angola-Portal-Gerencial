using System;
using AlplaPortal.Infrastructure.Services.Reminders;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Reminders;

/// <summary>
/// Change 4 — digest scheduling: 08:00 Africa/Luanda (UTC+1, no DST), Monday–Friday, with catch-up
/// after a restart only when today's send time passed without a recorded run. Age is in Luanda
/// calendar days. 2026-10-07 is a Wednesday.
/// </summary>
public class ApprovalReminderScheduleTests
{
    private static readonly ApprovalReminderOptions Opts = new(); // defaults: 08:00, Mon–Fri
    private static readonly TimeZoneInfo Zone = ApprovalReminderSchedule.ResolveZone(Opts.TimeZoneId);

    [Fact]
    public void Zone_resolves_to_utc_plus_one_and_falls_back_for_unknown_ids()
    {
        Assert.Equal(TimeSpan.FromHours(1), Zone.BaseUtcOffset);
        var fallback = ApprovalReminderSchedule.ResolveZone("No/Such_Zone");
        Assert.Equal(TimeSpan.FromHours(1), fallback.BaseUtcOffset);
        Assert.False(fallback.SupportsDaylightSavingTime);
    }

    [Fact]
    public void Send_time_parses_or_defaults_to_eight()
    {
        Assert.Equal(new TimeSpan(8, 0, 0), ApprovalReminderSchedule.ParseSendTime("08:00"));
        Assert.Equal(new TimeSpan(14, 30, 0), ApprovalReminderSchedule.ParseSendTime("14:30"));
        Assert.Equal(new TimeSpan(8, 0, 0), ApprovalReminderSchedule.ParseSendTime("garbage"));
        Assert.Equal(new TimeSpan(8, 0, 0), ApprovalReminderSchedule.ParseSendTime(null));
    }

    [Fact]
    public void Before_send_time_on_a_business_day_waits_until_0700_utc_today()
    {
        var now = new DateTime(2026, 10, 7, 6, 30, 0, DateTimeKind.Utc); // 07:30 Luanda, Wednesday
        Assert.Equal(new DateTime(2026, 10, 7, 7, 0, 0, DateTimeKind.Utc), ApprovalReminderSchedule.ComputeNextRunUtc(now, Opts, Zone));
        Assert.Equal(TimeSpan.FromMinutes(30), ApprovalReminderSchedule.ComputeInitialDelay(now, Opts, Zone, lastRunLocalDate: null));
    }

    [Fact]
    public void Restart_after_send_time_without_a_run_today_catches_up_immediately()
    {
        var now = new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc); // 09:00 Luanda
        Assert.Equal(TimeSpan.Zero, ApprovalReminderSchedule.ComputeInitialDelay(now, Opts, Zone, lastRunLocalDate: null));
        Assert.Equal(TimeSpan.Zero, ApprovalReminderSchedule.ComputeInitialDelay(now, Opts, Zone, lastRunLocalDate: new DateTime(2026, 10, 6)));
    }

    [Fact]
    public void Restart_after_send_time_with_a_run_today_waits_for_tomorrow()
    {
        var now = new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
        var delay = ApprovalReminderSchedule.ComputeInitialDelay(now, Opts, Zone, lastRunLocalDate: new DateTime(2026, 10, 7));
        Assert.Equal(new DateTime(2026, 10, 8, 7, 0, 0, DateTimeKind.Utc), now + delay);
    }

    [Fact]
    public void Friday_after_send_rolls_to_monday_and_weekends_never_catch_up()
    {
        var friday = new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc), ApprovalReminderSchedule.ComputeNextRunUtc(friday, Opts, Zone));

        var saturday = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var delay = ApprovalReminderSchedule.ComputeInitialDelay(saturday, Opts, Zone, lastRunLocalDate: null);
        Assert.NotEqual(TimeSpan.Zero, delay);
        Assert.Equal(new DateTime(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc), saturday + delay);
    }

    [Fact]
    public void Exactly_at_send_time_the_next_run_is_tomorrow_not_now()
    {
        var now = new DateTime(2026, 10, 7, 7, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 10, 8, 7, 0, 0, DateTimeKind.Utc), ApprovalReminderSchedule.ComputeNextRunUtc(now, Opts, Zone));
    }

    [Fact]
    public void Days_pending_counts_luanda_calendar_days_not_utc_days()
    {
        // 23:30 UTC on the 3rd is already 00:30 on the 4th in Luanda → 3 days until the 7th (UTC arithmetic would say 4).
        var entered = new DateTime(2026, 10, 3, 23, 30, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 10, 7, 6, 0, 0, DateTimeKind.Utc);
        Assert.Equal(3, ApprovalReminderSchedule.DaysPending(entered, now, Zone));
        Assert.Equal(0, ApprovalReminderSchedule.DaysPending(now, now, Zone));
        Assert.Equal(0, ApprovalReminderSchedule.DaysPending(now.AddHours(1), now, Zone)); // clock skew never goes negative
    }
}
