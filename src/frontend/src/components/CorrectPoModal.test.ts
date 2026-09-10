import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level guards for the v2.242.0 CorrectPoModal group-context fix (no RTL).
import src from './CorrectPoModal.tsx?raw';

describe('CorrectPoModal — group-scoped expected context (v2.242.0)', () => {
  it('resolves the selected PO group by poGroupId', () => {
    expect(src).toMatch(/requestData\?\.poGroups\?\.find\(g => g\.id === poGroupId\)/);
  });

  it('reads expected amount/supplier/currency from the GROUP, not request scalars', () => {
    expect(src).toMatch(/resolveExpectedTotalAmount\(selectedGroup\?\.totalAmount\)/);
    expect(src).toMatch(/resolveExpectedSupplierName\(selectedGroup\?\.supplierNameSnapshot\)/);
    expect(src).toMatch(/selectedGroup\?\.currencyCode \|\| 'AOA'/);
    // The old request-scalar sources must be gone for the expected values.
    expect(src).not.toMatch(/resolveExpectedTotalAmount\(requestData\?\.estimatedTotalAmount\)/);
    expect(src).not.toMatch(/resolveExpectedSupplierName\(requestData\?\.supplierName\)/);
  });

  it('fails safe when the group cannot be resolved (no silent 0 / "Não definido")', () => {
    expect(src).toMatch(/const groupMissing = !loading && !!requestData && !!poGroupId && !selectedGroup/);
    // A dedicated error branch + guards on OCR and submit.
    expect(src).toMatch(/Não foi possível carregar os dados do grupo P\.O\. selecionado/);
    expect(src).toMatch(/if \(groupMissing\) return;/);           // OCR guard
    expect(src).toMatch(/if \(groupMissing\) \{/);                // submit guard
  });

  it('forwards the actual OCR extraction to the backend for authoritative recomputation', () => {
    expect(src).toMatch(/extractedTotalAmount: ocrResult\?\.extractedTotal/);
    expect(src).toMatch(/extractedSupplierName: ocrResult\?\.extractedSupplier/);
  });
});

describe('CorrectPoModal — Finance return reason UX (v2.242.0)', () => {
  it('parses the return comment via the shared helper (message above technical context)', () => {
    expect(src).toMatch(/parseFinanceReturnComment/);
    expect(src).toMatch(/Mensagem de Finanças/);
    // The prominent text is the parsed message, not the raw concatenated comment.
    expect(src).toMatch(/\{parsed\.message\}/);
    expect(src).not.toMatch(/"\{returnReason\}"/);
  });

  it('keeps actor/date as secondary context, below the message', () => {
    expect(src).toMatch(/Devolvido por \{returnActor/);
  });

  it('collapses the technical context behind "Ver detalhes técnicos"', () => {
    expect(src).toMatch(/Ver detalhes técnicos/);
    expect(src).toMatch(/showReturnTechnical/);
    expect(src).toMatch(/\{parsed\.technical\}/);
  });

  it('selects the return event for THIS PO group (no sibling-group reason)', () => {
    expect(src).toMatch(/\.includes\(poGroupId\)/);
  });
});
