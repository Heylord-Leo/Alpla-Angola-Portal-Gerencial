import { OcrDocumentClassificationDto } from './index';

/**
 * The legacy OCR envelope returned by <c>POST /api/v1/requests/direct-ocr</c> and
 * <c>POST /api/v1/requests/{id}/ocr-extract</c>, as produced by
 * <c>ExtractionMapper.MapToLegacyOcrResult</c>.
 *
 * <p><b>The endpoint answers HTTP 200 even when the extraction failed.</b> A blocked module, a
 * disabled provider, an unreadable file and a provider timeout all come back as
 * <code>200 OK</code> carrying <code>success: false</code> and <code>status.code: "ERROR"</code>,
 * with every field null and zero tokens. That contract is shared with the PO-registration modals
 * and the single-document payment flow, so it is not changed here — but <b>HTTP 200 alone must
 * never be read as a successful reading</b>. Check {@link OcrExtractionEnvelope.success}.</p>
 */
export interface OcrExtractionEnvelope {
    success: boolean;
    status?: OcrEnvelopeStatus | null;
    integration?: OcrIntegration | null;
    metadata?: Record<string, unknown> | null;
}

export interface OcrEnvelopeStatus {
    /** `"ok"` when the extraction ran; `"ERROR"` or `"PORTAL_ERROR"` when it did not. */
    code?: string | null;
    qualityScore?: number | null;
}

/** Every scalar arrives wrapped with the confidence label the extraction assigned it. */
export interface OcrValue<T> {
    value?: T | null;
    status?: string | null;
}

export interface OcrIntegration {
    headerSuggestions?: OcrHeaderSuggestions | null;
    lineItemSuggestions?: OcrLineItemSuggestion[] | null;
    reviewRequired?: boolean | null;
    lineItemsRequireReview?: boolean | null;
}

export interface OcrHeaderSuggestions {
    supplierName?: OcrValue<string> | null;
    supplierTaxId?: OcrValue<string> | null;
    billedCompany?: OcrValue<string> | null;
    /** The CUSTOMER's fiscal number, kept distinct from the supplier's. */
    billedCompanyTaxId?: OcrValue<string> | null;
    documentNumber?: OcrValue<string> | null;
    date?: OcrValue<string> | null;
    documentDate?: OcrValue<string> | null;
    dueDate?: OcrValue<string> | null;
    currencyCode?: OcrValue<string> | null;
    currency?: OcrValue<string> | null;
    totalAmount?: OcrValue<number> | null;
    grandTotal?: OcrValue<number> | null;
    netAmount?: OcrValue<number> | null;
    taxAmount?: OcrValue<number> | null;
    ivaAmount?: OcrValue<number> | null;
    discountAmount?: OcrValue<number> | null;

    /**
     * What the extraction believes the document is, with its evidence. A proposal only — never
     * written into the user's selection.
     *
     * <p>Present even on a failed extraction, because the fallback classifier can guess from the
     * filename alone. Such a guess describes the <i>name of the file</i>, not the document, and must
     * not be shown as a reading.</p>
     */
    documentClassification?: OcrDocumentClassificationDto | null;
}

export interface OcrLineItemSuggestion {
    description?: string | null;
    quantity?: number | null;
    unit?: string | null;
    unitPrice?: number | null;
    discountAmount?: number | null;
    discountPercent?: number | null;
    totalAmount?: number | null;
    taxRate?: number | null;
    status?: string | null;
}

/**
 * The OCR module this extraction is governed by.
 *
 * <p><code>sourceContext</code> is <b>an allowlist key looked up against `OcrModuleConfigs`</b>, not
 * a free-text hint: <c>DocumentExtractionService</c> refuses the extraction outright when the value
 * does not match a configured, enabled module row. The seeded modules are <code>REQUESTS</code>
 * ("Requests &amp; Buy2Pay") and <code>CONTRACTS</code>.</p>
 *
 * <p>A PAYMENT request is a Request, and the single-document payment path already extracts under
 * <code>REQUESTS</code>. Using it here keeps the admin allowlist meaningful — disabling the Requests
 * module now correctly disables multi-document payment OCR too — which passing no context at all
 * would silently bypass.</p>
 */
export const OCR_MODULE_REQUESTS = 'REQUESTS';

/** True only when the extraction actually ran and produced a reading. */
export function isSuccessfulExtraction(
    envelope: OcrExtractionEnvelope | null | undefined
): envelope is OcrExtractionEnvelope {
    return !!envelope && envelope.success === true;
}

/**
 * A safe, user-facing reason for a failed extraction.
 *
 * <p>Never surfaces a stack trace, a provider name or a configuration detail — the envelope carries
 * no safe message of its own, and the useful diagnosis lives in the server's admin log under
 * `OCR_MODULE_BLOCKED` / `OCR_DOCUMENT_TYPE_BLOCKED`.</p>
 */
export function extractionFailureMessage(
    envelope: OcrExtractionEnvelope | null | undefined
): string {
    const base = 'Não foi possível extrair os dados deste documento.';

    return envelope?.status?.code === 'PORTAL_ERROR'
        ? `${base} Ocorreu um erro no servidor. Tente novamente ou preencha os dados manualmente.`
        : `${base} Tente novamente ou preencha os dados manualmente.`;
}
