import { describe, it, expect } from 'vitest';
import {
  WORKSPACE_TABS, DEFAULT_TAB, resolveTab, backToQueueTarget, coverageChips,
  formatCurrency, formatTotalsByCurrency, metricOrAbsent, bucketLabel, batchKindLabel, supplierStatusLabel,
  lotLineNumbersLabel, lotItemCountLabel, stepStateMeta, clampIndex, formatLotAmount,
  timelineStepTimestamp,
  GROUPS_TAB, poGroupStatusLabel, batchReleasedItemsLabel, batchTotalsLines, headerApprovedTotals, resolveCancelPoGroupTarget, groupLineNumbersLabel,
} from './buyerWorkspaceView';

describe('tab routing (refresh-safe)', () => {
  it('exposes the four approved tabs (incl. "Grupos P.O."), default = items, no History tab', () => {
    expect(WORKSPACE_TABS.map(t => t.id)).toEqual(['items', 'quotes', 'batches', 'groups']);
    expect(WORKSPACE_TABS.map(t => t.label)).toEqual(['Itens & Cobertura', 'Cotações & Documentos', 'Lotes & Aprovações', 'Grupos P.O.']);
    expect(GROUPS_TAB).toBe('groups');
    expect(DEFAULT_TAB).toBe('items');
    expect(WORKSPACE_TABS.some(t => /hist/i.test(t.id) || /hist/i.test(t.label))).toBe(false);
  });
  it('resolves a valid tab and falls back to default for unknown/absent', () => {
    expect(resolveTab('quotes')).toBe('quotes');
    expect(resolveTab('batches')).toBe('batches');
    expect(resolveTab('groups')).toBe('groups');
    expect(resolveTab(null)).toBe('items');
    expect(resolveTab('bogus')).toBe('items');
  });
});

describe('back-navigation preserves queue state', () => {
  it('returns the captured queue URL (with filters) when present', () => {
    expect(backToQueueTarget('/buyer/items?ownership=me&card=partial&page=2&sort=deadline'))
      .toBe('/buyer/items?ownership=me&card=partial&page=2&sort=deadline');
  });
  it('falls back to the bare queue for missing or foreign origins', () => {
    expect(backToQueueTarget(undefined)).toBe('/buyer/items');
    expect(backToQueueTarget('/somewhere/else')).toBe('/buyer/items');
    expect(backToQueueTarget(123 as unknown)).toBe('/buyer/items');
  });
});

describe('coverage chips', () => {
  const base = { totalItems: 5, treated: 3, pending: 2, approved: 1, inActiveBatch: 1, readyForBatch: 1, closedNotQuoted: 0 };
  it('always shows the core buckets and hides legacy not-quoted when zero', () => {
    const chips = coverageChips({ ...base, notQuotedProposed: 0, notQuotedAccepted: 0 });
    const keys = chips.map(c => c.key);
    expect(keys).toContain('total');
    expect(keys).toContain('treated');
    expect(keys).toContain('pending');
    expect(keys).not.toContain('nqProposed');
    expect(keys).not.toContain('nqAccepted');
  });
  it('surfaces legacy not-quoted states only when present', () => {
    const chips = coverageChips({ ...base, notQuotedProposed: 2, notQuotedAccepted: 0 });
    expect(chips.map(c => c.key)).toContain('nqProposed');
    expect(chips.map(c => c.key)).not.toContain('nqAccepted');
  });
});

describe('per-currency formatting (never summed)', () => {
  it('formats a single currency amount with 2 decimals and the currency code', () => {
    // Assert decimals + code (grouping separator varies by the host ICU; browsers group, some Node ICUs don't).
    expect(formatCurrency(1234.5, 'AOA')).toMatch(/234,50 AOA$/);
  });
  it('joins multiple currencies WITHOUT summing them', () => {
    const s = formatTotalsByCurrency([{ currency: 'AOA', amount: 1000 }, { currency: 'EUR', amount: 200 }]);
    expect(s).toMatch(/AOA/);
    expect(s).toContain('200,00 EUR');
    expect(s).toContain('·'); // shown side by side, not added
    expect(s).not.toContain('1200'); // never summed
  });
  it('shows a dash for no purchase currencies', () => {
    expect(formatTotalsByCurrency([])).toBe('—');
  });
});

describe('neutral absence + labels', () => {
  it('uses a neutral absence label where zero would mislead', () => {
    expect(metricOrAbsent(0)).toBe('Sem histórico');
    expect(metricOrAbsent(3, 'compras')).toBe('3 compras');
  });
  it('maps canonical bucket and batch-kind codes to PT labels', () => {
    expect(bucketLabel('QUOTED_READY_FOR_BATCH')).toBe('Pronto para lote');
    expect(bucketLabel('PENDING_QUOTATION')).toBe('Pendente de cotação');
    expect(batchKindLabel('SUPERSEDED')).toBe('Substituído');
    expect(batchKindLabel('ACTIVE')).toBe('Ativo');
  });
  it('derives supplier status label', () => {
    expect(supplierStatusLabel({ isActive: false, registrationStatus: 'ACTIVE' })).toBe('Inativo');
    expect(supplierStatusLabel({ isActive: true, registrationStatus: 'ACTIVE' })).toBe('Ativo');
  });
});

describe('lot wording + timeline presentation', () => {
  it('renders line NUMBERS as identifiers (#), distinct from the item COUNT', () => {
    expect(lotLineNumbersLabel([2])).toBe('Itens incluídos: #2');
    expect(lotLineNumbersLabel([2, 3, 5])).toBe('Itens incluídos: #2, #3, #5');
    expect(lotLineNumbersLabel([])).toBe('Itens incluídos: —');
    expect(lotItemCountLabel(1)).toBe('1 item');
    expect(lotItemCountLabel(3)).toBe('3 itens');
  });

  it('maps timeline step states to distinct presentation, skipped → "Não aplicável"', () => {
    expect(stepStateMeta('completed').color).toBe('var(--color-status-green)');
    expect(stepStateMeta('current').color).toBe('var(--color-primary)');
    expect(stepStateMeta('blocked').color).toBe('var(--color-status-red)');
    expect(stepStateMeta('pending').muted).toBe(true);
    expect(stepStateMeta('skipped').note).toBe('Não aplicável');
  });

  it('clamps carousel index within bounds (no wrap)', () => {
    expect(clampIndex(-1, 3)).toBe(0);
    expect(clampIndex(5, 3)).toBe(2);
    expect(clampIndex(1, 3)).toBe(1);
    expect(clampIndex(0, 0)).toBe(0);
  });

  it('formats a lot amount per currency (never combined)', () => {
    expect(formatLotAmount(1500, 'AOA')).toMatch(/500,00 AOA$/);
    expect(formatLotAmount(1500, null)).toMatch(/500,00$/);
  });
});

// ── Phase 3E.2: timeline step timestamp disambiguation (no fabricated dates) ──
describe('timelineStepTimestamp', () => {
  it('shows the real date for a step that carries a recorded timestamp', () => {
    expect(timelineStepTimestamp('completed', '2026-08-24T14:32:00Z')).toEqual({ date: '2026-08-24T14:32:00Z' });
    expect(timelineStepTimestamp('current', '2026-08-25T09:15:00Z')).toEqual({ date: '2026-08-25T09:15:00Z' });
  });
  it('a reached step (completed/current) with no timestamp shows "Data não registada" — never a fake date', () => {
    expect(timelineStepTimestamp('completed', null)).toEqual({ text: 'Data não registada' });
    expect(timelineStepTimestamp('current', undefined)).toEqual({ text: 'Data não registada' });
  });
  it('a future/pending step shows "Ainda não iniciado" (distinct from a missing historical timestamp)', () => {
    expect(timelineStepTimestamp('pending', null)).toEqual({ text: 'Ainda não iniciado' });
  });
  it('blocked and skipped keep their own labels', () => {
    expect(timelineStepTimestamp('blocked', null)).toEqual({ text: 'Requer ação' });
    expect(timelineStepTimestamp('skipped', null)).toEqual({ text: 'Não aplicável' });
  });
});

// ── Phase 1 P.O. cancellation: "Grupos P.O." tab helpers, batch release lines, header totals ──
describe('P.O. group cancellation view helpers', () => {
  it('group status label reuses the shared map and maps CANCELLED → "Cancelado"', () => {
    expect(poGroupStatusLabel('CANCELLED')).toBe('Cancelado');
    expect(poGroupStatusLabel('WAITING_PO')).toBe('Aguardando P.O.');
    expect(poGroupStatusLabel('PO_ISSUED')).toBe('P.O. Emitida');
    expect(poGroupStatusLabel('SOMETHING_NEW')).toBe('SOMETHING_NEW');
    expect(groupLineNumbersLabel([1, 3])).toBe('Itens: #1, #3');
    expect(groupLineNumbersLabel([])).toBe('Itens: —');
  });
  it('released items line only when releasedItemCount > 0, with line identifiers', () => {
    expect(batchReleasedItemsLabel({ releasedItemCount: 0, releasedLineNumbers: [] })).toBeNull();
    expect(batchReleasedItemsLabel({ releasedItemCount: undefined, releasedLineNumbers: undefined })).toBeNull();
    expect(batchReleasedItemsLabel({ releasedItemCount: 2, releasedLineNumbers: [2, 5] })).toBe('Itens libertados: #2, #5');
  });
  it('batch totals: one line per currency, never summed across currencies; none when nothing was released', () => {
    expect(batchTotalsLines({ approvedTotalAmount: 1000, releasedItemCount: 0, activeTotalsByCurrency: [{ currency: 'AOA', amount: 1000 }], releasedTotalsByCurrency: [] })).toEqual([]);
    const single = batchTotalsLines({ approvedTotalAmount: 1000, releasedItemCount: 1, activeTotalsByCurrency: [{ currency: 'AOA', amount: 600 }], releasedTotalsByCurrency: [{ currency: 'AOA', amount: 400 }] });
    expect(single).toHaveLength(1);
    expect(single[0]).toMatch(/^Aprovado \(histórico\): .*000,00 AOA · Ativo: 600,00 AOA · Libertado: 400,00 AOA$/);
    const multi = batchTotalsLines({ approvedTotalAmount: null, releasedItemCount: 2, activeTotalsByCurrency: [{ currency: 'AOA', amount: 100 }, { currency: 'EUR', amount: 50 }], releasedTotalsByCurrency: [{ currency: 'EUR', amount: 25 }] });
    expect(multi).toHaveLength(2);
    expect(multi[0]).toContain('100,00 AOA');
    expect(multi[0]).toContain('Libertado: 0,00 AOA');
    expect(multi[1]).toContain('Aprovado (histórico): 75,00 EUR');
    expect(multi[1]).toContain('Libertado: 25,00 EUR');
    expect(multi.join(' ')).not.toContain('175'); // never summed across AOA + EUR
  });
  it('header totals: active per currency; historical only when a cancellation/release happened', () => {
    expect(headerApprovedTotals(null)).toBeNull();
    const clean = headerApprovedTotals({ activeByCurrency: [{ currency: 'AOA', amount: 1500 }], activeAmount: 1500, activeCurrency: 'AOA', historicalApprovedTotalAmount: 1500, historicalApprovedCurrencyCode: 'AOA', cancelledGroupCount: 0, releasedItemCount: 0 });
    expect(clean!.active).toMatch(/500,00 AOA$/);
    expect(clean!.historical).toBeUndefined();
    const afterCancel = headerApprovedTotals({ activeByCurrency: [{ currency: 'AOA', amount: 900 }, { currency: 'USD', amount: 10 }], activeAmount: null, activeCurrency: 'MULTIPLE', historicalApprovedTotalAmount: 1500, historicalApprovedCurrencyCode: 'AOA', cancelledGroupCount: 1, releasedItemCount: 0 });
    expect(afterCancel!.active).toContain('900,00 AOA');
    expect(afterCancel!.active).toContain('10,00 USD');
    expect(afterCancel!.active).toContain('·');
    expect(afterCancel!.historical).toMatch(/500,00 AOA$/);
    expect(headerApprovedTotals({ activeByCurrency: [], activeAmount: null, activeCurrency: null, historicalApprovedTotalAmount: 300, historicalApprovedCurrencyCode: 'AOA', cancelledGroupCount: 1, releasedItemCount: 1 })!.active).toBe('Sem valor ativo');
  });
  it('header "Cancelar P.O." target: server code gates it; one cancellable group opens directly, several go to the tab', () => {
    const groups = [{ id: 'a', canCancel: true }, { id: 'b', canCancel: false }];
    expect(resolveCancelPoGroupTarget([{ code: 'CANCEL_PO_GROUP', actionable: false }], groups)).toEqual({ kind: 'NONE' });
    expect(resolveCancelPoGroupTarget([{ code: 'SUBMIT_BATCH', actionable: true }], groups)).toEqual({ kind: 'NONE' });
    expect(resolveCancelPoGroupTarget([{ code: 'CANCEL_PO_GROUP', actionable: true }], groups)).toEqual({ kind: 'SINGLE', groupId: 'a' });
    expect(resolveCancelPoGroupTarget([{ code: 'CANCEL_PO_GROUP', actionable: true }], [{ id: 'a', canCancel: true }, { id: 'b', canCancel: true }])).toEqual({ kind: 'MULTIPLE' });
    expect(resolveCancelPoGroupTarget([{ code: 'CANCEL_PO_GROUP', actionable: true }], [])).toEqual({ kind: 'MULTIPLE' });
  });
});

// ── v2.249.4 — cancelled group card: historical lines instead of "Itens: —" ──
import { groupLineNumbersLabel as _lines } from './buyerWorkspaceView';
describe('groupLineNumbersLabel (v2.249.4)', () => {
    it('live lines as before; a cancelled RESTART group names the released lines as history; CANCEL_ITEMS keeps live lines labelled as history', () => {
        expect(_lines([1, 3])).toBe('Itens: #1, #3');
        expect(_lines([], [1], true)).toBe('Itens (histórico, libertados): #1');
        expect(_lines([2], [], true)).toBe('Itens (histórico): #2');
        expect(_lines([], [], true)).toBe('Itens: —');
        expect(_lines([], [1], false)).toBe('Itens: —');
    });
});
