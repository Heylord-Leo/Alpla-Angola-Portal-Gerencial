// ─── I.T Equipment Module Types ───

export interface ITEquipmentSummary {
    total: number;
    inUse: number;
    available: number;
    inRepair: number;
    lost: number;
    retired: number;
    reserved: number;
    unknown: number;
    noOwner: number;
    noSerial: number;
    noType: number;
}

export interface ITEquipmentListItem {
    id: string;
    assetTag: string;
    hostname: string | null;
    plant: string | null;
    equipmentType: string;
    statusCode: string;
    manufacturer: string | null;
    model: string | null;
    serialNumber: string | null;
    macAddress: string | null;
    currentOwnerName: string | null;
    biometricMfaEnabled: boolean;
    updatedAt: string | null;
    createdAt: string;
}

export interface ITEquipmentListResponse {
    items: ITEquipmentListItem[];
    totalCount: number;
    page: number;
    pageSize: number;
}

export interface ITEquipmentAcquisition {
    id: string;
    acquisitionDate: string | null;
    supplierName: string | null;
    purchaseOrderNumber: string | null;
    invoiceNumber: string | null;
    paymentReference: string | null;
    paymentDate: string | null;
    purchaseAmount: number | null;
    currency: string | null;
    warrantyStartDate: string | null;
    warrantyEndDate: string | null;
    warrantyNotes: string | null;
    acquisitionNotes: string | null;
    purchaseRequestNumber: string | null;
}

export interface ITEquipmentDocument {
    id: string;
    documentType: string;
    fileName: string;
    uploadedAt: string;
    notes: string | null;
    acquisitionId: string | null;
    assignmentId: string | null;
    uploadedByName?: string | null;
}

export interface ITEquipmentAssignment {
    id: string;
    assignedToName: string;
    assignedToEmail: string | null;
    assignedToDepartment: string | null;
    assignedToPlant: string | null;
    assignedDate: string;
    expectedReturnDate: string | null;
    returnedDate: string | null;
    assignmentStatus: string;
    notes: string | null;
}

export interface ITEquipmentMovement {
    id: string;
    movementType: string;
    previousStatus: string | null;
    newStatus: string | null;
    previousOwnerName: string | null;
    newOwnerName: string | null;
    notes: string | null;
    createdAt: string;
    createdByName: string | null;
}

export interface ITEquipmentDetail {
    id: string;
    assetTag: string;
    hostname: string | null;
    plant: string | null;
    equipmentType: string;
    statusCode: string;
    manufacturer: string | null;
    model: string | null;
    serialNumber: string | null;
    macAddress: string | null;
    processor: string | null;
    memoryRam: string | null;
    color: string | null;
    biometricMfaEnabled: boolean;
    idCard: string | null;
    devicePhotoUrl: string | null;
    currentOwnerName: string | null;
    currentOwnerEmail: string | null;
    currentOwnerUserId: string | null;
    currentOwnerEmployeeId: string | null;
    notes: string | null;
    sourceType: string;
    isActive: boolean;
    createdAt: string;
    updatedAt: string | null;
    createdByName: string | null;
    updatedByName: string | null;
    acquisition: ITEquipmentAcquisition | null;
    documents: ITEquipmentDocument[];
    assignments: ITEquipmentAssignment[];
    movements: ITEquipmentMovement[];
}

export interface ITEquipmentFilterOptions {
    plants: string[];
    manufacturers: string[];
}

export interface ITEquipmentImportResult {
    message: string;
    created: number;
    skipped: number;
    totalLines: number;
    errors: Array<{ line: number; error: string }>;
    duplicateHostnames: Array<{ line: number; hostname: string; conflictWith: string }>;
}

// ─── Status Display Config ───

export const EQUIPMENT_STATUS_CONFIG: Record<string, { label: string; color: string; bgColor: string }> = {
    AVAILABLE: { label: 'Disponível', color: '#10b981', bgColor: '#ecfdf5' },
    IN_USE: { label: 'Em uso', color: '#3b82f6', bgColor: '#eff6ff' },
    RESERVED: { label: 'Reservado', color: '#f59e0b', bgColor: '#fffbeb' },
    IN_REPAIR: { label: 'Em conserto', color: '#f97316', bgColor: '#fff7ed' },
    RETURNED: { label: 'Devolvido', color: '#8b5cf6', bgColor: '#f5f3ff' },
    LOST: { label: 'Perdido', color: '#ef4444', bgColor: '#fef2f2' },
    RETIRED: { label: 'Baixado', color: '#6b7280', bgColor: '#f9fafb' },
    DISPOSED: { label: 'Descartado', color: '#374151', bgColor: '#f3f4f6' },
    DAMAGED: { label: 'Danificado', color: '#dc2626', bgColor: '#fef2f2' },
    UNKNOWN: { label: 'Desconhecido', color: '#9ca3af', bgColor: '#f9fafb' }
};

export const EQUIPMENT_TYPE_CONFIG: Record<string, { label: string }> = {
    LAPTOP: { label: 'Laptop' },
    DESKTOP: { label: 'Desktop' },
    MONITOR: { label: 'Monitor' },
    PRINTER: { label: 'Impressora' },
    NVR: { label: 'NVR' },
    UNKNOWN: { label: 'Desconhecido' }
};

export const MOVEMENT_TYPE_LABELS: Record<string, string> = {
    CREATED: 'Criação',
    IMPORTED: 'Importação',
    ASSIGNED: 'Atribuição',
    RETURNED: 'Devolução',
    SENT_TO_REPAIR: 'Enviado p/ Conserto',
    RETURNED_FROM_REPAIR: 'Retorno de Conserto',
    MARKED_AS_LOST: 'Marcado como Perdido',
    RESERVED: 'Reserva',
    RELEASED_FROM_RESERVATION: 'Liberação de Reserva',
    RETIRED: 'Baixa',
    UPDATED: 'Atualização',
    PHOTO_UPDATED: 'Foto Atualizada',
    NOTES_UPDATED: 'Notas Atualizadas',
    AGREEMENT_GENERATED: 'Termo Gerado',
    EMAIL_SENT: 'E-mail Enviado',
    EMAIL_FAILED: 'Falha no E-mail',
    RETURN_DOCUMENT_GENERATED: 'Termo de Devolução Gerado',
    RETURN_EMAIL_SENT: 'E-mail de Devolução Enviado',
    RETURN_EMAIL_FAILED: 'Falha E-mail de Devolução',
    USER_CHANGED: 'Troca de Utilizador',
    USER_CHANGE_RETURNED: 'Devolução (Troca)',
    USER_CHANGE_ASSIGNED: 'Atribuição (Troca)',
    SIGNED_TERM_UPLOADED: 'Termo Assinado Carregado'
};

export const ASSIGNMENT_STATUS_CONFIG: Record<string, { label: string; color: string; bgColor: string }> = {
    ACTIVE: { label: 'Ativa', color: '#10b981', bgColor: '#ecfdf5' },
    RETURNED: { label: 'Devolvida', color: '#3b82f6', bgColor: '#eff6ff' },
    LOST: { label: 'Perdida', color: '#ef4444', bgColor: '#fef2f2' },
    REPLACED: { label: 'Substituída', color: '#f59e0b', bgColor: '#fffbeb' },
    CANCELLED: { label: 'Cancelada', color: '#6b7280', bgColor: '#f9fafb' }
};

export const DOCUMENT_TYPE_LABELS: Record<string, string> = {
    PAYMENT_PROOF: 'Comprovativo de Pagamento',
    INVOICE: 'Fatura',
    PROFORMA: 'Proforma',
    PURCHASE_ORDER: 'Ordem de Compra / P.O',
    WARRANTY: 'Garantia',
    RECEIPT: 'Recibo',
    DELIVERY_NOTE: 'Guia de Entrega',
    ASSIGNMENT_AGREEMENT: 'Termo de Responsabilidade',
    RETURN_AGREEMENT: 'Termo de Devolução',
    SIGNED_ASSIGNMENT_AGREEMENT: 'Termo de Responsabilidade Assinado',
    SIGNED_RETURN_AGREEMENT: 'Termo de Devolução Assinado',
    OTHER: 'Outro'
};
