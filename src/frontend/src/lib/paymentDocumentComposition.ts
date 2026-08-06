import { TemporaryPaymentDocument, itemsTotalOf } from './paymentRequestCreation';
import {
    CONFLICT_JUSTIFICATION_MIN_LENGTH,
    evaluateClassificationConflict
} from './documentClassificationDecision';

/**
 * The progressive composition of a PAYMENT request's source documents.
 *
 * <p>A payment request is built one document at a time: import or type it, review what was read,
 * <b>confirm</b> it, and only then move on. Confirmation is the hinge — before it a document is a
 * draft being worked on, after it a settled part of the request whose value counts towards the
 * total.</p>
 *
 * <p>Everything here is pure. The screen renders a state; it does not decide one. That matters most
 * for confirmation: whether a document may be accepted is a question with one answer, and the answer
 * must not depend on which component happens to ask.</p>
 */

// ── Per-document state ──────────────────────────────────────────────────────────────────────

export type DocumentLifecycle =
    /** The file is being read. Nothing about the document can be judged yet. */
    | 'EXTRACTING'
    /** Open in the editor, not yet confirmed. */
    | 'EDITING'
    /** Was confirmed, then something invalidated it — a replaced file, a cleared field. */
    | 'REVIEW_REQUIRED'
    | 'CONFIRMED'
    /** The reading failed, or the document failed to save. */
    | 'ERROR';

export interface DocumentLifecycleInfo {
    state: DocumentLifecycle;
    label: string;
    severity: 'success' | 'warning' | 'error' | 'muted';
}

/**
 * What this one document is doing, in the order the user must deal with it.
 *
 * <p>A failure outranks everything — it is the only state the user cannot resolve by continuing.
 * A document still being read cannot be called incomplete, because nothing has had a chance to fill
 * it in yet.</p>
 */
export function documentLifecycle(
    document: TemporaryPaymentDocument,
    isExtracting: boolean,
    hasReadError: boolean,
    isActive: boolean
): DocumentLifecycleInfo {
    if (document.error || hasReadError) {
        return { state: 'ERROR', label: 'Erro', severity: 'error' };
    }

    if (isExtracting) {
        return { state: 'EXTRACTING', label: 'Em extração', severity: 'muted' };
    }

    if (isActive) {
        return { state: 'EDITING', label: 'Em revisão', severity: 'warning' };
    }

    if (document.confirmed && confirmationBlockers(document, false).length === 0) {
        return { state: 'CONFIRMED', label: 'Confirmado', severity: 'success' };
    }

    // Confirmed once, but no longer satisfying the rules — a replaced attachment, or a field the
    // user emptied afterwards. It must never keep showing "Confirmado".
    return { state: 'REVIEW_REQUIRED', label: 'Com pendências', severity: 'warning' };
}

// ── Confirmation ────────────────────────────────────────────────────────────────────────────

/**
 * Tolerance between the sum of a document's items and its stated total.
 *
 * <p>Mirrors <c>RequestConstants.FinancialIntegrity.CalculateTolerance</c>: the greater of one
 * currency unit or 0.1%. Rounding noise between a browser and .NET must not block a document; a real
 * disagreement must.</p>
 */
export function itemsTolerance(amount: number): number {
    return Math.max(1, Math.abs(amount) * 0.001);
}

/**
 * Everything standing between this document and "Confirmar e adicionar documento".
 *
 * <p>Deliberately the same list the backend enforces at submission
 * (<c>PaymentSourceDocumentValidator</c>), stated here so the user learns about a missing field
 * while looking at the document rather than at the end of the request. The server stays
 * authoritative — this exists to make the screen honest, not to replace the check.</p>
 */
export function confirmationBlockers(
    document: TemporaryPaymentDocument,
    isExtracting: boolean
): string[] {
    if (isExtracting) return ['A leitura do documento ainda está em curso.'];

    const problems: string[] = [];

    if (!document.attachmentId) problems.push('Anexe o ficheiro do documento.');
    if (!document.supplierId) problems.push('Indique o fornecedor.');
    if (!document.plantId) problems.push('Indique a planta.');
    if (!document.documentNumber?.trim()) problems.push('Indique o número do documento.');
    if (!document.documentDate) problems.push('Indique a data do documento.');
    // Every line item of a PAYMENT request must carry a due date, and an item takes it from its
    // document. Without this the document confirms happily and then fails during persistence, after
    // the request has already been created — which is exactly the reported defect.
    if (!document.dueDate) problems.push('Informe a data de vencimento do documento.');
    if (!document.currency) problems.push('Indique a moeda.');
    if (!document.sourceDocumentType) problems.push('Indique o tipo de documento anexado.');

    const gross = document.grossAmount ?? 0;
    if (gross <= 0) problems.push('Indique o total do documento.');

    if (document.items.length === 0) {
        problems.push('Adicione pelo menos um item a este documento.');
    } else if (gross > 0) {
        const total = itemsTotalOf(document.items);
        if (Math.abs(total - gross) > itemsTolerance(gross)) {
            problems.push(
                `A soma dos itens (${total.toLocaleString('pt-PT', { minimumFractionDigits: 2 })}) ` +
                `não corresponde ao total do documento ` +
                `(${gross.toLocaleString('pt-PT', { minimumFractionDigits: 2 })}).`);
        }
    }

    // A classification that contradicts the reading must have been answered, and justified where
    // the answer understates what the evidence says the document is.
    if (document.sourceDocumentType) {
        const conflict = evaluateClassificationConflict(
            document.sourceDocumentType, document.classification);

        if (conflict.hasConflict && !document.conflict.acknowledged) {
            problems.push('Confirme a classificação: ela difere da leitura do documento.');
        } else if (conflict.isHighRisk &&
                   document.conflict.justification.trim().length < CONFLICT_JUSTIFICATION_MIN_LENGTH) {
            problems.push(
                `Justifique a classificação (mínimo ${CONFLICT_JUSTIFICATION_MIN_LENGTH} caracteres).`);
        }
    }

    return problems;
}

export function canConfirmDocument(
    document: TemporaryPaymentDocument,
    isExtracting: boolean
): boolean {
    return confirmationBlockers(document, isExtracting).length === 0;
}

/**
 * The field a blocker is about, so the screen can take the user to it.
 *
 * <p>"1 pendência" is not help. The first unresolved field is focused and outlined, because the
 * point of blocking confirmation is to get the document finished, not to record that it isn't.</p>
 */
export function blockerField(message: string): string | null {
    if (message.startsWith('Anexe')) return 'attachment';
    if (message.includes('fornecedor')) return 'supplierId';
    if (message.includes('planta')) return 'plantId';
    if (message.includes('número do documento')) return 'documentNumber';
    if (message.includes('data de vencimento')) return 'dueDate';
    if (message.includes('data do documento')) return 'documentDate';
    if (message.includes('moeda')) return 'currency';
    if (message.includes('tipo de documento')) return 'sourceDocumentType';
    if (message.includes('total do documento')) return 'grossAmount';
    if (message.includes('itens')) return 'items';
    return null;
}

/**
 * Confirmed documents that no longer satisfy the rules.
 *
 * <p>Checked before a request is created, not after. A document can be confirmed and then edited
 * back into an invalid state, and discovering that during persistence means a request already
 * exists describing documents that could not be saved.</p>
 */
export function invalidConfirmedDocuments(
    documents: TemporaryPaymentDocument[]
): Array<{ document: TemporaryPaymentDocument; blockers: string[] }> {
    return documents
        .filter(d => d.confirmed)
        .map(d => ({ document: d, blockers: confirmationBlockers(d, false) }))
        .filter(x => x.blockers.length > 0);
}

// ── Sequence ────────────────────────────────────────────────────────────────────────────────

/**
 * The number the next document gets.
 *
 * <p>Always one past the highest ever issued, never a count. Removing Documento 2 of three leaves
 * Documento 1 and Documento 3 — renumbering would silently rewrite what the user, and any note they
 * wrote, already called "Documento 3".</p>
 */
export function nextLocalSequence(documents: TemporaryPaymentDocument[]): number {
    return documents.reduce((max, d) => Math.max(max, d.localSequence ?? 0), 0) + 1;
}

// ── Totals ──────────────────────────────────────────────────────────────────────────────────

export interface CompositionTotals {
    count: number;
    net: number;
    tax: number;
    gross: number;
    currency: string | null;
}

/**
 * The consolidated summary.
 *
 * <p><b>Confirmed documents only.</b> A document still being edited has values that change with
 * every keystroke; folding those into the request total would show the user a number that was never
 * agreed to. The one being edited is reported separately as provisional.</p>
 */
export function confirmedTotals(documents: TemporaryPaymentDocument[]): CompositionTotals {
    const confirmed = documents.filter(d => d.confirmed);

    return {
        count: confirmed.length,
        net: confirmed.reduce((s, d) => s + (d.netAmount ?? 0), 0),
        tax: confirmed.reduce((s, d) => s + (d.taxAmount ?? 0), 0),
        gross: confirmed.reduce((s, d) => s + (d.grossAmount ?? 0), 0),
        currency: confirmed.find(d => !!d.currency)?.currency ?? null
    };
}

export function confirmedDocuments(documents: TemporaryPaymentDocument[]): TemporaryPaymentDocument[] {
    return documents.filter(d => d.confirmed);
}

/**
 * What happens to the document still open when the user saves a draft.
 *
 * <p>Three honest outcomes, never a silent one: it is complete enough to keep, it is incomplete but
 * has a file so the server can hold it, or it has nothing to hold on to and the user has to decide.
 * §15 forbids losing it quietly, and dropping an invoice the user just typed is exactly the kind of
 * loss nobody notices until the approval is already wrong.</p>
 */
export type ActiveDraftDisposition = 'NONE' | 'PERSISTABLE' | 'INCOMPLETE' | 'UNPERSISTABLE';

export function activeDraftDisposition(
    active: TemporaryPaymentDocument | null | undefined
): ActiveDraftDisposition {
    if (!active) return 'NONE';
    if (!active.attachmentId) return 'UNPERSISTABLE';
    return confirmationBlockers(active, false).length === 0 ? 'PERSISTABLE' : 'INCOMPLETE';
}
