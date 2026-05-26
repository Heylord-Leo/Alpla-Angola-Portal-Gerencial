namespace AlplaPortal.Domain.Entities;

/// <summary>
/// A single leave/absence record for an HR employee.
///
/// Workflow: Manager creates DRAFT → submits → HR approves/rejects.
/// HR may also create directly for administrative reasons.
///
/// Status transitions:
///   DRAFT → SUBMITTED → APPROVED | REJECTED
///   DRAFT → CANCELLED
///   SUBMITTED → CANCELLED
///   APPROVED → CANCELLED
/// </summary>
public class LeaveRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EmployeeId { get; set; }
    public HREmployee Employee { get; set; } = null!;

    public int LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>Business days (excluding weekends). Auto-calculated on create/update.</summary>
    public int TotalDays { get; set; }

    /// <summary>Internal status code — see LeaveStatusCodes.</summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary>Portal user who created this record (typically the Department Manager).</summary>
    public Guid RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; } = null!;

    /// <summary>Portal user who approved this record (typically HR).</summary>
    public Guid? ApprovedByUserId { get; set; }
    public User? ApprovedByUser { get; set; }

    public string? Notes { get; set; }
    public string? RejectedReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public ICollection<LeaveStatusHistory> StatusHistory { get; set; } = new List<LeaveStatusHistory>();
}
