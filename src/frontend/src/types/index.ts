export interface RequestListItemDto {
    id: string;
    requestNumber?: string;
    title: string;
    statusId: number;
    statusName: string;
    statusCode: string;
    statusDisplayOrder: number;
    statusBadgeColor: string;
    requestTypeId: number;
    requestTypeName: string;
    requestTypeCode: string;
    needLevelId: number | null;
    needLevelName: string | null;
    requesterId: string;
    requesterName: string;
    buyerId: string | null;
    buyerName: string | null;
    areaApproverId: string | null;
    areaApproverName: string | null;
    finalApproverId: string | null;
    finalApproverName: string | null;
    departmentId: number;
    departmentName: string | null;
    companyId: number;
    companyName: string | null;
    plantId: number | null;
    plantName: string | null;
    supplierId: number | null;
    supplierName: string | null;
    supplierPortalCode: string | null;
    estimatedTotalAmount: number;
    currencyId: number | null;
    currencyCode: string | null;
    capexOpexClassificationId: number | null;
    requestedDateUtc: string;
    needByDateUtc: string | null;
    createdAtUtc: string;
    isCancelled: boolean;
    selectedQuotationId: string | null;
}

export interface RequestLineItemDto {
    id: string;
    lineNumber: number;
    itemPriority: 'HIGH' | 'MEDIUM' | 'LOW';
    description: string;
    quantity: number;
    unit: string;
    unitPrice: number;
    totalAmount: number;
    supplierName: string | null;
    notes: string | null;
    plantId: number | null;
    plantName: string | null;
    lineItemStatusCode: string | null;
    lineItemStatusName: string | null;
    lineItemStatusBadgeColor: string | null;
    supplierId: number | null;
    currencyId: number | null;
    currencyCode: string | null;
    costCenterId: number | null;
    costCenterName: string | null;
    costCenterCode: string | null;
    ivaRateId: number | null;
    ivaRateCode: string | null;
    ivaRateName: string | null;
    ivaRatePercent: number | null;
    dueDate: string | null;
}

export interface SavedQuotationItemDto {
    id: string;
    lineNumber: number;
    description: string;
    quantity: number;
    unitPrice: number;
    discountType: string | null;
    discountValue: number;
    ivaRateId: number | null;
    ivaRatePercent: number;
    grossSubtotal: number;
    discountAmount: number;
    taxableBase: number;
    ivaAmount: number;
    lineTotal: number;
    unitId: number | null;
    unitName: string | null;
    unitCode: string | null;
    
    // Receiving Fields
    receivedQuantity?: number;
    divergenceNotes?: string;
    lineItemStatusCode?: string | null;
    lineItemStatusName?: string | null;
    lineItemStatusBadgeColor?: string | null;
}

export interface SavedQuotationDto {
    id: string;
    requestId: string;
    supplierId?: number;
    supplierNameSnapshot: string;
    documentNumber?: string;
    documentDate?: string;
    currency: string;
    totalGrossAmount: number;
    totalDiscountAmount: number;
    discountAmount: number;
    totalTaxableBase: number;
    totalIvaAmount: number;
    totalAmount: number;
    sourceType: string;
    sourceFileName?: string;
    proformaAttachmentId?: string;
    isSelected: boolean;
    createdAtUtc: string;
    itemCount: number;
    items: SavedQuotationItemDto[];
}

export interface RequestStatusHistoryDto {
    id: string;
    actionTaken: string;
    newStatusName: string;
    comment?: string;
    createdAtUtc: string;
    actorName: string;
}

export interface RequestAttachmentDto {
    id: string;
    fileName: string;
    fileExtension: string;
    fileSizeMBytes: number;
    attachmentTypeCode: string;
    uploadedAtUtc: string;
    uploadedByName: string;
}

// Keep details types minimal just to prove routing works later
export interface RequestDetailsDto extends RequestListItemDto {
    description: string;
    lineItems: RequestLineItemDto[];
    attachments: RequestAttachmentDto[];
    quotations: SavedQuotationDto[];
    statusHistory: RequestStatusHistoryDto[];
}

export interface LookupDto {
    id: number;
    code: string;
    name: string;
    isActive: boolean;
    companyId?: number;
    plantId?: number;        // Used by CostCenter entries
    plantName?: string;      // Used by CostCenter entries
    allowsDecimalQuantity?: boolean;
    taxId?: string;
    portalCode?: string;
    primaveraCode?: string;
}

export interface SupplierSearchDto {
    id: number;
    portalCode: string;
    primaveraCode?: string;
    name: string;
}

export interface CurrencyDto {
    id: number;
    code: string;
    symbol: string;
    isActive: boolean;
}

export interface UserDto {
    id: string;
    fullName: string;
    email: string;
    isActive: boolean;
    roles: string[];
}

export interface RequestStatusDto {
    id: number;
    code: string;
    name: string;
    displayOrder: number;
    badgeColor: string;
    isActive: boolean;
}

export interface TimelineStepDto {
    label: string;
    completedAt?: string;
    state: 'completed' | 'current' | 'pending' | 'blocked';
}

export interface RequestTimelineDto {
    steps: TimelineStepDto[];
}

export interface DashboardSummaryDto {
    totalRequests: number;
    waitingQuotation: number;
    waitingAreaApproval: number;
    waitingFinalApproval: number;
    inAdjustment: number;
    inAttention: number;

    // KPI Cards Specific
    awaitingApproval: number;
    awaitingPayment: number;
    completedRequests: number;
}

export interface PagedResult<T> {
    items: T[];
    totalCount: number;
    page: number;
    pageSize: number;
}

export interface RequestListResponseDto {
    pagedResult: PagedResult<RequestListItemDto>;
    summary: DashboardSummaryDto;
}

export interface DocumentExtractionSettingsDto {
    defaultProvider: string;
    isEnabled: boolean;
    globalTimeoutSeconds: number;
    localOcrEnabled: boolean;
    localOcrBaseUrl?: string;
    localOcrTimeoutSeconds?: number;
    openAiEnabled: boolean;
    openAiModel?: string;
    openAiTimeoutSeconds?: number;
    azureDocumentIntelligenceEnabled: boolean;
    azureDocumentIntelligenceTimeoutSeconds?: number;
}
export interface AttentionPointDto {
    id: string;
    title: string;
    description: string;
    count: number;
    targetPath: string;
    type: 'WARNING' | 'INFO' | 'DANGER' | 'SUCCESS';
}

export interface PurchasingSummaryDto {
    totalActiveRequests: number;
    waitingQuotation: number;
    awaitingApproval: number;
    awaitingPayment: number;
    pendingReceiving: number;
    attentionPoints: AttentionPointDto[];
}
