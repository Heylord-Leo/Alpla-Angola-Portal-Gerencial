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
    },

    // ─── Reactivate ───
    reactivate: async (id: string, data: { newStatus?: string; reason?: string; notes?: string }): Promise<{ message: string; newStatus: string }> => {
        const response = await apiFetch(`${BASE}/${id}/reactivate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao reativar equipamento.');
        return response.json();
    },

    // ─── Equipment Types ───
    types: {
        list: async (activeOnly?: boolean): Promise<Array<{ id: string; code: string; displayName: string; isActive: boolean; sortOrder: number }>> => {
            const qs = activeOnly ? '?activeOnly=true' : '';
            const response = await apiFetch(`${BASE}/types${qs}`);
            if (!response.ok) return handleError(response, 'Falha ao carregar tipos de equipamento.');
            return response.json();
        },

        create: async (data: { code: string; displayName: string; sortOrder?: number }): Promise<{ id: string; code: string; displayName: string; isActive: boolean; sortOrder: number }> => {
            const response = await apiFetch(`${BASE}/types`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao criar tipo de equipamento.');
            return response.json();
        },

        update: async (id: string, data: { displayName?: string; sortOrder?: number; isActive?: boolean }): Promise<{ id: string; code: string; displayName: string; isActive: boolean; sortOrder: number }> => {
            const response = await apiFetch(`${BASE}/types/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao atualizar tipo de equipamento.');
            return response.json();
        },

        toggle: async (id: string): Promise<{ id: string; code: string; displayName: string; isActive: boolean; sortOrder: number }> => {
            const response = await apiFetch(`${BASE}/types/${id}/toggle`, { method: 'POST' });
            if (!response.ok) return handleError(response, 'Falha ao alternar tipo de equipamento.');
            return response.json();
        }
    }
};

// ─── Delivery Terms API ───

const DT_BASE = `${API_BASE_URL}/api/it/delivery-terms`;

export const deliveryTermsApi = {
    list: async (params: {
        search?: string;
        status?: string;
        plant?: string;
        dateFrom?: string;
        dateTo?: string;
        sortBy?: string;
        isDescending?: boolean;
        page?: number;
        pageSize?: number;
    } = {}) => {
        const qs = new URLSearchParams();
        if (params.search) qs.append('search', params.search);
        if (params.status) qs.append('status', params.status);
        if (params.plant) qs.append('plant', params.plant);
        if (params.dateFrom) qs.append('dateFrom', params.dateFrom);
        if (params.dateTo) qs.append('dateTo', params.dateTo);
        if (params.sortBy) qs.append('sortBy', params.sortBy);
        if (params.isDescending !== undefined) qs.append('isDescending', String(params.isDescending));
        if (params.page) qs.append('page', String(params.page));
        if (params.pageSize) qs.append('pageSize', String(params.pageSize));
        const response = await apiFetch(`${DT_BASE}?${qs.toString()}`);
        if (!response.ok) return handleError(response, 'Falha ao carregar termos de entrega.');
        return response.json();
    },

    getById: async (id: string) => {
        const response = await apiFetch(`${DT_BASE}/${id}`);
        if (!response.ok) return handleError(response, 'Falha ao carregar termo de entrega.');
        return response.json();
    },

    create: async (data: {
        employeeName: string;
        employeeEmail?: string;
        employeeUserId?: string;
        employeeDepartment?: string;
        employeePosition?: string;
        employeePlant?: string;
        companyId?: number;
        employeePlantId?: number;
        employeeDepartmentId?: number;
        deliveryDate: string;
        notes?: string;
        equipmentIds?: string[];
    }) => {
        const response = await apiFetch(DT_BASE, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao criar termo de entrega.');
        return response.json();
    },

    update: async (id: string, data: {
        employeeName?: string;
        employeeEmail?: string;
        employeeUserId?: string;
        employeeDepartment?: string;
        employeePosition?: string;
        employeePlant?: string;
        companyId?: number;
        employeePlantId?: number;
        employeeDepartmentId?: number;
        deliveryDate?: string;
        notes?: string;
    }) => {
        const response = await apiFetch(`${DT_BASE}/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao atualizar termo de entrega.');
        return response.json();
    },

    addItems: async (id: string, equipmentIds: string[]) => {
        const response = await apiFetch(`${DT_BASE}/${id}/items`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ equipmentIds })
        });
        if (!response.ok) return handleError(response, 'Falha ao adicionar equipamentos ao termo.');
        return response.json();
    },

    removeItem: async (termId: string, itemId: string) => {
        const response = await apiFetch(`${DT_BASE}/${termId}/items/${itemId}`, {
            method: 'DELETE'
        });
        if (!response.ok) return handleError(response, 'Falha ao remover item do termo.');
        return response.json();
    },

    generate: async (id: string) => {
        const response = await apiFetch(`${DT_BASE}/${id}/generate`, {
            method: 'POST'
        });
        if (!response.ok) return handleError(response, 'Falha ao confirmar e gerar o termo.');
        return response.json();
    },

    send: async (id: string) => {
        const response = await apiFetch(`${DT_BASE}/${id}/send`, {
            method: 'POST'
        });
        if (!response.ok) return handleError(response, 'Falha ao enviar o termo.');
        return response.json();
    },

    uploadSigned: async (id: string, file: File) => {
        const formData = new FormData();
        formData.append('file', file);
        const response = await apiFetch(`${DT_BASE}/${id}/upload-signed`, {
            method: 'POST',
            body: formData
        });
        if (!response.ok) return handleError(response, 'Falha ao carregar documento assinado.');
        return response.json();
    },

    returnItem: async (termId: string, itemId: string, data: {
        returnDate?: string;
        returnCondition?: string;
        notes?: string;
    }) => {
        const response = await apiFetch(`${DT_BASE}/${termId}/items/${itemId}/return`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!response.ok) return handleError(response, 'Falha ao devolver item.');
        return response.json();
    },

    cancel: async (id: string) => {
        const response = await apiFetch(`${DT_BASE}/${id}/cancel`, {
            method: 'POST'
        });
        if (!response.ok) return handleError(response, 'Falha ao cancelar o termo.');
        return response.json();
    },

    downloadDocument: (id: string) => `${DT_BASE}/${id}/document`,
    downloadSignedDocument: (id: string) => `${DT_BASE}/${id}/signed-document`
};

// ═══════════════════════════════════════════════════════════════
//  IT Equipment Catalog API (Manufacturers, Models, Processors, Memory)
// ═══════════════════════════════════════════════════════════════

export const itEquipmentCatalogApi = {
    // ─── Manufacturers ───
    manufacturers: {
        list: async (activeOnly?: boolean) => {
            const qs = activeOnly ? '?activeOnly=true' : '';
            const response = await apiFetch(`${BASE}/manufacturers${qs}`);
            if (!response.ok) return handleError(response, 'Falha ao carregar fabricantes.');
            return response.json();
        },
        create: async (data: { name: string; sortOrder?: number }) => {
            const response = await apiFetch(`${BASE}/manufacturers`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao criar fabricante.');
            return response.json();
        },
        update: async (id: string, data: { name?: string; sortOrder?: number; isActive?: boolean }) => {
            const response = await apiFetch(`${BASE}/manufacturers/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao atualizar fabricante.');
            return response.json();
        },
        toggle: async (id: string) => {
            const response = await apiFetch(`${BASE}/manufacturers/${id}/toggle`, { method: 'POST' });
            if (!response.ok) return handleError(response, 'Falha ao alternar estado do fabricante.');
            return response.json();
        }
    },

    // ─── Models ───
    models: {
        list: async (params?: { activeOnly?: boolean; manufacturerId?: string; equipmentTypeCode?: string }) => {
            const qs = new URLSearchParams();
            if (params?.activeOnly) qs.append('activeOnly', 'true');
            if (params?.manufacturerId) qs.append('manufacturerId', params.manufacturerId);
            if (params?.equipmentTypeCode) qs.append('equipmentTypeCode', params.equipmentTypeCode);
            const response = await apiFetch(`${BASE}/models?${qs.toString()}`);
            if (!response.ok) return handleError(response, 'Falha ao carregar modelos.');
            return response.json();
        },
        create: async (data: { name: string; manufacturerId: string; equipmentTypeCode?: string; sortOrder?: number }) => {
            const response = await apiFetch(`${BASE}/models`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao criar modelo.');
            return response.json();
        },
        update: async (id: string, data: { name?: string; equipmentTypeCode?: string; sortOrder?: number; isActive?: boolean }) => {
            const response = await apiFetch(`${BASE}/models/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao atualizar modelo.');
            return response.json();
        },
        toggle: async (id: string) => {
            const response = await apiFetch(`${BASE}/models/${id}/toggle`, { method: 'POST' });
            if (!response.ok) return handleError(response, 'Falha ao alternar estado do modelo.');
            return response.json();
        }
    },

    // ─── Processors ───
    processors: {
        list: async (activeOnly?: boolean) => {
            const qs = activeOnly ? '?activeOnly=true' : '';
            const response = await apiFetch(`${BASE}/processors${qs}`);
            if (!response.ok) return handleError(response, 'Falha ao carregar processadores.');
            return response.json();
        },
        create: async (data: { name: string; sortOrder?: number }) => {
            const response = await apiFetch(`${BASE}/processors`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao criar processador.');
            return response.json();
        },
        update: async (id: string, data: { name?: string; sortOrder?: number; isActive?: boolean }) => {
            const response = await apiFetch(`${BASE}/processors/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao atualizar processador.');
            return response.json();
        },
        toggle: async (id: string) => {
            const response = await apiFetch(`${BASE}/processors/${id}/toggle`, { method: 'POST' });
            if (!response.ok) return handleError(response, 'Falha ao alternar estado do processador.');
            return response.json();
        }
    },

    // ─── Memory Options ───
    memoryOptions: {
        list: async (activeOnly?: boolean) => {
            const qs = activeOnly ? '?activeOnly=true' : '';
            const response = await apiFetch(`${BASE}/memory-options${qs}`);
            if (!response.ok) return handleError(response, 'Falha ao carregar opções de memória.');
            return response.json();
        },
        create: async (data: { displayName: string; valueInGb?: number; sortOrder?: number }) => {
            const response = await apiFetch(`${BASE}/memory-options`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao criar opção de memória.');
            return response.json();
        },
        update: async (id: string, data: { displayName?: string; valueInGb?: number; sortOrder?: number; isActive?: boolean }) => {
            const response = await apiFetch(`${BASE}/memory-options/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            if (!response.ok) return handleError(response, 'Falha ao atualizar opção de memória.');
            return response.json();
        },
        toggle: async (id: string) => {
            const response = await apiFetch(`${BASE}/memory-options/${id}/toggle`, { method: 'POST' });
            if (!response.ok) return handleError(response, 'Falha ao alternar estado da opção de memória.');
            return response.json();
        }
    }
};
