namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Pure scheduling arithmetic for <see cref="ProformaDeadlineAlertService"/>: when the next
/// daily cycle must run, given the configured UTC hour. Kept free of I/O so the behaviour on
/// startup, restart and between cycles can be unit-tested.
///
/// <para><b>Rules</b></para>
/// <list type="bullet">
///   <item><b>Daily anchor:</b> a cycle is due once per UTC calendar day, at <c>checkHourUtc</c>.</item>
///   <item><b>Startup / restart catch-up:</b> if today's anchor has already passed and no cycle has
///   completed today (per the last recorded cycle), the first run happens immediately after the
///   warm-up delay. Otherwise the service waits for the next anchor. This matters because the API
///   runs inside an IIS worker process that can be recycled or idle-stopped, so the process is not
///   guaranteed to be alive at the anchor hour.</item>
///   <item><b>Subsequent cycles:</b> always the next anchor strictly after "now" — no fixed 24 h
///   sleep, so the time never drifts with process restarts.</item>
/// </list>
/// </summary>
public static class ProformaDeadlineSchedule
{
    /// <summary>Delay before the FIRST cycle after the service starts.</summary>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <param name="checkHourUtc">Configured anchor hour (0-23). Out-of-range values are clamped.</param>
    /// <param name="lastCycleUtc">UTC time of the last completed cycle, or null when none is recorded.</param>
    public static TimeSpan ComputeInitialDelay(DateTime nowUtc, int checkHourUtc, DateTime? lastCycleUtc)
    {
        var hour = Clamp(checkHourUtc);
        var todayAnchor = nowUtc.Date.AddHours(hour);

        var anchorPassedToday = nowUtc >= todayAnchor;
        var ranToday = lastCycleUtc.HasValue && lastCycleUtc.Value.Date == nowUtc.Date && lastCycleUtc.Value >= todayAnchor;

        if (anchorPassedToday && !ranToday)
            return TimeSpan.Zero; // catch-up: the process was not alive at the anchor

        return ComputeNextDelay(nowUtc, hour);
    }

    /// <summary>Delay until the next anchor strictly after <paramref name="nowUtc"/>.</summary>
    public static TimeSpan ComputeNextDelay(DateTime nowUtc, int checkHourUtc)
    {
        var hour = Clamp(checkHourUtc);
        var next = nowUtc.Date.AddHours(hour);
        if (next <= nowUtc) next = next.AddDays(1);
        return next - nowUtc;
    }

    private static int Clamp(int hour) => hour < 0 ? 0 : hour > 23 ? 23 : hour;
}
