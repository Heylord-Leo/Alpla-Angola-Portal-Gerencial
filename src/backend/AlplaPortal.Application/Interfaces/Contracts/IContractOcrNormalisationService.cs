using AlplaPortal.Application.DTOs.Extraction;

namespace AlplaPortal.Application.Interfaces.Contracts;

/// <summary>
/// Converts raw OCR extraction output into normalised, portal-matched typed values.
/// Date strings → DateOnly, currency raw text → matched Currency record,
/// value text → parsed decimal, supplier name → fuzzy-matched Supplier.
/// </summary>
public interface IContractOcrNormalisationService
{
    /// <summary>
    /// Normalises a raw ExtractionContractDto into a typed result ready for staging.
    /// </summary>
    Task<ContractOcrNormalisedResult> NormaliseAsync(
        ExtractionContractDto raw,
        CancellationToken ct = default);
}

/// <summary>
/// Typed, portal-matched output of the normalisation pass.
/// All fields are nullable — null means "not found / low-confidence / no match".
/// </summary>
public record ContractOcrNormalisedResult
{
    // ── Dates (State A — high confidence, auto-fill candidate) ────────────
    public DateOnly? EffectiveDate          { get; init; }
    public decimal?  EffectiveDateConf      { get; init; }

    public DateOnly? ExpirationDate         { get; init; }
    public decimal?  ExpirationDateConf     { get; init; }

    public DateOnly? SignatureDate          { get; init; }
    public decimal?  SignatureDateConf      { get; init; }

    // ── Financial (State A) ───────────────────────────────────────────────
    public decimal?  TotalContractValue     { get; init; }
    public decimal?  TotalContractValueConf { get; init; }

    /// <summary>
    /// Matched Currency.Id from the portal master data. Null if no exact match.
    /// </summary>
    public int?      CurrencyId             { get; init; }
    public string?   CurrencyRaw            { get; init; }
    public decimal?  CurrencyConf           { get; init; }

    // ── Suggestion-only fields (State B) ─────────────────────────────────
    public string?   SuggestedTitle         { get; init; }
    public string?   SuggestedCounterparty  { get; init; }
    public string?   SuggestedGoverningLaw  { get; init; }
    public string?   SuggestedPaymentTerms  { get; init; }

    /// <summary>
    /// 1–2 sentence operational summary of the contract scope/services,
    /// derived from raw.ContractScope and trimmed to 300 chars.
    /// Always SUGGESTION — never AUTO_FILL or gated.
    /// Null when the LLM could not identify a clear object/scope clause.
    /// </summary>
    public string?   SuggestedDescription   { get; init; }

    // ── Phase 1.1 financial derivation fields ─────────────────────────────
    /// <summary>
    /// Derived suggestion: RecurringMonthlyAmountParsed × ContractDurationMonths.
    /// Always SUGGESTION (confidence fixed at 0.60). Null when not applicable.
    /// </summary>
    public decimal?  SuggestedTotalContractValue     { get; init; }
    public decimal?  SuggestedTotalContractValueConf { get; init; }

    /// <summary>Raw recurring monthly amount text for display in the OCR panel.</summary>
    public string?   RecurringMonthlyAmountRaw       { get; init; }
    public decimal?  RecurringMonthlyAmountParsed    { get; init; }
    public int?      ContractDurationMonths          { get; init; }

    /// <summary>
    /// True when the LLM flagged a mismatch between numeric and written-word amounts.
    /// Causes TotalContractValue confidence to be clamped to 0.20 (below AUTO_FILL threshold).
    /// </summary>
    public bool      FinancialInconsistencyDetected  { get; init; }

    // ── Audit metadata ────────────────────────────────────────────────────
    public decimal   QualityScore           { get; init; }
    public bool      HasAnyAutoFillField    { get; init; }
    public bool      HasAnySuggestionField  { get; init; }

    /// <summary>
    /// Best fuzzy supplier match found. Null if confidence below threshold.
    /// The frontend shows this as a suggestion — user must accept explicitly.
    /// </summary>
    public SupplierMatchResult? SupplierMatch { get; init; }
}

public record SupplierMatchResult(int SupplierId, string SupplierName, double MatchScore);
