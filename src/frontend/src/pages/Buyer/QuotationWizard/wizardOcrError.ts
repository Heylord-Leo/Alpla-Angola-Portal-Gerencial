// ─────────────────────────────────────────────────────────────────────────────
// v2.249.2 — import-wizard failure classification (pure, node-vitest friendly). The document step must
// show WHAT failed and WHAT to do, inside the active step: a business-rule rejection by the Portal (409/400
// ProblemDetails — e.g. "Ação Bloqueada" when the request has no quotation window) is not a provider or
// network failure, and only the latter are worth a retry. Technical details (status, correlation id) stay
// sanitized; stack traces and credentials are never surfaced.
// ─────────────────────────────────────────────────────────────────────────────

export type WizardOcrStage = 'UPLOAD' | 'OCR';
export type WizardOcrErrorKind = 'BUSINESS' | 'AUTH' | 'PROVIDER' | 'NETWORK';

export interface WizardOcrError {
    stage: WizardOcrStage;
    kind: WizardOcrErrorKind;
    /** Short heading (PT). */
    title: string;
    /** What happened, in the operator's words (PT). */
    message: string;
    /** What to do next (PT). */
    guidance: string;
    /** True when trying again can reasonably succeed (provider/network); false for business/authorization. */
    retryable: boolean;
    /** Kept so a retry reuses the already-uploaded document instead of uploading it again. */
    fileName: string;
    status?: number;
    correlationId?: string;
    /** Machine code when the API provided one (e.g. REQUEST_LINE_REFERENCE_INVALID, OCR_EXTRACTION_FAILED). */
    code?: string;
}

const STAGE_LABEL: Record<WizardOcrStage, string> = {
    UPLOAD: 'Carregamento do documento',
    OCR: 'Extração OCR',
};

function pick(details: any, ...keys: string[]): string | undefined {
    for (const k of keys) {
        const v = details?.[k];
        if (typeof v === 'string' && v.trim().length > 0) return v.trim();
    }
    return undefined;
}

/** Classify a thrown error (ApiError from lib/api, a fetch TypeError, or anything else) for one stage. */
export function classifyOcrFailure(stage: WizardOcrStage, error: unknown, fileName: string): WizardOcrError {
    const e = (error ?? {}) as any;
    const status: number | undefined = typeof e.status === 'number' ? e.status : undefined;
    const details = e.details ?? null;
    const title = pick(details, 'title');
    const detail = pick(details, 'detail');
    const code: string | undefined = e.errorCode || pick(details, 'code', 'errorCode') || pick(details?.status, 'code');
    const correlationId: string | undefined = e.correlationId || pick(details, 'correlationId');
    const base = { stage, fileName, status, correlationId, code };

    if (status === 401 || status === 403) {
        return {
            ...base, kind: 'AUTH', retryable: false,
            title: 'Sem permissão para esta ação',
            message: detail || e.message || 'A sua sessão ou o seu perfil não permite esta operação.',
            guidance: 'Verifique se é o comprador responsável pelo pedido e se a sessão continua válida. Tentar de novo não resolve.',
        };
    }
    if (status === 409 || status === 400 || status === 404 || status === 422) {
        return {
            ...base, kind: 'BUSINESS', retryable: false,
            title: title || (status === 409 ? 'Ação bloqueada pelo Portal' : 'Pedido recusado pelo Portal'),
            message: detail || e.message || 'O Portal recusou a operação com base no estado atual do pedido.',
            guidance: status === 409
                ? 'É uma regra de negócio, não uma falha técnica: o pedido ou o item não aceita uma nova cotação neste momento. Confirme no separador “Itens & Cobertura” que existe um item pendente de cotação e que é o comprador responsável; se o estado mudar, volte a tentar.'
                : 'Corrija o que a mensagem indica (ficheiro, item ou dados) e tente de novo. Não é necessário escolher outro documento se o ficheiro estiver correto.',
        };
    }
    if (status === undefined || status === 0) {
        return {
            ...base, kind: 'NETWORK', retryable: true,
            title: 'Sem resposta do servidor',
            message: 'Não foi possível concluir o pedido ao Portal (rede, sessão ou tempo de espera).',
            guidance: 'Verifique a ligação e tente novamente. O documento já carregado é reaproveitado: não será criado um duplicado.',
        };
    }
    // 5xx / 502 / 503 / 504 and the Portal's own PORTAL_ERROR envelope
    return {
        ...base, kind: 'PROVIDER', retryable: true,
        title: stage === 'OCR' ? 'Falha na extração do documento' : 'Falha ao carregar o documento',
        message: detail || (stage === 'OCR'
            ? 'O serviço de extração (OCR) não conseguiu processar o documento. O ficheiro pode estar ilegível, protegido ou o fornecedor OCR pode estar indisponível.'
            : 'O Portal não conseguiu guardar o documento.'),
        guidance: 'Tente novamente. Se persistir, insira a cotação manualmente (“Inserir Manualmente”) ou carregue um PDF com texto selecionável; o documento já carregado não será duplicado.',
    };
}

/**
 * A 200 OK whose envelope says `success: false` is an extraction (provider) failure, not a usable result:
 * the wizard must not build an empty draft from it.
 */
export function describeOcrResultFailure(result: any, fileName: string): WizardOcrError | null {
    if (!result || result.success !== false) return null;
    const code: string | undefined = typeof result?.status?.code === 'string' ? result.status.code : undefined;
    const blocked = code === 'OCR_MODULE_BLOCKED';
    return {
        stage: 'OCR', kind: 'PROVIDER', retryable: !blocked, fileName, code,
        title: blocked ? 'Extração OCR indisponível' : 'A extração não produziu dados utilizáveis',
        message: blocked
            ? 'O módulo de extração está desativado ou bloqueado para este contexto.'
            : 'O fornecedor OCR devolveu um resultado sem dados (documento ilegível, formato não suportado ou falha do fornecedor).',
        guidance: blocked
            ? 'Insira a cotação manualmente (“Inserir Manualmente”) ou contacte a administração para ativar o módulo.'
            : 'Tente novamente ou carregue um PDF com texto selecionável; em alternativa insira a cotação manualmente. O documento já carregado não será duplicado.',
    };
}

/** One-line technical footer for the alert (sanitized): stage · HTTP status · code · correlation id. */
export function ocrErrorTechnicalLine(err: WizardOcrError): string {
    const parts = [STAGE_LABEL[err.stage]];
    if (err.status) parts.push(`HTTP ${err.status}`);
    if (err.code) parts.push(err.code);
    if (err.correlationId) parts.push(`ref. ${err.correlationId.slice(0, 12)}`);
    return parts.join(' · ');
}
