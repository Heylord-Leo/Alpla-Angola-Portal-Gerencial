namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Per-field audit record for a contract OCR extraction result.
/// One row per extracted field per extraction run.
///
/// DisplayHint drives frontend rendering:
///   AUTO_FILL     → State A: field pre-populated, amber border, requires Confirmar
///   SUGGESTION    → State B: chip below empty field, user clicks Aplicar
///   REFERENCE_ONLY → Shown in summary panel only; not applied to any form field
/// </summary>
public class ContractOcrExtractedField
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── Parent links ──────────────────────────────────────────────────
    public Guid ExtractionRecordId { get; set; }
    public ContractOcrExtractionRecord ExtractionRecord { get; set; } = null!;

    /// <summary>Denormalised from ExtractionRecord for efficient direct queries.</summary>
    public Guid ContractId { get; set; }

    // ── Field identity ────────────────────────────────────────────────
    /// <summary>
    /// Canonical form field name this row maps to.
    /// Auto-fill fields: "EffectiveDateUtc" | "ExpirationDateUtc" | "SignedAtUtc" |
    ///                   "TotalContractValue" | "CurrencyId"
    /// Suggestion fields: "Title" | "CounterpartyName" | "GoverningLaw" | "PaymentTerms" | "SupplierId"
    /// Reference fields:  "TerminationClausesText" | "SignatoryNames" | "ExternalContractReference"
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    // ── Extracted values ──────────────────────────────────────────────
    /// <summary>Raw unprocessed string from the LLM (as-is, before normalisation).</summary>
    public string? RawExtractedValue { get; set; }

    /// <summary>
    /// Normalised value after parsing (ISO date for dates, cleaned decimal for amounts,
    /// matched ID as string for lookups, truncated text for legal fields).
    /// </summary>
    public string? NormalisedValue { get; set; }

    /// <summary>Per-field confidence score (0.0–1.0). Set by the LLM or post-match normalisation.</summary>
    public decimal? ConfidenceScore { get; set; }

    /// <summary>AUTO_FILL | SUGGESTION | REFERENCE_ONLY</summary>
    public string DisplayHint { get; set; } = "SUGGESTION";

    // ── User confirmation ─────────────────────────────────────────────
    public bool ConfirmedByUser { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public Guid? ConfirmedByUserId { get; set; }

    /// <summary>True if the user changed the suggested value before confirming.</summary>
    public bool WasOverridden { get; set; }

    /// <summary>The value the user ultimately accepted (may differ from NormalisedValue if overridden).</summary>
    public string? FinalSavedValue { get; set; }

    /// <summary>True if the user explicitly dismissed this suggestion (not just left it unconfirmed).</summary>
    public bool DiscardedByUser { get; set; }
}
