using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Dashboard;

// ── Dashboard V2 B9.3 — HONEST legacy backfill of OperationalStageState (CURRENT SNAPSHOT ONLY). ──
// Populates a current-stage snapshot for every EXISTING in-scope aging entity (APPROVAL_BATCH + PO_GROUP —
// Buyer/REQUEST is out of scope, B9.2d) so the future read side can count them immediately. Source=BACKFILL,
// IsBackfilled=true. A reliable server-authored entry timestamp is set where one provably exists
// (OperationalStageBackfillEvidence); otherwise StageEnteredAtUtc stays NULL — never fabricated.
//
// It NEVER writes OperationalStageTransition (no fake legacy history — the transition table stays LIVE-truth
// from B9.2 onward), NEVER overwrites a LIVE snapshot, and supports dry-run (same classification, writes
// suppressed). Idempotent and rerunnable in bounded batches.
public sealed class OperationalStageBackfillService
{
    private readonly ApplicationDbContext _context;
    public OperationalStageBackfillService(ApplicationDbContext context) => _context = context;

    // In-scope raw statuses (mirror CanonicalOperationalStageResolver's non-null domain — a test asserts parity).
    private static readonly string[] ActiveBatchStatuses =
    {
        RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
        RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
        RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
        RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
    };
    private static readonly string[] ActiveGroupStatuses =
    {
        RequestConstants.PoGroupStatuses.WaitingPo, RequestConstants.PoGroupStatuses.WaitingPoCorrection,
        RequestConstants.PoGroupStatuses.PoIssued, RequestConstants.PoGroupStatuses.PaymentRequestSent,
        RequestConstants.PoGroupStatuses.AdvancePaymentRequired, RequestConstants.PoGroupStatuses.PaymentScheduled,
        RequestConstants.PoGroupStatuses.AdvancePaymentScheduled, RequestConstants.PoGroupStatuses.PaymentCompleted,
        RequestConstants.PoGroupStatuses.WaitingReceipt, RequestConstants.PoGroupStatuses.InFollowup,
        RequestConstants.PoGroupStatuses.WaitingSupplierDelivery, RequestConstants.PoGroupStatuses.WaitingFiscalReceipt,
        RequestConstants.PoGroupStatuses.WaitingReconciliation,
    };

    public async Task<OperationalStageBackfillResult> BackfillAsync(
        bool dryRun, int batchSize = 250, CancellationToken ct = default)
    {
        if (batchSize <= 0) batchSize = 250;
        var result = new OperationalStageBackfillResult { DryRun = dryRun };

        // ── APPROVAL_BATCH ──
        var batchIds = await _context.Set<ApprovalBatch>()
            .Where(b => ActiveBatchStatuses.Contains(b.Status))
            .OrderBy(b => b.Id).Select(b => b.Id).ToListAsync(ct);
        foreach (var page in Paginate(batchIds, batchSize))
        {
            var batches = await _context.Set<ApprovalBatch>()
                .Where(b => page.Contains(b.Id))
                .Select(b => new { b.Id, b.RequestId, b.Status, b.CreatedAtUtc }).ToListAsync(ct);

            var candidates = new List<Candidate>();
            foreach (var b in batches)
            {
                result.Scanned++;
                var stage = CanonicalOperationalStageResolver.ResolveApprovalBatchStage(b.Status);
                if (stage == null) continue; // defensive; the query already filters to active statuses
                var entry = OperationalStageBackfillEvidence.ForApprovalBatch(stage, b.CreatedAtUtc);
                candidates.Add(new Candidate(OperationalStageEntityTypes.ApprovalBatch, b.Id, b.RequestId, stage, entry));
            }
            await ApplyPageAsync(candidates, result, dryRun, ct);
        }

        // ── PO_GROUP ──
        var groupIds = await _context.Set<RequestPoGroup>()
            .Where(g => ActiveGroupStatuses.Contains(g.Status))
            .OrderBy(g => g.Id).Select(g => g.Id).ToListAsync(ct);
        foreach (var page in Paginate(groupIds, batchSize))
        {
            var groups = await _context.Set<RequestPoGroup>()
                .Where(g => page.Contains(g.Id))
                .Select(g => new { g.Id, g.RequestId, g.Status, g.OperationalReceiptCompletedAtUtc }).ToListAsync(ct);

            // One bounded query for the latest SCHEDULED payment creation per group in this page (no N+1).
            var scheduledByGroup = await _context.Set<RequestPayment>()
                .Where(p => p.RequestPoGroupId != null && page.Contains(p.RequestPoGroupId.Value)
                            && p.PaymentStatus == RequestPayment.PaymentStatuses.Scheduled)
                .GroupBy(p => p.RequestPoGroupId!.Value)
                .Select(grp => new { GroupId = grp.Key, LatestCreated = grp.Max(p => p.CreatedAtUtc) })
                .ToDictionaryAsync(x => x.GroupId, x => (DateTime?)x.LatestCreated, ct);

            var candidates = new List<Candidate>();
            foreach (var g in groups)
            {
                result.Scanned++;
                var stage = CanonicalOperationalStageResolver.ResolvePoGroupStage(g.Status);
                if (stage == null) continue;
                scheduledByGroup.TryGetValue(g.Id, out var latestScheduled);
                var entry = OperationalStageBackfillEvidence.ForPoGroup(stage, latestScheduled, g.OperationalReceiptCompletedAtUtc);
                candidates.Add(new Candidate(OperationalStageEntityTypes.PoGroup, g.Id, g.RequestId, stage, entry));
            }
            await ApplyPageAsync(candidates, result, dryRun, ct);
        }

        return result;
    }

    private async Task ApplyPageAsync(List<Candidate> candidates, OperationalStageBackfillResult result, bool dryRun, CancellationToken ct)
    {
        if (candidates.Count == 0) return;

        // ONE bounded, no-tracking snapshot lookup for the whole page — used only to CLASSIFY. The actual
        // writes are DB-current-state conditional operations (OperationalStageBackfillWriter), so a LIVE
        // change landing between this read and the write can never be overwritten.
        var entityType = candidates[0].EntityType;
        var ids = candidates.Select(c => c.EntityId).ToList();
        var existing = (await _context.Set<OperationalStageState>().AsNoTracking()
                .Where(s => s.EntityType == entityType && ids.Contains(s.EntityId)).ToListAsync(ct))
            .ToDictionary(s => s.EntityId);

        var now = DateTime.UtcNow;
        foreach (var c in candidates)
        {
            result.InScope++;
            result.Bump(c.StageCode, reliable: c.StageEnteredAtUtc != null);

            if (!existing.TryGetValue(c.EntityId, out var snap))
            {
                // Create a fresh BACKFILL snapshot (loses to a concurrent LIVE insert via the unique index).
                if (dryRun) { result.Created++; continue; }
                var inserted = await OperationalStageBackfillWriter.TryInsertAsync(_context, new OperationalStageState
                {
                    Id = Guid.NewGuid(),
                    EntityType = c.EntityType,
                    EntityId = c.EntityId,
                    RequestId = c.RequestId,
                    Domain = CanonicalOperationalStageResolver.DomainForStage(c.StageCode),
                    StageCode = c.StageCode,
                    StageEnteredAtUtc = c.StageEnteredAtUtc,
                    Source = OperationalStageSources.Backfill,
                    IsBackfilled = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }, ct);
                if (inserted) result.Created++; else result.LostRaceToLive++;
            }
            else if (snap.Source == OperationalStageSources.Live)
            {
                result.SkippedLive++; // LIVE already authoritative at classify time.
            }
            else if (snap.StageCode != c.StageCode)
            {
                // Stale BACKFILL — correct to current canonical stage, but ONLY while the DB row is still
                // BACKFILL (conditional). No fabricated transition history.
                if (dryRun) { result.Updated++; continue; }
                var n = await OperationalStageBackfillWriter.TryCorrectStaleStageAsync(
                    _context, snap.Id, c.StageCode, CanonicalOperationalStageResolver.DomainForStage(c.StageCode),
                    c.StageEnteredAtUtc, c.RequestId, now, ct);
                if (n > 0) result.Updated++; else result.LostRaceToLive++;
            }
            else if (snap.StageEnteredAtUtc == null && c.StageEnteredAtUtc != null)
            {
                // null → reliable improvement, conditional on the row still being an unknown-age BACKFILL row.
                if (dryRun) { result.Improved++; continue; }
                var n = await OperationalStageBackfillWriter.TryImproveEntryAsync(
                    _context, snap.Id, c.StageCode, c.StageEnteredAtUtc.Value, now, ct);
                if (n > 0) result.Improved++; else result.LostRaceToLive++;
            }
            else
            {
                // Same stage, no improvement — never downgrade a reliable timestamp to null.
                result.SkippedUnchanged++;
            }
        }
    }

    private static IEnumerable<List<T>> Paginate<T>(List<T> all, int size)
    {
        for (var i = 0; i < all.Count; i += size)
            yield return all.GetRange(i, Math.Min(size, all.Count - i));
    }

    private sealed record Candidate(string EntityType, Guid EntityId, Guid RequestId, string StageCode, DateTime? StageEnteredAtUtc);
}

public sealed class OperationalStageBackfillResult
{
    public bool DryRun { get; set; }
    public int Scanned { get; set; }
    public int InScope { get; set; }
    public int Created { get; set; }
    public int Improved { get; set; }
    public int Updated { get; set; }
    public int SkippedLive { get; set; }
    public int SkippedUnchanged { get; set; }
    /// <summary>A backfill candidate that lost a concurrent race to LIVE capture (never overwritten).</summary>
    public int LostRaceToLive { get; set; }
    public int ReliableTimestampCount { get; set; }
    public int UnknownTimestampCount { get; set; }
    public List<StageCountDto> ByStage { get; set; } = new();

    internal void Bump(string stage, bool reliable)
    {
        if (reliable) ReliableTimestampCount++; else UnknownTimestampCount++;
        var row = ByStage.FirstOrDefault(s => s.StageCode == stage);
        if (row == null) { row = new StageCountDto { StageCode = stage }; ByStage.Add(row); }
        row.InScope++;
        if (reliable) row.Reliable++; else row.Unknown++;
    }
}

public sealed class StageCountDto
{
    public string StageCode { get; set; } = string.Empty;
    public int InScope { get; set; }
    public int Reliable { get; set; }
    public int Unknown { get; set; }
}
