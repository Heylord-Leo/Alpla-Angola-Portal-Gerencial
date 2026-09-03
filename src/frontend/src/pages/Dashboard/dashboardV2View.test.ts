import { describe, it, expect } from 'vitest';
import {
  workloadAttention,
  personalAttention,
  hasPersonalWork,
  hasSharedWork,
  displayWorkloadRows,
  workloadValue,
  workloadColumnMaxes,
  barPercent,
  buyerQueueHref,
  BUYER_QUEUE_ROUTE,
  financePaymentsHref,
  FINANCE_PAYMENTS_ROUTE,
} from './dashboardV2View';
import type { BuyerWorkloadRowDto, BuyerPersonalSummaryDto, BuyerSharedQueueSummaryDto } from '../../types/dashboardV2';

const row = (over: Partial<BuyerWorkloadRowDto> = {}): BuyerWorkloadRowDto => ({
  buyerId: 'b1', buyerName: 'Ana', isUnassigned: false,
  assignedRequests: 0, actionableRequests: 0, pendingQuotationItems: 0, readyForBatchItems: 0,
  needsQuotationRequests: 0, partialCoverageRequests: 0, readyForApprovalRequests: 0,
  adjustmentRequests: 0, overdueActionableRequests: 0, criticalActionableRequests: 0, ...over,
});

describe('dashboardV2View', () => {
  it('workloadAttention = overdue + critical (a display sum, not a recomputation)', () => {
    expect(workloadAttention(row({ overdueActionableRequests: 3, criticalActionableRequests: 2 }))).toBe(5);
    expect(workloadAttention(row())).toBe(0);
  });

  it('personalAttention = overdue + critical', () => {
    const p = { overdueActionableRequests: 1, criticalActionableRequests: 4 } as BuyerPersonalSummaryDto;
    expect(personalAttention(p)).toBe(5);
  });

  it('hasPersonalWork only when assignedRequests > 0', () => {
    expect(hasPersonalWork(null)).toBe(false);
    expect(hasPersonalWork({ assignedRequests: 0 } as BuyerPersonalSummaryDto)).toBe(false);
    expect(hasPersonalWork({ assignedRequests: 1 } as BuyerPersonalSummaryDto)).toBe(true);
  });

  it('hasSharedWork only when unassignedRequests > 0', () => {
    expect(hasSharedWork(null)).toBe(false);
    expect(hasSharedWork({ unassignedRequests: 0 } as BuyerSharedQueueSummaryDto)).toBe(false);
    expect(hasSharedWork({ unassignedRequests: 11 } as BuyerSharedQueueSummaryDto)).toBe(true);
  });

  it('displayWorkloadRows never includes the unassigned bucket (it is a shared pool, not a person)', () => {
    const rows = [row({ buyerId: 'b1' }), { ...row(), isUnassigned: true, buyerId: null, buyerName: null }];
    const out = displayWorkloadRows(rows);
    expect(out).toHaveLength(1);
    expect(out.every((r) => !r.isUnassigned)).toBe(true);
    expect(displayWorkloadRows(null)).toEqual([]);
  });

  it('workloadValue reads the right server field per column (attention = overdue+critical)', () => {
    const r = row({ assignedRequests: 10, actionableRequests: 4, pendingQuotationItems: 34, readyForBatchItems: 7, overdueActionableRequests: 2, criticalActionableRequests: 1 });
    expect(workloadValue(r, 'assigned')).toBe(10);
    expect(workloadValue(r, 'actionable')).toBe(4);
    expect(workloadValue(r, 'pending')).toBe(34);
    expect(workloadValue(r, 'ready')).toBe(7);
    expect(workloadValue(r, 'attention')).toBe(3);
  });

  it('workloadColumnMaxes scales each column independently', () => {
    const rows = [
      row({ assignedRequests: 10, pendingQuotationItems: 5 }),
      row({ assignedRequests: 2, pendingQuotationItems: 34 }),
      row({ assignedRequests: 7, pendingQuotationItems: 12 }),
    ];
    const m = workloadColumnMaxes(rows);
    expect(m.assigned).toBe(10);   // max of the Atribuídos column only
    expect(m.pending).toBe(34);    // max of the Itens pendentes column only — never compared to assigned
  });

  it('buyerQueueHref targets the canonical /buyer/items route (never /buyer/queue)', () => {
    expect(BUYER_QUEUE_ROUTE).toBe('/buyer/items');
    expect(buyerQueueHref({ buyerId: 'abc' })).toBe('/buyer/items?buyer=abc');
    expect(buyerQueueHref({ ownership: 'unassigned' })).toBe('/buyer/items?ownership=unassigned');
    expect(buyerQueueHref({ ownership: 'me' })).toBe('/buyer/items?ownership=me');
    expect(buyerQueueHref()).toBe('/buyer/items');
    expect(buyerQueueHref({ buyerId: 'x' })).not.toContain('/buyer/queue');
  });

  it('financePaymentsHref maps each card to an EXISTING /finance/payments server filter', () => {
    expect(FINANCE_PAYMENTS_ROUTE).toBe('/finance/payments');
    expect(financePaymentsHref('actionable')).toBe('/finance/payments?actionableOnly=true');
    expect(financePaymentsHref('needsScheduling')).toBe('/finance/payments?actionClass=NEEDS_SCHEDULING');
    expect(financePaymentsHref('needsPayment')).toBe('/finance/payments?actionClass=NEEDS_PAYMENT');
    expect(financePaymentsHref('dueToday')).toBe('/finance/payments?dueTodayOnly=true');
    expect(financePaymentsHref('overdue')).toBe('/finance/payments?overdueOnly=true');
    expect(financePaymentsHref('paidWaitingReceiving')).toBe('/finance/payments?actionClass=PAID_WAITING_RECEIVING');
  });

  it('barPercent scales within a column; zero => 0 (no bar); empty column => 0', () => {
    expect(barPercent(10, 10)).toBe(100);
    expect(barPercent(9, 10)).toBe(90);
    expect(barPercent(2, 10)).toBe(20);
    expect(barPercent(0, 10)).toBe(0);   // zero value -> no visible bar
    expect(barPercent(5, 0)).toBe(0);    // empty column -> no bar
  });
});
