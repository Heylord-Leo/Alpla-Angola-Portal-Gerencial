namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// DTO for the Buyer to close a line item without quotation (CLOSED_NOT_QUOTED).
/// Unlike the legacy not-quoted proposal flow, this is a final Buyer decision —
/// no Requester/Area Approver acceptance is involved.
/// </summary>
public class CloseNotQuotedDto
{
    /// <summary>Required reason selected from the predefined list (stored as its display label).</summary>
    public string ReasonCode { get; set; } = null!;

    /// <summary>Required free-text justification. Minimum 20 characters.</summary>
    public string Justification { get; set; } = null!;
}
