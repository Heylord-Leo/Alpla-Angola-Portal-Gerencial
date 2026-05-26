namespace AlplaPortal.Application.DTOs.MonthlyChanges;

public class ExcludeItemRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class ResolveAnomalyRequest
{
    public string ResolutionNote { get; set; } = string.Empty;
    
    /// <summary>
    /// "APPROVE" or "EXCLUDE"
    /// </summary>
    public string Action { get; set; } = string.Empty;
}
