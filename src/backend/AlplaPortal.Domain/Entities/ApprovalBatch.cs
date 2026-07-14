using System;
using System.Collections.Generic;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Represents a batch of request line items grouped by the Buyer for partial approval.
/// Each batch follows its own approval lifecycle independently from the parent request.
/// Only applicable to QUOTATION requests.
/// </summary>
public class ApprovalBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    /// <summary>Sequential batch number within the request (1, 2, 3, ...).</summary>
    public int BatchNumber { get; set; }

    /// <summary>
    /// Batch lifecycle status:
    /// WAITING_AREA_APPROVAL, AREA_ADJUSTMENT, WAITING_FINAL_APPROVAL,
    /// FINAL_ADJUSTMENT, APPROVED, REJECTED
    /// </summary>
    public string Status { get; set; } = "WAITING_AREA_APPROVAL";

    /// <summary>Optional comment from the Buyer when creating the batch.</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Immutable snapshot of the total amount at the time of final approval.
    /// Null until final approval.
    /// </summary>
    public decimal? ApprovedTotalAmount { get; set; }

    /// <summary>Budget justification provided during approval (if required).</summary>
    public string? BudgetJustification { get; set; }

    // Audit fields
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    // Navigation properties
    public ICollection<ApprovalBatchItem> Items { get; set; } = new List<ApprovalBatchItem>();
    public ICollection<ApprovalBatchExtraItemDecision> ExtraItemDecisions { get; set; } = new List<ApprovalBatchExtraItemDecision>();
    public ICollection<RequestPoGroup> PoGroups { get; set; } = new List<RequestPoGroup>();
}
