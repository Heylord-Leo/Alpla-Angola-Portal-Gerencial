namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Adjustment V2 (Phase 2 — DORMANT): one structured adjustment cycle of one ApprovalBatch.
///
/// <para>Dormancy contract: as of Phase 2 NO workflow writes or reads this aggregate — the
/// existing Area/Final adjustment flow (batch statuses, RequestStatusHistory audit, the v2.233.0
/// QF context parser) remains the active implementation. Cycle creation begins in Phase 3.</para>
///
/// <para>A batch can accumulate multiple historical cycles (CycleNumber unique per batch) but at
/// most ONE open cycle at a time — enforced by a filtered unique index on Status, and re-checked
/// transactionally by the future creating code.</para>
///
/// <para>User references are plain Guids without FK/navigation, following the ApprovalBatch
/// CreatedByUserId convention (names are resolved post-query).</para>
/// </summary>
public class ApprovalBatchAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApprovalBatchId { get; set; }
    public ApprovalBatch ApprovalBatch { get; set; } = null!;

    /// <summary>Sequential cycle number within the batch (1, 2, 3, ...). Unique per batch.</summary>
    public int CycleNumber { get; set; }

    /// <summary>AdjustmentConstants.SourceStages — which approval stage requested the cycle.</summary>
    public string SourceStage { get; set; } = string.Empty;

    /// <summary>AdjustmentConstants.States — cycle lifecycle, SEPARATE from ApprovalBatch.Status.
    /// The one-open-cycle-per-batch rule is a filtered unique index directly on this column (a
    /// SQL Server filtered index cannot reference a computed column, so there is no IsOpen
    /// helper column — keep the index filter in sync with AdjustmentConstants.States.Open).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the approver flagged the whole lot rather than specific items.</summary>
    public bool WholeBatch { get; set; }

    /// <summary>The approver's mandatory free-text motive (the structured reasons classify it).</summary>
    public string ApproverComment { get; set; } = string.Empty;

    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }

    /// <summary>Min/max candidate combination totals captured at cycle creation — the "before"
    /// side of the Phase 7 totals diff. Null when not computable (mixed currencies etc.).</summary>
    public decimal? TotalsBeforeMin { get; set; }
    public decimal? TotalsBeforeMax { get; set; }

    /// <summary>Set when the cycle reaches a terminal state (RESUBMITTED or CANCELLED).</summary>
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

    // Audit fields (ApprovalBatch convention)
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    // Navigation properties (owned children — cascade from this root)
    public ICollection<ApprovalBatchAdjustmentReason> Reasons { get; set; } = new List<ApprovalBatchAdjustmentReason>();
    public ICollection<ApprovalBatchAdjustmentResolution> Resolutions { get; set; } = new List<ApprovalBatchAdjustmentResolution>();
    public ICollection<ApprovalBatchAdjustmentFieldChange> FieldChanges { get; set; } = new List<ApprovalBatchAdjustmentFieldChange>();
    public ICollection<ApprovalBatchCandidateReview> CandidateReviews { get; set; } = new List<ApprovalBatchCandidateReview>();
}

/// <summary>
/// One structured reason of an adjustment cycle (AdjustmentConstants.ReasonCodes). A null
/// RequestLineItemId means the reason applies to the whole lot; item-required reasons
/// (quantity/specification/unit/remove-item) reference their line.
/// </summary>
public class ApprovalBatchAdjustmentReason
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdjustmentId { get; set; }
    public ApprovalBatchAdjustment Adjustment { get; set; } = null!;

    public string ReasonCode { get; set; } = string.Empty;

    public Guid? RequestLineItemId { get; set; }
    public RequestLineItem? RequestLineItem { get; set; }

    /// <summary>Optional reason-specific context (e.g. what "OTHER" means).</summary>
    public string? Detail { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// The mandatory "Resposta ao reajuste" recorded at each hand-off: the Requester's on completing
/// their correction, the Buyer's at resubmission. At most one per actor type per cycle.
/// </summary>
public class ApprovalBatchAdjustmentResolution
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdjustmentId { get; set; }
    public ApprovalBatchAdjustment Adjustment { get; set; } = null!;

    /// <summary>AdjustmentConstants.ActorTypes (REQUESTER | BUYER).</summary>
    public string ActorType { get; set; } = string.Empty;

    public Guid ResolvedByUserId { get; set; }
    public string ResolutionComment { get; set; } = string.Empty;
    public DateTime ResolvedAtUtc { get; set; }
}

/// <summary>
/// Typed old→new audit of one Requester-owned business-field edit during a cycle. FieldCode is
/// the CLOSED AdjustmentConstants.FieldCodes catalog — deliberately not a generic audit framework.
/// Powers the Phase 7 timeline diffs ("Quantidade solicitada: 20 → 15 UN").
/// </summary>
public class ApprovalBatchAdjustmentFieldChange
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdjustmentId { get; set; }
    public ApprovalBatchAdjustment Adjustment { get; set; } = null!;

    public Guid RequestLineItemId { get; set; }
    public RequestLineItem RequestLineItem { get; set; } = null!;

    public string FieldCode { get; set; } = string.Empty;

    /// <summary>Display-normalized values (never re-parsed for logic; the entity is the truth).</summary>
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}

/// <summary>
/// Buyer review of one candidate option flagged by a blocking Requester edit
/// (AdjustmentConstants.CandidateReviewTriggers → CandidateReviewStates).
///
/// <para>ApprovalBatchItemId and QuotationItemId are deliberately plain Guids WITHOUT foreign
/// keys: review rows are the audit of what the Buyer decided and must SURVIVE candidate/item
/// deletion on REPLACE/REMOVE (mirroring the candidate snapshot's "frozen facts must not block"
/// convention). Uniqueness on (AdjustmentId, ApprovalBatchItemId, QuotationItemId) keeps one
/// review per option per cycle.</para>
/// </summary>
public class ApprovalBatchCandidateReview
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdjustmentId { get; set; }
    public ApprovalBatchAdjustment Adjustment { get; set; } = null!;

    /// <summary>Identity of the batch item whose option was flagged (no FK — see class remarks).</summary>
    public Guid ApprovalBatchItemId { get; set; }

    /// <summary>Canonical quotation-line identity of the flagged option (no FK — see class remarks).</summary>
    public Guid QuotationItemId { get; set; }

    /// <summary>AdjustmentConstants.CandidateReviewTriggers — which blocking edit flagged it.</summary>
    public string TriggerReason { get; set; } = string.Empty;

    /// <summary>AdjustmentConstants.CandidateReviewStates (PENDING until the Buyer acts).</summary>
    public string Status { get; set; } = string.Empty;

    public string? ReviewComment { get; set; }

    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
