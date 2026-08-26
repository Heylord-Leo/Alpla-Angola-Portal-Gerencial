import { describe, it, expect } from 'vitest';
import { confirmationBlockers } from './paymentDocumentComposition';
import type { TemporaryPaymentDocument } from './paymentRequestCreation';

// Phase 4B (Fix 4A) — a PAYMENT source document cannot be confirmed/persisted without a due date and at
// least one item. This is the guard that prevents the historical broken-draft state (document commits,
// then every line-item POST fails because the due date is absent, leaving a 0-item orphan draft).

const item = (): any => ({
  tempId: 't-item', description: 'x', quantity: 1, unitId: 1, unitCode: 'UN', unitPrice: 100,
  discountAmount: 0, ivaRateId: null, totalAmount: 100, itemCatalogId: null,
});

const doc = (over: Partial<TemporaryPaymentDocument> = {}): TemporaryPaymentDocument => ({
  tempId: 't1', localSequence: 1, confirmed: false, entryMode: 'DOCUMENT' as any,
  attachmentId: 'att-1', supplierId: 5, supplierInternalCompany: null, plantId: 1,
  sourceDocumentType: 'PROFORMA', documentNumber: 'DOC-1', documentDate: '2026-08-20',
  dueDate: '2026-09-20', currency: 'AOA', netAmount: 100, taxAmount: 0, grossAmount: 100,
  items: [item()], persistedId: null, ...over,
} as any);

describe('confirmationBlockers — due date & items guard', () => {
  it('blocks a document with no due date', () => {
    const blockers = confirmationBlockers(doc({ dueDate: null }), false);
    expect(blockers.some(b => b.includes('data de vencimento'))).toBe(true);
  });

  it('blocks a document with zero items', () => {
    const blockers = confirmationBlockers(doc({ items: [] }), false);
    expect(blockers.some(b => b.includes('pelo menos um item'))).toBe(true);
  });

  it('does NOT raise the due-date blocker once a due date is present', () => {
    const blockers = confirmationBlockers(doc({ dueDate: '2026-09-20' }), false);
    expect(blockers.some(b => b.includes('data de vencimento'))).toBe(false);
  });

  it('a fully-specified document (attachment, supplier, dates, due date, items, total) is confirmable', () => {
    expect(confirmationBlockers(doc(), false)).toEqual([]);
  });
});
