import { describe, it, expect } from 'vitest';
import {
  QUEUE_CARDS, cardCount, activeCardId, countAdvancedFilters, resolveNoteTooltip,
  operationalStateColor, deadlineChip, QUEUE_CLEAR_KEYS, QUEUE_SORT_OPTIONS,
  OWNERSHIP_TABS, DEFAULT_OWNERSHIP, QUEUE_DEFAULT_SORT, OP,
  coverageProgress, pctOfTotal, resolvePlantOnCompanyChange,
  resolveNeedLevel, needLevelApiValue, isOwnRequest, NEED_LEVEL_DEFAULT, NEED_LEVEL_ALL,
} from './buyerQueueView';

const summary = {
  total: 52, requiresAttention: 34, needsAction: 40, awaitingApproval: 11, unassigned: 15,
  byOperationalState: { NEEDS_QUOTATION: 32, PARTIAL_COVERAGE: 3, AWAITING_APPROVAL: 11 },
};

describe('KPI cards', () => {
  it('exposes exactly the five approved cards in order', () => {
    expect(QUEUE_CARDS.map(c => c.id)).toEqual(['all', 'needs_quotation', 'partial', 'awaiting', 'attention']);
    expect(QUEUE_CARDS.map(c => c.title)).toEqual([
      'Todos os Pedidos', 'Sem Cotação', 'Cobertura Parcial', 'Em Aprovação', 'Requer Atenção',
    ]);
  });

  it('reads card counts ONLY from the summary (never a page subset)', () => {
    const byId = (id: string) => cardCount(QUEUE_CARDS.find(c => c.id === id)!, summary);
    expect(byId('all')).toBe(52);
    expect(byId('needs_quotation')).toBe(32);   // byState
    expect(byId('partial')).toBe(3);            // byState
    expect(byId('awaiting')).toBe(11);          // awaitingApproval
    expect(byId('attention')).toBe(34);         // requiresAttention
  });

  it('returns 0 for every card when summary is null', () => {
    for (const c of QUEUE_CARDS) expect(cardCount(c, null)).toBe(0);
  });

  it('selecting a card maps to a list filter but the count source is unchanged', () => {
    // The card only carries a list-narrowing filter; it never re-scopes the summary.
    expect(QUEUE_CARDS.find(c => c.id === 'needs_quotation')!.apply).toEqual({ operationalState: OP.NeedsQuotation });
    expect(QUEUE_CARDS.find(c => c.id === 'attention')!.apply).toEqual({ priority: 'EXCEPTION_OR_OVERDUE' });
    expect(QUEUE_CARDS.find(c => c.id === 'all')!.apply).toEqual({});
  });

  it('derives the active card id from the current filter params', () => {
    expect(activeCardId(null, null)).toBe('all');
    expect(activeCardId(OP.NeedsQuotation, null)).toBe('needs_quotation');
    expect(activeCardId(OP.PartialCoverage, null)).toBe('partial');
    expect(activeCardId(null, 'EXCEPTION_OR_OVERDUE')).toBe('attention');
  });
});

describe('ownership + sort', () => {
  it('has the three approved ownership tabs with server semantics', () => {
    expect(OWNERSHIP_TABS.map(t => t.id)).toEqual(['all', 'me', 'unassigned']);
    expect(OWNERSHIP_TABS.map(t => t.label)).toEqual(['Todos', 'Meus Pedidos', 'Não Atribuídos']);
    expect(DEFAULT_OWNERSHIP).toBe('all');
  });

  it('defaults sort to operational priority and offers deadline/newest/oldest', () => {
    expect(QUEUE_DEFAULT_SORT).toBe('priority');
    expect(QUEUE_SORT_OPTIONS.map(o => o.value)).toEqual(['priority', 'deadline', 'created', 'created_asc']);
  });
});

describe('advanced-filter counting + clear keys', () => {
  it('counts only advanced filters (not search/sort/ownership/card) — company included', () => {
    expect(countAdvancedFilters({})).toBe(0);
    expect(countAdvancedFilters({ company: '1' })).toBe(1);
    expect(countAdvancedFilters({ company: '1', plant: '1', department: '2', needLevel: 'CRITICO', deadline: 'OVERDUE', includeCompleted: true })).toBe(6);
  });

  it('clear keys include company + every filter param and page, and reset sort too', () => {
    expect(QUEUE_CLEAR_KEYS).toContain('sort');
    expect(QUEUE_CLEAR_KEYS).toContain('page');
    expect(QUEUE_CLEAR_KEYS).toContain('company');
    for (const k of ['search', 'plant', 'department', 'needLevel', 'deadline', 'operationalState', 'priority', 'card', 'includeCompleted']) {
      expect(QUEUE_CLEAR_KEYS).toContain(k);
    }
  });
});

describe('Company → Plant dependency (atomic clear)', () => {
  const plantsC1 = [{ id: 1 }, { id: 3 }];
  it('keeps the plant when it belongs to the newly selected company', () => {
    expect(resolvePlantOnCompanyChange('1', plantsC1)).toBe('1');
    expect(resolvePlantOnCompanyChange('3', plantsC1)).toBe('3');
  });
  it('clears the plant when it does not belong to the new company', () => {
    expect(resolvePlantOnCompanyChange('2', plantsC1)).toBeNull(); // plant 2 is another company's
  });
  it('is null-safe when no plant is selected or company cleared', () => {
    expect(resolvePlantOnCompanyChange(null, plantsC1)).toBeNull();
    expect(resolvePlantOnCompanyChange('1', [])).toBeNull(); // "Todas as empresas" → no company plants list
  });
});

describe('note tooltip (zero / one / multiple)', () => {
  it('returns null when there are no notes', () => {
    expect(resolveNoteTooltip({ hasNotes: false, noteCount: 0, latestNoteText: null })).toBeNull();
    expect(resolveNoteTooltip({ hasNotes: true, noteCount: 0, latestNoteText: null })).toBeNull();
  });

  it('single note → "Observação" with no "earlier" line', () => {
    const t = resolveNoteTooltip({ hasNotes: true, noteCount: 1, latestNoteText: 'única' })!;
    expect(t.title).toBe('Observação');
    expect(t.body).toBe('única');
    expect(t.extra).toBeNull();
  });

  it('multiple notes → "Última observação" + pluralized "+N earlier"', () => {
    const two = resolveNoteTooltip({ hasNotes: true, noteCount: 2, latestNoteText: 'recente' })!;
    expect(two.title).toBe('Última observação');
    expect(two.extra).toBe('+1 observação anterior');
    const four = resolveNoteTooltip({ hasNotes: true, noteCount: 4, latestNoteText: 'r' })!;
    expect(four.extra).toBe('+3 observações anteriores');
  });
});

describe('operational-state presentation (red is reserved)', () => {
  it('uses red for attention conditions, never for every actionable state', () => {
    // NEEDS_QUOTATION is actionable but NOT red.
    expect(operationalStateColor({ operationalState: OP.NeedsQuotation, requiresAttention: false })).toBe('var(--color-status-blue)');
    expect(operationalStateColor({ operationalState: OP.ReadyForApproval, requiresAttention: false })).toBe('var(--color-status-green)');
    // Attention (adjustment/overdue) IS red.
    expect(operationalStateColor({ operationalState: OP.AdjustmentRequired, requiresAttention: true })).toBe('var(--color-status-red)');
    expect(operationalStateColor({ operationalState: OP.NeedsQuotation, requiresAttention: true })).toBe('var(--color-status-red)');
  });

  it('deadline chip is separate from need level and only appears for urgent conditions', () => {
    expect(deadlineChip({ deadlineCondition: 'OVERDUE' })!.label).toBe('Vencido');
    expect(deadlineChip({ deadlineCondition: 'DUE_TODAY' })!.label).toBe('Vence hoje');
    expect(deadlineChip({ deadlineCondition: 'APPROACHING' })!.label).toBe('Prazo próximo');
    expect(deadlineChip({ deadlineCondition: 'WITHIN_DEADLINE' })).toBeNull();
    expect(deadlineChip({ deadlineCondition: 'NONE' })).toBeNull();
  });
});

describe('coverage progress + percentage (presentation only)', () => {
  it('maps treated/total to segment fill without implying approval', () => {
    expect(coverageProgress(0, 19)).toMatchObject({ filled: 0, pct: 0 });          // nothing treated
    expect(coverageProgress(6, 8).pct).toBe(75);
    expect(coverageProgress(19, 19)).toMatchObject({ filled: 8, pct: 100 });        // fully treated
    expect(coverageProgress(0, 0)).toMatchObject({ filled: 0, pct: 0 });            // no active items
  });

  it('keeps ≥1 filled cell for partial progress and never fills while pending remains', () => {
    const p = coverageProgress(1, 100); // barely started
    expect(p.filled).toBeGreaterThanOrEqual(1);
    expect(p.filled).toBeLessThan(p.segments);
    const almost = coverageProgress(99, 100); // treated but 1 pending
    expect(almost.filled).toBeLessThan(almost.segments);
  });

  it('pctOfTotal is null when the base is unreliable (0)', () => {
    expect(pctOfTotal(34, 52)).toBe(65);
    expect(pctOfTotal(5, 0)).toBeNull();
    expect(pctOfTotal(0, 10)).toBe(0);
  });
});

describe('no static legacy quotation labels', () => {
  it('never emits "PRECISA COTAR" / "AÇÃO NECESSÁRIA: COTAR"', () => {
    const allText = [
      ...QUEUE_CARDS.map(c => c.title),
      ...OWNERSHIP_TABS.map(t => t.label),
      ...QUEUE_SORT_OPTIONS.map(o => o.label),
    ].join(' | ').toUpperCase();
    expect(allText).not.toContain('PRECISA COTAR');
    expect(allText).not.toContain('AÇÃO NECESSÁRIA');
    expect(allText).not.toContain('COTAR');
  });
});

// ── Phase 3E.2: default need-level filter + own-request indicator ──
describe('need-level default filter', () => {
  it('defaults to CRITICAL when no URL param (fresh queue)', () => {
    expect(resolveNeedLevel(null)).toBe(NEED_LEVEL_DEFAULT);
    expect(resolveNeedLevel(null)).toBe('CRITICO');
    expect(resolveNeedLevel('')).toBe('CRITICO');
  });
  it('respects an explicit need level in the URL', () => {
    expect(resolveNeedLevel('URGENTE')).toBe('URGENTE');
    expect(resolveNeedLevel(NEED_LEVEL_ALL)).toBe('ALL');
  });
  it('sends a specific code to the API but nothing for "Todos"', () => {
    expect(needLevelApiValue('CRITICO')).toBe('CRITICO');
    expect(needLevelApiValue(NEED_LEVEL_ALL)).toBeUndefined();
  });
  it('advanced-filter count includes the default CRITICAL but not Todos', () => {
    expect(countAdvancedFilters({ needLevel: 'CRITICO' })).toBe(1);
    expect(countAdvancedFilters({ needLevel: NEED_LEVEL_ALL })).toBe(0);
    expect(countAdvancedFilters({ needLevel: '' })).toBe(0);
  });
  it('clearing filters (param removed) restores CRITICAL', () => {
    // clearFilters deletes the needLevel param → resolveNeedLevel(null) → default
    expect(resolveNeedLevel(null)).toBe('CRITICO');
  });
});

describe('isOwnRequest (canonical identity, never name)', () => {
  it('true only when the row buyerId matches the current user id', () => {
    expect(isOwnRequest('u1', 'u1')).toBe(true);
    expect(isOwnRequest('u1', 'u2')).toBe(false);
  });
  it('false for unassigned or missing identity', () => {
    expect(isOwnRequest(null, 'u1')).toBe(false);
    expect(isOwnRequest('u1', null)).toBe(false);
    expect(isOwnRequest(undefined, undefined)).toBe(false);
  });
});
