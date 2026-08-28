import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (BuyerRequestWorkspace.wizardMount pattern).
import panelSrc from './ApprovalDetailPanel.tsx?raw';

// Ghost-card defense in depth (REQ-20/08/2026-274): a batch-model QUOTATION row that reaches the
// drawer without resolving an actionable batch (e.g. its only lot is in FINAL_ADJUSTMENT with the
// Buyer while the scalar intentionally stays WAITING_FINAL_APPROVAL) must never expose the
// request-wide Final quick actions. The backend batch-model gate refuses those calls anyway —
// this locks the matching frontend parity of the Area-side G1 scope guard.

describe('Final-branch ghost guard — no request-wide actions without a resolved batch', () => {
    it('defines the guard from batch-model shape: QUOTATION + has batches + no activeBatch', () => {
        expect(panelSrc).toMatch(/isBatchModelWithoutActiveBatch = isQuotation && hasApprovalBatches && !activeBatch/);
    });

    it('the Final action footer branches on the guard BEFORE the request-wide buttons', () => {
        // Shape: `) : isBatchModelWithoutActiveBatch ? (` — the guard owns the branch between the
        // area branch and the legacy three-button footer.
        expect(panelSrc).toMatch(/\)\s*:\s*isBatchModelWithoutActiveBatch \? \(/);
    });

    it('renders a safe explanation instead of actions for the ghost shape', () => {
        expect(panelSrc).toMatch(/aprovado por lotes e não possui nenhum lote aguardando aprovação final/);
    });

    it('the normal batch-backed / PAYMENT / legacy footer with Aprovar remains present', () => {
        expect(panelSrc).toMatch(/<ShieldCheck size=\{16\} \/> Aprovar/);
    });
});
