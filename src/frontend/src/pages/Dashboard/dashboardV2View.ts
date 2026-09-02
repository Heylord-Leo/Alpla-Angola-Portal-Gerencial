// Dashboard V2 — pure presentation helpers (slice B1+B2). These derive ONLY display conveniences
// (a combined "Atenção" count = overdue + critical-today, and empty-state flags). They must NOT
// recompute any Buyer workflow metric — every count comes from the server (DashboardV2BuyerSectionDto).
// Node-vitest friendly (no DOM).

import type {
  BuyerWorkloadRowDto,
  BuyerPersonalSummaryDto,
  BuyerSharedQueueSummaryDto,
} from '../../types/dashboardV2';

/** Compact "Atenção" for a workload row = overdue actionable + critical-today actionable.
 * A summary of two server values for one column; no recomputation of urgency itself. */
export function workloadAttention(row: Pick<BuyerWorkloadRowDto, 'overdueActionableRequests' | 'criticalActionableRequests'>): number {
  return (row.overdueActionableRequests || 0) + (row.criticalActionableRequests || 0);
}

/** Same combined attention for the personal summary. */
export function personalAttention(p: Pick<BuyerPersonalSummaryDto, 'overdueActionableRequests' | 'criticalActionableRequests'>): number {
  return (p.overdueActionableRequests || 0) + (p.criticalActionableRequests || 0);
}

/** The personal (PESSOAL) card is meaningful only when the user actually has assigned buyer work. */
export function hasPersonalWork(p: BuyerPersonalSummaryDto | null | undefined): boolean {
  return !!p && p.assignedRequests > 0;
}

/** The shared (COMPARTILHADO) card is meaningful only when the unassigned pool is non-empty. */
export function hasSharedWork(s: BuyerSharedQueueSummaryDto | null | undefined): boolean {
  return !!s && s.unassignedRequests > 0;
}

/** Sorted workload rows for display: assigned buyers by actionable desc; the UNASSIGNED row is
 * NEVER inside this list — the caller pins it separately (it is a shared bucket, not a person). */
export function displayWorkloadRows(rows: BuyerWorkloadRowDto[] | null | undefined): BuyerWorkloadRowDto[] {
  return (rows || []).filter((r) => !r.isUnassigned);
}

// ── Buyer drill-down route (single source of truth) ──
// The Buyer operational screen ("Gestão de Cotações", BuyerQueueList) is mounted at /buyer/items
// (see App.tsx). All Dashboard V2 Buyer drill-downs MUST target this canonical route — never invent
// a new one. The page consumes ?buyer=<guid> and ?ownership=unassigned from the URL.
export const BUYER_QUEUE_ROUTE = '/buyer/items';

export function buyerQueueHref(opts: { buyerId?: string; ownership?: 'me' | 'unassigned' } = {}): string {
  const p = new URLSearchParams();
  if (opts.buyerId) p.set('buyer', opts.buyerId);
  if (opts.ownership) p.set('ownership', opts.ownership);
  const qs = p.toString();
  return qs ? `${BUYER_QUEUE_ROUTE}?${qs}` : BUYER_QUEUE_ROUTE;
}

// ── In-cell column bars (each column has its OWN scale) ──

export type WorkloadMetricKey = 'assigned' | 'actionable' | 'pending' | 'ready' | 'attention';

/** The numeric value a workload row contributes to a given column. Reads server values only
 * (attention is the overdue+critical display sum from workloadAttention). */
export function workloadValue(row: BuyerWorkloadRowDto, key: WorkloadMetricKey): number {
  switch (key) {
    case 'assigned': return row.assignedRequests;
    case 'actionable': return row.actionableRequests;
    case 'pending': return row.pendingQuotationItems;
    case 'ready': return row.readyForBatchItems;
    case 'attention': return workloadAttention(row);
  }
}

/** Per-column maxima across ALL displayed rows (pass buyer rows AND the pinned unassigned row).
 * Each column is scaled independently — one metric's magnitude never scales another's bar. */
export function workloadColumnMaxes(rows: BuyerWorkloadRowDto[]): Record<WorkloadMetricKey, number> {
  const keys: WorkloadMetricKey[] = ['assigned', 'actionable', 'pending', 'ready', 'attention'];
  const maxes = { assigned: 0, actionable: 0, pending: 0, ready: 0, attention: 0 } as Record<WorkloadMetricKey, number>;
  for (const r of rows) for (const k of keys) maxes[k] = Math.max(maxes[k], workloadValue(r, k));
  return maxes;
}

/** Bar width % for a cell within its own column. Zero (or an empty column) => 0 (no visible bar). */
export function barPercent(value: number, columnMax: number): number {
  if (value <= 0 || columnMax <= 0) return 0;
  return Math.round((value / columnMax) * 100);
}
