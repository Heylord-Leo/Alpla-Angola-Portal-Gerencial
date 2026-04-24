namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Structured log entry for the Monthly Changes processing pipeline.
/// Captures sync events, detection results, coding results, export generation, errors.
///
/// Unlike AdminLogEntry (which is UI-facing), MCProcessingLog is a technical
/// diagnostic log for auditing the automated pipeline.
/// </summary>
public class MCProcessingLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Parent ───

    public Guid ProcessingRunId { get; set; }
    public MCProcessingRun? ProcessingRun { get; set; }

    // ─── Event ───

    /// <summary>
    /// Event type for filtering and aggregation.
    /// Values: SYNC_START, SYNC_COMPLETE, DETECT_START, DETECT_COMPLETE,
    ///         CODE_START, CODE_COMPLETE, EXPORT_GENERATED, STATUS_CHANGE, ERROR.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Human-readable log message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional structured details (JSON or free text). E.g., error stack trace.</summary>
    public string? Details { get; set; }

    /// <summary>Actor: system, user email, or service name.</summary>
    public string? Actor { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
