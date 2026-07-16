using System;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Records the Area Approver's decision on an extra quotation item within a specific batch.
/// Extra items are quotation items not mapped to any request line item.
/// Approved extras generate new RequestLineItems linked to the batch.
/// </summary>
public class ApprovalBatchExtraItemDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApprovalBatchId { get; set; }
    public ApprovalBatch ApprovalBatch { get; set; } = null!;

    /// <summary>The extra quotation item being decided upon.</summary>
    public Guid QuotationItemId { get; set; }
    public QuotationItem QuotationItem { get; set; } = null!;

    /// <summary>Decision: APPROVE, REJECT, ADJUST.</summary>
    public string Decision { get; set; } = null!;

    /// <summary>Optional comment from the Area Approver.</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// If decision is APPROVE, the generated RequestLineItem that was created from this extra item.
    /// Null for REJECT/ADJUST decisions.
    /// </summary>
    public Guid? GeneratedRequestLineItemId { get; set; }
    public RequestLineItem? GeneratedRequestLineItem { get; set; }

    // Audit fields
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}
