// ─────────────────────────────────────────────────────────────────────────────
// Phase 1 P.O. cancellation — decision logic for the register / correct P.O. modals when the backend answers
// with one of the cancellation-related ProblemDetails (pure, node-vitest friendly):
//   • 409 PRIOR_ECF_CONFIRMATION_PENDING (from POST attachments/upload OR POST operational/register-po): a
//     replacement P.O. is blocked until Finance confirms the previous ECF annulment → blocking notice, no retry.
//   • 400 PREVIOUS_PO_NUMBER_REUSED: the new number is the number of a previous CANCELLED P.O. of the same lines →
//     warning + mandatory explanation (≥ 20) + explicit checkbox, then resubmit the SAME payload with
//     previousPoNumberReuseConfirmed=true and previousPoNumberReuseComment.
// The existing DUPLICATE_PO / OCR_MISMATCH branches are classified here too so every branch reads the error
// the same way: ApiError exposes the ProblemDetails as `details` (title/detail/extensions) and the `code`
// extension as `errorCode`.
// ─────────────────────────────────────────────────────────────────────────────
import { MIN_JUSTIFICATION_LENGTH } from './poGroupCancellation';

export type RegisterPoErrorKind = 'PRIOR_ECF_CONFIRMATION_PENDING' | 'PREVIOUS_PO_NUMBER_REUSED' | 'DUPLICATE_PO' | 'OCR_MISMATCH' | 'OTHER';

export interface RegisterPoErrorDecision {
    kind: RegisterPoErrorKind;
    /** Human text to show (server detail when present). */
    message: string;
    /** PREVIOUS_PO_NUMBER_REUSED extensions. */
    cancellationId?: string | null;
    previousPoGroupId?: string | null;
    /** PRIOR_ECF_CONFIRMATION_PENDING extension (prior cancellations, informational). */
    cancellations?: unknown[];
}

interface ErrorLike {
    message?: string;
    status?: number;
    errorCode?: string;
    code?: string;
    title?: string;
    detail?: string;
    details?: { title?: string; detail?: string; code?: string; cancellationId?: string; previousPoGroupId?: string; cancellations?: unknown[] } | null;
}

export const REGISTER_PO_ERROR_MESSAGES = {
    priorEcfBlocked: 'Registo da nova P.O. bloqueado até Finanças confirmar a anulação da ECF anterior.',
    reuseExplanationRequired: `Explique porque a nova P.O. reutiliza o número da P.O. cancelada (mínimo ${MIN_JUSTIFICATION_LENGTH} caracteres).`,
    reuseConfirmationRequired: 'Confirme que a nova P.O. reutiliza intencionalmente o número da P.O. cancelada.',
    reuseWarningTitle: 'Número de P.O. de uma P.O. cancelada',
    reuseCheckbox: 'Confirmo que a nova P.O. reutiliza intencionalmente o número da P.O. cancelada',
    generic: 'Não foi possível registrar a P.O. Tente novamente.',
} as const;

function readCode(err: ErrorLike | null | undefined): string | undefined {
    return err?.errorCode || err?.details?.code || err?.code || undefined;
}
function readTitle(err: ErrorLike | null | undefined): string | undefined {
    return err?.details?.title || err?.title || undefined;
}
function readDetail(err: ErrorLike | null | undefined): string | undefined {
    return err?.details?.detail || err?.detail || err?.message || undefined;
}

/** Classify a register-po / upload failure. Codes win over titles; titles keep the legacy DUPLICATE_PO/OCR_MISMATCH contract. */
export function classifyRegisterPoError(err: ErrorLike | null | undefined): RegisterPoErrorDecision {
    const code = readCode(err);
    const title = readTitle(err);
    const detail = readDetail(err);
    if (code === 'PRIOR_ECF_CONFIRMATION_PENDING') {
        return { kind: 'PRIOR_ECF_CONFIRMATION_PENDING', message: detail || REGISTER_PO_ERROR_MESSAGES.priorEcfBlocked, cancellations: err?.details?.cancellations ?? [] };
    }
    if (code === 'PREVIOUS_PO_NUMBER_REUSED' || title === 'PREVIOUS_PO_NUMBER_REUSED') {
        return {
            kind: 'PREVIOUS_PO_NUMBER_REUSED',
            message: detail || REGISTER_PO_ERROR_MESSAGES.reuseWarningTitle,
            cancellationId: err?.details?.cancellationId ?? null,
            previousPoGroupId: err?.details?.previousPoGroupId ?? null,
        };
    }
    if (title === 'DUPLICATE_PO' || code === 'DUPLICATE_PO') return { kind: 'DUPLICATE_PO', message: detail || 'Número de P.O já existente.' };
    if (title === 'OCR_MISMATCH' || code === 'OCR_MISMATCH') return { kind: 'OCR_MISMATCH', message: detail || 'Divergências validadas pelo sistema central.' };
    return { kind: 'OTHER', message: err?.message || detail || REGISTER_PO_ERROR_MESSAGES.generic };
}

/** Client-side gate before resubmitting after PREVIOUS_PO_NUMBER_REUSED; null = ok. */
export function validatePreviousPoReuse(confirmed: boolean, comment: string): string | null {
    if (!confirmed) return REGISTER_PO_ERROR_MESSAGES.reuseConfirmationRequired;
    if ((comment ?? '').trim().length < MIN_JUSTIFICATION_LENGTH) return REGISTER_PO_ERROR_MESSAGES.reuseExplanationRequired;
    return null;
}

/** The SAME payload, plus the two reuse fields (never any other change). */
export function withPreviousPoReuse<T extends object>(payload: T, comment: string): T & { previousPoNumberReuseConfirmed: true; previousPoNumberReuseComment: string } {
    return { ...payload, previousPoNumberReuseConfirmed: true, previousPoNumberReuseComment: (comment ?? '').trim() };
}

/** Whether a decision is a blocking one (no retry offered to the user). */
export function isBlockingRegisterPoError(d: RegisterPoErrorDecision): boolean {
    return d.kind === 'PRIOR_ECF_CONFIRMATION_PENDING';
}
