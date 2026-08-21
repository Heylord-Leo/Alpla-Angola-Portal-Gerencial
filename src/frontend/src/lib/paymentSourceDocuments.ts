import {
    PaymentSourceDocumentDto,
    PaymentSourceDocumentsSummaryDto,
    SavePaymentSourceDocumentDto
} from '../types/paymentSourceDocument';
import { documentTypeLabel, normalizeDocumentType } from './sourceDocumentType';
import { evaluateClassificationConflict, OcrDocumentClassification } from './documentClassificationDecision';

/**
 * Pure derivations for the PAYMENT source-document collection.
 *
 * <p>Everything here is a function of its arguments — no React, no fetch. The card component stays
 * a renderer, and these rules can be reasoned about (and, if a frontend test runner is ever added,
 * asserted) without a browser.</p>
 */

// ── Card status ─────────────────────────────────────────────────────────────────────────────

export type DocumentCardStatus = 'READY' | 'INCOMPLETE' | 'OCR_PENDING' | 'CLASSIFICATION_CONFLICT' | 'VOIDED';

export interface DocumentCardStatusInfo {
    status: DocumentCardStatus;
    label: string;
    severity: 'success' | 'warning' | 'error' | 'muted';
}

/**
 * The one-word verdict shown on a collapsed card.
 *
 * <p>Ordered by what the user must deal with first: a document still being read cannot be judged,
 * an unresolved contradiction outranks a missing field, and only a document with nothing outstanding
 * is "Pronto".</p>
 */
export function deriveCardStatus(
    document: PaymentSourceDocumentDto,
    ocrProcessing: boolean,
    classification: OcrDocumentClassification | null
): DocumentCardStatusInfo {
    if (document.isVoided) {
        return { status: 'VOIDED', label: 'Anulado', severity: 'muted' };
    }

    if (ocrProcessing) {
        return { status: 'OCR_PENDING', label: 'OCR pendente', severity: 'warning' };
    }

    const conflict = evaluateClassificationConflict(document.sourceDocumentType, classification);
    const conflictUnresolved =
        conflict.hasConflict && !document.classificationConflictAcknowledged;

    if (conflictUnresolved) {
        return { status: 'CLASSIFICATION_CONFLICT', label: 'Conflito de classificação', severity: 'error' };
    }

    if (!document.isValid) {
        return { status: 'INCOMPLETE', label: 'Incompleto', severity: 'warning' };
    }

    return { status: 'READY', label: 'Pronto', severity: 'success' };
}

/** A one-line identification for the collapsed header, tolerant of everything still being blank. */
export function describeDocument(document: PaymentSourceDocumentDto): string {
    const parts = [
        document.supplierNameSnapshot,
        document.documentNumber,
        document.plantName,
        document.sourceDocumentType ? documentTypeLabel(document.sourceDocumentType) : null
    ].filter((p): p is string => !!p && p.trim().length > 0);

    return parts.length > 0 ? parts.join(' · ') : 'Novo documento (sem dados)';
}

// ── Totals ──────────────────────────────────────────────────────────────────────────────────

export interface PaymentDocumentsTotals {
    activeCount: number;
    net: number;
    tax: number;
    gross: number;
    currency: string | null;
}

/**
 * The request-level summary.
 *
 * <p>Optimistic only. The authoritative total is whatever the backend returned in the last summary,
 * because the backend is what decides which documents are active — this exists so the number moves
 * as the user types rather than jumping after each save.</p>
 */
export function computeTotals(documents: PaymentSourceDocumentDto[]): PaymentDocumentsTotals {
    const active = documents.filter(d => !d.isVoided);

    return {
        activeCount: active.length,
        net: active.reduce((sum, d) => sum + (d.netAmount ?? 0), 0),
        tax: active.reduce((sum, d) => sum + (d.taxAmount ?? 0), 0),
        gross: active.reduce((sum, d) => sum + (d.grossAmount ?? 0), 0),
        currency: active.find(d => !!d.currency)?.currency ?? null
    };
}

// ── Currency ────────────────────────────────────────────────────────────────────────────────

/**
 * The currency a new document should default to.
 *
 * <p>One payment request supports one currency, so pre-filling it is not a convenience — it is how
 * the user avoids walking into a blocking error they had no way to anticipate.</p>
 */
export function establishedCurrency(documents: PaymentSourceDocumentDto[]): string | null {
    return documents.find(d => !d.isVoided && !!d.currency)?.currency ?? null;
}

/** The refusal shown when a document is given a currency the request has already committed to. */
export function currencyConflictMessage(established: string, attempted: string): string {
    return `Este pedido está em ${established}. Um pedido de pagamento suporta apenas uma moeda, ` +
           `pelo que ${attempted} não pode ser usado noutro documento. ` +
           `Crie um pedido separado para documentos noutra moeda.`;
}

// ── Validation summary ──────────────────────────────────────────────────────────────────────

export interface ValidationIssue {
    /** Null for request-level problems (no documents, mixed currencies). */
    documentId: string | null;
    sequenceNumber: number | null;
    documentNumber: string | null;
    message: string;
    /** What the user reads in the summary list. */
    display: string;
}

/**
 * Every reason the request cannot be submitted, flattened so the user sees the whole list at once
 * instead of discovering it one refusal at a time.
 */
export function collectValidationIssues(
    summary: PaymentSourceDocumentsSummaryDto | null
): ValidationIssue[] {
    if (!summary) return [];

    const issues: ValidationIssue[] = summary.requestValidationMessages.map(message => ({
        documentId: null,
        sequenceNumber: null,
        documentNumber: null,
        message,
        display: message
    }));

    for (const document of summary.documents) {
        for (const message of document.validationMessages) {
            const label = document.documentNumber
                ? `Documento ${document.sequenceNumber} (${document.documentNumber})`
                : `Documento ${document.sequenceNumber}`;

            issues.push({
                documentId: document.id,
                sequenceNumber: document.sequenceNumber,
                documentNumber: document.documentNumber ?? null,
                message,
                display: `${label}: ${message}`
            });
        }
    }

    return issues;
}

// ── "Duplicar dados básicos" ────────────────────────────────────────────────────────────────

/**
 * The subset of a document that may be copied into a new one.
 *
 * <p>Deliberately narrow. Supplier, currency and plant are shared context that a second invoice
 * from the same supplier genuinely repeats. Everything that makes a document a <em>distinct
 * document</em> — the file, its number and series, its dates, its values, its OCR reading, its
 * classification decision and its items — is never copied. Copying an OCR reading would attach one
 * file's evidence to another file, which is the exact mistake the classification workflow exists to
 * prevent.</p>
 */
export function duplicateBasicData(source: PaymentSourceDocumentDto): SavePaymentSourceDocumentDto {
    return {
        supplierId: source.supplierId ?? null,
        supplierTaxIdSnapshot: source.supplierTaxIdSnapshot ?? null,
        plantId: source.plantId ?? null,
        currency: source.currency ?? null
        // Everything else is deliberately absent — see the note above.
    };
}

// ── Save payload ────────────────────────────────────────────────────────────────────────────

/** Maps a document plus its live classification decision onto the wire shape. */
export function toSavePayload(
    document: PaymentSourceDocumentDto,
    classification: OcrDocumentClassification | null,
    conflictAcknowledged: boolean,
    justification: string
): SavePaymentSourceDocumentDto {
    const suggestion = classification?.suggestedType ?? null;
    const selected = document.sourceDocumentType ?? null;

    return {
        attachmentId: document.attachmentId,
        supplierId: document.supplierId ?? null,
        supplierTaxIdSnapshot: document.supplierTaxIdSnapshot ?? null,
        plantId: document.plantId ?? null,
        sourceDocumentType: selected,
        documentNumber: document.documentNumber ?? null,
        documentSeries: document.documentSeries ?? null,
        documentDate: document.documentDate ?? null,
        dueDate: document.dueDate ?? null,
        currency: document.currency ?? null,
        netAmount: document.netAmount ?? null,
        taxAmount: document.taxAmount ?? null,
        grossAmount: document.grossAmount ?? null,
        ocrSuggestion: suggestion,
        ocrConfidence: classification?.confidence ?? null,
        ocrTitleFound: classification?.titleFound ?? null,
        ocrEvidenceJson: classification ? JSON.stringify(classification) : null,
        ocrConflictingEvidenceJson: classification?.conflictingEvidence?.length
            ? JSON.stringify(classification.conflictingEvidence) : null,
        // Compared in canonical form: an OCR "Orçamento/Cotação" reading confirmed as
        // Factura Pró-forma is agreement, not a user override.
        classificationSource: suggestion && selected
            && normalizeDocumentType(suggestion) === normalizeDocumentType(selected)
            ? 'OCR_CONFIRMED' : 'USER_SELECTED',
        classificationSuggestionSource: classification
            ? (classification.isFallback ? 'FALLBACK' : 'OCR')
            : null,
        classificationConflictAcknowledged: conflictAcknowledged,
        classificationJustification: justification.trim() || null,
        rowVersion: document.rowVersion ?? null
    };
}

/** Rehydrates a stored evidence blob, tolerating anything that is not the shape we wrote. */
export function parseStoredClassification(
    document: PaymentSourceDocumentDto
): OcrDocumentClassification | null {
    if (!document.ocrSuggestion) return null;

    let parsed: Partial<OcrDocumentClassification> = {};
    if (document.ocrEvidenceJson) {
        try {
            const raw = JSON.parse(document.ocrEvidenceJson);
            if (raw && typeof raw === 'object') parsed = raw as Partial<OcrDocumentClassification>;
        } catch {
            parsed = {};
        }
    }

    return {
        ...parsed,
        suggestedType: document.ocrSuggestion,
        confidence: document.ocrConfidence ?? null,
        titleFound: document.ocrTitleFound ?? parsed.titleFound ?? null,
        isFallback: document.classificationSuggestionSource === 'FALLBACK'
    };
}

// ── Routing plant vs document plants ────────────────────────────────────────────────────────

export interface PlantMismatch {
    sequenceNumber: number;
    documentNumber: string | null;
    plantId: number | null;
    plantName: string | null;
}

/**
 * Active documents whose plant differs from the request's routing plant.
 *
 * <p>The two plants answer different questions and are <b>allowed</b> to differ:
 * <c>Request.PlantId</c> decides who approves the request and what the requester is authorized to
 * raise; <c>PaymentSourceDocument.PlantId</c> decides how the payment is grouped and which
 * obligations follow it. A request routed through Viana 1 that pays an invoice issued to Viana 2 is
 * ordinary, not an error.</p>
 *
 * <p>So this is disclosure, never a rule: it exists so nobody has to discover the split later, when
 * the groups come out addressed to a plant the request header never mentioned. Voided documents are
 * excluded — a document that was withdrawn explains nothing about where the payment will land.</p>
 */
export function plantMismatches(
    requestPlantId: number | null | undefined,
    documents: PaymentSourceDocumentDto[],
    plantName?: (plantId: number) => string | null
): PlantMismatch[] {
    // With no routing plant there is nothing to compare against, and the field's own required-field
    // validation is the honest thing to show instead.
    if (requestPlantId == null) return [];

    return documents
        .filter(d => !d.isVoided && d.plantId != null && d.plantId !== requestPlantId)
        .map(d => ({
            sequenceNumber: d.sequenceNumber,
            documentNumber: d.documentNumber ?? null,
            plantId: d.plantId ?? null,
            plantName: d.plantName ?? (d.plantId != null ? plantName?.(d.plantId) ?? null : null)
        }))
        .sort((a, b) => a.sequenceNumber - b.sequenceNumber);
}
