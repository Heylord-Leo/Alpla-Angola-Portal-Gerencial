/**
 * v2.249.5 — P.O. evidence on the client (docs/PO_EVIDENCE_AND_QUOTATION_LIFECYCLE_v2.249.5.md §2 and §9).
 *
 * The server owns the rule (PoEvidenceProtection: a registered P.O. file — current, superseded, voided or registered before
 * the group had a pointer — is never deleted; DELETE answers 409 PO_EVIDENCE_PROTECTED, and the decision runs under the same
 * row lock register-po takes). The client only (a) never OFFERS a deletion the server would refuse and (b) cleans up its own
 * TEMPORARY upload: the file a refused or abandoned wizard attempt stored (upload precedes register-po) and that no
 * registration ever used. A clean-up the server refuses because the file turned out to be registered evidence is the
 * CORRECT outcome (the registration may have succeeded while the client saw a network error): it is reported as "kept",
 * never as a failure, and it never undoes or misrepresents that registration.
 */

export const PO_EVIDENCE_PROTECTED = 'PO_EVIDENCE_PROTECTED';

export interface PoAttachmentLike {
    attachmentTypeCode: string;
    isPoEvidence?: boolean | null;
    isCurrentPo?: boolean;
    supersededAtUtc?: string | null;
    voidedAtUtc?: string | null;
}

/**
 * Whether the attachment list may show "Remover" for this file. Non-P.O. files follow the existing per-type window.
 * A P.O. file is offered ONLY when the server says it is NOT evidence (`isPoEvidence === false`): an unknown flag (older
 * server), the current document, a superseded or a voided file are never offered.
 */
export function isPoAttachmentDeleteOffered(a: PoAttachmentLike): boolean {
    if (a.attachmentTypeCode !== 'PO') return true;
    if (a.isCurrentPo || a.supersededAtUtc || a.voidedAtUtc) return false;
    return a.isPoEvidence === false;
}

/**
 * The temporary upload a wizard must clean up when the user clears the file or cancels: the attachment uploaded by this
 * wizard session (`uploadedAttachmentId`) when no registration succeeded with it. Null when there is nothing to clean.
 */
export function temporaryPoUploadToCleanup(state: { uploadedAttachmentId: string | null | undefined; registered: boolean }): string | null {
    if (state.registered) return null;
    return state.uploadedAttachmentId || null;
}

/** True when the server refused the deletion because the file is registered P.O. evidence (409 PO_EVIDENCE_PROTECTED). */
export function isPoEvidenceProtectedRefusal(error: unknown): boolean {
    const e = error as { errorCode?: unknown; code?: unknown; status?: unknown; details?: { code?: unknown } | null; message?: unknown } | null | undefined;
    if (!e || typeof e !== 'object') return false;
    const code = e.errorCode ?? e.code ?? e.details?.code;
    if (code === PO_EVIDENCE_PROTECTED) return true;
    return e.status === 409 && typeof e.message === 'string' && /Evid[êe]ncia de P\.O\. protegida/i.test(e.message);
}

export interface CleanupOutcome {
    attempted: boolean;
    cleaned: boolean;
    /** True when the server kept the file as registered evidence (PO_EVIDENCE_PROTECTED): correct outcome, not a failure. */
    keptAsEvidence: boolean;
    /** Set on a real failure (network, 5xx, status window …): an unregistered file may remain, still deletable from the list. */
    error?: unknown;
}

/**
 * Best-effort server clean-up of ONE temporary upload. Never throws, never blocks the wizard. The id is captured when the
 * clean-up starts (the wizard nulls its ref right after a successful registration, so a clean-up started later sends nothing);
 * a clean-up that races a registration is decided by the server lock: a refusal (PO_EVIDENCE_PROTECTED) means the file is
 * registered evidence and is kept — `onKept` is informed, `onError` is NOT. Any other failure goes to `onError`.
 */
export async function cleanupTemporaryPoUpload(
    deleteAttachment: (id: string) => Promise<void>,
    attachmentId: string | null | undefined,
    onError?: (error: unknown, attachmentId: string) => void,
    onKept?: (attachmentId: string) => void
): Promise<CleanupOutcome> {
    if (!attachmentId) return { attempted: false, cleaned: false, keptAsEvidence: false };
    try {
        await deleteAttachment(attachmentId);
        return { attempted: true, cleaned: true, keptAsEvidence: false };
    } catch (error) {
        if (isPoEvidenceProtectedRefusal(error)) {
            try { onKept?.(attachmentId); } catch { /* reporting must never fail the wizard */ }
            return { attempted: true, cleaned: false, keptAsEvidence: true };
        }
        try { onError?.(error, attachmentId); } catch { /* reporting must never fail the wizard */ }
        return { attempted: true, cleaned: false, keptAsEvidence: false, error };
    }
}
