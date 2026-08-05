import { SOURCE_DOCUMENT_TYPES, isFiscalDocument, normalizeDocumentType } from './sourceDocumentType';

/** What document extraction proposed, with the evidence behind it. Never applied automatically. */
export interface OcrDocumentClassification {
    suggestedType?: string | null;
    confidence?: number | null;
    titleFound?: string | null;
    supportingEvidence?: string[];
    conflictingEvidence?: string[];
    fiscalMarkers?: string[];
    nonFiscalMarkers?: string[];
    indicatesFiscalDocument?: boolean;
    /** Derived from weak Portal heuristics (prefix/filename), not from reading the document. */
    isFallback?: boolean;
}

/** The user's answer to a conflict, carried alongside the selection until it is saved. */
export interface ClassificationConflictState {
    hasConflict: boolean;
    isHighRisk: boolean;
    acknowledged: boolean;
    justification: string;
}

export const EMPTY_CONFLICT: ClassificationConflictState = {
    hasConflict: false,
    isHighRisk: false,
    acknowledged: false,
    justification: ''
};

/** Minimum characters of written reason for a high-risk override. Mirrors the backend recorder. */
export const CONFLICT_JUSTIFICATION_MIN_LENGTH = 20;

/** Confidence at or above which a plain disagreement already demands a written reason. */
export const HIGH_CONFIDENCE_THRESHOLD = 0.7;

export interface ConflictEvaluation {
    hasConflict: boolean;
    isHighRisk: boolean;
}

/**
 * Whether the extraction actually committed to a type.
 *
 * <p><c>OTHER</c> and <c>UNCLASSIFIED</c> are not opinions — they are the extraction saying it could
 * not place the document in any specific category. Naming a concrete type after that is the user
 * <b>supplying</b> the missing classification, not contradicting one, and treating it as a conflict
 * would demand a written justification for answering a question the system asked.</p>
 */
export function isAuthoritativeSuggestion(suggestion: string | null | undefined): boolean {
    const key = normalizeDocumentType(suggestion);
    return !!key
        && key !== SOURCE_DOCUMENT_TYPES.OTHER
        && key !== SOURCE_DOCUMENT_TYPES.UNCLASSIFIED;
}

/**
 * The user named a type the extraction looked for and could not determine.
 *
 * <p>Not a conflict, but still worth showing and recording: a reading did happen, and it must remain
 * visible that the type attached to this document is not the one it produced.</p>
 *
 * <p>Deliberately requires a suggestion to <b>exist</b>. A document nothing was read from is ordinary
 * data entry — mirroring the backend, which records nothing in that case rather than burying the
 * decisions that matter under thousands that do not.</p>
 */
export function isUserResolvedIndeterminate(
    selected: string | null | undefined,
    ocr?: OcrDocumentClassification | null
): boolean {
    const choice = normalizeDocumentType(selected);
    if (!choice || choice === SOURCE_DOCUMENT_TYPES.UNCLASSIFIED) return false;

    const suggestion = normalizeDocumentType(ocr?.suggestedType);
    return suggestion !== null && !isAuthoritativeSuggestion(suggestion);
}

/**
 * Evaluates a user selection against the extraction's opinion.
 *
 * <p>The rule that matters: choosing a NON-FISCAL type for a document the evidence reads as FISCAL
 * is always high-risk, whatever the numeric confidence. That is exactly the observed defect — an
 * `FT` invoice classified as "Fatura Proforma" — and it is the direction that understates fiscal
 * reality, so it never gets the benefit of the doubt.</p>
 *
 * <p>Mirrors `DocumentClassificationOverrideRecorder.RequiresJustification` on the backend, which
 * re-decides all of this. This copy exists to ask the question at the right moment, not to be
 * trusted.</p>
 */
export function evaluateClassificationConflict(
    selected: string | null | undefined,
    ocr?: OcrDocumentClassification | null
): ConflictEvaluation {
    const suggestion = normalizeDocumentType(ocr?.suggestedType);
    const choice = normalizeDocumentType(selected);

    if (!suggestion || !choice || suggestion === choice) {
        return { hasConflict: false, isHighRisk: false };
    }

    // OTHER / UNCLASSIFIED are the extraction declining to classify. There is no opinion to
    // contradict, so naming a type is an answer, not an override.
    if (!isAuthoritativeSuggestion(suggestion)) {
        return { hasConflict: false, isHighRisk: false };
    }

    const confidence = ocr?.confidence ?? 0;
    const understatesFiscalReality = !!ocr?.indicatesFiscalDocument && !isFiscalDocument(choice);

    return {
        hasConflict: true,
        isHighRisk: understatesFiscalReality || confidence >= HIGH_CONFIDENCE_THRESHOLD
    };
}

/**
 * Whether a conflicting selection may be committed.
 *
 * <p>Acknowledgement is always required; a written reason only when the conflict is high-risk. The
 * conflict modal's confirm button is bound directly to this, so the rule and the button can never
 * disagree.</p>
 */
export function canConfirmConflict(
    evaluation: ConflictEvaluation,
    acknowledged: boolean,
    justification: string
): boolean {
    if (!evaluation.hasConflict) return true;
    if (!acknowledged) return false;
    if (!evaluation.isHighRisk) return true;

    return justification.trim().length >= CONFLICT_JUSTIFICATION_MIN_LENGTH;
}

/** The single reason the confirm button is disabled, or null when it is not. */
export function conflictBlockingReason(
    evaluation: ConflictEvaluation,
    acknowledged: boolean,
    justification: string
): string | null {
    if (!evaluation.hasConflict) return null;
    if (!acknowledged) return 'Confirme que mantém a sua classificação apesar da leitura do documento.';

    if (evaluation.isHighRisk) {
        const written = justification.trim().length;
        if (written < CONFLICT_JUSTIFICATION_MIN_LENGTH) {
            return `Faltam ${CONFLICT_JUSTIFICATION_MIN_LENGTH - written} caracteres na justificativa ` +
                   `(mínimo ${CONFLICT_JUSTIFICATION_MIN_LENGTH}).`;
        }
    }

    return null;
}

/**
 * How the suggestion was reached — recorded because contradicting a document the provider actually
 * read is a stronger act than contradicting a guess made from a filename.
 */
export function classificationSuggestionSource(
    ocr?: OcrDocumentClassification | null
): 'OCR' | 'FALLBACK' | null {
    if (!ocr?.suggestedType) return null;
    return ocr.isFallback ? 'FALLBACK' : 'OCR';
}

/** Everything the API needs to persist and audit a classification decision, for either context. */
export interface ClassificationWirePayload {
    suggestion: string | null;
    confidence: number | null;
    evidenceJson: string | null;
    conflictingEvidenceJson: string | null;
    titleFound: string | null;
    suggestionSource: 'OCR' | 'FALLBACK' | null;
    source: 'OCR_CONFIRMED' | 'USER_SELECTED';
    acknowledged: boolean;
    justification: string | null;
}

/**
 * Shapes the decision for the wire. Built here rather than at each call site so the payment and
 * quotation payloads can never carry a different subset of the same decision.
 */
export function buildClassificationPayload(
    selected: string | null | undefined,
    ocr: OcrDocumentClassification | null | undefined,
    conflict: ClassificationConflictState
): ClassificationWirePayload {
    const choice = normalizeDocumentType(selected);
    const suggestion = normalizeDocumentType(ocr?.suggestedType);
    const justification = conflict.justification.trim();

    return {
        suggestion,
        confidence: ocr?.confidence ?? null,
        evidenceJson: ocr ? JSON.stringify(ocr) : null,
        conflictingEvidenceJson: ocr?.conflictingEvidence?.length
            ? JSON.stringify(ocr.conflictingEvidence) : null,
        titleFound: ocr?.titleFound ?? null,
        suggestionSource: classificationSuggestionSource(ocr),
        source: suggestion !== null && suggestion === choice ? 'OCR_CONFIRMED' : 'USER_SELECTED',
        acknowledged: conflict.acknowledged,
        justification: justification || null
    };
}
