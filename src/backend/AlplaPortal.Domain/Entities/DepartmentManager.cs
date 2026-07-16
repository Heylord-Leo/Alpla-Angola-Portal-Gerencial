namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Area-approval responsibility record: which user manages a department, optionally
/// scoped to a single plant. Source of truth for area-approval routing (Phase B+ of
/// the DepartmentManager redesign — see docs/department-manager-routing-redesign-plan.md).
///
/// PlantId semantics:
/// - NULL  → global manager: covers every plant (including ones created later).
/// - value → manager for that specific plant only.
///
/// Since Phase B/C this table is the ONLY source of truth for area-approval routing
/// (submit, queue, authorization, e-mails and the derived "Area Approver" claim).
/// </summary>
public class DepartmentManager
{
    public int Id { get; set; }

    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
