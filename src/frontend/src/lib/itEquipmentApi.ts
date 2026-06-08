import { apiFetch, API_BASE_URL, ApiError } from './api';
import type {
    ITEquipmentSummary,
    ITEquipmentListResponse,
    ITEquipmentDetail,
    ITEquipmentFilterOptions,
    ITEquipmentImportResult,
    ITEquipmentDocument
} from '../types/itEquipment';

const BASE = `${API_BASE_URL}/api/it/equipment`;

async function handleError(response: Response, defaultMsg: string): Promise<never> {
    const errJson = await response.json().catch(() => null);
    const msg = errJson?.detail || errJson?.title || errJson?.message || defaultMsg;
    throw new ApiError(msg, response.status);
}

export const itEquipmentApi = {
    // ─── Summary ───
    getSummary: async (): Promise<ITEquipmentSummary> => {
        const response = await apiFetch(`${BASE}/summary`);
        if (!response.ok) return handleError(response, 'Falha ao carregar resumo de equipamentos.');
        return response.json();
    },

    // ─── List ───
    list: async (params: {
        search?: string;
        statusCode?: string;
        equipmentType?: string;
        plant?: string;
        manufacturer?: string;
        hasOwner?: boolean;
        biometricMfa?: boolean;
        sortBy?: string;
        isDescending?: boolean;
        page?: number;
        pageSize?: number;
    } = {}): Promise<ITEquipmentListResponse> => {
        const qs = new URLSearchParams();
        if (params.search) qs.append('search', params.search);
        if (params.statusCode) qs.append('statusCode', params.statusCode);
        if (params.equipmentType) qs.append('equipmentType', params.equipmentType);
        if (params.plant) qs.append('plant', params.plant);
        if (params.manufacturer) qs.append('manufacturer', params.manufacturer);
        if (params.hasOwner !== undefined) qs.append('hasOwner', String(params.hasOwner));
        if (params.biometricMfa !== undefined) qs.append('biometricMfa', String(params.biometricMfa));
        if (params.sortBy) qs.append('sortBy', params.sortBy);
        if (params.isDescending !== undefined) qs.append('isDescending', String(params.isDescending));
        qs.append('page', String(params.page ?? 1));
        qs.append('pageSize', String(params.pageSize ?? 30));

        const response = await apiFetch(`${BASE}?${qs.toString()}`);
        if (!response.ok) return handleError(response, 'Falha ao carregar equipamentos.');
        return response.json();
    },

    // ─── Detail ───
    get: async (id: string): Promise<ITEquipmentDetail> => {
        const response = await apiFetch(`${BASE}/${id}`);
        if (!response.ok) return handleError(response, 'Falha ao carregar detalhes do equipamento.');
        return response.json();
    },

    // ─── Filter Options ───
    getFilterOptions: async (): Promise<ITEquipmentFilterOptions> => {
        const response = await apiFetch(`${BASE}/filters`);
        if (!response.ok) return handleError(response, 'Falha ao carregar opções de filtro.');
        return response.json();
    },

    // ─── Create ───
    create: async (data: any): Promise<{ id: string; assetTag: string }> => {
        const response = await apiFetch(BASE, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao criar equipamento.');
        return response.json();
    },

    // ─── Update ───
    update: async (id: string, data: any): Promise<void> => {
        const response = await apiFetch(`${BASE}/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao atualizar equipamento.');
    },

    // ─── Assign ───
    assign: async (id: string, data: any): Promise<{ message: string; warnings?: string[] }> => {
        const response = await apiFetch(`${BASE}/${id}/assign`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao atribuir equipamento.');
        return response.json();
    },

    // ─── Return ───
    return: async (id: string, data: any): Promise<{ message: string; newStatus: string; warnings?: string[] }> => {
        const response = await apiFetch(`${BASE}/${id}/return`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao devolver equipamento.');
        return response.json();
    },

    // ─── Change User (Transfer) ───
    changeUser: async (id: string, data: any): Promise<{
        success: boolean; equipmentId: string; previousAssignmentId: string;
        newAssignmentId: string; returnDocumentId: string;
        assignmentAgreementDocumentId: string; warnings?: string[];
    }> => {
        const response = await apiFetch(`${BASE}/${id}/change-user`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao trocar utilizador.');
        return response.json();
    },

    // ─── Send to Repair ───
    sendToRepair: async (id: string, data: any): Promise<{ message: string }> => {
        const response = await apiFetch(`${BASE}/${id}/send-to-repair`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao enviar para conserto.');
        return response.json();
    },

    // ─── Return from Repair ───
    returnFromRepair: async (id: string, data: any): Promise<{ message: string; newStatus: string }> => {
        const response = await apiFetch(`${BASE}/${id}/return-from-repair`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao retornar do conserto.');
        return response.json();
    },

    // ─── Mark Lost ───
    markLost: async (id: string, data: any): Promise<{ message: string }> => {
        const response = await apiFetch(`${BASE}/${id}/mark-lost`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao marcar como perdido.');
        return response.json();
    },

    // ─── Reserve ───
    reserve: async (id: string, data: any): Promise<{ message: string }> => {
        const response = await apiFetch(`${BASE}/${id}/reserve`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao reservar equipamento.');
        return response.json();
    },

    // ─── Retire ───
    retire: async (id: string, data: any): Promise<{ message: string }> => {
        const response = await apiFetch(`${BASE}/${id}/retire`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao baixar equipamento.');
        return response.json();
    },

    // ─── Import CSV ───
    importCsv: async (file: File): Promise<ITEquipmentImportResult> => {
        const formData = new FormData();
        formData.append('file', file);
        const response = await apiFetch(`${BASE}/import`, {
            method: 'POST',
            body: formData
        });
        if (!response.ok) return handleError(response, 'Falha ao importar CSV.');
        return response.json();
    },

    // ─── Documents ───
    documents: {
        list: async (equipmentId: string): Promise<ITEquipmentDocument[]> => {
            const response = await apiFetch(`${BASE}/${equipmentId}/documents`);
            if (!response.ok) return handleError(response, 'Falha ao carregar documentos.');
            return response.json();
        },

        upload: async (equipmentId: string, file: File, documentType: string, notes?: string, acquisitionId?: string, assignmentId?: string): Promise<{ id: string; fileName: string }> => {
            const formData = new FormData();
            formData.append('file', file);
            formData.append('documentType', documentType);
            if (notes) formData.append('notes', notes);
            if (acquisitionId) formData.append('acquisitionId', acquisitionId);
            if (assignmentId) formData.append('assignmentId', assignmentId);
            const response = await apiFetch(`${BASE}/${equipmentId}/documents/upload`, {
                method: 'POST',
                body: formData
            });
            if (!response.ok) return handleError(response, 'Falha ao carregar documento.');
            return response.json();
        },

        download: async (equipmentId: string, docId: string): Promise<Blob> => {
            const response = await apiFetch(`${BASE}/${equipmentId}/documents/${docId}/download`);
            if (!response.ok) return handleError(response, 'Falha ao descarregar documento.');
            return response.blob();
        },

        delete: async (equipmentId: string, docId: string): Promise<void> => {
            const response = await apiFetch(`${BASE}/${equipmentId}/documents/${docId}`, {
                method: 'DELETE'
            });
            if (!response.ok) return handleError(response, 'Falha ao remover documento.');
        }
    }
};
