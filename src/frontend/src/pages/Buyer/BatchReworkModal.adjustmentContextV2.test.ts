import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards, matching the sibling
// BatchReworkModal.adjustmentContext.test.ts / .adjustmentResponse.test.ts pattern.
import modalSrc from './BatchReworkModal.tsx?raw';

// Adjustment V2 Phase 4 (Buyer context UX fix): for an OPEN structured cycle the modal must show the
// STRUCTURED reasons (friendly labels) distinctly from the approver's free-text comment, and must NOT
// label that comment "Motivo do reajuste". Legacy pre-V2 batches keep the QF1 "Motivo do reajuste".

describe('Phase 4 — V2 structured adjustment context', () => {
    it('branches on the structured open cycle (backend-projected), not the QF1 scalar', () => {
        expect(modalSrc).toMatch(/batch\.openAdjustmentCycle \? \(/);
    });

    it('renders friendly reason labels via reasonLabel — never a raw ReasonCode', () => {
        expect(modalSrc).toMatch(/import \{[^}]*\breasonLabel\b[^}]*\}\s*from\s*'\.\.\/\.\.\/lib\/adjustmentReasons'/);
        expect(modalSrc).toMatch(/reasonLabel\(r\.reasonCode\)/);
        // The raw enum value must not be rendered directly anywhere in the modal.
        expect(modalSrc).not.toMatch(/\{r\.reasonCode\}/);
        expect(modalSrc).not.toMatch(/PRICE_NEGOTIATION/);
    });

    it('renders a "Motivos:" block for the structured reasons', () => {
        expect(modalSrc).toMatch(/Motivos:/);
    });

    it('renders the approver free-text as "Comentário do aprovador" (from the cycle), not as the motive', () => {
        expect(modalSrc).toMatch(/Comentário do aprovador:/);
        expect(modalSrc).toMatch(/batch\.openAdjustmentCycle\.approverComment/);
    });

    it('item-scoped reasons render a business-readable affected item (no GUID)', () => {
        expect(modalSrc).toMatch(/affectedItemLabel\(\{ lineNumber: r\.lineNumber, itemCatalogCode: r\.itemCatalogCode, description: r\.description \}\)/);
        expect(modalSrc).toMatch(/Item afetado:/);
    });

    it('uses sourceStageLabel for origin (shared vocabulary, no duplicate map)', () => {
        expect(modalSrc).toMatch(/sourceStageLabel\(batch\.openAdjustmentCycle\.sourceStage\)/);
    });
});

describe('Phase 4 — legacy fallback preserved', () => {
    it('keeps the QF1 "Motivo do reajuste" in the ELSE (no-cycle) branch only', () => {
        // The ternary splits V2 (truthy) from legacy (falsy). "Motivo do reajuste" must live AFTER the
        // `) : (` fallback marker, i.e. only in the legacy branch.
        const parts = modalSrc.split('batch.openAdjustmentCycle ? (');
        expect(parts.length).toBe(2);
        const afterBranch = parts[1];
        const fallbackIdx = afterBranch.indexOf(') : (');
        expect(fallbackIdx).toBeGreaterThan(-1);
        const v2Branch = afterBranch.slice(0, fallbackIdx);
        const legacyBranch = afterBranch.slice(fallbackIdx);
        expect(v2Branch).not.toMatch(/Motivo do reajuste:/);
        expect(legacyBranch).toMatch(/Motivo do reajuste:/);
        expect(legacyBranch).toMatch(/batch\.adjustmentReason/);
    });
});
