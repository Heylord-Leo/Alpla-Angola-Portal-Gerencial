namespace AlplaPortal.Application.DTOs.Extraction;

public class ExtractionHeaderDto
{
    public string? SupplierName { get; set; }
    public string? SupplierTaxId { get; set; }
    public string? BilledCompanyName { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentDate { get; set; }
    public string? Currency { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? GrandTotal { get; set; }
    public decimal? DiscountAmount { get; set; }
}

public class ExtractionLineItemDto
{
    public int LineNumber { get; set; }
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? TaxRate { get; set; }
}

public class ExtractionMetadataDto
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int PagesProcessed { get; set; }
    public int ProcessingDpi { get; set; }
    public string ProcessingFormat { get; set; } = string.Empty;
    public string RoutingStrategy { get; set; } = string.Empty;
    public string DetailMode { get; set; } = "auto";
    public bool NativeTextDetected { get; set; }
    
    // Phase 3 Extensions
    public int ChunkCount { get; set; }
    public bool IsPartial { get; set; }
    public bool ConflictsDetected { get; set; }
}

/// <summary>
/// Phase 1 OCR extraction output for a contract document.
/// All string values are raw text from the LLM. Parsing and normalisation are
/// performed by ContractOcrNormalisationService before persistence.
/// Per-field confidence scores (0.0–1.0) drive auto-fill vs. suggestion decisions.
/// Auto-fill threshold: ≥ 0.70 (ContractOcrNormalisationService.AutoFillThreshold)
/// Supplier fuzzy match min: ≥ 0.82 Jaro-Winkler (SupplierFuzzyThreshold)
/// Currency match: exact alias/code lookup — no score, matched or not.
/// </summary>
public class ExtractionContractDto
{
    // ── Identification ────────────────────────────────────────────────
    /// <summary>Original classification hint from triage. "CONTRACT" expected.</summary>
    public string? DocumentType { get; set; }

    /// <summary>External contract reference number (from the counterparty's document).</summary>
    public string? ExternalContractReference { get; set; }

    // ── Parties ───────────────────────────────────────────────────────
    /// <summary>Raw extracted name of the primary contracting party (supplier / counterparty).</summary>
    public string? SupplierName { get; set; }

    /// <summary>Legacy "Parties" field — kept for backward compatibility with existing mapper paths.</summary>
    public string? Parties { get; set; }

    /// <summary>Tax ID / NIF extracted for the supplier. Used as a high-confidence matching signal.</summary>
    public string? SupplierTaxId { get; set; }

    /// <summary>Names of signatories near the signature block (semicolon-separated if multiple).</summary>
    public string? SignatoryNames { get; set; }

    // ── Dates (raw strings — normalised by ContractOcrNormalisationService) ──
    /// <summary>Contract effective / start date. Expected ISO-8601 or best-effort parse string.</summary>
    public string? EffectiveDate { get; set; }
    public decimal? EffectiveDateConfidence { get; set; }

    /// <summary>Contract expiry / end date.</summary>
    public string? EndDate { get; set; }
    public decimal? EndDateConfidence { get; set; }

    /// <summary>Date of signature.</summary>
    public string? SignatureDate { get; set; }
    public decimal? SignatureDateConfidence { get; set; }

    // ── Financial ─────────────────────────────────────────────────────
    /// <summary>
    /// Total contract value as raw text (e.g. "USD 150,000.00" or "150000").
    /// Only the global total should be extracted — not instalments or subtotals.
    /// </summary>
    public string? TotalContractValue { get; set; }
    public decimal? TotalContractValueConfidence { get; set; }

    /// <summary>Raw currency text (e.g. "USD", "Kwanza", "€"). Resolved to CurrencyId by normalisation.</summary>
    public string? CurrencyRaw { get; set; }
    public decimal? CurrencyConfidence { get; set; }

    // ── Legal ─────────────────────────────────────────────────────────
    /// <summary>Governing law clause text (truncated to 200 chars at normalisation).</summary>
    public string? GoverningLaw { get; set; }
    public decimal? GoverningLawConfidence { get; set; }

    /// <summary>Raw payment terms / conditions text extracted from the document.</summary>
    public string? PaymentTerms { get; set; }

    /// <summary>Termination clause extract — reference only, never auto-filled into the form.</summary>
    public string? TerminationClauses { get; set; }

    /// <summary>
    /// 1–2 sentence summary of the contract object/service scope clause,
    /// generated by the LLM directly from the object or scope-of-work clause.
    /// Never includes payment, termination, or legal boilerplate text.
    /// Normalised into SuggestedDescription (max 300 chars) by ContractOcrNormalisationService.
    /// Always rendered as SUGGESTION in the OCR panel — never AUTO_FILL.
    /// </summary>
    public string? ContractScope { get; set; }

    // ── Supplier confidence ───────────────────────────────────────────
    /// <summary>
    /// Confidence that SupplierName + SupplierTaxId match a portal Supplier record.
    /// Set by ContractOcrNormalisationService after fuzzy matching, not by the LLM.
    /// </summary>
    public decimal? SupplierMatchConfidence { get; set; }

    /// <summary>
    /// Portal SupplierId if a match was found above threshold. Null otherwise.
    /// Set by ContractOcrNormalisationService.
    /// </summary>
    public int? MatchedSupplierId { get; set; }

    // ── Recurring / periodic amount (Phase 1.1 financial rules) ──────────
    /// <summary>
    /// Per-month amount as raw text extracted by the LLM (e.g. "770,330.00 Kz").
    /// Only present when the contract specifies a recurring monthly amount rather than
    /// (or in addition to) a global total.
    /// </summary>
    public string?  RecurringMonthlyAmount           { get; set; }
    public decimal? RecurringMonthlyAmountConfidence { get; set; }

    /// <summary>
    /// Number of months found in the contract term clause (e.g. 12).
    /// Combined with RecurringMonthlyAmount to derive SuggestedTotalContractValue.
    /// </summary>
    public int?     ContractDurationMonths           { get; set; }
    public decimal? ContractDurationMonthsConfidence { get; set; }

    /// <summary>
    /// Amount written in words as it appears in the document (e.g. "nine hundred and eighty
    /// thousand three hundred and thirty kwanzas"). Used only for inconsistency detection.
    /// </summary>
    public string?  WrittenAmountText                { get; set; }

    /// <summary>
    /// True when the LLM detected a discrepancy between the numeric amount and the written-word
    /// amount in the same clause. Triggers confidence penalty and panel warning in normalisation.
    /// </summary>
    public bool     WrittenAmountInconsistencyDetected { get; set; }
}


public class ExtractionResultDto
{
    public bool Success { get; set; }
    public ExtractionHeaderDto Header { get; set; } = new();
    public List<ExtractionLineItemDto> Items { get; set; } = new();
    public ExtractionContractDto? Contract { get; set; }
    public string? ProviderName { get; set; }
    public decimal QualityScore { get; set; }
    public ExtractionMetadataDto Metadata { get; set; } = new();
}

