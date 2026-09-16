import { describe, it, expect } from 'vitest';
// Node-env vitest source guards for the v2.245.0 receiving-finalization frontend fix (§25) + the
// UI-consistency hardening (empty-group-id guard, top-level resolver, modal guard).
import op from './ReceivingOperation.tsx?raw';
import modal from '../../components/modals/FinalizeReceivingModal.tsx?raw';

describe('ReceivingOperation — group mapping fallback', () => {
  it('resolves an operational quotation item to its group by explicit link first', () => {
    expect(op).toMatch(/request\.lineItems\?\.find\(\(li: any\) => li\.selectedQuotationItemId === qi\.id\)/);
  });
  it('falls back to an UNAMBIGUOUS LineNumber match (guarded by count === 1)', () => {
    expect(op).toMatch(/li\.lineNumber === qi\.lineNumber/);
    expect(op).toMatch(/byNumber\.length === 1 \? byNumber\[0\] : undefined/);
  });
  it('never leaks across groups — items are filtered by the group id', () => {
    expect(op).toMatch(/operationalItems\.filter\(\(i: any\) => i\.requestPoGroupId === group\.id\)/);
  });
  it('leaves the PAYMENT (line-item) receiving branch on its own requestPoGroupId', () => {
    expect(op).toMatch(/requestPoGroupId: li\.requestPoGroupId/);
  });
});

describe('ReceivingOperation — confirm UX (§8)', () => {
  it('computes group completeness and a hint, never a silent no-action state', () => {
    expect(op).toMatch(/groupAllReceived = groupItems\.length > 0 && groupItems\.every/);
    expect(op).toMatch(/Recebimento completo — confirme o recebimento\./);
    expect(op).toMatch(/de \$\{groupItems\.length\} itens recebidos\. Existem quantidades pendentes\./);
  });
  it('keeps the CONFIRMAR RECEBIMENTO action for actionable groups (incl. IN_FOLLOWUP)', () => {
    expect(op).toMatch(/CONFIRMAR RECEBIMENTO/);
    // v2.245.0 §16: actionability (incl. IN_FOLLOWUP) is now the canonical shared helper on group status;
    // the specific status membership is asserted in src/lib/receivingEligibility.test.ts.
    expect(op).toMatch(/const groupActionable = isReceivingActionableGroupStatus\(group\.status\)/);
    expect(op).toMatch(/!isGroupReadOnly &&/);
  });
  it('does not call any mutation endpoint from the mapping/render path directly', () => {
    // Receipt registration + confirm go through api.* handlers already; the render path adds none.
    expect(op).not.toMatch(/document\.write/);
  });
});

describe('ReceivingOperation — never send an empty group id (§10 A–H)', () => {
  it('A/H: the top-level action resolves a single real group; group-specific button uses group.id', () => {
    expect(op).toMatch(/const handleTopLevelFinalize = \(\) =>/);
    expect(op).toMatch(/resolvable\.length === 1/);
    expect(op).toMatch(/handleFinalizeClick\(resolvable\[0\]\.id/);
    expect(op).toMatch(/handleFinalizeClick\(group\.id, group\.supplierNameSnapshot\)/);
    // the old empty-id call is gone
    expect(op).not.toMatch(/handleFinalizeClick\('', ''\)/);
    expect(op).toMatch(/onClick=\{handleTopLevelFinalize\}/);
  });
  it('B: multiple resolvable groups do NOT guess (require group-specific confirm)', () => {
    expect(op).toMatch(/resolvable\.length === 0/);
    expect(op).toMatch(/múltiplos grupos\. Confirme o recebimento em cada grupo/);
  });
  it('C/G: handleFinalizeClick refuses an empty id with visible feedback (no modal, no backend call)', () => {
    expect(op).toMatch(/if \(!groupId\) \{\s*\n\s*setFeedback\(\{ type: 'error'/);
    expect(op).toMatch(/Não foi possível identificar o grupo de recebimento/);
  });
  it('F: item receipt registration refetches request detail on success', () => {
    expect(op).toMatch(/fetchRequest\(\)/);
  });
});

describe('FinalizeReceivingModal — defensive empty-group-id guard (§10 D,E)', () => {
  it('D/E: does not POST confirm without a group id; shows feedback', () => {
    expect(modal).toMatch(/if \(!groupId\) \{/);
    expect(modal).toMatch(/Não foi possível identificar o grupo de recebimento/);
    // the guard sits before the confirmReceiving call
    const guardIdx = modal.indexOf('if (!groupId)');
    const confirmIdx = modal.indexOf('api.requests.confirmReceiving');
    expect(guardIdx).toBeGreaterThan(-1);
    expect(confirmIdx).toBeGreaterThan(guardIdx);
  });
  it('no alert()', () => { expect(modal).not.toMatch(/alert\(/); });
});
