/**
 * Operations Module — TypeScript DTOs
 *
 * Timeline types match GET /api/operations/transfers/{plant}/{idBestellung}/timeline
 * List types match GET /api/operations/transfers
 *
 * @since v2.164.0 — Phase 3 Frontend MVP (timeline)
 * @since v2.166.0 — Phase 5 Frontend List Integration (list)
 */

// ─── Timeline DTOs ───

export interface OperationsTimelineEvent {
    sortOrder: number;
    eventCode: string;
    eventLabelPT: string;
    eventLabelEN: string;
    eventDate: string | null;
    eventUser: string | null;
    mainStatus: number | null;
    statusMeaning: string | null;
    severity: 'success' | 'info' | 'warning' | 'error';
    isCompleted: boolean;
    isTechnical: boolean;
    sourceTable: string | null;
    referenceNumber: string | null;
    materialName: string | null;
    quantity: number | null;
    notes: string | null;
}

export interface OperationsTimelineResponse {
    plant: string;
    plantServer: string;
    plantDatabase: string;
    idBestellung: number;
    journalNummer: string | null;
    pipelineModel: 'STANDARD' | 'INHOUSE' | 'PARTIAL';
    expectedEventCount: number;
    completedEventCount: number;
    events: OperationsTimelineEvent[];
    queryDurationMs: number;
}

// ─── Transfer List DTOs ───

export interface OperationsTransferListItem {
    plant: string;
    plantServer: string | null;
    plantDatabase: string | null;
    idBestellung: number;
    idJournal: number | null;
    journalNummer: string | null;
    pipelineModel: string;
    createdDate: string | null;
    updatedDate: string | null;
    mainStatus: number | null;
    statusMeaning: string | null;
    severity: 'success' | 'info' | 'warning' | 'error';
    materialName: string | null;
    articleAlias: string | null;
    articleVariantType: string | null;
    packagingName: string | null;
    quantity: number | null;
    quantityUnit: string | null;
    expectedEventCount: number;
    completedEventCount: number | null;
    referenceNumber: string | null;
}

export interface OperationsTransferListResponse {
    plant: string;
    pipelineModel: string;
    dateFrom: string;
    dateTo: string;
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    items: OperationsTransferListItem[];
    queryTimestamp: string;
    queryDurationMs: number;
}

export interface OperationsTransferListFilters {
    plant: string;
    dateFrom: string;
    dateTo: string;
    status: string;
    articleSearch: string;
    poSearch: string;
    page: number;
    pageSize: number;
}

// ─── Transfer Detail DTOs (Phase 6) ───

export interface OperationsTransferDetail {
    plant: string;
    plantServer: string | null;
    plantDatabase: string | null;
    pipelineModel: string;
    header: OperationsTransferHeader;
    material: OperationsTransferMaterial;
    quantity: OperationsTransferQuantity;
    loading: OperationsTransferLoading;
    goodsReceipt: OperationsTransferGoodsReceipt;
    technicalReferences: OperationsTransferTechRefs;
    queryTimestamp: string;
    queryDurationMs: number;
}

export interface OperationsTransferHeader {
    idBestellung: number;
    idJournal: number | null;
    journalNummer: string | null;
    status: number | null;
    statusMeaning: string | null;
    severity: string | null;
    createdDate: string | null;
    updatedDate: string | null;
    createdBy: string | null;
    updatedBy: string | null;
    notes: string | null;
}

export interface OperationsTransferMaterial {
    materialName: string | null;
    articleAlias: string | null;
    articleVariantType: string | null;
    articleTypeName: string | null;
    classification: string | null;
    color: string | null;
    idArtikelVarianten: number | null;
}

export interface OperationsTransferQuantity {
    orderedQuantity: number | null;
    deliveredQuantity: number | null;
    receivedQuantity: number | null;
    openQuantity: number | null;
    quantityUnit: string | null;
    palletQuantity: number | null;
    packagingName: string | null;
}

export interface OperationsTransferLoading {
    // Standard pipeline fields
    idLadeAuftrag: number | null;
    idLadePlanung: number | null;
    ladeDatum: string | null;
    loadingStatus: number | null;
    loadingStatusMeaning: string | null;
    truckNumber: string | null;
    truckDescription: string | null;
    deliveryNumber: string | null;
    deliveryDate: string | null;
    // Inhouse pipeline fields
    idInhouseLieferung: number | null;
    lieferscheinDatum: string | null;
    prodTag: string | null;
    inhouseIdJournal: number | null;
    inhouseJournalNummer: string | null;
}

export interface OperationsTransferGoodsReceipt {
    idWareneingang: number | null;
    receiptDate: string | null;
    receiptStatus: number | null;
    receiptStatusMeaning: string | null;
    receivedQuantity: number | null;
    receiptCount: number;
    lastReceiptDate: string | null;
    isCompleted: boolean;
}

export interface OperationsTransferTechRefs {
    idBestellung: number;
    idBestellPosition: number | null;
    idJournal: number | null;
    journalNummer: string | null;
    idAuftragsAbruf: number | null;
    idAbrufe: number | null;
    idLadePlanung: number | null;
    idLadeAuftrag: number | null;
    idWareneingang: number | null;
    idInhouseLieferung: number | null;
    referenceNumber: string | null;
}

// ─── Live Board DTOs (Phase Live 3) ───

export interface OperationsLiveBoardStep {
    code: string;
    label: string;
    state: 'done' | 'active' | 'pending';
}

export interface OperationsLiveBoardTransfer {
    idBestellung: number;
    journalNummer: string | null;
    originPlant: string;
    originPlantName: string;
    destinationPlant: string;
    destinationPlantName: string;
    direction: 'INBOUND' | 'OUTBOUND';
    materialName: string | null;
    orderedQuantity: number | null;
    receivedQuantity: number | null;
    openQuantity: number | null;
    quantityUnit: string | null;
    currentStage: string;
    currentStageLabel: string;
    statusColor: string;
    isAttention: boolean;
    attentionReason: string | null;
    lastEventAt: string | null;
    ageMinutes: number | null;
    steps: OperationsLiveBoardStep[];
}

export interface OperationsLiveBoardSummary {
    inboundTotal: number;
    inboundActive: number;
    outboundTotal: number;
    outboundActive: number;
    attentionCount: number;
    completedRecentCount: number;
}

export interface OperationsLiveBoardResponse {
    plant: string;
    plantName: string;
    lastUpdated: string;
    refreshSeconds: number;
    summary: OperationsLiveBoardSummary;
    inbound: OperationsLiveBoardTransfer[];
    outbound: OperationsLiveBoardTransfer[];
    queryDurationMs: number;
}
