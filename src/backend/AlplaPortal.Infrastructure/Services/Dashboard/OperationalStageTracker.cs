using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AlplaPortal.Infrastructure.Services.Dashboard;

/// <summary>Identity of a tracked operational entity (grain + polymorphic id).</summary>
public readonly record struct StageEntityKey(string EntityType, Guid EntityId);

/// <summary>One detected canonical-stage change for a tracked lot entity (pure data — no EF state).</summary>
public sealed record StageChangeCandidate(
    string EntityType,
    Guid EntityId,
    Guid RequestId,
    string? PreviousStage,
    string? CurrentStage,
    string? RawCurrentStatus);

// ── Dashboard V2 B9.2 — shared live stage-capture logic. ──
// One component owns: detect a real canonical-stage change from the ChangeTracker (comparing MAPPED stage,
// not raw status), and apply the snapshot + immutable transition mutations. It NEVER queries the DB and
// NEVER calls SaveChanges — DetectCandidates is pure over the tracker; Apply only Adds/Updates/Removes
// tracked entities using a snapshot dictionary the caller pre-fetched in ONE bounded query. This keeps the
// hot path free of N+1 and lets the same logic serve both sync and async SaveChanges.
public static class OperationalStageTracker
{
    // Detect canonical-stage changes for the two lot grains captured centrally (PO_GROUP, APPROVAL_BATCH).
    // REQUEST/Buyer is captured elsewhere (projection-derived) — never here. Returns [] when nothing changed
    // canonically (so the caller performs ZERO snapshot lookups on a no-op or metadata-only save).
    public static List<StageChangeCandidate> DetectCandidates(ChangeTracker tracker)
    {
        var result = new List<StageChangeCandidate>();

        foreach (var e in tracker.Entries<RequestPoGroup>())
        {
            var c = Detect(
                OperationalStageEntityTypes.PoGroup, e, g => g.Id, g => g.RequestId, g => g.Status,
                CanonicalOperationalStageResolver.ResolvePoGroupStage);
            if (c != null) result.Add(c);
        }

        foreach (var e in tracker.Entries<ApprovalBatch>())
        {
            var c = Detect(
                OperationalStageEntityTypes.ApprovalBatch, e, b => b.Id, b => b.RequestId, b => b.Status,
                CanonicalOperationalStageResolver.ResolveApprovalBatchStage);
            if (c != null) result.Add(c);
        }

        return result;
    }

    private static StageChangeCandidate? Detect<TEntity>(
        string entityType,
        EntityEntry<TEntity> entry,
        Func<TEntity, Guid> id,
        Func<TEntity, Guid> requestId,
        Func<TEntity, string> statusSelector,
        Func<string?, string?> resolve) where TEntity : class
    {
        // Only creations, status mutations and hard-deletes can change the canonical stage. Unchanged /
        // detached entries never do.
        string? rawPrev;
        string? rawCurr;
        switch (entry.State)
        {
            case EntityState.Added:
                rawPrev = null;
                rawCurr = statusSelector(entry.Entity);
                break;
            case EntityState.Deleted:
                rawPrev = (string?)entry.Property(nameof(RequestPoGroup.Status)).OriginalValue;
                rawCurr = null; // hard delete → leaves all active stages
                break;
            case EntityState.Modified:
                rawPrev = (string?)entry.Property(nameof(RequestPoGroup.Status)).OriginalValue;
                rawCurr = statusSelector(entry.Entity);
                break;
            default:
                return null; // Unchanged / Detached
        }

        var previousStage = resolve(rawPrev);
        var currentStage = resolve(rawCurr);

        // Compare CANONICAL STAGE, not raw status: two raw statuses mapping to the same stage, or a
        // metadata-only edit, produce no transition and must NOT reset StageEnteredAtUtc.
        if (previousStage == currentStage) return null;

        return new StageChangeCandidate(entityType, id(entry.Entity), requestId(entry.Entity),
            previousStage, currentStage, rawCurr);
    }

    // Apply snapshot + transition mutations for the detected candidates. `existing` is the caller's ONE
    // batched lookup of current snapshots (keyed by grain+id). `nowUtc` is a single shared timestamp so a
    // snapshot and its transition event carry the identical moment. Mutates the context only — no SaveChanges.
    public static void Apply(
        ApplicationDbContext context,
        IReadOnlyList<StageChangeCandidate> candidates,
        IReadOnlyDictionary<StageEntityKey, OperationalStageState> existing,
        DateTime nowUtc)
    {
        foreach (var c in candidates)
        {
            var key = new StageEntityKey(c.EntityType, c.EntityId);
            existing.TryGetValue(key, out var snapshot);

            if (c.CurrentStage != null)
            {
                // ENTER / MOVE into an active stage — upsert the snapshot, reset its entry time.
                var domain = CanonicalOperationalStageResolver.DomainForStage(c.CurrentStage);
                if (snapshot == null)
                {
                    context.OperationalStageStates.Add(new OperationalStageState
                    {
                        Id = Guid.NewGuid(),
                        EntityType = c.EntityType,
                        EntityId = c.EntityId,
                        RequestId = c.RequestId,
                        Domain = domain,
                        StageCode = c.CurrentStage,
                        StageEnteredAtUtc = nowUtc,
                        Source = OperationalStageSources.Live,
                        IsBackfilled = false,
                        CreatedAtUtc = nowUtc,
                        UpdatedAtUtc = nowUtc,
                    });
                }
                else
                {
                    snapshot.Domain = domain;
                    snapshot.StageCode = c.CurrentStage;
                    snapshot.RequestId = c.RequestId;
                    snapshot.StageEnteredAtUtc = nowUtc;
                    snapshot.Source = OperationalStageSources.Live;
                    snapshot.IsBackfilled = false;
                    snapshot.UpdatedAtUtc = nowUtc;
                }

                context.OperationalStageTransitions.Add(new OperationalStageTransition
                {
                    Id = Guid.NewGuid(),
                    EntityType = c.EntityType,
                    EntityId = c.EntityId,
                    RequestId = c.RequestId,
                    Domain = domain,
                    FromStageCode = c.PreviousStage,
                    ToStageCode = c.CurrentStage,
                    OccurredAtUtc = nowUtc,
                    TransitionSource = OperationalStageSources.Live,
                    CreatedAtUtc = nowUtc,
                });
            }
            else
            {
                // EXIT into a terminal / out-of-scope state — remove the snapshot, but ALWAYS record a real
                // history event (never a silent delete). Terminal code lives in history only.
                if (snapshot != null) context.OperationalStageStates.Remove(snapshot);

                var terminal = CanonicalOperationalStageResolver.ResolveTerminalCode(c.RawCurrentStatus);
                context.OperationalStageTransitions.Add(new OperationalStageTransition
                {
                    Id = Guid.NewGuid(),
                    EntityType = c.EntityType,
                    EntityId = c.EntityId,
                    RequestId = c.RequestId,
                    Domain = c.PreviousStage != null
                        ? CanonicalOperationalStageResolver.DomainForStage(c.PreviousStage)
                        : PipelineDomainsForTerminal,
                    FromStageCode = c.PreviousStage,
                    ToStageCode = terminal,
                    OccurredAtUtc = nowUtc,
                    TransitionSource = OperationalStageSources.Live,
                    CreatedAtUtc = nowUtc,
                });
            }
        }
    }

    // Distinct candidate keys for the caller's single bounded snapshot lookup.
    public static IReadOnlyList<StageEntityKey> DistinctKeys(IReadOnlyList<StageChangeCandidate> candidates)
        => candidates.Select(c => new StageEntityKey(c.EntityType, c.EntityId)).Distinct().ToList();

    private const string PipelineDomainsForTerminal = "CONCLUSAO";
}
