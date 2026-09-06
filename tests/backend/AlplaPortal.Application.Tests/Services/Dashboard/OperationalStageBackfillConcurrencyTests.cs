using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// Dashboard V2 B9.3a — DB-ENFORCED LIVE precedence for backfill writes, on a real relational DB (isolated
/// LocalDB; the tracking tables have no FKs so only OperationalStageState rows are seeded). Proves the
/// critical guarantee: BACKFILL never overwrites LIVE, even when LIVE becomes authoritative AFTER the
/// backfill read. LIVE capture's effect is a LIVE OperationalStageState row (exactly what B9.2 writes); the
/// conditional predicate protects it regardless of how it became LIVE. Skips when LocalDB is unavailable.
/// </summary>
[Collection("IntegrationTests")]
public class OperationalStageBackfillConcurrencyTests
{
    static OperationalStageBackfillConcurrencyTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var tableId = ctx.Database.SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.OperationalStageStates'), 0) AS [Value]").AsEnumerable().First();
                if (tableId == 0) ctx.Database.EnsureDeleted();
            }
            ctx.Database.EnsureCreated();
        }
        catch { /* LocalDB unavailable — CanConnect() gates every test. */ }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    private static OperationalStageState Backfill(Guid id, Guid entityId, string stage, DateTime? entry) => new()
    {
        Id = id, EntityType = OperationalStageEntityTypes.PoGroup, EntityId = entityId, RequestId = Guid.NewGuid(),
        Domain = "PO", StageCode = stage, StageEnteredAtUtc = entry,
        Source = OperationalStageSources.Backfill, IsBackfilled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
    };

    // Simulate live capture flipping the snapshot to LIVE (what B9.2 SaveChanges produces).
    private static Task FlipToLiveAsync(ApplicationDbContext ctx, Guid id, DateTime liveEntry)
        => ctx.Set<OperationalStageState>().Where(s => s.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.Source, OperationalStageSources.Live)
            .SetProperty(x => x.IsBackfilled, false)
            .SetProperty(x => x.StageEnteredAtUtc, liveEntry));

    [Fact]
    public async Task Try_improve_updates_a_backfill_null_row()
    {
        if (!CanConnect()) return;
        var id = Guid.NewGuid(); var entity = Guid.NewGuid();
        var entry = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        await using var ctx = new ApplicationDbContext(Options());
        try
        {
            ctx.OperationalStageStates.Add(Backfill(id, entity, "FIN_SCHEDULED", null));
            await ctx.SaveChangesAsync();

            var n = await OperationalStageBackfillWriter.TryImproveEntryAsync(ctx, id, "FIN_SCHEDULED", entry, DateTime.UtcNow, CancellationToken.None);
            Assert.Equal(1, n);
            var row = await ctx.OperationalStageStates.AsNoTracking().FirstAsync(s => s.Id == id);
            Assert.Equal(entry, row.StageEnteredAtUtc);
        }
        finally { await ctx.OperationalStageStates.Where(s => s.EntityId == entity).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task Try_improve_never_touches_a_row_that_became_live()
    {
        if (!CanConnect()) return;
        var id = Guid.NewGuid(); var entity = Guid.NewGuid();
        var liveEntry = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);
        await using var ctx = new ApplicationDbContext(Options());
        try
        {
            ctx.OperationalStageStates.Add(Backfill(id, entity, "FIN_SCHEDULED", null));
            await ctx.SaveChangesAsync();
            await FlipToLiveAsync(ctx, id, liveEntry); // capture wins the race

            var n = await OperationalStageBackfillWriter.TryImproveEntryAsync(ctx, id, "FIN_SCHEDULED", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow, CancellationToken.None);
            Assert.Equal(0, n); // conditional predicate matched nothing — LIVE untouched
            var row = await ctx.OperationalStageStates.AsNoTracking().FirstAsync(s => s.Id == id);
            Assert.Equal(OperationalStageSources.Live, row.Source);
            Assert.False(row.IsBackfilled);
            Assert.Equal(liveEntry, row.StageEnteredAtUtc);
        }
        finally { await ctx.OperationalStageStates.Where(s => s.EntityId == entity).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task Try_correct_stale_updates_backfill_but_not_live()
    {
        if (!CanConnect()) return;
        var id = Guid.NewGuid(); var entity = Guid.NewGuid();
        await using var ctx = new ApplicationDbContext(Options());
        try
        {
            ctx.OperationalStageStates.Add(Backfill(id, entity, "FIN_SCHEDULED", null));
            await ctx.SaveChangesAsync();

            var n1 = await OperationalStageBackfillWriter.TryCorrectStaleStageAsync(ctx, id, "PO_WAITING", "PO", null, Guid.NewGuid(), DateTime.UtcNow, CancellationToken.None);
            Assert.Equal(1, n1);
            Assert.Equal("PO_WAITING", (await ctx.OperationalStageStates.AsNoTracking().FirstAsync(s => s.Id == id)).StageCode);

            await FlipToLiveAsync(ctx, id, DateTime.UtcNow);
            var n2 = await OperationalStageBackfillWriter.TryCorrectStaleStageAsync(ctx, id, "REC_WAITING", "RECEBIMENTO", null, Guid.NewGuid(), DateTime.UtcNow, CancellationToken.None);
            Assert.Equal(0, n2); // LIVE wins
            Assert.Equal("PO_WAITING", (await ctx.OperationalStageStates.AsNoTracking().FirstAsync(s => s.Id == id)).StageCode);
        }
        finally { await ctx.OperationalStageStates.Where(s => s.EntityId == entity).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task Insert_loses_to_an_existing_live_snapshot()
    {
        if (!CanConnect()) return;
        var entity = Guid.NewGuid();
        await using var ctx = new ApplicationDbContext(Options());
        try
        {
            // A LIVE snapshot already owns the entity.
            var live = Backfill(Guid.NewGuid(), entity, "PO_WAITING", DateTime.UtcNow);
            live.Source = OperationalStageSources.Live; live.IsBackfilled = false;
            ctx.OperationalStageStates.Add(live);
            await ctx.SaveChangesAsync();

            var inserted = await OperationalStageBackfillWriter.TryInsertAsync(ctx, Backfill(Guid.NewGuid(), entity, "PO_WAITING", null), CancellationToken.None);
            Assert.False(inserted); // unique index rejected the backfill insert
            var rows = await ctx.OperationalStageStates.AsNoTracking().Where(s => s.EntityId == entity).ToListAsync();
            Assert.Single(rows);
            Assert.Equal(OperationalStageSources.Live, rows[0].Source);
        }
        finally { await ctx.OperationalStageStates.Where(s => s.EntityId == entity).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task Two_context_race_backfill_read_then_live_commit_then_backfill_write_keeps_live()
    {
        if (!CanConnect()) return;
        var id = Guid.NewGuid(); var entity = Guid.NewGuid();
        var liveEntry = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        await using var ctxA = new ApplicationDbContext(Options());
        try
        {
            ctxA.OperationalStageStates.Add(Backfill(id, entity, "FIN_SCHEDULED", null));
            await ctxA.SaveChangesAsync();

            // Context A "reads" and plans an improvement (StageEnteredAtUtc is still null here).
            var planned = await ctxA.OperationalStageStates.AsNoTracking().FirstAsync(s => s.Id == id);
            Assert.Null(planned.StageEnteredAtUtc);

            // Context B: live capture commits LIVE for the same row BEFORE A writes.
            await using (var ctxB = new ApplicationDbContext(Options()))
                await FlipToLiveAsync(ctxB, id, liveEntry);

            // Context A now applies its planned improvement — the conditional predicate saves LIVE.
            var n = await OperationalStageBackfillWriter.TryImproveEntryAsync(ctxA, id, "FIN_SCHEDULED", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow, CancellationToken.None);
            Assert.Equal(0, n);

            var final = await ctxA.OperationalStageStates.AsNoTracking().FirstAsync(s => s.Id == id);
            Assert.Equal(OperationalStageSources.Live, final.Source);
            Assert.False(final.IsBackfilled);
            Assert.Equal(liveEntry, final.StageEnteredAtUtc); // LIVE authoritative — never overwritten
        }
        finally { await ctxA.OperationalStageStates.Where(s => s.EntityId == entity).ExecuteDeleteAsync(); }
    }
}
