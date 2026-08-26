import type { PaymentSourceDocumentDto } from '../types/paymentSourceDocument';
import type { TemporaryPaymentItem } from './paymentRequestCreation';

/**
 * Draft recovery for a PERSISTED payment source document.
 *
 * <p>A persisted document with zero linked items is structurally unsubmittable, yet the review
 * screen never mounted an item editor for it (the collection's <c>renderItems</c> was never
 * supplied), so the first item could not be added — the draft was unrecoverable. These pure rules
 * decide when the "Adicionar item" affordance may be offered, and build the canonical line-item
 * payload that links the new item back to its document.</p>
 *
 * <p>No React, no fetch — a function of its arguments, so the recovery gate can be reasoned about
 * and asserted without a browser.</p>
 */

/**
 * Why this persisted document is not yet ready to RECEIVE its first item.
 *
 * <p>Deliberately excludes the "needs at least one item" rule: gating item entry on already having
 * an item would be circular and is exactly what left REQ-276 stranded. It mirrors the document-level
 * half of <c>confirmationBlockers</c> (metadata only). Empty ⇒ the document is valid enough to add
 * items, and the backend <c>AddLineItem</c> endpoint (which requires a due date for PAYMENT items,
 * and inherits plant/supplier from the document) will accept them.</p>
 *
 * <p>Optional metadata introduced after a historical draft was created (series, OCR evidence, …) is
 * never required here — an older document becomes recoverable the moment its required fields are
 * present, which is what keeps the path working for pre-existing drafts.</p>
 */
export function documentItemEntryBlockers(document: PaymentSourceDocumentDto): string[] {
    const blockers: string[] = [];
    const missing = (v: unknown) =>
        v == null || (typeof v === 'string' && v.trim().length === 0);

    if (missing(document.attachmentId)) blockers.push('Anexe o ficheiro do documento.');
    if (missing(document.supplierId)) blockers.push('Selecione o fornecedor do documento.');
    if (missing(document.sourceDocumentType)) blockers.push('Selecione o tipo de documento.');
    if (missing(document.documentNumber)) blockers.push('Informe o número do documento.');
    if (missing(document.documentDate)) blockers.push('Informe a data do documento.');
    // Required by the backend for every PAYMENT line item — an item created without it is rejected.
    if (missing(document.dueDate)) blockers.push('Informe a data de vencimento do documento.');
    if (missing(document.currency)) blockers.push('Selecione a moeda do documento.');
    if ((document.grossAmount ?? 0) <= 0) blockers.push('Informe o valor total do documento.');

    return blockers;
}

/** True when the persisted document may receive a new item. */
export function canAddItemToDocument(document: PaymentSourceDocumentDto): boolean {
    return documentItemEntryBlockers(document).length === 0;
}

/**
 * The canonical <c>createLineItem</c> payload for an item added to a persisted document.
 *
 * <p>Identical shape to the creation flow (<c>usePaymentRequestCreation</c>): the plant, the due
 * date and — the mapping that matters — the <c>paymentSourceDocumentId</c> are taken from the
 * document the item belongs to, never from the request. Reuses the existing endpoint and its
 * validator; nothing here is a second persistence model.</p>
 */
export function buildRecoveryLineItemPayload(
    document: Pick<PaymentSourceDocumentDto, 'id' | 'dueDate' | 'plantId'>,
    item: Pick<TemporaryPaymentItem,
        'description' | 'quantity' | 'unitId' | 'unitCode' | 'unitPrice' |
        'discountAmount' | 'ivaRateId' | 'totalAmount' | 'itemCatalogId'>
) {
    return {
        description: item.description,
        quantity: item.quantity,
        unitId: item.unitId,
        // The backend resolves the unit from its CODE, so both are sent.
        unit: item.unitCode ?? undefined,
        unitPrice: item.unitPrice,
        discountAmount: item.discountAmount,
        ivaRateId: item.ivaRateId,
        totalAmount: item.totalAmount,
        itemCatalogId: item.itemCatalogId,
        // Mandatory for every PAYMENT item, taken from the document (a request holding two invoices
        // due on different dates has no single request-level answer).
        dueDate: document.dueDate ? new Date(document.dueDate).toISOString() : undefined,
        plantId: document.plantId,
        // The item names its OWN document's persisted id — the link the whole recovery exists for.
        paymentSourceDocumentId: document.id,
    };
}

/** A new line the user typed is only worth persisting once it is minimally complete. */
export function recoveryItemValidationError(item: TemporaryPaymentItem): string | null {
    if (!item.description || item.description.trim().length === 0)
        return 'Descrição do item é obrigatória.';
    if (!item.quantity || item.quantity <= 0)
        return 'Quantidade deve ser maior que zero.';
    if (item.unitId == null)
        return 'Selecione a unidade do item.';
    if (!item.unitPrice || item.unitPrice <= 0)
        return 'Preço unitário deve ser maior que zero.';
    if (item.ivaRateId == null)
        return 'Selecione a taxa de IVA do item.';
    return null;
}
