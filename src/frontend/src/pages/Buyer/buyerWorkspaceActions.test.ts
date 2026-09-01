import { describe, it, expect } from 'vitest';
import { deriveWorkspaceHeaderActions, WorkspaceNextAction } from './buyerWorkspaceActions';

const a = (code: string, actionable = true, label?: string): WorkspaceNextAction => ({ code, actionable, label });

// Buyer Workspace partial-coverage fix: the header derives each action from the server's nextActions
// by code + actionable, position-independent — so SUBMIT_BATCH is exposed alongside ADD_QUOTATION in
// PartialCoverage. No frontend eligibility recomputation.

describe('deriveWorkspaceHeaderActions', () => {
    it('A. ReadyForApproval: [SUBMIT_BATCH] → send only, no quotation action', () => {
        const r = deriveWorkspaceHeaderActions([a('SUBMIT_BATCH', true, 'Enviar itens para aprovação')]);
        expect(r.hasSubmitBatch).toBe(true);
        expect(r.hasAddQuotation).toBe(false);
        expect(r.submitBatchLabel).toBe('Enviar itens para aprovação');
    });

    it('B. PartialCoverage: [ADD_QUOTATION, SUBMIT_BATCH] → BOTH exposed', () => {
        const r = deriveWorkspaceHeaderActions([a('ADD_QUOTATION', true, 'Completar cotações'), a('SUBMIT_BATCH', true, 'Enviar itens cobertos para aprovação')]);
        expect(r.hasAddQuotation).toBe(true);
        expect(r.hasSubmitBatch).toBe(true);
    });

    it('C. Order independence: [SUBMIT_BATCH, ADD_QUOTATION] → same result', () => {
        const r = deriveWorkspaceHeaderActions([a('SUBMIT_BATCH'), a('ADD_QUOTATION')]);
        expect(r.hasSubmitBatch).toBe(true);
        expect(r.hasAddQuotation).toBe(true);
    });

    it('D. Pending-only: [ADD_QUOTATION] → quotation only, no send', () => {
        const r = deriveWorkspaceHeaderActions([a('ADD_QUOTATION', true, 'Completar cotações')]);
        expect(r.hasAddQuotation).toBe(true);
        expect(r.hasSubmitBatch).toBe(false);
    });

    it('E. A non-actionable SUBMIT_BATCH is not exposed', () => {
        const r = deriveWorkspaceHeaderActions([a('SUBMIT_BATCH', false), a('ADD_QUOTATION', true)]);
        expect(r.hasSubmitBatch).toBe(false);
        expect(r.hasAddQuotation).toBe(true);
    });

    it('F. RESOLVE_ADJUSTMENT is reported (special/primary flow preserved)', () => {
        const r = deriveWorkspaceHeaderActions([a('RESOLVE_ADJUSTMENT', true, 'Revisar e reenviar lote')]);
        expect(r.hasResolveAdjustment).toBe(true);
        expect(r.resolveAdjustmentLabel).toBe('Revisar e reenviar lote');
    });

    it('G. empty / null → nothing exposed (no throw)', () => {
        expect(deriveWorkspaceHeaderActions([])).toMatchObject({ hasSubmitBatch: false, hasAddQuotation: false, hasResolveAdjustment: false });
        expect(deriveWorkspaceHeaderActions(null)).toMatchObject({ hasSubmitBatch: false, hasAddQuotation: false });
    });
});

describe('regression shapes (225 / 273 / 335)', () => {
    it('REQ-225 (1 ready, 0 pending → ReadyForApproval): send visible', () => {
        const r = deriveWorkspaceHeaderActions([a('SUBMIT_BATCH', true, 'Enviar itens para aprovação')]);
        expect(r.hasSubmitBatch).toBe(true);
        expect(r.hasAddQuotation).toBe(false);
    });
    it('REQ-273 (20 ready, 21 pending → PartialCoverage): send + quotation continuation visible', () => {
        const r = deriveWorkspaceHeaderActions([a('ADD_QUOTATION', true, 'Completar cotações'), a('SUBMIT_BATCH', true, 'Enviar itens cobertos para aprovação')]);
        expect(r.hasSubmitBatch).toBe(true);
        expect(r.hasAddQuotation).toBe(true);
    });
    it('REQ-335 (1 ready, 1 pending → PartialCoverage): send + quotation continuation visible', () => {
        const r = deriveWorkspaceHeaderActions([a('ADD_QUOTATION', true), a('SUBMIT_BATCH', true)]);
        expect(r.hasSubmitBatch).toBe(true);
        expect(r.hasAddQuotation).toBe(true);
    });
});
