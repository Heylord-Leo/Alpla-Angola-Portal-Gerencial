using System;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Dashboard;

// ── Dashboard V2 B9.3a — DB-enforced LIVE precedence for backfill writes. ──
// Every mutation of an EXISTING snapshot is a CONDITIONAL update evaluated against the row's CURRENT
// database state (Source = 'BACKFILL' AND IsBackfilled = 1), NOT against a value read earlier. If live
// capture flipped the row to Source = 'LIVE' after the backfill read, the predicate matches 0 rows and the
// backfill leaves it untouched — LIVE always wins, even when it becomes authoritative AFTER the read. This
// closes the read-then-write race without any schema change (no rowversion). Inserts rely on the unique
// (EntityType, EntityId) index: a concurrent LIVE insert makes the backfill insert lose (0 rows written).
public static class OperationalStageBackfillWriter
{
    /// <summary>
    /// null → reliable improvement, ONLY while the row is still BACKFILL, at the expected stage, still null.
    /// Returns rows affected (0 = lost to LIVE / no longer eligible).
    /// </summary>
    public static Task<int> TryImproveEntryAsync(
        ApplicationDbContext ctx, Guid id, string expectedStage, DateTime entry, DateTime now, CancellationToken ct)
        => ctx.Set<OperationalStageState>()
            .Where(s => s.Id == id
                        && s.Source == OperationalStageSources.Backfill && s.IsBackfilled
                        && s.StageCode == expectedStage && s.StageEnteredAtUtc == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.StageEnteredAtUtc, entry)
                .SetProperty(x => x.UpdatedAtUtc, now), ct);

    /// <summary>
    /// Correct a stale BACKFILL snapshot to the current canonical stage, ONLY while the row is still BACKFILL.
    /// No transition history is written. Returns rows affected (0 = lost to LIVE).
    /// </summary>
    public static Task<int> TryCorrectStaleStageAsync(
        ApplicationDbContext ctx, Guid id, string newStage, string newDomain, DateTime? entry, Guid requestId, DateTime now, CancellationToken ct)
        => ctx.Set<OperationalStageState>()
            .Where(s => s.Id == id && s.Source == OperationalStageSources.Backfill && s.IsBackfilled)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.StageCode, newStage)
                .SetProperty(x => x.Domain, newDomain)
                .SetProperty(x => x.RequestId, requestId)
                .SetProperty(x => x.StageEnteredAtUtc, entry)
                .SetProperty(x => x.UpdatedAtUtc, now), ct);

    /// <summary>
    /// Insert a fresh BACKFILL snapshot. Returns false when the unique (EntityType, EntityId) index rejects
    /// it — a concurrent snapshot (LIVE from capture, or another backfill) already owns the entity, so the
    /// backfill loses. The failed entity is detached so the context stays usable.
    /// </summary>
    public static async Task<bool> TryInsertAsync(ApplicationDbContext ctx, OperationalStageState snapshot, CancellationToken ct)
    {
        ctx.Set<OperationalStageState>().Add(snapshot);
        try
        {
            await ctx.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            ctx.Entry(snapshot).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
}
