namespace AlplaPortal.Domain.Constants;

// ── Dashboard V2 B9.1 — canonical operational stage tracking (persistence foundation). ──
// These constants name the polymorphic grains and the provenance of a stage-entry timestamp for the
// OperationalStageState snapshot and the OperationalStageTransition history. The STAGE CODE and DOMAIN
// string values are the SAME canonical taxonomy emitted by the B6 Operational Pipeline
// (Application layer: PipelineStages / PipelineDomains). They are intentionally NOT redefined here to
// avoid a Domain→Application dependency; the (unwired in B9.1) CanonicalOperationalStageResolver owns the
// single mapping so B6 and B9 never drift. B9.1 persists these as opaque strings only — no capture yet.
public static class OperationalStageEntityTypes
{
    /// <summary>Buyer-domain stages are tracked at the Request grain.</summary>
    public const string Request = "REQUEST";

    /// <summary>Approval / Reajuste stages are tracked at the ApprovalBatch grain.</summary>
    public const string ApprovalBatch = "APPROVAL_BATCH";

    /// <summary>PO / Finance / Receiving / Documentation stages are tracked at the PO group grain.</summary>
    public const string PoGroup = "PO_GROUP";
}

// Provenance of a snapshot's StageEnteredAtUtc — kept honest so the read side (B9.4) can distinguish a
// captured live transition from a best-effort backfill, and can render "Idade não disponível" for UNKNOWN.
public static class OperationalStageSources
{
    /// <summary>Written by live capture from a real transition (B9.2 onward).</summary>
    public const string Live = "LIVE";

    /// <summary>Best-effort backfill from a reliable domain timestamp (B9.3).</summary>
    public const string Backfill = "BACKFILL";

    /// <summary>Current stage is known but no defensible entry time exists — StageEnteredAtUtc stays null.</summary>
    public const string Unknown = "UNKNOWN";
}
