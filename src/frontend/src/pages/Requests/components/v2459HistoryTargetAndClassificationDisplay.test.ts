import { describe, it, expect } from 'vitest';
// v2.245.9 — structural guards for the two TEST inconsistencies:
//   1. GROUP_COMPLETED history rows: the drawer and the print render the backend-resolved target
//      (newStatusName) and never re-map it locally.
//   2. "Tipo de Documento Anexado: NÃO CLASSIFICADO": the request-level declaration is relabelled or
//      suppressed once operational groups exist; the group card shows the authoritative classification.
// The repository's test policy is node-env vitest without jsdom/RTL, hence source-level anchors
// (behavior is unit-tested in src/lib/requestDocumentTypeDisplay.test.ts and print/requestPrintModel.test.ts).
import requestEdit from '../RequestEdit.tsx?raw';
import generalSection from './RequestGeneralDataSection.tsx?raw';
import invoiceSection from './OperationInvoiceSection.tsx?raw';
import docTypeField from '../../../components/requests/SourceDocumentTypeField.tsx?raw';
import printModel from './print/requestPrintModel.ts?raw';

describe('history target rendering (issue 1)', () => {
  it('the drawer renders the backend-provided resulting status of every row, without a local status remap', () => {
    expect(requestEdit).toMatch(/➡ \{entry\.newStatusName\}/);
    expect(requestEdit).not.toMatch(/GROUP_COMPLETED/);          // no client-side special-casing
    expect(requestEdit).not.toMatch(/newStatusName\s*===/);
  });

  it('the print maps newStatusName straight through and labels the completion events in PT', () => {
    expect(printModel).toMatch(/newStatus: h\.newStatusName \|\| ''/);
    expect(printModel).toMatch(/GROUP_COMPLETED: 'Grupo concluído'/);
    expect(printModel).toMatch(/REQUEST_COMPLETED: 'Pedido finalizado'/);
    expect(printModel).toMatch(/FISCAL_RECEIPT_UNLOCKED: 'Recibo Fiscal desbloqueado'/);
  });
});

describe('request-level declaration vs group classification (issue 2)', () => {
  it('RequestEdit tells the header whether operational groups exist', () => {
    expect(requestEdit).toMatch(/hasOperationalGroups=\{\(poGroups\?\.length \?\? 0\) > 0\}/);
  });

  it('the header resolves the request-level field through the display rule and suppresses it when hidden', () => {
    expect(generalSection).toMatch(/import \{ resolveRequestLevelDocumentTypeDisplay \} from '\.\.\/\.\.\/\.\.\/lib\/requestDocumentTypeDisplay'/);
    expect(generalSection).toMatch(/resolveRequestLevelDocumentTypeDisplay\(\{\s*status, hasOperationalGroups, value: formData\.sourceDocumentType\s*\}\)/);
    expect(generalSection).toMatch(/requestLevelDocumentType\.mode !== 'hidden' && \(/);
    // relabelled + explained when a creation-time declaration exists next to operational groups
    expect(generalSection).toMatch(/readOnlyLabel=\{requestLevelDocumentType\.mode === 'declared' \? requestLevelDocumentType\.label : undefined\}/);
    expect(generalSection).toMatch(/readOnlyHint=\{requestLevelDocumentType\.mode === 'declared' \? requestLevelDocumentType\.hint : undefined\}/);
    // the editable DRAFT path is untouched: same value/onChange/required wiring
    expect(generalSection).toMatch(/readOnly=\{status !== 'DRAFT'\}/);
    expect(generalSection).toMatch(/required=\{featureFlags\?\.sourceDocumentTypeRequired\}/);
  });

  it('the read-only field accepts the override label/hint and keeps the default wording otherwise', () => {
    expect(docTypeField).toMatch(/\{readOnlyLabel \?\? 'Tipo de documento anexado'\}/);
    expect(docTypeField).toMatch(/data-testid="source-document-type-hint"/);
    // the editable label is unchanged
    expect(docTypeField).toMatch(/Tipo de documento anexado \{required && <span style=\{\{ color: 'red' \}\}>\*<\/span>\}/);
  });

  it('the coverage card shows the group\'s OWN classification from the authoritative group field, per group, never a request-level value', () => {
    expect(invoiceSection).toMatch(/import \{ documentTypeLabel \} from '\.\.\/\.\.\/\.\.\/lib\/sourceDocumentType'/);
    expect(invoiceSection).toMatch(/data-testid="group-source-document"/);
    expect(invoiceSection).toMatch(/\{documentTypeLabel\(obligation\.sourceDocumentType\)\}/);
    expect(invoiceSection).toMatch(/\{!classificationPending && \(\s*<div data-testid="group-source-document"/);
    expect(invoiceSection).not.toMatch(/request\.sourceDocumentType|formData\.sourceDocumentType/);
  });
});
