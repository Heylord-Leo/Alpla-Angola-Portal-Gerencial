using System.Globalization;

namespace AlplaPortal.Infrastructure.Services.Reminders;

/// <summary>
/// Pure scheduling arithmetic for the daily digest: one send per Luanda business day at the
/// configured local time. Catch-up on startup/restart when today's send time passed without a run.
/// </summary>
public static class ApprovalReminderSchedule
{
    private static readonly TimeSpan LuandaFallbackOffset = TimeSpan.FromHours(1); // WAT, no DST

    /// <summary>Resolves the configured zone; unknown ids fall back to a fixed +01:00 zone (Angola has no DST).</summary>
    public static TimeZoneInfo ResolveZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Africa/Luanda (fixed)", LuandaFallbackOffset, "Africa/Luanda", "West Africa Time");
    }

    public static TimeSpan ParseSendTime(string? hhmm)
    {
        if (TimeSpan.TryParseExact(hhmm ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out var t)) return t;
        return new TimeSpan(8, 0, 0);
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);

    public static bool IsBusinessDay(DateTime localDate, IReadOnlyCollection<DayOfWeek> businessDays) =>
        businessDays.Contains(localDate.DayOfWeek);

    /// <summary>Next send instant (UTC) strictly after <paramref name="nowUtc"/> on a business day.</summary>
    public static DateTime ComputeNextRunUtc(DateTime nowUtc, ApprovalReminderOptions options, TimeZoneInfo zone)
    {
        var sendTime = ParseSendTime(options.SendTimeLocal);
        var nowLocal = ToLocal(nowUtc, zone);
        var candidate = nowLocal.Date + sendTime;
        for (var i = 0; i < 14; i++)
        {
            if (candidate > nowLocal && IsBusinessDay(candidate.Date, options.BusinessDays))
                return ToUtc(candidate, zone);
            candidate = candidate.Date.AddDays(1) + sendTime;
        }
        return ToUtc(candidate, zone); // unreachable with a non-empty business-day set; defensive
    }

    /// <summary>
    /// Delay before the first cycle after start. Zero (catch-up) when today is a business day, the
    /// send time already passed, and no run is recorded for today's local date.
    /// </summary>
    public static TimeSpan ComputeInitialDelay(DateTime nowUtc, ApprovalReminderOptions options, TimeZoneInfo zone, DateTime? lastRunLocalDate)
    {
        var nowLocal = ToLocal(nowUtc, zone);
        var todayAnchor = nowLocal.Date + ParseSendTime(options.SendTimeLocal);
        var ranToday = lastRunLocalDate.HasValue && lastRunLocalDate.Value.Date == nowLocal.Date;
        if (IsBusinessDay(nowLocal.Date, options.BusinessDays) && nowLocal >= todayAnchor && !ranToday)
            return TimeSpan.Zero;
        var next = ComputeNextRunUtc(nowUtc, options, zone);
        return next - nowUtc;
    }

    /// <summary>Calendar-day age in the zone: (local date now) − (local date of the stage entry).</summary>
    public static int DaysPending(DateTime stageEnteredUtc, DateTime nowUtc, TimeZoneInfo zone) =>
        Math.Max(0, (ToLocal(nowUtc, zone).Date - ToLocal(stageEnteredUtc, zone).Date).Days);
}
