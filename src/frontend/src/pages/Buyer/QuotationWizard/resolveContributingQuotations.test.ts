import { describe, it, expect } from 'vitest';
import { resolveBatchContributingQuotations, quotationEditMode } from './resolveContributingQuotations';

// Phase 4 — resolver from an ApprovalBatch's candidates/legacy winner to the DISTINCT contributing
// existing quotations, plus the EDIT-mode source rule. Pure, deterministic, strict matching.

const q = (id: string, over: any = {}): any => ({
    id, supplierNameSnapshot: 'Fornecedor ' + id, documentNumber: 'DOC-' + id, currency: 'AOA',
    totalAmount: 1000, sourceType: 'MANUAL', items: [{ id: 'qi-' + id, totalAmount: 1000 }], ...over,
});
const candItem = (quotationIds: string[]): any => ({
    id: 'abi-' + quotationIds.join('-'), requestLineItemId: 'li', selectedQuotationItemId: null,
    candidates: quotationIds.map((qid, i) => ({ id: 'c' + i, quotationItemId: 'qi-' + qid, quotationId: qid, supplierName: 'S', description: 'd', quantity: 1 })),
});

describe('resolveBatchContributingQuotations', () => {
    it('A1 — one candidate → one quotation', () => {
        const res = resolveBatchContributingQuotations({ items: [candItem(['a'])] }, [q('a'), q('b')]);
        expect(res.map(x => x.id)).toEqual(['a']);
    });

    it('A2 — multiple candidates from the SAME quotation → deduped to one', () => {
        const batch = { items: [candItem(['a']), candItem(['a'])] };
        const res = resolveBatchContributingQuotations(batch as any, [q('a'), q('b')]);
        expect(res.map(x => x.id)).toEqual(['a']);
    });

    it('A3 — candidates from different quotations → many (order-preserving)', () => {
        const batch = { items: [candItem(['b']), candItem(['a'])] };
        const res = resolveBatchContributingQuotations(batch as any, [q('a'), q('b')]);
        expect(res.map(x => x.id)).toEqual(['a', 'b']); // preserves the request's quotation order
    });

    it('A4 — no candidate relation → zero', () => {
        const batch = { items: [{ id: 'abi', requestLineItemId: 'li', selectedQuotationItemId: null, candidates: [] }] };
        expect(resolveBatchContributingQuotations(batch as any, [q('a')])).toEqual([]);
    });

    it('A5 — legacy winner (no candidates) resolves via the quotation ITEM id, strictly', () => {
        const legacy = { items: [{ id: 'abi', requestLineItemId: 'li', selectedQuotationItemId: 'qi-a', candidates: [] }] };
        const res = resolveBatchContributingQuotations(legacy as any, [q('a'), q('b')]);
        expect(res.map(x => x.id)).toEqual(['a']);
    });

    it('A6 — never guesses by supplier name or total (only id links resolve)', () => {
        // Same supplier name + same total on both, but the candidate points to "a" only.
        const qa = q('a', { supplierNameSnapshot: 'ACME', totalAmount: 5000 });
        const qb = q('b', { supplierNameSnapshot: 'ACME', totalAmount: 5000 });
        const res = resolveBatchContributingQuotations({ items: [candItem(['a'])] }, [qa, qb]);
        expect(res.map(x => x.id)).toEqual(['a']);
    });

    it('A7 — empty/undefined inputs → zero, never throws', () => {
        expect(resolveBatchContributingQuotations(null, [q('a')])).toEqual([]);
        expect(resolveBatchContributingQuotations({ items: [candItem(['a'])] }, [])).toEqual([]);
        expect(resolveBatchContributingQuotations({ items: [] as any }, undefined)).toEqual([]);
    });
});

describe('quotationEditMode (D — source-driven, EDIT not conversion)', () => {
    it('MANUAL quotation → MANUAL edit', () => {
        expect(quotationEditMode({ sourceType: 'MANUAL' })).toBe('MANUAL');
    });
    it('OCR quotation → UPLOAD edit', () => {
        expect(quotationEditMode({ sourceType: 'OCR' })).toBe('UPLOAD');
    });
});
