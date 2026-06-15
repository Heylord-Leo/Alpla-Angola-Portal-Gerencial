// ─── I.T Equipment Module Types ───

// ─── Master Data / Catalog Lookup Types ───

export interface MasterDataCompany {
    id: number;
    name: string;
    isActive: boolean;
}

export interface MasterDataPlant {
    id: number;
    name: string;
    companyId: number;
    isActive: boolean;
}

export interface MasterDataDepartment {
    id: number;
    name: string;
    isActive: boolean;
}

export interface CatalogManufacturer {
    id: string;
    name: string;
    isActive: boolean;
    sortOrder: number;
}

export interface CatalogModel {
    id: string;
    name: string;
    manufacturerId: string;
    equipmentTypeCode: string | null;
    isActive: boolean;
    sortOrder: number;
}

export interface CatalogProcessor {
    id: string;
    name: string;
    isActive: boolean;
    sortOrder: number;
}

export interface CatalogMemoryOption {
    id: string;
    displayName: string;
    valueInGb: number | null;
    isActive: boolean;
    sortOrder: number;
}

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
    legacyAssetCode: string | null;
    hostname: string | null;
    plant: string | null;
    equipmentType: string;
    statusCode: string;
    manufacturer: string | null;
    model: string | null;
    serialNumber: string | null;
    macAddress: string | null;
    wifiMacAddress: string | null;
    currentOwnerName: string | null;
    biometricMfaEnabled: boolean;
    companyCode: string | null;
    plantCode: string | null;
    qrCodeUrl: string | null;
    manufactureDate: string | null;
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
    legacyAssetCode: string | null;
    hostname: string | null;
    plant: string | null;
    equipmentType: string;
    statusCode: string;
    manufacturer: string | null;
    model: string | null;
    serialNumber: string | null;
    macAddress: string | null;
    wifiMacAddress: string | null;
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
    companyId: number | null;
    plantId: number | null;
    companyCode: string | null;
    plantCode: string | null;
    equipmentTypeShortCode: string | null;
    sequenceNumber: number;
    qrCodeUrl: string | null;
    manufactureDate: string | null;
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
    MOUSE: { label: 'Rato' },
    KEYBOARD: { label: 'Teclado' },
    HEADSET: { label: 'Headset' },
    DOCKING_STATION: { label: 'Docking Station' },
    BAG: { label: 'Mala / Bolsa' },
    PHONE: { label: 'Telemóvel' },
    CHARGER: { label: 'Carregador' },
    TABLET: { label: 'Tablet' },
    SERVER: { label: 'Servidor' },
    NETWORK_EQUIPMENT: { label: 'Equipamento de Rede' },
    ACCESS_POINT: { label: 'Access Point' },
    SWITCH: { label: 'Switch' },
    FIREWALL: { label: 'Firewall' },
    UPS: { label: 'UPS / Nobreak' },
    PROJECTOR: { label: 'Projetor' },
    SCANNER: { label: 'Scanner' },
    ACCESSORIES: { label: 'Acessórios' },
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
    SIGNED_TERM_UPLOADED: 'Termo Assinado Carregado',
    REACTIVATED: 'Reativação'
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
    DELIVERY_TERM_AGREEMENT: 'Termo de Entrega Agrupado',
    SIGNED_DELIVERY_TERM_AGREEMENT: 'Termo de Entrega Agrupado Assinado',
    OTHER: 'Outro'
};

// ─── Equipment Type Management ───

export interface ITEquipmentTypeItem {
    id: string;
    code: string;
    displayName: string;
    isActive: boolean;
    sortOrder: number;
}

// ─── Grouped Delivery Terms ───

export interface ITDeliveryTermListItem {
    id: string;
    termNumber: string;
    employeeName: string;
    employeeEmail: string | null;
    employeePlant: string | null;
    deliveryDate: string;
    status: string;
    statusDisplay: string;
    itemCount: number;
    createdAt: string;
    createdByName: string | null;
}

export interface ITDeliveryTermListResponse {
    items: ITDeliveryTermListItem[];
    totalCount: number;
    page: number;
    pageSize: number;
}

export interface ITDeliveryTermDetail {
    id: string;
    termNumber: string;
    employeeName: string;
    employeeEmail: string | null;
    employeeUserId: string | null;
    employeeDepartment: string | null;
    employeePosition: string | null;
    employeePlant: string | null;
    deliveryDate: string;
    status: string;
    statusDisplay: string;
    generatedDocumentId: string | null;
    signedDocumentId: string | null;
    notes: string | null;
    createdAt: string;
    createdByName: string | null;
    updatedAt: string | null;
    updatedByName: string | null;
    items: ITDeliveryItemDetail[];
}

export interface ITDeliveryItemDetail {
    id: string;
    equipmentId: string;
    assignmentId: string | null;
    itemStatus: string;
    itemStatusDisplay: string;
    deliveredAt: string | null;
    returnedAt: string | null;
    returnCondition: string | null;
    returnConditionDisplay: string | null;
    notes: string | null;
    equipment: {
        id: string;
        assetTag: string;
        hostname: string | null;
        equipmentType: string | null;
        manufacturer: string | null;
        model: string | null;
        serialNumber: string | null;
        statusCode: string;
        statusDisplay: string;
        currentOwnerName: string | null;
    } | null;
}

export const DELIVERY_TERM_STATUS_CONFIG: Record<string, { label: string; color: string }> = {
    DRAFT: { label: 'Rascunho', color: '#6b7280' },
    GENERATED: { label: 'PDF Gerado', color: '#3b82f6' },
    SENT: { label: 'Enviado', color: '#8b5cf6' },
    SIGNED: { label: 'Assinado', color: '#10b981' },
    PARTIALLY_RETURNED: { label: 'Parcialmente Devolvido', color: '#f59e0b' },
    CLOSED: { label: 'Encerrado', color: '#6b7280' },
    CANCELLED: { label: 'Cancelado', color: '#ef4444' }
};

export const DELIVERY_ITEM_STATUS_CONFIG: Record<string, { label: string; color: string }> = {
    PENDING: { label: 'Pendente', color: '#6b7280' },
    DELIVERED: { label: 'Entregue', color: '#10b981' },
    RETURNED: { label: 'Devolvido', color: '#3b82f6' },
    REPLACED: { label: 'Substituído', color: '#8b5cf6' },
    LOST: { label: 'Perdido', color: '#ef4444' },
    RETIRED: { label: 'Baixado', color: '#6b7280' }
};

export const RETURN_CONDITION_CONFIG: Record<string, { label: string; color: string }> = {
    GOOD: { label: 'Bom estado', color: '#10b981' },
    DAMAGED: { label: 'Danificado', color: '#ef4444' },
    NEEDS_REPAIR: { label: 'Necessita reparo', color: '#f59e0b' }
};
