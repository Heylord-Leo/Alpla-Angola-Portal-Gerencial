import { describe, it, expect } from 'vitest';
import {
  WORKSPACE_TABS, DEFAULT_TAB, resolveTab, backToQueueTarget, coverageChips,
  formatCurrency, formatTotalsByCurrency, metricOrAbsent, bucketLabel, batchKindLabel, supplierStatusLabel,
  lotLineNumbersLabel, lotItemCountLabel, stepStateMeta, clampIndex, formatLotAmount,
  timelineStepTimestamp,
} from './buyerWorkspaceView';

describe('tab routing (refresh-safe)', () => {
  it('exposes the three approved tabs, default = items, no History tab', () => {
    expect(WORKSPACE_TABS.map(t => t.id)).toEqual(['items', 'quotes', 'batches']);
    expect(WORKSPACE_TABS.map(t => t.label)).toEqual(['Itens & Cobertura', 'Cotações & Documentos', 'Lotes & Aprovações']);
    expect(DEFAULT_TAB).toBe('items');
    expect(WORKSPACE_TABS.some(t => /hist/i.test(t.id) || /hist/i.test(t.label))).toBe(false);
  });
  it('resolves a valid tab and falls back to default for unknown/absent', () => {
    expect(resolveTab('quotes')).toBe('quotes');
    expect(resolveTab('batches')).toBe('batches');
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
