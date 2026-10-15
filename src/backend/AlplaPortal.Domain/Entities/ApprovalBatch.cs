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

    /// <summary>
    /// UTC moment the batch entered its CURRENT approval stage (set on creation, on resubmission to
    /// area, and on area approval). Basis for "time waiting" in the approval reminder digest. Null
    /// only for pre-existing batches whose stage entry could not be established from reliable
    /// transition evidence during the backfill (those are reported, never guessed).
    /// </summary>
    public DateTime? StageEnteredAtUtc { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Two alternative approvers deciding the same batch at the same
    /// time can both pass the status guard; the second UPDATE then fails with
    /// DbUpdateConcurrencyException (mapped to 409 APPROVAL_CONCURRENCY_CONFLICT) instead of
    /// committing a second transition, a second PO-group activation or a second notification.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // Navigation properties
    public ICollection<ApprovalBatchItem> Items { get; set; } = new List<ApprovalBatchItem>();
    public ICollection<ApprovalBatchExtraItemDecision> ExtraItemDecisions { get; set; } = new List<ApprovalBatchExtraItemDecision>();
    public ICollection<RequestPoGroup> PoGroups { get; set; } = new List<RequestPoGroup>();
    /// <summary>Adjustment V2 structured cycles (dormant until Phase 3 — see ApprovalBatchAdjustment).</summary>
    public ICollection<ApprovalBatchAdjustment> Adjustments { get; set; } = new List<ApprovalBatchAdjustment>();
}
