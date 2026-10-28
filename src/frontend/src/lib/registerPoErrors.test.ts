import { describe, it, expect } from 'vitest';
import { classifyRegisterPoError, validatePreviousPoReuse, withPreviousPoReuse, isBlockingRegisterPoError, REGISTER_PO_ERROR_MESSAGES } from './registerPoErrors';

// ApiError shape (lib/api.ts handleApiError): message = detail||title, errorCode = extensions.code, details = ProblemDetails body.
const apiError = (status: number, body: Record<string, unknown>) => ({
    message: (body.detail as string) || (body.title as string),
    status,
    errorCode: body.code as string | undefined,
    details: body,
});

describe('classifyRegisterPoError', () => {
    it('409 PRIOR_ECF_CONFIRMATION_PENDING (register-po or upload) → blocking, server detail, no retry', () => {
        const d = classifyRegisterPoError(apiError(409, { title: 'Registo de P.O bloqueado', detail: 'Finanças ainda não confirmou a anulação da ECF10 2026/364.', code: 'PRIOR_ECF_CONFIRMATION_PENDING', cancellations: [{ cancellationId: 'c1' }] }));
        expect(d.kind).toBe('PRIOR_ECF_CONFIRMATION_PENDING');
        expect(d.message).toBe('Finanças ainda não confirmou a anulação da ECF10 2026/364.');
        expect(d.cancellations).toHaveLength(1);
        expect(isBlockingRegisterPoError(d)).toBe(true);
    });
    it('400 PREVIOUS_PO_NUMBER_REUSED → warning with the extensions', () => {
        const d = classifyRegisterPoError(apiError(400, { title: 'PREVIOUS_PO_NUMBER_REUSED', detail: 'O número é o da P.O anterior cancelada.', code: 'PREVIOUS_PO_NUMBER_REUSED', cancellationId: 'c1', previousPoGroupId: 'g1' }));
        expect(d.kind).toBe('PREVIOUS_PO_NUMBER_REUSED');
        expect(d.message).toBe('O número é o da P.O anterior cancelada.');
        expect(d.cancellationId).toBe('c1');
        expect(d.previousPoGroupId).toBe('g1');
        expect(isBlockingRegisterPoError(d)).toBe(false);
    });
    it('recognises the reuse code by title alone and by code alone', () => {
        expect(classifyRegisterPoError({ details: { title: 'PREVIOUS_PO_NUMBER_REUSED' } }).kind).toBe('PREVIOUS_PO_NUMBER_REUSED');
        expect(classifyRegisterPoError({ errorCode: 'PREVIOUS_PO_NUMBER_REUSED' }).kind).toBe('PREVIOUS_PO_NUMBER_REUSED');
        expect(classifyRegisterPoError({ code: 'PRIOR_ECF_CONFIRMATION_PENDING' }).message).toBe(REGISTER_PO_ERROR_MESSAGES.priorEcfBlocked);
    });
    it('keeps the legacy DUPLICATE_PO / OCR_MISMATCH title contract (read from details.title, the ProblemDetails body)', () => {
        expect(classifyRegisterPoError(apiError(400, { title: 'DUPLICATE_PO', detail: 'já registado' })).kind).toBe('DUPLICATE_PO');
        expect(classifyRegisterPoError(apiError(400, { title: 'OCR_MISMATCH', detail: 'diverge' })).kind).toBe('OCR_MISMATCH');
        expect(classifyRegisterPoError({ title: 'DUPLICATE_PO' }).kind).toBe('DUPLICATE_PO');
    });
    it('anything else is OTHER with the error message or the generic text', () => {
        expect(classifyRegisterPoError(new Error('boom'))).toEqual({ kind: 'OTHER', message: 'boom' });
        expect(classifyRegisterPoError(null).message).toBe(REGISTER_PO_ERROR_MESSAGES.generic);
        expect(classifyRegisterPoError(apiError(400, { title: 'Ação Bloqueada', detail: 'x' })).kind).toBe('OTHER');
    });
});

describe('PREVIOUS_PO_NUMBER_REUSED resubmission', () => {
    it('requires the checkbox AND a 20+ char explanation', () => {
        expect(validatePreviousPoReuse(false, 'a'.repeat(30))).toBe(REGISTER_PO_ERROR_MESSAGES.reuseConfirmationRequired);
        expect(validatePreviousPoReuse(true, 'curto')).toBe(REGISTER_PO_ERROR_MESSAGES.reuseExplanationRequired);
        expect(validatePreviousPoReuse(true, '   ' + 'a'.repeat(19))).toBe(REGISTER_PO_ERROR_MESSAGES.reuseExplanationRequired);
        expect(validatePreviousPoReuse(true, 'A P.O. foi reemitida no Primavera com o mesmo número.')).toBeNull();
    });
    it('resubmits the SAME payload plus the two reuse fields', () => {
        const payload = { poGroupId: 'g', comment: 'c', hasMismatches: false, purchaseOrderNumber: 'ECF10 2026/364' };
        const out = withPreviousPoReuse(payload, '  Mesmo número reemitido.  ');
        expect(out).toEqual({ ...payload, previousPoNumberReuseConfirmed: true, previousPoNumberReuseComment: 'Mesmo número reemitido.' });
        expect(payload).not.toHaveProperty('previousPoNumberReuseConfirmed'); // original untouched
    });
    it('the checkbox wording is the approved one', () => {
        expect(REGISTER_PO_ERROR_MESSAGES.reuseCheckbox).toBe('Confirmo que a nova P.O. reutiliza intencionalmente o número da P.O. cancelada');
    });
});
