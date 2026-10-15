namespace AlplaPortal.Domain.Entities;

/// <summary>
/// One execution of the daily approval-reminder digest cycle (real or dry run). Written first, so
/// an aborted cycle is visible; counters are filled at the end.
/// </summary>
public class ApprovalReminderRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    /// <summary>Luanda calendar date the run belongs to (the dedup day).</summary>
    public DateTime LocalDate { get; set; }
    public bool DryRun { get; set; }
    /// <summary>MANUAL_PREVIEW runs come from the admin preview endpoint and never persist digests.</summary>
    public string Trigger { get; set; } = "SCHEDULED";
    public int UnitsConsidered { get; set; }
    public int UnitsEligible { get; set; }
    public int UnitsWithoutStageEntry { get; set; }
    public int UnitsWithoutRecipient { get; set; }
    public int Recipients { get; set; }
    public int DigestsQueued { get; set; }
    public int DigestsDryRun { get; set; }
    public int SkippedDedup { get; set; }
    /// <summary>Recipients excluded by the optional RecipientAllowList (bounded first live day).</summary>
    public int SkippedAllowList { get; set; }
    public int Failed { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// One consolidated digest for one approver on one Luanda business day. The unique key
/// (RecipientUserId, DigestDateLocal) is the persistent, multi-instance dedup: whoever inserts first
/// owns the day; the loser gets a unique-key violation and queues nothing. The digest row, its items
/// and the EmailOutbox row are written in ONE SaveChanges.
/// </summary>
public class ApprovalReminderDigest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public ApprovalReminderRun Run { get; set; } = null!;
    public Guid RecipientUserId { get; set; }
    public User RecipientUser { get; set; } = null!;
    public DateTime DigestDateLocal { get; set; }
    public int ItemCount { get; set; }
    /// <summary>Items beyond MaxItemsPerDigest summarised as "+N" (0 when everything was listed).</summary>
    public int OverflowCount { get; set; }
    public bool DryRun { get; set; }
    public string Subject { get; set; } = string.Empty;
    /// <summary>Rendered body as queued (dry runs keep it for review). Contents reflect queue time.</summary>
    public string PayloadHtml { get; set; } = string.Empty;
    public Guid? OutboxEntryId { get; set; }
    public EmailOutboxEntry? OutboxEntry { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<ApprovalReminderDigestItem> Items { get; set; } = new List<ApprovalReminderDigestItem>();
}

/// <summary>Audit of exactly which approval unit a digest listed, and the age it showed.</summary>
public class ApprovalReminderDigestItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DigestId { get; set; }
    public ApprovalReminderDigest Digest { get; set; } = null!;
    public Guid RequestId { get; set; }
    public Guid? ApprovalBatchId { get; set; }
    public int? BatchNumber { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    /// <summary>AREA or FINAL.</summary>
    public string Stage { get; set; } = string.Empty;
    public DateTime StageEnteredAtUtc { get; set; }
    public int DaysPending { get; set; }
}
