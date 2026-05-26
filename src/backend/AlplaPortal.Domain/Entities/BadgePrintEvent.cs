namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Badge Print Event — individual audit record for each reprint action.
///
/// Separate from BadgePrintHistory to provide full traceability:
/// - WHO reprinted
/// - WHEN
/// - WHY (optional reason, reserved for future requirement)
///
/// Each reprint creates one BadgePrintEvent AND increments
/// BadgePrintHistory.PrintCount for fast aggregate queries.
/// </summary>
public class BadgePrintEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the parent print history record.</summary>
    public Guid BadgePrintHistoryId { get; set; }
    public BadgePrintHistory? BadgePrintHistory { get; set; }

    /// <summary>User who performed this specific reprint.</summary>
    public Guid ReprintedByUserId { get; set; }
    public User? ReprintedByUser { get; set; }

    /// <summary>Timestamp of the reprint.</summary>
    public DateTime ReprintedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional reprint reason (reserved for future use).
    /// Examples: "Damaged card", "Name change", "Lost badge".
    /// </summary>
    public string? Reason { get; set; }
}
