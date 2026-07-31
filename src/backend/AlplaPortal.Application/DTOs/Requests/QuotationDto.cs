using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

public class SaveQuotationRequestDto
{
    public int SupplierId { get; set; }
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public DateTime? DocumentDate { get; set; }

    /// <summary>
    /// Post-Payment Completion (Release 2): billing document type of this quotation —
    /// PROFORMA or FINAL_INVOICE. No default; the Buyer must select explicitly. Only the WINNING
    /// quotation's value ever produces a Final Invoice obligation on the PO group.
    /// </summary>
    public string? DocumentType { get; set; }

    public string Currency { get; set; } = string.Empty;
    public decimal TotalGrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalIvaAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? SourceType { get; set; } // OCR, MANUAL
    public string? SourceFileName { get; set; }
    public Guid? ProformaAttachmentId { get; set; }

    // Financial Integrity Fields (Per-Quotation)
    public decimal? OcrTotal { get; set; }
    public bool FinancialIntegrityOverride { get; set; }
    public string? OverrideJustification { get; set; }

    public List<SaveQuotationItemDto> Items { get; set; } = new();
}

public class SaveQuotationItemDto
{
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? UnitId { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? IvaRateId { get; set; }
    public int? ItemCatalogId { get; set; } // Optional catalog item linkage
    public Guid? MappedRequestLineItemId { get; set; } // Link back to original request line item

    public string ReconciliationStatus { get; set; } = "MAPPED";
    public string? ReconciliationJustification { get; set; }

    // ── Financial Reconciliation ──
    /// <summary>"OCR" for a line the extraction produced, "MANUAL" for a buyer-added line. Null is
    /// tolerated (legacy/back-compat) and resolved by quotation context. An "OCR" line MUST carry
    /// its baseline (see backend enforcement) — it is never silently exempted.</summary>
    public string? LineOrigin { get; set; }

    /// <summary>OCR-original per-line baseline captured at extraction. Null on any field = "not
    /// extracted" (never an implicit 0). Only written to the entity by SaveQuotation / document
    /// replacement; UpdateQuotation compares against the persisted baseline and never overwrites it.</summary>
    public decimal? OcrOriginalQuantity { get; set; }
    public decimal? OcrOriginalUnitPrice { get; set; }
    public decimal? OcrOriginalDiscountAmount { get; set; }
    public decimal? OcrOriginalIvaRatePercent { get; set; }
    public string? OcrOriginalUnitText { get; set; }
    public int? OcrOriginalUnitId { get; set; }
    public decimal? OcrOriginalLineTotal { get; set; }

    /// <summary>One consolidated free-text reason for material financial-field edits of this line
    /// vs its OCR baseline. Distinct from ReconciliationJustification and the residual justification.</summary>
    public string? LineAdjustmentJustification { get; set; }
}

/// <summary>Canonical values for <see cref="SaveQuotationItemDto.LineOrigin"/>.</summary>
public static class QuotationLineOrigins
{
    public const string Ocr = "OCR";
    public const string Manual = "MANUAL";
}

/// <summary>One line's reconciliation diagnostics for the breakdown UI (mirrors LineReconciliationResult).</summary>
public class LineReconciliationDto
{
    public Guid? QuotationItemId { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ReconciliationStatus { get; set; } = "MAPPED";
    public bool HasOcrBaseline { get; set; }
    public bool IsManualAddition { get; set; }
    public bool QuantityChanged { get; set; }
    public bool UnitPriceChanged { get; set; }
    public bool DiscountChanged { get; set; }
    public bool IvaChanged { get; set; }
    public bool UnitChanged { get; set; }
    public List<string> ImputedOcrComponents { get; set; } = new();
    public bool RequiresAdjustmentReason { get; set; }
    public bool HasAdjustmentReason { get; set; }
    public bool HasReconciliationReason { get; set; }
}

/// <summary>
/// Backend-authoritative financial reconciliation between the OCR document and the final considered
/// total. Returned by the preview endpoint and inside the SaveQuotation/UpdateQuotation 409.
/// <see cref="ResidualVariance"/> is SIGNED; only <see cref="ResidualExceedsTolerance"/> (which uses
/// Math.Abs) gates the save. A residual justification never zeroes the residual.
/// </summary>
public class QuotationReconciliationDto
{
    public decimal OcrHeaderTotal { get; set; }
    public decimal OcrLineSumTotal { get; set; }
    public decimal ReconstructedOcrLineSum { get; set; }
    public decimal StructuralHeaderDifference { get; set; }
    public decimal OcrLineComponentDifference { get; set; }

    public decimal FinalConsideredTotal { get; set; }
    public decimal ManualAdditionsTotal { get; set; }

    public decimal IgnoredImpact { get; set; }
    public decimal QuantityImpact { get; set; }
    public decimal UnitPriceImpact { get; set; }
    public decimal DiscountImpact { get; set; }
    public decimal IvaImpact { get; set; }
    public decimal GlobalDiscountImpact { get; set; }
    public decimal ManualAdditionsImpact { get; set; }
    public decimal ExplainedLineAdjustments { get; set; }

    /// <summary>Signed document residual (OcrHeaderTotal + ExplainedLineAdjustments − FinalConsideredTotal).</summary>
    public decimal ResidualVariance { get; set; }
    public decimal ToleranceApplied { get; set; }
    public bool ResidualExceedsTolerance { get; set; }

    public List<LineReconciliationDto> Lines { get; set; } = new();
}

public class SavedQuotationDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public int? SupplierId { get; set; }
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public DateTime? DocumentDate { get; set; }

    /// <summary>Post-Payment Completion (Release 2): PROFORMA or FINAL_INVOICE, or null when unclassified.</summary>
    public string? DocumentType { get; set; }

    public string Currency { get; set; } = string.Empty;
    public decimal TotalGrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalIvaAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalTaxableBase { get; set; }
    public decimal TotalDiscountAmount { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? SourceFileName { get; set; }
    public Guid? ProformaAttachmentId { get; set; }
    public bool IsSelected { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int ItemCount { get; set; }
    
    // Additional Supplier Identity Fields for Wizard UX
    public string? SupplierPortalCode { get; set; }
    public string? SupplierPrimaveraCode { get; set; }
    public string? SupplierRegistrationStatus { get; set; }

    public List<SavedQuotationItemDto> Items { get; set; } = new();
}

public class PurchaseHistoryInsightDto
{
    public bool HasHistory { get; set; }
    public DateTime? LastPurchaseDateUtc { get; set; }
    public decimal? LastUnitPrice { get; set; }
    public string? LastCurrency { get; set; }
    public string? LastUom { get; set; }
    public decimal CurrentUnitPrice { get; set; }
    public decimal? DifferencePercent { get; set; }
    public string Status { get; set; } = "NO_HISTORY";
}

public class SavedQuotationItemDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public string? UnitCode { get; set; }
    public decimal UnitPrice { get; set; }

    public int? IvaRateId { get; set; }
    public decimal IvaRatePercent { get; set; }

    public decimal DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }

    public decimal GrossSubtotal { get; set; }
    public decimal TaxableBase { get; set; }
    public decimal IvaAmount { get; set; }
    public decimal LineTotal { get; set; }

    public int? ItemCatalogId { get; set; }
    public string? ItemCatalogCode { get; set; }
    
    public Guid? MappedRequestLineItemId { get; set; }
    
    public string ReconciliationStatus { get; set; } = "MAPPED";
    public string? ReconciliationJustification { get; set; }

    // Financial Reconciliation — persisted OCR-original baseline (for edit hydration) + line reason.
    public decimal? OcrOriginalQuantity { get; set; }
    public decimal? OcrOriginalUnitPrice { get; set; }
    public decimal? OcrOriginalDiscountAmount { get; set; }
    public decimal? OcrOriginalIvaRatePercent { get; set; }
    public string? OcrOriginalUnitText { get; set; }
    public int? OcrOriginalUnitId { get; set; }
    public decimal? OcrOriginalLineTotal { get; set; }
    public string? LineAdjustmentJustification { get; set; }

    // Receiving Fields
    public decimal ReceivedQuantity { get; set; }
    public string? DivergenceNotes { get; set; }
    public string? LineItemStatusCode { get; set; }
    public string? LineItemStatusName { get; set; }
    public string? LineItemStatusBadgeColor { get; set; }

    public PurchaseHistoryInsightDto? HistoryInsight { get; set; }

    // ── Cancelled-batch reuse (Option C) — annotated server-side from the eligibility service ──
    /// <summary>Used in a CANCELLED batch and NOT authorized for reuse — must not be a selectable candidate.</summary>
    public bool IsReuseBlocked { get; set; }
    /// <summary>Explicitly authorized for reuse (active, unconsumed authorization).</summary>
    public bool IsReuseAuthorized { get; set; }
    public Guid? SourceCancelledBatchId { get; set; }
    public int? SourceCancelledBatchNumber { get; set; }
    public Guid? ReuseAuthorizationId { get; set; }
    /// <summary>Batch that consumed the reuse authorization (provenance after consumption).</summary>
    public Guid? ReuseConsumedFromBatchId { get; set; }
}

/// <summary>Buyer request to authorize reuse of specific quotation items previously used in a CANCELLED batch (Option C).</summary>
public class AuthorizeQuotationReuseDto
{
    public List<Guid> QuotationItemIds { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Buyer request to revoke an active, unconsumed reuse authorization.</summary>
public class RevokeQuotationReuseDto
{
    public string Reason { get; set; } = string.Empty;
}
