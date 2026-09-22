import { describe, it, expect } from 'vitest';
// v2.245.0 §15/§16/§23 — source guards (node-env vitest) proving the operation route derives receiving
// eligibility from the canonical GROUP-status helper and never exposes an action the backend rejects.
import op from './ReceivingOperation.tsx?raw';

describe('ReceivingOperation — canonical eligibility wiring (§16)', () => {
  it('imports the shared canonical helper (no ad-hoc status whitelist)', () => {
    expect(op).toMatch(/isReceivingActionableGroupStatus/);
    expect(op).toMatch(/RECEIVING_PHASE_BLOCKER/);
    // the old inline whitelist must be gone
    expect(op).not.toMatch(/\['WAITING_RECEIPT', 'IN_FOLLOWUP', 'PAYMENT_COMPLETED', 'WAITING_SUPPLIER_DELIVERY', 'PAG_REALIZADO', 'RECEBIMENTO_ANDAMENTO'\]/);
  });

  it('A–D/E: group read-only is driven by the canonical helper on the GROUP status', () => {
    expect(op).toMatch(/const groupActionable = isReceivingActionableGroupStatus\(group\.status\)/);
    expect(op).toMatch(/const isGroupReadOnly = isReadOnly \|\| !groupActionable/);
  });
});

describe('ReceivingOperation — invalid-phase blocker (§15 F)', () => {
  it('F: renders a read-only blocker for a group that is not in a valid receiving phase', () => {
    expect(op).toMatch(/const showPhaseBlocker = !isReadOnly && !groupActionable/);
    expect(op).toMatch(/showPhaseBlocker && \(/);
    expect(op).toMatch(/Status atual do grupo:/);
  });

  it('H: the item-receipt modal is read-only when the item\'s group is not actionable', () => {
    // v2.245.5: the modal gate is the PRE-confirmation registration rule (canRegisterItemReceipt), which is a
    // strict subset of the general receiving-access rule — a confirmed (WAITING_RECEIPT) group is read-only.
    expect(op).toMatch(/canRegisterItemReceipt\(selectedGroup\.status\)/);
    expect(op).toMatch(/readOnly=\{selectedItemReadOnly\}/);
  });

  it('the group confirm button still only renders for actionable groups', () => {
    expect(op).toMatch(/!isGroupReadOnly &&/);
    expect(op).toMatch(/CONFIRMAR RECEBIMENTO/);
  });
});

describe('ReceivingOperation — duplicate-confirm fix (§11/§12 H,I)', () => {
  it('the confirm button is gated by the dedicated one-time canConfirmReceiving rule', () => {
    expect(op).toMatch(/const showConfirmButton = !isReadOnly && canConfirmReceiving\(group\.status, groupAllReceived\)/);
    // the button is wrapped in showConfirmButton, not merely !isGroupReadOnly
    expect(op).toMatch(/\{showConfirmButton && \(/);
  });

  it('H: after confirmation (WAITING_RECEIPT) the confirm button disappears — driven by isReceivingConfirmed', () => {
    expect(op).toMatch(/const groupConfirmed = isReceivingConfirmed\(group\.status\)/);
    // canConfirmReceiving returns false for a confirmed group, so the button will not render
    expect(op).toMatch(/canConfirmReceiving\(group\.status, groupAllReceived\)/);
  });

  it('§11/I: the "confirme o recebimento" message shows only while pending; confirmed shows next guidance', () => {
    expect(op).toMatch(/groupConfirmed\s*\n?\s*\? 'Recebimento confirmado\. Anexar recibo do fornecedor e finalizar pedido\.'/);
    expect(op).toMatch(/Recebimento completo — confirme o recebimento\./);
  });

  it('the top-level (no-groups) confirm action is gated by canConfirmReceiving too', () => {
    expect(op).toMatch(/canConfirmReceiving\(request\.statusCode, allReceived\)/);
  });
});
