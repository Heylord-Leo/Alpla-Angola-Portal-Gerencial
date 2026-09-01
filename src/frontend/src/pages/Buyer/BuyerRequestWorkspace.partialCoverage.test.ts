import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards.
import src from './BuyerRequestWorkspace.tsx?raw';

// Buyer Workspace partial-coverage fix: the header renders SUBMIT_BATCH and ADD_QUOTATION independently
// (both together in PartialCoverage), position-independent via the shared derivation helper, while
// RESOLVE_ADJUSTMENT remains the special/primary flow that owns the header alone.

describe('BuyerRequestWorkspace — partial-coverage header wiring', () => {
    it('derives actions from the server list via the shared helper (no first-actionable collapse)', () => {
        expect(src).toMatch(/import \{ deriveWorkspaceHeaderActions \} from '\.\/buyerWorkspaceActions'/);
        expect(src).toMatch(/deriveWorkspaceHeaderActions\(ws\.nextActions\)/);
        // The single-action gate is gone.
        expect(src).not.toMatch(/const actionableCode =/);
    });

    it('renders SUBMIT_BATCH whenever hasSubmitBatch — it opens the EXISTING batch host', () => {
        expect(src).toMatch(/\{hasSubmitBatch && \(\s*<button onClick=\{\(\) => setActiveHost\('approval'\)\}/);
    });

    it('renders the quotation entries whenever hasAddQuotation — independently of SUBMIT_BATCH', () => {
        expect(src).toMatch(/\{hasAddQuotation && \(/);
        // Both live inside the same (non-adjustment) branch, so they can appear together.
        const elseBranch = src.split(') : (')[1] ?? '';
        expect(elseBranch).toMatch(/hasSubmitBatch &&/);
        expect(elseBranch).toMatch(/hasAddQuotation &&/);
    });

    it('RESOLVE_ADJUSTMENT owns the header alone (suppresses send/quotation)', () => {
        expect(src).toMatch(/\{hasResolveAdjustment \? \(/);
        const resolveBranch = src.split('hasResolveAdjustment ? (')[1]?.split(') : (')[0] ?? '';
        expect(resolveBranch).toMatch(/setActiveHost\('rework'\)/);
        expect(resolveBranch).not.toMatch(/setActiveHost\('approval'\)/);
    });

    it('combined next-action label only when BOTH activities exist', () => {
        expect(src).toMatch(/hasSubmitBatch && hasAddQuotation\)\s*\?\s*'Enviar itens prontos ou completar cotações'/);
    });

    it('no duplicate send button (single setActiveHost(\'approval\'))', () => {
        expect((src.match(/setActiveHost\('approval'\)/g) ?? []).length).toBe(1);
    });
});
