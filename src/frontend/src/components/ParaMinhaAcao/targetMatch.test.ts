import { describe, it, expect } from 'vitest';
import { resolveTargetItem, normalizeId } from './targetMatch';
import type { PersonalActionItem } from '../../types/myActions';

// Real (pure) unit tests — resolveTargetItem needs no DOM, so we exercise the actual matcher, not just
// its source. Guards the Phase-2 GUID false-not-found bug (uppercase deep-link vs lowercase .NET Guid).

const REQ_LOWER = '9b2b23e7-3f4e-4c1c-a5ab-cbccc66392ac';
const REQ_UPPER = '9B2B23E7-3F4E-4C1C-A5AB-CBCCC66392AC';
const GRP_LOWER = '5772d42c-336f-41e2-9530-6d69ac941eab';
const GRP_UPPER = '5772D42C-336F-41E2-9530-6D69AC941EAB';

function item(over: Partial<PersonalActionItem> = {}): PersonalActionItem {
  return {
    actionId: 'PO_CORRECTION:' + REQ_LOWER, requestId: REQ_LOWER, requestNumber: 'REQ-14/08/2026-254',
    requestTitle: 'Pagamento', requestTypeCode: 'PAYMENT', actionType: 'PO_CORRECTION',
    actionLabel: 'Corrigir P.O.', actionStatus: 'WAITING_PO_CORRECTION',
    poGroupId: GRP_LOWER, supplierId: 266, supplierName: 'SIMOTECNICA', purchaseOrderNumber: 'FAC2025/125',
    affectedSuppliers: [{ poGroupId: GRP_LOWER, supplierId: 266, supplierName: 'SIMOTECNICA', purchaseOrderNumber: 'FAC2025/125' }],
    ownerUserId: 'u1', ownerName: 'Celestina', dueDateUtc: null, needLevelCode: null, stageEnteredAtUtc: null,
    createdAtUtc: '2026-08-14T00:00:00Z', isOverdue: true, priorityBand: 'EXCEPTION_OR_OVERDUE',
    amount: 94791, currencyCode: 'AOA', route: '/requests?action=PO_CORRECTION&requestId=' + REQ_LOWER,
    ...over,
  };
}

describe('resolveTargetItem — case-insensitive GUID matching (v2.243.0 Phase 2 bugfix)', () => {
  const items = [item()];

  it('A. UPPERCASE target requestId matches a lowercase item.requestId', () => {
    expect(resolveTargetItem(items, { actionType: 'PO_CORRECTION', requestId: REQ_UPPER })).toBe(items[0]);
  });
  it('B. UPPERCASE target poGroupId matches a lowercase item.poGroupId', () => {
    expect(resolveTargetItem(items, { actionType: 'PO_CORRECTION', requestId: REQ_UPPER, poGroupId: GRP_UPPER })).toBe(items[0]);
  });
  it('C. affectedSuppliers poGroupId match is case-insensitive', () => {
    const it2 = [item({ poGroupId: 'other-group', affectedSuppliers: [{ poGroupId: GRP_LOWER, supplierName: 'SIMOTECNICA' }] })];
    expect(resolveTargetItem(it2, { actionType: 'PO_CORRECTION', requestId: REQ_UPPER, poGroupId: GRP_UPPER })).toBe(it2[0]);
  });
  it('D. fallback to requestId+actionType when the specific poGroupId is absent', () => {
    const it3 = [item({ poGroupId: 'x', affectedSuppliers: [{ poGroupId: 'y', supplierName: 'S' }] })];
    expect(resolveTargetItem(it3, { actionType: 'PO_CORRECTION', requestId: REQ_UPPER, poGroupId: GRP_UPPER })).toBe(it3[0]);
  });
  it('E. a genuinely different requestId does NOT match', () => {
    expect(resolveTargetItem(items, { actionType: 'PO_CORRECTION', requestId: '00000000-0000-0000-0000-000000000000' })).toBeUndefined();
  });
  it('E2. a different actionType does NOT match (actionType stays exact)', () => {
    expect(resolveTargetItem(items, { actionType: 'RECEIVING', requestId: REQ_UPPER })).toBeUndefined();
  });
  it('F. a valid match is resolved (so the widget shows the target, not the not-found note)', () => {
    expect(resolveTargetItem(items, { actionType: 'PO_CORRECTION', requestId: REQ_UPPER, poGroupId: GRP_UPPER })).toBeDefined();
  });
  it('normalizeId trims + lowercases, null-safe', () => {
    expect(normalizeId('  9B2B23E7  ')).toBe('9b2b23e7');
    expect(normalizeId(null)).toBe('');
    expect(normalizeId(undefined)).toBe('');
  });
});
