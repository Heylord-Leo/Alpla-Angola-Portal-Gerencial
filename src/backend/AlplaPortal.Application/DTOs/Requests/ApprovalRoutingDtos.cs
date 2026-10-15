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

/// <summary>Where the final-stage NOTIFICATION recipient came from (single-nominee model; not an authorization rule).</summary>
public enum FinalNotificationSource
{
    /// <summary>Nobody notifiable: no request nominee and no company nominee that is active with an e-mail.</summary>
    None = 0,
    /// <summary>The request's own nominee, <c>Request.FinalApproverId</c> (set at submit from the company nominee).</summary>
    RequestNominee = 1,
    /// <summary>The company's current nominee, <c>Company.FinalApproverUserId</c> (fallback when the request nominee is not notifiable).</summary>
    CompanyNominee = 2
}

/// <summary>
/// Who is NOTIFIED for the final stage. Who may APPROVE it is decided elsewhere (role + access
/// scope) and can differ: a role-holder who is not the nominee approves but is not notified; a
/// nominee without the role is notified but cannot approve.
/// </summary>
public class FinalNotificationRecipientsDto
{
    public FinalNotificationSource Source { get; set; } = FinalNotificationSource.None;
    public List<FinalApproverDto> Recipients { get; set; } = new();
    public bool HasRecipients => Recipients.Count > 0;
}

public class FinalApproverDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class ManagedScopeDto
{
    public int DepartmentId { get; set; }

    /// <summary>Null = manages every plant of the department.</summary>
    public int? PlantId { get; set; }
}
