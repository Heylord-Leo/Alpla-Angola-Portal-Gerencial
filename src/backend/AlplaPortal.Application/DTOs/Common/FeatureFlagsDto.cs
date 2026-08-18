namespace AlplaPortal.Application.DTOs.Common;

/// <summary>
/// Feature flags exposed to the frontend by <c>GET /api/v1/config/features</c>.
/// Booleans only — the UI needs to know what to render, not how the server is configured.
/// </summary>
public class FeatureFlagsDto
{
    /// <summary>
    /// Post-Payment Completion workflow is switched on. While false the UI must render exactly
    /// what it rendered before the feature existed.
    /// </summary>
    public bool PostPaymentCompletionEnabled { get; set; }

    /// <summary>
    /// A request created now must carry an explicit billing document type
    /// (PROFORMA or FINAL_INVOICE) before it can be submitted.
    /// False while the feature is off or the effective date has not been reached.
    /// </summary>
    public bool SourceDocumentTypeRequired { get; set; }

    /// <summary>
    /// Release 3: a PAYMENT request may carry SEVERAL source documents, each with its own OCR
    /// reading, classification and items. PAYMENT only — Quotation Management keeps one document
    /// per quotation regardless of this flag.
    ///
    /// <para>While false the payment screens render exactly the single-document layout they
    /// rendered before, and a request that already holds source-document rows still displays them
    /// through the legacy path.</para>
    /// </summary>
    public bool PaymentMultiDocumentEnabled { get; set; }

    /// <summary>
    /// Release 4 Phase 4: the automatic post-payment completion LIFECYCLE is switched on —
    /// grouped requests complete through the new path (Fiscal Receipt driven) instead of the
    /// legacy finalization. Deliberately separate from
    /// <see cref="PostPaymentCompletionEnabled"/>: during Phase 3B the intake/coverage
    /// capability is on while this stays false, and the UI must keep offering the legacy
    /// finalization affordances it offers today. Never true unless
    /// <see cref="PostPaymentCompletionEnabled"/> is also true.
    /// </summary>
    public bool CompletionLifecycleEnabled { get; set; }
}
