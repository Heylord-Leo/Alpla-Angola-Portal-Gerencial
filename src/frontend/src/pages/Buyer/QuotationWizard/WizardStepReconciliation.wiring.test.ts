import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards.
import src from './WizardStepReconciliation.tsx?raw';

// Phase 4 — the reconciliation UI surfaces that represent request targets must use the union target
// set (eligible + already-mapped-by-this-draft), not the NEW-only eligible list; and the
// "covered elsewhere" message must not count an item that is shown here as already linked.

describe('WizardStepReconciliation wiring (union target set)', () => {
    it('derives the union target set from the shared helper', () => {
        expect(src).toMatch(/import \{ reconciliationRequestItems \} from '\.\/reconciliationTargets'/);
        expect(src).toMatch(/const reconTargets = reconciliationRequestItems\(request\.lineItems, mappedIds as Set<string>, isLineItemEligibleForQuotation\)/);
    });

    it('the right-side "Itens Solicitados" panel renders the union set', () => {
        expect(src).toMatch(/\{reconTargets\.map\(\(reqItem: any, idx: number\) =>/);
    });

    it('the mapping dropdown offers the union set (so an already-linked target is selectable)', () => {
        expect(src).toMatch(/\{reconTargets\.filter\(\(reqItem: any\) =>/);
    });

    it('coverage message excludes already-linked items (not falsely reported as "tratado")', () => {
        expect(src).toMatch(/coveredElsewhereCount = \(request\.lineItems \|\| \[\]\)\.filter\(\(li: any\) => !reconTargetIds\.has\(li\.id\)\)\.length/);
        expect(src).toMatch(/'BATCH_ASSIGNED', 'QUOTATION_APPROVED'\]\.includes\(i\.quotationLifecycleStatus\) && !mappedIds\.has\(i\.id\)/);
    });

    it('does NOT weaken the global rule — isLineItemEligibleForQuotation is still used for eligibility', () => {
        expect(src).toMatch(/const eligibleRequestItems = \(request\.lineItems \|\| \[\]\)\.filter\(isLineItemEligibleForQuotation\)/);
    });
});
