namespace AlplaPortal.Application.DTOs.Requests;

public class RegisterPoActionDto
{
    public string? Comment { get; set; }
    public Guid? PoGroupId { get; set; }
    
    // OCR Validation Payload
    public bool HasMismatches { get; set; }
    public bool OverrideConfirmed { get; set; }
    public string? MismatchDetails { get; set; }

    // ── Buy-to-Pay: Payment Condition (Buyer decision at PO registration) ──
    /// <summary>
    /// Payment condition: POST_PAID, ADVANCE_FULL, ADVANCE_PARTIAL.
    /// NULL = POST_PAID (backward compatible).
    /// </summary>
    public string? PaymentConditionCode { get; set; }

    /// <summary>
    /// Advance percentage (1–100). Required when PaymentConditionCode = ADVANCE_PARTIAL.
    /// Automatically set to 100 for ADVANCE_FULL.
    /// </summary>
    public decimal? AdvancePaymentPercent { get; set; }

    /// <summary>
    /// Source of the payment condition selection: OCR_DETECTED, USER_SELECTED.
    /// </summary>
    public string? PaymentConditionSource { get; set; }

    // ── Backend OCR Validation Payload ──
    public string? PurchaseOrderNumber { get; set; }
    public string? ExtractedSupplierName { get; set; }
    public decimal? ExtractedTotalAmount { get; set; }
    public string? ExtractedCurrencyCode { get; set; }

    // Duplicate Validation Payload
    public bool OverrideDuplicateConfirmed { get; set; }
    public string? DuplicateOverrideComment { get; set; }
}
