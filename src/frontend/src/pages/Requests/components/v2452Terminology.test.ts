import { describe, it, expect } from 'vitest';
import { humanizeActionLabel, humanizeDocumentType } from './print/requestPrintModel';
import edit from '../RequestEdit.tsx?raw';
import panel from './RequestStatusActionPanels.tsx?raw';

// v2.245.2 — canonical terminology (print history/doc labels) + finalize gating source guards.
describe('print history labels — no raw internal codes leak', () => {
  it('CONFIRM_RECEIVING uses "Recebimento confirmado" (not "Recepção confirmada")', () => {
    expect(humanizeActionLabel('CONFIRM_RECEIVING')).toBe('Recebimento confirmado');
  });
  it('RECEIVING_PROGRESS and OPERATIONAL_RECEIPT_COMPLETED are humanized, not raw', () => {
    expect(humanizeActionLabel('RECEIVING_PROGRESS')).toBe('Acompanhamento do recebimento');
    expect(humanizeActionLabel('OPERATIONAL_RECEIPT_COMPLETED')).toBe('Recebimento operacional concluído');
    expect(humanizeActionLabel('RECEIVING_PROGRESS')).not.toMatch(/Receiving progress/i);
  });
  it('repair + divergence codes are humanized', () => {
    expect(humanizeActionLabel('RECEIVING_LINKAGE_REPAIR')).toMatch(/vínculo de recebimento/);
    expect(humanizeActionLabel('RECEIVING_PAYMENT_GROUP_SYNC_REPAIR')).toMatch(/recebimento \(pagamento\)/);
    expect(humanizeActionLabel('PAYMENT_DIVERGENCE_DETECTED')).toBe('Divergência de pagamento');
  });
});

describe('print document-type labels — the three concepts are distinct', () => {
  it('payment proof, supplier receipt, fiscal receipt and receiving evidence are all different', () => {
    const paymentProof = humanizeDocumentType('PAYMENT_PROOF');
    const receipt = humanizeDocumentType('RECEIPT');
    const fiscal = humanizeDocumentType('FISCAL_RECEIPT');
    const evidence = humanizeDocumentType('RECEIVING_EVIDENCE');
    expect(paymentProof).toBe('Comprovativo de Pagamento');
    expect(receipt).toBe('Recibo do Fornecedor');
    expect(fiscal).toBe('Recibo Fiscal');
    expect(evidence).toBe('Comprovativo de Recebimento/Execução');
    // all distinct
    expect(new Set([paymentProof, receipt, fiscal, evidence]).size).toBe(4);
  });
});

describe('Request Details header + finalize gating (source guards)', () => {
  it('header badge uses the canonical status label override, not the raw persisted name', () => {
    expect(edit).toMatch(/canonicalStatusLabel\(status, statusFullName\)/);
    expect(edit).toMatch(/from '\.\.\/\.\.\/lib\/statusLabels'/);
  });
  it('Finalize is gated on WAITING_RECEIPT scalar AND at least one group AND every active group ready', () => {
    expect(panel).toMatch(/status === 'WAITING_RECEIPT'/);
    // v2.245.2: require at least one group (groupless legacy requests are NOT finalizable from the UI)
    expect(panel).toMatch(/!!poGroups\?\.length && poGroups\.every\(g => g\.status === 'WAITING_RECEIPT' \|\| g\.status === 'COMPLETED'\)/);
    // the old groupless-permissive form is gone
    expect(panel).not.toMatch(/!poGroups\?\.length \|\| poGroups\.every/);
  });
  it('finalize label uses "Recibo do Fornecedor"', () => {
    expect(panel).toMatch(/FINALIZAR PEDIDO \(Recibo do Fornecedor\)/);
  });
});
