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
    public int? PlantId { get; init; }
}
