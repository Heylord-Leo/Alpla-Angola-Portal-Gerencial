import { OcrDocumentClassification } from '../lib/documentClassificationDecision';
import { SourceDocumentType } from '../lib/sourceDocumentType';

/**
 * A document that originates a PAYMENT request.
 *
 * <p>PAYMENT only. Quotation Management keeps one document per quotation and nothing here reaches
 * it.</p>
 */
export interface PaymentSourceDocumentDto {
    id: string;
    /** Backend-assigned. Never renumbered after a removal — "Documento 2" must keep its meaning. */
    sequenceNumber: number;

    attachmentId: string;
    attachmentFileName?: string | null;
    attachmentStorageReference?: string | null;

    supplierId?: number | null;
    supplierNameSnapshot?: string | null;
    supplierTaxIdSnapshot?: string | null;
    plantId?: number | null;
    plantName?: string | null;

    /** One of the seven approved values; see SOURCE_DOCUMENT_TYPES. */
    sourceDocumentType?: SourceDocumentType | string | null;
    documentNumber?: string | null;
    documentSeries?: string | null;
    documentDate?: string | null;
    dueDate?: string | null;
    currency?: string | null;

    netAmount?: number | null;
    taxAmount?: number | null;
    grossAmount?: number | null;
    /** Sum of this document's active items — the number the gross must agree with. */
    itemsTotal: number;

    ocrSuggestion?: string | null;
    ocrConfidence?: number | null;
    ocrTitleFound?: string | null;
    ocrEvidenceJson?: string | null;
    ocrConflictingEvidenceJson?: string | null;
    classificationSource?: string | null;
    classificationSuggestionSource?: 'OCR' | 'FALLBACK' | null;
    classificationConflictAcknowledged: boolean;
    classificationJustification?: string | null;

    classificationReviewedByFinance: boolean;
    classificationReviewedByUserId?: string | null;
    classificationReviewedByName?: string | null;
    classificationReviewedAtUtc?: string | null;

    requiresOperationInvoice: boolean;
    requiresAdvanceRegularization: boolean;
    requiresFinanceClassificationReview: boolean;

    isVoided: boolean;
    voidReason?: string | null;

    items: PaymentSourceDocumentItemDto[];

    /** Why this document is not yet submittable. Empty when it is. */
    validationMessages: string[];
    isValid: boolean;

    /** Echoed on update — a stale edit is reported, never merged. */
    rowVersion?: string | null;
}

export interface PaymentSourceDocumentItemDto {
    id: string;
    lineNumber: number;
    description: string;
    quantity: number;
    unitId?: number | null;
    unitCode?: string | null;
    unitPrice: number;
    discountAmount?: number | null;
    ivaRateId?: number | null;
    totalAmount: number;
    plantId?: number | null;
    supplierId?: number | null;
    requestPoGroupId?: string | null;
}

/** Everything the payment screen needs about a request's origin documents. */
export interface PaymentSourceDocumentsSummaryDto {
    requestId: string;
    documents: PaymentSourceDocumentDto[];
    /** Voided documents, kept visible because their decisions were audited. */
    voidedDocuments: PaymentSourceDocumentDto[];
    requestTotal: number;
    currency?: string | null;

    canEditDocuments: boolean;
    editBlockedReason?: string | null;

    /** Request-level problems: no documents at all, mixed currencies. */
    requestValidationMessages: string[];

    /**
     * Non-blocking: the same supplier appears with different document types. Submission is never
     * refused because of it, and no justification is owed.
     */
    mixedTypeNotice?: string | null;

    canSubmit: boolean;
}

/** Create or replace a source document. */
export interface SavePaymentSourceDocumentDto {
    attachmentId?: string | null;

    supplierId?: number | null;
    supplierTaxIdSnapshot?: string | null;
    plantId?: number | null;

    sourceDocumentType?: string | null;
    documentNumber?: string | null;
    documentSeries?: string | null;
    documentDate?: string | null;
    dueDate?: string | null;
    currency?: string | null;

    netAmount?: number | null;
    taxAmount?: number | null;
    grossAmount?: number | null;

    ocrSuggestion?: string | null;
    ocrConfidence?: number | null;
    ocrTitleFound?: string | null;
    ocrEvidenceJson?: string | null;
    ocrConflictingEvidenceJson?: string | null;
    classificationSource?: string | null;
    classificationSuggestionSource?: 'OCR' | 'FALLBACK' | null;
    classificationConflictAcknowledged?: boolean | null;
    classificationJustification?: string | null;

    /** Required on update. Absent or stale is reported as a conflict, never merged. */
    rowVersion?: string | null;
}

export interface VoidPaymentSourceDocumentDto {
    reason?: string | null;
    rowVersion?: string | null;
}

/**
 * A 409 from an update or void. Carries the server's current view so the UI can show what moved
 * instead of leaving the user to guess.
 */
export interface PaymentSourceDocumentConflictDto {
    title: string;
    detail: string;
    status: number;
    current?: PaymentSourceDocumentDto | null;
}

/**
 * One document's OCR reading, held per document.
 *
 * <p>The single most important property of this feature: <b>nothing about a document's reading may
 * live in a request-level variable.</b> Uploading Documento 2 must not touch Documento 1.</p>
 */
export interface PaymentDocumentOcrState {
    isProcessing: boolean;
    classification: OcrDocumentClassification | null;
    /** Persistent, not a toast — a failed read must stay on screen until it is dealt with. */
    error: string | null;
}
