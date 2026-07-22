/**
 * Shared, pure logic for the "Emitir P.O Sistemica" / "Corrigir P.O" OCR validation flow
 * (RegisterPoModal.tsx / CorrectPoModal.tsx). Extracted so both modals use one implementation
 * instead of two independently-duplicated copies, and so the logic can be unit-tested without
 * a component-rendering test runner.
 */

export const GENERIC_OCR_FAILURE_MESSAGE =
    'Falha ao extrair dados do OCR. Documento ilegível ou em formato não suportado.';

export const CLIENT_PROCESSING_ERROR_MESSAGE =
    'Os dados foram extraídos do documento, mas ocorreu um erro ao processar o resultado.';

export function normalizeForComparison(s: string): string {
    return s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
}

/**
 * Robust supplier name similarity: case-insensitive, accent-insensitive, token-based.
 * Returns `null` — not a numeric score — whenever a comparison is not meaningful: either input
 * is missing/blank, or either side has no alphanumeric content once stripped (e.g. a placeholder
 * like '---'). This deliberately avoids the previous 'includes(\'\')' pitfall, where comparing
 * against an empty stripped string was vacuously true and scored as a 0.9 near-match — silently
 * treating "no expected supplier" as a match. Two genuinely empty values are never a match.
 */
export function calculateSimilarity(a: string | null | undefined, b: string | null | undefined): number | null {
    if (typeof a !== 'string' || typeof b !== 'string') return null;
    const trimmedA = a.trim();
    const trimmedB = b.trim();
    if (trimmedA.length === 0 || trimmedB.length === 0) return null;

    const na = normalizeForComparison(trimmedA);
    const nb = normalizeForComparison(trimmedB);

    // Exact match after normalization (covers pure case differences)
    const stripped1 = na.replace(/[^a-z0-9]/g, '');
    const stripped2 = nb.replace(/[^a-z0-9]/g, '');
    if (stripped1.length === 0 || stripped2.length === 0) return null;
    if (stripped1 === stripped2) return 1.0;
    if (stripped1.includes(stripped2) || stripped2.includes(stripped1)) return 0.9;

    // Token-based overlap: compare word-by-word
    const tokens1 = na.replace(/[^a-z0-9\s]/g, '').split(/\s+/).filter(t => t.length > 1);
    const tokens2 = nb.replace(/[^a-z0-9\s]/g, '').split(/\s+/).filter(t => t.length > 1);
    if (tokens1.length === 0 || tokens2.length === 0) return 0.0;

    const set2 = new Set(tokens2);
    const matchCount = tokens1.filter(t => set2.has(t)).length;
    return matchCount / Math.max(tokens1.length, tokens2.length);
}

export const NOT_DEFINED_SUPPLIER_DISPLAY = 'Não definido';

/**
 * requestData.supplierName can be null/undefined/blank at runtime even where its prop type
 * claims otherwise. Returns the trimmed comparison value, or `null` when there is none — this is
 * the comparison value, never a display placeholder (see resolveSupplierDisplay for that).
 */
export function resolveExpectedSupplierName(supplierName: string | null | undefined): string | null {
    if (typeof supplierName !== 'string') return null;
    const trimmed = supplierName.trim();
    return trimmed.length > 0 ? trimmed : null;
}

/** UI-only display text for an expected supplier that may be unset — never fed back into a comparison. */
export function resolveSupplierDisplay(expectedSupplierName: string | null): string {
    return expectedSupplierName ?? NOT_DEFINED_SUPPLIER_DISPLAY;
}

/** requestData.totalAmount can be non-numeric at runtime even where its prop type claims otherwise. */
export function resolveExpectedTotalAmount(totalAmount: unknown): number {
    return typeof totalAmount === 'number' ? totalAmount : 0;
}

export interface OcrHeaderSuggestions {
    extractedTotal: number;
    extractedSupplier: string;
    extractedPoNumber: string;
    paymentCondition: string | undefined;
    advancePercent: number | undefined;
}

/**
 * Reads the direct-ocr response shape: { integration: { headerSuggestions: { ... } } }.
 * PO number is sourced from documentNumber - the backend DTO has no purchaseOrderNumber field.
 */
export function extractOcrHeaderSuggestions(ocrData: any): OcrHeaderSuggestions {
    const suggestions = ocrData?.integration?.headerSuggestions;
    return {
        extractedTotal: Number(suggestions?.grandTotal?.value) || 0,
        extractedSupplier: suggestions?.supplierName?.value || '',
        extractedPoNumber: suggestions?.documentNumber?.value || '',
        paymentCondition: suggestions?.paymentCondition?.value,
        advancePercent: suggestions?.paymentConditionAdvancePercent?.value,
    };
}

export interface OcrMismatchResult {
    hasMismatches: boolean;
    details: string[];
}

/**
 * Math check (tolerance 1 unit due to weird IVA roundings on ERPs) + supplier-name similarity
 * check. `expectedSupplierName` is the comparison value (see resolveExpectedSupplierName) — when
 * it is `null` (the group has no expected supplier defined), this is reported as an explicit
 * "not comparable" divergence, never as a silent match.
 */
export function buildOcrMismatchResult(
    extractedTotal: number,
    expectedTotalAmount: number,
    extractedSupplier: string,
    expectedSupplierName: string | null
): OcrMismatchResult {
    const details: string[] = [];
    let hasMismatches = false;

    if (Math.abs(extractedTotal - expectedTotalAmount) > 1.0) {
        details.push(`Total divergente: Identificado ${extractedTotal.toFixed(2)} (Esperado: ${expectedTotalAmount.toFixed(2)})`);
        hasMismatches = true;
    }

    if (!extractedSupplier) {
        details.push('Fornecedor não identificado claramente no documento.');
        hasMismatches = true;
    } else if (expectedSupplierName === null) {
        details.push('Não foi possível comparar o fornecedor extraído porque o grupo não possui fornecedor esperado definido.');
        details.push(`Fornecedor extraído: "${extractedSupplier}"`);
        details.push(`Fornecedor esperado: "${resolveSupplierDisplay(expectedSupplierName)}"`);
        hasMismatches = true;
    } else {
        const sim = calculateSimilarity(extractedSupplier, expectedSupplierName);
        if (sim === null || sim < 0.6) {
            details.push(`Fornecedor divergente: Identificado "${extractedSupplier}" (Esperado: "${expectedSupplierName}")`);
            hasMismatches = true;
        }
    }

    return { hasMismatches, details };
}

/**
 * Resolves the message(s) shown for a transport/network/backend-declared error (a rejected
 * directOcrExtract call) - as opposed to a client-side exception while processing a successful
 * response, which must use CLIENT_PROCESSING_ERROR_MESSAGE instead (see runOcrValidation).
 */
export function resolveTransportErrorDetails(err: any): string[] {
    const details = [err?.detail || err?.message || GENERIC_OCR_FAILURE_MESSAGE];
    if (err?.correlationId) details.push(`Ref: ${err.correlationId}`);
    return details;
}
