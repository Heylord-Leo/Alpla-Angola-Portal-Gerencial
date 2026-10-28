import type { RequestDetailsDto } from '../types';

/**
 * docs §G.19.3 — validation, payload and submission of the "Corrigir P.O" modal, kept framework-free so the real
 * payload path is unit-tested.
 *
 * Rules:
 * - The modal never fabricates a justification. A blank comment is sent as ABSENT (`comment` undefined); the
 *   generated text that used to replace it ("P.O corrigida após devolução por Finanças.") no longer exists.
 * - Whether the justification is mandatory is decided by the backend for the legacy request-level Finance return
 *   (`RequestDetailsDto.legacyReturnCorrection`, same predicate RegisterPo enforces) and, as before, by an
 *   unresolved OCR divergence. Current-code corrections keep the comment optional.
 * - When mandatory, null/empty/whitespace is refused BEFORE the P.O. upload, so nothing is written.
 */
export type JustificationSource = 'LEGACY_RETURN' | 'OCR_MISMATCH' | 'NONE';

export interface JustificationPolicy {
    required: boolean;
    source: JustificationSource;
}

export const CORRECT_PO_MESSAGES = {
    detailsUnavailable: 'Não foi possível carregar os dados do pedido. Atualize a página e tente novamente.',
    groupMissing: 'Não foi possível carregar os dados do grupo P.O. selecionado. Atualize a página e tente novamente.',
    fileRequired: 'É obrigatório anexar o novo documento de P.O em formato PDF.',
    mismatchUnconfirmed: 'Confirme estar ciente das divergências antes de registrar.',
    mismatchJustification: 'Um comentário justificativo é obrigatório quando há divergências.',
    legacyJustification: 'A justificativa do Comprador é obrigatória para corrigir uma P.O devolvida por Finanças.',
    success: 'P.O corrigida e registrada com sucesso!',
} as const;

export function resolveJustificationPolicy(
    details: Pick<RequestDetailsDto, 'legacyReturnCorrection'> | null | undefined,
    poGroupId: string,
    hasMismatches: boolean,
): JustificationPolicy {
    const legacy = details?.legacyReturnCorrection;
    if (legacy && legacy.justificationRequired && legacy.poGroupId === poGroupId) {
        return { required: true, source: 'LEGACY_RETURN' };
    }
    if (hasMismatches) return { required: true, source: 'OCR_MISMATCH' };
    return { required: false, source: 'NONE' };
}

/** Trimmed Buyer text, or undefined when there is none. Never a generated default. */
export function normalizeJustification(raw: string | null | undefined): string | undefined {
    const text = (raw ?? '').trim();
    return text.length > 0 ? text : undefined;
}

export interface CorrectPoOcrResult {
    hasMismatches: boolean;
    details: string[];
    extractedTotal?: number;
    extractedSupplier?: string;
}

export interface CorrectPoSubmissionInput {
    requestId: string;
    poGroupId: string;
    /** The modal's own detail fetch failed: the server decision (legacy return, group context) is unknown. */
    detailsUnavailable: boolean;
    groupMissing: boolean;
    file: File | null;
    comment: string;
    ocrResult: CorrectPoOcrResult | null;
    overrideConfirmed: boolean;
    paymentCondition: string;
    advancePercent: number;
    paymentConditionSource: 'OCR_DETECTED' | 'USER_SELECTED' | '';
    justification: JustificationPolicy;
    /**
     * Phase 1 P.O. cancellation (D15): set only when the previous attempt was refused with 400
     * PREVIOUS_PO_NUMBER_REUSED and the Buyer confirmed + explained the intentional reuse. The resubmission sends
     * the SAME payload plus previousPoNumberReuseConfirmed=true and this comment.
     */
    previousPoNumberReuseComment?: string;
    /** The file of this attempt was already uploaded by the refused attempt: do not upload it twice. */
    skipUpload?: boolean;
}

export interface RegisterPoPayload {
    poGroupId: string;
    comment?: string;
    hasMismatches: boolean;
    overrideConfirmed: boolean;
    mismatchDetails: string;
    extractedTotalAmount?: number;
    extractedSupplierName?: string;
    paymentConditionCode: string;
    advancePaymentPercent?: number;
    paymentConditionSource: string;
    previousPoNumberReuseConfirmed?: boolean;
    previousPoNumberReuseComment?: string;
}

export function validateCorrectPoSubmission(input: CorrectPoSubmissionInput): string | null {
    // Without the detail payload the mandatory-justification decision is unknown: never submit (the upload would
    // happen before the backend refusal and leave an orphan P.O. attachment).
    if (input.detailsUnavailable) return CORRECT_PO_MESSAGES.detailsUnavailable;
    if (input.groupMissing) return CORRECT_PO_MESSAGES.groupMissing;
    if (!input.file) return CORRECT_PO_MESSAGES.fileRequired;
    if (input.ocrResult?.hasMismatches && !input.overrideConfirmed) return CORRECT_PO_MESSAGES.mismatchUnconfirmed;
    if (input.justification.required && normalizeJustification(input.comment) === undefined) {
        return input.justification.source === 'LEGACY_RETURN'
            ? CORRECT_PO_MESSAGES.legacyJustification
            : CORRECT_PO_MESSAGES.mismatchJustification;
    }
    return null;
}

export function buildRegisterPoPayload(input: CorrectPoSubmissionInput): RegisterPoPayload {
    const { ocrResult } = input;
    return {
        poGroupId: input.poGroupId,
        comment: normalizeJustification(input.comment),
        hasMismatches: ocrResult?.hasMismatches || false,
        overrideConfirmed: input.overrideConfirmed,
        mismatchDetails: ocrResult?.details ? ocrResult.details.join('; ') : '',
        // v2.242.0 — forward the ACTUAL OCR extraction so the backend recomputes divergence against the
        // persisted RequestPoGroup; only real extracted values are sent (no currency in this flow).
        extractedTotalAmount: ocrResult?.extractedTotal,
        extractedSupplierName: ocrResult?.extractedSupplier || undefined,
        paymentConditionCode: input.paymentCondition,
        advancePaymentPercent: input.paymentCondition === 'ADVANCE_PARTIAL' ? input.advancePercent : undefined,
        paymentConditionSource: input.paymentConditionSource || 'USER_SELECTED',
        ...(input.previousPoNumberReuseComment && input.previousPoNumberReuseComment.trim().length > 0
            ? { previousPoNumberReuseConfirmed: true as const, previousPoNumberReuseComment: input.previousPoNumberReuseComment.trim() }
            : {}),
    };
}

export interface CorrectPoSubmissionDeps {
    upload: (requestId: string, files: File[], attachmentTypeCode: string, poGroupId: string) => Promise<unknown>;
    registerPo: (requestId: string, payload: RegisterPoPayload) => Promise<{ message?: string } | undefined>;
}

export type CorrectPoSubmissionOutcome =
    | { ok: true; message: string }
    | { ok: false; error: string };

/** Validates, uploads the new P.O. file for the group and registers the P.O. — in that order, or not at all. */
export async function submitCorrectPo(deps: CorrectPoSubmissionDeps, input: CorrectPoSubmissionInput): Promise<CorrectPoSubmissionOutcome> {
    const error = validateCorrectPoSubmission(input);
    if (error) return { ok: false, error };
    if (!input.skipUpload) await deps.upload(input.requestId, [input.file as File], 'PO', input.poGroupId);
    const result = await deps.registerPo(input.requestId, buildRegisterPoPayload(input));
    return { ok: true, message: result?.message || CORRECT_PO_MESSAGES.success };
}
