import { PaymentSourceDocumentDto, SavePaymentSourceDocumentDto } from '../types/paymentSourceDocument';
import { OcrDraft } from '../types';
import { OcrExtractionEnvelope } from '../types/ocrExtraction';
import { ClassificationConflictState, EMPTY_CONFLICT, OcrDocumentClassification } from './documentClassificationDecision';
import { normalizeDocumentType } from './sourceDocumentType';

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
export type PaymentDocumentEntryMode = 'PENDING_OCR' | 'REVIEW' | 'MANUAL';

/** Supplier details read off the document, used only to pre-fill supplier registration. */
export interface SupplierExtractionSnapshot {
    address: string | null;
    contactName: string | null;
    email: string | null;
    phone: string | null;
    bankIban: string | null;
    bankAccountNumber: string | null;
    bankSwift: string | null;
    paymentTerms: string | null;
}

export interface TemporaryPaymentDocument {
    tempId: string;

    /**
     * The number this document is called, issued once and never reissued.
     *
     * <p>Independent of array position: removing Documento 2 of three must leave Documento 1 and
     * Documento 3 exactly as they were. Renumbering would rewrite what the user already called
     * "Documento 3".</p>
     */
    localSequence: number;

    /**
     * The user has reviewed this document and accepted it into the request.
     *
     * <p>The hinge of the whole screen. Before it, the document is a draft being worked on and its
     * value is provisional; after it, the document is settled, collapses to a summary card and
     * counts towards the consolidated total. It is <b>not</b> a submission — it confirms one
     * document inside the client-side composition, nothing more.</p>
     */
    confirmed: boolean;

    /**
     * Which view the active document shows, and why.
     *
     * <ul>
     *   <li><b>PENDING_OCR</b> — created by "importar com OCR", not yet read. The document area is
     *   a blocking loading view (or a failure view); the editor is never rendered in this state, so
     *   an empty form cannot appear before the values arrive.</li>
     *   <li><b>REVIEW</b> — read successfully. The editor opens already populated.</li>
     *   <li><b>MANUAL</b> — the user is filling it in themselves, either by choosing "inserir
     *   manualmente" or by choosing it after a failed reading.</li>
     * </ul>
     *
     * <p>Kept as an explicit value rather than inferred from "is a request in flight": the gap
     * between creating the document and the request actually starting is a render in which nothing
     * is loading and nothing has been read, and inference would show the empty editor in it.</p>
     */
    entryMode: PaymentDocumentEntryMode;

    /** Set once the file is uploaded. Reused on retry so the same file is never uploaded twice. */
    attachmentId: string | null;
    attachmentFileName: string | null;

    supplierId: number | null;
    supplierNameSnapshot: string | null;
    supplierTaxIdSnapshot: string | null;
    /**
     * Supplier details the extraction read off the document — address, contact, bank, payment terms.
     *
     * <p>Transient and never persisted with the document: these describe the <b>supplier</b>, not
     * this invoice, and their home is the supplier's own record. They are kept only so that
     * registering an unknown supplier can pre-fill what the document already told us instead of
     * asking the user to retype it. Not part of {@link toCreatePayload}.</p>
     */
    supplierExtraction: SupplierExtractionSnapshot | null;

    /**
     * Set when the counterparty read off this document is an ALPLA legal entity.
     *
     * <p>The reading is not wrong when this happens — ALPLA really is named on the document — but it
     * is named as the <b>issuer</b>, and an ALPLA company can never be the entity a payment request
     * owes money to. Held as evidence about the document rather than as a supplier choice, because
     * there is no choice to record: the field stays empty and the document cannot be confirmed.</p>
     *
     * <p>Client-side only. It is never persisted, and never sent to the server — the server reaches
     * the same conclusion itself, from the same authoritative company rows.</p>
     */
    supplierInternalCompany: { id: number; name: string } | null;

    /**
     * A PROBABLE existing supplier the backend matcher found when the document's supplier could
     * not be resolved exactly (typically: same name, different NIF — a misread or changed fiscal
     * number). Surfaced so the user can review and USE the existing supplier instead of being
     * steered into creating a twin. Never auto-applied, never overwrites master data.
     */
    probableSupplier: { id: number; name: string; taxId: string | null } | null;

    /** The customer/billed-company NIF read off the document. Display-only evidence — compared
     *  against the selected company's registered NIF at review time, never persisted. */
    billedCompanyTaxId: string | null;

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
    /** The backend resolves the unit from its CODE, so it is carried alongside the id. */
    unitCode: string | null;
    unitPrice: number;
    discountAmount: number | null;
    ivaRateId: number | null;
    totalAmount: number;
    /** Catalog match, preserved so reconciliation sees the same item the legacy grid would. */
    itemCatalogId: number | null;
    itemCatalogCode: string | null;
    /** Set after the owning document is persisted, never before. */
    persistedId: string | null;
}

export function createTemporaryItem(seed: Partial<TemporaryPaymentItem> = {}): TemporaryPaymentItem {
    return {
        tempId: newTempId('item'),
        description: '',
        quantity: 1,
        unitId: null,
        unitCode: null,
        unitPrice: 0,
        discountAmount: null,
        ivaRateId: null,
        totalAmount: 0,
        itemCatalogId: null,
        itemCatalogCode: null,
        persistedId: null,
        ...seed
    };
}

/**
 * One line's total: (quantity × price − discount) + IVA on that net.
 *
 * <p>Rounded at each step, mirroring <c>useOcrProcessor.calculateItemTotal</c> so a document read by
 * OCR and the same document typed by hand arrive at the same number. A cent of drift between the two
 * paths would surface as a false "a soma dos itens não corresponde".</p>
 */
export function computeItemTotal(
    item: Pick<TemporaryPaymentItem, 'quantity' | 'unitPrice' | 'discountAmount'>,
    ivaPercent: number
): number {
    const gross = Math.round((item.quantity || 0) * (item.unitPrice || 0) * 100) / 100;
    const net = Math.max(0, gross - (item.discountAmount || 0));
    const iva = Math.round(net * (ivaPercent / 100) * 100) / 100;
    return Math.round((net + iva) * 100) / 100;
}

export function itemsTotalOf(items: TemporaryPaymentItem[]): number {
    return Math.round(items.reduce((s, i) => s + (i.totalAmount ?? 0), 0) * 100) / 100;
}

// ── Declared document totals and rounding-residual reconciliation (v2.229.10) ───────────────
//
// A supplier document's declared Net/Tax/Gross are documentary evidence. When the triplet is
// internally consistent, it is authoritative — line arithmetic exists to validate it, never to
// silently overwrite it. The cent-level gap between the declared gross and the VAT-inclusive
// line sum (the inevitable artifact of per-line rounding) is attributed deterministically to the
// LAST eligible line, so ONE monetary truth flows downstream:
// Σ(item totals) == document gross == group total == expected/paid amount.
//
// Mirrors the tested reference implementation `PaymentRoundingResidual` (backend Domain).

/** Strict internal-consistency bound for a declared triplet: cent arithmetic only — never the
 *  far looser 0.1% financial-integrity tolerance. */
export const DECLARED_TRIPLET_CONSISTENCY_TOLERANCE = 0.01;

/** Maximum residual attributable to rounding, per line. Each line's total is rounded at most to
 *  the cent, so per-line rounding can explain at most one cent per line. */
export const PER_LINE_RESIDUAL_CAP = 0.01;

/** The document-level totals the supplier actually declared, as read by the extraction. */
export interface DeclaredDocumentTotals {
    net: number | null;
    tax: number | null;
    gross: number | null;
}

export function readDeclaredTotals(raw?: OcrExtractionEnvelope | null): DeclaredDocumentTotals {
    const h = (raw?.integration?.headerSuggestions ?? {}) as Record<string, unknown>;
    return {
        net: numOf(h.netAmount),
        tax: numOf(h.taxAmount) ?? numOf(h.ivaAmount),
        gross: numOf(h.grandTotal) ?? numOf(h.totalAmount)
    };
}

/**
 * Whether the declared triplet may be trusted as documentary truth. Positive net and gross,
 * with net + tax = gross to the cent (a missing tax is derived as gross − net, so it is
 * consistent by construction). A triplet that fails this is NOT rescued by any wider tolerance —
 * it falls back to line-derived values and the ordinary mismatch handling.
 */
export function isConsistentDeclaredTriplet(t: DeclaredDocumentTotals): boolean {
    if (t.net == null || t.gross == null) return false;
    if (t.net <= 0 || t.gross <= 0) return false;
    const tax = t.tax ?? Math.round((t.gross - t.net) * 100) / 100;
    if (tax < 0) return false;
    return Math.abs(t.net + tax - t.gross) <= DECLARED_TRIPLET_CONSISTENCY_TOLERANCE + 1e-9;
}

/** Disclosure metadata for an applied residual — what the muted UI note reports. */
export interface RoundingAdjustment {
    /** Signed residual, e.g. +0.01 or −0.01. */
    amount: number;
    lineTempId: string;
    /** 1-based position in document order, for "linha N" wording. */
    lineNumber: number;
}

export interface RoundingAllocationResult {
    /** The lines with the residual embodied on the adjusted line. Input order preserved. */
    items: TemporaryPaymentItem[];
    /** Null when nothing was (or could be) adjusted. */
    adjustment: RoundingAdjustment | null;
}

/**
 * Reconciles canonical line totals against the document's declared gross (MODEL 2).
 *
 * <p>Pure, deterministic and idempotent: integer-cent arithmetic (no floating-point drift), and
 * reconciling an already-reconciled set yields residual 0 and returns the input untouched. Input
 * totals must be the canonical per-line values — the callers guarantee that, because the editor
 * recomputes a line's total from its components on every change.</p>
 *
 * <p>Only the selected line's <c>totalAmount</c> changes. Quantity, unit price, discount and tax
 * rate are extracted commercial components and are never altered. When the residual exceeds
 * <c>PER_LINE_RESIDUAL_CAP × eligible lines</c>, nothing is adjusted — a 100-AOA gap must never
 * be disguised as rounding, whatever the percentage tolerance would forgive.</p>
 */
export function allocateRoundingResidual(
    items: TemporaryPaymentItem[],
    declaredGross: number | null | undefined
): RoundingAllocationResult {
    if (!declaredGross || declaredGross <= 0 || items.length === 0) {
        return { items, adjustment: null };
    }

    const toCents = (v: number) => Math.round(v * 100);
    const residualCents = toCents(declaredGross) -
        items.reduce((s, i) => s + toCents(i.totalAmount ?? 0), 0);

    if (residualCents === 0) return { items, adjustment: null };

    const eligibleCount = items.filter(i => (i.totalAmount ?? 0) > 0).length;
    if (eligibleCount === 0 ||
        Math.abs(residualCents) > Math.round(PER_LINE_RESIDUAL_CAP * 100) * eligibleCount) {
        return { items, adjustment: null };
    }

    // Last eligible line in document order whose total survives the adjustment positive.
    for (let i = items.length - 1; i >= 0; i--) {
        const current = items[i].totalAmount ?? 0;
        if (current <= 0) continue;

        const adjusted = (toCents(current) + residualCents) / 100;
        if (adjusted <= 0) continue;

        const next = items.slice();
        next[i] = { ...items[i], totalAmount: adjusted };
        return {
            items: next,
            adjustment: {
                amount: residualCents / 100,
                lineTempId: items[i].tempId,
                lineNumber: i + 1
            }
        };
    }

    return { items, adjustment: null };
}

function numOf(value: unknown): number | null {
    const v = typeof value === 'object' && value !== null && 'value' in (value as any)
        ? (value as any).value : value;
    if (v == null || v === '') return null;
    const n = Number(v);
    return Number.isFinite(n) && n !== 0 ? n : null;
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
        localSequence: 1,
        confirmed: false,
        // Safe default: a document nobody declared as an OCR import is one the user is typing, and
        // showing them the editor is never the wrong answer for that.
        entryMode: 'MANUAL',
        attachmentId: null,
        attachmentFileName: null,
        supplierId: null,
        supplierNameSnapshot: null,
        supplierTaxIdSnapshot: null,
        supplierExtraction: null,
        supplierInternalCompany: null,
        probableSupplier: null,
        billedCompanyTaxId: null,
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
 *
 * <p><b>The plant is deliberately not copied.</b> Two invoices from the same supplier for Viana 1
 * and Viana 2 is the exact case this feature was built for, so carrying the first document's plant
 * over would pre-fill the wrong answer in the most common reason for adding a second document at
 * all. The user names it once per document, on purpose.</p>
 */
export function duplicateTemporaryBasics(source: TemporaryPaymentDocument): TemporaryPaymentDocument {
    return createTemporaryDocument({
        supplierId: source.supplierId,
        supplierNameSnapshot: source.supplierNameSnapshot,
        supplierTaxIdSnapshot: source.supplierTaxIdSnapshot,
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
        // Compared in canonical form: an OCR "Orçamento/Cotação" reading confirmed as
        // Factura Pró-forma is agreement, not a user override.
        classificationSource:
            suggestion && document.sourceDocumentType
                && normalizeDocumentType(suggestion) === normalizeDocumentType(document.sourceDocumentType)
                ? 'OCR_CONFIRMED' : 'USER_SELECTED',
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

/** The currency the request has committed to, so later documents default to it. */
export function temporaryEstablishedCurrency(documents: TemporaryPaymentDocument[]): string | null {
    return documents.find(d => !!d.currency)?.currency ?? null;
}

// ── Review-time candidate matching (v2.229.10 L4 flow) ──────────────────────────────────────

/** One existing commercial document the backend considers a candidate match. */
export interface SourceDocumentCandidate {
    /** RELATED_DOCUMENT | AMBIGUOUS_MATCH | STRONG_BUSINESS_DUPLICATE | SEMANTIC_DUPLICATE */
    classification: string;
    /** ALLOW | AMBIGUOUS | BLOCK — what persistence would decide. */
    verdict: string;
    reason?: string | null;
    matchingFields: string[];
    conflictingFields: string[];
    requestVisible: boolean;
    requestId?: string | null;
    requestNumber?: string | null;
    documentId?: string | null;
    sequenceNumber?: number | null;
    existing?: {
        supplierName?: string | null;
        supplierTaxId?: string | null;
        documentNumber?: string | null;
        documentDate?: string | null;
        currency?: string | null;
        grossAmount?: number | null;
    } | null;
}

export interface SourceDocumentCandidatesResult {
    normalizedDocumentNumber?: string | null;
    topClassification?: string | null;
    candidates: SourceDocumentCandidate[];
}

/** Portuguese labels for the stable field codes the backend reports. */
export const CANDIDATE_FIELD_LABELS: Record<string, string> = {
    DOCUMENT_NUMBER: 'Nº do documento',
    SUPPLIER: 'Fornecedor',
    SUPPLIER_NAME: 'Nome do fornecedor',
    SUPPLIER_NIF: 'NIF do fornecedor',
    DOCUMENT_DATE: 'Data do documento',
    CURRENCY: 'Moeda',
    GROSS_AMOUNT: 'Total',
    COMPANY: 'Empresa',
    CONTENT: 'Conteúdo (itens)'
};

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
        sequenceNumber: document.sequenceNumber ?? document.localSequence ?? displaySequence,
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
        supplierIsInternalCompany: !!document.supplierInternalCompany,
        supplierInternalCompanyName: document.supplierInternalCompany?.name ?? null,
        isVoided: false,
        voidReason: null,
        items: document.items.map((i, idx) => ({
            id: i.tempId,
            lineNumber: idx + 1,
            description: i.description,
            quantity: i.quantity,
            unitId: i.unitId,
            unitCode: i.unitCode,
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
    if (document.supplierInternalCompany) {
        problems.push(
            'A empresa identificada como emitente pertence à ALPLA e não pode ser utilizada como ' +
            'fornecedor em um pedido de pagamento. Verifique se o documento selecionado é o correto.');
    } else if (!document.supplierId) {
        problems.push('Indique o fornecedor.');
    }
    if (!document.plantId) problems.push('Indique a planta.');
    if (!document.documentNumber?.trim()) problems.push('Indique o número do documento.');
    if (!document.documentDate) problems.push('Indique a data do documento.');
    if (!document.dueDate) problems.push('Informe a data de vencimento do documento.');
    if (!document.currency) problems.push('Indique a moeda.');
    if (!document.sourceDocumentType) problems.push('Indique o tipo de documento anexado.');
    if ((document.grossAmount ?? 0) <= 0) problems.push('Indique o total do documento.');

    return problems;
}

// ── OCR extraction, merged into a temporary document ─────────────────────────────────────────

/** The subset of the extraction response a source-document card can use. */
export interface ExtractedDocumentFields {
    supplierId: number | null;
    supplierName: string | null;
    supplierTaxId: string | null;
    documentNumber: string | null;
    documentDate: string | null;
    dueDate: string | null;
    currency: string | null;
    netAmount: number | null;
    taxAmount: number | null;
    grossAmount: number | null;
    classification: OcrDocumentClassification | null;
    /** The document's own lines. Empty when the reading produced none. */
    items: TemporaryPaymentItem[];
    /** Supplier details for pre-filling registration. Null when nothing was read. */
    supplierExtraction: SupplierExtractionSnapshot | null;
    /** The ALPLA legal entity the reading resolved to, as decided by the SERVER's match. */
    internalCompany: { id: number; name: string } | null;
    /** Probable existing supplier (backend DuplicateSuspected candidate). Never auto-applied. */
    probableSupplier: { id: number; name: string; taxId: string | null } | null;
    /** Customer/billed-company NIF read off the document. Display-only evidence. */
    billedCompanyTaxId: string | null;
}

/**
 * Reads an {@link OcrDraft} — the shape <c>useOcrProcessor.mapOcrResultToDraft</c> produces.
 *
 * <p>This is the reuse that matters. That mapper is where unit aliases are resolved, IVA rates are
 * matched, the discount column is cross-validated against the document's own line totals and the
 * supplier is matched <b>by the backend</b> rather than guessed from a paginated client search. All
 * of it was written for the single-document editor and all of it applies unchanged to a document in
 * a collection; reimplementing any of it here would be a second, worse copy.</p>
 */
export function fromOcrDraft(
    draft: OcrDraft,
    /** The raw envelope, consulted only to tell "read as EUR" from "not read at all". */
    raw?: OcrExtractionEnvelope | null
): ExtractedDocumentFields {
    const items: TemporaryPaymentItem[] = (draft.items ?? []).map(i => createTemporaryItem({
        description: i.description ?? '',
        quantity: i.quantity ?? 0,
        unitId: i.unitId ?? null,
        unitCode: i.unit ?? null,
        unitPrice: i.unitPrice ?? 0,
        discountAmount: i.discountAmount ?? null,
        ivaRateId: i.ivaRateId ?? null,
        totalAmount: i.totalPrice ?? 0,
        itemCatalogId: i.itemCatalogId ?? null,
        itemCatalogCode: i.itemCatalogCode ?? null
    }));

    // v2.229.10 monetary reconciliation: the DECLARED document totals are authoritative when the
    // triplet is internally consistent — line arithmetic validates them, it does not overwrite
    // them. Only when the document declared nothing (or an inconsistent triplet) are net and IVA
    // reconstructed from the lines, which is per-line-rounded and can legitimately differ from
    // the supplier's own document-level arithmetic by a cent.
    const declared = readDeclaredTotals(raw);
    const declaredIsAuthoritative = isConsistentDeclaredTriplet(declared);

    let gross: number | null;
    let net: number | null;
    let tax: number | null;

    if (declaredIsAuthoritative) {
        gross = declared.gross;
        net = declared.net;
        tax = declared.tax ?? Math.round((declared.gross! - declared.net!) * 100) / 100;
    } else {
        gross = draft.totalAmount > 0 ? draft.totalAmount : null;
        net = items.length > 0
            ? Math.round(items.reduce(
                (s, i) => s + Math.max(0, (i.quantity * i.unitPrice) - (i.discountAmount ?? 0)), 0) * 100) / 100
            : null;
        tax = gross != null && net != null ? Math.round((gross - net) * 100) / 100 : null;
    }

    // The legacy mapper falls back to 'EUR' when the document names no currency
    // (`currencyCode || currency || 'EUR'`), which is harmless in a form the user is already
    // filling in but wrong for a document card: it presents an invented currency as an extracted
    // one. So the currency is taken only when the envelope actually carried one — and when it did,
    // the DRAFT's value is used, because that is the one with AKZ→AOA alias resolution applied.
    const header = raw?.integration?.headerSuggestions;
    const currencyWasRead = raw === undefined
        || !!text(header?.currencyCode?.value) || !!text(header?.currency?.value);

    return {
        supplierId: draft.supplierId ?? null,
        supplierName: draft.supplierNameSnapshot?.trim() || null,
        supplierTaxId: draft.supplierTaxId?.trim() || null,
        documentNumber: draft.documentNumber?.trim() || null,
        documentDate: draft.documentDate?.substring(0, 10) || null,
        dueDate: draft.dueDate?.substring(0, 10) || null,
        currency: currencyWasRead ? (draft.currency?.toUpperCase() || null) : null,
        netAmount: net,
        taxAmount: tax != null && tax > 0 ? tax : null,
        grossAmount: gross,
        classification: (draft.documentClassification as OcrDocumentClassification | null) ?? null,
        items,
        // The authoritative supplier match already answered this: `MatchAsync` resolves the
        // extracted name/NIF against the Companies table before it looks at suppliers at all. The
        // client only reads the verdict — it does not carry its own copy of who ALPLA is.
        internalCompany: draft.supplierMatch?.status === 'InternalCompanyTaxId' &&
                         draft.supplierMatch?.internalCompany
            ? {
                id: draft.supplierMatch.internalCompany.id,
                name: draft.supplierMatch.internalCompany.name
            }
            : null,
        // The matcher's DuplicateSuspected candidates used to be silently discarded here — which
        // is exactly how "CONSULTIT with a misread NIF" became "criar fornecedor". The first
        // candidate is carried as a PROBABLE supplier for the user to review; nothing is selected
        // or created automatically.
        probableSupplier: draft.supplierMatch?.status === 'DuplicateSuspected' &&
                          Array.isArray(draft.supplierMatch?.candidates) &&
                          draft.supplierMatch.candidates.length > 0
            ? {
                id: draft.supplierMatch.candidates[0].id,
                name: draft.supplierMatch.candidates[0].name,
                taxId: draft.supplierMatch.candidates[0].taxId ?? null
            }
            : null,
        billedCompanyTaxId: text(header?.billedCompanyTaxId?.value) ?? null,
        // Only what the document actually carried — an absent field stays null and the registration
        // form shows it empty rather than inventing a value.
        supplierExtraction: {
            address: draft.supplierAddress?.trim() || null,
            contactName: draft.supplierContactName?.trim() || null,
            email: draft.supplierEmail?.trim() || null,
            phone: draft.supplierPhone?.trim() || null,
            bankIban: draft.supplierBankIban?.trim() || null,
            bankAccountNumber: draft.supplierBankAccountNumber?.trim() || null,
            bankSwift: draft.supplierBankSwift?.trim() || null,
            paymentTerms: draft.supplierPaymentTerms?.trim() || null
        }
    };
}

function text(value: unknown): string | null {
    if (value == null) return null;
    const v = typeof value === 'object' && value !== null && 'value' in (value as any)
        ? (value as any).value : value;
    if (v == null) return null;
    const s = String(v).trim();
    return s.length > 0 ? s : null;
}

function num(value: unknown): number | null {
    const v = typeof value === 'object' && value !== null && 'value' in (value as any)
        ? (value as any).value : value;
    if (v == null || v === '') return null;
    const n = Number(v);
    return Number.isFinite(n) && n !== 0 ? n : null;
}

/**
 * Reads the legacy extraction envelope produced by <c>ExtractionMapper.MapToLegacyOcrResult</c>.
 *
 * <p>Tolerant by design: a provider that omits a block must degrade to "not read", never to a
 * thrown error that would leave the card stuck mid-upload.</p>
 */
export function extractDocumentFields(
    ocrResult: OcrExtractionEnvelope | null | undefined
): ExtractedDocumentFields {
    const h = (ocrResult?.integration?.headerSuggestions ?? {}) as Record<string, unknown>;

    const gross = num(h.totalAmount);
    const tax = num(h.taxAmount) ?? num(h.ivaAmount);
    const net = num(h.netAmount) ?? (gross != null && tax != null ? gross - tax : null);

    const rawDate = text(h.date);

    return {
        supplierId: null,
        items: [],
        supplierExtraction: null,
        // This path never consulted the supplier matcher, so it has nothing to report. The server
        // still refuses an internal supplier at persistence and at submission.
        internalCompany: null,
        probableSupplier: null,
        billedCompanyTaxId: text(h.billedCompanyTaxId) ?? null,
        supplierName: text(h.supplierName),
        supplierTaxId: text(h.supplierTaxId),
        documentNumber: text(h.documentNumber),
        documentDate: rawDate ? rawDate.substring(0, 10) : null,
        dueDate: text(h.dueDate)?.substring(0, 10) ?? null,
        currency: text(h.currencyCode)?.toUpperCase() ?? null,
        netAmount: net,
        taxAmount: tax,
        grossAmount: gross,
        // The classification block, carried through verbatim with its evidence. Never applied to
        // the selection — the user confirms or corrects it.
        classification: h.documentClassification ?? null
    };
}

export interface ExtractionDiscrepancy {
    field: string;
    label: string;
    userValue: string;
    extractedValue: string;
}

export interface MergeResult {
    document: TemporaryPaymentDocument;
    /** Fields the user had already filled differently. Reported, never overwritten. */
    discrepancies: ExtractionDiscrepancy[];
}

/**
 * Folds an extraction into a document.
 *
 * <p>The rule, matching the single-document flow: <b>an empty field may be filled, a field the user
 * has already set is left alone</b>, and a disagreement is reported rather than silently applied.
 * This matters on a re-run — the user may have corrected a misread number by hand, and a retry that
 * quietly reinstated the misreading would be worse than no retry at all.</p>
 */
export function mergeExtraction(
    document: TemporaryPaymentDocument,
    extracted: ExtractedDocumentFields,
    supplierLookup?: (name: string, taxId: string | null) => { id: number; name: string; taxId?: string | null } | null
): MergeResult {
    const discrepancies: ExtractionDiscrepancy[] = [];

    const take = <T,>(current: T | null, incoming: T | null, field: string, label: string): T | null => {
        if (incoming == null) return current;
        if (current == null || current === '') return incoming;
        if (String(current) !== String(incoming)) {
            discrepancies.push({
                field, label,
                userValue: String(current),
                extractedValue: String(incoming)
            });
        }
        return current;   // the user's value always survives
    };

    const supplier = extracted.supplierName && supplierLookup
        ? supplierLookup(extracted.supplierName, extracted.supplierTaxId)
        : null;

    // Lines the user has already entered are never replaced. On a re-read they would otherwise be
    // silently swapped for the reading the user had just finished correcting.
    const keepExistingItems = document.items.length > 0;
    if (keepExistingItems && extracted.items.length > 0) {
        discrepancies.push({
            field: 'items',
            label: 'Itens',
            userValue: `${document.items.length} linha(s) introduzida(s)`,
            extractedValue: `${extracted.items.length} linha(s) lida(s), não aplicadas`
        });
    }

    return {
        document: {
            ...document,
            items: keepExistingItems ? document.items : extracted.items,
            supplierId: document.supplierId ?? extracted.supplierId ?? supplier?.id ?? null,
            supplierNameSnapshot: document.supplierNameSnapshot ?? supplier?.name ?? extracted.supplierName,
            supplierTaxIdSnapshot: document.supplierTaxIdSnapshot ?? extracted.supplierTaxId,
            supplierExtraction: document.supplierExtraction ?? extracted.supplierExtraction,
            // Always replaces: it describes the FILE that was just read, not anything the user
            // typed, and a stale verdict from a previous file must not survive a re-read.
            supplierInternalCompany: extracted.internalCompany,
            probableSupplier: extracted.probableSupplier,
            billedCompanyTaxId: extracted.billedCompanyTaxId,
            documentNumber: take(document.documentNumber, extracted.documentNumber, 'documentNumber', 'Nº do documento'),
            documentDate: take(document.documentDate, extracted.documentDate, 'documentDate', 'Data do documento'),
            dueDate: document.dueDate ?? extracted.dueDate,
            currency: take(document.currency, extracted.currency, 'currency', 'Moeda'),
            netAmount: take(document.netAmount, extracted.netAmount, 'netAmount', 'Valor líquido'),
            taxAmount: take(document.taxAmount, extracted.taxAmount, 'taxAmount', 'IVA'),
            grossAmount: take(document.grossAmount, extracted.grossAmount, 'grossAmount', 'Total'),
            // The reading itself always replaces: it describes the FILE, not the user's typing, and
            // this only ever runs for the file currently attached to this document.
            classification: extracted.classification ?? document.classification,
            error: null
        },
        discrepancies
    };
}
