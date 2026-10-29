import { describe, it, expect } from 'vitest';
import { classifyOcrFailure, describeOcrResultFailure, ocrErrorTechnicalLine } from './wizardOcrError';

// v2.249.2 — the import wizard distinguishes a Portal business-rule rejection from provider/network failures, keeps
// the message in the operator's words (PT, from the API's ProblemDetails title/detail) and marks what is retryable.

const FILE = 'Cotacao_TEST_MAQSIS_9003_REQ628.pdf';

describe('classifyOcrFailure', () => {
    it('HTTP 409 ProblemDetails is a BUSINESS rule, not retryable, with the API title/detail and no technical leak', () => {
        const err = Object.assign(new Error('Não é possível realizar extração OCR neste status do pedido.'), {
            name: 'ApiError', status: 409, correlationId: 'd90703146a3b0000',
            details: { title: 'Ação Bloqueada', detail: 'Não é possível realizar extração OCR neste status do pedido.', status: 409 },
        });
        const e = classifyOcrFailure('OCR', err, FILE);
        expect(e.kind).toBe('BUSINESS');
        expect(e.retryable).toBe(false);
        expect(e.title).toBe('Ação Bloqueada');
        expect(e.message).toBe('Não é possível realizar extração OCR neste status do pedido.');
        expect(e.guidance).toContain('regra de negócio');
        expect(e.stage).toBe('OCR');
        expect(e.status).toBe(409);
        expect(e.fileName).toBe(FILE);
        expect(ocrErrorTechnicalLine(e)).toBe('Extração OCR · HTTP 409 · ref. d90703146a3b');
    });
    it('HTTP 400 with a machine code (e.g. upload blocked / invalid line reference) is BUSINESS with the code kept', () => {
        const err = { status: 400, errorCode: 'REQUEST_LINE_REFERENCE_INVALID', details: { title: 'Validação de Item', detail: 'Item(ns) 2: o item do pedido referenciado não pertence a este pedido ou já não existe.' } };
        const e = classifyOcrFailure('OCR', err, FILE);
        expect(e.kind).toBe('BUSINESS');
        expect(e.code).toBe('REQUEST_LINE_REFERENCE_INVALID');
        expect(e.title).toBe('Validação de Item');
        expect(e.retryable).toBe(false);
    });
    it('401/403 are AUTH, not retryable', () => {
        expect(classifyOcrFailure('OCR', { status: 403, details: { detail: 'Apenas compradores podem extrair cotações por OCR.' } }, FILE)).toMatchObject({ kind: 'AUTH', retryable: false, message: 'Apenas compradores podem extrair cotações por OCR.' });
        expect(classifyOcrFailure('UPLOAD', { status: 401 }, FILE).kind).toBe('AUTH');
    });
    it('5xx (incl. the Portal PORTAL_ERROR envelope) is a PROVIDER failure, retryable, stage-aware', () => {
        const e = classifyOcrFailure('OCR', { status: 500, details: { success: false, status: { code: 'PORTAL_ERROR', qualityScore: 0 } } }, FILE);
        expect(e.kind).toBe('PROVIDER');
        expect(e.retryable).toBe(true);
        expect(e.code).toBe('PORTAL_ERROR');
        expect(e.title).toBe('Falha na extração do documento');
        expect(e.guidance).toContain('não será duplicado');
        expect(classifyOcrFailure('UPLOAD', { status: 503 }, FILE).title).toBe('Falha ao carregar o documento');
    });
    it('no HTTP status (fetch TypeError / aborted) is NETWORK, retryable', () => {
        const e = classifyOcrFailure('OCR', new TypeError('Failed to fetch'), FILE);
        expect(e.kind).toBe('NETWORK');
        expect(e.retryable).toBe(true);
        expect(e.title).toBe('Sem resposta do servidor');
        expect(ocrErrorTechnicalLine(e)).toBe('Extração OCR');
    });
    it('never includes a stack trace in the user-facing fields', () => {
        const err = Object.assign(new Error('boom'), { status: 500, stack: 'Error: boom\n    at secret/path.ts:1:1' });
        const e = classifyOcrFailure('OCR', err, FILE);
        for (const v of [e.title, e.message, e.guidance, ocrErrorTechnicalLine(e)]) expect(v).not.toContain('secret/path');
    });
});

describe('describeOcrResultFailure — a 200 OK whose envelope says success:false', () => {
    it('is a PROVIDER failure (retryable) and never a draft', () => {
        const e = describeOcrResultFailure({ success: false, status: { code: 'ERROR', qualityScore: 0 } }, FILE);
        expect(e).not.toBeNull();
        expect(e!.kind).toBe('PROVIDER');
        expect(e!.retryable).toBe(true);
        expect(e!.title).toBe('A extração não produziu dados utilizáveis');
    });
    it('a blocked OCR module is not retryable and points to manual entry', () => {
        const e = describeOcrResultFailure({ success: false, status: { code: 'OCR_MODULE_BLOCKED' } }, FILE)!;
        expect(e.retryable).toBe(false);
        expect(e.guidance).toContain('Inserir Manualmente');
    });
    it('a successful envelope (or no envelope) is not a failure', () => {
        expect(describeOcrResultFailure({ success: true, items: [] }, FILE)).toBeNull();
        expect(describeOcrResultFailure(undefined, FILE)).toBeNull();
    });
});
