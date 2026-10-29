import type { RequestDetailsDto } from '../types';
import type { PoDocumentIdentification, PoNumberSource } from './poDocumentIdentification';

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
export type JustificationSource = 'LEGACY_RETURN' | 'OCR_MISMATCH' | 'DOCUMENT_TYPE' | 'NONE';

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
    // v2.249.4
    numberRequired: 'Indique o número da P.O. corrigida: confirme o número atual ou introduza o novo.',
    numberDecisionRequired: 'O documento indica um número de P.O. diferente do atual. Escolha "Usar número detetado", escreva outro número ou confirme "Manter número atual" antes de registar.',
    documentBlocked: 'O documento foi identificado como cotação / proforma / factura e não pode ser registado como P.O. Escolha outro ficheiro.',
    typeConfirmationRequired: 'O documento não foi identificado como Ordem de Compra. Confirme explicitamente que é uma P.O. antes de registrar.',
    typeJustification: 'A justificativa é obrigatória quando o documento não foi identificado automaticamente como P.O.',
    duplicateUnconfirmed: 'Confirme estar ciente da duplicidade de P.O antes de continuar.',
    duplicateJustification: 'Um comentário justificativo é obrigatório para registrar uma P.O. com número duplicado.',
} as const;

export function resolveJustificationPolicy(
    details: Pick<RequestDetailsDto, 'legacyReturnCorrection'> | null | undefined,
    poGroupId: string,
    hasMismatches: boolean,
    /** v2.249.4 — the document was not identified as a P.O. (UNCERTAIN): the explicit confirmation needs a justification. */
    documentTypeUncertain: boolean = false,
): JustificationPolicy {
    const legacy = details?.legacyReturnCorrection;
    if (legacy && legacy.justificationRequired && legacy.poGroupId === poGroupId) {
        return { required: true, source: 'LEGACY_RETURN' };
    }
    if (hasMismatches) return { required: true, source: 'OCR_MISMATCH' };
    if (documentTypeUncertain) return { required: true, source: 'DOCUMENT_TYPE' };
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

    // ── v2.249.4 — P.O. number + document identification ──
    /** The number the correction confirms (current) or replaces. Always sent; the server refuses a blank one (PO_NUMBER_REQUIRED). */
    purchaseOrderNumber?: string;
    purchaseOrderNumberSource?: PoNumberSource;
    /** Server verdict read from the request-scoped extraction (null = OCR did not run). Blocked verdicts never reach the upload. */
    identification?: PoDocumentIdentification | null;
    /** The Buyer ticked "é uma Ordem de Compra" for an UNCERTAIN document. */
    documentTypeConfirmed?: boolean;
    /** Correction: the document reads a different number and the Buyer has not chosen yet (numberDecisionPending). */
    numberDecisionPending?: boolean;
    /** DUPLICATE_PO answered by the server: confirmation + justification on the resubmission. */
    overrideDuplicateConfirmed?: boolean;
    duplicateOverrideComment?: string;
    /** The attachment id of the file already uploaded by a refused attempt (sent as poAttachmentId when the upload is skipped). */
    uploadedAttachmentId?: string | null;
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
    // v2.249.4 — the identification itself is never sent: the server reads its own evidence (po-document-extract) by file hash.
    purchaseOrderNumber: string;
    purchaseOrderNumberSource?: PoNumberSource;
    documentTypeConfirmed?: boolean;
    overrideDuplicateConfirmed?: boolean;
    duplicateOverrideComment?: string;
    poAttachmentId?: string;
}

export function validateCorrectPoSubmission(input: CorrectPoSubmissionInput): string | null {
    // Without the detail payload the mandatory-justification decision is unknown: never submit (the upload would
    // happen before the backend refusal and leave an orphan P.O. attachment).
    if (input.detailsUnavailable) return CORRECT_PO_MESSAGES.detailsUnavailable;
    if (input.groupMissing) return CORRECT_PO_MESSAGES.groupMissing;
    if (!input.file) return CORRECT_PO_MESSAGES.fileRequired;
    // v2.249.4 — the document and the number, BEFORE any upload (a refused document must never leave an orphan P.O. file).
    if (input.identification?.isBlocked) return CORRECT_PO_MESSAGES.documentBlocked;
    if (!(input.purchaseOrderNumber ?? '').trim()) return CORRECT_PO_MESSAGES.numberRequired;
    if (input.numberDecisionPending) return CORRECT_PO_MESSAGES.numberDecisionRequired;
    if (input.identification?.requiresExplicitConfirmation && !input.documentTypeConfirmed) return CORRECT_PO_MESSAGES.typeConfirmationRequired;
    if (input.ocrResult?.hasMismatches && !input.overrideConfirmed) return CORRECT_PO_MESSAGES.mismatchUnconfirmed;
    if (input.overrideDuplicateConfirmed && normalizeJustification(input.duplicateOverrideComment) === undefined) return CORRECT_PO_MESSAGES.duplicateJustification;
    if (input.justification.required && normalizeJustification(input.comment) === undefined) {
        return input.justification.source === 'LEGACY_RETURN'
            ? CORRECT_PO_MESSAGES.legacyJustification
            : input.justification.source === 'DOCUMENT_TYPE'
                ? CORRECT_PO_MESSAGES.typeJustification
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
        // v2.249.4 — the number the correction confirms or replaces (never silently retained), the document facts the
        // server re-evaluates, the explicit type confirmation, the duplicate confirmation and the uploaded file id.
        purchaseOrderNumber: (input.purchaseOrderNumber ?? '').trim(),
        purchaseOrderNumberSource: input.purchaseOrderNumberSource,
        documentTypeConfirmed: !!input.documentTypeConfirmed,
        ...(input.overrideDuplicateConfirmed ? { overrideDuplicateConfirmed: true, duplicateOverrideComment: normalizeJustification(input.duplicateOverrideComment) } : {}),
        ...(input.uploadedAttachmentId ? { poAttachmentId: input.uploadedAttachmentId } : {}),
    };
}

export interface CorrectPoSubmissionDeps {
    upload: (requestId: string, files: File[], attachmentTypeCode: string, poGroupId: string) => Promise<unknown>;
    registerPo: (requestId: string, payload: RegisterPoPayload) => Promise<{ message?: string } | undefined>;
    /** v2.249.4 — called with the attachment id of the file just uploaded (so a refused register-po can be retried without a second upload). */
    onUploaded?: (attachmentId: string | null) => void;
}

/** The upload endpoint answers `[{ id, fileName }]`; anything else yields null (the server then falls back to the newest live file). */
export function readUploadedAttachmentId(uploadResult: unknown): string | null {
    const first = Array.isArray(uploadResult) ? uploadResult[0] : null;
    const id = first && typeof first === 'object' ? (first as { id?: unknown }).id : null;
    return typeof id === 'string' && id ? id : null;
}

export type CorrectPoSubmissionOutcome =
    | { ok: true; message: string }
    | { ok: false; error: string };

/** Validates, uploads the new P.O. file for the group and registers the P.O. — in that order, or not at all. */
export async function submitCorrectPo(deps: CorrectPoSubmissionDeps, input: CorrectPoSubmissionInput): Promise<CorrectPoSubmissionOutcome> {
    const error = validateCorrectPoSubmission(input);
    if (error) return { ok: false, error };
    let uploadedAttachmentId = input.uploadedAttachmentId ?? null;
    if (!input.skipUpload) {
        const uploaded = await deps.upload(input.requestId, [input.file as File], 'PO', input.poGroupId);
        uploadedAttachmentId = readUploadedAttachmentId(uploaded);
        deps.onUploaded?.(uploadedAttachmentId);
    }
    const result = await deps.registerPo(input.requestId, buildRegisterPoPayload({ ...input, uploadedAttachmentId }));
    return { ok: true, message: result?.message || CORRECT_PO_MESSAGES.success };
}
