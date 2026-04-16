namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Local HR employee projection — synced from Innux, enriched with Portal mappings.
///
/// This entity exists because Leave/Attendance records need stable FK references
/// to employees. Innux is the operational identity source (has card numbers,
/// departments, active status). Primavera remains the master for profile lookups.
///
/// Scoping strategy:
/// - InnuxDepartmentName/InnuxDepartmentId: raw Innux source fields (always populated on sync)
/// - PortalDepartmentId, PlantId, ManagerUserId: optional Portal mapping fields for scope filtering
/// - IsMapped: computed flag indicating whether enough Portal fields are set for reliable scoping
///
/// HR/Admin can assign or correct Portal mappings via the Employee admin area.
/// Unmapped employees are visible to HR/Admin but not to scoped Department Managers.
/// </summary>
public class HREmployee
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Innux Source Fields (populated by sync) ───

    /// <summary>Internal Innux technical PK (IDFuncionario).</summary>
    public int InnuxEmployeeId { get; set; }

    /// <summary>Innux Numero — primary match key with Primavera Codigo.</summary>
    public string EmployeeCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Innux department description (dbo.Departamentos.Descricao).</summary>
    public string? InnuxDepartmentName { get; set; }

    /// <summary>Innux department FK (dbo.Funcionarios.IDDepartamento).</summary>
    public int? InnuxDepartmentId { get; set; }

    public string? JobTitle { get; set; }
    public string? CardNumber { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? HireDate { get; set; }
    public DateTime? TerminationDate { get; set; }

    // ─── Portal Mapping Fields (assigned by HR/Admin) ───

    /// <summary>
    /// FK to the robust synced Primavera Department Master for accurate source mapping.
    /// This provides composite-department contexts (e.g. DTI - ALPLAPLASTICO).
    /// </summary>
    public int? DepartmentMasterId { get; set; }
    public DepartmentMaster? DepartmentMaster { get; set; }

    /// <summary>
    /// Optional FK to Portal Department for scope filtering.
    /// Null until explicitly mapped by HR/Admin.
    /// Note: Slated for deprecation / safe replacement by DepartmentMasterId.
    /// </summary>
    public int? PortalDepartmentId { get; set; }
    public Department? PortalDepartment { get; set; }

    /// <summary>
    /// Optional FK to Portal Plant for scope filtering.
    /// Null until explicitly mapped by HR/Admin.
    /// </summary>
    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

    /// <summary>
    /// Optional FK to the Portal User who manages this employee.
    /// Used for Department Manager scope resolution.
    /// </summary>
    public Guid? ManagerUserId { get; set; }
    public User? ManagerUser { get; set; }

    // ─── Mapping Status ───

    /// <summary>
    /// True when at least one Portal mapping field is set (PortalDepartmentId, PlantId, or ManagerUserId).
    /// Unmapped employees are visible to HR/Admin but excluded from scoped manager views.
    /// </summary>
    public bool IsMapped { get; set; }

    // ─── Sync Metadata ───

    public DateTime? LastSyncedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
