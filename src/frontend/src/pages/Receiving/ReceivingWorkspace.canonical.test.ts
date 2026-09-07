import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './ReceivingWorkspace.tsx?raw';

// B4.2: the workspace gains a canonical group-level mode when opened with Dashboard V2 drill-down
// params, while preserving the historical request-scalar mode when they are absent.
describe('ReceivingWorkspace — canonical drill-down mode', () => {
  it('switches on Dashboard V2 params (queue=actionable / receivingBucket=X)', () => {
    expect(src).toMatch(/useSearchParams/);
    expect(src).toMatch(/params\.get\('queue'\)/);
    expect(src).toMatch(/params\.get\('receivingBucket'\)/);
    expect(src).toMatch(/queue === 'actionable' \|\| bucket !== null/);
  });

  it('canonical mode fetches the group-level queue endpoint (not the scalar request list)', () => {
    expect(src).toMatch(/api\.receiving\.getQueue\(\{ actionableOnly: true, bucket: bucket \?\? undefined \}\)/);
  });

  it('the scalar branch is preserved (still populates from api.requests.list)', () => {
    expect(src).toMatch(/function ReceivingScalarWorkspace\(\)/);
    expect(src).toMatch(/api\.requests\.list\(/);
    expect(src).toMatch(/function ReceivingCanonicalWorkspace\(/);
  });

  it('accepts exactly the four canonical buckets (mirrors ReceivingActionEvaluator.Buckets)', () => {
    for (const b of ['READY_FOR_RECEIPT', 'WAITING_RECEIPT', 'IN_FOLLOWUP', 'WAITING_SUPPLIER_DELIVERY']) {
      expect(src).toMatch(new RegExp(b));
    }
    // WAITING_PO is Buyer-owned and must never appear as a Receiving bucket.
    expect(src).not.toMatch(/WAITING_PO/);
  });

  it('rows are keyed by group id (distinct groups per request reconcile with the summary)', () => {
    expect(src).toMatch(/key=\{row\.requestPoGroupId\}/);
  });

  it('echoes server-provided availableActions (no client-side action recompute)', () => {
    expect(src).toMatch(/row\.availableActions/);
    expect(src).toMatch(/MOVE_TO_RECEIPT/);
    expect(src).toMatch(/CONFIRM_RECEIVING/);
  });

  it('canonical mode shows no monetary value column (formatCurrency only in the scalar branch)', () => {
    // The scalar branch keeps its Valor Estimado column; the canonical table must not add one.
    const canonicalStart = src.indexOf('function ReceivingCanonicalWorkspace');
    const canonicalSrc = src.slice(canonicalStart);
    expect(canonicalSrc).not.toMatch(/formatCurrencyAO|Valor Estimado|estimatedTotalAmount/);
  });
});
