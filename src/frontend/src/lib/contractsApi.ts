import { apiFetch, API_BASE_URL, ApiError } from './api';
import type {
    OcrTriggerResponse,
    OcrStatusResponse,
    OcrFieldsResponse,
    OcrConfirmRequest,
    OcrConfirmResponse,
} from '../types/contractOcr.types';

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
    /** True when this DRAFT contract was previously returned by a technical or final approver. */
    wasReturnedFromApproval: boolean;
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
    // OCR relationship (Phase 1.2)
    hasOcrRecord: boolean;
    ocrStatus?: string | null;
    /** True when this document would be auto-selected by the OCR trigger logic. */
    isPrimaryOcrSource: boolean;
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
    // Due date tracking (DEC-117)
    referenceDateUtc?: string;
    calculatedDueDateUtc?: string;
    dueDateUtc: string;
    dueDateSourceCode?: string;
    graceDateUtc?: string;
    penaltyStartDateUtc?: string;
    // Operational
    invoiceReceivedDateUtc?: string;
    serviceAcceptanceDateUtc?: string;
    billingReference?: string;
    obligationNotes?: string;
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
    // Payment Rule (DEC-117)
    paymentTermTypeCode?: string;
    referenceEventTypeCode?: string;
    paymentTermDays?: number;
    paymentFixedDay?: number;
    allowsManualDueDateOverride: boolean;
    gracePeriodDays?: number;
    hasLatePenalty: boolean;
    latePenaltyTypeCode?: string;
    latePenaltyValue?: number;
    hasLateInterest: boolean;
    lateInterestTypeCode?: string;
    lateInterestValue?: number;
    paymentRuleSummary?: string;
    financialNotes?: string;
    penaltyNotes?: string;
    governingLaw?: string;
    terminationClauses?: string;
    ocrValidatedByUser: boolean;
    ocrStatus?: string | null;
    // Two-step approval participants (DEC-118)
    technicalApproverId?: string;
    technicalApproverName?: string;
    finalApproverId?: string;
    finalApproverName?: string;
    createdAtUtc: string;
    createdByUserName: string;
    updatedAtUtc?: string;
    documents: ContractDocument[];
    obligations: Obligation[];
    histories: ContractHistory[];
    alerts: ContractAlert[];
}

// Payment rule fields shared by CreateContractPayload and UpdateContractPayload (DEC-117)
export interface PaymentRuleFields {
    paymentTermTypeCode?: string;
    referenceEventTypeCode?: string;
    paymentTermDays?: number;
    paymentFixedDay?: number;
    allowsManualDueDateOverride?: boolean;
    gracePeriodDays?: number;
    hasLatePenalty?: boolean;
    latePenaltyTypeCode?: string;
    latePenaltyValue?: number;
    hasLateInterest?: boolean;
    lateInterestTypeCode?: string;
    lateInterestValue?: number;
    /** For CUSTOM_TEXT: user-authored. For structured types: leave blank (backend auto-generates). */
    paymentRuleSummary?: string;
    financialNotes?: string;
    penaltyNotes?: string;
}

export interface CreateContractPayload extends PaymentRuleFields {
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
    /** Required for MANUAL / ADVANCE_PAYMENT / CUSTOM_TEXT rules. Optional for auto-calculated rules when AllowsManualDueDateOverride = true. */
    dueDateUtc?: string;
    /** Supply when ReferenceEventTypeCode = MANUAL_REFERENCE_DATE */
    manualReferenceDateUtc?: string;
    /** Supply when ReferenceEventTypeCode = INVOICE_RECEIVED_DATE */
    invoiceReceivedDateUtc?: string;
    serviceAcceptanceDateUtc?: string;
    billingReference?: string;
    obligationNotes?: string;
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

/** Lookup: payment term type metadata returned by GET /api/v1/contracts/payment-term-types */
export interface PaymentTermType {
    code: string;
    label: string;
    description?: string;
    isAutoCalculated: boolean;
    requiresReferenceEvent: boolean;
    requiresDays: boolean;
    requiresFixedDay: boolean;
}

/** Lookup: reference event type metadata returned by GET /api/v1/contracts/reference-event-types */
export interface ReferenceEventType {
    code: string;
    label: string;
    description?: string;
    requiresUserInput: boolean;
}

// ─── Approval Queue Types (DEC-118) ───

export interface ContractApprovalItem {
    id: string;
    contractNumber: string;
    title: string;
    statusCode: string;
    contractTypeName: string;
    supplierName?: string;
    supplierPortalCode?: string;
    counterpartyName?: string;
    departmentName: string;
    companyName: string;
    plantName?: string;
    totalContractValue?: number;
    currencyCode?: string;
    effectiveDateUtc: string;
    expirationDateUtc: string;
    paymentRuleSummary?: string;
    technicalApproverId?: string;
    technicalApproverName?: string;
    finalApproverId?: string;
    finalApproverName?: string;
    createdByUserName: string;
    createdAtUtc: string;
}

export interface PendingContractApprovalsResponse {
    technicalApprovals: ContractApprovalItem[];
    finalApprovals: ContractApprovalItem[];
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

export async function fetchPaymentTermTypes(): Promise<PaymentTermType[]> {
    const res = await apiFetch(`${BASE}/payment-term-types`);
    return handleResponse<PaymentTermType[]>(res);
}

export async function fetchReferenceEventTypes(): Promise<ReferenceEventType[]> {
    const res = await apiFetch(`${BASE}/reference-event-types`);
    return handleResponse<ReferenceEventType[]>(res);
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

// ─── Approval Workflow API (DEC-118) ───

export async function fetchPendingContractApprovals(): Promise<PendingContractApprovalsResponse> {
    const res = await apiFetch(`${BASE}/pending-approvals`);
    return handleResponse<PendingContractApprovalsResponse>(res);
}

export async function contractTechnicalApprove(id: string, comment?: string): Promise<void> {
    return transitionContractStatus(id, 'technical-approve', comment);
}

export async function contractTechnicalReturn(id: string, comment: string): Promise<void> {
    return transitionContractStatus(id, 'technical-return', comment);
}

export async function contractFinalApprove(id: string, comment?: string): Promise<void> {
    return transitionContractStatus(id, 'final-approve', comment);
}

export async function contractFinalReturn(id: string, comment: string): Promise<void> {
    return transitionContractStatus(id, 'final-return', comment);
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

/** Soft-deletes a contract document. Returns 204 on success. */
export async function deleteContractDocument(contractId: string, documentId: string): Promise<void> {
    const res = await apiFetch(`${BASE}/${contractId}/documents/${documentId}`, { method: 'DELETE' });
    if (!res.ok) {
        const text = await res.text();
        throw new ApiError(text || `HTTP ${res.status}`, res.status);
    }
}

/**
 * Triggers OCR on a specific document.
 * When documentId is provided it is passed as a query parameter so the backend
 * uses that document instead of the auto-pick logic.
 */
export async function triggerOcr(contractId: string, documentId?: string): Promise<void> {
    const url = documentId
        ? `${BASE}/${contractId}/ocr-trigger?documentId=${documentId}`
        : `${BASE}/${contractId}/ocr-trigger`;
    const res = await apiFetch(url, { method: 'POST' });
    if (!res.ok) {
        const text = await res.text();
        throw new ApiError(text || `HTTP ${res.status}`, res.status);
    }
}

// ─── Status Helpers ───

export const CONTRACT_STATUS_MAP: Record<string, { label: string; color: string; bg: string }> = {
    DRAFT: { label: 'Rascunho', color: '#6b7280', bg: '#f3f4f6' },
    // Legacy: pre-DEC-118 contracts submitted for review
    UNDER_REVIEW: { label: 'Em Revisão (Legacy)', color: '#d97706', bg: '#fef3c7' },
    // DEC-118 two-step statuses
    UNDER_TECHNICAL_REVIEW: { label: 'Revisão Técnica', color: '#0369a1', bg: '#e0f2fe' },
    UNDER_FINAL_REVIEW: { label: 'Aprovação Final', color: '#7c3aed', bg: '#ede9fe' },
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

// ─── OCR API (Phase 1) ────────────────────────────────────────────────────────

/**
 * POST /api/v1/contracts/{id}/ocr-trigger
 *
 * Selects the primary document automatically (ORIGINAL type preferred, then
 * most recent) and starts async OCR extraction.
 *
 * Returns 202 Accepted immediately. Poll getOcrStatus() to track progress.
 * Throws ApiError(409) if OCR is already PROCESSING for this contract.
 */
export async function triggerContractOcr(contractId: string): Promise<OcrTriggerResponse> {
    const res = await apiFetch(`${BASE}/${contractId}/ocr-trigger`, { method: 'POST' });
    return handleResponse<OcrTriggerResponse>(res);
}

/**
 * GET /api/v1/contracts/{id}/ocr-status
 *
 * Returns the current OCR status and latest extraction record summary.
 * Call this while ocrStatus is PENDING or PROCESSING. Stop polling when
 * ocrStatus is COMPLETED or FAILED.
 *
 * `latestRecord` is null when OCR has never been triggered for this contract.
 */
export async function getOcrStatus(contractId: string): Promise<OcrStatusResponse> {
    const res = await apiFetch(`${BASE}/${contractId}/ocr-status`);
    return handleResponse<OcrStatusResponse>(res);
}

/**
 * GET /api/v1/contracts/{id}/ocr-fields
 *
 * Returns per-field extraction rows from the latest COMPLETED record.
 * Call this once after getOcrStatus() returns COMPLETED.
 *
 * Throws ApiError(404) when no COMPLETED record exists yet.
 */
export async function getOcrFields(contractId: string): Promise<OcrFieldsResponse> {
    const res = await apiFetch(`${BASE}/${contractId}/ocr-fields`);
    return handleResponse<OcrFieldsResponse>(res);
}

/**
 * POST /api/v1/contracts/{id}/ocr-confirm
 *
 * Persists user field-level confirmation decisions.
 * Call this when the user:
 *   - clicks "Confirmar" on an individual AUTO_FILL field,
 *   - clicks "Confirmar todos" in the summary panel, or
 *   - clicks "Limpar" / "Ignorar" to discard a field.
 *
 * Prefer `fieldId` (OcrExtractedField.id) over `fieldName` in each item.
 * Fields NOT included in the request are left unchanged in the database.
 *
 * This does NOT save field values to the contract — that is done by
 * createContract() / updateContract() using the form payload.
 */
export async function confirmOcrFields(
    contractId: string,
    request: OcrConfirmRequest,
): Promise<OcrConfirmResponse> {
    const res = await apiFetch(`${BASE}/${contractId}/ocr-confirm`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request),
    });
    return handleResponse<OcrConfirmResponse>(res);
}

