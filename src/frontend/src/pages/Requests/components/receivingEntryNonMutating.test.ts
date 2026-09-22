import { describe, it, expect } from 'vitest';
// v2.245.1 — the Request Details "enter receiving" action must be a NON-MUTATING navigation into the
// receiving operation, never the legacy move-to-receipt mutation that prematurely set WAITING_RECEIPT.
// Node-env vitest source guards (no jsdom/RTL).
import panel from './RequestStatusActionPanels.tsx?raw';
import hook from '../hooks/useRequestDetail.ts?raw';
import api from '../../../lib/api.ts?raw';
import modal from '../../../components/ApprovalModal.tsx?raw';

describe('Request Details receiving entry — non-mutating navigation (§5.A)', () => {
  it('the receiving action navigates to the receiving operation (no business-state mutation)', () => {
    expect(panel).toMatch(/navigate\(`\/receiving\/operation\/\$\{requestId\}`\)/);
    // label describes entering/conducting receiving, not awaiting supplier receipt
    expect(panel).toMatch(/INICIAR RECEBIMENTO/);
    expect(panel).not.toMatch(/MOVER PARA RECEBIMENTO/);
  });

  it('the receiving action does NOT open the legacy MOVE_TO_RECEIPT modal', () => {
    expect(panel).not.toMatch(/type: 'MOVE_TO_RECEIPT'/);
  });

  it('useRequestDetail no longer has a MOVE_TO_RECEIPT mutation handler', () => {
    expect(hook).not.toMatch(/action === 'MOVE_TO_RECEIPT'/);
    expect(hook).not.toMatch(/api\.requests\.moveToReceipt/);
  });

  it('the api client no longer exposes the move-to-receipt wrapper/endpoint', () => {
    expect(api).not.toMatch(/moveToReceipt:/);
    expect(api).not.toMatch(/operational\/move-to-receipt/);
  });

  it('the misleading "aguardando recibo" move modal is gone', () => {
    expect(modal).not.toMatch(/'MOVE_TO_RECEIPT'/);
    expect(modal).not.toMatch(/Deseja mover este pedido para a fase de aguardando recibo/);
  });
});
