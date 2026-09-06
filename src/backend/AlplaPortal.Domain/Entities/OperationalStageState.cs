using System;

namespace AlplaPortal.Domain.Entities;

// ── Dashboard V2 B9.1 — current canonical operational stage snapshot (persistence foundation). ──
// Exactly ONE row per tracked operational entity (EntityType + EntityId), holding the entity's CURRENT
// canonical stage and, when defensibly known, when it entered that stage. This is the primary read source
// for the future Stage Aging dashboard (B9.4). It answers "where is it now, and since when (if known)?".
//
// KEY SEMANTIC: StageEnteredAtUtc is NULLABLE BY DESIGN. Null means "stage is known, historical entry
// time is unknown" — the honest state for entities that already existed when tracking began and whose
// entry moment cannot be reconstructed. Null is never rendered as an age of 0; the read side treats it as
// "Idade não disponível". UpdatedAtUtc is snapshot bookkeeping ONLY and MUST NEVER be used to compute
// stage age (an unrelated snapshot rewrite would corrupt the age).
//
// EntityId is polymorphic (a Request, ApprovalBatch, or RequestPoGroup id) and therefore has NO foreign
// key. RequestId is denormalized purely so RequestAccessScope can filter by scope with a single join; it
// also carries no FK, so deleting/cancelling a Request can never cascade-destroy this snapshot.
// No capture writes to this table yet in B9.1 — the schema exists; population arrives in B9.2 / B9.3.
public class OperationalStageState
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Grain: see <see cref="Constants.OperationalStageEntityTypes"/> (REQUEST | APPROVAL_BATCH | PO_GROUP).</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Polymorphic id of the tracked entity (no FK — resolved by EntityType).</summary>
    public Guid EntityId { get; set; }

    /// <summary>Owning request, denormalized for RequestAccessScope joins (no FK — never cascades).</summary>
    public Guid RequestId { get; set; }

    /// <summary>Canonical B6 domain (COMPRAS | APROVACOES | PO | FINANCAS | RECEBIMENTO | DOCUMENTACAO).</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Canonical B6 stage code (e.g. PO_WAITING, FIN_SCHEDULED, REC_WAITING).</summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>When the entity entered the current stage. NULL = known stage, unknown entry time.</summary>
    public DateTime? StageEnteredAtUtc { get; set; }

    /// <summary>Provenance of <see cref="StageEnteredAtUtc"/>: see <see cref="Constants.OperationalStageSources"/>.</summary>
    public string? Source { get; set; }

    /// <summary>True when the row (and its entry time, if any) came from a best-effort backfill, not live capture.</summary>
    public bool IsBackfilled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Snapshot bookkeeping ONLY. NEVER used to compute stage age.</summary>
    public DateTime? UpdatedAtUtc { get; set; }
}
