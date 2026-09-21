import { describe, it, expect } from 'vitest';
import { buildRequestPrintModel, humanizeActionLabel, humanizeDocumentType, normalizeActionCode, toPrintFileTitle, safeRequestNumber } from './requestPrintModel';
import type { RequestDetailsDto } from '../../../../types';

// v2.245.0 Request Print View — Phase 1. Pure model coverage (§27).

function baseDetail(over: Partial<RequestDetailsDto> = {}): RequestDetailsDto {
  return {
    id: 'r1', requestNumber: 'REQ-01/09/2026-355', title: 'Compra de EPIs',
    statusName: 'Aprovado', displayStatusName: null, requestTypeName: 'Pagamento',
    requesterName: 'Rui Solicitante', buyerName: null, departmentName: 'Produção',
    companyName: 'ALPLA Angola', plantName: 'Luanda', needLevelName: 'Normal',
    estimatedTotalAmount: 125000, currencyCode: 'AOA',
    createdAtUtc: '2026-09-01T08:00:00Z', needByDateUtc: '2026-09-20T00:00:00Z',
    description: 'Justificação da compra.',
    lineItems: [], attachments: [], quotations: [], poGroups: [], approvalBatches: [], statusHistory: [],
    ...(over as any),
  } as any as RequestDetailsDto;
}

describe('buildRequestPrintModel (§27)', () => {
  it('A: maps general request fields', () => {
    const m = buildRequestPrintModel(baseDetail());
    const labels = m.general.map(f => f.label);
    expect(m.header.requestNumber).toBe('REQ-01/09/2026-355');
    expect(m.header.title).toBe('Compra de EPIs');
    expect(labels).toContain('Solicitante');
    expect(m.general.find(f => f.label === 'Solicitante')!.value).toBe('Rui Solicitante');
    expect(labels).toContain('Departamento');
    expect(m.description).toBe('Justificação da compra.');
  });

  it('B: omits empty optional fields (no null/undefined placeholders)', () => {
    const m = buildRequestPrintModel(baseDetail({ buyerName: null, description: '   ' }));
    expect(m.general.find(f => f.label === 'Comprador')).toBeUndefined();
    expect(m.description).toBeNull();
    // No value should be the literal "null"/"undefined".
    expect(m.general.every(f => !/^(null|undefined)$/i.test(f.value))).toBe(true);
  });

  it('C/D: represents ALL poGroups (5-group fixture → 5 groups, no slice)', () => {
    const poGroups = Array.from({ length: 5 }, (_, i) => ({
      id: `g${i}`, requestId: 'r1', supplierNameSnapshot: `Fornecedor ${i}`, totalAmount: 1000 * (i + 1),
      currencyCode: 'AOA', status: 'APPROVED', lineItemCount: 2, attachmentCount: 0, payments: [],
    }));
    const m = buildRequestPrintModel(baseDetail({ poGroups: poGroups as any }));
    expect(m.groups).toHaveLength(5);
    expect(m.groups[4].title).toContain('Fornecedor 4');
  });

  it('E: represents ALL line items', () => {
    const lineItems = Array.from({ length: 7 }, (_, i) => ({ lineNumber: i + 1, description: `Item ${i}`, quantity: i + 1, totalPrice: 10 }));
    const m = buildRequestPrintModel(baseDetail({ lineItems: lineItems as any }));
    expect(m.lineItems).toHaveLength(7);
  });

  it('F: maps attachment metadata (no binaries)', () => {
    const attachments = [{ id: 'a1', fileName: 'fatura.pdf', fileExtension: 'pdf', attachmentTypeCode: 'INVOICE', uploadedAtUtc: '2026-09-02T10:00:00Z', uploadedByName: 'Ana' }];
    const m = buildRequestPrintModel(baseDetail({ attachments: attachments as any }));
    expect(m.attachments).toHaveLength(1);
    const labels = m.attachments[0].fields.map(f => f.label);
    expect(labels).toContain('Documento');
    expect(labels).toContain('Tipo');
    expect(labels).toContain('Enviado por');
  });

  it('G/H: represents the COMPLETE history, not truncated', () => {
    const statusHistory = Array.from({ length: 50 }, (_, i) => ({
      id: `h${i}`, actionTaken: 'ACTION', newStatusName: 'Estado', actorName: 'X',
      comment: `c${i}`, createdAtUtc: new Date(2026, 7, 1, 0, i).toISOString(), fieldChanges: [],
    }));
    const m = buildRequestPrintModel(baseDetail({ statusHistory: statusHistory as any }));
    expect(m.history).toHaveLength(50);
  });

  it('I: history is oldest→newest and the source array is NOT mutated', () => {
    const statusHistory = [
      { id: 'c', actionTaken: 'C', newStatusName: 's', actorName: 'x', createdAtUtc: '2026-08-03T00:00:00Z', fieldChanges: [] },
      { id: 'a', actionTaken: 'A', newStatusName: 's', actorName: 'x', createdAtUtc: '2026-08-01T00:00:00Z', fieldChanges: [] },
      { id: 'b', actionTaken: 'B', newStatusName: 's', actorName: 'x', createdAtUtc: '2026-08-02T00:00:00Z', fieldChanges: [] },
    ];
    const original = [...statusHistory];
    const m = buildRequestPrintModel(baseDetail({ statusHistory: statusHistory as any }));
    expect(m.history.map(h => h.actionCode)).toEqual(['A', 'B', 'C']); // oldest → newest
    expect(statusHistory).toEqual(original); // input untouched
  });

  it('J: multiple currencies are NOT summed into one total', () => {
    const poGroups = [
      { id: 'g0', requestId: 'r1', supplierNameSnapshot: 'A', totalAmount: 100, currencyCode: 'AOA', status: 'X', lineItemCount: 1, attachmentCount: 0, payments: [] },
      { id: 'g1', requestId: 'r1', supplierNameSnapshot: 'B', totalAmount: 200, currencyCode: 'USD', status: 'X', lineItemCount: 1, attachmentCount: 0, payments: [] },
    ];
    const m = buildRequestPrintModel(baseDetail({ poGroups: poGroups as any }));
    // Each group keeps its own currency-formatted amount; the model exposes no cross-group total field.
    expect((m as any).totalAmount).toBeUndefined();
    expect((m as any).grandTotal).toBeUndefined();
    expect(m.groups[0].fields.some(f => f.label === 'Montante')).toBe(true);
    expect(m.groups[1].fields.some(f => f.label === 'Montante')).toBe(true);
  });

  it('K: a group without payments produces no payment rows (no fabricated receiving/payment)', () => {
    const poGroups = [{ id: 'g0', requestId: 'r1', supplierNameSnapshot: 'A', totalAmount: 100, currencyCode: 'AOA', status: 'WAITING_RECEIPT', lineItemCount: 1, attachmentCount: 0, payments: [] }];
    const m = buildRequestPrintModel(baseDetail({ poGroups: poGroups as any }));
    expect(m.groups[0].payments).toHaveLength(0);
  });

  it('L: advance % and payments shown only when present', () => {
    const poGroups = [
      { id: 'g0', requestId: 'r1', supplierNameSnapshot: 'A', totalAmount: 100, currencyCode: 'AOA', status: 'X', advancePaymentPercent: 30, lineItemCount: 1, attachmentCount: 0,
        payments: [{ id: 1, paymentType: 'ADVANCE', paymentStatus: 'PAID', plannedAmount: 30, actualPaidAmount: 30, scheduledDateUtc: null, paidDateUtc: '2026-09-05T00:00:00Z', currencyCode: 'AOA', hasDivergence: false }] },
      { id: 'g1', requestId: 'r1', supplierNameSnapshot: 'B', totalAmount: 200, currencyCode: 'AOA', status: 'X', advancePaymentPercent: null, lineItemCount: 1, attachmentCount: 0, payments: [] },
    ];
    const m = buildRequestPrintModel(baseDetail({ poGroups: poGroups as any }));
    expect(m.groups[0].fields.some(f => f.label === 'Adiantamento (%)')).toBe(true);
    expect(m.groups[0].payments).toHaveLength(1);
    expect(m.groups[1].fields.some(f => f.label === 'Adiantamento (%)')).toBe(false);
    expect(m.groups[1].payments).toHaveLength(0);
  });

  // ── Phase 2 §18: human-readable labels ──
  it('A: known history action maps to a PT label', () => {
    expect(humanizeActionLabel('SUBMIT')).toBe('Pedido submetido');
    expect(humanizeActionLabel('FINANCE_RETURN_ADJUSTMENT')).toBe('Devolvido por Finanças para correção');
    expect(humanizeActionLabel('REREGISTER_PO')).toBe('P.O. corrigida e registrada');
  });
  it('B: unknown action is preserved safely (normalized, never hidden)', () => {
    expect(humanizeActionLabel('SOME_NEW_CODE')).toBe('Some new code');
    expect(normalizeActionCode('SOME_NEW_CODE')).toBe('Some new code');
    expect(humanizeActionLabel(null)).toBe('—');
  });
  it('C: the raw technical code stays available on each history entry', () => {
    const statusHistory = [{ id: 'h', actionTaken: 'REGISTER_PO', newStatusName: 'P.O. Emitida', actorName: 'X', createdAtUtc: '2026-08-01T00:00:00Z', fieldChanges: [] }];
    const m = buildRequestPrintModel(baseDetail({ statusHistory: statusHistory as any }));
    expect(m.history[0].actionLabel).toBe('P.O. registrada');
    expect(m.history[0].actionCode).toBe('REGISTER_PO');
  });
  it('v2.245.6: RECEIVING_REOPENED renders its label and the backend-provided resulting status (never a local guess)', () => {
    const statusHistory = [
      { id: 'h1', actionTaken: 'RECEIVING_REOPENED', newStatusName: 'Em Acompanhamento', actorName: 'X', createdAtUtc: '2026-09-21T10:00:00Z',
        comment: '[Grupo P.O.: F | GroupId: 12345678] Recebimento reaberto para correção (WAITING_RECEIPT → IN_FOLLOWUP). Motivo: x', fieldChanges: [] },
      { id: 'h2', actionTaken: 'STATUS_SYNC', newStatusName: 'Em Acompanhamento', actorName: 'X', createdAtUtc: '2026-09-21T10:00:01Z', fieldChanges: [] },
    ];
    const m = buildRequestPrintModel(baseDetail({ statusHistory: statusHistory as any }));
    expect(m.history[0].actionLabel).toBe('Recebimento reaberto para correção');
    expect(m.history[0].newStatus).toBe('Em Acompanhamento');
    expect(m.history[0].newStatus).not.toBe('Aguardando Recibo');
    expect(m.history[1].actionLabel).toBe('Sincronização de estado');
    expect(m.history[1].newStatus).toBe('Em Acompanhamento');
  });
  it('D: known document type gets a human label', () => {
    expect(humanizeDocumentType('PAYMENT_SOURCE_DOCUMENT')).toBe('Documento de origem do pagamento');
    expect(humanizeDocumentType('QUOTATION')).toBe('Cotação');
    expect(humanizeDocumentType('INVOICE')).toBe('Fatura');
    const attachments = [{ id: 'a', fileName: 'x.pdf', fileExtension: 'pdf', attachmentTypeCode: 'PAYMENT_SCHEDULE', uploadedAtUtc: '2026-09-02T10:00:00Z', uploadedByName: 'Ana' }];
    const m = buildRequestPrintModel(baseDetail({ attachments: attachments as any }));
    expect(m.attachments[0].fields.find(f => f.label === 'Tipo')!.value).toBe('Cronograma de pagamento');
  });
  it('E: unknown document type is preserved unchanged', () => {
    expect(humanizeDocumentType('WEIRD_TYPE')).toBe('WEIRD_TYPE');
  });

  // ── Print metadata & dynamic title ──
  it('A: the authenticated user display name reaches print metadata (printedBy)', () => {
    const m = buildRequestPrintModel(baseDetail(), null, 'Leonardo Cintra');
    expect(m.header.printedBy).toBe('Leonardo Cintra');
  });
  it('printedBy is null when no user name is supplied (no fabrication)', () => {
    expect(buildRequestPrintModel(baseDetail()).header.printedBy).toBeNull();
    expect(buildRequestPrintModel(baseDetail(), null, '   ').header.printedBy).toBeNull();
  });
  it('C/E: dynamic title contains the request number and a YYYY-MM-DD date', () => {
    const title = toPrintFileTitle('REQ-07/09/2026-377', new Date(2026, 8, 14));
    expect(title).toBe('Portal Gerencial - Pedido REQ-07-09-2026-377 - 2026-09-14');
    expect(title).toMatch(/\d{4}-\d{2}-\d{2}$/);
  });
  it('D: filename-safe request number replaces "/" (and other unsafe chars)', () => {
    expect(safeRequestNumber('REQ-07/09/2026-377')).toBe('REQ-07-09-2026-377');
    expect(safeRequestNumber('A/B:C*D?')).toBe('A-B-C-D-');
    expect(safeRequestNumber(null)).toBe('Pedido');
  });
  it('I: the business request number inside the document is unchanged (only the filename is sanitized)', () => {
    const m = buildRequestPrintModel(baseDetail({ requestNumber: 'REQ-07/09/2026-377' }));
    expect(m.header.requestNumber).toBe('REQ-07/09/2026-377'); // slashes intact in the document
    expect(toPrintFileTitle('REQ-07/09/2026-377', new Date(2026, 8, 14))).toContain('REQ-07-09-2026-377');
  });

  it('empty request omits optional sections (no groups/items/attachments/history/approvals)', () => {
    const m = buildRequestPrintModel(baseDetail());
    expect(m.groups).toHaveLength(0);
    expect(m.lineItems).toHaveLength(0);
    expect(m.attachments).toHaveLength(0);
    expect(m.history).toHaveLength(0);
    expect(m.approvals).toHaveLength(0);
  });
});
