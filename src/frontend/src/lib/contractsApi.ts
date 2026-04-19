import { apiFetch, API_BASE_URL, ApiError } from './api';

const BASE = `${API_BASE_URL}/api/v1/contracts`;

// ─── Types ───

export interface ContractListItem {
    id: string;
    contractNumber: string;
    title: string;
    statusCode: string;
    contractTypeName: string;
    contractTypeCode: string;
    supplierName?: string;
    counterpartyName?: string;
    companyName?: string;
    plantName?: string;
    departmentName?: string;
    effectiveDateUtc: string;
    expirationDateUtc: string;
    totalContractValue?: number;
    currencyCode?: string;
    createdAtUtc: string;
    obligationCount: number;
    pendingObligationCount: number;
}

export interface ContractSummary {
    totalContracts: number;
    activeContracts: number;
    expiringIn30Days: number;
    draftContracts: number;
    suspendedContracts: number;
}

export interface ContractListResponse {
    items: ContractListItem[];
    totalCount: number;
    page: number;
    pageSize: number;
    summary: ContractSummary;
}

export interface ContractDocument {
    id: string;
    documentType: string;
    fileName: string;
    contentType?: string;
    fileSizeBytes: number;
    description?: string;
    versionNumber: number;
    uploadedAtUtc: string;
    uploadedByUserName: string;
}

export interface ContractHistory {
    id: string;
    eventType: string;
    fromStatusCode?: string;
    toStatusCode?: string;
    comment: string;
    occurredAtUtc: string;
    actorUserName: string;
}

export interface ContractAlert {
    id: string;
    alertType: string;
    triggerDateUtc: string;
    isDismissed: boolean;
    message?: string;
    createdAtUtc: string;
    contractId: string;
    contractNumber?: string;
    contractTitle?: string;
    contractStatusCode?: string;
}

export interface Obligation {
    id: string;
    sequenceNumber: number;
    description?: string;
    expectedAmount: number;
    currencyId?: number;
    currencyCode?: string;
    dueDateUtc: string;
    statusCode: string;
    createdAtUtc: string;
    linkedRequestId?: string;
    linkedRequestNumber?: string;
    linkedRequestStatusCode?: string;
}

export interface ContractDetail {
    id: string;
    contractNumber: string;
    title: string;
    description?: string;
    statusCode: string;
    contractTypeId: number;
    contractTypeName: string;
    contractTypeCode: string;
    supplierId?: number;
    supplierName?: string;
    counterpartyName?: string;
    departmentId: number;
    departmentName?: string;
    companyId: number;
    companyName?: string;
    plantId?: number;
    plantName?: string;
    signedAtUtc?: string;
    effectiveDateUtc: string;
    expirationDateUtc: string;
    terminatedAtUtc?: string;
    autoRenew: boolean;
    renewalNoticeDays?: number;
    totalContractValue?: number;
    currencyId?: number;
    currencyCode?: string;
    paymentTerms?: string;
    governingLaw?: string;
    terminationClauses?: string;
    ocrValidatedByUser: boolean;
    createdAtUtc: string;
    createdByUserName: string;
    updatedAtUtc?: string;
    documents: ContractDocument[];
    obligations: Obligation[];
    histories: ContractHistory[];
    alerts: ContractAlert[];
}

export interface CreateContractPayload {
    title: string;
    description?: string;
    contractTypeId: number;
    supplierId?: number;
    counterpartyName?: string;
    departmentId: number;
    companyId: number;
    plantId?: number;
    signedAtUtc?: string;
    effectiveDateUtc: string;
    expirationDateUtc: string;
    autoRenew: boolean;
    renewalNoticeDays?: number;
    totalContractValue?: number;
    currencyId?: number;
    paymentTerms?: string;
    governingLaw?: string;
    terminationClauses?: string;
}

export interface CreateObligationPayload {
    description?: string;
    expectedAmount: number;
    currencyId?: number;
    dueDateUtc: string;
}

export interface ContractType {
    id: number;
    code: string;
    name: string;
}

export interface GenerateRequestResult {
    requestId: string;
    requestNumber: string;
    message: string;
}

// ─── Helpers ───

async function handleResponse<T>(response: Response): Promise<T> {
    if (!response.ok) {
        const text = await response.text();
        throw new ApiError(text || `HTTP ${response.status}`, response.status);
    }
    return response.json();
}

// ─── API Functions ───

export async function fetchContracts(params: Record<string, string | number | boolean> = {}): Promise<ContractListResponse> {
    const query = new URLSearchParams();
    Object.entries(params).forEach(([k, v]) => {
        if (v !== undefined && v !== null && v !== '') query.set(k, String(v));
    });
    const res = await apiFetch(`${BASE}?${query.toString()}`);
    return handleResponse<ContractListResponse>(res);
}

export async function fetchContractDetail(id: string): Promise<ContractDetail> {
    const res = await apiFetch(`${BASE}/${id}`);
    return handleResponse<ContractDetail>(res);
}

export async function createContract(data: CreateContractPayload): Promise<ContractDetail> {
    const res = await apiFetch(BASE, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    return handleResponse<ContractDetail>(res);
}

export async function updateContract(id: string, data: CreateContractPayload): Promise<ContractDetail> {
    const res = await apiFetch(`${BASE}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    return handleResponse<ContractDetail>(res);
}

export async function transitionContractStatus(id: string, action: string, comment?: string): Promise<void> {
    const res = await apiFetch(`${BASE}/${id}/${action}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ comment })
    });
    if (!res.ok) {
        const text = await res.text();
        throw new ApiError(text || `HTTP ${res.status}`, res.status);
    }
}

export async function createObligation(contractId: string, data: CreateObligationPayload): Promise<Obligation> {
    const res = await apiFetch(`${BASE}/${contractId}/obligations`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    return handleResponse<Obligation>(res);
}

export async function updateObligation(contractId: string, obligationId: string, data: CreateObligationPayload): Promise<Obligation> {
    const res = await apiFetch(`${BASE}/${contractId}/obligations/${obligationId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    return handleResponse<Obligation>(res);
}

export async function cancelObligation(contractId: string, obligationId: string): Promise<void> {
    const res = await apiFetch(`${BASE}/${contractId}/obligations/${obligationId}`, { method: 'DELETE' });
    if (!res.ok) {
        const text = await res.text();
        throw new ApiError(text || `HTTP ${res.status}`, res.status);
    }
}

export async function generatePaymentRequest(contractId: string, obligationId: string): Promise<GenerateRequestResult> {
    const res = await apiFetch(`${BASE}/${contractId}/obligations/${obligationId}/generate-request`, { method: 'POST' });
    return handleResponse<GenerateRequestResult>(res);
}

export async function fetchContractTypes(): Promise<ContractType[]> {
    const res = await apiFetch(`${BASE}/types`);
    return handleResponse<ContractType[]>(res);
}

export async function fetchActiveAlerts(): Promise<ContractAlert[]> {
    const res = await apiFetch(`${BASE}/alerts`);
    return handleResponse<ContractAlert[]>(res);
}

export async function dismissAlert(alertId: string): Promise<void> {
    const res = await apiFetch(`${BASE}/alerts/${alertId}/dismiss`, { method: 'POST' });
    if (!res.ok) {
        const text = await res.text();
        throw new ApiError(text || `HTTP ${res.status}`, res.status);
    }
}

export async function uploadContractDocument(contractId: string, file: File, documentType?: string, description?: string): Promise<ContractDocument> {
    const formData = new FormData();
    formData.append('file', file);
    if (documentType) formData.append('documentType', documentType);
    if (description) formData.append('description', description);

    const res = await apiFetch(`${BASE}/${contractId}/documents`, {
        method: 'POST',
        body: formData
    });
    return handleResponse<ContractDocument>(res);
}

export function getDocumentDownloadUrl(contractId: string, documentId: string): string {
    return `${BASE}/${contractId}/documents/${documentId}/download`;
}

// ─── Status Helpers ───

export const CONTRACT_STATUS_MAP: Record<string, { label: string; color: string; bg: string }> = {
    DRAFT: { label: 'Rascunho', color: '#6b7280', bg: '#f3f4f6' },
    UNDER_REVIEW: { label: 'Em Revisão', color: '#d97706', bg: '#fef3c7' },
    ACTIVE: { label: 'Ativo', color: '#059669', bg: '#d1fae5' },
    SUSPENDED: { label: 'Suspenso', color: '#dc2626', bg: '#fee2e2' },
    EXPIRED: { label: 'Expirado', color: '#9333ea', bg: '#f3e8ff' },
    TERMINATED: { label: 'Terminado', color: '#1f2937', bg: '#e5e7eb' }
};

export const OBLIGATION_STATUS_MAP: Record<string, { label: string; color: string; bg: string }> = {
    PENDING: { label: 'Pendente', color: '#d97706', bg: '#fef3c7' },
    REQUEST_CREATED: { label: 'Pedido Criado', color: '#2563eb', bg: '#dbeafe' },
    PAID: { label: 'Pago', color: '#059669', bg: '#d1fae5' },
    CANCELLED: { label: 'Cancelado', color: '#6b7280', bg: '#f3f4f6' }
};
