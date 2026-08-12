/**
 * Release 4 Phase 3B — Operation Invoice ("Fatura Final") types.
 *
 * Mirrors the backend contracts exactly:
 * - OperationInvoicesController (Phase 2 CRUD + Phase 3A allocations/validate)
 * - GET /requests/{id}/operation-invoice-obligations (coverage read model)
 * - OperationInvoiceShortClosesController
 *
 * The obligations endpoint is the ONE authoritative coverage read — nothing in the frontend
 * reconstructs coverage from invoice lists.
 */

// ── Document lifecycle (RequestConstants.OperationInvoiceDocumentStatuses) ──────────────────

export type OperationInvoiceDocumentStatus =
    | 'UPLOADED'
    | 'PENDING_VALIDATION'
    | 'VALIDATED'
    | 'REJECTED'
    | 'VOIDED'
    | 'REPLACEMENT_REQUESTED'
    | 'DIVERGENCE_DETECTED';

// ── Group aggregate (RequestConstants.OperationInvoiceStatuses) ─────────────────────────────

export type OperationInvoiceAggregateStatus =
    | 'UNCLASSIFIED'
    | 'NOT_REQUIRED'
    | 'PENDING_UPLOAD'
    | 'PENDING_VALIDATION'
    | 'PARTIALLY_INVOICED'
    | 'SATISFIED'
    | 'DIVERGENCE_DETECTED';

// ── Invoice document ────────────────────────────────────────────────────────────────────────

export interface OperationInvoiceDto {
    id: string;
    requestId: string;

    supplierId?: number | null;
    supplierName?: string | null;
    supplierTaxIdSnapshot?: string | null;

    documentNumber?: string | null;
    documentSeries?: string | null;
    documentDate?: string | null;
    dueDate?: string | null;
    currency?: string | null;

    netAmount?: number | null;
    taxAmount?: number | null;
    grossAmount?: number | null;

    status: string;
    amountsEnteredManually: boolean;

    notes?: string | null;

    attachmentId: string;
    attachmentFileName?: string | null;

    uploadedAtUtc: string;
    uploadedByUserId: string;
    uploadedByName?: string | null;

    updatedAtUtc?: string | null;

    validatedAtUtc?: string | null;
    validatedByUserId?: string | null;
    rejectionReason?: string | null;

    voidedAtUtc?: string | null;
    voidReason?: string | null;

    supersededByOperationInvoiceId?: string | null;

    rowVersion?: string | null;
}

export interface SaveOperationInvoiceDto {
    attachmentId?: string | null;
    supplierId?: number | null;
    documentNumber?: string | null;
    documentSeries?: string | null;
    documentDate?: string | null;
    dueDate?: string | null;
    currency?: string | null;
    netAmount?: number | null;
    taxAmount?: number | null;
    grossAmount?: number | null;
    notes?: string | null;
    amountsEnteredManually?: boolean | null;
    rowVersion?: string | null;
}

export interface ReplaceOperationInvoiceDto extends SaveOperationInvoiceDto {
    replacementReason?: string | null;
}

export interface CheckOperationInvoiceDuplicateDto {
    contentHash?: string | null;
    supplierId?: number | null;
    documentNumber?: string | null;
    documentSeries?: string | null;
}

export interface OperationInvoiceDuplicateCandidateDto {
    operationInvoiceId: string;
    requestId: string;
    requestNumber?: string | null;
    documentNumber?: string | null;
    documentSeries?: string | null;
    status: string;
}

export interface OperationInvoiceDuplicateResultDto {
    hasDuplicate: boolean;
    sameFile?: OperationInvoiceDuplicateCandidateDto | null;
    sameBusinessDocument?: OperationInvoiceDuplicateCandidateDto | null;
}

// ── Allocations (Phase 3A) ──────────────────────────────────────────────────────────────────

export interface OperationInvoiceAllocationDto {
    id: string;
    operationInvoiceId: string;
    requestPoGroupId: string;

    allocatedNetAmount: number;
    allocatedTaxAmount: number;
    allocatedGrossAmount: number;

    sequenceNumber: number;
    notes?: string | null;

    createdAtUtc: string;
    updatedAtUtc?: string | null;

    invoiceStatus: string;
    invoiceDocumentNumber?: string | null;
    invoiceDocumentSeries?: string | null;

    /** Counts toward validated coverage (invoice VALIDATED). Server-derived. */
    isEffective: boolean;
    /** Draft awaiting the Finance decision. Server-derived. */
    isPendingDecision: boolean;

    groupSupplierName?: string | null;
    groupCurrencyCode?: string | null;
}

export interface SaveOperationInvoiceAllocationItemDto {
    requestPoGroupId: string;
    allocatedNetAmount: number;
    allocatedTaxAmount: number;
    allocatedGrossAmount: number;
    notes?: string | null;
}

export interface SaveOperationInvoiceAllocationsDto {
    rowVersion?: string | null;
    allocations: SaveOperationInvoiceAllocationItemDto[];
}

export interface OperationInvoiceDivergenceAcceptanceDto {
    requestPoGroupId: string;
    accepted: boolean;
    justification?: string | null;
}

export interface ValidateOperationInvoiceDto {
    rowVersion?: string | null;
    divergenceAcceptances?: OperationInvoiceDivergenceAcceptanceDto[] | null;
}

// ── Obligations / coverage read model ───────────────────────────────────────────────────────

export interface OperationInvoiceObligationAllocationDto {
    allocationId: string;
    operationInvoiceId: string;
    invoiceDocumentNumber?: string | null;
    invoiceDocumentSeries?: string | null;
    invoiceStatus: string;

    allocatedNetAmount: number;
    allocatedTaxAmount: number;
    allocatedGrossAmount: number;
    sequenceNumber: number;
    notes?: string | null;

    isEffective: boolean;
    isPendingDecision: boolean;

    createdAtUtc: string;
    updatedAtUtc?: string | null;
}

export interface OperationInvoiceObligationDto {
    groupId: string;

    supplierId?: number | null;
    supplierName?: string | null;
    currency?: string | null;
    paymentConditionCode?: string | null;
    plantId?: number | null;

    sourceDocumentType?: string | null;
    requiresOperationInvoice: boolean;

    /** Null when never captured (pre-activation group). Unknown finish line — NEVER zero. */
    expectedAmount?: number | null;
    expectedCurrency?: string | null;

    validatedCoveredAmount: number;
    pendingCoveredAmount: number;
    remainingAmount?: number | null;
    appliedTolerance: number;

    /** Null when the expected total is unknown or not positive. */
    coveragePercent?: number | null;
    allocations: OperationInvoiceObligationAllocationDto[];

    derivedStatus: string;
    persistedStatus: string;
    statusDrift: boolean;

    closedShort: boolean;

    purchaseOrderNumber?: string | null;

    paymentSourceDocumentIds: string[];
    lineItemCount: number;

    reasonCode: string;
    explanation: string;
}

export interface OperationInvoiceObligationRollupDto {
    totalGroups: number;
    requiringOperationInvoiceCount: number;
    pendingActionCount: number;
    satisfiedCount: number;
    notRequiredCount: number;
    unclassifiedCount: number;
    driftCount: number;
    hasStatusDrift: boolean;
    groupsWithUnknownExpectedTotal: number;
    currencyTotals: {
        currencyCode: string;
        expectedTotal: number;
        validatedTotal: number;
        pendingValidationTotal: number;
        remainingTotal: number;
        groupsWithUnknownExpectedTotal: number;
    }[];
}

export interface OperationInvoiceObligationsDto {
    requestId: string;
    obligations: OperationInvoiceObligationDto[];
    rollup: OperationInvoiceObligationRollupDto;
}

// ── Short-close (Phase 3A) ──────────────────────────────────────────────────────────────────

export type OperationInvoiceShortCloseStatus = 'PROPOSED' | 'APPROVED' | 'REJECTED';

export interface OperationInvoiceShortCloseDto {
    id: string;
    requestPoGroupId: string;
    status: string;

    proposedByUserId: string;
    proposedByName?: string | null;
    proposedAtUtc: string;
    proposalJustification: string;
    evidenceAttachmentId?: string | null;
    remainingAmountAtProposal: number;

    decidedByUserId?: string | null;
    decidedByName?: string | null;
    decidedAtUtc?: string | null;
    decisionReason?: string | null;

    rowVersion?: string | null;
}

export interface ProposeOperationInvoiceShortCloseDto {
    justification?: string | null;
    evidenceAttachmentId?: string | null;
}

export interface DecideOperationInvoiceShortCloseDto {
    decisionReason?: string | null;
    rowVersion?: string | null;
}
