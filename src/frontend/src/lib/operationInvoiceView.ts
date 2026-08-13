import { ApiError } from './api';
import type {
    OperationInvoiceDto,
    OperationInvoiceObligationDto
} from '../types/operationInvoice';

/**
 * Release 4 Phase 3B — pure derivations for the Operation Invoice UI.
 *
 * Everything here is a function of its arguments: no React, no fetch. Business labels live in one
 * place so the group card, the Finance drawer and the allocation wizard can never disagree. Raw
 * enum strings are never shown to users.
 */

// ── Group aggregate status labels ───────────────────────────────────────────────────────────

interface StatusPresentation {
    label: string;
    severity: 'success' | 'warning' | 'error' | 'info' | 'muted';
}

const AGGREGATE_STATUS_LABELS: Record<string, StatusPresentation> = {
    UNCLASSIFIED: { label: 'Classificação Pendente', severity: 'muted' },
    NOT_REQUIRED: { label: 'Fatura Final Não Obrigatória', severity: 'muted' },
    PENDING_UPLOAD: { label: 'Aguardando Fatura Final', severity: 'warning' },
    PENDING_VALIDATION: { label: 'Fatura em Validação', severity: 'info' },
    PARTIALLY_INVOICED: { label: 'Parcialmente Faturado', severity: 'warning' },
    SATISFIED: { label: 'Fatura Final Completa', severity: 'success' },
    DIVERGENCE_DETECTED: { label: 'Divergência em Análise', severity: 'error' }
};

/**
 * The group aggregate as the user reads it. ClosedShort overrides the SATISFIED wording: a group
 * closed below its expected total is complete BY DECISION, and must never present as 100% invoiced.
 */
export function aggregateStatusPresentation(
    status: string | null | undefined, closedShort: boolean
): StatusPresentation {
    if (closedShort && status === 'SATISFIED') {
        return { label: 'Encerrado com Saldo Aceite', severity: 'success' };
    }
    return AGGREGATE_STATUS_LABELS[status ?? ''] ?? { label: status || '—', severity: 'muted' };
}

// ── Document status labels ──────────────────────────────────────────────────────────────────

const DOCUMENT_STATUS_LABELS: Record<string, StatusPresentation> = {
    UPLOADED: { label: 'Registada', severity: 'info' },
    PENDING_VALIDATION: { label: 'Aguarda Validação', severity: 'info' },
    VALIDATED: { label: 'Validada', severity: 'success' },
    REJECTED: { label: 'Rejeitada', severity: 'error' },
    VOIDED: { label: 'Anulada', severity: 'muted' },
    REPLACEMENT_REQUESTED: { label: 'Substituída', severity: 'muted' },
    DIVERGENCE_DETECTED: { label: 'Divergência Detetada', severity: 'error' }
};

export function documentStatusPresentation(status: string | null | undefined): StatusPresentation {
    return DOCUMENT_STATUS_LABELS[status ?? ''] ?? { label: status || '—', severity: 'muted' };
}

/** The document statuses whose allocations may still be edited (backend drafting window). */
export function isInvoiceAwaitingDecision(status: string | null | undefined): boolean {
    return status === 'UPLOADED' || status === 'PENDING_VALIDATION';
}

/** Header fields editable — mirrors OperationInvoiceLifecyclePolicy.IsEditable. */
export function isInvoiceEditable(status: string | null | undefined): boolean {
    return isInvoiceAwaitingDecision(status);
}

// ── Dates (v2.228.2) ────────────────────────────────────────────────────────────────────────

/**
 * CALENDAR date formatter for DateOnly-like business fields (DocumentDate, DueDate).
 *
 * <p>The API serializes these as offsetless strings ("2026-08-12T00:00:00"); `new Date(...)`
 * would parse that as browser-LOCAL time, and any UTC-based re-formatting then shifts the date
 * by one day on a UTC+ browser (12/08 → 11/08 — the exact TEST defect). A calendar date is not
 * an instant, so no Date object is involved at all: pure string slicing, timezone-proof.</p>
 */
export function formatDateOnly(value: string | null | undefined): string {
    if (!value) return '—';
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
    if (!match) return value;
    return `${match[3]}/${match[2]}/${match[1]}`;
}

/**
 * INSTANT formatter for UTC audit timestamps (UploadedAtUtc, ValidatedAtUtc, ProposedAtUtc, …).
 *
 * <p>These ARE instants and are parsed with Date — but the backend serializes them without an
 * offset after an EF round-trip (Kind=Unspecified), so the value is normalized to its true UTC
 * meaning (trailing "Z") BEFORE parsing; display then follows the Portal convention of the
 * browser-local date. Never use this for DateOnly-like business dates.</p>
 */
export function formatUtcTimestampDate(value: string | null | undefined): string {
    if (!value) return '—';
    const normalized = /(Z|[+-]\d{2}:?\d{2})$/.test(value)
        ? value
        : /T/.test(value) ? `${value}Z` : value;
    const parsed = new Date(normalized);
    return isNaN(parsed.getTime()) ? value : parsed.toLocaleDateString('pt-BR');
}

// ── Money ───────────────────────────────────────────────────────────────────────────────────

export function formatMoney(amount: number | null | undefined, currency: string | null | undefined): string {
    if (amount == null) return '—';
    try {
        return new Intl.NumberFormat('pt-AO', { style: 'currency', currency: currency || 'AOA' }).format(amount);
    } catch {
        return `${new Intl.NumberFormat('pt-AO').format(amount)} ${currency ?? ''}`.trim();
    }
}

// ── Coverage presentation ───────────────────────────────────────────────────────────────────

export interface CoverageView {
    /** "Valor esperado ainda não definido" when the finish line was never captured. */
    expectedLabel: string;
    validatedLabel: string;
    pendingLabel: string;
    remainingLabel: string;
    /** Null when no percentage is honest (unknown expected). */
    percent: number | null;
    hasExpected: boolean;
}

/**
 * The five numbers of a group's coverage, formatted once for every screen.
 * An unknown expected total is stated as unknown — NEVER as "0 AOA".
 */
export function coverageView(obligation: OperationInvoiceObligationDto): CoverageView {
    const currency = obligation.expectedCurrency ?? obligation.currency;
    const hasExpected = obligation.expectedAmount != null && obligation.expectedAmount > 0;

    return {
        expectedLabel: hasExpected
            ? formatMoney(obligation.expectedAmount, currency)
            : 'Valor esperado ainda não definido',
        validatedLabel: formatMoney(obligation.validatedCoveredAmount, currency),
        pendingLabel: formatMoney(obligation.pendingCoveredAmount, currency),
        remainingLabel: obligation.remainingAmount != null
            ? formatMoney(obligation.remainingAmount, currency)
            : '—',
        percent: obligation.coveragePercent ?? null,
        hasExpected
    };
}

/** Groups the allocation wizard may target: backend obligation eligibility, mirrored for UX only. */
export function isGroupAllocatable(obligation: OperationInvoiceObligationDto): boolean {
    return obligation.requiresOperationInvoice &&
        obligation.derivedStatus !== 'NOT_REQUIRED' &&
        obligation.derivedStatus !== 'UNCLASSIFIED';
}

/** A short-close proposal makes sense only with a real remaining amount beyond tolerance. */
export function isShortCloseProposable(obligation: OperationInvoiceObligationDto): boolean {
    return isGroupAllocatable(obligation) &&
        !obligation.closedShort &&
        obligation.expectedAmount != null && obligation.expectedAmount > 0 &&
        obligation.remainingAmount != null &&
        obligation.remainingAmount > obligation.appliedTolerance;
}

// ── Structured backend error mapping ────────────────────────────────────────────────────────

/**
 * Phase 3A business codes → precise Portuguese messages. Anything unknown falls back to the
 * backend's own detail text (which is already user-worded) — never a generic "Erro inesperado"
 * unless there is genuinely nothing better.
 */
const ERROR_MESSAGES: Record<string, string> = {
    OI_ALLOC_NOT_EDITABLE:
        'A distribuição desta fatura já não pode ser alterada — a fatura já foi decidida.',
    OI_ALLOC_GROUP_INVALID:
        'Um dos grupos selecionados não está elegível para receber esta fatura.',
    OI_ALLOC_SUPPLIER_MISMATCH:
        'O fornecedor da fatura não corresponde ao fornecedor do grupo selecionado.',
    OI_ALLOC_CURRENCY_MISMATCH:
        'A moeda da fatura não corresponde à moeda do grupo selecionado.',
    OI_ALLOC_INVOICE_OVER:
        'A soma das distribuições ultrapassa o total da própria fatura.',
    OI_ALLOC_GROUP_OVER:
        'A distribuição excede o valor esperado deste grupo.',
    OI_VALIDATE_ALLOCATION_INCOMPLETE:
        'A soma das distribuições não corresponde ao total da fatura. Distribua o valor completo antes de validar.',
    OI_VALIDATE_DIVERGENCE_REQUIRED:
        'Existe um grupo acima do valor esperado — a validação exige a aceitação explícita da divergência.',
    OPERATION_INVOICE_NO_OBLIGATION:
        'Este pedido ainda não possui um grupo classificado que exija Fatura Final.',
    OPERATION_INVOICE_DUPLICATE:
        'Já existe uma fatura registada com este fornecedor, número e série.',
    OPERATION_INVOICE_FILE_DUPLICATE:
        'Este ficheiro já corresponde a uma fatura final registada no Portal.',
    OPERATION_INVOICE_NOT_EDITABLE:
        'Esta fatura já não pode ser alterada.',
    OPERATION_INVOICE_NOT_VOIDABLE:
        'Só uma fatura ainda não validada pode ser anulada.',
    OPERATION_INVOICE_NOT_REPLACEABLE:
        'Esta fatura não pode ser substituída.',
    OPERATION_INVOICE_EVIDENCE_EXISTS:
        'A fatura já tem distribuições ou reconciliações associadas e não pode ser substituída.',
    OI_SHORTCLOSE_NOT_ELIGIBLE:
        'Este grupo não está num estado que permita encerramento com saldo.',
    OI_SHORTCLOSE_NOTHING_REMAINING:
        'A cobertura validada já satisfaz o valor esperado — não existe saldo a encerrar.',
    OI_SHORTCLOSE_ACTIVE_EXISTS:
        'Já existe uma proposta de encerramento ativa para este grupo.',
    OI_SHORTCLOSE_NOT_DECIDABLE:
        'Esta proposta de encerramento já foi decidida.',
    OI_SHORTCLOSE_SELF_APPROVAL:
        'Quem propôs o encerramento não pode aprová-lo — é necessária uma segunda pessoa.'
};

/** Concurrency codes — every 409 of this family gets the reload guidance. */
const CONCURRENCY_CODES = new Set([
    'OPERATION_INVOICE_CONCURRENCY',
    'OI_SHORTCLOSE_CONCURRENCY'
]);

export interface MappedApiError {
    message: string;
    /** The backend's structured code, when present. */
    code: string | null;
    /** True → offer "Recarregar dados" and never resubmit stale values. */
    isConcurrency: boolean;
    /** ProblemDetails extensions (expectedTotal, tolerance, requestPoGroupId, …) when present. */
    extensions: Record<string, unknown>;
}

export function mapOperationInvoiceError(error: unknown): MappedApiError {
    if (error instanceof ApiError) {
        const code = error.errorCode ?? null;
        const backendDetail: string | undefined = error.details?.detail || error.details?.title;
        const isConcurrency = !!code && CONCURRENCY_CODES.has(code);

        return {
            message: isConcurrency
                ? 'Os dados desta fatura ou grupo foram alterados por outro utilizador.'
                : (code && ERROR_MESSAGES[code]) || backendDetail || error.message,
            code,
            isConcurrency,
            extensions: extractExtensions(error.details)
        };
    }

    return {
        message: error instanceof Error ? error.message : 'Erro de comunicação.',
        code: null,
        isConcurrency: false,
        extensions: {}
    };
}

function extractExtensions(details: any): Record<string, unknown> {
    if (!details || typeof details !== 'object') return {};
    // ProblemDetails extensions are serialized flat next to the standard members.
    const { type, title, status, detail, instance, errors, ...rest } = details;
    return rest;
}
