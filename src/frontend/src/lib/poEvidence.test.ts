import { describe, it, expect, vi } from 'vitest';
import { cleanupTemporaryPoUpload, isPoAttachmentDeleteOffered, isPoEvidenceProtectedRefusal, temporaryPoUploadToCleanup } from './poEvidence';

describe('poEvidence — isPoAttachmentDeleteOffered', () => {
    it('non-P.O. files keep the per-type window (always offered here)', () => {
        expect(isPoAttachmentDeleteOffered({ attachmentTypeCode: 'QUOTATION' })).toBe(true);
        expect(isPoAttachmentDeleteOffered({ attachmentTypeCode: 'PAYMENT_PROOF', isPoEvidence: null })).toBe(true);
    });
    it('a P.O. file is offered only when the server says it is NOT evidence', () => {
        expect(isPoAttachmentDeleteOffered({ attachmentTypeCode: 'PO', isPoEvidence: false })).toBe(true);
        expect(isPoAttachmentDeleteOffered({ attachmentTypeCode: 'PO', isPoEvidence: true })).toBe(false);
        // unknown flag (older server) → never offered
        expect(isPoAttachmentDeleteOffered({ attachmentTypeCode: 'PO' })).toBe(false);
        expect(isPoAttachmentDeleteOffered({ attachmentTypeCode: 'PO', isPoEvidence: null })).toBe(false);
    });
    it('current, superseded and voided P.O. files are never offered, whatever the flag says', () => {
        expect(isPoAttachmentDeleteOffered({ attachmentTypeCode: 'PO', isPoEvidence: false, isCurrentPo: true })).toBe(false);
        expect(isPoAttachmentDeleteOffered({ attachmentTypeCode: 'PO', isPoEvidence: false, supersededAtUtc: '2026-10-30T08:13:00Z' })).toBe(false);
        expect(isPoAttachmentDeleteOffered({ attachmentTypeCode: 'PO', isPoEvidence: false, voidedAtUtc: '2026-10-30T08:13:00Z' })).toBe(false);
    });
});

describe('poEvidence — temporaryPoUploadToCleanup', () => {
    it('names the uploaded attachment only while no registration succeeded with it', () => {
        expect(temporaryPoUploadToCleanup({ uploadedAttachmentId: 'att-1', registered: false })).toBe('att-1');
        expect(temporaryPoUploadToCleanup({ uploadedAttachmentId: 'att-1', registered: true })).toBeNull();
        expect(temporaryPoUploadToCleanup({ uploadedAttachmentId: null, registered: false })).toBeNull();
        expect(temporaryPoUploadToCleanup({ uploadedAttachmentId: '', registered: false })).toBeNull();
    });
});

describe('poEvidence — isPoEvidenceProtectedRefusal', () => {
    it('recognises the server refusal by code (ApiError.errorCode, plain code, ProblemDetails extensions) or by the 409 title', () => {
        expect(isPoEvidenceProtectedRefusal(Object.assign(new Error('x'), { errorCode: 'PO_EVIDENCE_PROTECTED' }))).toBe(true);
        expect(isPoEvidenceProtectedRefusal(Object.assign(new Error('x'), { code: 'PO_EVIDENCE_PROTECTED' }))).toBe(true);
        expect(isPoEvidenceProtectedRefusal(Object.assign(new Error('x'), { status: 409, details: { code: 'PO_EVIDENCE_PROTECTED' } }))).toBe(true);
        expect(isPoEvidenceProtectedRefusal(Object.assign(new Error('Evidência de P.O. protegida'), { status: 409 }))).toBe(true);
    });
    it('anything else is a real failure', () => {
        expect(isPoEvidenceProtectedRefusal(Object.assign(new Error('Exclusão Bloqueada'), { status: 400 }))).toBe(false);
        expect(isPoEvidenceProtectedRefusal(Object.assign(new Error('Failed to fetch')))).toBe(false);
        expect(isPoEvidenceProtectedRefusal(Object.assign(new Error('Evidência de P.O. protegida'), { status: 500 }))).toBe(false);
        expect(isPoEvidenceProtectedRefusal(null)).toBe(false);
        expect(isPoEvidenceProtectedRefusal('PO_EVIDENCE_PROTECTED')).toBe(false);
    });
});

describe('poEvidence — cleanupTemporaryPoUpload (best effort, server authoritative)', () => {
    it('does nothing without an id', async () => {
        const del = vi.fn();
        expect(await cleanupTemporaryPoUpload(del, null)).toEqual({ attempted: false, cleaned: false, keptAsEvidence: false });
        expect(del).not.toHaveBeenCalled();
    });
    it('deletes the temporary upload', async () => {
        const del = vi.fn().mockResolvedValue(undefined);
        expect(await cleanupTemporaryPoUpload(del, 'att-1')).toEqual({ attempted: true, cleaned: true, keptAsEvidence: false });
        expect(del).toHaveBeenCalledWith('att-1');
    });
    it('a server refusal PO_EVIDENCE_PROTECTED is "kept as evidence": reported to onKept, never to onError, never thrown', async () => {
        const err = Object.assign(new Error('Evidência de P.O. protegida'), { status: 409, errorCode: 'PO_EVIDENCE_PROTECTED' });
        const del = vi.fn().mockRejectedValue(err);
        const onError = vi.fn(); const onKept = vi.fn();
        const outcome = await cleanupTemporaryPoUpload(del, 'att-2', onError, onKept);
        expect(outcome).toEqual({ attempted: true, cleaned: false, keptAsEvidence: true });
        expect(onKept).toHaveBeenCalledWith('att-2');
        expect(onError).not.toHaveBeenCalled();
    });
    it('any other failure (network, status window) is reported to onError and never throws', async () => {
        const err = Object.assign(new Error('Failed to fetch'));
        const del = vi.fn().mockRejectedValue(err);
        const onError = vi.fn(); const onKept = vi.fn();
        const outcome = await cleanupTemporaryPoUpload(del, 'att-3', onError, onKept);
        expect(outcome).toMatchObject({ attempted: true, cleaned: false, keptAsEvidence: false, error: err });
        expect(onError).toHaveBeenCalledWith(err, 'att-3');
        expect(onKept).not.toHaveBeenCalled();
    });
    it('a throwing reporter (either) does not break the wizard', async () => {
        const failing = vi.fn().mockRejectedValue(new Error('x'));
        expect(await cleanupTemporaryPoUpload(failing, 'att-4', () => { throw new Error('reporter failed'); })).toMatchObject({ attempted: true, cleaned: false });
        const kept = vi.fn().mockRejectedValue(Object.assign(new Error('x'), { errorCode: 'PO_EVIDENCE_PROTECTED' }));
        expect(await cleanupTemporaryPoUpload(kept, 'att-5', undefined, () => { throw new Error('reporter failed'); })).toMatchObject({ attempted: true, cleaned: false, keptAsEvidence: true });
    });
    it('the wizard sequence after a SUCCESSFUL registration sends no delete at all: the ref is nulled before any later clear/cancel', async () => {
        // The modals read `uploadedAttachmentIdRef.current` when the handler runs; after success they set it to null first.
        const ref: { current: string | null } = { current: 'att-6' };
        const del = vi.fn().mockResolvedValue(undefined);
        ref.current = null;                                   // onSuccess path: registered → nothing to clean
        expect(await cleanupTemporaryPoUpload(del, ref.current)).toEqual({ attempted: false, cleaned: false, keptAsEvidence: false });
        expect(del).not.toHaveBeenCalled();
    });
    it('a delayed clean-up that races a registration which succeeded meanwhile is decided by the server: kept, not failed', async () => {
        // Clear-file captured the id while the file was temporary; the server answered the DELETE only after register-po
        // committed (row lock) → 409 PO_EVIDENCE_PROTECTED. The client reports "kept" and leaves the registration intact.
        let release!: (e: unknown) => void;
        const del = vi.fn(() => new Promise<void>((_, reject) => { release = reject; }));
        const onError = vi.fn(); const onKept = vi.fn();
        const pending = cleanupTemporaryPoUpload(del, 'att-7', onError, onKept);
        release(Object.assign(new Error('Evidência de P.O. protegida'), { status: 409, errorCode: 'PO_EVIDENCE_PROTECTED' }));
        expect(await pending).toEqual({ attempted: true, cleaned: false, keptAsEvidence: true });
        expect(onKept).toHaveBeenCalledWith('att-7');
        expect(onError).not.toHaveBeenCalled();
    });
});
