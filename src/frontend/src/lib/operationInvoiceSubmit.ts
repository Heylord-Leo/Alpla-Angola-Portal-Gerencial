/**
 * v2.245.8 — the registration orchestration of a Final Invoice, framework-free so it is testable at
 * the consumer boundary.
 *
 * The order is the whole point: the backend's create admissibility is verified BEFORE the evidence
 * file is uploaded (an upload followed by a refused create left an orphan OPERATION_INVOICE attachment
 * that cannot be deleted after approval). When the create itself fails after a successful upload, the
 * uploaded attachment id is RETAINED and reused on the next attempt — the backend's own contract
 * ("one attachment is one invoice": the same attachment offered again returns the existing invoice,
 * and an unclaimed attachment may be claimed by a later create) — so a retry never uploads a second
 * file and never creates a second invoice. Concurrent invocations are refused while one is in flight.
 */

export interface RegisterInvoicePorts {
    /** The backend preflight — rejects (throws) when the create could not succeed. */
    preflight: () => Promise<unknown>;
    /** Uploads the evidence file; resolves with the attachment id. */
    upload: () => Promise<string>;
    /** The authoritative create/replace with the attachment id. */
    create: (attachmentId: string) => Promise<unknown>;
}

export type RegisterInvoiceOutcome =
    | { ok: true; attachmentId: string; uploaded: boolean }
    | { ok: false; stage: 'preflight' | 'upload' | 'create' | 'busy'; error: unknown; attachmentId: string | null };

export interface RegisterInvoiceOptions {
    /** An attachment uploaded by a previous attempt whose create failed — reused, never re-uploaded. */
    retainedAttachmentId?: string | null;
}

export async function registerOperationInvoice(
    ports: RegisterInvoicePorts,
    options: RegisterInvoiceOptions = {}
): Promise<RegisterInvoiceOutcome> {
    const retained = options.retainedAttachmentId ?? null;

    if (retained === null) {
        try {
            await ports.preflight();
        } catch (error) {
            return { ok: false, stage: 'preflight', error, attachmentId: null };
        }
    }

    let attachmentId = retained;
    if (attachmentId === null) {
        try {
            attachmentId = await ports.upload();
        } catch (error) {
            return { ok: false, stage: 'upload', error, attachmentId: null };
        }
    }

    try {
        await ports.create(attachmentId);
    } catch (error) {
        // The file is already in the request; keep its id so the retry claims it instead of
        // uploading again.
        return { ok: false, stage: 'create', error, attachmentId };
    }

    return { ok: true, attachmentId, uploaded: retained === null };
}

// ── Interrupted-registration recovery (server-owned) ─────────────────────────────────────────
//
// The uploaded attachment is a persisted server fact (RequestAttachment, type OPERATION_INVOICE) and
// the backend lists the ones no invoice claims (`unclaimed-attachments`). Component memory is only a
// cache of that list: after a closed modal, a reload or from another session the drawer re-reads it and
// offers the file for reuse — the create is idempotent per attachment (the same attachment returns
// the existing invoice), so a lost response is recovered by retrying, never by uploading again.
// Discarding a previous upload is an explicit backend release that refuses claimed attachments.

export type ReleaseOutcome = 'released' | 'claimed' | 'failed';

/**
 * Explicitly resolves a previous uploaded-but-unclaimed attachment when the user picks another file.
 * `isClaimed(error)` recognizes the backend's "already an invoice" answer, in which case the file must
 * NOT be treated as discardable — the caller surfaces the existing invoice instead.
 */
export async function releasePreviousUpload(
    release: (attachmentId: string) => Promise<unknown>,
    attachmentId: string,
    isClaimed: (error: unknown) => boolean
): Promise<ReleaseOutcome> {
    try {
        await release(attachmentId);
        return 'released';
    } catch (error) {
        return isClaimed(error) ? 'claimed' : 'failed';
    }
}

/**
 * The ONLY upload a create-mode modal may resume automatically: the one this uninterrupted submission
 * session uploaded itself, and only while the server still lists it as unclaimed (server truth wins over
 * memory — a retained id the server no longer lists was claimed or released elsewhere). Uploads merely
 * DISCOVERED on the server (after a closed modal, a reload, another session) are never selected
 * automatically: they may be old or abandoned documents, so the user must explicitly choose
 * "Reutilizar" or "Descartar" for each — see `sortRecoverableCandidates`.
 */
export function pickResumableAttachment<T extends { attachmentId: string; uploadedAtUtc: string }>(
    unclaimed: readonly T[],
    retainedAttachmentId: string | null
): T | null {
    if (!retainedAttachmentId) return null;
    return unclaimed.find(u => u.attachmentId === retainedAttachmentId) ?? null;
}

/** Deterministic order for the recoverable-upload list: newest first, attachment id as the tie-break. */
export function sortRecoverableCandidates<T extends { attachmentId: string; uploadedAtUtc: string }>(
    unclaimed: readonly T[]
): T[] {
    return [...unclaimed].sort((a, b) =>
        b.uploadedAtUtc.localeCompare(a.uploadedAtUtc) || a.attachmentId.localeCompare(b.attachmentId));
}

/**
 * Serializes submissions: while one registration is in flight every further call resolves
 * immediately as `busy` without touching any port (double-clicks, keyboard repeats, races between
 * the button and a form submit). The UI keeps its own `saving` flag for presentation; this guard is
 * what makes the "exactly once" property independent of rendering.
 */
export function createInvoiceSubmitter() {
    let inFlight = false;
    return async (
        ports: RegisterInvoicePorts, options: RegisterInvoiceOptions = {}
    ): Promise<RegisterInvoiceOutcome> => {
        if (inFlight) return { ok: false, stage: 'busy', error: null, attachmentId: options.retainedAttachmentId ?? null };
        inFlight = true;
        try {
            return await registerOperationInvoice(ports, options);
        } finally {
            inFlight = false;
        }
    };
}
