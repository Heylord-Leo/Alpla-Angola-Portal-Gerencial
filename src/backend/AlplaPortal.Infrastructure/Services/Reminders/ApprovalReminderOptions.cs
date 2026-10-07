namespace AlplaPortal.Infrastructure.Services.Reminders;

/// <summary>
/// Configuration for the daily pending-approval reminder digest (section <c>AppConfig:ApprovalReminders</c>).
/// Defaults are the SAFE state: disabled, and dry-run when enabled.
/// </summary>
public sealed class ApprovalReminderOptions
{
    public const string SectionName = "AppConfig:ApprovalReminders";

    /// <summary>Master switch for the hosted service. Default false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>When true the full pipeline runs and digests are recorded, but NO outbox row is created. Default true.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Local send time, HH:mm, in <see cref="TimeZoneId"/>. Default 08:00.</summary>
    public string SendTimeLocal { get; set; } = "08:00";

    /// <summary>Windows time zone id for Angola (UTC+1, no DST). Falls back to a fixed +01:00 offset when the id is unknown on the host.</summary>
    public string TimeZoneId { get; set; } = "W. Central Africa Standard Time";

    /// <summary>Units are included when they have waited MORE than this many calendar days in their current stage. Default 3.</summary>
    public int MinPendingAgeDays { get; set; } = 3;

    /// <summary>Business days (Luanda). Default Monday–Friday.</summary>
    public DayOfWeek[] BusinessDays { get; set; } = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

    /// <summary>Oldest N units listed in full; the rest summarised as "+N" with a link. Default 50.</summary>
    public int MaxItemsPerDigest { get; set; } = 50;

    /// <summary>Rendered-body cap; above it the digest degrades to counts + link. Default 256 KB.</summary>
    public int MaxBodyBytes { get; set; } = 256 * 1024;

    /// <summary>Outbox freshness limit for a digest (hours). Past it the processor marks the row EXPIRED. Default 4.</summary>
    public int DigestExpiryHours { get; set; } = 4;

    /// <summary>Optional allow-list of recipient e-mails for a bounded first live day. Empty = everyone.</summary>
    public string[] RecipientAllowList { get; set; } = Array.Empty<string>();

    /// <summary>Approvals-center link appended to every digest.</summary>
    public string ApprovalsCenterPath { get; set; } = "/approvals";
}
