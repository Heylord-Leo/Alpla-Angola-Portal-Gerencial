namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// DTO for the Buyer to propose a line item as not-quoted.
/// </summary>
public class NotQuotedProposeDto
{
    /// <summary>Required justification explaining why the item cannot be quoted. Minimum 20 characters.</summary>
    public string Justification { get; set; } = null!;
}
