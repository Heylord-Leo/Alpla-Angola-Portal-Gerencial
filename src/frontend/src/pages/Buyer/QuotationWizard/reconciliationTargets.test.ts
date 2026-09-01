import { describe, it, expect } from 'vitest';
import { reconciliationRequestItems, draftMappedRequestItemIds } from './reconciliationTargets';
import { isLineItemEligibleForQuotation } from '../batchEligibility';

// Phase 4 — reconciliation target = eligible-for-new UNION already-mapped-by-this-draft. The helper
// is STATE-INDEPENDENT: it keys off item lifecycle + the draft's persisted mapping, not the batch
// status — so the AREA_ADJUSTMENT and FINAL_ADJUSTMENT scenarios are the same code path (a mapped
// BATCH_ASSIGNED item and a mapped QUOTATION_APPROVED item are both covered below).

const li = (id: string, lifecycle: string | null): any => ({ id, lineNumber: 1, description: 'Item ' + id, quantity: 1, quotationLifecycleStatus: lifecycle });
const targets = (items: any[], mapped: string[]) =>
    reconciliationRequestItems(items, new Set(mapped), isLineItemEligibleForQuotation).map(x => x.id);

describe('reconciliationRequestItems (union of eligible + already-mapped)', () => {
    it('1. eligible QUOTATION_PENDING item is included', () => {
        expect(targets([li('a', 'QUOTATION_PENDING')], [])).toEqual(['a']);
        expect(targets([li('a', null)], [])).toEqual(['a']); // null is also eligible
    });

    it('2. BATCH_ASSIGNED item NOT mapped by the draft is excluded', () => {
        expect(targets([li('a', 'BATCH_ASSIGNED')], [])).toEqual([]);
    });

    it('3. QUOTATION_APPROVED item NOT mapped by the draft is excluded', () => {
        expect(targets([li('a', 'QUOTATION_APPROVED')], [])).toEqual([]);
    });

    it('4. BATCH_ASSIGNED item ALREADY mapped by the draft is included (AREA_ADJUSTMENT edit)', () => {
        expect(targets([li('a', 'BATCH_ASSIGNED')], ['a'])).toEqual(['a']);
    });

    it('5. QUOTATION_APPROVED item ALREADY mapped by the draft is included (FINAL_ADJUSTMENT edit)', () => {
        expect(targets([li('a', 'QUOTATION_APPROVED')], ['a'])).toEqual(['a']);
    });

    it('6. multiple mapped ids include ONLY those exact request items (cross-batch safe)', () => {
        const items = [
            li('open', 'QUOTATION_PENDING'),
            li('mine', 'BATCH_ASSIGNED'),      // linked by this draft → included
            li('other', 'BATCH_ASSIGNED'),     // another batch, NOT linked → excluded
        ];
        expect(targets(items, ['mine'])).toEqual(['open', 'mine']);
    });

    it('7. no duplicate when an item is BOTH globally eligible AND mapped', () => {
        expect(targets([li('a', 'QUOTATION_PENDING')], ['a'])).toEqual(['a']);
    });

    it('8. empty mapped set preserves NEW behavior (union collapses to eligible)', () => {
        const items = [li('a', 'QUOTATION_PENDING'), li('b', 'BATCH_ASSIGNED')];
        expect(targets(items, [])).toEqual(['a']); // identical to eligible-only
    });

    it('preserves the request order and never throws on empty/undefined', () => {
        expect(reconciliationRequestItems(null, new Set(), isLineItemEligibleForQuotation)).toEqual([]);
        expect(reconciliationRequestItems([li('a', null)], new Set(), isLineItemEligibleForQuotation).map(x => x.id)).toEqual(['a']);
    });
});

describe('draftMappedRequestItemIds', () => {
    it('9. collects the existing mappedRequestLineItemId values (price-only edit preserves the link)', () => {
        const draftItems = [
            { mappedRequestLineItemId: 'req-1', unitPrice: 80000 }, // price edited, mapping intact
            { mappedRequestLineItemId: null },
            { mappedRequestLineItemId: undefined },
        ];
        const ids = draftMappedRequestItemIds(draftItems);
        expect([...ids]).toEqual(['req-1']);
    });

    it('empty/undefined draft yields an empty set', () => {
        expect(draftMappedRequestItemIds(null).size).toBe(0);
        expect(draftMappedRequestItemIds([]).size).toBe(0);
    });
});
