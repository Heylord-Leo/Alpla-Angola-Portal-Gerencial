using System;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// One quotation option the Buyer submitted for a single ApprovalBatchItem. The Area Approver —
/// never the Buyer — selects exactly one candidate per item as the winner at area approval.
///
/// <para>Every commercial fact is FROZEN here at submission time (server-loaded, never
/// client-supplied). Approvers, group building, and audit read these snapshots; the FKs to the
/// live quotation rows exist only for traceability. A later edit of the live quotation never
/// changes what was submitted for approval — divergence is resolved by returning the batch,
/// never silently.</para>
///
/// <para>Legacy compatibility: batch items created before the candidate model have zero
/// candidate rows and a populated <see cref="ApprovalBatchItem.SelectedQuotationItemId"/>
/// (the historical buyer-selected winner). No candidate rows are ever synthesized for them.</para>
/// </summary>
public class ApprovalBatchItemCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApprovalBatchItemId { get; set; }
    public ApprovalBatchItem ApprovalBatchItem { get; set; } = null!;

    // ── Traceability references (never the approver-facing read path) ──
    public Guid QuotationItemId { get; set; }
    public QuotationItem QuotationItem { get; set; } = null!;

    public Guid QuotationId { get; set; }
    public Quotation Quotation { get; set; } = null!;

    // ── Frozen commercial snapshot (source of truth for approval and group building) ──
    public int? SupplierId { get; set; }
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public string? SupplierNifSnapshot { get; set; }

    public string QuotedDescription { get; set; } = string.Empty;
    public decimal QuotedQuantity { get; set; }
    public int? UnitId { get; set; }
    public string? UnitTextSnapshot { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal IvaRatePercent { get; set; }
    public decimal IvaAmount { get; set; }
    public decimal GrossSubtotal { get; set; }
    public decimal LineTotal { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string? QuotationDocumentNumber { get; set; }

    /// <summary>Snapshot of Quotation.DocumentDate (the quotation carries no separate due/validity
    /// date today — this is the closest persisted document date).</summary>
    public DateTime? QuotationDocumentDate { get; set; }

    // ── Frozen reconciliation context (why this line looked the way it did at submission) ──
    public bool HasReconciliationWarnings { get; set; }
    public string? ReconciliationStatusSnapshot { get; set; }
    public string? ReconciliationJustificationSnapshot { get; set; }
    public string? LineAdjustmentJustificationSnapshot { get; set; }

    /// <summary>Optional informational note from the Buyer about this option. Frozen with the
    /// snapshot; carries NO winner/preference semantics for authorization or group building.</summary>
    public string? BuyerNote { get; set; }

    // Audit fields
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}
