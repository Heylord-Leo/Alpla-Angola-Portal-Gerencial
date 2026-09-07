using System;

namespace AlplaPortal.Domain.Entities;

// ── Dashboard V2 B9.1 — immutable canonical operational stage transition history (persistence foundation). ──
// Each row is ONE real transition INTO ToStageCode that actually occurred, at OccurredAtUtc. The event
// model (FromStageCode? → ToStageCode @ OccurredAtUtc) is deliberately chosen over an interval model
// (Entered/Exited): a single append-only insert can never become internally inconsistent (there is no
// open interval to keep closed), it is trivial to write atomically inside the business transaction, and
// "where is it now?" is already answered by OperationalStageState. Duration reconstruction (B9.4+) is
// obtained by ordering an entity's events by OccurredAtUtc.
//
// HONESTY RULE: never fabricate a transition to make legacy data count. A row exists only for a
// transition whose moment is genuinely known. Entities whose current stage is known but whose entry time
// is not are represented by an OperationalStageState row with a NULL StageEnteredAtUtc — NOT by a fake
// event here.
//
// EntityId is polymorphic (no FK). RequestId is denormalized for scope joins (no FK — deleting a Request
// never cascades away audit history). Re-entering the same stage later is legitimate and produces another
// row: there is intentionally NO uniqueness constraint that would block repeated re-entry.
// No capture writes to this table yet in B9.1.
public class OperationalStageTransition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Grain: see <see cref="Constants.OperationalStageEntityTypes"/>.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Polymorphic id of the tracked entity (no FK — resolved by EntityType).</summary>
    public Guid EntityId { get; set; }

    /// <summary>Owning request, denormalized for RequestAccessScope joins (no FK — never cascades).</summary>
    public Guid RequestId { get; set; }

    /// <summary>Canonical B6 domain of the destination stage.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Stage the entity left. NULL when this is the first tracked stage for the entity.</summary>
    public string? FromStageCode { get; set; }

    /// <summary>Canonical B6 stage the entity entered.</summary>
    public string ToStageCode { get; set; } = string.Empty;

    /// <summary>When the transition actually occurred (UTC). Always known — a row exists only for a real event.</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>How the event was recorded: see <see cref="Constants.OperationalStageSources"/>.</summary>
    public string? TransitionSource { get; set; }

    /// <summary>Row insertion time (bookkeeping). NOT the transition moment — that is OccurredAtUtc.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
