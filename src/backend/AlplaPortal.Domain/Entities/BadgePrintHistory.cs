namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Badge Print History — audit record of each badge printing event.
///
/// Stores a complete snapshot of the badge data at print time, enabling
/// fast reprints without re-querying Primavera/Innux. Each reprint
/// creates a separate BadgePrintEvent for full audit traceability.
///
/// Key design decisions:
/// - SnapshotPayloadJson contains the full BadgeData used at print time
///   (name, department, photo URL, etc.). This enables reprint even if
///   the source systems are unavailable.
/// - PhotoSource tracks whether the photo was from Innux or manually uploaded,
///   important for compliance and audit.
/// - Photo resilience: if the photo URL is no longer accessible, the reprint
///   flow gracefully degrades to a placeholder — never breaks.
/// </summary>
public class BadgePrintHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Employee Snapshot (frozen at first print time) ───

    /// <summary>Primavera employee code, primary identifier.</summary>
    public string EmployeeCode { get; set; } = string.Empty;

    /// <summary>Full employee name as printed on the badge.</summary>
    public string EmployeeName { get; set; } = string.Empty;

    /// <summary>Department name at badge creation time.</summary>
    public string? Department { get; set; }

    /// <summary>Employee category at badge creation time.</summary>
    public string? Category { get; set; }

    /// <summary>RFID/card number printed on the badge.</summary>
    public string? CardNumber { get; set; }

    // ─── Organization Context ───

    /// <summary>Company code at print time (e.g. "ALPLAPLASTICO").</summary>
    public string? CompanyCode { get; set; }

    /// <summary>Plant code at print time (e.g. "V1").</summary>
    public string? PlantCode { get; set; }

    // ─── Photo Tracking ───

    /// <summary>Photo source at print time: "INNUX", "MANUAL_UPLOAD", "NONE".</summary>
    public string PhotoSource { get; set; } = BadgePhotoSource.None;

    /// <summary>Photo URL or file path at print time. May become stale.</summary>
    public string? PhotoReference { get; set; }

    // ─── Snapshot for Reprint ───

    /// <summary>
    /// Full JSON snapshot of the BadgeData used to render the badge.
    /// Contains all fields needed for a pixel-perfect reprint.
    /// </summary>
    public string SnapshotPayloadJson { get; set; } = "{}";

    // ─── Layout Reference ───

    /// <summary>FK to the BadgeLayout used at print time. Nullable for V1 default layouts.</summary>
    public Guid? BadgeLayoutId { get; set; }
    public BadgeLayout? BadgeLayout { get; set; }

    // ─── Print Audit ───

    /// <summary>User who performed the first print.</summary>
    public Guid PrintedByUserId { get; set; }
    public User? PrintedByUser { get; set; }

    /// <summary>Timestamp of the first print.</summary>
    public DateTime PrintedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Total number of times this badge has been printed (first + reprints).</summary>
    public int PrintCount { get; set; } = 1;

    // ─── Reprint Events ───

    /// <summary>Collection of individual reprint audit events for this history record.</summary>
    public ICollection<BadgePrintEvent> ReprintEvents { get; set; } = new List<BadgePrintEvent>();
}

/// <summary>Photo source constants for badge print tracking.</summary>
public static class BadgePhotoSource
{
    public const string None = "NONE";
    public const string Innux = "INNUX";
    public const string ManualUpload = "MANUAL_UPLOAD";
}
