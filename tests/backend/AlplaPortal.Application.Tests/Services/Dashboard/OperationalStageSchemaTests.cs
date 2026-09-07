using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// Dashboard V2 B9.1 — schema validation for the canonical stage-tracking tables on the REAL relational
/// model (isolated LocalDB, never the Development clone — <see cref="IntegrationTestDatabase"/> forbids it).
/// Verifies the two tables and their indexes are created, the one-current-stage-per-entity unique
/// constraint is actually enforced, a null StageEnteredAtUtc is accepted (honest unknown age), and repeated
/// re-entry into the same stage is allowed by the history table. Skips gracefully when LocalDB is absent.
/// </summary>
[Collection("IntegrationTests")]
public class OperationalStageSchemaTests
{
    static OperationalStageSchemaTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                // If the shared isolated DB predates these tables, recreate it from the current model.
                var tableId = ctx.Database
                    .SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.OperationalStageStates'), 0) AS [Value]")
                    .AsEnumerable().First();
                if (tableId == 0) ctx.Database.EnsureDeleted();
            }
            ctx.Database.EnsureCreated();
        }
        catch { /* LocalDB unavailable — CanConnect() gates every test. */ }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    private static OperationalStageState NewState(Guid entityId, Guid requestId, DateTime? enteredAt) => new()
    {
        Id = Guid.NewGuid(),
        EntityType = OperationalStageEntityTypes.PoGroup,
        EntityId = entityId,
        RequestId = requestId,
        Domain = "FINANCAS",
        StageCode = "FIN_SCHEDULED",
        StageEnteredAtUtc = enteredAt,
        Source = enteredAt == null ? OperationalStageSources.Unknown : OperationalStageSources.Live,
        IsBackfilled = false,
        CreatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public void Both_tables_and_expected_indexes_exist()
    {
        if (!CanConnect()) return;
        using var ctx = new ApplicationDbContext(Options());

        int Obj(string name) => ctx.Database.SqlQueryRaw<int>($"SELECT ISNULL(OBJECT_ID('dbo.{name}'), 0) AS [Value]").AsEnumerable().First();
        Assert.NotEqual(0, Obj("OperationalStageStates"));
        Assert.NotEqual(0, Obj("OperationalStageTransitions"));

        int Idx(string name) => ctx.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = '{name}'").AsEnumerable().First();
        Assert.Equal(1, Idx("UX_OperationalStageState_Entity"));
        Assert.Equal(1, Idx("IX_OperationalStageState_RequestId"));
        Assert.Equal(1, Idx("IX_OperationalStageState_Domain_Stage"));
        Assert.Equal(1, Idx("IX_OperationalStageTransition_Entity_Occurred"));
        Assert.Equal(1, Idx("IX_OperationalStageTransition_RequestId"));
    }

    [Fact]
    public async Task Null_stage_entered_is_accepted_as_honest_unknown_age()
    {
        if (!CanConnect()) return;
        var entityId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await using var ctx = new ApplicationDbContext(Options());
        try
        {
            ctx.OperationalStageStates.Add(NewState(entityId, requestId, enteredAt: null));
            await ctx.SaveChangesAsync();

            var saved = await ctx.OperationalStageStates.AsNoTracking().FirstAsync(s => s.EntityId == entityId);
            Assert.Null(saved.StageEnteredAtUtc);
            Assert.Equal(OperationalStageSources.Unknown, saved.Source);
        }
        finally
        {
            await ctx.OperationalStageStates.Where(s => s.EntityId == entityId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Duplicate_current_stage_for_same_entity_is_rejected()
    {
        if (!CanConnect()) return;
        var entityId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await using var ctx = new ApplicationDbContext(Options());
        try
        {
            ctx.OperationalStageStates.Add(NewState(entityId, requestId, DateTime.UtcNow));
            await ctx.SaveChangesAsync();

            await using var ctx2 = new ApplicationDbContext(Options());
            ctx2.OperationalStageStates.Add(NewState(entityId, requestId, DateTime.UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        }
        finally
        {
            await ctx.OperationalStageStates.Where(s => s.EntityId == entityId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task A_failed_save_rolls_back_all_stage_rows_in_the_same_write_unit()
    {
        if (!CanConnect()) return;
        // Capture adds its snapshot/transition rows to the SAME SaveChanges as the business change, so they
        // share one transaction. This proves the atomicity capture relies on: if any row in the unit fails,
        // a row that would otherwise have succeeded is rolled back too — no orphan transition/snapshot.
        var existing = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await using var ctx = new ApplicationDbContext(Options());
        try
        {
            ctx.OperationalStageStates.Add(NewState(existing, requestId, DateTime.UtcNow));
            await ctx.SaveChangesAsync(); // commit the pre-existing snapshot

            await using var ctx2 = new ApplicationDbContext(Options());
            ctx2.OperationalStageStates.Add(NewState(fresh, requestId, DateTime.UtcNow));       // would succeed alone
            ctx2.OperationalStageStates.Add(NewState(existing, requestId, DateTime.UtcNow));     // duplicate → violates unique index
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());

            // The otherwise-valid row must NOT have persisted — the whole unit rolled back.
            await using var verify = new ApplicationDbContext(Options());
            Assert.False(await verify.OperationalStageStates.AnyAsync(s => s.EntityId == fresh));
        }
        finally
        {
            await ctx.OperationalStageStates.Where(s => s.EntityId == existing || s.EntityId == fresh).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Repeated_re_entry_into_same_stage_is_allowed_in_history()
    {
        if (!CanConnect()) return;
        var entityId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await using var ctx = new ApplicationDbContext(Options());
        try
        {
            OperationalStageTransition Ev(DateTime at, string? from) => new()
            {
                Id = Guid.NewGuid(), EntityType = OperationalStageEntityTypes.PoGroup, EntityId = entityId,
                RequestId = requestId, Domain = "PO", FromStageCode = from, ToStageCode = "PO_WAITING",
                OccurredAtUtc = at, TransitionSource = OperationalStageSources.Live, CreatedAtUtc = DateTime.UtcNow,
            };
            // Enter PO_WAITING, leave to correction, then RE-ENTER PO_WAITING — two rows into the same stage.
            ctx.OperationalStageTransitions.Add(Ev(DateTime.UtcNow.AddDays(-2), null));
            ctx.OperationalStageTransitions.Add(Ev(DateTime.UtcNow, "PO_CORRECTION"));
            await ctx.SaveChangesAsync();

            var count = await ctx.OperationalStageTransitions.CountAsync(t => t.EntityId == entityId && t.ToStageCode == "PO_WAITING");
            Assert.Equal(2, count);
        }
        finally
        {
            await ctx.OperationalStageTransitions.Where(t => t.EntityId == entityId).ExecuteDeleteAsync();
        }
    }
}
