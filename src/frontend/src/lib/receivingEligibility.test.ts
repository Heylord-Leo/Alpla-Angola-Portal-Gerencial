import { describe, it, expect } from 'vitest';
import {
  isReceivingActionableGroupStatus,
  RECEIVING_ACTIONABLE_GROUP_STATUSES,
  RECEIVING_PHASE_BLOCKER,
  canConfirmReceiving,
  isReceivingConfirmed,
  isPreConfirmReceivingStatus,
} from './receivingEligibility';

// v2.245.0 §16/§23 — the canonical frontend eligibility rule must mirror the backend evaluator exactly
// (ReceivingActionEvaluator.ActionableStatuses). Pure unit tests (node-env vitest).
describe('receivingEligibility — canonical group-status rule', () => {
  it('A–D: the four canonical backend-valid statuses are actionable', () => {
    for (const s of ['PAYMENT_COMPLETED', 'WAITING_RECEIPT', 'IN_FOLLOWUP', 'WAITING_SUPPLIER_DELIVERY']) {
      expect(isReceivingActionableGroupStatus(s)).toBe(true);
    }
  });

  it('E: PENDING is NOT actionable (the PAYMENT drift) and must stay invalid', () => {
    expect(isReceivingActionableGroupStatus('PENDING')).toBe(false);
    expect(RECEIVING_ACTIONABLE_GROUP_STATUSES as readonly string[]).not.toContain('PENDING');
  });

  it('other non-receiving group statuses are not actionable', () => {
    for (const s of ['WAITING_PO', 'PO_ISSUED', 'PAYMENT_SCHEDULED', 'COMPLETED', 'CANCELLED']) {
      expect(isReceivingActionableGroupStatus(s)).toBe(false);
    }
  });

  it('null / undefined / empty are not actionable', () => {
    expect(isReceivingActionableGroupStatus(null)).toBe(false);
    expect(isReceivingActionableGroupStatus(undefined)).toBe(false);
    expect(isReceivingActionableGroupStatus('')).toBe(false);
  });

  it('G: a repaired group (PENDING → PAYMENT_COMPLETED) becomes actionable', () => {
    expect(isReceivingActionableGroupStatus('PENDING')).toBe(false);
    expect(isReceivingActionableGroupStatus('PAYMENT_COMPLETED')).toBe(true);
  });

  it('legacy display aliases keep working (never PENDING)', () => {
    expect(isReceivingActionableGroupStatus('PAG_REALIZADO')).toBe(true);
    expect(isReceivingActionableGroupStatus('AG_RECIBO')).toBe(true);
  });

  it('exposes a human-readable phase blocker message', () => {
    expect(RECEIVING_PHASE_BLOCKER).toMatch(/fase válida de recebimento/);
  });
});

// v2.245.0 duplicate-confirm fix — the CONFIRM action is one-time and distinct from receiving access.
describe('receivingEligibility — canConfirmReceiving (duplicate-confirm fix)', () => {
  it('A: PAYMENT_COMPLETED + incomplete items → NOT confirmable (pending)', () => {
    expect(canConfirmReceiving('PAYMENT_COMPLETED', false)).toBe(false);
  });

  it('B: PAYMENT_COMPLETED + all items received → confirmable', () => {
    expect(canConfirmReceiving('PAYMENT_COMPLETED', true)).toBe(true);
  });

  it('C: IN_FOLLOWUP + all items received → confirmable', () => {
    expect(canConfirmReceiving('IN_FOLLOWUP', true)).toBe(true);
  });

  it('D: WAITING_RECEIPT (already confirmed) → NEVER confirmable, even with all received', () => {
    expect(canConfirmReceiving('WAITING_RECEIPT', true)).toBe(false);
    expect(isReceivingConfirmed('WAITING_RECEIPT')).toBe(true);
  });

  it('WAITING_SUPPLIER_DELIVERY + all received → confirmable', () => {
    expect(canConfirmReceiving('WAITING_SUPPLIER_DELIVERY', true)).toBe(true);
  });

  it('post-confirmation states are confirmed, pre-confirmation are not', () => {
    for (const s of ['WAITING_RECEIPT', 'WAITING_FISCAL_RECEIPT', 'COMPLETED']) {
      expect(isReceivingConfirmed(s)).toBe(true);
      expect(isPreConfirmReceivingStatus(s)).toBe(false);
    }
    for (const s of ['PAYMENT_COMPLETED', 'IN_FOLLOWUP', 'WAITING_SUPPLIER_DELIVERY']) {
      expect(isReceivingConfirmed(s)).toBe(false);
      expect(isPreConfirmReceivingStatus(s)).toBe(true);
    }
  });

  it('the confirm action set never contains WAITING_RECEIPT', () => {
    expect(canConfirmReceiving('PENDING', true)).toBe(false);
    expect(isPreConfirmReceivingStatus('WAITING_RECEIPT')).toBe(false);
  });
});
