using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Infrastructure.Services.Reminders;

/// <summary>Outcome of one digest cycle (persisted on <see cref="ApprovalReminderRun"/> too).</summary>
public sealed record DigestCycleResult(ApprovalReminderRun Run, IReadOnlyList<ApprovalReminderDigest> Digests);

/// <summary>
/// Builds and queues the daily pending-approval digests.
///
/// <para><b>Dedup / atomicity:</b> for each recipient the digest row, its items and (live mode only)
/// the EmailOutbox row are added to the context and committed in ONE SaveChanges. The unique index
/// (RecipientUserId, DigestDateLocal, DryRun) is the persistent dedup: a second instance or a restart
/// on the same day gets a unique-key violation and queues nothing (counted as SkippedDedup). A
/// cheap pre-check skips already-recorded recipients without hitting the index.</para>
///
/// <para><b>Dry run:</b> the whole pipeline runs and the digest (subject + rendered body + items) is
/// stored for review, but NO outbox row is created, so nothing can be sent.</para>
///
/// <para><b>Preview:</b> <see cref="PreviewAsync"/> computes the same digests in memory and persists
/// nothing (Trigger MANUAL_PREVIEW).</para>
///
/// <para>"Queued" means an outbox row exists; delivery is read from that row's Status.</para>
/// </summary>
public sealed class ApprovalReminderDigestCycle
{
    public const string EventCode = "APPROVAL_REMINDER_DIGEST";
    public const string TriggerScheduled = "SCHEDULED";
    public const string TriggerManualPreview = "MANUAL_PREVIEW";
    private const int TransientRetries = 3;

    private readonly ApplicationDbContext _context;
    private readonly IApprovalRoutingService _routing;
    private readonly ApprovalReminderOptions _options;
    private readonly IConfiguration _config;
    private readonly AdminLogWriter _adminLog;
    private readonly ILogger<ApprovalReminderDigestCycle> _logger;

    public ApprovalReminderDigestCycle(
        ApplicationDbContext context,
        IApprovalRoutingService routing,
        IOptions<ApprovalReminderOptions> options,
        IConfiguration config,
        AdminLogWriter adminLog,
        ILogger<ApprovalReminderDigestCycle> logger)
    {
        _context = context;
        _routing = routing;
        _options = options.Value;
        _config = config;
        _adminLog = adminLog;
        _logger = logger;
    }

    /// <summary>Scheduled run: persists a run row first, then digests per recipient.</summary>
    public Task<DigestCycleResult> RunAsync(DateTime nowUtc, CancellationToken ct) =>
        ExecuteAsync(nowUtc, _options.DryRun, persist: true, TriggerScheduled, ct);

    /// <summary>Admin preview: computes digests in memory; nothing is persisted or queued.</summary>
    public Task<DigestCycleResult> PreviewAsync(DateTime nowUtc, CancellationToken ct) =>
        ExecuteAsync(nowUtc, dryRun: true, persist: false, TriggerManualPreview, ct);

    private async Task<DigestCycleResult> ExecuteAsync(DateTime nowUtc, bool dryRun, bool persist, string trigger, CancellationToken ct)
    {
        var zone = ApprovalReminderSchedule.ResolveZone(_options.TimeZoneId);
        var localDate = ApprovalReminderSchedule.ToLocal(nowUtc, zone).Date;
        var frontendBaseUrl = _config["AppConfig:FrontendBaseUrl"] ?? "https://portal.alpla.com";
        var allowList = new HashSet<string>((_options.RecipientAllowList ?? Array.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()), StringComparer.OrdinalIgnoreCase);

        var run = new ApprovalReminderRun
        {
            StartedAtUtc = nowUtc,
            LocalDate = localDate,
            DryRun = dryRun,
            Trigger = trigger
        };
        if (persist)
        {
            _context.ApprovalReminderRuns.Add(run);
            await _context.SaveChangesAsync(ct);
        }

        var produced = new List<ApprovalReminderDigest>();
        try
        {
            var query = new PendingApprovalUnitQuery(_context, _routing);
            var units = await query.RunAsync(nowUtc, _options.MinPendingAgeDays, zone, ct);

            run.UnitsConsidered = units.Considered.Count;
            run.UnitsEligible = units.Eligible.Count;
            run.UnitsWithoutStageEntry = units.WithoutStageEntry.Count;
            run.UnitsWithoutRecipient = units.WithoutRecipient.Count;
            run.Recipients = units.ByRecipient.Count;

            foreach (var u in units.WithoutStageEntry)
                _logger.LogWarning("[ApprovalReminders] Unit {Request} {Stage} batch={Batch} has no established stage entry; excluded from digests.", u.RequestNumber, u.Stage, u.BatchNumber);
            if (persist && units.WithoutRecipient.Count > 0)
                await _adminLog.WriteAsync("Warning", "Notification", "APPROVAL_REMINDER_NO_RECIPIENT",
                    $"{units.WithoutRecipient.Count} eligible approval unit(s) have no resolvable approver: " +
                    string.Join(", ", units.WithoutRecipient.Take(20).Select(u => $"{u.RequestNumber}/{u.Stage}" + (u.BatchNumber.HasValue ? $"#{u.BatchNumber}" : ""))));

            // Already recorded today (restart / overlapping instance). Pre-check only; the unique index is the guard.
            var alreadyRecorded = persist
                ? await _context.ApprovalReminderDigests.AsNoTracking()
                    .Where(d => d.DigestDateLocal == localDate && d.DryRun == dryRun)
                    .Select(d => d.RecipientUserId)
                    .ToListAsync(ct)
                : new List<Guid>();
            var alreadySet = alreadyRecorded.ToHashSet();

            foreach (var (userId, (recipient, recipientUnits)) in units.ByRecipient.OrderBy(kv => kv.Value.Recipient.Email))
            {
                ct.ThrowIfCancellationRequested();

                if (allowList.Count > 0 && !allowList.Contains(recipient.Email))
                {
                    run.SkippedAllowList++;
                    continue;
                }
                if (alreadySet.Contains(userId))
                {
                    run.SkippedDedup++;
                    continue;
                }

                var rendered = ApprovalReminderDigestRenderer.Render(recipient.FullName, recipientUnits, nowUtc, zone, _options, frontendBaseUrl);
                var digest = BuildDigest(run, recipient, recipientUnits, rendered, nowUtc, localDate, dryRun);

                if (!dryRun)
                {
                    var outbox = new EmailOutboxEntry
                    {
                        RecipientEmail = recipient.Email,
                        RecipientName = recipient.FullName,
                        Subject = rendered.Subject,
                        Headline = rendered.Headline,
                        BodyHtml = rendered.BodyHtml,
                        ActionUrl = frontendBaseUrl.TrimEnd('/') + _options.ApprovalsCenterPath,
                        ActionLabel = "Abrir Centro de Aprovações →",
                        EventCode = EventCode,
                        CorrelationId = digest.Id,
                        Status = "PENDING",
                        CreatedAtUtc = nowUtc,
                        ExpiresAtUtc = nowUtc.AddHours(Math.Max(1, _options.DigestExpiryHours))
                    };
                    digest.OutboxEntry = outbox;
                    digest.OutboxEntryId = outbox.Id;
                }

                if (!persist)
                {
                    produced.Add(digest);
                    run.DigestsDryRun++;
                    continue;
                }

                var outcome = await PersistDigestAsync(digest, ct);
                switch (outcome)
                {
                    case PersistOutcome.Saved:
                        produced.Add(digest);
                        if (dryRun) run.DigestsDryRun++; else run.DigestsQueued++;
                        await _adminLog.WriteAsync("Info", "Notification", dryRun ? "APPROVAL_REMINDER_DRYRUN" : "APPROVAL_REMINDER_QUEUED",
                            $"Approval reminder digest {(dryRun ? "recorded (dry run, no e-mail)" : "queued to outbox")} for {recipient.Email}: {digest.ItemCount} unit(s), overflow {digest.OverflowCount}. (queued = outbox row created, not delivery)",
                            payload: $"{{\"DigestId\":\"{digest.Id}\",\"RunId\":\"{run.Id}\",\"Items\":{digest.ItemCount},\"Overflow\":{digest.OverflowCount},\"DryRun\":{(dryRun ? "true" : "false")}}}");
                        break;
                    case PersistOutcome.Duplicate:
                        run.SkippedDedup++;
                        await _adminLog.WriteAsync("Info", "Notification", "APPROVAL_REMINDER_DEDUP_SKIP",
                            $"Approval reminder digest for {recipient.Email} on {localDate:yyyy-MM-dd} already exists (another instance or an earlier run); nothing queued.");
                        break;
                    case PersistOutcome.Failed:
                        run.Failed++;
                        break;
                }
            }

            run.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Error = Truncate(ex.ToString(), 2000);
            run.CompletedAtUtc = DateTime.UtcNow;
            _logger.LogError(ex, "[ApprovalReminders] Digest cycle failed.");
        }

        if (persist)
        {
            await SaveRunCountersAsync(run, ct);
            _logger.LogInformation("[ApprovalReminders] Cycle complete ({Trigger}, dryRun={DryRun}). Considered={Considered} Eligible={Eligible} NoStageEntry={NoStage} NoRecipient={NoRcp} Recipients={Rcp} Queued={Queued} DryRun={Dry} Dedup={Dedup} AllowList={Allow} Failed={Failed}.",
                trigger, dryRun, run.UnitsConsidered, run.UnitsEligible, run.UnitsWithoutStageEntry, run.UnitsWithoutRecipient, run.Recipients, run.DigestsQueued, run.DigestsDryRun, run.SkippedDedup, run.SkippedAllowList, run.Failed);
            await _adminLog.WriteAsync(run.Error is null ? "Info" : "Error", "Notification", "APPROVAL_REMINDER_CYCLE",
                $"Approval reminder cycle ({(dryRun ? "DRY RUN" : "LIVE")}): {run.DigestsQueued} queued, {run.DigestsDryRun} dry-run, {run.SkippedDedup} dedup-skipped, {run.SkippedAllowList} allow-list-skipped, {run.Failed} failed. Units: {run.UnitsConsidered} considered, {run.UnitsEligible} eligible, {run.UnitsWithoutStageEntry} without stage entry, {run.UnitsWithoutRecipient} without recipient. (queued = outbox row created, not delivery)",
                exceptionDetail: run.Error,
                payload: $"{{\"RunId\":\"{run.Id}\",\"LocalDate\":\"{localDate:yyyy-MM-dd}\",\"DryRun\":{(dryRun ? "true" : "false")}}}");
        }

        return new DigestCycleResult(run, produced);
    }

    private static ApprovalReminderDigest BuildDigest(ApprovalReminderRun run, PendingApprovalRecipient recipient, List<PendingApprovalUnit> units,
        RenderedDigest rendered, DateTime nowUtc, DateTime localDate, bool dryRun)
    {
        var digest = new ApprovalReminderDigest
        {
            RunId = run.Id,
            RecipientUserId = recipient.UserId,
            DigestDateLocal = localDate,
            ItemCount = rendered.ItemCount,
            OverflowCount = rendered.OverflowCount,
            DryRun = dryRun,
            Subject = rendered.Subject,
            PayloadHtml = rendered.BodyHtml,
            CreatedAtUtc = nowUtc
        };
        foreach (var u in units.OrderBy(u => u.StageEnteredAtUtc))
        {
            digest.Items.Add(new ApprovalReminderDigestItem
            {
                DigestId = digest.Id,
                RequestId = u.RequestId,
                ApprovalBatchId = u.ApprovalBatchId,
                BatchNumber = u.BatchNumber,
                RequestNumber = u.RequestNumber,
                Stage = u.Stage,
                StageEnteredAtUtc = u.StageEnteredAtUtc!.Value,
                DaysPending = u.DaysPending
            });
        }
        return digest;
    }

    private enum PersistOutcome { Saved, Duplicate, Failed }

    /// <summary>One SaveChanges for digest + items (+ outbox). Unique violation → Duplicate; other DB errors retried, then Failed.</summary>
    private async Task<PersistOutcome> PersistDigestAsync(ApprovalReminderDigest digest, CancellationToken ct)
    {
        // Snapshot the graph: a failed SaveChanges leaves the tracker holding the rejected rows, and
        // resetting the tracker lets EF fix-up strip navigations. Restore before any retry.
        var items = digest.Items.ToList();
        var outbox = digest.OutboxEntry;

        for (var attempt = 1; ; attempt++)
        {
            _context.ApprovalReminderDigests.Add(digest); // Items and OutboxEntry are reachable → added too
            try
            {
                await _context.SaveChangesAsync(ct);
                return PersistOutcome.Saved;
            }
            catch (DbUpdateException ex)
            {
                // Drop the rejected rows (the run row is re-attached by SaveRunCountersAsync).
                _context.ChangeTracker.Clear();
                digest.Items = items;
                digest.OutboxEntry = outbox;
                digest.OutboxEntryId = outbox?.Id;
                if (IsUniqueViolation(ex)) return PersistOutcome.Duplicate;
                if (attempt >= TransientRetries)
                {
                    _logger.LogError(ex, "[ApprovalReminders] Failed to persist digest for recipient {UserId} after {Attempts} attempts.", digest.RecipientUserId, attempt);
                    await _adminLog.WriteAsync("Error", "Notification", "APPROVAL_REMINDER_RECIPIENT_FAILED",
                        $"Approval reminder digest for recipient {digest.RecipientUserId} could not be persisted after {attempt} attempts; nothing queued.",
                        exceptionDetail: Truncate(ex.ToString(), 4000));
                    return PersistOutcome.Failed;
                }
                _logger.LogWarning(ex, "[ApprovalReminders] Transient failure persisting digest for recipient {UserId} (attempt {Attempt}); retrying.", digest.RecipientUserId, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
            }
        }
    }

    private async Task SaveRunCountersAsync(ApprovalReminderRun run, CancellationToken ct)
    {
        try
        {
            if (_context.Entry(run).State == EntityState.Detached) _context.ApprovalReminderRuns.Update(run);
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[ApprovalReminders] Could not persist run counters for run {RunId}.", run.Id);
        }
    }

    /// <summary>SQL Server 2601 (unique index) / 2627 (unique constraint).</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    private static string? Truncate(string? s, int max) => s is null ? null : (s.Length <= max ? s : s[..max]);
}
