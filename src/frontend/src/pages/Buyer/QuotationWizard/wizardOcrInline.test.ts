import { describe, it, expect, vi, beforeEach } from 'vitest';
// Node-only vitest. The controller imports lib/api → lib/logger (reads localStorage at module load): mock the api.
vi.mock('../../../lib/api', () => ({
    api: {
        attachments: { upload: vi.fn() },
        requests: { ocrExtract: vi.fn() },
    },
}));
import { api } from '../../../lib/api';
import { createBuyerQuotationWizardController } from './buyerQuotationWizardController';
import controllerSrc from './buyerQuotationWizardController.ts?raw';
import stepSrc from './WizardStepDocumentsOcr.tsx?raw';
import modalSrc from './QuotationWizardModal.tsx?raw';
import hostSrc from './hooks/useWorkspaceWizardHost.ts?raw';
import workspaceSrc from '../BuyerRequestWorkspace.tsx?raw';
import classicSrc from '../BuyerItemsList.tsx?raw';

// v2.249.2 — the document step reports its own failures: business vs provider vs network, inside the wizard, with the
// loading state reset, no draft from a failed/empty extraction, and a retry that reuses the already-uploaded document.

const REQ = 'a0eba2c8-1155-425a-b5a6-d86f0f8fee04';
const upload = api.attachments.upload as unknown as ReturnType<typeof vi.fn>;
const ocrExtract = api.requests.ocrExtract as unknown as ReturnType<typeof vi.fn>;

function apiError(status: number, details?: any, extra: Record<string, unknown> = {}) {
    return Object.assign(new Error(details?.detail || 'x'), { name: 'ApiError', status, details, ...extra });
}

function harness() {
    const setIsProcessingOcr = vi.fn();
    const setOcrError = vi.fn();
    const setDraft = vi.fn();
    const setTemporaryWizardAttachmentIds = vi.fn();
    const onFeedback = vi.fn();
    const pendingUploadRef = { current: null as { file: File; attachmentId: string } | null };
    const controller = createBuyerQuotationWizardController({
        quotationWizardState: { setDraft, closeWizard: vi.fn(), isEditing: false, editingQuotationId: null, reworkBatchId: null, revisesQuotationId: null, classificationConflict: null } as any,
        wizardActiveRequest: { requestId: REQ },
        setWizardActiveRequest: vi.fn(),
        setIsSaving: vi.fn(),
        setIsProcessingOcr,
        temporaryWizardAttachmentIds: [],
        setTemporaryWizardAttachmentIds,
        preAttemptSnapshotRef: { current: null },
        mapOcrResultToDraft: vi.fn().mockResolvedValue({ items: [{ description: 'Rolo de filme stretch 500 mm - TEST' }] }),
        onSaved: vi.fn(),
        onFeedback,
        setOcrError,
        pendingUploadRef,
    });
    const processing = () => setIsProcessingOcr.mock.calls.map(c => c[0]({})[REQ]);
    return { controller, setIsProcessingOcr, setOcrError, setDraft, setTemporaryWizardAttachmentIds, onFeedback, pendingUploadRef, processing };
}

const file = () => new File(['%PDF-1.4 TEST'], 'Cotacao_TEST_MAQSIS_9003_REQ628.pdf', { type: 'application/pdf' });

beforeEach(() => { upload.mockReset(); ocrExtract.mockReset(); });

describe('startWizardUpload — inline failure reporting', () => {
    it('HTTP 409 from ocr-extract: BUSINESS error reported inside the wizard, loading reset, no draft, no page banner', async () => {
        upload.mockResolvedValue([{ id: 'att-1' }]);
        ocrExtract.mockRejectedValue(apiError(409, { title: 'Ação Bloqueada', detail: 'Não é possível realizar extração OCR neste status do pedido.' }, { correlationId: 'd90703146a3b' }));
        const h = harness();
        await h.controller.startWizardUpload(file());

        expect(h.setOcrError).toHaveBeenNthCalledWith(1, null);                 // a new attempt clears the previous failure
        const err = h.setOcrError.mock.calls[h.setOcrError.mock.calls.length - 1][0];
        expect(err).toMatchObject({ stage: 'OCR', kind: 'BUSINESS', retryable: false, title: 'Ação Bloqueada', status: 409, fileName: 'Cotacao_TEST_MAQSIS_9003_REQ628.pdf' });
        expect(h.processing()).toEqual([true, false]);                             // loading state always reset
        expect(h.setDraft).not.toHaveBeenCalled();                                 // no stale/empty extraction data
        expect(h.onFeedback).not.toHaveBeenCalled();                               // nothing on the page behind the wizard
        expect(h.pendingUploadRef.current).toEqual({ file: expect.any(File), attachmentId: 'att-1' }); // document kept for the attempt
        expect(h.setTemporaryWizardAttachmentIds).toHaveBeenCalledTimes(1);        // still cleaned up on cancel, as before
    });

    it('provider failure then retry with the SAME file: OCR re-runs, the document is NOT uploaded again', async () => {
        upload.mockResolvedValue([{ id: 'att-1' }]);
        ocrExtract.mockRejectedValueOnce(apiError(500, { success: false, status: { code: 'PORTAL_ERROR' } }))
                  .mockResolvedValueOnce({ success: true, items: [] });
        const h = harness();
        const f = file();
        await h.controller.startWizardUpload(f);
        expect(h.setOcrError.mock.calls[h.setOcrError.mock.calls.length - 1][0]).toMatchObject({ kind: 'PROVIDER', retryable: true, stage: 'OCR' });

        await h.controller.retryWizardUpload();
        expect(upload).toHaveBeenCalledTimes(1);                                   // no duplicate upload / attachment
        expect(ocrExtract).toHaveBeenCalledTimes(2);
        expect(ocrExtract).toHaveBeenLastCalledWith(REQ, f);
        expect(h.setDraft).toHaveBeenCalledTimes(1);
        expect(h.pendingUploadRef.current).toBeNull();                             // attempt completed
        expect(h.setOcrError.mock.calls[h.setOcrError.mock.calls.length - 1][0]).toBeNull();
    });

    it('a 200 envelope with success:false is a PROVIDER failure: no draft is built', async () => {
        upload.mockResolvedValue([{ id: 'att-1' }]);
        ocrExtract.mockResolvedValue({ success: false, status: { code: 'ERROR', qualityScore: 0 } });
        const h = harness();
        await h.controller.startWizardUpload(file());
        expect(h.setOcrError.mock.calls[h.setOcrError.mock.calls.length - 1][0]).toMatchObject({ kind: 'PROVIDER', retryable: true });
        expect(h.setDraft).not.toHaveBeenCalled();
        expect(h.processing()).toEqual([true, false]);
    });

    it('a refused upload (400 "Upload Bloqueado") is reported as the UPLOAD stage and OCR is never called', async () => {
        upload.mockRejectedValue(apiError(400, { title: 'Upload Bloqueado', detail: 'O documento de Cotação só pode ser carregado…' }));
        const h = harness();
        await h.controller.startWizardUpload(file());
        expect(ocrExtract).not.toHaveBeenCalled();
        expect(h.setOcrError.mock.calls[h.setOcrError.mock.calls.length - 1][0]).toMatchObject({ stage: 'UPLOAD', kind: 'BUSINESS', title: 'Upload Bloqueado' });
        expect(h.pendingUploadRef.current).toBeNull();
    });

    it('a DIFFERENT file after a failure is a new attempt: uploaded once more, previous error cleared', async () => {
        upload.mockResolvedValueOnce([{ id: 'att-1' }]).mockResolvedValueOnce([{ id: 'att-2' }]);
        ocrExtract.mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce({ success: true });
        const h = harness();
        await h.controller.startWizardUpload(file());
        expect(h.setOcrError.mock.calls[h.setOcrError.mock.calls.length - 1][0]).toMatchObject({ kind: 'NETWORK', retryable: true });
        h.controller.dismissOcrError();
        expect(h.setOcrError.mock.calls[h.setOcrError.mock.calls.length - 1][0]).toBeNull();
        await h.controller.startWizardUpload(new File(['other'], 'outro.pdf', { type: 'application/pdf' }));
        expect(upload).toHaveBeenCalledTimes(2);
        expect(h.setDraft).toHaveBeenCalledTimes(1);
    });
});

describe('wiring (source-level)', () => {
    it('the controller no longer reports OCR failures on the page behind the wizard', () => {
        expect(controllerSrc).not.toContain("onFeedback({ type: 'error', message: 'Erro ao processar documento via OCR.' })");
        expect(controllerSrc).toMatch(/reportOcrError\(classifyOcrFailure\(stage, error, file\.name\)\)/);
        expect(controllerSrc).toMatch(/const resultFailure = describeOcrResultFailure\(result, file\.name\);/);
    });
    it('the document step renders an accessible, sticky inline alert with retry (retryable only) and "choose another file"', () => {
        expect(stepSrc).toMatch(/role="alert"\s+aria-live="assertive"\s+data-testid="wizard-ocr-error"/);
        expect(stepSrc).toMatch(/position: 'sticky', top: 0/);
        expect(stepSrc).toMatch(/\{ocrError\.retryable && onRetryOcr && \(/);
        expect(stepSrc).toContain('Tentar novamente');
        expect(stepSrc).toContain('Escolher outro ficheiro');
        expect(stepSrc).toMatch(/Ficheiro: \{ocrError\.fileName\} · \{ocrErrorTechnicalLine\(ocrError\)\}/);
        expect(stepSrc).toMatch(/\{!draft && ocrError && \(/);                         // never after a draft exists
    });
    it('the modal resets the file input so the same file can be re-selected, and forwards the error props to the step', () => {
        expect(modalSrc).toMatch(/e\.target\.value = '';/);
        expect(modalSrc).toMatch(/ocrError=\{ocrError\}\s+onRetryOcr=\{onRetryOcr\}\s+onDismissOcrError=\{onDismissOcrError\}/);
    });
    it('both hosts own the error state and the current-attempt document, and pass them to the modal', () => {
        expect(hostSrc).toMatch(/const \[ocrError, setOcrError\] = useState<WizardOcrError \| null>\(null\);/);
        expect(hostSrc).toMatch(/const pendingUploadRef = useRef<\{ file: File; attachmentId: string \} \| null>\(null\);/);
        expect(hostSrc).toMatch(/retryOcr: \(\) => void controller\.retryWizardUpload\(\)/);
        expect(workspaceSrc).toMatch(/ocrError=\{wizardHost\.ocrError\}\s+onRetryOcr=\{wizardHost\.retryOcr\}\s+onDismissOcrError=\{wizardHost\.dismissOcrError\}/);
        expect(classicSrc).toMatch(/ocrError=\{wizardOcrError\}/);
        expect(classicSrc).toMatch(/pendingUploadRef: wizardPendingUploadRef/);
    });
});
