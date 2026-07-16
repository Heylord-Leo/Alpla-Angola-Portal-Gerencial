namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Which level of the resolution cascade produced the managers of an
/// <see cref="ApprovalRoutingResultDto"/>.
/// </summary>
public enum ApprovalRoutingSource
{
    /// <summary>No manager found at any level — submission must be blocked.</summary>
    None = 0,

    /// <summary>DepartmentManagers rows matching the request's specific plant.</summary>
    PlantSpecific = 1,

    /// <summary>Global DepartmentManagers rows (PlantId NULL).</summary>
    GlobalManagers = 2
}

public class ApprovalRoutingResultDto
{
    public ApprovalRoutingSource Source { get; set; } = ApprovalRoutingSource.None;
    public List<AreaManagerDto> Managers { get; set; } = new();
    public bool HasManagers => Managers.Count > 0;
}

public class AreaManagerDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Null when the user qualifies via a global row or the legacy fallback.</summary>
    public int? PlantId { get; set; }
}

public class ManagedScopeDto
{
    public int DepartmentId { get; set; }

    /// <summary>Null = manages every plant of the department.</summary>
    public int? PlantId { get; set; }
}
