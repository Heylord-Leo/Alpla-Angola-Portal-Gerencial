namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Adjustment V2 — Phase 3 structured "Solicitar Reajuste" request, shared by the Area and Final
/// approval endpoints (the source stage is fixed by the route, not the body). Replaces the previous
/// comment-only body: the approver now classifies the request with one or more structured reasons
/// while still writing the mandatory free-text comment.
/// </summary>
public class BatchAdjustmentRequestDto
{
    /// <summary>The approver's mandatory free-text motive (the reasons classify it, the comment
    /// specifies). Backend-enforced, as today.</summary>
    public string? Comment { get; set; }

    /// <summary>True when the approver flagged the whole lot rather than specific items. Item-scoped
    /// reasons are still allowed to name their items; item-required reasons are incompatible with a
    /// whole-lot-only request.</summary>
    public bool WholeBatch { get; set; }

    /// <summary>The structured reasons (at least one required). Each reason may target a specific
    /// batch line item or the whole lot (null item).</summary>
    public List<BatchAdjustmentReasonInputDto> Reasons { get; set; } = new();
}

/// <summary>One structured reason selected by the approver.</summary>
public class BatchAdjustmentReasonInputDto
{
    /// <summary>An <see cref="AlplaPortal.Domain.Constants.AdjustmentConstants.ReasonCodes"/> value.</summary>
    public string ReasonCode { get; set; } = string.Empty;

    /// <summary>Affected batch line item; null = whole-lot scope for this reason. Must reference an
    /// item that belongs to the target batch.</summary>
    public Guid? RequestLineItemId { get; set; }

    /// <summary>Optional reason-specific context (e.g. what "OTHER" means).</summary>
    public string? Detail { get; set; }
}
