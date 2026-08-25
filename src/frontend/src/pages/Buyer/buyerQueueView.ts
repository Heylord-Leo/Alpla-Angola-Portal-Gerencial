// ─────────────────────────────────────────────────────────────────────────────
// Pure, framework-free view logic for the new Buyer operational queue (/buyer/items).
// Extracted so KPI-card→filter mapping, sort options, clear-filter keys, advanced-filter
// counting, note-tooltip resolution and operational-state presentation can be unit-tested
// WITHOUT a component-rendering runner (mirrors financePaymentsView.ts). Contains NO React and
// NO server-workflow derivation — the operational state/labels come from the server projection.
// ─────────────────────────────────────────────────────────────────────────────
import type { BuyerQueueItem } from '../../types/buyerQueue';

// Server operational-state codes (mirror BuyerQueueConstants.OperationalStates).
export const OP = {
  NeedsQuotation: 'NEEDS_QUOTATION',
  PartialCoverage: 'PARTIAL_COVERAGE',
  ReadyForApproval: 'READY_FOR_APPROVAL',
  AwaitingApproval: 'AWAITING_APPROVAL',
  AdjustmentRequired: 'ADJUSTMENT_REQUIRED',
  AwaitingRequesterDecision: 'AWAITING_REQUESTER_DECISION',
  CompletedForBuyer: 'COMPLETED_FOR_BUYER',
  NoBuyerAction: 'NO_BUYER_ACTION',
} as const;

// ── KPI cards ── The five approved work-queue cards. `id` is the URL card key; `summaryKey`
// reads the count from /buyer/queue/summary; `apply` maps a selected card to queue list filters.
export type CardFilter = { operationalState?: string; priority?: string };
export interface QueueCardDef {
  id: string;
  title: string;
  summaryKey: 'total' | 'requiresAttention' | 'needsAction' | 'awaitingApproval' | 'unassigned' | 'byState';
  byStateCode?: string;
  color: string;
  apply: CardFilter; // list-narrowing filter when this card is selected (empty for "all")
}

export const QUEUE_CARDS: QueueCardDef[] = [
  { id: 'all', title: 'Todos os Pedidos', summaryKey: 'total', color: 'var(--color-status-slate)', apply: {} },
  { id: 'needs_quotation', title: 'Sem Cotação', summaryKey: 'byState', byStateCode: OP.NeedsQuotation, color: 'var(--color-status-blue)', apply: { operationalState: OP.NeedsQuotation } },
  { id: 'partial', title: 'Cobertura Parcial', summaryKey: 'byState', byStateCode: OP.PartialCoverage, color: 'var(--color-status-orange)', apply: { operationalState: OP.PartialCoverage } },
  { id: 'awaiting', title: 'Em Aprovação', summaryKey: 'awaitingApproval', color: 'var(--color-status-indigo)', apply: { operationalState: OP.AwaitingApproval } },
  { id: 'attention', title: 'Requer Atenção', summaryKey: 'requiresAttention', color: 'var(--color-status-red)', apply: { priority: 'EXCEPTION_OR_OVERDUE' } },
];

export function cardCount(card: QueueCardDef, summary: {
  total: number; requiresAttention: number; needsAction: number; awaitingApproval: number;
  unassigned: number; byOperationalState: Record<string, number>;
} | null): number {
  if (!summary) return 0;
  switch (card.summaryKey) {
    case 'total': return summary.total;
    case 'requiresAttention': return summary.requiresAttention;
    case 'needsAction': return summary.needsAction;
    case 'awaitingApproval': return summary.awaitingApproval;
    case 'unassigned': return summary.unassigned;
    case 'byState': return summary.byOperationalState?.[card.byStateCode ?? ''] ?? 0;
  }
}

// The URL card key derived from the current operationalState/priority filters (so the selected card
// highlights correctly on reload). Returns 'all' when no card-owned filter is active.
export function activeCardId(operationalState: string | null, priority: string | null): string {
  if (priority === 'EXCEPTION_OR_OVERDUE' && !operationalState) return 'attention';
  const byState = QUEUE_CARDS.find(c => c.apply.operationalState && c.apply.operationalState === operationalState);
  return byState ? byState.id : 'all';
}

// ── Sort ──
export const QUEUE_SORT_OPTIONS = [
  { value: 'priority', label: 'Prioridade operacional' },
  { value: 'deadline', label: 'Data necessária — mais próxima' },
  { value: 'created', label: 'Mais recentes' },
  { value: 'created_asc', label: 'Mais antigos' },
];
export const QUEUE_DEFAULT_SORT = 'priority';

// ── Ownership tabs ──
export const OWNERSHIP_TABS = [
  { id: 'all', label: 'Todos' },
  { id: 'me', label: 'Meus Pedidos' },
  { id: 'unassigned', label: 'Não Atribuídos' },
];
export const DEFAULT_OWNERSHIP = 'all';

// ── Clear-filters / advanced-filter counting ──
// Keys the "Limpar filtros" action removes. Ownership + card selection are NOT cleared here
// (ownership is a tab; the card is reset separately to 'all').
export const QUEUE_CLEAR_KEYS = [
  'search', 'sort', 'company', 'plant', 'department', 'needLevel', 'deadline',
  'operationalState', 'priority', 'card', 'includeCompleted', 'page',
];

// Advanced filters that light the "Mais filtros (N)" badge. Search, sort, ownership and the KPI
// card selection are deliberately excluded (they have their own affordances).
export function countAdvancedFilters(p: {
  company?: string | null; plant?: string | null; department?: string | null; needLevel?: string | null;
  deadline?: string | null; includeCompleted?: boolean;
}): number {
  let n = 0;
  if (p.company) n++;
  if (p.plant) n++;
  if (p.department) n++;
  if (p.needLevel && p.needLevel !== NEED_LEVEL_ALL) n++; // 'ALL' (Todos) is the no-need-filter state
  if (p.deadline) n++;
  if (p.includeCompleted) n++;
  return n;
}

// ── Need-level default filter (Phase 3E.2) ──
// The Buyer Queue opens on the CRITICAL need level so critical purchase requests are the initial work
// view. This is a real filter (server-side), not a sort. "Todos" is an explicit sentinel so it survives
// in the URL and is not re-defaulted; an absent param means a fresh load → the product default.
export const NEED_LEVEL_DEFAULT = 'CRITICO';
export const NEED_LEVEL_ALL = 'ALL';

/** Effective need-level filter from the URL param: absent/empty → product default (Crítico). */
export function resolveNeedLevel(param: string | null | undefined): string {
  return param && param.length > 0 ? param : NEED_LEVEL_DEFAULT;
}

/** Value sent to the API: a specific need-level code, or undefined for "all need levels" (Todos). */
export function needLevelApiValue(needLevel: string): string | undefined {
  return needLevel === NEED_LEVEL_ALL ? undefined : needLevel;
}

/** Canonical ownership test — the row belongs to the current buyer (never inferred from display name). */
export function isOwnRequest(buyerId: string | null | undefined, currentUserId: string | null | undefined): boolean {
  return !!buyerId && !!currentUserId && buyerId === currentUserId;
}

// Company→Plant dependency: when the company changes, a plant selection that does not belong to the
// new company must be cleared atomically. Returns the plant id to keep, or null to clear. Pure so the
// dependency rule is unit-testable (mirrors Finance's atomic company/plant param update).
export function resolvePlantOnCompanyChange(currentPlantId: string | null, plantsOfNewCompany: { id: number }[]): string | null {
  if (!currentPlantId) return null;
  return plantsOfNewCompany.some(p => String(p.id) === currentPlantId) ? currentPlantId : null;
}

// ── Note tooltip (latest note + "+N earlier") ──
export interface NoteTooltip { title: string; body: string; extra: string | null; }
export function resolveNoteTooltip(item: Pick<BuyerQueueItem, 'hasNotes' | 'noteCount' | 'latestNoteText'>): NoteTooltip | null {
  if (!item.hasNotes || !item.latestNoteText) return null;
  const count = item.noteCount ?? 1;
  const earlier = count - 1;
  return {
    title: count > 1 ? 'Última observação' : 'Observação',
    body: item.latestNoteText,
    extra: earlier > 0 ? `+${earlier} ${earlier === 1 ? 'observação anterior' : 'observações anteriores'}` : null,
  };
}

// ── Operational-state presentation ── color token per state. Red is reserved for true attention
// (adjustment/overdue), never for every actionable request.
export function operationalStateColor(item: Pick<BuyerQueueItem, 'operationalState' | 'requiresAttention'>): string {
  if (item.requiresAttention) return 'var(--color-status-red)';
  switch (item.operationalState) {
    case OP.NeedsQuotation: return 'var(--color-status-blue)';
    case OP.PartialCoverage: return 'var(--color-status-orange)';
    case OP.ReadyForApproval: return 'var(--color-status-green)';
    case OP.AwaitingApproval: return 'var(--color-status-indigo)';
    case OP.AdjustmentRequired: return 'var(--color-status-red)';
    case OP.AwaitingRequesterDecision: return 'var(--color-status-purple)';
    default: return 'var(--color-status-gray)';
  }
}

// Deadline chip label + whether it is an urgent (red/orange) condition. Need level and deadline are
// SEPARATE dimensions — this covers only the deadline.
export function deadlineChip(item: Pick<BuyerQueueItem, 'deadlineCondition'>): { label: string; color: string } | null {
  switch (item.deadlineCondition) {
    case 'OVERDUE': return { label: 'Vencido', color: 'var(--color-status-red)' };
    case 'DUE_TODAY': return { label: 'Vence hoje', color: 'var(--color-status-orange)' };
    case 'APPROACHING': return { label: 'Prazo próximo', color: 'var(--color-status-amber)' };
    default: return null; // WITHIN_DEADLINE / NONE → no chip
  }
}

export const NEED_LEVEL_LABEL: Record<string, string> = {
  CRITICO: 'Crítico', URGENTE: 'Urgente', NORMAL: 'Normal', BAIXO: 'Baixo',
};

// ── Coverage mini-bar ── "treated" = the server covered count (approved + in-batch + ready +
// closed + not-quoted-accepted). It NEVER implies "approved" — the label must say "tratados".
export function coverageProgress(covered: number, total: number, segments = 8): { filled: number; segments: number; pct: number } {
  if (total <= 0) return { filled: 0, segments, pct: 0 };
  const ratio = Math.max(0, Math.min(1, covered / total));
  return {
    // Guarantee ≥1 filled cell when any progress exists, and never fully filled while pending remains.
    filled: covered <= 0 ? 0 : covered >= total ? segments : Math.max(1, Math.min(segments - 1, Math.round(ratio * segments))),
    segments,
    pct: Math.round(ratio * 100),
  };
}

// Share of a summary total, for the subtle KPI subtitle. Null when the base is 0 (not reliable).
export function pctOfTotal(count: number, total: number): number | null {
  if (!total || total <= 0) return null;
  return Math.round((count / total) * 100);
}
