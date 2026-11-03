import { describe, it, expect } from 'vitest';
import { compareQuotationsPerLine, describeCardComparison, describeItemComparison, describeScopeNotice, ComparableQuotation } from './quotationComparison';

const L1 = 'line-1', L2 = 'line-2';
const item = (id: string, line: string | null, unitPrice: number, extra: Partial<ComparableQuotation['items'] extends (infer T)[] | undefined ? T : never> = {}) => ({
    id, mappedRequestLineItemId: line, reconciliationStatus: 'MAPPED', unitCode: 'UN', quantity: 2, unitPrice, ...extra,
});
const quotation = (id: string, items: any[], extra: Partial<ComparableQuotation> = {}): ComparableQuotation => ({
    id, currency: 'AOA', totalAmount: items.reduce((s, i) => s + i.unitPrice * i.quantity, 0), items, ...extra,
});

describe('quotationComparison — the REQ-628 shape (cancelled 2-item adjudication vs 1-item re-quotation)', () => {
    const trevotech = quotation('q-trevo', [
        item('t1', L1, 100, { adjudicationStatus: 'AWARD_CANCELLED', isReuseBlocked: true }),
        item('t2', L2, 50, { adjudicationStatus: 'AWARD_CANCELLED', isReuseBlocked: true }),
    ], { lifecycleStatus: 'HISTORICAL_CANCELLED' });
    const maqsis = quotation('q-maqsis', [item('m1', L1, 120, { adjudicationStatus: 'AWARDED' })], { lifecycleStatus: 'AWARDED' });

    it('never awards a card-level "Menor valor": scopes differ and one document is history', () => {
        const r = compareQuotationsPerLine([trevotech, maqsis]);
        expect(r.identicalScope).toBe(false);
        expect(r.byQuotationId.get('q-trevo')).toMatchObject({ lowestTotal: false, reason: 'HISTORICAL' });
        expect(r.byQuotationId.get('q-maqsis')).toMatchObject({ lowestTotal: false, reason: 'SINGLE' });
        expect(describeCardComparison(r.byQuotationId.get('q-maqsis'), 'AOA')).toBeNull();
        expect(describeCardComparison(r.byQuotationId.get('q-trevo'), 'AOA')).toBeNull();
    });
    it('historical items are not candidates; the re-quotation is the only current candidate of its line', () => {
        const r = compareQuotationsPerLine([trevotech, maqsis]);
        expect(r.byItemId.get('t1')).toMatchObject({ status: 'HISTORICAL', reason: 'HISTORICAL_QUOTATION' });
        expect(r.byItemId.get('m1')).toMatchObject({ status: 'ONLY_CANDIDATE', candidateCount: 1 });
        expect(describeItemComparison(r.byItemId.get('m1'))).toMatchObject({ label: 'Única cotação atual para o item' });
    });
});

describe('quotationComparison — per-line unit price', () => {
    it('identical scope: per-item winners + a card-level lowest total', () => {
        const a = quotation('a', [item('a1', L1, 100), item('a2', L2, 10)]);
        const b = quotation('b', [item('b1', L1, 90), item('b2', L2, 12)]);
        const r = compareQuotationsPerLine([a, b]);
        expect(r.identicalScope).toBe(true);
        expect(r.anyQuantityDiffers).toBe(false);
        expect(r.byItemId.get('a1')?.status).toBe('HIGHER');
        expect(r.byItemId.get('b1')?.status).toBe('LOWEST');
        expect(r.byItemId.get('a2')?.status).toBe('LOWEST');
        expect(r.byItemId.get('b2')?.status).toBe('HIGHER');
        // totals: a = 220, b = 204
        expect(r.byQuotationId.get('b')).toMatchObject({ lowestTotal: true, lowestLineCount: 1, indicativeLineCount: 0, candidateLineCount: 2, reason: 'IDENTICAL_SCOPE' });
        expect(r.byQuotationId.get('a')).toMatchObject({ lowestTotal: false, lowestLineCount: 1 });
        expect(describeCardComparison(r.byQuotationId.get('b'), 'AOA')).toEqual({ label: 'Menor valor (AOA)', kind: 'LOWEST_TOTAL' });
        expect(describeItemComparison(r.byItemId.get('b1'))).toEqual({ label: 'Menor preço unit.', tone: 'lowest' });
        expect(describeScopeNotice(r, [a, b])).toBeNull();
    });
    it('different scope (partial coverage): no total claim, per-item chip instead', () => {
        const a = quotation('a', [item('a1', L1, 100), item('a2', L2, 10)]);
        const b = quotation('b', [item('b1', L1, 90)]);
        const r = compareQuotationsPerLine([a, b]);
        expect(r.identicalScope).toBe(false);
        expect(r.byQuotationId.get('b')).toMatchObject({ lowestTotal: false, reason: 'DIFFERENT_SCOPE', lowestLineCount: 1, candidateLineCount: 1 });
        expect(describeCardComparison(r.byQuotationId.get('b'), 'AOA')).toEqual({ label: 'Menor preço unit. em 1 de 1 item(ns)', kind: 'PER_ITEM' });
        expect(describeCardComparison(r.byQuotationId.get('a'), 'AOA')).toBeNull();
        expect(r.byItemId.get('a2')?.status).toBe('ONLY_CANDIDATE');
        expect(describeScopeNotice(r, [a, b])).toMatch(/itens ou quantidades diferentes/);
        expect(describeScopeNotice(r, [a, b])).not.toMatch(/indicativo/);
    });
    it('mixed currencies: not comparable per item nor per card', () => {
        const a = quotation('a', [item('a1', L1, 100)], { currency: 'AOA' });
        const b = quotation('b', [item('b1', L1, 1)], { currency: 'USD' });
        const r = compareQuotationsPerLine([a, b]);
        expect(r.byItemId.get('b1')).toMatchObject({ status: 'NOT_COMPARABLE', reason: 'CURRENCY' });
        expect(r.byQuotationId.get('b')).toMatchObject({ lowestTotal: false, reason: 'MIXED_CURRENCY' });
        expect(describeScopeNotice(r, [a, b])).toMatch(/moedas diferentes/);
        expect(describeItemComparison(r.byItemId.get('b1'))?.label).toMatch(/Moeda diferente/);
    });
    it('different units on the same line are not compared', () => {
        const a = quotation('a', [item('a1', L1, 100, { unitCode: 'UN' })]);
        const b = quotation('b', [item('b1', L1, 5, { unitCode: 'CX' })]);
        const r = compareQuotationsPerLine([a, b]);
        expect(r.byItemId.get('b1')).toMatchObject({ status: 'NOT_COMPARABLE', reason: 'UNIT' });
        expect(r.byItemId.get('a1')).toMatchObject({ status: 'NOT_COMPARABLE', reason: 'UNIT' });
        expect(r.byQuotationId.get('a')?.lowestTotal).toBe(false);
    });
    it('ties mark every lowest candidate', () => {
        const a = quotation('a', [item('a1', L1, 100)]);
        const b = quotation('b', [item('b1', L1, 100)]);
        const c = quotation('c', [item('c1', L1, 130)]);
        const r = compareQuotationsPerLine([a, b, c]);
        expect(r.byItemId.get('a1')?.status).toBe('TIE_LOWEST');
        expect(r.byItemId.get('b1')?.status).toBe('TIE_LOWEST');
        expect(r.byItemId.get('c1')?.status).toBe('HIGHER');
        expect(r.byQuotationId.get('a')?.lowestTotal).toBe(true);
        expect(r.byQuotationId.get('b')?.lowestTotal).toBe(true);
        expect(r.byQuotationId.get('c')?.lowestTotal).toBe(false);
    });
    it('a cancelled adjudication item or a reuse-blocked item is excluded from the current comparison', () => {
        const a = quotation('a', [item('a1', L1, 50, { adjudicationStatus: 'AWARD_CANCELLED' })], { lifecycleStatus: 'PARTIALLY_HISTORICAL' });
        const b = quotation('b', [item('b1', L1, 80, { isReuseBlocked: true })]);
        const c = quotation('c', [item('c1', L1, 90)]);
        const r = compareQuotationsPerLine([a, b, c]);
        expect(r.byItemId.get('a1')).toMatchObject({ status: 'HISTORICAL', reason: 'HISTORICAL_ADJUDICATION' });
        expect(r.byItemId.get('b1')).toMatchObject({ status: 'HISTORICAL', reason: 'REUSE_BLOCKED' });
        expect(r.byItemId.get('c1')?.status).toBe('ONLY_CANDIDATE');
        expect([...r.byQuotationId.values()].every(x => !x.lowestTotal)).toBe(true);
    });
    it('unmapped lines (EXTRA_ITEM / NOT_QUOTED / IGNORED) are never compared', () => {
        const a = quotation('a', [item('a1', L1, 100), item('ax', null, 1, { reconciliationStatus: 'EXTRA_ITEM' }), item('an', L2, 0, { reconciliationStatus: 'NOT_QUOTED' })]);
        const r = compareQuotationsPerLine([a]);
        expect(r.byItemId.get('ax')?.status).toBe('NOT_COMPARED');
        expect(r.byItemId.get('an')?.status).toBe('NOT_COMPARED');
        expect(r.byQuotationId.get('a')).toMatchObject({ reason: 'SINGLE', lowestTotal: false });
    });
    it('a single quotation never claims "Menor valor"', () => {
        const r = compareQuotationsPerLine([quotation('a', [item('a1', L1, 100)])]);
        expect(describeCardComparison(r.byQuotationId.get('a'), 'AOA')).toBeNull();
    });
});

describe('quotationComparison — unequal quantities never produce an unqualified "Menor valor"', () => {
    it('same line, different quantities: informational QUANTITY_DIFFERS on every candidate, no win counted, totals not comparable', () => {
        const a = quotation('a', [item('a1', L1, 100, { quantity: 2 })]);
        const b = quotation('b', [item('b1', L1, 90, { quantity: 5 })]);
        const r = compareQuotationsPerLine([a, b]);
        expect(r.anyQuantityDiffers).toBe(true);
        expect(r.identicalScope).toBe(false);
        expect(r.byItemId.get('b1')).toMatchObject({ status: 'QUANTITY_DIFFERS', quantityDiffers: true, indicativeLowest: true });
        expect(r.byItemId.get('a1')).toMatchObject({ status: 'QUANTITY_DIFFERS', quantityDiffers: true, indicativeLowest: false });
        expect(describeItemComparison(r.byItemId.get('b1'))).toEqual({ label: 'Quantidades diferentes — menor preço unit. apenas indicativo', tone: 'muted' });
        expect(describeItemComparison(r.byItemId.get('a1'))).toEqual({ label: 'Quantidades diferentes — comparação apenas indicativa', tone: 'muted' });
        // no firm win on either card; the indicative chip is explicitly qualified
        expect(r.byQuotationId.get('b')).toMatchObject({ lowestTotal: false, lowestLineCount: 0, indicativeLineCount: 1, reason: 'DIFFERENT_SCOPE' });
        expect(r.byQuotationId.get('a')).toMatchObject({ lowestTotal: false, lowestLineCount: 0, indicativeLineCount: 0 });
        expect(describeCardComparison(r.byQuotationId.get('b'), 'AOA')).toEqual({ label: 'Menor preço unit. (indicativo, qtd. diferente) em 1 de 1 item(ns)', kind: 'INDICATIVE' });
        expect(describeCardComparison(r.byQuotationId.get('a'), 'AOA')).toBeNull();
        expect(describeScopeNotice(r, [a, b])).toMatch(/quantidades diferentes: nesses itens o menor preço unitário é apenas indicativo/);
    });
    it('a third candidate at another quantity turns the whole line indicative — the equal-quantity pair is not declared a winner either', () => {
        const a = quotation('a', [item('a1', L1, 100, { quantity: 2 })]);
        const b = quotation('b', [item('b1', L1, 95, { quantity: 2 })]);
        const c = quotation('c', [item('c1', L1, 80, { quantity: 10 })]);
        const r = compareQuotationsPerLine([a, b, c]);
        expect(r.byItemId.get('b1')?.status).toBe('QUANTITY_DIFFERS');
        expect(r.byItemId.get('b1')?.indicativeLowest).toBe(false);
        expect(r.byItemId.get('c1')).toMatchObject({ status: 'QUANTITY_DIFFERS', indicativeLowest: true });
        expect([...r.byQuotationId.values()].every(x => !x.lowestTotal && x.lowestLineCount === 0)).toBe(true);
    });
    it('tie at the same quantity stays a firm tie; a tie at different quantities is only indicative', () => {
        const same = compareQuotationsPerLine([quotation('a', [item('a1', L1, 100)]), quotation('b', [item('b1', L1, 100)])]);
        expect(same.byItemId.get('a1')?.status).toBe('TIE_LOWEST');
        const diff = compareQuotationsPerLine([quotation('a', [item('a1', L1, 100, { quantity: 1 })]), quotation('b', [item('b1', L1, 100, { quantity: 3 })])]);
        expect(diff.byItemId.get('a1')).toMatchObject({ status: 'QUANTITY_DIFFERS', indicativeLowest: true });
        expect(diff.byItemId.get('b1')).toMatchObject({ status: 'QUANTITY_DIFFERS', indicativeLowest: true });
        expect(diff.byQuotationId.get('a')?.lowestTotal).toBe(false);
        expect(diff.byQuotationId.get('b')?.lowestTotal).toBe(false);
    });
    it('mixed-scope quotations: firm win on the equal-quantity line, indicative on the other, card counts only the firm line', () => {
        const a = quotation('a', [item('a1', L1, 100), item('a2', L2, 10, { quantity: 4 })]);
        const b = quotation('b', [item('b1', L1, 90), item('b2', L2, 9, { quantity: 8 })]);
        const r = compareQuotationsPerLine([a, b]);
        expect(r.identicalScope).toBe(false);
        expect(r.byItemId.get('b1')?.status).toBe('LOWEST');
        expect(r.byItemId.get('b2')).toMatchObject({ status: 'QUANTITY_DIFFERS', indicativeLowest: true });
        expect(r.byQuotationId.get('b')).toMatchObject({ lowestTotal: false, lowestLineCount: 1, indicativeLineCount: 1, candidateLineCount: 2, reason: 'DIFFERENT_SCOPE' });
        expect(describeCardComparison(r.byQuotationId.get('b'), 'AOA')).toEqual({ label: 'Menor preço unit. em 1 de 2 item(ns)', kind: 'PER_ITEM' });
        expect(describeScopeNotice(r, [a, b])).toMatch(/itens ou quantidades diferentes/);
        expect(describeScopeNotice(r, [a, b])).toMatch(/apenas indicativo/);
    });
    it('identical lines and units but different quantities on a card never give a card-level "Menor valor" even with the lower total', () => {
        const a = quotation('a', [item('a1', L1, 100, { quantity: 10 })]);   // total 1000
        const b = quotation('b', [item('b1', L1, 120, { quantity: 1 })]);    // total 120 — cheaper total, smaller volume
        const r = compareQuotationsPerLine([a, b]);
        expect(r.identicalScope).toBe(false);
        expect(r.byQuotationId.get('b')?.lowestTotal).toBe(false);
        expect(r.byQuotationId.get('a')?.lowestTotal).toBe(false);
        expect(describeCardComparison(r.byQuotationId.get('b'), 'AOA')).toBeNull();
        expect(describeCardComparison(r.byQuotationId.get('a'), 'AOA')?.kind).toBe('INDICATIVE');
    });
});
