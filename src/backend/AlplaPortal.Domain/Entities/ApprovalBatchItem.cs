using System;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Links a RequestLineItem to an ApprovalBatch with the buyer-selected winner (QuotationItem).
/// The selected winner is immutable after batch creation — Area Approvers review but cannot change it.
/// </summary>
public class ApprovalBatchItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApprovalBatchId { get; set; }
    public ApprovalBatch ApprovalBatch { get; set; } = null!;

    public Guid RequestLineItemId { get; set; }
    public RequestLineItem RequestLineItem { get; set; } = null!;

    /// <summary>
    /// The quotation item selected as winner by the Buyer.
    /// This is the source of truth for winner selection in the batch model.
    /// </summary>
    public Guid SelectedQuotationItemId { get; set; }
    public QuotationItem SelectedQuotationItem { get; set; } = null!;

    // Audit fields
    public DateTime CreatedAtUtc { get; set; }
}
