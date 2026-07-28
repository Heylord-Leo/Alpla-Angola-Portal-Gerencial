using System;

namespace AlplaPortal.Domain.Entities;

public class QuotationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }
    public Quotation Quotation { get; set; } = null!;

    public string Description { get; set; } = string.Empty;
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }
    
    public int? ItemCatalogId { get; set; }
    public ItemCatalog? ItemCatalog { get; set; }
    
    // Mapping to original RequestLineItem (organizational only)
    public Guid? MappedRequestLineItemId { get; set; }
    public RequestLineItem? MappedRequestLineItem { get; set; }
    
    // Reconciliation (Phase R1)
    public string ReconciliationStatus { get; set; } = "MAPPED";
    public string? ReconciliationJustification { get; set; }

    // ── OCR-original per-line baseline (Financial Reconciliation) ──
    // Immutable snapshot of what the OCR extraction captured for this line, written ONCE at
    // SaveQuotation / document-replacement time and NEVER overwritten by a normal UpdateQuotation.
    // All nullable: legacy rows (pre-feature), manually-created quotation lines, and manually-added
    // lines in an OCR quotation legitimately have no baseline. A NULL is never treated as 0 —
    // callers must distinguish "not extracted" from "extracted as zero" (see reconciliation calculator).
    public decimal? OcrOriginalQuantity { get; set; }
    public decimal? OcrOriginalUnitPrice { get; set; }
    public decimal? OcrOriginalDiscountAmount { get; set; }
    public decimal? OcrOriginalIvaRatePercent { get; set; }
    public string? OcrOriginalUnitText { get; set; }
    public int? OcrOriginalUnitId { get; set; }
    public decimal? OcrOriginalLineTotal { get; set; }

    /// <summary>One consolidated free-text reason for material financial-field edits of this line
    /// against its OCR baseline (quantity/price/discount/IVA/unit). Distinct from
    /// ReconciliationJustification (SUBSTITUTE/EXTRA/IGNORED reason), the EXCLUDE comment, and the
    /// document residual justification — never overloaded onto any of those.</summary>
    public string? LineAdjustmentJustification { get; set; }


    // Deterministic ordering
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    
    // New Financial Fields (Step 10 Refinement)
    public int LineNumber { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? IvaRateId { get; set; }
    public IvaRate? IvaRate { get; set; }
    public decimal IvaRatePercent { get; set; } // Snapshot at the time of creation/update
    public decimal GrossSubtotal { get; set; }
    public decimal IvaAmount { get; set; }

    // Receiving Fields
    public decimal ReceivedQuantity { get; set; }
    public string? DivergenceNotes { get; set; }
    public int? LineItemStatusId { get; set; }
    public LineItemStatus? LineItemStatus { get; set; }
}
