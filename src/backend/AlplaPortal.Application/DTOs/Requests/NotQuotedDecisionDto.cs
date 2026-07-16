namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// DTO for accepting or rejecting a not-quoted proposal.
/// </summary>
public class NotQuotedDecisionDto
{
    /// <summary>Required comment explaining the accept/reject decision.</summary>
    public string Comment { get; set; } = null!;
}
