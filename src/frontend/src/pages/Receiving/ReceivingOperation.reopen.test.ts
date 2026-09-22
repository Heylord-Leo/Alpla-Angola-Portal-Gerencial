import { describe, it, expect } from 'vitest';
// v2.245.5 — node-env vitest source guards for the post-confirmation correction UX:
// frozen quantities after CONFIRMAR RECEBIMENTO, REGISTRAR/AJUSTAR only pre-confirmation, REABRIR
// RECEBIMENTO for authorized users through a dedicated mandatory-reason modal, and the API wrapper.
// The pure eligibility rules (labels, roles, statuses) are unit-tested in src/lib/receivingEligibility.test.ts.
import op from './ReceivingOperation.tsx?raw';
import modal from '../../components/modals/ReopenReceivingModal.tsx?raw';
import apiSrc from '../../lib/api.ts?raw';
import printModel from '../Requests/components/print/requestPrintModel.ts?raw';

describe('ReceivingOperation — item action is driven by the canonical pre-confirmation rule', () => {
  it('the item button label comes from receivingItemActionLabel(group.status, item.receivedQty, isReadOnly)', () => {
    expect(op).toMatch(/receivingItemActionLabel\(group\.status, item\.receivedQty, isReadOnly\)/);
    // no ad-hoc literal that could leak REGISTRAR into a confirmed group
    expect(op).not.toMatch(/isGroupReadOnly \? 'VER DETALHES' : 'REGISTRAR'/);
  });

  it('the quantity modal is read-only unless the group is registrable (canRegisterItemReceipt)', () => {
    expect(op).toMatch(/const selectedItemReadOnly = isReadOnly \|\| \(!!selectedGroup && !canRegisterItemReceipt\(selectedGroup\.status\)\)/);
    expect(op).toMatch(/readOnly=\{selectedItemReadOnly\}/);
  });

  it('the groupless (legacy) branch follows the same rule on the request scalar', () => {
    expect(op).toMatch(/receivingItemActionLabel\(request\.statusCode, item\.receivedQty, isReadOnly\)/);
    expect(op).toMatch(/\(!selectedGroup && !canRegisterItemReceipt\(request\.statusCode\)\)/);
    expect(op).not.toMatch(/isReadOnly \? 'VER DETALHES' : 'REGISTRAR'/);
  });

  it('confirm still requires completeness and pre-confirmation status (unchanged v2.245.0 rule)', () => {
    expect(op).toMatch(/const showConfirmButton = !isReadOnly && canConfirmReceiving\(group\.status, groupAllReceived\)/);
  });
});

describe('ReceivingOperation — REABRIR RECEBIMENTO', () => {
  it('is gated by canShowReopenReceiving(group.status, currentUserRoles, isReadOnly)', () => {
    expect(op).toMatch(/const showReopenButton = canShowReopenReceiving\(group\.status, currentUserRoles, isReadOnly\)/);
    expect(op).toMatch(/\{showReopenButton && \(/);
    expect(op).toMatch(/REABRIR RECEBIMENTO/);
  });

  it('reads the roles from the authenticated user (useAuth)', () => {
    expect(op).toMatch(/const \{ user: currentUser \} = useAuth\(\)/);
    expect(op).toMatch(/const currentUserRoles: string\[\] = currentUser\?\.roles \?\? \[\]/);
  });

  it('explains that the group returns to Receiving and needs a new confirmation', () => {
    expect(op).toMatch(/Devolve este grupo ao Recebimento para correção\. Será necessária nova confirmação\./);
  });

  it('opens the dedicated modal for the selected group only (group-scoped reopen)', () => {
    expect(op).toMatch(/setReopenState\(\{ show: true, groupId: group\.id, groupName: group\.supplierNameSnapshot \}\)/);
    expect(op).toMatch(/<ReopenReceivingModal/);
    expect(op).toMatch(/onConfirm=\{handleReopenReceiving\}/);
  });

  it('calls the group-scoped API with the reason and refreshes on success', () => {
    expect(op).toMatch(/api\.requests\.reopenReceiving\(id, reopenState\.groupId, reason\)/);
    const callIdx = op.indexOf('api.requests.reopenReceiving(');
    const refetchIdx = op.indexOf('await fetchRequest()', callIdx);
    expect(callIdx).toBeGreaterThan(-1);
    expect(refetchIdx).toBeGreaterThan(callIdx);
    expect(op).toMatch(/setReopenState\(\{ show: false, groupId: null \}\)/);
  });

  it('surfaces a backend conflict (e.g. active supplier RECEIPT) inside the modal, never alert()', () => {
    expect(op).toMatch(/setReopenError\(errorMessage\)/);
    expect(op).toMatch(/error=\{reopenError\}/);
    expect(op).not.toMatch(/alert\(/);
  });
});

describe('ReopenReceivingModal — mandatory reason', () => {
  it('trims the reason and refuses to submit when empty', () => {
    expect(modal).toMatch(/const trimmed = reason\.trim\(\)/);
    expect(modal).toMatch(/const canSubmit = trimmed\.length > 0 && !processing/);
    expect(modal).toMatch(/disabled=\{!canSubmit\}/);
    expect(modal).toMatch(/if \(canSubmit\) onConfirm\(trimmed\)/);
    expect(modal).toMatch(/Informe o motivo para prosseguir\./);
  });

  it('explains the consequence: quantities preserved, history kept, new confirmation required', () => {
    expect(modal).toMatch(/devolve este grupo ao Recebimento para correção/);
    expect(modal).toMatch(/As quantidades já registadas são preservadas e o histórico não é apagado\./);
    expect(modal).toMatch(/Será obrigatória uma nova confirmação do recebimento\./);
  });

  it('shows the backend error as an alert region', () => {
    expect(modal).toMatch(/role="alert"/);
    expect(modal).toMatch(/\{error\}/);
  });
});

describe('api.requests.reopenReceiving — group-scoped endpoint', () => {
  it('POSTs to /operational/groups/{groupId}/reopen-receiving with { reason }', () => {
    expect(apiSrc).toMatch(/reopenReceiving: async \(id: string, groupId: string, reason: string\)/);
    expect(apiSrc).toMatch(/\/api\/v1\/requests\/\$\{id\}\/operational\/groups\/\$\{groupId\}\/reopen-receiving/);
    expect(apiSrc).toMatch(/body: JSON\.stringify\(\{ reason \}\)/);
  });
});

describe('ReceivingOperation — receipt toast follows the operation semantics (v2.245.6)', () => {
  it('captures the pre-submit accumulated quantity BEFORE the API call and derives the wording from it', () => {
    expect(op).toMatch(/const previousReceivedQty: number = selectedItem\.receivedQty \?\? 0/);
    const captureIdx = op.indexOf('const previousReceivedQty');
    const callIdx = op.indexOf('api.lineItems.updateReceiving(selectedItem.id, receivedQty, notes)');
    expect(captureIdx).toBeGreaterThan(-1);
    expect(callIdx).toBeGreaterThan(captureIdx);
    expect(op).toMatch(/message: receiptSubmitSuccessMessage\(previousReceivedQty, receivedQty\)/);
  });

  it('no hard-coded success literal remains in the operation (the rule lives in receivingEligibility)', () => {
    expect(op).not.toMatch(/message: 'Recebimento registrado com sucesso\.'/);
    expect(op).not.toMatch(/AJUSTAR' \?/); // wording is never inferred from the button label
  });
});

describe('print/history labels', () => {
  it('names the new audit events', () => {
    expect(printModel).toMatch(/ITEM_RECEIVING_ADJUSTMENT: 'Ajuste de recebimento de item'/);
    expect(printModel).toMatch(/RECEIVING_REOPENED: 'Recebimento reaberto para correção'/);
  });
});
