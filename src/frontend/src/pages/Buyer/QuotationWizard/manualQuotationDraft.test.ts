import { describe, it, expect } from 'vitest';
import { buildManualQuotationDraftItems } from './manualQuotationDraft';

// Eligible = quotationLifecycleStatus in [null, undefined, 'QUOTATION_PENDING'] (batchEligibility).
const eligible = (over: any = {}) => ({ id: 'li-1', description: 'Laptop HP Probook 450', quantity: 1, unitId: 7, quotationLifecycleStatus: null, ...over });

describe('manual quotation draft seeding (Option A fix)', () => {
  it('1. seeds the eligible requested item as a quotation row', () => {
    const items = buildManualQuotationDraftItems([eligible()]);
    expect(items).toHaveLength(1);
    expect(items[0].description).toBe('Laptop HP Probook 450');
  });

  it('2. does NOT pre-set reconciliationStatus to NOT_QUOTED (left unset, like OCR items)', () => {
    const items = buildManualQuotationDraftItems([eligible()]);
    expect(items[0].reconciliationStatus).toBeUndefined();
    expect(items[0].reconciliationStatus).not.toBe('NOT_QUOTED');
  });

  it('3. preserves mappedRequestLineItemId (= requested line id) so reconciliation can auto-map', () => {
    const items = buildManualQuotationDraftItems([eligible({ id: 'req-99' })]);
    expect(items[0].mappedRequestLineItemId).toBe('req-99');
  });

  it('4. preserves quantity/description/unit context; prices start at 0', () => {
    const items = buildManualQuotationDraftItems([eligible({ description: 'Rato sem fios', quantity: 3, unitId: 12 })]);
    expect(items[0].quantity).toBe(3);
    expect(items[0].description).toBe('Rato sem fios');
    expect(items[0].unitId).toBe(12);
    expect(items[0].unitPrice).toBe(0);
    expect(items[0].totalPrice).toBe(0);
  });

  it('5. seeds multiple eligible items and excludes ineligible ones', () => {
    const items = buildManualQuotationDraftItems([
      eligible({ id: 'a', quotationLifecycleStatus: null }),
      eligible({ id: 'b', quotationLifecycleStatus: 'QUOTATION_PENDING' }),
      eligible({ id: 'c', quotationLifecycleStatus: 'BATCH_ASSIGNED' }),   // ineligible
      eligible({ id: 'd', quotationLifecycleStatus: 'QUOTATION_APPROVED' }), // ineligible
    ]);
    expect(items.map(i => i.mappedRequestLineItemId)).toEqual(['a', 'b']);
  });

  it('6. leaves items open so the EXISTING "Não cotado" transition remains possible downstream', () => {
    // With no pre-set status, a seeded row is a priceable quotation row AND the buyer can still mark
    // it not-quoted via the unchanged control — nothing here pre-empts that transition.
    const items = buildManualQuotationDraftItems([eligible()]);
    expect(items[0].reconciliationStatus).toBeUndefined();
    // It carries the requested-line link the not-quoted control keys off of.
    expect(items[0].mappedRequestLineItemId).toBe('li-1');
  });

  it('null/empty input yields no items (safe)', () => {
    expect(buildManualQuotationDraftItems([])).toEqual([]);
    expect(buildManualQuotationDraftItems(null as any)).toEqual([]);
  });
});

describe('reconciliation shape contract (follow-up crash fix)', () => {
  it('populates `description` as a non-empty STRING — the field consumed at reconciliation (auto-suggest .trim())', () => {
    const items = buildManualQuotationDraftItems([eligible({ description: 'Laptop HP Probook 450' })]);
    expect(typeof items[0].description).toBe('string');
    expect(items[0].description.trim().length).toBeGreaterThan(0); // would have thrown if undefined
  });

  it('description reflects the NORMALIZED requested line-item data (group.lineItems.description)', () => {
    const items = buildManualQuotationDraftItems([eligible({ description: 'Teclado ABNT2' })]);
    expect(items[0].description).toBe('Teclado ABNT2');
  });

  it('quantity is a NUMBER (required by the draft contract)', () => {
    const items = buildManualQuotationDraftItems([eligible({ quantity: 4 })]);
    expect(typeof items[0].quantity).toBe('number');
    expect(items[0].quantity).toBe(4);
  });

  it('raw list-row shape (itemDescription only) leaves description undefined — proving the seed MUST use group.lineItems', () => {
    // Documents the root cause: the raw group.items rows carry `itemDescription`, not `description`.
    const rawRow: any = { id: 'x', itemDescription: 'Só bruto', quantity: 1, quotationLifecycleStatus: null };
    const items = buildManualQuotationDraftItems([rawRow]);
    expect(items[0].description).toBeUndefined(); // <- why the call site passes group.lineItems, not group.items
  });

  it('an eligible item missing optional fields (unitId/itemCatalog) still yields a valid priceable row', () => {
    const items = buildManualQuotationDraftItems([eligible({ unitId: undefined })]);
    expect(items).toHaveLength(1);
    expect(items[0].unitId).toBeNull();      // normalized to null, not crashing
    expect(items[0].itemCatalogId).toBeNull();
    expect(items[0].reconciliationStatus).toBeUndefined();
  });
});
