using System;
using System.Collections.Generic;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Links a RequestLineItem to an ApprovalBatch. Under the candidate model the Buyer submits one
/// or more <see cref="ApprovalBatchItemCandidate"/> options and the AREA APPROVER selects the
/// single winner at area approval; the Buyer never selects a winner.
/// </summary>
public class ApprovalBatchItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApprovalBatchId { get; set; }
    public ApprovalBatch ApprovalBatch { get; set; } = null!;

    public Guid RequestLineItemId { get; set; }
    public RequestLineItem RequestLineItem { get; set; } = null!;

    /// <summary>
    /// Denormalized downstream-compatibility pointer to the WINNING quotation item.
    /// Candidate model: NULL until the Area Approver selects the winner (stamped together with
    /// <see cref="SelectedCandidateId"/> in the area-approval transaction).
    /// Legacy batches (zero candidates): populated at batch creation by the old buyer-selects
    /// flow and still authoritative for them.
    /// </summary>
    public Guid? SelectedQuotationItemId { get; set; }
    public QuotationItem? SelectedQuotationItem { get; set; }

    /// <summary>The candidate the Area Approver selected as winner. Null before the area
    /// decision and on legacy batch items (which have no candidate rows).</summary>
    public Guid? SelectedCandidateId { get; set; }
    public ApprovalBatchItemCandidate? SelectedCandidate { get; set; }

    // ── Area winner-decision stamps (written only by area approval; cleared on return) ──
    public Guid? WinnerSelectedByUserId { get; set; }
    public DateTime? WinnerSelectedAtUtc { get; set; }

    /// <summary>Mandatory when the selected candidate is more expensive than the cheapest
    /// same-currency candidate beyond the FinancialIntegrity tolerance; optional otherwise
    /// (persisted whenever supplied).</summary>
    public string? WinnerSelectionJustification { get; set; }

    public ICollection<ApprovalBatchItemCandidate> Candidates { get; set; } = new List<ApprovalBatchItemCandidate>();

    // Audit fields
    public DateTime CreatedAtUtc { get; set; }
}
