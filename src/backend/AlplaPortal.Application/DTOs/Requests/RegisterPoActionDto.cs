using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

public class RegisterPoActionDto
{
    public string? Comment { get; set; }
    public Guid? PoGroupId { get; set; }

    // ── Phase 1 P.O. cancellation (D15): the replacement P.O. reuses the number of a previous CANCELLED P.O. of the
    // same lines. Warned, never blocked by itself; the Buyer confirms and explains (audited). ──
    public bool PreviousPoNumberReuseConfirmed { get; set; }
    public string? PreviousPoNumberReuseComment { get; set; }
    
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

    // ── v2.249.4 — P.O. document identification + document supersession ──
    /// <summary>The P.O. attachment this registration/correction refers to (the upload response id). Null = newest live P.O. file of the group.</summary>
    public Guid? PoAttachmentId { get; set; }
    /// <summary>
    /// The Buyer explicitly confirmed that a document the server could not identify as a P.O. is one (audited with the Comment).
    /// The identification itself never comes from the client: it is derived from the server-held extraction evidence bound to
    /// the registered file's hash (<c>PoDocumentExtraction</c>); no evidence for that exact file = UNCERTAIN.
    /// </summary>
    public bool DocumentTypeConfirmed { get; set; }
    /// <summary>EXTRACTED | MANUAL | CURRENT — audit only (how the submitted number was obtained).</summary>
    public string? PurchaseOrderNumberSource { get; set; }
}
