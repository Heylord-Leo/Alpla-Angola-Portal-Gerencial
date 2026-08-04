import { PaymentSourceDocumentDto, SavePaymentSourceDocumentDto } from '../types/paymentSourceDocument';
import { ClassificationConflictState, EMPTY_CONFLICT, OcrDocumentClassification } from './documentClassificationDecision';

/**
 * Creating a PAYMENT request that carries several source documents takes more than one call: the
 * request must exist before a document can be attached to it, and a document must exist before its
 * items can name it. That makes creation a small workflow rather than a single POST — and a workflow
 * that can fail halfway.
 *
 * <p>Everything in this module is pure. The orchestration is a reducer over an explicit phase, so
 * "which documents were actually saved" is a value that can be inspected, retried and reported,
 * rather than something inferred from whatever the last exception happened to be.</p>
 */

// ── Phase ───────────────────────────────────────────────────────────────────────────────────

export type CreationPhase =
    | 'NOT_STARTED'
    | 'CREATING_REQUEST'
    | 'SAVING_DOCUMENTS'
    | 'SAVING_ITEMS'
    | 'PARTIAL_FAILURE'
    | 'COMPLETE';

/** Human wording for each phase. The user must never be told "a guardar" while nothing is moving. */
export const PHASE_LABEL: Record<CreationPhase, string> = {
    NOT_STARTED: '',
    CREATING_REQUEST: 'A criar o pedido…',
    SAVING_DOCUMENTS: 'A guardar os documentos…',
    SAVING_ITEMS: 'A associar os itens…',
    PARTIAL_FAILURE: 'Nem todos os documentos foram guardados.',
    COMPLETE: 'Pedido criado.'
};

// ── Temporary documents ─────────────────────────────────────────────────────────────────────

/**
 * A document being composed before the request exists.
 *
 * <p><b>`tempId` is the identity for the whole of its client life</b>, including the map of OCR
 * results and conflict decisions. Keying by array position would reassign one document's reading to
 * another the moment anything was removed.</p>
 */
export interface TemporaryPaymentDocument {
    tempId: string;
    /** Set once the file is uploaded. Reused on retry so the same file is never uploaded twice. */
    attachmentId: string | null;
    attachmentFileName: string | null;

    supplierId: number | null;
    supplierNameSnapshot: string | null;
    supplierTaxIdSnapshot: string | null;
    plantId: number | null;

    sourceDocumentType: string | null;
    documentNumber: string | null;
    documentSeries: string | null;
    documentDate: string | null;
    dueDate: string | null;
    currency: string | null;

    netAmount: number | null;
    taxAmount: number | null;
    grossAmount: number | null;

    classification: OcrDocumentClassification | null;
    conflict: ClassificationConflictState;

    /** Items composed client-side, owned by this document until it is persisted. */
    items: TemporaryPaymentItem[];

    /** Filled in by Stage C. Its presence is what makes a retry skip this document. */
    persistedId: string | null;
    sequenceNumber: number | null;
    /** Why this document failed to persist, if it did. */
    error: string | null;
}

export interface TemporaryPaymentItem {
    tempId: string;
    description: string;
    quantity: number;
    unitId: number | null;
    unitPrice: number;
    discountAmount: number | null;
    ivaRateId: number | null;
    totalAmount: number;
    /** Set after the owning document is persisted, never before. */
    persistedId: string | null;
}

let counter = 0;
/** Collision-proof without a uuid dependency: monotonic counter plus the clock. */
export function newTempId(prefix = 'tmp'): string {
    counter += 1;
    return `${prefix}-${Date.now().toString(36)}-${counter}`;
}

export function createTemporaryDocument(
    seed: Partial<TemporaryPaymentDocument> = {}
): TemporaryPaymentDocument {
    return {
        tempId: newTempId('doc'),
        attachmentId: null,
        attachmentFileName: null,
        supplierId: null,
        supplierNameSnapshot: null,
        supplierTaxIdSnapshot: null,
        plantId: null,
        sourceDocumentType: null,
        documentNumber: null,
        documentSeries: null,
        documentDate: null,
        dueDate: null,
        currency: null,
        netAmount: null,
        taxAmount: null,
        grossAmount: null,
        classification: null,
        conflict: EMPTY_CONFLICT,
        items: [],
        persistedId: null,
        sequenceNumber: null,
        error: null,
        ...seed
    };
}

/**
 * The subset copied by "Duplicar dados básicos" at creation time.
 *
 * <p>Identical in spirit to the persisted version: shared commercial context only. The file, its
 * number and series, its dates, its values, its OCR reading, its classification decision and its
 * items are what make a document a distinct document, and copying any of them would attach one
 * file's evidence to another.</p>
 */
export function duplicateTemporaryBasics(source: TemporaryPaymentDocument): TemporaryPaymentDocument {
    return createTemporaryDocument({
        supplierId: source.supplierId,
        supplierNameSnapshot: source.supplierNameSnapshot,
        supplierTaxIdSnapshot: source.supplierTaxIdSnapshot,
        plantId: source.plantId,
        currency: source.currency
    });
}

// ── What may be persisted ───────────────────────────────────────────────────────────────────

/**
 * A document needs an attachment before it can exist server-side — that is the one field the create
 * endpoint refuses without. Everything else may be filled in afterwards, which is what allows an
 * incomplete draft to be saved.
 */
export function canPersist(document: TemporaryPaymentDocument): boolean {
    return !!document.attachmentId && !document.persistedId;
}

/** Documents that still have to be created, in the order the user added them. */
export function pendingDocuments(documents: TemporaryPaymentDocument[]): TemporaryPaymentDocument[] {
    return documents.filter(canPersist);
}

/** Documents that cannot be persisted at all, so the user is told rather than left wondering. */
export function unpersistableDocuments(documents: TemporaryPaymentDocument[]): TemporaryPaymentDocument[] {
    return documents.filter(d => !d.persistedId && !d.attachmentId);
}

export function toCreatePayload(document: TemporaryPaymentDocument): SavePaymentSourceDocumentDto {
    const suggestion = document.classification?.suggestedType ?? null;

    return {
        attachmentId: document.attachmentId,
        supplierId: document.supplierId,
        supplierTaxIdSnapshot: document.supplierTaxIdSnapshot,
        plantId: document.plantId,
        sourceDocumentType: document.sourceDocumentType,
        documentNumber: document.documentNumber,
        documentSeries: document.documentSeries,
        documentDate: document.documentDate,
        dueDate: document.dueDate,
        currency: document.currency,
        netAmount: document.netAmount,
        taxAmount: document.taxAmount,
        grossAmount: document.grossAmount,
        ocrSuggestion: suggestion,
        ocrConfidence: document.classification?.confidence ?? null,
        ocrTitleFound: document.classification?.titleFound ?? null,
        ocrEvidenceJson: document.classification ? JSON.stringify(document.classification) : null,
        ocrConflictingEvidenceJson: document.classification?.conflictingEvidence?.length
            ? JSON.stringify(document.classification.conflictingEvidence) : null,
        classificationSource:
            suggestion && suggestion === document.sourceDocumentType ? 'OCR_CONFIRMED' : 'USER_SELECTED',
        classificationSuggestionSource: document.classification
            ? (document.classification.isFallback ? 'FALLBACK' : 'OCR')
            : null,
        classificationConflictAcknowledged: document.conflict.acknowledged,
        classificationJustification: document.conflict.justification.trim() || null
    };
}

// ── Result of a persistence run ─────────────────────────────────────────────────────────────

export interface PersistenceOutcome {
    phase: CreationPhase;
    documents: TemporaryPaymentDocument[];
    /** Documents that failed, so the UI can name them instead of saying "algo correu mal". */
    failures: Array<{ tempId: string; label: string; message: string }>;
}

/**
 * Folds one document's result into the running state.
 *
 * <p>A failure on Documento 2 must leave Documento 1's persisted id intact — the whole point of
 * tracking `persistedId` per document is that a retry skips what already succeeded rather than
 * creating it twice.</p>
 */
export function applyDocumentResult(
    documents: TemporaryPaymentDocument[],
    tempId: string,
    result: { persisted: PaymentSourceDocumentDto } | { error: string }
): TemporaryPaymentDocument[] {
    return documents.map(d => {
        if (d.tempId !== tempId) return d;

        if ('error' in result) return { ...d, error: result.error };

        return {
            ...d,
            persistedId: result.persisted.id,
            sequenceNumber: result.persisted.sequenceNumber,
            error: null
        };
    });
}

/** The phase implied by the current document set. Derived, never set by hand. */
export function derivePhase(documents: TemporaryPaymentDocument[]): CreationPhase {
    const persistable = documents.filter(d => !!d.attachmentId);
    if (persistable.length === 0) return 'NOT_STARTED';

    if (persistable.some(d => d.error)) return 'PARTIAL_FAILURE';
    if (persistable.every(d => d.persistedId)) return 'COMPLETE';

    return 'SAVING_DOCUMENTS';
}

export function summariseFailures(documents: TemporaryPaymentDocument[]) {
    return documents
        .filter(d => !!d.error)
        .map((d, index) => ({
            tempId: d.tempId,
            label: d.sequenceNumber ? `Documento ${d.sequenceNumber}` : `Documento ${index + 1}`,
            message: d.error!
        }));
}

// ── Totals, for the creation screen ─────────────────────────────────────────────────────────

export function temporaryTotals(documents: TemporaryPaymentDocument[]) {
    return {
        count: documents.length,
        net: documents.reduce((s, d) => s + (d.netAmount ?? 0), 0),
        tax: documents.reduce((s, d) => s + (d.taxAmount ?? 0), 0),
        gross: documents.reduce((s, d) => s + (d.grossAmount ?? 0), 0),
        currency: documents.find(d => !!d.currency)?.currency ?? null
    };
}

/** The currency the request has committed to, so later documents default to it. */
export function temporaryEstablishedCurrency(documents: TemporaryPaymentDocument[]): string | null {
    return documents.find(d => !!d.currency)?.currency ?? null;
}

// ── Adapter: one card component for both stages ─────────────────────────────────────────────

/**
 * Presents a temporary document in the shape the persisted card expects.
 *
 * <p>This is what lets creation and editing share <b>one</b> visual implementation instead of
 * growing a second one that drifts. The card renders a document; whether that document has reached
 * the database is a concern of the container, not of the layout.</p>
 *
 * <p>The id it exposes is the <b>temporary</b> id, because that is the key the card's callbacks come
 * back with. Substituting the persisted id here would break the mapping the moment a document was
 * saved.</p>
 */
export function asCardDocument(
    document: TemporaryPaymentDocument,
    displaySequence: number
): PaymentSourceDocumentDto {
    const itemsTotal = document.items.reduce((s, i) => s + (i.totalAmount ?? 0), 0);

    return {
        id: document.tempId,
        sequenceNumber: document.sequenceNumber ?? displaySequence,
        attachmentId: document.attachmentId ?? '',
        attachmentFileName: document.attachmentFileName,
        supplierId: document.supplierId,
        supplierNameSnapshot: document.supplierNameSnapshot,
        supplierTaxIdSnapshot: document.supplierTaxIdSnapshot,
        plantId: document.plantId,
        plantName: null,
        sourceDocumentType: document.sourceDocumentType,
        documentNumber: document.documentNumber,
        documentSeries: document.documentSeries,
        documentDate: document.documentDate,
        dueDate: document.dueDate,
        currency: document.currency,
        netAmount: document.netAmount,
        taxAmount: document.taxAmount,
        grossAmount: document.grossAmount,
        itemsTotal,
        ocrSuggestion: document.classification?.suggestedType ?? null,
        ocrConfidence: document.classification?.confidence ?? null,
        ocrTitleFound: document.classification?.titleFound ?? null,
        ocrEvidenceJson: null,
        ocrConflictingEvidenceJson: null,
        classificationSource: null,
        classificationSuggestionSource: document.classification
            ? (document.classification.isFallback ? 'FALLBACK' : 'OCR') : null,
        classificationConflictAcknowledged: document.conflict.acknowledged,
        classificationJustification: document.conflict.justification,
        classificationReviewedByFinance: false,
        classificationReviewedByUserId: null,
        classificationReviewedByName: null,
        classificationReviewedAtUtc: null,
        requiresOperationInvoice: false,
        requiresAdvanceRegularization: false,
        requiresFinanceClassificationReview: false,
        isVoided: false,
        voidReason: null,
        items: document.items.map((i, idx) => ({
            id: i.tempId,
            lineNumber: idx + 1,
            description: i.description,
            quantity: i.quantity,
            unitId: i.unitId,
            unitCode: null,
            unitPrice: i.unitPrice,
            discountAmount: i.discountAmount,
            ivaRateId: i.ivaRateId,
            totalAmount: i.totalAmount,
            plantId: document.plantId,
            supplierId: document.supplierId,
            requestPoGroupId: null
        })),
        validationMessages: localValidation(document),
        isValid: localValidation(document).length === 0,
        rowVersion: null
    };
}

/**
 * What can be checked before the server sees the document. Deliberately a subset of the backend
 * rules — the server re-checks everything and stays authoritative; this exists so a card can show
 * "Incompleto" without a round trip.
 */
export function localValidation(document: TemporaryPaymentDocument): string[] {
    const problems: string[] = [];

    if (!document.attachmentId) problems.push('Anexe o documento.');
    if (!document.supplierId) problems.push('Indique o fornecedor.');
    if (!document.plantId) problems.push('Indique a planta.');
    if (!document.documentNumber?.trim()) problems.push('Indique o número do documento.');
    if (!document.documentDate) problems.push('Indique a data do documento.');
    if (!document.currency) problems.push('Indique a moeda.');
    if (!document.sourceDocumentType) problems.push('Indique o tipo de documento anexado.');
    if ((document.grossAmount ?? 0) <= 0) problems.push('Indique o total do documento.');

    return problems;
}
