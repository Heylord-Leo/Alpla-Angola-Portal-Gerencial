using System;
using System.Globalization;

namespace AlplaPortal.Domain.Common;

/// <summary>
/// The application's established display timezone: Africa/Luanda (WAT, UTC+1, no DST) — the same convention the
/// Dashboard stage-aging policy uses for calendar days. Server time is always stored and transported in UTC; this
/// helper only renders it for people, and always with an explicit timezone label.
/// </summary>
public static class AppTimeZone
{
    public const string Label = "Luanda, WAT UTC+1";
    public static readonly TimeSpan Offset = TimeSpan.FromHours(1);

    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(Offset);

    /// <summary>"dd/MM/yyyy HH:mm (Luanda, WAT UTC+1) — HH:mm UTC".</summary>
    public static string FormatWithUtc(DateTime utc)
    {
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return $"{ToLocal(u).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} ({Label}) — {u.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC";
    }
}
