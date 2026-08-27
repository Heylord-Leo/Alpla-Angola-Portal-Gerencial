import { describe, it, expect } from 'vitest';
import {
    buildRecoveryLineItemPayload,
    canAddItemToDocument,
    documentItemEntryBlockers,
    recoveryItemValidationError
} from './paymentSourceDocumentRecovery';
import type { PaymentSourceDocumentDto } from '../types/paymentSourceDocument';
import type { TemporaryPaymentItem } from './paymentRequestCreation';

// Phase 4B.1 (Issue 2) — a PERSISTED payment source document with zero linked items must be
// recoverable: the user can add the first item once the document's own required fields are present,
// and that item must link back to the document (paymentSourceDocumentId), not be orphaned.

const doc = (over: Partial<PaymentSourceDocumentDto> = {}): PaymentSourceDocumentDto => ({
    id: 'doc-1',
    sequenceNumber: 1,
    attachmentId: 'att-1',
    supplierId: 5,
    supplierNameSnapshot: 'ACME',
    plantId: 3,
    sourceDocumentType: 'PROFORMA',
    documentNumber: 'FT 2026/1',
    documentDate: '2026-08-20',
    dueDate: '2026-09-20',
    currency: 'AOA',
    netAmount: 100,
    taxAmount: 0,
    grossAmount: 100,
    itemsTotal: 0,
    classificationConflictAcknowledged: false,
    classificationReviewedByFinance: false,
    requiresOperationInvoice: false,
    requiresAdvanceRegularization: false,
    requiresFinanceClassificationReview: false,
    isVoided: false,
    items: [],
    validationMessages: [],
    isValid: true,
    ...over,
} as PaymentSourceDocumentDto);

const item = (over: Partial<TemporaryPaymentItem> = {}): TemporaryPaymentItem => ({
    tempId: 't-1',
    description: 'Serviço X',
    quantity: 2,
    unitId: 7,
    unitCode: 'UN',
    unitPrice: 50,
    discountAmount: null,
    ivaRateId: 4,
    totalAmount: 100,
    itemCatalogId: null,
    itemCatalogCode: null,
    persistedId: null,
    ...over,
});

describe('documentItemEntryBlockers', () => {
    it('A — a fully-specified persisted document with zero items is ready to receive its first item', () => {
        const d = doc({ items: [] });
        expect(documentItemEntryBlockers(d)).toEqual([]);
        expect(canAddItemToDocument(d)).toBe(true);
    });

    it('B — a document missing its due date blocks item entry with an explicit reason', () => {
        const blockers = documentItemEntryBlockers(doc({ dueDate: null }));
        expect(blockers.some(b => b.includes('data de vencimento'))).toBe(true);
        expect(canAddItemToDocument(doc({ dueDate: null }))).toBe(false);
    });

    it('B — a document missing supplier / number / total each blocks with a reason', () => {
        expect(documentItemEntryBlockers(doc({ supplierId: null })).some(b => b.includes('fornecedor'))).toBe(true);
        expect(documentItemEntryBlockers(doc({ documentNumber: null })).some(b => b.includes('número'))).toBe(true);
        expect(documentItemEntryBlockers(doc({ grossAmount: 0 })).some(b => b.includes('valor total'))).toBe(true);
    });

    it('C — supplying the missing field flips the document to ready (no residual blocker)', () => {
        const before = doc({ dueDate: null });
        expect(canAddItemToDocument(before)).toBe(false);
        const after = { ...before, dueDate: '2026-09-20' };
        expect(documentItemEntryBlockers(after)).toEqual([]);
        expect(canAddItemToDocument(after)).toBe(true);
    });

    it('G — a historical document without newer OPTIONAL metadata is still recoverable', () => {
        // No series, no OCR evidence, no classification review — none of these are required.
        const historical = doc({
            documentSeries: null,
            ocrSuggestion: null,
            ocrEvidenceJson: null,
            classificationSuggestionSource: null,
        });
        expect(documentItemEntryBlockers(historical)).toEqual([]);
        expect(canAddItemToDocument(historical)).toBe(true);
    });

    it('does not gate item entry on already having an item (would be circular)', () => {
        // The zero-item case is the whole point — it must NOT itself be a blocker.
        expect(documentItemEntryBlockers(doc({ items: [], itemsTotal: 0 }))).toEqual([]);
    });
});

describe('buildRecoveryLineItemPayload', () => {
    it('D — the payload carries the document id as paymentSourceDocumentId', () => {
        const payload = buildRecoveryLineItemPayload(doc({ id: 'doc-42' }), item());
        expect(payload.paymentSourceDocumentId).toBe('doc-42');
    });

    it('D — plant and due date are taken from the document, not the request', () => {
        const payload = buildRecoveryLineItemPayload(
            doc({ id: 'doc-42', plantId: 9, dueDate: '2026-09-20' }), item());
        expect(payload.plantId).toBe(9);
        expect(payload.dueDate).toBe(new Date('2026-09-20').toISOString());
    });

    it('D — carries the canonical item fields (unit code included for backend resolution)', () => {
        const payload = buildRecoveryLineItemPayload(doc(), item({ unitCode: 'KG', ivaRateId: 4 }));
        expect(payload).toMatchObject({
            description: 'Serviço X', quantity: 2, unitId: 7, unit: 'KG',
            unitPrice: 50, ivaRateId: 4, totalAmount: 100,
        });
    });

    it('omits due date when the document has none (backend then refuses — surfaced upstream)', () => {
        const payload = buildRecoveryLineItemPayload(doc({ dueDate: null }), item());
        expect(payload.dueDate).toBeUndefined();
    });
});

describe('recoveryItemValidationError', () => {
    it('accepts a minimally complete line', () => {
        expect(recoveryItemValidationError(item())).toBeNull();
    });
    it('rejects blank description, zero quantity, missing unit, zero price, missing IVA', () => {
        expect(recoveryItemValidationError(item({ description: '  ' }))).toMatch(/Descrição/);
        expect(recoveryItemValidationError(item({ quantity: 0 }))).toMatch(/Quantidade/);
        expect(recoveryItemValidationError(item({ unitId: null }))).toMatch(/unidade/);
        expect(recoveryItemValidationError(item({ unitPrice: 0 }))).toMatch(/Preço/);
        expect(recoveryItemValidationError(item({ ivaRateId: null }))).toMatch(/IVA/);
    });
});
