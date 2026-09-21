import { describe, it, expect } from 'vitest';
import {
  isReceivingActionableGroupStatus,
  RECEIVING_ACTIONABLE_GROUP_STATUSES,
  RECEIVING_PHASE_BLOCKER,
  canConfirmReceiving,
  isReceivingConfirmed,
  isPreConfirmReceivingStatus,
  canRegisterItemReceipt,
  canReopenReceiving,
  receivingItemActionLabel,
  userCanReopenReceiving,
  canShowReopenReceiving,
} from './receivingEligibility';
import { ROLES } from '../constants/roles';

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

// v2.245.5 — registration/correction is PRE-confirmation only; reopen is the audited way back.
describe('receivingEligibility — v2.245.5 registration, correction and reopen', () => {
  it('canRegisterItemReceipt: pre-confirmation statuses only (mirror of CanRegisterItemReceipt)', () => {
    for (const s of ['PAYMENT_COMPLETED', 'IN_FOLLOWUP', 'WAITING_SUPPLIER_DELIVERY']) {
      expect(canRegisterItemReceipt(s)).toBe(true);
    }
    for (const s of ['WAITING_RECEIPT', 'WAITING_FISCAL_RECEIPT', 'COMPLETED', 'PENDING', 'WAITING_PO', '', null, undefined]) {
      expect(canRegisterItemReceipt(s)).toBe(false);
    }
  });

  it('WAITING_RECEIPT is still receiving-accessible (queue) but NOT registrable — the v2.245.4 gap', () => {
    expect(isReceivingActionableGroupStatus('WAITING_RECEIPT')).toBe(true);
    expect(canRegisterItemReceipt('WAITING_RECEIPT')).toBe(false);
  });

  it('receivingItemActionLabel: pending → REGISTRAR, received → AJUSTAR (pre-confirmation only)', () => {
    expect(receivingItemActionLabel('PAYMENT_COMPLETED', 0, false)).toBe('REGISTRAR');
    expect(receivingItemActionLabel('IN_FOLLOWUP', 0, false)).toBe('REGISTRAR');
    expect(receivingItemActionLabel('PAYMENT_COMPLETED', 1, false)).toBe('AJUSTAR');
    expect(receivingItemActionLabel('IN_FOLLOWUP', 2, false)).toBe('AJUSTAR');
    expect(receivingItemActionLabel('IN_FOLLOWUP', null, false)).toBe('REGISTRAR');
  });

  it('receivingItemActionLabel: WAITING_RECEIPT / COMPLETED NEVER render REGISTRAR or AJUSTAR', () => {
    for (const s of ['WAITING_RECEIPT', 'WAITING_FISCAL_RECEIPT', 'COMPLETED']) {
      for (const qty of [0, 1, 2]) {
        expect(receivingItemActionLabel(s, qty, false)).toBe('VER DETALHES');
      }
    }
  });

  it('receivingItemActionLabel: a read-only view is always VER DETALHES', () => {
    expect(receivingItemActionLabel('PAYMENT_COMPLETED', 0, true)).toBe('VER DETALHES');
    expect(receivingItemActionLabel('IN_FOLLOWUP', 2, true)).toBe('VER DETALHES');
  });

  it('canReopenReceiving: only a confirmed, not-yet-finalized group', () => {
    expect(canReopenReceiving('WAITING_RECEIPT')).toBe(true);
    for (const s of ['PAYMENT_COMPLETED', 'IN_FOLLOWUP', 'WAITING_SUPPLIER_DELIVERY', 'WAITING_FISCAL_RECEIPT', 'COMPLETED', 'PENDING', null, undefined]) {
      expect(canReopenReceiving(s)).toBe(false);
    }
  });

  it('userCanReopenReceiving: Receiving or System Administrator only', () => {
    expect(userCanReopenReceiving([ROLES.RECEIVING])).toBe(true);
    expect(userCanReopenReceiving([ROLES.SYSTEM_ADMINISTRATOR])).toBe(true);
    expect(userCanReopenReceiving([ROLES.BUYER, ROLES.RECEIVING])).toBe(true);
    expect(userCanReopenReceiving([ROLES.BUYER])).toBe(false);
    expect(userCanReopenReceiving([ROLES.FINANCE])).toBe(false);
    expect(userCanReopenReceiving([])).toBe(false);
    expect(userCanReopenReceiving(null)).toBe(false);
    expect(userCanReopenReceiving(undefined)).toBe(false);
  });

  it('canShowReopenReceiving: eligible WAITING_RECEIPT + authorized user, never read-only / COMPLETED / unauthorized', () => {
    expect(canShowReopenReceiving('WAITING_RECEIPT', [ROLES.RECEIVING], false)).toBe(true);
    expect(canShowReopenReceiving('WAITING_RECEIPT', [ROLES.SYSTEM_ADMINISTRATOR], false)).toBe(true);
    expect(canShowReopenReceiving('WAITING_RECEIPT', [ROLES.BUYER], false)).toBe(false);
    expect(canShowReopenReceiving('WAITING_RECEIPT', [ROLES.RECEIVING], true)).toBe(false);
    expect(canShowReopenReceiving('COMPLETED', [ROLES.RECEIVING], false)).toBe(false);
    expect(canShowReopenReceiving('IN_FOLLOWUP', [ROLES.RECEIVING], false)).toBe(false);
    expect(canShowReopenReceiving('PAYMENT_COMPLETED', [ROLES.SYSTEM_ADMINISTRATOR], false)).toBe(false);
  });

  it('after a reopen (group back to IN_FOLLOWUP) corrections are exposed and confirm requires completeness', () => {
    expect(canRegisterItemReceipt('IN_FOLLOWUP')).toBe(true);
    expect(receivingItemActionLabel('IN_FOLLOWUP', 2, false)).toBe('AJUSTAR');
    expect(canConfirmReceiving('IN_FOLLOWUP', false)).toBe(false); // corrected incomplete → no confirm
    expect(canConfirmReceiving('IN_FOLLOWUP', true)).toBe(true);   // corrected complete → confirm again
    expect(canShowReopenReceiving('IN_FOLLOWUP', [ROLES.RECEIVING], false)).toBe(false); // no reopen while open
  });
});
