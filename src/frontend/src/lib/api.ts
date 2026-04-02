import { RequestDetailsDto, RequestTimelineDto, DashboardSummaryDto, DocumentExtractionSettingsDto, RequestListResponseDto, PurchasingSummaryDto, PendingApprovalsResponseDto, ApprovalIntelligenceDto, HistoricalPurchaseRecordDto } from '../types';
import { logger, FrontendComponentKey } from './logger';

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

export class ApiError extends Error {
    public status?: number;
    public fieldErrors?: Record<string, string[]>;

    constructor(message: string, status?: number, fieldErrors?: Record<string, string[]>) {
        super(message);
        this.status = status;
        this.fieldErrors = fieldErrors;
        this.name = 'ApiError';
    }
}

export async function apiFetch(url: string, options: RequestInit = {}): Promise<Response> {
    const token = sessionStorage.getItem('auth_token');
    const headers = {
        ...options.headers,
        ...(token ? { 'Authorization': `Bearer ${token}` } : {})
    };

    try {
        return await fetch(url, { ...options, headers });
    } catch (error: any) {
        // Detailed error reporting for the browser console
        console.error(`[API ERROR] ${options.method || 'GET'} ${url}:`, error);

        // Map generic "Failed to fetch" to a more descriptive user error
        throw new ApiError(
            `Falha na ligação ao servidor (${API_BASE_URL}). Verifique se o servidor está online e acessível. (CORS ou Certificado SSL)`,
            0
        );
    }
}

async function handleApiError(
    response: Response, 
    defaultMessage: string = 'Erro de comunicação.',
    componentKey?: FrontendComponentKey
): Promise<never> {
    // Detect expired/invalid token
    // We skip the redirect if it is a login request, so the caller can handle and display the error
    if (response.status === 401 && !response.url.includes('/api/auth/login')) {
        sessionStorage.removeItem('auth_token');
        sessionStorage.removeItem('auth_user');
        window.location.href = '/login';
    }

    const errJson = await response.json().catch(() => null);
    const correlationId = response.headers.get('X-Correlation-ID') || undefined;
    // ... rest of logic

    if (componentKey) {
        logger.log({
            level: 'Error',
            eventType: 'API_REQUEST_FAILED',
            message: errJson?.detail || errJson?.title || defaultMessage,
            componentKey,
            endpoint: response.url,
            statusCode: response.status,
            correlationId
        });
    }

    let errorMsg = defaultMessage;
    if (response.status === 400 && errJson?.errors) {
        throw new ApiError(
            errJson.title || 'Existem campos inválidos.',
            response.status,
            errJson.errors
        );
    } else if (response.status === 401) {
        // Detailed log for unauthorized without sensitive info
        console.error(`[AUTH] 401 Unauthorized for ${response.url}. Ensure JWT is active and valid.`);
        errorMsg = 'Sessão expirada ou acesso não autorizado.';
    } else if (response.status === 403) {
        console.error(`[AUTH] 403 Forbidden for ${response.url}. Insufficient roles/permissions.`);
        errorMsg = 'Você não tem permissão para realizar esta ação.';
    } else if (errJson?.detail || errJson?.title) {
        errorMsg = errJson.detail || errJson.title;
    }

    throw new ApiError(
        errorMsg,
        response.status
    );
}

export const api = {
    users: {
        list: async (includeInactive = false): Promise<any[]> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/users?includeInactive=${includeInactive}`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar utilizadores.');
            return response.json();
        },
        get: async (id: string): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/users/${id}`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar dados do utilizador.');
            return response.json();
        },
        create: async (data: any): Promise<{ newPassword: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/users`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao criar utilizador.');
            return response.json();
        },
        update: async (id: string, data: any): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/users/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao atualizar utilizador.');
        },
        resetPassword: async (id: string): Promise<{ newPassword: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/users/${id}/reset-password`, {
                method: 'POST'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao repor palavra-passe.');
            return response.json();
        },
        me: async (): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/users/me`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar perfil.');
            return response.json();
        }
    },
    auth: {
        login: async (data: any): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/auth/login`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao autenticar.');
            return response.json();
        },
        changePassword: async (data: any): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/auth/change-password`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao alterar palavra-passe.');
            return response.json();
        }
    },
    requests: {
        getDashboardSummary: async (): Promise<DashboardSummaryDto> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/summary`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar sumário do dashboard.');
            return response.json();
        },
        getPurchasingSummary: async (): Promise<PurchasingSummaryDto> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/purchasing-summary`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar sumário de compras.');
            return response.json();
        },
        getPendingApprovals: async (): Promise<PendingApprovalsResponseDto> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/pending-approvals`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar centro de aprovações.');
            return response.json();
        },
        list: async (
            search?: string, 
            filters?: { statusIds?: string, typeIds?: string, plantIds?: string, companyIds?: string, departmentIds?: string, isAttention?: boolean, ivaRateId?: number }, 
            page: number = 1, 
            pageSize: number = 20
        ): Promise<RequestListResponseDto> => {
            const params = new URLSearchParams();
            if (search) params.append('search', search);
            if (filters?.statusIds) params.append('statusIds', filters.statusIds);
            if (filters?.typeIds) params.append('typeIds', filters.typeIds);
            if (filters?.plantIds) params.append('plantIds', filters.plantIds);
            if (filters?.companyIds) params.append('companyIds', filters.companyIds);
            if (filters?.departmentIds) params.append('departmentIds', filters.departmentIds);
            if (filters?.isAttention !== undefined) params.append('isAttention', String(filters.isAttention));
            if (filters?.ivaRateId !== undefined) params.append('ivaRateId', String(filters.ivaRateId));
            params.append('page', page.toString());
            params.append('pageSize', pageSize.toString());

            const queryString = params.toString();
            const url = `${API_BASE_URL}/api/v1/requests?${queryString}`;

            const response = await apiFetch(url);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar pedidos.');
            return response.json();
        },
        get: async (id: string): Promise<RequestDetailsDto> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar detalhes do pedido.');
            return response.json();
        },
        getTemplate: async (id: string): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/template`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar modelo para cópia.');
            return response.json();
        },
        getTimeline: async (id: string): Promise<RequestTimelineDto> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/timeline`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar timeline do pedido.');
            return response.json();
        },
        create: async (data: any): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(data),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao criar o pedido de rascunho.');
            return response.json();
        },
        updateDraft: async (id: string, data: any): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/draft`, {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(data),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao atualizar o pedido de rascunho.');
        },
        createLineItem: async (requestId: string, data: any): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${requestId}/line-items`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao adicionar item.');
            return response.json();
        },
        updateLineItem: async (requestId: string, itemId: string, data: any): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${requestId}/line-items/${itemId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao atualizar item.');
        },
        deleteLineItem: async (requestId: string, itemId: string): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${requestId}/line-items/${itemId}`, {
                method: 'DELETE'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao deletar item.');
        },
        remove: async (id: string): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}`, {
                method: 'DELETE'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao excluir o rascunho.');
        },
        submit: async (id: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/submit`, {
                method: 'POST'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao submeter o pedido.');
            return response.json();
        },
        cancel: async (id: string, reason: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/cancel`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ reason }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao cancelar o pedido.');
            return response.json();
        },
        duplicate: async (id: string): Promise<{ id: string; title: string; statusCode: string; createdAtUtc: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/duplicate`, {
                method: 'POST'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao duplicar o pedido.');
            return response.json();
        },
        approveArea: async (id: string, comment?: string, selectedQuotationId?: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/area-approval/approve`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment, selectedQuotationId }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao aprovar o pedido.');
            return response.json();
        },
        rejectArea: async (id: string, comment: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/area-approval/reject`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao rejeitar o pedido.');
            return response.json();
        },
        requestAdjustmentArea: async (id: string, comment: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/area-approval/request-adjustment`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao solicitar ajuste no pedido.');
            return response.json();
        },
        approveFinal: async (id: string, comment?: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/final-approval/approve`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao aprovar o pedido (Aprovação Final).');
            return response.json();
        },
        rejectFinal: async (id: string, comment: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/final-approval/reject`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao rejeitar o pedido (Aprovação Final).');
            return response.json();
        },
        requestAdjustmentFinal: async (id: string, comment: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/final-approval/request-adjustment`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao solicitar ajuste no pedido (Aprovação Final).');
            return response.json();
        },
        registerPo: async (id: string, comment?: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/operational/register-po`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao registrar P.O.');
            return response.json();
        },
        updateSupplier: async (id: string, supplierId: number | null): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/supplier`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ supplierId }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao atualizar fornecedor do pedido.');
        },
        schedulePayment: async (id: string, comment?: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/operational/schedule-payment`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao agendar pagamento.');
            return response.json();
        },
        completePayment: async (id: string, comment?: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/operational/complete-payment`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao completar pagamento.');
            return response.json();
        },
        moveToReceipt: async (id: string, comment?: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/operational/move-to-receipt`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao mover para aguardando recibo.');
            return response.json();
        },
        finalize: async (id: string, comment?: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/operational/finalize`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao finalizar o pedido.');
            return response.json();
        },
        completeQuotation: async (id: string, comment?: string): Promise<{ message: string; statusCode: string }> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/quotation/complete`, {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao concluir cotação.');
            return response.json();
        },
        ocrExtract: async (id: string, file: File): Promise<any> => {
            const formData = new FormData();
            formData.append('file', file);
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${id}/ocr-extract`, {
                method: 'POST',
                body: formData,
            });
            if (!response.ok) return handleApiError(response, 'Falha ao processar OCR do documento.', 'OcrSettings');
            return response.json();
        },
        saveQuotation: async (requestId: string, quotation: any, replaceQuotationId?: string): Promise<any> => {
            const url = replaceQuotationId 
                ? `${API_BASE_URL}/api/v1/requests/${requestId}/quotations?replaceQuotationId=${replaceQuotationId}`
                : `${API_BASE_URL}/api/v1/requests/${requestId}/quotations`;
            const response = await apiFetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(quotation),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao salvar cotação.');
            return response.json();
        },
        selectQuotation: async (requestId: string, quotationId: string): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${requestId}/quotations/${quotationId}/select`, {
                method: 'POST'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao selecionar a cotação vencedora.');
        },
        updateQuotation: async (requestId: string, quotationId: string, quotation: any): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${requestId}/quotations/${quotationId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(quotation),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao atualizar cotação.');
            return response.json();
        },
        deleteQuotation: async (requestId: string, quotationId: string): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/requests/${requestId}/quotations/${quotationId}`, {
                method: 'DELETE'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao excluir cotação.');
        },
        updateItemReceiving: async (quotationItemId: string, receivedQuantity: number, divergenceNotes?: string): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/quotation-items/${quotationItemId}/receiving`, {
                method: 'PATCH',
                headers: { 
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ receivedQuantity, divergenceNotes }),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao registrar recebimento do item da cotação.');
        }
    },
    approvals: {
        getIntelligence: async (id: string): Promise<ApprovalIntelligenceDto> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/approvals/${id}/intelligence`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar inteligência de decisão.');
            return response.json();
        },
        getItemHistory: async (id: string, lineItemId: string): Promise<HistoricalPurchaseRecordDto[]> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/approvals/${id}/items/${lineItemId}/history`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar histórico detalhado do item.');
            return response.json();
        }
    },
    dev: {
        seedIntelligence: async (): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/dev/seed-intelligence`, { method: 'POST' });
            if (!response.ok) return handleApiError(response, 'Falha ao semear dados de inteligência.');
            return response.json();
        },
        cleanupIntelligence: async (): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/dev/cleanup-intelligence`, { method: 'DELETE' });
            if (!response.ok) return handleApiError(response, 'Falha ao limpar dados de inteligência.');
            return response.json();
        }
    },
    notifications: {
        list: async (): Promise<any[]> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/notifications`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar notificações.');
            return response.json();
        },
        markAsRead: async (id: string): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/notifications/${id}/read`, {
                method: 'POST'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao marcar notificação como lida.');
        },
        markAllAsRead: async (): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/notifications/mark-all-read`, {
                method: 'POST'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao marcar todas como lidas.');
        },
        clearRead: async (): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/notifications/clear-read`, {
                method: 'POST'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao limpar notificações lidas.');
        }
    },
    attachments: {
        upload: async (requestId: string, files: File[], typeCode: string): Promise<any> => {
            const formData = new FormData();
            files.forEach(file => {
                formData.append('files', file);
            });
            // We only send the 'files' array to avoid duplication in newer controller versions
            // but the controller still handles the single 'file' field for backward compatibility if needed.
            formData.append('typeCode', typeCode);

            const response = await apiFetch(`${API_BASE_URL}/api/v1/attachments/upload/${requestId}`, {
                method: 'POST',
                body: formData,
            });
            if (!response.ok) return handleApiError(response, 'Falha ao carregar anexo.');
            return response.json();
        },
        download: async (id: string, fileName: string): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/attachments/${id}/download`);
            if (!response.ok) return handleApiError(response, 'Falha ao descarregar anexo.');

            const blob = await response.blob();
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            window.URL.revokeObjectURL(url);
            document.body.removeChild(a);
        },
        delete: async (id: string): Promise<void> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/attachments/${id}`, {
                method: 'DELETE'
            });
            if (!response.ok) return handleApiError(response, 'Falha ao remover anexo.');
        }
    },
    lookups: {
        getRequestStatuses: async (includeInactive = false): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/request-statuses?includeInactive=${includeInactive}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar status dos pedidos.');
            return res.json();
        },
        getUnits: async (includeInactive = false): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/units?includeInactive=${includeInactive}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar unidades.');
            return res.json();
        },
        createUnit: async (data: any): Promise<any> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/units`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao salvar unidade.');
            return res.json();
        },
        updateUnit: async (id: number, data: any): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/units/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao alterar unidade.');
        },
        toggleUnit: async (id: number): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/units/${id}/toggle-active`, { method: 'PUT' });
            if (!res.ok) return handleApiError(res, 'Falha ao alternar estado da unidade.');
        },
        getCurrencies: async (includeInactive = false): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/currencies?includeInactive=${includeInactive}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar moedas.');
            return res.json();
        },
        createCurrency: async (data: any): Promise<any> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/currencies`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao salvar moeda.');
            return res.json();
        },
        updateCurrency: async (id: number, data: any): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/currencies/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao alterar moeda.');
        },
        toggleCurrency: async (id: number): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/currencies/${id}/toggle-active`, { method: 'PUT' });
            if (!res.ok) return handleApiError(res, 'Falha ao alternar estado da moeda.');
        },
        getNeedLevels: async (includeInactive = false): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/need-levels?includeInactive=${includeInactive}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar níveis de necessidade.');
            return res.json();
        },
        createNeedLevel: async (data: any): Promise<any> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/need-levels`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao salvar nível de necessidade.');
            return res.json();
        },
        updateNeedLevel: async (id: number, data: any): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/need-levels/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao alterar nível de necessidade.');
        },
        toggleNeedLevel: async (id: number): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/need-levels/${id}/toggle-active`, { method: 'PUT' });
            if (!res.ok) return handleApiError(res, 'Falha ao alternar estado do nível de necessidade.');
        },
        getDepartments: async (includeInactive = false): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/departments?includeInactive=${includeInactive}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar departamentos.');
            return res.json();
        },
        createDepartment: async (data: any): Promise<any> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/departments`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao salvar departamento.');
            return res.json();
        },
        updateDepartment: async (id: number, data: any): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/departments/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao alterar departamento.');
        },
        toggleDepartment: async (id: number): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/departments/${id}/toggle-active`, { method: 'PUT' });
            if (!res.ok) return handleApiError(res, 'Falha ao alternar estado do departamento.');
        },
        getPlants: async (companyId?: number, includeInactive = false): Promise<any[]> => {
            const params = new URLSearchParams();
            if (companyId) params.append('companyId', companyId.toString());
            if (includeInactive) params.append('includeInactive', 'true');
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/plants?${params.toString()}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar plantas.');
            return res.json();
        },
        getRoles: async (): Promise<any[]> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/lookups/roles`);
            if (!response.ok) return handleApiError(response, 'Falha ao carregar funções.');
            return response.json();
        },
        createPlant: async (data: any): Promise<any> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/plants`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao salvar planta.');
            return res.json();
        },
        updatePlant: async (id: number, data: any): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/plants/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao alterar planta.');
        },
        togglePlant: async (id: number): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/plants/${id}/toggle-active`, { method: 'PUT' });
            if (!res.ok) return handleApiError(res, 'Falha ao alternar estado da planta.');
        },
        getCompanies: async (includeInactive = false): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/companies?includeInactive=${includeInactive}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar empresas.');
            return res.json();
        },
        getSuppliers: async (includeInactive = false): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/suppliers?includeInactive=${includeInactive}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar fornecedores.');
            return res.json();
        },
        searchSuppliers: async (term: string): Promise<any[]> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/lookups/suppliers/search?q=${encodeURIComponent(term)}`);
            if (!response.ok) return handleApiError(response, 'Falha ao pesquisar fornecedores.');
            return response.json();
        },
        createSupplier: async (data: { name: string, taxId?: string, primaveraCode?: string }): Promise<any> => {
            const response = await apiFetch(`${API_BASE_URL}/api/v1/lookups/suppliers`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!response.ok) return handleApiError(response, 'Falha ao criar fornecedor.');
            return response.json();
        },
        updateSupplier: async (id: number, data: any): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/suppliers/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao alterar fornecedor.');
        },
        toggleSupplier: async (id: number): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/suppliers/${id}/toggle-active`, { method: 'PUT' });
            if (!res.ok) return handleApiError(res, 'Falha ao alternar estado do fornecedor.');
        },
        checkSupplierUniqueness: async (name?: string, primaveraCode?: string, excludeId?: number): Promise<{ isNameDuplicate: boolean, isPrimaveraDuplicate: boolean }> => {
            const params = new URLSearchParams();
            if (name) params.append('name', name);
            if (primaveraCode) params.append('primaveraCode', primaveraCode);
            if (excludeId) params.append('excludeId', excludeId.toString());

            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/suppliers/check-uniqueness?${params.toString()}`);
            if (!res.ok) return handleApiError(res, 'Falha ao verificar unicidade do fornecedor.');
            return res.json();
        },
        getCostCenters: async (includeInactive = false, plantId?: number): Promise<any[]> => {
            const params = new URLSearchParams({ includeInactive: String(includeInactive) });
            if (plantId) params.append('plantId', String(plantId));
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/cost-centers?${params.toString()}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar centros de custo.');
            return res.json();
        },
        searchCostCenters: async (query: string): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/cost-centers/search?q=${encodeURIComponent(query)}`);
            if (!res.ok) return handleApiError(res, 'Falha ao buscar centros de custo.');
            return res.json();
        },
        createCostCenter: async (data: { code: string, name: string, companyId?: number }): Promise<any> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/cost-centers`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao criar centro de custo.');
            return res.json();
        },
        updateCostCenter: async (id: number, data: { code: string, name: string, companyId?: number }): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/cost-centers/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao atualizar centro de custo.');
        },
        toggleCostCenter: async (id: number): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/cost-centers/${id}/toggle-active`, { method: 'PUT' });
            if (!res.ok) return handleApiError(res, 'Falha ao alternar estado do centro de custo.');
        },
        getRequestTypes: async (includeInactive = false): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/lookups/request-types?includeInactive=${includeInactive}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar tipos de pedido.');
            return res.json();
        },
        getIvaRates: async (activeOnly: boolean = false): Promise<any[]> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/iva-rates?activeOnly=${activeOnly}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar taxas de IVA.');
            return res.json();
        },
        createIvaRate: async (data: { code: string, name: string, ratePercent: number }): Promise<any> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/iva-rates`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao criar taxa de IVA.');
            return res.json();
        },
        updateIvaRate: async (id: number, data: { code: string, name: string, ratePercent: number }): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/iva-rates/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
            });
            if (!res.ok) return handleApiError(res, 'Falha ao atualizar taxa de IVA.');
        },
        toggleIvaRate: async (id: number): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/iva-rates/${id}/toggle-active`, { method: 'PUT' });
            if (!res.ok) return handleApiError(res, 'Falha ao alternar estado da taxa de IVA.');
        }
    },
    lineItems: {
        list: async (
            search?: string, 
            itemStatus?: string, 
            requestStatus?: string, 
            plant?: number, 
            department?: number, 
            filters?: { ivaRateId?: number, ivaRatePercent?: number, grossSubtotal?: number, ivaAmount?: number, lineTotal?: number }, 
            page: number = 1, 
            pageSize: number = 20
        ): Promise<{ data: any[], totalCount: number, page: number, pageSize: number }> => {
            const params = new URLSearchParams();
            if (search) params.append('query', search);
            if (itemStatus) params.append('itemStatus', itemStatus);
            if (requestStatus) params.append('requestStatus', requestStatus);
            if (plant) params.append('plant', plant.toString());
            if (department) params.append('department', department.toString());
            if (filters?.ivaRateId !== undefined) params.append('ivaRateId', String(filters.ivaRateId));
            if (filters?.ivaRatePercent !== undefined) params.append('ivaRatePercent', String(filters.ivaRatePercent));
            if (filters?.grossSubtotal !== undefined) params.append('grossSubtotal', String(filters.grossSubtotal));
            if (filters?.ivaAmount !== undefined) params.append('ivaAmount', String(filters.ivaAmount));
            if (filters?.lineTotal !== undefined) params.append('lineTotal', String(filters.lineTotal));
            params.append('page', page.toString());
            params.append('pageSize', pageSize.toString());

            const res = await apiFetch(`${API_BASE_URL}/api/v1/line-items?${params.toString()}`);
            if (!res.ok) return handleApiError(res, 'Falha ao carregar itens de linha.');
            return res.json();
        },
        updateStatus: async (id: string, statusCode: string, comment?: string): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/line-items/${id}/status`, {
                method: 'PATCH',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ statusCode, comment })
            });
            if (!res.ok) return handleApiError(res, 'Falha ao atualizar status do item.');
        },
        updateSupplier: async (id: string, supplierId: number | null): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/line-items/${id}/supplier`, {
                method: 'PATCH',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ supplierId })
            });
            if (!res.ok) return handleApiError(res, 'Falha ao atualizar fornecedor do item.');
        },
        updateCostCenter: async (id: string, costCenterId: number | null): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/line-items/${id}/cost-center`, {
                method: 'PATCH',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ costCenterId })
            });
            if (!res.ok) return handleApiError(res, 'Falha ao atualizar centro de custo do item.');
        },
        updateCurrency: async (id: string, currencyId: number | null): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/line-items/${id}/currency`, {
                method: 'PATCH',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ currencyId })
            });
            if (!res.ok) return handleApiError(res, 'Falha ao atualizar moeda do item.');
        },
        checkLastPending: async (id: string): Promise<{ isLastPending: boolean }> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/line-items/${id}/check-last-pending`);
            if (!res.ok) return handleApiError(res, 'Falha ao verificar status do pedido.');
            return res.json();
        },
        updateReceiving: async (id: string, receivedQuantity: number, divergenceNotes?: string): Promise<void> => {
            const res = await apiFetch(`${API_BASE_URL}/api/v1/line-items/${id}/receiving`, {
                method: 'PATCH',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ receivedQuantity, divergenceNotes })
            });
            if (!res.ok) return handleApiError(res, 'Falha ao atualizar recebimento do item.');
        }
    },
    admin: {
        extractionSettings: {
            get: async (): Promise<DocumentExtractionSettingsDto> => {
                const response = await apiFetch(`${API_BASE_URL}/api/admin/document-extraction-settings`);
                if (!response.ok) return handleApiError(response, 'Falha ao carregar configurações de extração.', 'AdminApi');
                return response.json();
            },
            update: async (data: DocumentExtractionSettingsDto): Promise<void> => {
                const response = await apiFetch(`${API_BASE_URL}/api/admin/document-extraction-settings`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(data),
                });
                if (!response.ok) return handleApiError(response, 'Falha ao atualizar configurações de extração.', 'AdminApi');
            },
            testConnection: async (): Promise<any> => {
                const response = await apiFetch(`${API_BASE_URL}/api/admin/document-extraction-settings/test-connection`, {
                    method: 'POST'
                });
                if (!response.ok) return handleApiError(response, 'Falha ao testar conexão.', 'AdminApi');
                return response.json();
            }
        },
        diagnostics: {
            getHealth: async (): Promise<any> => {
                const response = await apiFetch(`${API_BASE_URL}/api/admin/diagnostics/health`);
                if (!response.ok) return handleApiError(response, 'Falha ao carregar diagnóstico de serviços.', 'AdminApi');
                return response.json();
            }
        }
    }
};
