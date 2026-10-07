using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Reminders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Api.Controllers.Admin;

/// <summary>
/// Read-only visibility over the daily approval reminder digests, plus an in-memory preview.
/// Nothing here sends e-mail or persists digests: preview computes what TODAY's digests would
/// contain and returns them (Trigger MANUAL_PREVIEW, no DB writes).
/// </summary>
[ApiController]
[Route("api/admin/approval-reminders")]
[Authorize(Roles = "System Administrator")]
public class AdminApprovalRemindersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ApprovalReminderDigestCycle _cycle;
    private readonly IOptionsMonitor<ApprovalReminderOptions> _options;

    public AdminApprovalRemindersController(ApplicationDbContext context, ApprovalReminderDigestCycle cycle, IOptionsMonitor<ApprovalReminderOptions> options)
    {
        _context = context;
        _cycle = cycle;
        _options = options;
    }

    /// <summary>Effective configuration (safe fields only).</summary>
    [HttpGet("config")]
    public ActionResult GetConfig()
    {
        var o = _options.CurrentValue;
        var zone = ApprovalReminderSchedule.ResolveZone(o.TimeZoneId);
        return Ok(new
        {
            o.Enabled, o.DryRun, o.SendTimeLocal, TimeZone = zone.Id, o.MinPendingAgeDays,
            BusinessDays = o.BusinessDays.Select(d => d.ToString()),
            o.MaxItemsPerDigest, o.MaxBodyBytes, o.DigestExpiryHours,
            AllowListCount = o.RecipientAllowList?.Length ?? 0,
            NextRunUtc = o.Enabled ? ApprovalReminderSchedule.ComputeNextRunUtc(DateTime.UtcNow, o, zone) : (DateTime?)null
        });
    }

    [HttpGet("runs")]
    public async Task<ActionResult> GetRuns([FromQuery] int take = 30)
    {
        take = Math.Clamp(take, 1, 200);
        var runs = await _context.ApprovalReminderRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(take)
            .ToListAsync();
        return Ok(runs);
    }

    /// <summary>Digests of one run with delivery evidence read from the outbox (queued ≠ delivered).</summary>
    [HttpGet("runs/{runId:guid}/digests")]
    public async Task<ActionResult> GetRunDigests(Guid runId)
    {
        var digests = await _context.ApprovalReminderDigests.AsNoTracking()
            .Where(d => d.RunId == runId)
            .Select(d => new
            {
                d.Id, d.RecipientUserId,
                RecipientEmail = d.RecipientUser.Email,
                RecipientName = d.RecipientUser.FullName,
                d.DigestDateLocal, d.ItemCount, d.OverflowCount, d.DryRun, d.Subject, d.CreatedAtUtc,
                d.OutboxEntryId,
                OutboxStatus = d.OutboxEntry != null ? d.OutboxEntry.Status : null,
                OutboxRetryCount = d.OutboxEntry != null ? (int?)d.OutboxEntry.RetryCount : null,
                OutboxLastError = d.OutboxEntry != null ? d.OutboxEntry.LastError : null,
                OutboxProcessedAtUtc = d.OutboxEntry != null ? d.OutboxEntry.ProcessedAtUtc : null,
                OutboxExpiresAtUtc = d.OutboxEntry != null ? d.OutboxEntry.ExpiresAtUtc : null,
                DeliveryStatus = d.DryRun ? "DRY_RUN"
                    : d.OutboxEntry == null ? "NOT_QUEUED"
                    : d.OutboxEntry.Status == "SENT" ? "SENT"
                    : d.OutboxEntry.Status == "DEAD_LETTER" || d.OutboxEntry.Status == "EXPIRED" ? "FAILED"
                    : "QUEUED"
            })
            .OrderBy(d => d.RecipientEmail)
            .ToListAsync();
        return Ok(digests);
    }

    /// <summary>One digest with its rendered body and listed items.</summary>
    [HttpGet("digests/{digestId:guid}")]
    public async Task<ActionResult> GetDigest(Guid digestId)
    {
        var digest = await _context.ApprovalReminderDigests.AsNoTracking()
            .Include(d => d.Items)
            .Include(d => d.RecipientUser)
            .Include(d => d.OutboxEntry)
            .FirstOrDefaultAsync(d => d.Id == digestId);
        if (digest is null) return NotFound();
        return Ok(new
        {
            digest.Id, digest.RunId, digest.RecipientUserId,
            RecipientEmail = digest.RecipientUser.Email,
            RecipientName = digest.RecipientUser.FullName,
            digest.DigestDateLocal, digest.ItemCount, digest.OverflowCount, digest.DryRun, digest.Subject, digest.PayloadHtml, digest.CreatedAtUtc,
            digest.OutboxEntryId,
            OutboxStatus = digest.OutboxEntry?.Status,
            Items = digest.Items.OrderBy(i => i.StageEnteredAtUtc).Select(i => new
            {
                i.RequestId, i.RequestNumber, i.ApprovalBatchId, i.BatchNumber, i.Stage, i.StageEnteredAtUtc, i.DaysPending
            })
        });
    }

    /// <summary>
    /// Computes today's digests in memory with the current rules and returns them.
    /// Persists nothing and queues nothing, regardless of Enabled/DryRun.
    /// </summary>
    [HttpPost("preview")]
    public async Task<ActionResult> Preview(CancellationToken ct)
    {
        var result = await _cycle.PreviewAsync(DateTime.UtcNow, ct);
        var run = result.Run;
        return Ok(new
        {
            Mode = "MANUAL_PREVIEW (nothing persisted, nothing queued)",
            run.LocalDate, run.UnitsConsidered, run.UnitsEligible, run.UnitsWithoutStageEntry, run.UnitsWithoutRecipient, run.Recipients,
            run.SkippedAllowList, run.Error,
            Digests = result.Digests.Select(d => new
            {
                d.RecipientUserId, d.Subject, d.ItemCount, d.OverflowCount, d.PayloadHtml,
                Items = d.Items.Select(i => new { i.RequestNumber, i.BatchNumber, i.Stage, i.StageEnteredAtUtc, i.DaysPending })
            })
        });
    }
}
