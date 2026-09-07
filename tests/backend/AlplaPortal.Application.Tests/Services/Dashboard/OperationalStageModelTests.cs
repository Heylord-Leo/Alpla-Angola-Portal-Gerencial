using System.Linq;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

// Dashboard V2 B9.1 — model-metadata guards for the canonical stage-tracking foundation. These inspect the
// relational (SQL Server) EF model directly — no database connection is opened (the model is built lazily),
// so they are deterministic and provider-accurate. They lock the schema contract before any capture is
// wired: uniqueness, honest-null age, polymorphic (no-FK) design, and the immutable-event history shape.
public class OperationalStageModelTests
{
    private static ApplicationDbContext BuildContext()
    {
        // A never-opened SQL Server connection string — enough to build the relational model.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\model-only;Database=model-only;Trusted_Connection=True;")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IEntityType State(ApplicationDbContext c) => c.Model.FindEntityType(typeof(OperationalStageState))!;
    private static IEntityType Transition(ApplicationDbContext c) => c.Model.FindEntityType(typeof(OperationalStageTransition))!;

    // ── OperationalStageState ──
    [Fact]
    public void State_has_unique_index_on_entity_identity()
    {
        using var c = BuildContext();
        var idx = State(c).GetIndexes().SingleOrDefault(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "EntityType", "EntityId" }));
        Assert.NotNull(idx);
        Assert.True(idx!.IsUnique, "exactly one current stage per (EntityType, EntityId)");
    }

    [Fact]
    public void State_stage_entered_is_nullable_with_no_fabricated_default()
    {
        using var c = BuildContext();
        var p = State(c).FindProperty(nameof(OperationalStageState.StageEnteredAtUtc))!;
        Assert.True(p.IsNullable, "null = known stage, unknown entry time (honest)");
        Assert.Null(p.GetDefaultValue());     // never a fabricated age
        Assert.Null(p.GetDefaultValueSql());
    }

    [Fact]
    public void State_identity_and_taxonomy_columns_are_required()
    {
        using var c = BuildContext();
        var e = State(c);
        Assert.False(e.FindProperty(nameof(OperationalStageState.EntityType))!.IsNullable);
        Assert.False(e.FindProperty(nameof(OperationalStageState.Domain))!.IsNullable);
        Assert.False(e.FindProperty(nameof(OperationalStageState.StageCode))!.IsNullable);
        // RequestId is stored (scope key).
        Assert.NotNull(e.FindProperty(nameof(OperationalStageState.RequestId)));
    }

    [Fact]
    public void State_updated_at_is_nullable_metadata_only()
    {
        using var c = BuildContext();
        Assert.True(State(c).FindProperty(nameof(OperationalStageState.UpdatedAtUtc))!.IsNullable);
    }

    [Fact]
    public void State_has_scope_and_aggregate_indexes()
    {
        using var c = BuildContext();
        var idx = State(c).GetIndexes().ToList();
        Assert.Contains(idx, i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "RequestId" }));
        Assert.Contains(idx, i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Domain", "StageCode" }));
    }

    // ── OperationalStageTransition ──
    [Fact]
    public void Transition_event_shape_is_from_nullable_to_required_occurred_required()
    {
        using var c = BuildContext();
        var e = Transition(c);
        Assert.True(e.FindProperty(nameof(OperationalStageTransition.FromStageCode))!.IsNullable);  // first entry has no prior stage
        Assert.False(e.FindProperty(nameof(OperationalStageTransition.ToStageCode))!.IsNullable);
        Assert.False(e.FindProperty(nameof(OperationalStageTransition.OccurredAtUtc))!.IsNullable); // a row = a real event
    }

    [Fact]
    public void Transition_history_index_is_not_unique_so_re_entry_is_allowed()
    {
        using var c = BuildContext();
        var idx = Transition(c).GetIndexes().SingleOrDefault(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "EntityType", "EntityId", "OccurredAtUtc" }));
        Assert.NotNull(idx);
        Assert.False(idx!.IsUnique, "re-entering the same stage later is legitimate history");
        // And there is NO unique index that could block repeated re-entry.
        Assert.DoesNotContain(Transition(c).GetIndexes(), i => i.IsUnique);
    }

    // ── Polymorphic / no-cascade design ──
    [Fact]
    public void Tracking_tables_have_no_foreign_keys_so_deletes_never_cascade_into_audit()
    {
        using var c = BuildContext();
        Assert.Empty(State(c).GetForeignKeys());
        Assert.Empty(Transition(c).GetForeignKeys());
        // And no navigations were inferred (polymorphic EntityId, denormalized RequestId).
        Assert.Empty(State(c).GetNavigations());
        Assert.Empty(Transition(c).GetNavigations());
    }

    [Fact]
    public void No_shortcut_navigation_was_added_to_the_tracked_workflow_entities()
    {
        using var c = BuildContext();
        foreach (var t in new[] { typeof(Request), typeof(ApprovalBatch), typeof(RequestPoGroup) })
        {
            var e = c.Model.FindEntityType(t)!;
            Assert.DoesNotContain(e.GetNavigations(), n =>
                n.TargetEntityType.ClrType == typeof(OperationalStageState) ||
                n.TargetEntityType.ClrType == typeof(OperationalStageTransition));
        }
    }
}
