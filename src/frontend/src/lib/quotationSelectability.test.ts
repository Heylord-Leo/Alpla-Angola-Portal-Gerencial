import { describe, it, expect } from 'vitest';
import { describeQuotationLifecycle, describeQuotationSelectability } from './quotationLifecycle';

/**
 * v2.249.5 §9.2 — the historical label is display; SELECTION follows the D3 reuse eligibility the server already resolves
 * per item (isReuseBlocked / isReuseAuthorized). A historical quotation whose reuse was explicitly authorised is selectable;
 * one with a single current (never-batched) item is selectable; one whose every item is blocked is not.
 */
const blocked = { reconciliationStatus: 'MAPPED', adjudicationStatus: 'AWARD_CANCELLED', isReuseBlocked: true, isReuseAuthorized: false };
const authorised = { reconciliationStatus: 'MAPPED', adjudicationStatus: 'AWARD_CANCELLED', isReuseBlocked: false, isReuseAuthorized: true };
const current = { reconciliationStatus: 'MAPPED', adjudicationStatus: 'NOT_SUBMITTED', isReuseBlocked: false, isReuseAuthorized: false };

describe('quotationLifecycle — describeQuotationSelectability', () => {
    it('unauthorised reuse: historical label visible, selection blocked with a hint', () => {
        const q = { lifecycleStatus: 'HISTORICAL_CANCELLED', historicalItemCount: 2, historicalReleaseKind: 'RESTART_QUOTATION', items: [blocked, blocked] };
        expect(describeQuotationLifecycle(q)?.label).toBe('Histórica — adjudicação cancelada');
        expect(describeQuotationSelectability(q)).toMatchObject({ selectable: false, reason: 'REUSE_AUTHORIZATION_REQUIRED' });
        expect(describeQuotationSelectability(q).hint).toMatch(/autorização de reutilização/);
    });
    it('authorised reuse: the SAME historical label stays, but the quotation is selectable', () => {
        const q = { lifecycleStatus: 'HISTORICAL_CANCELLED', historicalItemCount: 2, items: [authorised, blocked] };
        expect(describeQuotationLifecycle(q)?.isHistorical).toBe(true);
        expect(describeQuotationSelectability(q)).toMatchObject({ selectable: true, reason: 'REUSE_AUTHORIZED' });
        expect(describeQuotationSelectability(q).hint).toMatch(/Reutilização autorizada/);
    });
    it('mixed quotation (historical items + a current item): selectable on the current item, no authorisation needed', () => {
        const q = { lifecycleStatus: 'PARTIALLY_HISTORICAL', historicalItemCount: 1, quotedItemCount: 2, items: [blocked, current] };
        expect(describeQuotationLifecycle(q)?.isHistorical).toBe(false);
        expect(describeQuotationSelectability(q)).toEqual({ selectable: true, reason: 'ELIGIBLE', hint: null });
    });
    it('placeholders (NOT_QUOTED / IGNORED) never make a quotation selectable or blocked on their own', () => {
        const placeholder = { reconciliationStatus: 'NOT_QUOTED', adjudicationStatus: 'NOT_QUOTED' };
        expect(describeQuotationSelectability({ lifecycleStatus: 'HISTORICAL_CANCELLED', items: [blocked, placeholder] }).selectable).toBe(false);
        expect(describeQuotationSelectability({ lifecycleStatus: 'OPEN', items: [current, placeholder] }).selectable).toBe(true);
    });
    it('no item facts (legacy payload): falls back to the lifecycle — historical not selectable, anything else selectable', () => {
        expect(describeQuotationSelectability({ lifecycleStatus: 'HISTORICAL_CANCELLED', items: [] })).toMatchObject({ selectable: false, reason: 'NO_ITEM_FACTS' });
        expect(describeQuotationSelectability({ lifecycleStatus: 'OPEN' })).toMatchObject({ selectable: true, reason: 'NO_ITEM_FACTS' });
        expect(describeQuotationSelectability({})).toMatchObject({ selectable: true, reason: 'NO_ITEM_FACTS' });
    });
});
