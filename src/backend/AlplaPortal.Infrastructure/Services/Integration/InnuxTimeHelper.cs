namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Shared utility for converting Innux datetime-as-duration values to practical forms.
///
/// Innux stores durations and time-of-day as datetime objects with a base date
/// of 1900-01-01. For example:
///   1900-01-01T08:00:00 → 8 hours = 480 minutes
///   1900-01-01T00:30:00 → 30 minutes
///   1900-01-01T00:00:00 → 0 minutes (midnight / no duration)
///
/// These helpers centralise the conversion logic so that all Innux attendance
/// services apply the same rules consistently.
///
/// Known limitation: Negative balance encoding in Innux is unconfirmed.
/// The Saldo column may use a convention (e.g. date rollover, negative flag)
/// that is not yet documented. ToMinutes() will return 0 for values at or
/// before the base date, which may mask true negative balances. This is
/// documented and tracked as a follow-up validation item.
/// </summary>
internal static class InnuxTimeHelper
{
    private static readonly DateTime BaseDate = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>
    /// Converts an Innux datetime-as-duration value to total minutes.
    /// Returns 0 for null, DBNull, or values at/before the base date.
    /// </summary>
    public static int ToMinutes(object? value)
    {
        if (value is DateTime dt && dt > BaseDate)
            return (int)(dt - BaseDate).TotalMinutes;
        return 0;
    }

    /// <summary>
    /// Converts an Innux time-part datetime to an "HH:mm" string.
    /// Returns null for null, DBNull, or base-date values (00:00).
    /// </summary>
    public static string? ToTimeString(object? value)
    {
        if (value is DateTime dt && dt > BaseDate)
            return dt.ToString("HH:mm");
        return null;
    }

    /// <summary>
    /// Converts an Innux time-part datetime to an "HH:mm:ss" string.
    /// Used for raw punch time display (higher precision than ToTimeString).
    /// Returns empty string for null/DBNull.
    /// </summary>
    public static string ToTimeStringFull(object? value)
    {
        if (value is DateTime dt && dt > BaseDate)
            return dt.ToString("HH:mm:ss");
        return "";
    }

    /// <summary>
    /// Safely reads a nullable int from a SqlDataReader column.
    /// Returns 0 if the value is null or DBNull.
    /// </summary>
    public static int SafeInt(object? value)
    {
        if (value is int i) return i;
        if (value is short s) return s;
        if (value is long l) return (int)l;
        if (value is decimal d) return (int)d;
        return 0;
    }

    /// <summary>
    /// Safely reads a nullable bool from a SqlDataReader column.
    /// Returns false if the value is null, DBNull, or non-boolean.
    /// </summary>
    public static bool SafeBool(object? value)
    {
        if (value is bool b) return b;
        if (value is int i) return i != 0;
        if (value is short s) return s != 0;
        return false;
    }
}
