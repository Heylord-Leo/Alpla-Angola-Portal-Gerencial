// ─────────────────────────────────────────────────────────────────────────────
// v2.249.4 — P.O. DOCUMENT identification and the P.O. NUMBER field, shared by "Registrar P.O." and "Corrigir P.O."
// (both call direct-ocr and register-po). Pure, node-vitest friendly: no React, no fetch.
//
//   • The VERDICT is the server's (headerSuggestions.purchaseOrderIdentification, computed by the Domain
//     PurchaseOrderDocumentIdentifier over the document's own number + heading, never over body references). The same
//     rule is re-applied by register-po over the extraction facts the modal submits (readPoExtractionFacts), so what the
//     Buyer sees here is what the server enforces:
//         IDENTIFIED_PO          → green only when supplier/amount also match;
//         UNCERTAIN              → explicit confirmation ("é uma Ordem de Compra") + justification (existing override policy);
//         IDENTIFIED_OTHER_TYPE  → blocked, no override (a quotation / proforma / invoice is never a P.O.).
//   • The NUMBER field is a small state machine: EMPTY → EXTRACTED (filled from the document, never over a manual value)
//     → MANUAL (typed) — and, in the correction, CURRENT (the number the group has today, shown next to the proposed one).
//     A new file clears what the previous extraction filled and never touches a manual/current value.
// ─────────────────────────────────────────────────────────────────────────────
import { PRIMAVERA_FAMILY_LABELS, parsePrimaveraPoReference, resolveAutoFillPoNumber } from './ocrPoValidation';

export type PoDocumentVerdict = 'IDENTIFIED_PO' | 'IDENTIFIED_OTHER_TYPE' | 'UNCERTAIN';

export interface PoDocumentCandidate {
    reference: string;
    canonical: string;
    family: string;
    familyLabel: string;
    /** DOCUMENT_NUMBER | TITLE | BODY_REFERENCE */
    source: string;
}

export interface PoDocumentIdentification {
    verdict: PoDocumentVerdict;
    reasonCode: string;
    reason: string;
    family?: string | null;
    familyLabel?: string | null;
    reference?: string | null;
    canonical?: string | null;
    identityCandidates: PoDocumentCandidate[];
    bodyReferences: PoDocumentCandidate[];
    otherTypeCode?: string | null;
    otherTypeLabel?: string | null;
    requiresExplicitConfirmation: boolean;
    isBlocked: boolean;
}

/** The identification when no extraction ran (OCR failed / unavailable): UNCERTAIN, explicit confirmation required. */
export const NO_EXTRACTION_IDENTIFICATION: PoDocumentIdentification = {
    verdict: 'UNCERTAIN',
    reasonCode: 'NO_EXTRACTION',
    reason: 'A extração do documento não está disponível: o tipo e o número da P.O. não foram verificados. Confirme manualmente que o documento é uma Ordem de Compra e introduza o número completo.',
    identityCandidates: [],
    bodyReferences: [],
    requiresExplicitConfirmation: true,
    isBlocked: false,
};

function candidate(raw: any): PoDocumentCandidate {
    return {
        reference: String(raw?.reference ?? ''),
        canonical: String(raw?.canonical ?? ''),
        family: String(raw?.family ?? ''),
        familyLabel: String(raw?.familyLabel ?? PRIMAVERA_FAMILY_LABELS[String(raw?.family ?? '')] ?? ''),
        source: String(raw?.source ?? ''),
    };
}

/**
 * Reads the server verdict from the direct-ocr response. When the response carries none (older backend / truncated
 * envelope) the modal falls back to a conservative local reading: a positively parsed ECF reference in the document
 * number is a P.O., anything else is UNCERTAIN — never "validated".
 */
export function readPoDocumentIdentification(ocrData: any): PoDocumentIdentification {
    const raw = ocrData?.integration?.headerSuggestions?.purchaseOrderIdentification;
    if (raw && typeof raw.verdict === 'string') {
        const verdict = (['IDENTIFIED_PO', 'IDENTIFIED_OTHER_TYPE', 'UNCERTAIN'].includes(raw.verdict) ? raw.verdict : 'UNCERTAIN') as PoDocumentVerdict;
        return {
            verdict,
            reasonCode: String(raw.reasonCode ?? ''),
            reason: String(raw.reason ?? ''),
            family: raw.family ?? null,
            familyLabel: raw.familyLabel ?? null,
            reference: raw.reference ?? null,
            canonical: raw.canonical ?? null,
            identityCandidates: Array.isArray(raw.identityCandidates) ? raw.identityCandidates.map(candidate) : [],
            bodyReferences: Array.isArray(raw.bodyReferences) ? raw.bodyReferences.map(candidate) : [],
            otherTypeCode: raw.otherTypeCode ?? null,
            otherTypeLabel: raw.otherTypeLabel ?? null,
            requiresExplicitConfirmation: verdict === 'UNCERTAIN',
            isBlocked: verdict === 'IDENTIFIED_OTHER_TYPE',
        };
    }
    const number = ocrData?.integration?.headerSuggestions?.documentNumber?.value;
    const parse = parsePrimaveraPoReference(number);
    if (parse) {
        return {
            verdict: 'IDENTIFIED_PO', reasonCode: 'PO_IDENTIFIED',
            reason: `Ordem de Compra identificada: ${parse.display} (${parse.family} — ${PRIMAVERA_FAMILY_LABELS[parse.family] ?? parse.family}).`,
            family: parse.family, familyLabel: PRIMAVERA_FAMILY_LABELS[parse.family] ?? parse.family, reference: parse.display, canonical: parse.canonical,
            identityCandidates: [{ reference: parse.display, canonical: parse.canonical, family: parse.family, familyLabel: PRIMAVERA_FAMILY_LABELS[parse.family] ?? parse.family, source: 'DOCUMENT_NUMBER' }],
            bodyReferences: [], requiresExplicitConfirmation: false, isBlocked: false,
        };
    }
    return { ...NO_EXTRACTION_IDENTIFICATION, reasonCode: 'NO_IDENTIFIER', reason: 'Não foi possível identificar o tipo do documento nem uma referência de Ordem de Compra. Confirme manualmente que o documento é uma P.O. e introduza o número completo.' };
}

/**
 * v2.249.4 — the raw document number the extraction read (for display next to the verdict and as the suggestion). The server
 * never receives extraction facts from the client: `register-po` identifies the document from the evidence it persisted itself.
 */
export function readDetectedDocumentNumber(ocrData: any): string | null {
    const v = ocrData?.integration?.headerSuggestions?.documentNumber?.value;
    return typeof v === 'string' && v.trim() ? v.trim() : null;
}

// ── The P.O. number field ──

export type PoNumberSource = 'EMPTY' | 'EXTRACTED' | 'MANUAL' | 'CURRENT';

export interface PoNumberField {
    value: string;
    source: PoNumberSource;
    /** The number read from the document (null when none / unreadable). Offered, never imposed. */
    suggestion: string | null;
    suggestionFamilyLabel: string | null;
    /** Correction only: the Buyer explicitly chose to KEEP the current number although the document reads another one. */
    currentConfirmed?: boolean;
}

export function initialPoNumberField(current?: string | null): PoNumberField {
    const value = (current ?? '').trim();
    return { value, source: value ? 'CURRENT' : 'EMPTY', suggestion: null, suggestionFamilyLabel: null, currentConfirmed: false };
}

/**
 * What the document says. The field is filled ONLY when it is empty (a manual value — or the current number of a
 * correction — is never overwritten silently); otherwise the suggestion is shown next to the field with "Usar número
 * extraído". The suggestion comes from the positive identification first, then the auto-fill rule (never a NIF).
 */
export function applyExtractedNumber(field: PoNumberField, identification: PoDocumentIdentification | null, rawDocumentNumber: string | null | undefined, knownNifs: Array<string | null | undefined>): PoNumberField {
    let suggestion: string | null = null;
    let familyLabel: string | null = null;
    if (identification?.verdict === 'IDENTIFIED_PO' && identification.reference) {
        suggestion = identification.reference;
        familyLabel = identification.familyLabel ?? null;
    } else if (identification?.identityCandidates.length === 1) {
        suggestion = identification.identityCandidates[0].reference;
        familyLabel = identification.identityCandidates[0].familyLabel;
    } else {
        const auto = resolveAutoFillPoNumber(rawDocumentNumber, knownNifs);
        if (auto) { suggestion = auto.value; familyLabel = auto.parse ? PRIMAVERA_FAMILY_LABELS[auto.parse.family] ?? null : null; }
    }
    if (!suggestion) return { ...field, suggestion: null, suggestionFamilyLabel: null };
    if (field.source === 'EMPTY') return { value: suggestion, source: 'EXTRACTED', suggestion, suggestionFamilyLabel: familyLabel, currentConfirmed: false };
    // A current / manual value is never overwritten; a NEW suggestion reopens the decision of a correction.
    return { ...field, suggestion, suggestionFamilyLabel: familyLabel, currentConfirmed: false };
}

/**
 * Correction: the document reads a number DIFFERENT from the group's current one and the field still holds the current number
 * without an explicit choice. Submission is refused until the Buyer picks "Usar número detetado", types a number, or confirms
 * "Manter número atual" — the stale number is never retained silently (REQ-628). The server adds its own divergence check.
 */
export function numberDecisionPending(field: PoNumberField, current: string | null | undefined): boolean {
    if (!current || !current.trim() || !field.suggestion) return false;
    if (!poNumberChanged(current, field.suggestion)) return false;
    return field.source === 'CURRENT' && !field.currentConfirmed;
}

/** The Buyer explicitly keeps the current number (audited server-side as a divergence override when the document differs). */
export function keepCurrentPoNumber(field: PoNumberField, current: string | null | undefined): PoNumberField {
    return { ...field, value: (current ?? '').trim(), source: 'CURRENT', currentConfirmed: true };
}

export function userEditsPoNumber(field: PoNumberField, value: string, current?: string | null): PoNumberField {
    const trimmed = value.trim();
    if (!trimmed) return { ...field, value, source: 'EMPTY', currentConfirmed: false };
    if (current && trimmed === current.trim()) return { ...field, value, source: 'CURRENT' };   // typing the current number back is NOT the explicit decision
    if (field.suggestion && trimmed === field.suggestion) return { ...field, value, source: 'EXTRACTED' };
    return { ...field, value, source: 'MANUAL' };
}

export function useSuggestedPoNumber(field: PoNumberField): PoNumberField {
    if (!field.suggestion) return field;
    return { ...field, value: field.suggestion, source: 'EXTRACTED' };
}

/** A new file (or a cleared one): the previous extraction no longer applies. Manual and current values survive. */
export function clearExtractedNumber(field: PoNumberField, current?: string | null): PoNumberField {
    if (field.source === 'EXTRACTED') return initialPoNumberField(current);
    return { ...field, suggestion: null, suggestionFamilyLabel: null, currentConfirmed: false };
}

export function poNumberSourceLabel(source: PoNumberSource): string {
    switch (source) {
        case 'EXTRACTED': return 'Extraído do documento';
        case 'MANUAL': return 'Introduzido manualmente';
        case 'CURRENT': return 'Número atual (sem alteração)';
        default: return 'Por preencher';
    }
}

/** True when the correction proposes a different number than the group has today (normalized comparison). */
export function poNumberChanged(current: string | null | undefined, proposed: string): boolean {
    const norm = (v: string | null | undefined) => (v ?? '').trim().toUpperCase().replace(/\s+/g, ' ');
    return norm(current) !== norm(proposed) && norm(proposed).length > 0;
}

// ── The review summary shown instead of the unconditional "Documento Validado" ──

export type PoReviewTone = 'success' | 'warning' | 'blocked';

export interface PoDocumentReview {
    tone: PoReviewTone;
    title: string;
    message: string;
    /** The Buyer must tick "confirmo que é uma Ordem de Compra" and justify. */
    needsTypeConfirmation: boolean;
}

export function describePoDocumentReview(identification: PoDocumentIdentification | null, hasBusinessMismatches: boolean): PoDocumentReview {
    const id = identification ?? NO_EXTRACTION_IDENTIFICATION;
    if (id.verdict === 'IDENTIFIED_OTHER_TYPE') {
        return { tone: 'blocked', title: `Documento identificado como ${id.otherTypeLabel || 'outro tipo'}`, message: id.reason, needsTypeConfirmation: false };
    }
    if (id.verdict === 'UNCERTAIN') {
        return { tone: 'warning', title: 'Revisão necessária: documento não identificado como P.O.', message: id.reason, needsTypeConfirmation: true };
    }
    if (hasBusinessMismatches) {
        return { tone: 'warning', title: `P.O. identificada (${id.reference}) com dados divergentes`, message: 'O documento é uma Ordem de Compra, mas fornecedor / valor / condição não coincidem com o grupo aprovado. Reveja as divergências abaixo.', needsTypeConfirmation: false };
    }
    return { tone: 'success', title: 'Ordem de Compra identificada', message: `${id.reference} — ${id.family} (${id.familyLabel}). Fornecedor e valor correspondem ao grupo aprovado.`, needsTypeConfirmation: false };
}

/** Synthetic-document guidance for the test environment (never affects production validation). */
export const SYNTHETIC_PO_GUIDANCE =
    'Documentos de teste devem usar uma das séries reconhecidas — ECF11 AAAA/NNN (Serviços), ECF10 AAAA/NNN (Materiais diversos) ou ECF AAAA/NNN (Material de stock) — com um sequencial fictício (ex.: ECF11 2026/9003). Um prefixo inventado como "ECF99" não é classificado: o documento fica "não identificado" e exige confirmação manual.';
