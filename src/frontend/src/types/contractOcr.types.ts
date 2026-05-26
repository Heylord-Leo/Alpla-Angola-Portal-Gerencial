/**
 * Phase 1 Contract OCR — Frontend Type Definitions
 *
 * Source of truth: backend ContractsController.cs OCR endpoints.
 * Do not edit without reconciling against the backend response shapes.
 *
 * DisplayHint values drive the rendering decision in Batches 6–7:
 *   AUTO_FILL      → field pre-populated with amber border; requires Confirmar
 *   SUGGESTION     → chip shown below empty field; user clicks Aplicar
 *   REFERENCE_ONLY → summary panel only; never applied to a form field
 */

// ─── OCR Status ──────────────────────────────────────────────────────────────

/** Lifecycle values mirrored from ContractConstants.OcrStatuses */
export type OcrProcessingStatus = 'PENDING' | 'PROCESSING' | 'COMPLETED' | 'FAILED';

/** How the OCR extracted field is presented in the staging UI */
export type OcrDisplayHint = 'AUTO_FILL' | 'SUGGESTION' | 'REFERENCE_ONLY';

// ─── API Response shapes ──────────────────────────────────────────────────────

/** Returned by GET /contracts/{id}/ocr-status */
export interface OcrStatusResponse {
    contractId: string;
    /** Null when OCR has never been triggered for this contract. */
    ocrStatus: OcrProcessingStatus | null;
    latestRecord: OcrExtractionRecordSummary | null;
}

/** Summary of the most recent ContractOcrExtractionRecord */
export interface OcrExtractionRecordSummary {
    id: string;
    status: OcrProcessingStatus;
    qualityScore: number | null;
    isPartial: boolean;
    conflictsDetected: boolean;
    nativeTextDetected: boolean;
    routingStrategy: string | null;
    chunkCount: number;
    totalTokensUsed: number;
    triggeredAtUtc: string;
    processedAtUtc: string | null;
    errorMessage: string | null;
}

/** Single extracted field row returned inside OcrFieldsResponse */
export interface OcrExtractedField {
    /** ContractOcrExtractedField.Id — use this as the preferred key for ocr-confirm */
    id: string;
    /**
     * Canonical portal form field name — matches the key used in BuildFieldRows on the backend:
     *   AUTO_FILL:      "EffectiveDateUtc", "ExpirationDateUtc", "SignedAtUtc",
     *                   "TotalContractValue", "CurrencyId"
     *   SUGGESTION:     "Title", "CounterpartyName", "GoverningLaw", "PaymentTerms", "SupplierId"
     *   REFERENCE_ONLY: "TerminationClausesText", "SignatoryNames", "ExternalContractReference"
     */
    fieldName: string;
    /** Verbatim string extracted by the LLM before any normalisation */
    rawExtractedValue: string | null;
    /**
     * Parsed/typed value after normalisation:
     *   Dates       → ISO-8601 "YYYY-MM-DD"
     *   Decimal     → numeric string "150000.00"
     *   CurrencyId  → integer string "2"
     *   SupplierId  → integer string "42"
     *   Text fields → truncated plain text
     */
    normalisedValue: string | null;
    /** Per-field confidence score 0.0–1.0 from the LLM */
    confidenceScore: number | null;
    displayHint: OcrDisplayHint;
    /** True after the user clicks Confirmar (persisted via ocr-confirm) */
    confirmedByUser: boolean;
    confirmedAtUtc: string | null;
    /** True if user edited the value before confirming */
    wasOverridden: boolean;
    /** The final value the user accepted (set by ocr-confirm) */
    finalSavedValue: string | null;
    /** True if user explicitly dismissed this field via Limpar / Ignorar */
    discardedByUser: boolean;
}

/** Returned by GET /contracts/{id}/ocr-fields */
export interface OcrFieldsResponse {
    extractionRecordId: string;
    qualityScore: number | null;
    isPartial: boolean;
    conflictsDetected: boolean;
    processedAtUtc: string | null;
    fields: OcrExtractedField[];
}

// ─── ocr-trigger response ─────────────────────────────────────────────────────

/** Returned by POST /contracts/{id}/ocr-trigger (202 Accepted) */
export interface OcrTriggerResponse {
    contractId: string;
    documentId: string;
    status: OcrProcessingStatus;
    message: string;
}

// ─── ocr-confirm request / response ──────────────────────────────────────────

/** Single field decision sent to POST /contracts/{id}/ocr-confirm */
export interface OcrFieldConfirmItem {
    /**
     * Preferred: ContractOcrExtractedField.Id.
     * This is the `id` from OcrExtractedField.
     */
    fieldId?: string;
    /**
     * Fallback when fieldId is not available.
     * Case-insensitive match against FieldName in the database.
     */
    fieldName?: string;
    /** True when the user dismissed the suggestion (Limpar / Ignorar). */
    discarded: boolean;
    /**
     * The value the user accepted. Required when the user edited the
     * OCR-suggested value before confirming (wasOverridden = true).
     * When null/empty, the backend uses the persisted NormalisedValue.
     */
    acceptedValue?: string;
    /** True if the user changed the suggested value before clicking Confirmar. */
    wasOverridden: boolean;
}

/** Request body for POST /contracts/{id}/ocr-confirm */
export interface OcrConfirmRequest {
    fields: OcrFieldConfirmItem[];
}

/** Returned by POST /contracts/{id}/ocr-confirm (200 OK) */
export interface OcrConfirmResponse {
    contractId: string;
    extractionRecordId: string;
    confirmedCount: number;
    discardedCount: number;
    processedAtUtc: string;
    message: string;
}

// ─── Frontend OCR state model ─────────────────────────────────────────────────

/**
 * Per-field state managed in the ContractCreate / ContractEdit component.
 * One entry per OcrExtractedField with displayHint AUTO_FILL or SUGGESTION.
 * REFERENCE_ONLY fields are not tracked here — they go directly to the summary panel.
 */
export interface OcrFieldState {
    /** Maps to OcrExtractedField.id — used when calling ocr-confirm */
    fieldId: string;
    fieldName: string;
    displayHint: OcrDisplayHint;
    /** The normalised value from the backend (the suggested content) */
    normalisedValue: string | null;
    /** Verbatim LLM string — shown in the OCR tooltip */
    rawValue: string | null;
    confidenceScore: number | null;
    /**
     * True after the user clicks Confirmar.
     * When false, the field value is excluded from the save payload
     * (for AUTO_FILL fields in the financial/legal gate set).
     */
    confirmed: boolean;
    /** True after the user clicks Limpar / Ignorar */
    discarded: boolean;
    /**
     * Set when the user edits the value before confirming.
     * This overrides normalisedValue in the save payload and in the confirm call.
     */
    overriddenValue?: string;
}

/**
 * Full OCR state for a contract form session.
 * Keyed by fieldName for O(1) lookups during form rendering.
 */
export type OcrState = Record<string, OcrFieldState>;

/**
 * The set of AUTO_FILL fieldNames that require Confirmar before
 * their values are included in the CreateContractPayload / UpdateContractPayload.
 *
 * These are the "financial/legal gate" fields that trigger the warning modal
 * when unconfirmed at submit time.
 */
export const OCR_GATED_FIELDS = new Set<string>([
    'EffectiveDateUtc',
    'ExpirationDateUtc',
    'SignedAtUtc',
    'TotalContractValue',
    'CurrencyId',
]);

/**
 * Map from backend fieldName to the CreateContractPayload property key.
 * Used to apply SUGGESTION and AUTO_FILL OCR values to form state in ContractCreate.
 *
 * SupplierId is intentionally absent here—it is handled separately by pre-filling
 * the SupplierAutocomplete search text (setSupplierInitialName), never supplierId directly.
 * REFERENCE_ONLY fields have no form key.
 */
export const OCR_FIELD_TO_FORM_KEY: Record<string, string> = {
    // AUTO_FILL
    EffectiveDateUtc:   'effectiveDateUtc',
    ExpirationDateUtc:  'expirationDateUtc',
    SignedAtUtc:        'signedAtUtc',
    TotalContractValue: 'totalContractValue',
    CurrencyId:         'currencyId',
    // SUGGESTION text fields
    Title:              'title',
    CounterpartyName:   'counterpartyName',
    GoverningLaw:       'governingLaw',
    PaymentTerms:       'paymentTerms',
    // Phase 1.1 — derived total suggestion: maps to the same form field as explicit TotalContractValue
    SuggestedTotalContractValue: 'totalContractValue',
    // SupplierId → special case, handled separately via SupplierAutocomplete
    // REFERENCE_ONLY fields (no form key):
    //   TerminationClausesText, SignatoryNames, ExternalContractReference
    //   RecurringMonthlyAmount, ContractDurationMonths, FinancialInconsistencyWarning
};

