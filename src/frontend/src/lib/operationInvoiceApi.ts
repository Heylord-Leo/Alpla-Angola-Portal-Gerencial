import { apiFetch, API_BASE_URL, ApiError } from './api';
import type {
    OperationInvoiceDto,
    SaveOperationInvoiceDto,
    ReplaceOperationInvoiceDto,
    CheckOperationInvoiceDuplicateDto,
    OperationInvoiceDuplicateResultDto,
    OperationInvoiceAllocationDto,
    SaveOperationInvoiceAllocationsDto,
    ValidateOperationInvoiceDto,
    OperationInvoiceObligationsDto,
    OperationInvoiceShortCloseDto,
    ProposeOperationInvoiceShortCloseDto,
    DecideOperationInvoiceShortCloseDto
} from '../types/operationInvoice';

/**
 * Release 4 Phase 3B — API client for the Operation Invoice ("Fatura Final") workflow.
 *
 * Thin pass-through over the existing backend contracts; no financial rule lives here. Errors are
 * rethrown as ApiError carrying the backend's ProblemDetails (including `extensions.code` and the
 * numeric context of Phase 3A business errors) so the wizard can render precise messages — see
 * `operationInvoiceView.ts` for the code → message mapping.
 */

async function fail(response: Response, defaultMsg: string): Promise<never> {
    const errJson = await response.json().catch(() => null);
    const msg = errJson?.detail || errJson?.title || errJson?.message || defaultMsg;

    if (response.status === 400 && errJson?.errors) {
        throw new ApiError(errJson.title || 'Existem campos inválidos.', response.status, errJson.errors,
            undefined, undefined, undefined, errJson);
    }

    throw new ApiError(
        msg,
        response.status,
        undefined,
        errJson?.correlationId,
        errJson?.code || errJson?.errorCode || errJson?.extensions?.code,
        undefined,
        errJson
    );
}

const base = (requestId: string) => `${API_BASE_URL}/api/v1/requests/${requestId}/operation-invoices`;

const JSON_HEADERS = { 'Content-Type': 'application/json' };

export const operationInvoiceApi = {
    // ── Documents (Phase 2) ─────────────────────────────────────────────────────────────────

    list: async (requestId: string): Promise<OperationInvoiceDto[]> => {
        const response = await apiFetch(base(requestId));
        if (!response.ok) return fail(response, 'Falha ao carregar as faturas finais.');
        return response.json();
    },

    get: async (requestId: string, invoiceId: string): Promise<OperationInvoiceDto> => {
        const response = await apiFetch(`${base(requestId)}/${invoiceId}`);
        if (!response.ok) return fail(response, 'Falha ao carregar a fatura final.');
        return response.json();
    },

    checkDuplicate: async (
        requestId: string, dto: CheckOperationInvoiceDuplicateDto
    ): Promise<OperationInvoiceDuplicateResultDto> => {
        const response = await apiFetch(`${base(requestId)}/check-duplicate`, {
            method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(dto)
        });
        if (!response.ok) return fail(response, 'Falha ao verificar duplicidade da fatura.');
        return response.json();
    },

    create: async (requestId: string, dto: SaveOperationInvoiceDto): Promise<OperationInvoiceDto> => {
        const response = await apiFetch(base(requestId), {
            method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(dto)
        });
        if (!response.ok) return fail(response, 'Falha ao registar a fatura final.');
        return response.json();
    },

    update: async (
        requestId: string, invoiceId: string, dto: SaveOperationInvoiceDto
    ): Promise<OperationInvoiceDto> => {
        const response = await apiFetch(`${base(requestId)}/${invoiceId}`, {
            method: 'PUT', headers: JSON_HEADERS, body: JSON.stringify(dto)
        });
        if (!response.ok) return fail(response, 'Falha ao atualizar a fatura final.');
        return response.json();
    },

    void: async (
        requestId: string, invoiceId: string, reason: string, rowVersion?: string | null
    ): Promise<OperationInvoiceDto> => {
        const response = await apiFetch(`${base(requestId)}/${invoiceId}/void`, {
            method: 'POST', headers: JSON_HEADERS, body: JSON.stringify({ reason, rowVersion })
        });
        if (!response.ok) return fail(response, 'Falha ao anular a fatura final.');
        return response.json();
    },

    replace: async (
        requestId: string, invoiceId: string, dto: ReplaceOperationInvoiceDto
    ): Promise<OperationInvoiceDto> => {
        const response = await apiFetch(`${base(requestId)}/${invoiceId}/replace`, {
            method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(dto)
        });
        if (!response.ok) return fail(response, 'Falha ao substituir a fatura final.');
        return response.json();
    },

    validate: async (
        requestId: string, invoiceId: string, dto: ValidateOperationInvoiceDto
    ): Promise<OperationInvoiceDto> => {
        const response = await apiFetch(`${base(requestId)}/${invoiceId}/validate`, {
            method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(dto)
        });
        if (!response.ok) return fail(response, 'Falha ao validar a fatura final.');
        return response.json();
    },

    reject: async (
        requestId: string, invoiceId: string, reason: string, rowVersion?: string | null
    ): Promise<OperationInvoiceDto> => {
        const response = await apiFetch(`${base(requestId)}/${invoiceId}/reject`, {
            method: 'POST', headers: JSON_HEADERS, body: JSON.stringify({ reason, rowVersion })
        });
        if (!response.ok) return fail(response, 'Falha ao rejeitar a fatura final.');
        return response.json();
    },

    // ── Allocations (Phase 3A) ──────────────────────────────────────────────────────────────

    getAllocations: async (
        requestId: string, invoiceId: string
    ): Promise<OperationInvoiceAllocationDto[]> => {
        const response = await apiFetch(`${base(requestId)}/${invoiceId}/allocations`);
        if (!response.ok) return fail(response, 'Falha ao carregar a distribuição da fatura.');
        return response.json();
    },

    saveAllocations: async (
        requestId: string, invoiceId: string, dto: SaveOperationInvoiceAllocationsDto
    ): Promise<OperationInvoiceAllocationDto[]> => {
        const response = await apiFetch(`${base(requestId)}/${invoiceId}/allocations`, {
            method: 'PUT', headers: JSON_HEADERS, body: JSON.stringify(dto)
        });
        if (!response.ok) return fail(response, 'Falha ao guardar a distribuição da fatura.');
        return response.json();
    },

    // ── Coverage read model (the ONE authoritative source) ──────────────────────────────────

    getObligations: async (requestId: string): Promise<OperationInvoiceObligationsDto> => {
        const response = await apiFetch(
            `${API_BASE_URL}/api/v1/requests/${requestId}/operation-invoice-obligations`);
        if (!response.ok) return fail(response, 'Falha ao carregar a cobertura de faturas finais.');
        return response.json();
    },

    // ── Short-close (Phase 3A) ──────────────────────────────────────────────────────────────

    listShortCloses: async (
        requestId: string, groupId: string
    ): Promise<OperationInvoiceShortCloseDto[]> => {
        const response = await apiFetch(
            `${API_BASE_URL}/api/v1/requests/${requestId}/po-groups/${groupId}/operation-invoice-short-close`);
        if (!response.ok) return fail(response, 'Falha ao carregar propostas de encerramento.');
        return response.json();
    },

    proposeShortClose: async (
        requestId: string, groupId: string, dto: ProposeOperationInvoiceShortCloseDto
    ): Promise<OperationInvoiceShortCloseDto> => {
        const response = await apiFetch(
            `${API_BASE_URL}/api/v1/requests/${requestId}/po-groups/${groupId}/operation-invoice-short-close`, {
                method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(dto)
            });
        if (!response.ok) return fail(response, 'Falha ao propor o encerramento com saldo.');
        return response.json();
    },

    approveShortClose: async (
        requestId: string, groupId: string, shortCloseId: string,
        dto: DecideOperationInvoiceShortCloseDto
    ): Promise<OperationInvoiceShortCloseDto> => {
        const response = await apiFetch(
            `${API_BASE_URL}/api/v1/requests/${requestId}/po-groups/${groupId}/operation-invoice-short-close/${shortCloseId}/approve`, {
                method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(dto)
            });
        if (!response.ok) return fail(response, 'Falha ao aprovar o encerramento com saldo.');
        return response.json();
    },

    rejectShortClose: async (
        requestId: string, groupId: string, shortCloseId: string,
        dto: DecideOperationInvoiceShortCloseDto
    ): Promise<OperationInvoiceShortCloseDto> => {
        const response = await apiFetch(
            `${API_BASE_URL}/api/v1/requests/${requestId}/po-groups/${groupId}/operation-invoice-short-close/${shortCloseId}/reject`, {
                method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(dto)
            });
        if (!response.ok) return fail(response, 'Falha ao decidir o encerramento com saldo.');
        return response.json();
    }
};
