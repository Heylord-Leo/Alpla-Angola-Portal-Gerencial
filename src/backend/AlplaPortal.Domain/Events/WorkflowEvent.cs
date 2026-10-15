namespace AlplaPortal.Domain.Events;

/// <summary>
/// Immutable event payload emitted after a workflow status transition.
/// Carries all context needed by the notification orchestrator to resolve
/// recipients, build messages, and dispatch to in-app + email channels.
/// </summary>
public record WorkflowEvent
{
    /// <summary>Event type code (e.g., "REQUEST_SUBMITTED", "AREA_APPROVED"). See <see cref="Constants.WorkflowEventCodes"/>.</summary>
    public required string EventCode { get; init; }

    /// <summary>The request this event pertains to.</summary>
    public required Guid RequestId { get; init; }

    /// <summary>Human-readable request number (e.g., "REQ-11/04/2026-042").</summary>
    public string RequestNumber { get; init; } = "S/N";

    /// <summary>Request title for contextual messages.</summary>
    public string RequestTitle { get; init; } = string.Empty;

    /// <summary>The status code the request transitioned to.</summary>
    public required string TargetStatusCode { get; init; }

    /// <summary>The action code that triggered this transition (e.g., "SUBMIT", "APPROVE").</summary>
    public required string ActionTaken { get; init; }

    /// <summary>User who performed the action.</summary>
    public required Guid ActorUserId { get; init; }

    /// <summary>Display name of the actor.</summary>
    public string ActorName { get; init; } = "Sistema";

    /// <summary>Optional comment recorded in the status history.</summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Dedup anchor — typically the <c>RequestStatusHistory.Id</c>.
    /// Used to prevent duplicate notifications on retries.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    // --- Pre-resolved participant context (avoids re-querying inside orchestrator) ---

    public Guid RequesterId { get; init; }
    public Guid? BuyerId { get; init; }
    public Guid? AreaApproverId { get; init; }
    public Guid? FinalApproverId { get; init; }
    public int? DepartmentId { get; init; }
    public int? PlantId { get; init; }
    public int? CompanyId { get; init; }

    /// <summary>
    /// The P.O. group the event is about (PO_REGISTERED: the group just registered or re-registered). A request can
    /// hold several groups with different suppliers, totals and currencies; e-mail content for the event must come
    /// from THIS group, never from the request header or another group. Null for request-level events.
    /// </summary>
    public Guid? PoGroupId { get; init; }

    /// <summary>
    /// The <c>RequestPayment</c> row the event is about (PAYMENT_SCHEDULED: the row just scheduled; PAYMENT_COMPLETED:
    /// the row just completed). Both Finance actions operate on ONE P.O. group and ONE payment row, so the Accounts
    /// Payable notice takes its amount, currency and dates from this row (planned amount when scheduling, actual paid
    /// amount when completing) and its supplier from <see cref="PoGroupId"/>. Null for request-level (legacy) events.
    /// </summary>
    public int? PaymentId { get; init; }

    // --- Adjustment V2 (Phase 3) context — populated only for the batch-adjustment events ---

    /// <summary>The lot number the adjustment was requested on (e.g. "Lote #1").</summary>
    public int? BatchNumber { get; init; }

    /// <summary>Friendly, comma-separated reason labels for the adjustment (never raw codes).</summary>
    public string? AdjustmentReasonLabels { get; init; }

    /// <summary>Affected item descriptions, or null / "Lote inteiro" when whole-lot.</summary>
    public string? AdjustmentAffectedItems { get; init; }

    /// <summary>Adjustment source stage (AdjustmentConstants.SourceStages: AREA | FINAL) — drives the
    /// "returns first to Area" note on a Final-sourced resubmit. Null for non-adjustment events.</summary>
    public string? AdjustmentSourceStage { get; init; }
}
