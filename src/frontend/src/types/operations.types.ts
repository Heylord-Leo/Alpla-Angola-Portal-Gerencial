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

// ─── Viana 3 Production Monitor (TV) ───
// Mirror of AlplaPortal.Application.DTOs.Operations.Viana3ProductionMonitorDtos.
// Every datetime is Angola local wall-clock (UTC+1, no DST) WITHOUT offset, except fetchedAtUtc.

export type Viana3SourceStatus = 'LIVE' | 'CACHED' | 'STALE';

export interface Viana3MonitorSource {
    status: Viana3SourceStatus;
    fetchedAtLocal: string;
    fetchedAtUtc: string;
    ageSeconds: number;
    timeZoneLabel: string;
    unavailableReason: string | null;
    queryDurationMs: number;
}

export interface Viana3MonitorScope {
    warehouseId: number;
    warehouseName: string;
    locationIds: number[];
    retentionLocationId: number;
    bufferLocationId: number;
    wasteBottlesArticleId: number;
    wastePreformsArticleId: number;
}

export interface Viana3Waste {
    bookings: number;
    rejectedBottlesKg: number | null;
    rejectedBottlesUnitsEstimate: number | null;
    rejectedPreformsKg: number | null;
    rejectedPreformsUnitsEstimate: number | null;
    lastBooking: string | null;
    estimateNote: string;
}

export type Viana3DeviationStatus = 'NOT_APPLICABLE' | 'NO_SCAN' | 'NO_REPORT' | 'PROVISIONAL';

export interface Viana3Deviation {
    statusCode: Viana3DeviationStatus;
    statusLabel: string;
    ratio: number | null;
    differenceUnits: number | null;
    provisional: boolean;
}

export type Viana3LotKind = 'Production' | 'Maintenance' | 'NoOrder' | 'Placeholder';

export interface Viana3Lot {
    slot: number;
    lotId: number;
    lotKind: Viana3LotKind;
    lotKindLabel: string;
    lotStatus: 'OPEN' | 'CLOSED' | null;
    bottleArticle: string | null;
    bottleWeightG: number | null;
    preformArticleId: number | null;
    preformArticle: string | null;
    preformWeightG: number | null;
    planFrom: string | null;
    planTo: string | null;
    plannedBottles: number | null;
    reportedBottles: number | null;
    lastReportedDay: string | null;
    reportLastUpdated: string | null;
    scanCount: number;
    scannedPreforms: number | null;
    lastScan: string | null;
    otherPreformScanCount: number;
    otherPreformsScanned: number | null;
    waste: Viana3Waste;
    deviation: Viana3Deviation;
}

export interface Viana3PreformStockLocation {
    locationId: number;
    location: string;
    groupCode: 'BLOCKED_RETENTION' | 'BUFFER' | 'LINE_SIDE';
    groupLabel: string;
    positions: number;
    availableQty: number;
    blockedQty: number;
    lastChange: string | null;
}

export interface Viana3PreformStock {
    preformArticleId: number;
    preformArticle: string;
    availablePositions: number;
    availableQty: number;
    blockedPositions: number;
    blockedQty: number;
    lastChange: string | null;
    locations: Viana3PreformStockLocation[];
}

export type Viana3MachineStatus = 'RUNNING' | 'MAINTENANCE' | 'NO_ORDER' | 'PLACEHOLDER' | 'IDLE';

export interface Viana3MachinePanel {
    machineId: number;
    machineName: string;
    current: Viana3Lot | null;
    previous: Viana3Lot[];
    preformStock: Viana3PreformStock | null;
    statusCode: Viana3MachineStatus;
    statusLabel: string;
}

export interface Viana3MonitorResponse {
    plant: string;
    plantName: string;
    refreshSeconds: number;
    source: Viana3MonitorSource;
    machines: Viana3MachinePanel[];
    preformStock: Viana3PreformStock[];
    scope: Viana3MonitorScope;
    notes: string[];
}

export interface Viana3OpenSystemBalance {
    machineId: number;
    machineName: string | null;
    preformArticleId: number;
    preformArticle: string;
    barcode: string | null;
    lastLotId: number | null;
    lastOperation: number | null;
    openSystemBalance: number;
    lastMovement: string | null;
    daysSinceLastMovement: number | null;
    balanceStatus: string;
}

export interface Viana3OrphanWaste {
    bookingId: number;
    bookedAt: string | null;
    wasteArticleId: number;
    wasteType: string;
    wasteKg: number;
    lotOnBooking: number | null;
    machineId: number | null;
    bottleArticleId: number | null;
    bottleArticle: string | null;
    bottleWeightG: number | null;
    unitsEstimate: number | null;
}

export interface Viana3MonitorDiagnosticsResponse {
    plant: string;
    source: Viana3MonitorSource;
    openSystemBalances: Viana3OpenSystemBalance[];
    orphanWaste: Viana3OrphanWaste[];
    orphanWasteWindowDays: number;
    notes: string[];
}

// ─── Viana 3 Production Monitor: shared TV screens / layouts (display preferences only) ───

/** Card size on the 1920×1080 TV stage: COMPACT/STANDARD = 1×1, WIDE = 2×1, LARGE = 2×2. */
export type Viana3CardSize = 'COMPACT' | 'STANDARD' | 'WIDE' | 'LARGE';

export interface Viana3MonitorLayoutCard {
    machineId: number;
    size: Viana3CardSize;
    /** Hidden from the TV. Never deletes or changes machine data. */
    hidden: boolean;
}

/** One TV screen (summary for the editor's screen list). */
export interface Viana3MonitorScreen {
    screenKey: string;
    name: string;
    /** Backs the historical TV URL; cannot be deleted. */
    isDefault: boolean;
    /** False until saved once (default screen = discovery layout). */
    isPersisted: boolean;
    includeNewMachines: boolean;
    columns: number;
    cardCount: number;
    visibleCount: number;
    updatedAtUtc: string | null;
    updatedByName: string | null;
    /** Portal path of the read-only TV page (append ?fullscreen=true on the device). */
    tvPath: string;
}

/** Layout of one screen. */
export interface Viana3MonitorLayout {
    screenKey: string;
    name: string;
    isDefault: boolean;
    isPersisted: boolean;
    /** Machines discovered later (or not listed) are appended visible (true) or stay off the screen (false). */
    includeNewMachines: boolean;
    schemaVersion: number;
    columns: number;
    cards: Viana3MonitorLayoutCard[];
    updatedAtUtc: string | null;
    updatedByName: string | null;
    tvPath: string;
}

export interface SaveViana3MonitorLayoutRequest {
    columns: number;
    cards: Viana3MonitorLayoutCard[];
    includeNewMachines: boolean;
}

export interface CreateViana3MonitorScreenRequest {
    name: string;
    copyFromScreenKey?: string | null;
}

export interface RenameViana3MonitorScreenRequest {
    name: string;
}
