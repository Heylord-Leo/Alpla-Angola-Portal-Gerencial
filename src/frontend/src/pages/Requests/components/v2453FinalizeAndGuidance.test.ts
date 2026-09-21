import { describe, it, expect } from 'vitest';
// v2.245.3 — Finance FINALIZE routing + supplier-receipt gating + pre-confirmation guidance. Node-env
// source guards (no jsdom/RTL).
import edit from '../RequestEdit.tsx?raw';
import panel from './RequestStatusActionPanels.tsx?raw';
import hook from '../hooks/useRequestDetail.ts?raw';
import receivingOp from '../../Receiving/ReceivingOperation.tsx?raw';
import attachments from '../../../components/RequestAttachments.tsx?raw';

describe('Finance FINALIZE routing (Defect 1)', () => {
  it('RequestEdit no longer imports or renders the receiving-confirmation modal', () => {
    expect(edit).not.toMatch(/import\s*\{\s*FinalizeReceivingModal\s*\}/);
    expect(edit).not.toMatch(/<FinalizeReceivingModal/);
  });

  it('RequestEdit renders ApprovalModal and no longer branches on type === FINALIZE', () => {
    expect(edit).toMatch(/<ApprovalModal/);
    expect(edit).not.toMatch(/showApprovalModal\.type === 'FINALIZE' \?/);
  });

  it('FINALIZE goes through handleRequestAction → api.requests.finalize, never confirmReceiving', () => {
    expect(hook).toMatch(/action === 'FINALIZE'/);
    expect(hook).toMatch(/api\.requests\.finalize\(id/);
    // the FINALIZE handler must not call the receiving confirmation endpoint
    const finalizeIdx = hook.indexOf("action === 'FINALIZE'");
    const slice = hook.slice(finalizeIdx, finalizeIdx + 200);
    expect(slice).not.toMatch(/confirmReceiving/);
  });

  it('FinalizeReceivingModal remains used by the receiving operation page only', () => {
    expect(receivingOp).toMatch(/FinalizeReceivingModal/);
  });
});

describe('Supplier-receipt gating (Defect 1 UX)', () => {
  it('Finalize is offered only when an active RECEIPT exists; otherwise guides to attach it', () => {
    expect(panel).toMatch(/hasSupplierReceipt \?/);
    expect(panel).toMatch(/FINALIZAR PEDIDO \(Recibo do Fornecedor\)/);
    expect(panel).toMatch(/Anexe o "Recibo do Fornecedor"/);
  });

  it('RequestEdit derives hasSupplierReceipt strictly from the RECEIPT attachment type (active only)', () => {
    expect(edit).toMatch(/attachmentTypeCode === 'RECEIPT' && !a\.isDeleted && !a\.voidedAtUtc/);
    // RECEIVING_EVIDENCE / FISCAL_RECEIPT / PAYMENT_PROOF must not be part of the receipt gate
    expect(edit).not.toMatch(/hasSupplierReceipt[\s\S]{0,120}RECEIVING_EVIDENCE/);
  });

  it('the supplier-receipt upload card is labeled "Recibo do Fornecedor"', () => {
    expect(attachments).toMatch(/'RECEIPT': 'Recibo do Fornecedor'/);
  });
});

describe('Pre-confirmation guidance (Defect 2)', () => {
  it('the panel prefers the projection unit-truth guidance over the raw scalar map', () => {
    expect(panel).toMatch(/singleUnitGuidance \?\? getRequestGuidance\(status \|\| '', requestTypeCode\)/);
  });

  it('RequestEdit passes the projection single-unit guidance to the panel', () => {
    expect(edit).toMatch(/singleUnitGuidance=\{singleUnitGuidance\}/);
  });
});
