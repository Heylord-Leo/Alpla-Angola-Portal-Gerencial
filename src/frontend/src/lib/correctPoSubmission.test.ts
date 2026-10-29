import { describe, it, expect, vi } from 'vitest';
import {
    CORRECT_PO_MESSAGES,
    buildRegisterPoPayload,
    normalizeJustification,
    resolveJustificationPolicy,
    submitCorrectPo,
    validateCorrectPoSubmission,
    type CorrectPoSubmissionInput,
    type RegisterPoPayload,
} from './correctPoSubmission';
import modalSrc from '../components/CorrectPoModal.tsx?raw';

// docs §G.19.3 — regression for the TEST v2.247.6 gap on REQ-27/07/2026-156: the modal replaced an empty comment
// with "P.O corrigida após devolução por Finanças.", so the backend's mandatory-justification rule saw a non-blank
// Comment and the correction was recorded with a generated justification. These tests run the REAL submission
// path the modal invokes (validation → upload → register-po payload) against captured dependencies.

const GROUP = '44bd391c-9d32-496f-b237-30a597a4844e';
const REQUEST = '7008bd28-e902-414e-8651-1fe9540e7c19';
const LEGACY_DETAILS = { legacyReturnCorrection: { poGroupId: GROUP, justificationRequired: true, reason: 'LEGACY_RETURN_PENDING_CORRECTION' } };
const GENERATED_FALLBACK = 'P.O corrigida após devolução por Finanças.';
const pdf = { name: 'po.pdf', type: 'application/pdf' } as unknown as File;

function input(overrides: Partial<CorrectPoSubmissionInput> = {}): CorrectPoSubmissionInput {
    return {
        requestId: REQUEST,
        poGroupId: GROUP,
        detailsUnavailable: false,
        groupMissing: false,
        file: pdf,
        comment: '',
        ocrResult: null,
        overrideConfirmed: false,
        paymentCondition: 'ADVANCE_FULL',
        advancePercent: 50,
        paymentConditionSource: 'USER_SELECTED',
        justification: { required: false, source: 'NONE' },
        // v2.249.4 — a correction always states the number it confirms (CURRENT) or replaces
        purchaseOrderNumber: 'ECF11 2026/156',
        purchaseOrderNumberSource: 'CURRENT',
        ...overrides,
    };
}

function deps() {
    const calls: string[] = [];
    const upload = vi.fn(async () => { calls.push('upload'); return {}; });
    const registerPo = vi.fn(async (_id: string, _payload: RegisterPoPayload) => { calls.push('registerPo'); return { message: 'P.O corrigida e re-registrada com sucesso. Adiantamento necessário.' }; });
    return { upload, registerPo, calls };
}

describe('resolveJustificationPolicy — the backend decision is reused, never inferred from the scalar status', () => {
    it('is mandatory (LEGACY_RETURN) for the group the server named', () => {
        expect(resolveJustificationPolicy(LEGACY_DETAILS, GROUP, false)).toEqual({ required: true, source: 'LEGACY_RETURN' });
    });
    it('is not mandatory for another group, without the decision, or when the server says not required', () => {
        expect(resolveJustificationPolicy(LEGACY_DETAILS, 'other-group', false).required).toBe(false);
        expect(resolveJustificationPolicy({ legacyReturnCorrection: null }, GROUP, false).required).toBe(false);
        expect(resolveJustificationPolicy(null, GROUP, false).required).toBe(false);
        expect(resolveJustificationPolicy({ legacyReturnCorrection: { ...LEGACY_DETAILS.legacyReturnCorrection, justificationRequired: false } }, GROUP, false).required).toBe(false);
    });
    it('keeps the pre-existing OCR-divergence requirement for current-code corrections', () => {
        expect(resolveJustificationPolicy(null, GROUP, true)).toEqual({ required: true, source: 'OCR_MISMATCH' });
    });
    it('the legacy decision wins over the OCR source when both apply', () => {
        expect(resolveJustificationPolicy(LEGACY_DETAILS, GROUP, true).source).toBe('LEGACY_RETURN');
    });
});

describe('normalizeJustification — never a generated default', () => {
    it.each(['', '   ', '\t\n', null, undefined])('blank %j → undefined', (raw) => {
        expect(normalizeJustification(raw as any)).toBeUndefined();
    });
    it('keeps the Buyer text, trimmed', () => {
        expect(normalizeJustification('  NIF do fornecedor corrigido  ')).toBe('NIF do fornecedor corrigido');
    });
});

describe('submitCorrectPo — mandatory justification (legacy Finance return)', () => {
    const legacy = { required: true as const, source: 'LEGACY_RETURN' as const };

    it('when the detail fetch failed the server decision is unknown: nothing is uploaded or submitted', async () => {
        const d = deps();
        const outcome = await submitCorrectPo(d, input({ detailsUnavailable: true, comment: 'texto' }));
        expect(outcome).toEqual({ ok: false, error: CORRECT_PO_MESSAGES.detailsUnavailable });
        expect(d.upload).not.toHaveBeenCalled();
        expect(d.registerPo).not.toHaveBeenCalled();
    });

    it.each(['', '   ', '\t\n', '   '])('blank comment %j: refused BEFORE the upload, nothing is sent', async (comment) => {
        const d = deps();
        const outcome = await submitCorrectPo(d, input({ comment, justification: legacy }));
        expect(outcome).toEqual({ ok: false, error: CORRECT_PO_MESSAGES.legacyJustification });
        expect(d.upload).not.toHaveBeenCalled();
        expect(d.registerPo).not.toHaveBeenCalled();
    });

    it('the generated text that used to replace an empty comment is never sent', async () => {
        const d = deps();
        await submitCorrectPo(d, input({ comment: '', justification: legacy }));
        await submitCorrectPo(d, input({ comment: 'Proforma anexada e P.O corrigida', justification: legacy }));
        for (const call of d.registerPo.mock.calls) {
            expect(JSON.stringify(call[1])).not.toContain(GENERATED_FALLBACK);
            expect(JSON.stringify(call[1])).not.toContain('P.O corrigida após devolução');
        }
    });

    it('valid input: upload first, then register-po with the Buyer text (trimmed) and every other field preserved', async () => {
        const d = deps();
        const outcome = await submitCorrectPo(d, input({
            comment: '  NIF do fornecedor corrigido na P.O  ',
            justification: legacy,
            paymentCondition: 'ADVANCE_FULL',
            paymentConditionSource: 'OCR_DETECTED',
            ocrResult: { hasMismatches: false, details: [], extractedTotal: 1097000, extractedSupplier: 'Qlerius | Serviços de Limpeza' },
        }));
        expect(outcome).toEqual({ ok: true, message: 'P.O corrigida e re-registrada com sucesso. Adiantamento necessário.' });
        expect(d.calls).toEqual(['upload', 'registerPo']);
        expect(d.upload).toHaveBeenCalledWith(REQUEST, [pdf], 'PO', GROUP);
        expect(d.registerPo).toHaveBeenCalledWith(REQUEST, {
            poGroupId: GROUP,
            comment: 'NIF do fornecedor corrigido na P.O',
            hasMismatches: false,
            overrideConfirmed: false,
            mismatchDetails: '',
            extractedTotalAmount: 1097000,
            extractedSupplierName: 'Qlerius | Serviços de Limpeza',
            paymentConditionCode: 'ADVANCE_FULL',
            advancePaymentPercent: undefined,
            paymentConditionSource: 'OCR_DETECTED',
            purchaseOrderNumber: 'ECF11 2026/156',
            purchaseOrderNumberSource: 'CURRENT',
            documentTypeConfirmed: false,
        });
    });
});

describe('submitCorrectPo — current-code correction keeps the comment optional', () => {
    it('blank comment is accepted and sent as ABSENT (no fabricated "Justificativa")', async () => {
        const d = deps();
        const outcome = await submitCorrectPo(d, input({ comment: '   ', paymentCondition: 'POST_PAID' }));
        expect(outcome.ok).toBe(true);
        expect(d.calls).toEqual(['upload', 'registerPo']);
        const payload = d.registerPo.mock.calls[0][1];
        expect(payload.comment).toBeUndefined();
        expect(JSON.stringify(payload)).not.toContain('comment');
        expect(JSON.stringify(payload)).not.toContain(GENERATED_FALLBACK);
        expect(payload.paymentConditionCode).toBe('POST_PAID');
        expect(payload.paymentConditionSource).toBe('USER_SELECTED');
    });

    it('a comment the Buyer did write is sent unchanged (trimmed)', async () => {
        const d = deps();
        await submitCorrectPo(d, input({ comment: ' Data corrigida ' }));
        expect(d.registerPo.mock.calls[0][1].comment).toBe('Data corrigida');
    });

    it('ADVANCE_PARTIAL forwards the percentage; other conditions do not', () => {
        expect(buildRegisterPoPayload(input({ paymentCondition: 'ADVANCE_PARTIAL', advancePercent: 30 })).advancePaymentPercent).toBe(30);
        expect(buildRegisterPoPayload(input({ paymentCondition: 'POST_PAID', advancePercent: 30 })).advancePaymentPercent).toBeUndefined();
    });

    it('an empty paymentConditionSource defaults to USER_SELECTED (unchanged)', () => {
        expect(buildRegisterPoPayload(input({ paymentConditionSource: '' })).paymentConditionSource).toBe('USER_SELECTED');
    });
});

describe('validateCorrectPoSubmission — pre-existing guards unchanged, in the same order', () => {
    it('missing group, missing file, unconfirmed divergence, divergence without comment', () => {
        expect(validateCorrectPoSubmission(input({ groupMissing: true }))).toBe(CORRECT_PO_MESSAGES.groupMissing);
        expect(validateCorrectPoSubmission(input({ detailsUnavailable: true }))).toBe(CORRECT_PO_MESSAGES.detailsUnavailable);
        expect(validateCorrectPoSubmission(input({ file: null }))).toBe(CORRECT_PO_MESSAGES.fileRequired);
        const mismatch = { hasMismatches: true, details: ['Total divergente'] };
        expect(validateCorrectPoSubmission(input({ ocrResult: mismatch, overrideConfirmed: false, justification: { required: true, source: 'OCR_MISMATCH' } })))
            .toBe(CORRECT_PO_MESSAGES.mismatchUnconfirmed);
        expect(validateCorrectPoSubmission(input({ ocrResult: mismatch, overrideConfirmed: true, comment: ' ', justification: { required: true, source: 'OCR_MISMATCH' } })))
            .toBe(CORRECT_PO_MESSAGES.mismatchJustification);
        expect(validateCorrectPoSubmission(input({ ocrResult: mismatch, overrideConfirmed: true, comment: 'Valor confirmado com o fornecedor', justification: { required: true, source: 'OCR_MISMATCH' } })))
            .toBeNull();
    });

    it('divergence payload forwards the details and the override (unchanged)', () => {
        const payload = buildRegisterPoPayload(input({
            ocrResult: { hasMismatches: true, details: ['Total divergente', 'Fornecedor divergente'], extractedTotal: 10 },
            overrideConfirmed: true, comment: 'ok',
        }));
        expect(payload.hasMismatches).toBe(true);
        expect(payload.overrideConfirmed).toBe(true);
        expect(payload.mismatchDetails).toBe('Total divergente; Fornecedor divergente');
    });
});

describe('CorrectPoModal wiring (source-level — no DOM runtime in this suite)', () => {
    it('submits through submitCorrectPo with the real api functions and validates first', () => {
        expect(modalSrc).toMatch(/validateCorrectPoSubmission\(submission\)/);
        expect(modalSrc).toMatch(/submitCorrectPo\(\s*\{ upload: api\.attachments\.upload, registerPo: api\.requests\.registerPo, onUploaded: /);
    });
    it('contains no generated justification fallback', () => {
        expect(modalSrc).not.toContain(GENERATED_FALLBACK);
        expect(modalSrc).not.toMatch(/comment: comment \|\|/);
    });
    it('does not offer the form when its own detail fetch failed (no server decision available)', () => {
        expect(modalSrc).toMatch(/const detailsUnavailable = !loading && !requestData;/);
        expect(modalSrc).toMatch(/\) : \(detailsUnavailable \|\| groupMissing\) \? \(/);
        expect(modalSrc).toMatch(/requestId, poGroupId, detailsUnavailable, groupMissing, file, comment/);
    });
    it('derives the requirement from the server decision and labels/disables accordingly', () => {
        expect(modalSrc).toMatch(/resolveJustificationPolicy\(requestData, poGroupId, !!ocrResult\?\.hasMismatches, !!identification\?\.requiresExplicitConfirmation\)/);
        expect(modalSrc).toMatch(/Comentário \{justification\.required \? '\(Obrigatório\)' : '\(opcional\)'\}/);
        // v2.249.4 — one submit gate (submitDisabled) carries the requirement; the button reads it.
        expect((modalSrc.match(/\(justification\.required && !comment\.trim\(\)\)/g) || []).length).toBeGreaterThanOrEqual(1);
        expect(modalSrc).toMatch(/disabled=\{submitDisabled\}/);
        expect(modalSrc).not.toMatch(/ocrResult\?\.hasMismatches && !comment\.trim\(\)/);
    });
});

// ── Phase 1 P.O. cancellation (D15): resubmission after 400 PREVIOUS_PO_NUMBER_REUSED ──
describe('submitCorrectPo — previous P.O. number reuse', () => {
    it('the ordinary payload carries NO reuse fields', () => {
        const payload = buildRegisterPoPayload(input({ comment: 'ok' }));
        expect(payload).not.toHaveProperty('previousPoNumberReuseConfirmed');
        expect(payload).not.toHaveProperty('previousPoNumberReuseComment');
    });
    it('with a reuse comment the SAME payload is sent plus previousPoNumberReuseConfirmed=true + the trimmed comment, and the upload is skipped when the file was already uploaded', async () => {
        const upload = vi.fn().mockResolvedValue(undefined);
        const registerPo = vi.fn().mockResolvedValue({ message: 'ok' });
        const base = input({ comment: 'Corrigida.' });
        const out = await submitCorrectPo({ upload, registerPo }, { ...base, previousPoNumberReuseComment: '  Mesmo número reemitido no Primavera.  ', skipUpload: true });
        expect(out).toEqual({ ok: true, message: 'ok' });
        expect(upload).not.toHaveBeenCalled();
        const sent = registerPo.mock.calls[0][1] as RegisterPoPayload;
        expect(sent.previousPoNumberReuseConfirmed).toBe(true);
        expect(sent.previousPoNumberReuseComment).toBe('Mesmo número reemitido no Primavera.');
        const { previousPoNumberReuseConfirmed: _c, previousPoNumberReuseComment: _m, ...rest } = sent;
        expect(rest).toEqual(buildRegisterPoPayload(base));
    });
    it('without skipUpload the upload still happens first (unchanged order)', async () => {
        const calls: string[] = [];
        const upload = vi.fn().mockImplementation(async () => { calls.push('upload'); });
        const registerPo = vi.fn().mockImplementation(async () => { calls.push('register'); return { message: 'ok' }; });
        await submitCorrectPo({ upload, registerPo }, input({ comment: 'ok' }));
        expect(calls).toEqual(['upload', 'register']);
    });
});

// ── v2.249.4 — P.O. number on the correction + document identification + upload reuse ──
import { NO_EXTRACTION_IDENTIFICATION, readPoDocumentIdentification } from './poDocumentIdentification';
const OTHER_TYPE = readPoDocumentIdentification({ integration: { headerSuggestions: { purchaseOrderIdentification: { verdict: 'IDENTIFIED_OTHER_TYPE', reasonCode: 'OTHER_DOCUMENT_TYPE', reason: 'Cotação', otherTypeLabel: 'Cotação / Factura pró-forma', identityCandidates: [], bodyReferences: [] } } } });
const IDENTIFIED = readPoDocumentIdentification({ integration: { headerSuggestions: { documentNumber: { value: 'ECF11 2026/421' }, currencyCode: { value: 'AOA' }, documentTitle: { value: 'PO Serviços ECF11 2026/421' }, purchaseOrderIdentification: { verdict: 'IDENTIFIED_PO', reasonCode: 'PO_IDENTIFIED', reason: 'ok', family: 'ECF11', familyLabel: 'Serviços', reference: 'ECF11 2026/421', canonical: 'ECF11-2026-421', identityCandidates: [], bodyReferences: [] } } } });

describe('submitCorrectPo — v2.249.4 number and document rules, all BEFORE the upload', () => {
    it('a blank number is refused (the old modal sent none and the stale number survived the correction)', async () => {
        const d = deps();
        expect(await submitCorrectPo(d, input({ purchaseOrderNumber: '   ' }))).toEqual({ ok: false, error: CORRECT_PO_MESSAGES.numberRequired });
        expect(d.upload).not.toHaveBeenCalled();
    });
    it('a document identified as another type never reaches the upload, whatever the overrides', async () => {
        const d = deps();
        const out = await submitCorrectPo(d, input({ identification: OTHER_TYPE, overrideConfirmed: true, documentTypeConfirmed: true, comment: 'valores coincidem' }));
        expect(out).toEqual({ ok: false, error: CORRECT_PO_MESSAGES.documentBlocked });
        expect(d.upload).not.toHaveBeenCalled();
    });
    it('an UNCERTAIN document needs the explicit confirmation, then a justification', async () => {
        const d = deps();
        const policy = resolveJustificationPolicy(null, GROUP, false, true);
        expect(policy).toEqual({ required: true, source: 'DOCUMENT_TYPE' });
        expect(await submitCorrectPo(d, input({ identification: NO_EXTRACTION_IDENTIFICATION, justification: policy, comment: 'x' }))).toEqual({ ok: false, error: CORRECT_PO_MESSAGES.typeConfirmationRequired });
        expect(await submitCorrectPo(d, input({ identification: NO_EXTRACTION_IDENTIFICATION, justification: policy, documentTypeConfirmed: true, comment: ' ' }))).toEqual({ ok: false, error: CORRECT_PO_MESSAGES.typeJustification });
        expect(d.upload).not.toHaveBeenCalled();
        const ok = await submitCorrectPo(d, input({ identification: NO_EXTRACTION_IDENTIFICATION, justification: policy, documentTypeConfirmed: true, comment: 'OCR indisponível; conferido com o documento impresso', purchaseOrderNumber: 'ECF10 2026/9003', purchaseOrderNumberSource: 'MANUAL' }));
        expect(ok.ok).toBe(true);
        const sent = d.registerPo.mock.calls[0][1];
        expect(sent).toMatchObject({ purchaseOrderNumber: 'ECF10 2026/9003', purchaseOrderNumberSource: 'MANUAL', documentTypeConfirmed: true });
    });
    it('the payload carries NO extraction facts: the server identifies the document from its own evidence (po-document-extract) by file hash', async () => {
        const d = deps();
        await submitCorrectPo(d, input({ identification: IDENTIFIED, purchaseOrderNumber: 'ECF11 2026/421', purchaseOrderNumberSource: 'EXTRACTED' }));
        const sent = d.registerPo.mock.calls[0][1] as unknown as Record<string, unknown>;
        expect(sent).toMatchObject({ purchaseOrderNumber: 'ECF11 2026/421', purchaseOrderNumberSource: 'EXTRACTED', documentTypeConfirmed: false });
        for (const k of ['extractionOutcome', 'extractedDocumentNumber', 'extractedDocumentTitle', 'extractedDocumentClassification', 'extractedDocumentClassificationConfidence', 'extractedReferencedDocumentNumbers', 'extractedCurrencyCode']) {
            expect(sent).not.toHaveProperty(k);
        }
    });
    it('a pending number decision (document reads another number, field still holds the current one) is refused before the upload', async () => {
        const d = deps();
        expect(await submitCorrectPo(d, input({ numberDecisionPending: true }))).toEqual({ ok: false, error: CORRECT_PO_MESSAGES.numberDecisionRequired });
        expect(d.upload).not.toHaveBeenCalled();
    });
    it('the uploaded attachment id is captured (onUploaded) and sent as poAttachmentId; a retry skips the upload and names the same file', async () => {
        const uploaded: Array<string | null> = [];
        const upload = vi.fn(async () => [{ id: 'att-9', fileName: 'po.pdf' }]);
        const registerPo = vi.fn(async (_id: string, _p: RegisterPoPayload) => { throw Object.assign(new Error('dup'), { status: 400, details: { title: 'DUPLICATE_PO' } }); });
        await expect(submitCorrectPo({ upload, registerPo, onUploaded: id => uploaded.push(id) }, input())).rejects.toBeTruthy();
        expect(uploaded).toEqual(['att-9']);
        expect(registerPo.mock.calls[0][1]).toMatchObject({ poAttachmentId: 'att-9' });

        const registerOk = vi.fn(async (_id: string, _p: RegisterPoPayload) => ({ message: 'ok' }));
        const out = await submitCorrectPo({ upload, registerPo: registerOk }, input({ skipUpload: true, uploadedAttachmentId: 'att-9', overrideDuplicateConfirmed: true, duplicateOverrideComment: ' número confirmado no ERP ' }));
        expect(out.ok).toBe(true);
        expect(upload).toHaveBeenCalledTimes(1);                                     // no second upload
        expect(registerOk.mock.calls[0][1]).toMatchObject({ poAttachmentId: 'att-9', overrideDuplicateConfirmed: true, duplicateOverrideComment: 'número confirmado no ERP' });
    });
    it('a confirmed duplicate without a justification is refused before the upload', async () => {
        const d = deps();
        expect(await submitCorrectPo(d, input({ overrideDuplicateConfirmed: true, duplicateOverrideComment: '  ' }))).toEqual({ ok: false, error: CORRECT_PO_MESSAGES.duplicateJustification });
        expect(d.upload).not.toHaveBeenCalled();
    });
});

describe('CorrectPoModal wiring — v2.249.4 (source-level)', () => {
    it('shows the current number next to the editable proposed one, with provenance and the extracted suggestion', () => {
        expect(modalSrc).toMatch(/<PoNumberFieldBlock[^]*?currentNumber=\{selectedGroup\?\.purchaseOrderNumber\}/);
        expect(modalSrc).toMatch(/onKeepCurrent=\{\(\) => \{ setPoNumber\(f => keepCurrentPoNumber\(f, selectedGroup\?\.purchaseOrderNumber\)\)/);
        expect(modalSrc).toMatch(/numberDecisionPending: numberDecisionPending\(poNumber, selectedGroup\?\.purchaseOrderNumber\)/);
        expect(modalSrc).toMatch(/api\.requests\.poDocumentExtract\(requestId, selectedFile\)/);
        expect(modalSrc).not.toMatch(/api\.requests\.directOcrExtract\(/);
        expect(modalSrc).toMatch(/setPoNumber\(initialPoNumberField\(group\?\.purchaseOrderNumber\)\)/);
        expect(modalSrc).toMatch(/applyExtractedNumber\(f, id, extractedPoReference \|\| extractedPoNumber, \[extractedSupplierNif\]\)/);
        expect(modalSrc).toMatch(/purchaseOrderNumber: poNumber\.value, purchaseOrderNumberSource: poNumber\.source/);
    });
    it('renders the document verdict instead of the unconditional "Documento Validado" and clears stale extraction on a new file', () => {
        expect(modalSrc).toMatch(/<PoDocumentVerdictBlock identification=\{identification\}/);
        expect(modalSrc).not.toMatch(/>Documento Validado</);
        expect(modalSrc).toMatch(/setIdentification\(null\); setDetectedDocumentNumber\(null\); setDocumentTypeConfirmed\(false\);/);
        expect(modalSrc).toMatch(/setIdentification\(NO_EXTRACTION_IDENTIFICATION\)/);
    });
    it('handles DUPLICATE_PO, OCR_MISMATCH and the document blocks that used to be generic errors, and resolves the current P.O. file from the server pointer', () => {
        expect(modalSrc).toMatch(/decision\.kind === 'DUPLICATE_PO'\) setDuplicateWarning/);
        expect(modalSrc).toMatch(/decision\.kind === 'OCR_MISMATCH'\) setBackendOcrMismatch/);
        expect(modalSrc).toMatch(/isDocumentBlockingKind\(decision\.kind\)\) setDocumentBlock/);
        expect(modalSrc).toMatch(/group\?\.currentPoAttachmentId && groupPoFiles\.find/);
        expect(modalSrc).toMatch(/skipUpload: reuseUpload/);
    });
});
