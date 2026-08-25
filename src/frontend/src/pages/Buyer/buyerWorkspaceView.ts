// ─────────────────────────────────────────────────────────────────────────────
// Pure, framework-free view logic for the Buyer Request Workspace (Phase 3A). Extracted so tab
// resolution, back-navigation target, supplier per-currency formatting, coverage presentation and
// empty-state text can be unit-tested without a component runner (mirrors buyerQueueView.ts).
// ─────────────────────────────────────────────────────────────────────────────
import type { BuyerWorkspaceSupplier, CurrencyAmount } from '../../types/buyerWorkspace';

export const WORKSPACE_TABS = [
  { id: 'items', label: 'Itens & Cobertura' },
  { id: 'quotes', label: 'Cotações & Documentos' },
  { id: 'batches', label: 'Lotes & Aprovações' },
];
export const DEFAULT_TAB = 'items';

/** Refresh-safe tab resolution from the URL `tab` param; unknown/absent → the default tab. */
export function resolveTab(param: string | null | undefined): string {
  return WORKSPACE_TABS.some(t => t.id === param) ? (param as string) : DEFAULT_TAB;
}

/**
 * The queue URL to return to. Preference order: the explicit origin captured in navigation state
 * when the user entered the Workspace (preserves page/ownership/search/filters/card/sort), else the
 * bare queue. Any non-/buyer/items origin is ignored (defensive).
 */
export function backToQueueTarget(fromState: unknown): string {
  if (typeof fromState === 'string' && fromState.startsWith('/buyer/items')) return fromState;
  return '/buyer/items';
}

// ── Coverage buckets (canonical) → PT labels for the Workspace ──
export const COVERAGE_BUCKET_LABEL: Record<string, string> = {
  APPROVED: 'Aprovado',
  IN_ACTIVE_BATCH: 'Em lote ativo',
  QUOTED_READY_FOR_BATCH: 'Pronto para lote',
  CLOSED_NOT_QUOTED: 'Encerrado sem cotação',
  NOT_QUOTED_PROPOSED: 'Proposto não cotado',
  NOT_QUOTED_ACCEPTED: 'Aceite não cotado',
  CANCELLED_DELETED: 'Cancelado',
  PENDING_QUOTATION: 'Pendente de cotação',
};

export function bucketLabel(code: string): string {
  return COVERAGE_BUCKET_LABEL[code] ?? code;
}

// Coverage chips for the summary — legacy not-quoted states appear ONLY when present (count > 0).
export interface CoverageChip { key: string; label: string; value: number; alwaysShow: boolean; }
export function coverageChips(c: {
  totalItems: number; treated: number; pending: number; approved: number; inActiveBatch: number;
  readyForBatch: number; closedNotQuoted: number; notQuotedProposed: number; notQuotedAccepted: number;
}): CoverageChip[] {
  const chips: CoverageChip[] = [
    { key: 'total', label: 'Total de itens', value: c.totalItems, alwaysShow: true },
    { key: 'treated', label: 'Tratados', value: c.treated, alwaysShow: true },
    { key: 'pending', label: 'Pendentes', value: c.pending, alwaysShow: true },
    { key: 'approved', label: 'Aprovados', value: c.approved, alwaysShow: true },
    { key: 'inBatch', label: 'Em lote ativo', value: c.inActiveBatch, alwaysShow: true },
    { key: 'ready', label: 'Prontos para lote', value: c.readyForBatch, alwaysShow: true },
    { key: 'closed', label: 'Encerrados sem cotação', value: c.closedNotQuoted, alwaysShow: true },
    { key: 'nqProposed', label: 'Propostos não cotados', value: c.notQuotedProposed, alwaysShow: false },
    { key: 'nqAccepted', label: 'Aceites não cotados', value: c.notQuotedAccepted, alwaysShow: false },
  ];
  return chips.filter(ch => ch.alwaysShow || ch.value > 0);
}

// ── Per-currency formatting (NEVER sum across currencies) ──
export function formatCurrency(amount: number, currency: string): string {
  const n = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
  return `${n} ${currency}`;
}

export function formatTotalsByCurrency(totals: CurrencyAmount[]): string {
  if (!totals || totals.length === 0) return '—';
  return totals.map(t => formatCurrency(t.amount, t.currency)).join(' · ');
}

// A neutral absence label where a zero would mislead (e.g. no purchase history yet).
export function metricOrAbsent(value: number, unit?: string): string {
  if (value <= 0) return 'Sem histórico';
  return unit ? `${value} ${unit}` : String(value);
}

// Batch kind → PT label + whether it is a settled/inactive kind.
export const BATCH_KIND_LABEL: Record<string, string> = {
  ACTIVE: 'Ativo', APPROVED: 'Aprovado', REJECTED: 'Rejeitado', CANCELLED: 'Cancelado', SUPERSEDED: 'Substituído',
};
export function batchKindLabel(kind: string): string {
  return BATCH_KIND_LABEL[kind] ?? kind;
}

export function supplierStatusLabel(s: Pick<BuyerWorkspaceSupplier, 'isActive' | 'registrationStatus'>): string {
  if (!s.isActive) return 'Inativo';
  return s.registrationStatus === 'ACTIVE' ? 'Ativo' : (s.registrationStatus ?? 'Ativo');
}

// ── Lot line numbers wording — "2" is a line/item identifier, not a count ──
export function lotLineNumbersLabel(lineNumbers: number[]): string {
  if (!lineNumbers || lineNumbers.length === 0) return 'Itens incluídos: —';
  return 'Itens incluídos: ' + lineNumbers.map(n => `#${n}`).join(', ');
}

export function lotItemCountLabel(count: number): string {
  return `${count} ${count === 1 ? 'item' : 'itens'}`;
}

// ── Vertical lot timeline: step-state → presentation (colors + skipped wording) ──
export type TimelineState = 'completed' | 'current' | 'pending' | 'blocked' | 'skipped';
export interface StepStateMeta { color: string; muted: boolean; note?: string; }
export function stepStateMeta(state: string): StepStateMeta {
  switch (state as TimelineState) {
    case 'completed': return { color: 'var(--color-status-green)', muted: false };
    case 'current': return { color: 'var(--color-primary)', muted: false };
    case 'blocked': return { color: 'var(--color-status-red)', muted: false };
    case 'skipped': return { color: 'var(--color-text-muted)', muted: true, note: 'Não aplicável' };
    default: return { color: 'var(--color-text-muted)', muted: true }; // pending / future
  }
}

// Timeline step timestamp presentation (Phase 3E.2). Disambiguates the previously-overloaded "—":
//  • a real recorded timestamp → the date;
//  • a REACHED step (completed/current) with no recorded timestamp → "Data não registada" (never a fake
//    date — the backend attaches a timestamp only when a direct event recorded the transition);
//  • a FUTURE step → "Ainda não iniciado"; blocked/skipped keep their own labels.
// Returns { date } to format, or { text } to show verbatim.
export function timelineStepTimestamp(state: string, completedAt?: string | null): { date?: string; text?: string } {
  if (state === 'skipped') return { text: 'Não aplicável' };
  if (state === 'blocked') return { text: 'Requer ação' };
  if (completedAt) return { date: completedAt };
  if (state === 'completed' || state === 'current') return { text: 'Data não registada' };
  return { text: 'Ainda não iniciado' }; // pending / future
}

// Carousel index clamp (wrap-free): keeps the active slide within [0, total-1].
export function clampIndex(index: number, total: number): number {
  if (total <= 0) return 0;
  return Math.max(0, Math.min(index, total - 1));
}

// Lot header amount, per currency (never combined with other lots).
export function formatLotAmount(amount: number, currency?: string | null): string {
  const n = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
  return currency ? `${n} ${currency}` : n;
}
