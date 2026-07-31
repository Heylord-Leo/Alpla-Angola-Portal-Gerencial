export interface RequestListItemDto {
    id: string;
    requestNumber?: string;
    title: string;
    statusId: number;
    statusName: string;
    statusCode: string;
    statusDisplayOrder: number;
    statusBadgeColor: string;
    displayWorkflowState?: string | null;
    /** Group-aware display override (RequestGroupDisplayStateCalculator) — null means "no override", fall back to statusName. */
    displayStatusCode?: string | null;
    displayStatusName?: string | null;
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
    /** Authoritative actionable amount for the Approval Center queue (backend ApprovalQueueAmountResolver).
     *  null = amount could not be resolved — render "Valor ainda não definido", never 0. */
    actionableAmount?: number | null;
    /** Which rule produced actionableAmount (PAYMENT_AMOUNT, BATCH_SNAPSHOT, BATCH_ITEM_SUM, ...). */
    actionableAmountSource?: string | null;
    /** Batch snapshot disagrees with the sum of its selected quotation items — warn, don't show a false amount. */
    hasAmountInconsistency?: boolean;
    /** Current actionable lot (batch) number, when the amount comes from a batch. */
    actionableLotNumber?: number | null;
    discountAmount: number;
    currencyId: number | null;
    currencyCode: string | null;
    capexOpexClassificationId: number | null;
    requestedDateUtc: string;
    needByDateUtc: string | null;
    createdAtUtc: string;
    isCancelled: boolean;
    selectedQuotationId: string | null;

    // Context for Area Approval
    costCenterCode?: string | null;
    costCenterName?: string | null;
    
    // Virtual
    completedAtUtc?: string;
    paymentCompletedAtUtc?: string;
}

/**
 * One ACTIONABLE row in the Approval Center queue. The queue unit is the actionable ApprovalBatch
 * (per approvalStage), NOT the Request — a request with two simultaneous WAITING_AREA_APPROVAL
 * batches produces two rows sharing `requestNumber` but with distinct `approvalBatchId`.
 * PAYMENT / legacy whole-request actions have no batch: `approvalBatchId` is null and identity
 * falls back to requestId+stage (both encoded in `queueKey`).
 */
export interface ApprovalQueueItemDto {
    requestId: string;
    requestNumber?: string;
    /** Actionable batch id; null for PAYMENT / legacy whole-request actions. */
    approvalBatchId?: string | null;
    /** Batch (lot) number when this row is a batch; null for request-level rows. */
    lotNumber?: number | null;
    /** "AREA" | "FINAL". */
    approvalStage: string;
    /** Stable unique row key: `{batchId}:{stage}` or `{requestId}:{stage}`. Selection/dedup key. */
    queueKey: string;

    /** Actionable status (the batch's own status for batch rows). */
    batchStatus: string;
    statusName: string;
    statusBadgeColor: string;

    /** Parent request's own aggregate status — may differ from batchStatus. */
    requestStatusCode: string;
    requestStatusName: string;

    title: string;
    requestTypeId: number;
    requestTypeCode: string;
    requestTypeName: string;
    requesterName: string;
    departmentId: number;
    departmentName?: string | null;
    companyId: number;
    companyName: string;
    plantId?: number | null;
    plantName?: string | null;
    /** Supplier for THIS row (batch winner, or request selected/legacy) — never a sibling batch's. */
    supplierDisplay?: string | null;
    costCenterCode?: string | null;
    costCenterName?: string | null;
    currencyCode?: string | null;
    /** Number of items in THIS actionable unit. */
    itemCount: number;

    /** Authoritative actionable amount for THIS row. null = unresolved → "Valor ainda não definido". */
    actionableAmount?: number | null;
    actionableAmountSource?: string | null;
    hasAmountInconsistency?: boolean;

    needLevelId?: number | null;
    needByDateUtc?: string | null;
    createdAtUtc: string;
    selectedQuotationId?: string | null;
}

export interface PendingApprovalsResponseDto {
    areaApprovals: ApprovalQueueItemDto[];
    finalApprovals: ApprovalQueueItemDto[];
}

export interface RequestLineItemDto {
    id: string;
    lineNumber: number;
    itemPriority: 'HIGH' | 'MEDIUM' | 'LOW';
    description: string;
    quantity: number;
    unit: string;
    unitPrice: number;
    discountPercent?: number;
    discountAmount?: number;
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
    quotationLifecycleStatus?: string | null;
    notQuotedJustification?: string | null;
    notQuotedProposedByName?: string | null;
    notQuotedProposedAtUtc?: string | null;

    // Catalog linkage
    itemCatalogId: number | null;
    itemCatalogCode: string | null;
    
    // Awards and PO grouping
    selectedQuotationItemId?: string | null;
    requestPoGroupId?: string | null;

    /** How this line was created. Null = standard requester/create flow. E.g. 'BUYER_EXTRA_ITEM_INCLUDED'. */
    creationOrigin?: string | null;

    allocations?: RequestLineItemAllocationDto[];
}

export interface RequestLineItemAllocationDto {
    id: string;
    plantId: number;
    plantName?: string | null;
    costCenterId?: number | null;
    costCenterName?: string | null;
    costCenterCode?: string | null;
    percentage: number;
    allocationOrder: number;
}

export interface SavedQuotationItemDto {
    id: string;
    lineNumber: number;
    description: string;
    quantity: number;
    unitPrice: number;
    currencyCode?: string;
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
    
    // Catalog linkage
    itemCatalogId?: number | null;
    itemCatalogCode?: string | null;
    
    mappedRequestLineItemId?: string;
    reconciliationStatus?: 'MAPPED' | 'NOT_QUOTED' | 'EXTRA_ITEM' | 'IGNORED' | 'SUBSTITUTE' | string;
    reconciliationJustification?: string | null;
    buyerJustification?: string | null;
    /** Persisted OCR-original baseline + line adjustment reason (for EDIT hydration & reconciliation). */
    ocrOriginalQuantity?: number | null;
    ocrOriginalUnitPrice?: number | null;
    ocrOriginalDiscountAmount?: number | null;
    ocrOriginalIvaRatePercent?: number | null;
    ocrOriginalUnitText?: string | null;
    ocrOriginalUnitId?: number | null;
    ocrOriginalLineTotal?: number | null;
    lineAdjustmentJustification?: string | null;

    // Receiving Fields
    receivedQuantity?: number;
    divergenceNotes?: string;
    lineItemStatusCode?: string | null;
    lineItemStatusName?: string | null;
    lineItemStatusBadgeColor?: string | null;

    historyInsight?: PurchaseHistoryInsightDto;

    // Cancelled-batch reuse (Option C) — server-annotated eligibility/provenance
    isReuseBlocked?: boolean;
    isReuseAuthorized?: boolean;
    sourceCancelledBatchId?: string | null;
    sourceCancelledBatchNumber?: number | null;
    reuseAuthorizationId?: string | null;
    reuseConsumedFromBatchId?: string | null;
}

export interface PurchaseHistoryInsightDto {
    hasHistory: boolean;
    lastPurchaseDateUtc: string | null;
    lastUnitPrice: number | null;
    lastCurrency: string | null;
    lastUom: string | null;
    currentUnitPrice: number;
    differencePercent: number | null;
    status: 'NO_HISTORY' | 'LOWER_THAN_LAST' | 'SAME_AS_LAST' | 'HIGHER_THAN_LAST' | 'DIFFERENT_CURRENCY' | 'DIFFERENT_UOM';
}

export interface SavedQuotationDto {
    id: string;
    requestId: string;
    supplierId?: number;
    supplierNameSnapshot: string;
    supplierPortalCode?: string;
    supplierPrimaveraCode?: string;
    supplierRegistrationStatus?: string;
    documentNumber?: string;
    documentDate?: string;
    /** Post-Payment Completion (Release 2): PROFORMA or FINAL_INVOICE, null when unclassified. */
    documentType?: string | null;
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

export interface RequestPoGroupDto {
    id: string;
    requestId: string;
    supplierId?: number | null;
    supplierNameSnapshot?: string | null;
    supplierNifSnapshot?: string | null;
    currencyId?: number | null;
    currencyCode?: string | null;
    totalAmount: number;
    paymentConditionCode?: string | null;
    advancePaymentPercent?: number | null;
    status: string;
    purchaseOrderNumber?: string | null;
    createdAtUtc: string;
    createdByUserId: string;
    lineItemCount: number;
    attachmentCount: number;
    payments?: RequestPaymentDto[];
}

export interface RequestStatusHistoryDto {
    id: string;
    actionTaken: string;
    newStatusName: string;
    comment?: string;
    createdAtUtc: string;
    actorUserId: string;
    actorName: string;
    fieldChanges: RequestFieldChangeHistoryDto[];
}

export interface RequestFieldChangeHistoryDto {
    id: string;
    fieldName: string;
    fieldDisplayName: string;
    previousValue?: string;
    newValue?: string;
    statusCodeAtChange: string;
    lineItemId?: string;
    createdAtUtc: string;
    actorName: string;
}


export interface RequestAttachmentDto {
    id: string;
    fileName: string;
    fileExtension: string;
    fileSizeMBytes: number;
    attachmentTypeCode: string;
    requestPoGroupId?: string | null;
    uploadedAtUtc: string;
    uploadedByName: string;
    voidedAtUtc?: string | null;
    voidReason?: string | null;
}

// ── ApprovalBatch (partial/batch approval) — focused interfaces for the buyer batch-composition
// UI (Phase 3). RequestDetailsDto.approvalBatches stays `any[]` (existing, widely-used convention
// across the Buyer pages, which type `group`/`batch` as `any`); these are used explicitly by the
// new batch-creation/rework components, cast at the point group.approvalBatches is consumed. ──

export interface ApprovalBatchItemSummary {
    id: string;
    requestLineItemId: string;
    selectedQuotationItemId: string;
}

/** One informational (non-batch, non-total-affecting) quotation line — mirrors the backend's
 * BatchInformationalItemDto exactly (excluded extras, IGNORED lines, unresolved legacy extras). */
export interface BatchInformationalItem {
    quotationItemId: string;
    description: string;
    quantity: number;
    unitPrice: number;
    lineTotal: number;
    supplierName?: string | null;
    quotationDocumentNumber?: string | null;
    /** Why the line was classified this way at reconciliation time (SUBSTITUTE/EXTRA_ITEM/IGNORED). */
    reconciliationJustification?: string | null;
    /** Buyer's batch-composition comment — only populated for buyer-excluded extras. */
    comment?: string | null;
}

export interface ApprovalBatchSummary {
    id: string;
    batchNumber: number;
    status: string;
    comment?: string | null;
    createdAtUtc: string;
    createdByUserId: string;
    createdByUserName?: string | null;
    updatedByUserId?: string | null;
    updatedByUserName?: string | null;
    updatedAtUtc?: string | null;
    budgetJustification?: string | null;
    approvedTotalAmount?: number | null;
    items: ApprovalBatchItemSummary[];
    /** Genuine EXTRA_ITEM lines the buyer explicitly decided not to include in this batch. */
    excludedExtraItems?: BatchInformationalItem[];
    /** IGNORED-status lines from the contributing quotation(s) — complete, valid, read-only. */
    ignoredLines?: BatchInformationalItem[];
    /** Genuine EXTRA_ITEM lines with no recorded decision — only possible for legacy batches. */
    unresolvedLegacyLines?: BatchInformationalItem[];
    /** Normalized, lot-aware view model for the Final Approval screen (backend-computed). */
    lotView?: FinalApprovalLotView | null;
}

/** Normalized, lot-aware Final Approval view model — mirrors the backend's FinalApprovalLotViewDto.
 * The Final Approval UI renders straight from this; it never re-derives line totals or sums money. */
export interface FinalApprovalLotView {
    batchNumber: number;
    /** Authoritative approved total for THIS lot only. */
    lotTotal: number;
    currencyCode?: string | null;
    includedItems: FinalApprovalLotItem[];
    /** IGNORED quotation lines — audit only, never counted in the lot. */
    ignoredLines: BatchInformationalItem[];
    includedItemCount: number;
    ignoredItemCount: number;
    supplierNames: string[];
    /** Resolved supplier value for the header (single name or "N fornecedores"); null if unresolved. */
    supplierLabel?: string | null;
    /** Header caption: "Fornecedor do lote" or "Fornecedores do lote". */
    supplierHeading: string;
    /** True when the approved snapshot disagrees with the sum of included line totals. */
    hasMonetaryInconsistency: boolean;
    /** True when an included item has no resolvable selected-quotation line total. */
    hasUnresolvedItemValue: boolean;
}

export interface FinalApprovalLotItem {
    requestLineItemId: string;
    selectedQuotationItemId: string;
    description: string;
    quantity: number;
    unitCode?: string | null;
    /** Authoritative selected-quotation line total (IVA included). null = unresolved winner. */
    lineTotal?: number | null;
    supplierName?: string | null;
    isExtraItem: boolean;
}

/** Values accepted by the backend's batch-composition decision (distinct from Area Approval's
 * APPROVE/REJECT vocabulary — the buyer composes the batch, never "approves" anything). */
export type ExtraItemDecisionValue = 'INCLUDE' | 'EXCLUDE';

/** Wire payload sent to CreateBatch/UpdateBatch, keyed by QuotationItemId. */
export interface ExtraItemDecisionPayload {
    decision: ExtraItemDecisionValue;
    comment?: string | null;
}

/** Local UI state for one EXTRA_ITEM line's decision-in-progress, keyed by QuotationItemId. */
export interface ExtraItemDecisionState {
    decision: ExtraItemDecisionValue | null;
    comment: string;
}

// Keep details types minimal just to prove routing works later
/**
 * Post-Payment Completion feature flags (GET /api/v1/config/features).
 * Booleans only — the UI needs to know what to render, not how the server is configured.
 */
export interface FeatureFlagsDto {
    /** Workflow is switched on. While false the UI renders exactly what it did before the feature. */
    postPaymentCompletionEnabled: boolean;
    /** A request created now must carry an explicit billing document type before submission. */
    billingDocumentTypeRequired: boolean;
}

export interface RequestDetailsDto extends RequestListItemDto {
    /** Fase B: nomes dos managers elegíveis enquanto a aprovação de área está pendente e sem decisor. */
    eligibleAreaManagerNames?: string[] | null;
    description: string;
    lineItems: RequestLineItemDto[];
    attachments: RequestAttachmentDto[];
    quotations: SavedQuotationDto[];
    poGroups: RequestPoGroupDto[];
    approvalBatches?: ApprovalBatchSummary[];
    statusHistory: RequestStatusHistoryDto[];
    /**
     * Post-Payment Completion (Release 2): PROFORMA or FINAL_INVOICE for a PAYMENT request.
     * Null on QUOTATION requests and on requests created before the feature was activated.
     */
    billingDocumentType?: string | null;

    // B2P: Payment Condition
    paymentConditionCode?: string | null;
    advancePaymentPercent?: number | null;
    paymentConditionSource?: string | null;
}

export interface IvaRate {
    id: number;
    code: string;
    name: string;
    ratePercent: number;
    isActive: boolean;
}

export interface Unit {
    id: number;
    code: string;
    name: string;
    allowsDecimalQuantity: boolean;
    isActive?: boolean;
}

export interface OcrDraftItem {
    lineNumber: number;
    description: string;
    quantity: number;
    unitId: number | null;
    unit?: string; // Raw extracted unit string from OCR
    unitPrice: number;
    discountAmount: number;
    discountPercent?: number;
    ivaRateId: number | null;
    taxRate?: number; // Raw extracted tax percentage for suggestion hint
    totalPrice: number; // Front-end calculated preview
    isChecked?: boolean; // UI tracking variable for visual checklist
    isAutoSuggested?: boolean; // UI tracking variable for auto-suggest logic
    itemCatalogId?: number | null; // Optional catalog reference
    itemCatalogCode?: string | null; // Optional catalog code for display/traceability
    ivaUncertain?: boolean; // True when OCR could not confidently identify item-level IVA
    ivaGlobalInferred?: boolean; // True when IVA was inferred from document summary, not extracted per item
    autoMatchStatus?: 'AUTO_MATCHED' | 'NEEDS_REVIEW' | null; // Catalog auto-match result from OCR
    mappedRequestLineItemId?: string | null; // ID of the RequestLineItem this quotation item corresponds to
    reconciliationStatus?: 'MAPPED' | 'NOT_QUOTED' | 'EXTRA_ITEM' | 'IGNORED' | 'SUBSTITUTE';
    reconciliationJustification?: string | null;
    /** Persisted justification captured at hydration time (EDIT mode) — baseline for the
     * untouched-legacy validation exemption, mirroring the backend's legacy-vs-edited skip.
     * Undefined for lines created in this session. */
    originalReconciliationJustification?: string | null;

    // ── Financial Reconciliation (OCR baseline) ──
    /** 'OCR' for an extraction-produced line, 'MANUAL' for a buyer-added line. */
    lineOrigin?: 'OCR' | 'MANUAL' | null;
    /** Immutable OCR-original snapshot captured at extraction (new quotation) or hydrated from the
     * persisted baseline (edit). Null on a field = "not extracted", never an implicit 0. */
    ocrOriginalQuantity?: number | null;
    ocrOriginalUnitPrice?: number | null;
    ocrOriginalDiscountAmount?: number | null;
    ocrOriginalIvaRatePercent?: number | null;
    ocrOriginalUnitText?: string | null;
    ocrOriginalUnitId?: number | null;
    ocrOriginalLineTotal?: number | null;
    /** One consolidated reason for material financial-field edits vs the OCR baseline. */
    lineAdjustmentJustification?: string | null;
}

/** One line's reconciliation diagnostics (mirrors backend LineReconciliationDto). */
export interface LineReconciliationDto {
    quotationItemId?: string | null;
    lineNumber: number;
    description: string;
    reconciliationStatus: string;
    hasOcrBaseline: boolean;
    isManualAddition: boolean;
    quantityChanged: boolean;
    unitPriceChanged: boolean;
    discountChanged: boolean;
    ivaChanged: boolean;
    unitChanged: boolean;
    imputedOcrComponents: string[];
    requiresAdjustmentReason: boolean;
    hasAdjustmentReason: boolean;
    hasReconciliationReason: boolean;
}

/** Backend-authoritative reconciliation result (preview endpoint + Save/Update 409 extensions.reconciliation).
 * residualVariance is SIGNED; residualExceedsTolerance already applies Math.Abs. */
export interface QuotationReconciliationDto {
    ocrHeaderTotal: number;
    ocrLineSumTotal: number;
    reconstructedOcrLineSum: number;
    structuralHeaderDifference: number;
    ocrLineComponentDifference: number;
    finalConsideredTotal: number;
    manualAdditionsTotal: number;
    ignoredImpact: number;
    quantityImpact: number;
    unitPriceImpact: number;
    discountImpact: number;
    ivaImpact: number;
    globalDiscountImpact: number;
    manualAdditionsImpact: number;
    explainedLineAdjustments: number;
    residualVariance: number;
    toleranceApplied: number;
    residualExceedsTolerance: boolean;
    lines: LineReconciliationDto[];
}

export interface OcrDraft {
    supplierId: number | null;
    supplierNameSnapshot: string;
    supplierPortalCode?: string | null;
    supplierPrimaveraCode?: string | null;
    supplierTaxId?: string;
    companyId?: number | null;
    extractedCompanyName?: string;
    isCompanyOcrAutoFilled?: boolean;
    documentNumber: string;
    documentDate: string;
    dueDate?: string;
    documentType?: 'PROFORMA' | 'FINAL';
    currency: string;
    extractedCurrency?: string; // Raw extracted currency for suggestion hint
    discountAmount: number; // Front-end user input
    totalAmount: number; // Front-end calculated preview
    ocrTotalAmount?: number; // Raw total extracted by OCR
    proformaAttachmentId?: string; // Links attachment implicitly
    items: OcrDraftItem[];
    headerHasIva?: boolean; // True when the document header/totals indicate IVA exists
    globalVatInferred?: boolean; // True when global VAT was inferred from document summary and applied to all items
    inferredVatRatePercent?: number; // The inferred VAT rate percentage for display in UI banner
    supplierRegistrationStatus?: string;
    /** Backend authoritative supplier match result: { code, status, message, supplier, candidates }. */
    supplierMatch?: any;
    supplierAddress?: string;
    supplierContactName?: string;
    supplierEmail?: string;
    supplierPhone?: string;
    supplierBankAccountNumber?: string;
    supplierBankIban?: string;
    supplierBankSwift?: string;
    supplierPaymentTerms?: string;
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
    /** Departamentos: número de managers de aprovação ativos (Fase C). */
    managerCount?: number;
}

export interface SupplierSearchDto {
    id: number;
    portalCode: string;
    primaveraCode?: string;
    name: string;
    registrationStatus?: string;
}

export interface CurrencyDto {
    id: number;
    code: string;
    symbol: string;
    isActive: boolean;
}

// Item Catalog types
export interface ItemCatalogDto {
    id: number;
    code: string;
    description: string;
    primaveraCode: string | null;
    supplierCode: string | null;
    defaultUnitId: number | null;
    defaultUnitCode: string | null;
    defaultUnitName: string | null;
    category: string | null;
    origin: string;
    isActive: boolean;
    createdAtUtc: string;
    updatedAtUtc: string | null;
}

export interface RequesterItem {
    lineNumber: number;
    description: string;
    quantity: number;
    unitId: number | null;
    notes: string;
    itemCatalogId: number | null;
    itemCatalogCode: string | null;
}

export interface ImportResultDto {
    imported: number;
    skipped: number;
    errors: string[];
}

// Phase 2: Reconciliation types
export interface ReconciliationRecordDto {
    id: string;
    matchStatus: string;
    matchConfidence: number | null;
    matchStrategy: string | null;
    quantityDivergence: number | null;
    unitDivergence: boolean;
    buyerReviewStatus: string;
    buyerJustification: string | null;
    reviewedByName: string | null;
    reviewedAtUtc: string | null;
    requesterItemId: string | null;
    requesterDescription: string | null;
    requesterQuantity: number | null;
    requesterUnitCode: string | null;
    requesterCatalogId: number | null;
    requesterCatalogCode: string | null;
    ocrExtractedItemId: string | null;
    ocrDescription: string | null;
    ocrQuantity: number | null;
    ocrRawUnit: string | null;
    ocrUnitPrice: number | null;
    ocrLineTotal: number | null;
}

export interface ReconciliationSummaryDto {
    totalRecords: number;
    exactMatches: number;
    probableMatches: number;
    reviewRequired: number;
    extraSupplierItems: number;
    missingRequestedItems: number;
    buyerConfirmed: number;
    buyerPending: number;
    buyerRejected: number;
}

export interface ReconciliationBatchDto {
    extractionBatchId: string;
    extractedAtUtc: string;
    providerName: string | null;
    qualityScore: number;
    attachmentId: string | null;
    ocrItemCount: number;
    records: ReconciliationRecordDto[];
    summary: ReconciliationSummaryDto;
}

export interface ReconciliationReviewItemDto {
    recordId: string;
    reviewStatus: string;
    justification?: string;
}

/**
 * Captured once, before the first SaveQuotation create attempt of a logical wizard submission,
 * and reused unchanged across every retry (Financial Integrity override, controlled retry after
 * an ambiguous failure) of that same submission — never re-captured mid-sequence, or a quotation
 * created by an earlier attempt would become invisible to the "is this new?" check.
 */
export interface AmbiguousSavePreAttemptSnapshot {
    existingQuotationIds: Set<string>;
    attemptStartedAtUtc: string;
}

/** SaveQuotation Financial Integrity Gate 409 response (RequestsController.SaveQuotation). */
export interface FinancialIntegrityCheckFailedDto {
    integrityCheckFailed: true;
    ocrOriginalTotal: number;
    quotationTotal: number;
    varianceAmount: number;
    variancePercent: number;
    toleranceApplied: number;
    detail: string;
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
    /** 'skipped' = stage never executed (e.g. request closed without quotation) — rendered as "Não aplicável". */
    state: 'completed' | 'current' | 'pending' | 'blocked' | 'skipped';
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
    awaitingPo: number;
    awaitingPayment: number;
    completedRequests: number;

    filteredTotal: number;
    filteredCurrencyCodes: string[];
    filteredTotalTrend: number | null;
    filteredTotalTrendLabel: string | null;
}

// ── Cockpit Summary (Dashboard Operational Cockpit) ──

export interface CockpitSummaryDto {
    // My Work Queue
    myPendingActions: number;
    myUrgentItems: number;
    myAdjustmentItems: number;
    myOverdueItems: number;
    myNearDeadlineItems: number;

    // Pipeline counters
    totalActiveRequests: number;
    draft: number;
    waitingQuotation: number;
    waitingAreaApproval: number;
    waitingFinalApproval: number;
    inAdjustment: number;
    awaitingPo: number;
    awaitingPayment: number;
    paymentCompleted: number;
    waitingReceipt: number;
    completed: number;

    // Bottlenecks
    bottlenecks: StageBottleneckDto[];

    // Financial
    financialByStatus: FinancialByStatusDto[];

    // Alerts
    alerts: AttentionAlertDto[];
}

export interface StageBottleneckDto {
    stageCode: string;
    stageName: string;
    count: number;
    oldestCreatedAtUtc: string | null;
}

export interface FinancialByStatusDto {
    groupLabel: string;
    totalAmount: number;
    currencyCodes: string[];
    count: number;
}

export interface AttentionAlertDto {
    id: string;
    requestId: string;
    requestNumber: string;
    title: string;
    reason: string;
    responsibleArea: string;
    alertType: string;  // OVERDUE | NEAR_DEADLINE | STUCK | ADJUSTMENT
    severity: string;   // CRITICAL | WARNING | INFO
    createdAtUtc: string;
    targetPath: string;
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
    openAiEnabled: boolean;
    openAiModel?: string;
    openAiTimeoutSeconds?: number;
    azureDocumentIntelligenceEnabled: boolean;
    azureDocumentIntelligenceTimeoutSeconds?: number;
}

export interface OcrModuleConfigDto {
    id: number;
    moduleKey: string;
    displayName: string;
    isEnabled: boolean;
    allowedExtensions?: string;
    maxFileSizeMb?: number;
    providerOverride?: string;
    modelOverride?: string;
    updatedBy?: string;
    updatedAtUtc: string;
}

export interface SmtpSettingsDto {
    server?: string;
    port?: number;
    senderEmail?: string;
    senderName?: string;
    enableSsl: boolean;
    hasPassword: boolean;
    password?: string; // write-only: send to update, never received from GET
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

export interface DecisionAlertDto {
    type: string;
    level: 'INFO' | 'WARNING' | 'CRITICAL' | 'ERROR' | 'DANGER';
    message: string;
    relatedItemId?: string;
}

export interface DepartmentIntelligenceDto {
    monthAccumulatedTotal: number;
    yearAccumulatedTotal: number;
    monthApprovedCount: number;
    currentRequestSharePercentage: number;
    currency: string;
}

export interface ItemIntelligenceDto {
    lineItemId: string;
    description: string;
    currentUnitPrice: number;
    currency: string;
    
    // Historical
    lastPaidPrice?: number;
    averageHistoricalPrice?: number;
    lastSupplierName?: string;
    totalPurchaseCount: number;
    variationVsLastPercentage?: number;
    variationVsAvgPercentage?: number;
    hasHistory: boolean;
    matchType: string;
}

export interface ApprovalIntelligenceDto {
    requestId: string;
    items: ItemIntelligenceDto[];
    departmentContext: DepartmentIntelligenceDto;
    overallAlerts: DecisionAlertDto[];
    budgetAvailability?: BudgetAvailabilityDto;
    /** 'BATCH' = analysis scoped to the active ApprovalBatch (partial approval); 'REQUEST' = legacy whole-request analysis. */
    scope?: 'BATCH' | 'REQUEST';
}

export interface BudgetAvailabilityDto {
    hasBudgetConfig: boolean;
    matchLevel: string;
    annualBudget: number;
    committedAmount: number;
    availableBefore: number;
    currentRequestAmount: number;
    availableAfter: number;
    currencyCode: string;
    departmentName?: string;
    costCenterName?: string;
    plantName?: string;
    companyName?: string;
    fiscalYear: number;
    status: string;
    utilizationPercent: number;
    infoMessage?: string;
    costCenterBreakdown?: BudgetCostCenterBreakdownDto[];
    departmentCostCenters?: DepartmentCostCenterBudgetDto[];
}

export interface DepartmentCostCenterBudgetDto {
    costCenterId?: number;
    costCenterName: string;
    plantName?: string;
    annualBudget: number;
    committedAmount: number;
    availableAmount: number;
    utilizationPercent: number;
    status: string;
    isUsedInScope: boolean;
    hasBudgetConfigured: boolean;
}

export interface BudgetCostCenterBreakdownDto {
    costCenterId?: number;
    costCenterName: string;
    hasBudgetLine: boolean;
    annualBudget: number;
    committedAmount: number;
    requestAmountInCC: number;
    availableAfter: number;
    status: string;
    utilizationPercent: number;
}

export interface HistoricalPurchaseRecordDto {
    requestId: string;
    requestNumber: string;
    purchaseDate: string;
    supplierName: string;
    unitPrice: number;
    currency: string;
    isLastPurchase: boolean;
    isUsedInAverage: boolean;
    matchType: string;
    plantName?: string;
    departmentName?: string;
}

export interface FinanceCurrencyValueDto {
    totalAmount: number;
    currencyCode: string;
}

export interface FinanceAttentionPointDto {
    id: string;
    title: string;
    description: string;
    count: number;
    targetPath: string;
    type: 'WARNING' | 'INFO' | 'DANGER' | 'SUCCESS';
}

export interface FinanceSummaryDto {
    waitingFinanceAction: number;
    scheduledPayments: number;
    overduePayments: number;
    completedThisMonth: number;
    pendingValues: FinanceCurrencyValueDto[];
    scheduledValues: FinanceCurrencyValueDto[];
    overdueValues: FinanceCurrencyValueDto[];
    paidThisMonthValues: FinanceCurrencyValueDto[];
    currencyCodes: string[];
    attentionPoints: FinanceAttentionPointDto[];
    cashFlowProjections: FinanceCashFlowProjectionDto[];
    currencyExposures: FinanceCurrencyExposureDto[];
    topSuppliers: FinanceTopSupplierDto[];
    agingAnalysis: FinanceAgingAnalysisDto;
}

export interface FinanceCashFlowProjectionDto {
    date: string;
    totalAmount: number;
    currencyCode: string;
}

export interface FinanceCurrencyExposureDto {
    currencyCode: string;
    amount: number;
    count: number;
}

export interface FinanceTopSupplierDto {
    supplierName: string;
    totalPendingAmount: number;
    currencyCode: string;
    requestCount: number;
}

export interface FinanceAgingAnalysisDto {
    zeroToTwoDays: number;
    threeToFiveDays: number;
    moreThanFiveDays: number;
}

export interface FinanceListItemDto {
    id: string;
    requestNumber: string;
    title: string;
    supplierName: string;
    requesterName: string;
    plantName: string;
    amount: number;
    currencyCode: string | null;
    needByDateUtc: string | null;
    scheduledDateUtc: string | null;
    paidDateUtc: string | null;
    statusCode: string;
    statusName: string;
    statusBadgeColor: string;
    isOverdue: boolean;
    isDueSoon: boolean;
    isMissingDocuments: boolean;
    missingDocumentTypes: string[];
    availableFinanceActions: string[];
    poGroups: RequestPoGroupDto[];
    // Buy-to-Pay (Phase 8)
    paymentCondition?: string | null;
    advancePaymentPercent?: number | null;
}

export interface FinanceListResponseDto {
    pagedResult: PagedResult<FinanceListItemDto>;
    summary: FinanceSummaryDto;
}

export interface FinanceHistoryItemDto {
    id: string;
    requestId: string;
    requestNumber: string;
    requestTitle: string;
    amount: number | null;
    currencyCode: string | null;
    actionTaken: string;
    comment: string;
    createdAtUtc: string;
    actorName: string;
    newStatusCode: string | null;
    newStatusName: string | null;
    isVoided?: boolean;
    voidReason?: string | null;
}

export type PrimaveraRequestValidationStatus = 'VALID' | 'WARNING' | 'INVALID' | 'ERROR';

export interface PrimaveraRequestValidationResultDto {
    status: PrimaveraRequestValidationStatus;
    messages: string[];
    isSupplierFound: boolean;
    isArticleFound: boolean;
    isRelationshipValid: boolean;
}

// ─── Primavera Synchronization Types ──────────────────────────────────────

export type SyncMatchStatus = 'New' | 'Exists' | 'Conflict';

export interface CatalogSyncPreviewItemDto {
    primaveraCode: string;
    primaveraDescription: string | null;
    primaveraFamily: string | null;
    primaveraBaseUnit: string | null;
    primaveraIsCancelled: boolean;
    portalItemId: number | null;
    portalCode: string | null;
    portalDescription: string | null;
    status: SyncMatchStatus;
    conflictDetail: string | null;
}

export interface CatalogSyncPreviewDto {
    totalPrimaveraRecords: number;
    newCount: number;
    existsCount: number;
    conflictCount: number;
    items: CatalogSyncPreviewItemDto[];
}

export interface SupplierSyncPreviewItemDto {
    primaveraCode: string;
    primaveraName: string | null;
    primaveraTaxId: string | null;
    primaveraIsCancelled: boolean;
    portalSupplierId: number | null;
    portalName: string | null;
    portalPrimaveraCode: string | null;
    portalTaxId: string | null;
    status: SyncMatchStatus;
    conflictDetail: string | null;
}

export interface SupplierSyncPreviewDto {
    totalPrimaveraRecords: number;
    newCount: number;
    existsCount: number;
    conflictCount: number;
    items: SupplierSyncPreviewItemDto[];
}

export interface SyncImportRequestDto {
    selectedPrimaveraCodes: string[];
}

export interface SyncImportResultDto {
    created: number;
    skipped: number;
    errors: string[];
}

// ── Reviewed Supplier Import (V2) ───────────────────────────────────────────

export interface ReviewedSupplierItemDto {
    primaveraCode: string;
    name: string;
    taxId: string | null;
    notes: string | null;
}

export interface SyncSupplierReviewedImportRequestDto {
    suppliers: ReviewedSupplierItemDto[];
}

// ── Catalog Conflict Resolution Types ─────────────────────────────────────

export type CatalogConflictResolution =
    | 'UpdatePortal'
    | 'ConfirmAssociation'
    | 'CreateNew'
    | 'AssociateManually';

export interface CatalogResolveConflictRequestDto {
    primaveraCode: string;
    resolution: CatalogConflictResolution;
    portalItemId?: number | null;
    targetPortalItemId?: number | null;
    primaveraDescription?: string | null;
    primaveraFamily?: string | null;
    primaveraBaseUnit?: string | null;
    updateFields?: string[] | null;
}

export interface CatalogResolveConflictResultDto {
    success: boolean;
    message: string;
    affectedPortalItemId?: number | null;
}

export interface DepartmentMasterDto {
    id: number;
    departmentCode: string;
    departmentName: string;
    companyCode: string;
    displayName: string;
}

// ─── Catalog Item Reconciliation Types ────────────────────────────────────────

/** Internal status codes for catalog item reconciliation (English constants). */
export type ReconciliationItemStatus =
    | 'MATCHED'
    | 'UNMATCHED'
    | 'LOW_CONFIDENCE'
    | 'CREATED_PENDING'
    | 'LINKED_MANUALLY'
    | 'FREE_TEXT';

/** Any item array fed to the reconciliation hook must satisfy this interface. */
export interface ReconcilableItem {
    description: string;
    itemCatalogId?: number | null;
    itemCatalogCode?: string | null;
    reconciliationStatus?: ReconciliationItemStatus;
    reconciliationJustification?: string;
}

/** Per-item resolution outcome produced by the reconciliation modal. */
export interface ItemResolution {
    itemIndex: number;
    status: ReconciliationItemStatus;
    linkedCatalogId?: number | null;
    linkedCatalogCode?: string | null;
    linkedDescription?: string;
    defaultUnitId?: number | null;
    justification?: string;
}

/** Classified item returned by the reconciliation hook (original + computed status). */
export interface ClassifiedItem<T extends ReconcilableItem = ReconcilableItem> {
    item: T;
    index: number;
    status: ReconciliationItemStatus;
    justification?: string;
}

// ─── Integration Management Types ─────────────────────────────────────────

/** GET response DTO for integration provider settings. Never contains secrets. */
export interface IntegrationSettingsDto {
    code: string;
    name: string;
    providerType: string;
    connectionType: string;
    description?: string;
    environment?: string;
    isEnabled: boolean;
    isPlanned: boolean;
    isReadOnly: boolean;

    // Connection settings (non-secret)
    server?: string;
    databaseName?: string;
    instanceName?: string;
    authenticationMode?: string;
    username?: string;
    apiBaseUrl?: string;
    timeoutSeconds?: number;
    additionalConfig?: string;

    // SMTP-specific settings
    port?: number;
    enableSsl?: boolean;
    senderEmail?: string;
    senderName?: string;

    // SMTP Email Environment Identification
    enableSubjectPrefix?: boolean;
    subjectPrefixText?: string;
    enableBodyWarningBanner?: boolean;
    warningBannerText?: string;
    redirectAllToTestRecipient?: boolean;
    testRecipientEmail?: string;
    showOriginalRecipientsInBody?: boolean;
    allowRealRecipientsInNonProduction?: boolean;

    // Secret presence indicators — NEVER actual secrets
    hasPassword: boolean;
    hasApiKey: boolean;
    secretVersion: number;

    // Last connection test status
    lastTestStatus?: string;
    lastTestAt?: string;
    lastTestMessage?: string;
    lastTestResponseTimeMs?: number;

    // Company-specific settings for Primavera
    primaveraCompanies?: PrimaveraCompanySettingsDto[];

    // Plant-specific settings for AlplaPROD
    alplaProdPlants?: AlplaProdPlantSettingsDto[];

    // Audit
    updatedByUserName?: string;
    updatedAt?: string;
}

export interface PrimaveraCompanySettingsDto {
    companyKey: string;
    databaseName?: string;
    enabled: boolean;
    username?: string;
    hasPassword: boolean;
    secretVersion: number;
}

export interface UpdatePrimaveraCompanyDto {
    companyKey: string;
    databaseName?: string;
    enabled: boolean;
    username?: string;
}

export interface ReplacePrimaveraCompanySecretDto {
    companyKey: string;
    newPassword: string;
}

export interface AlplaProdPlantSettingsDto {
    plantKey: string;
    server?: string;
    databaseName?: string;
    enabled: boolean;
    username?: string;
    hasPassword: boolean;
    usesGlobalCredentials: boolean;
    secretVersion: number;
    pipelineModel?: string;
}

export interface UpdateAlplaProdPlantDto {
    plantKey: string;
    server?: string;
    databaseName?: string;
    enabled: boolean;
    username?: string;
}

export interface ReplaceAlplaProdPlantSecretDto {
    plantKey: string;
    newPassword: string;
}

/** PUT request DTO for updating non-secret integration settings. */
export interface UpdateIntegrationSettingsDto {
    server?: string;
    databaseName?: string;
    instanceName?: string;
    authenticationMode?: string;
    username?: string;
    apiBaseUrl?: string;
    timeoutSeconds?: number;
    additionalConfig?: string;

    // SMTP-specific settings
    port?: number;
    enableSsl?: boolean;
    senderEmail?: string;
    senderName?: string;

    // SMTP Email Environment Identification
    enableSubjectPrefix?: boolean;
    subjectPrefixText?: string;
    enableBodyWarningBanner?: boolean;
    warningBannerText?: string;
    redirectAllToTestRecipient?: boolean;
    testRecipientEmail?: string;
    showOriginalRecipientsInBody?: boolean;
    allowRealRecipientsInNonProduction?: boolean;
}

/** POST request DTO for replacing an encrypted secret. */
export interface ReplaceIntegrationSecretDto {
    secretType: 'PASSWORD' | 'API_KEY';
    newSecretValue: string;
}

/** POST response DTO from test-connection endpoint. */
export interface IntegrationConnectionTestResultDto {
    providerCode: string;
    success: boolean;
    message?: string;
    responseTimeMs?: number;
    testedAtUtc: string;
}



export interface RequestPaymentDto {
    id: number;
    paymentType: string;
    paymentStatus: string;
    plannedAmount: number;
    actualPaidAmount: number | null;
    scheduledDateUtc: string | null;
    paidDateUtc: string | null;
    currencyCode: string | null;
    hasDivergence: boolean;
}

