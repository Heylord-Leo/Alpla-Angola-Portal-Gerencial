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
}
