import { describe, it, expect } from 'vitest';
import { resolveQuotationActionState } from './workspaceQuotationActions';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';
import classicSrc from './BuyerItemsList.tsx?raw';

// §7 — ordinary Edit/Delete only for quotations never submitted for approval. The per-quotation lock is the SERVER's
// decision (isSubmittedForApproval + reason); both UIs consume it instead of recomputing it.
describe('resolveQuotationActionState — server lock first, request-level rule second', () => {
    it('never-submitted quotation: editable and deletable when the request-level rule allows', () => {
        expect(resolveQuotationActionState({ isSubmittedForApproval: false }, true)).toEqual({ editable: true, deletable: true, locked: false });
    });
    it('submitted quotation: locked with the server reason and batch label, whatever the batch outcome', () => {
        const st = resolveQuotationActionState({ isSubmittedForApproval: true, submittedBatchNumber: 1, submissionLockReason: 'Cotação já submetida para aprovação (Lote #1, REJECTED): …' }, true);
        expect(st.editable).toBe(false); expect(st.deletable).toBe(false); expect(st.locked).toBe(true);
        expect(st.lockLabel).toBe('Submetida (Lote #1)'); expect(st.lockReason).toContain('Lote #1, REJECTED');
    });
    it('falls back to a generic label/reason when the server omits them, and never unlocks client-side', () => {
        const st = resolveQuotationActionState({ isSubmittedForApproval: true }, true);
        expect(st.locked).toBe(true); expect(st.lockLabel).toBe('Submetida para aprovação'); expect(st.lockReason).toContain('revisão/reajuste');
    });
    it('request-level rule denied: no actions and no lock chip', () => {
        expect(resolveQuotationActionState({ isSubmittedForApproval: true }, false)).toEqual({ editable: false, deletable: false, locked: false });
    });
});

describe('both UIs consume the server decision (source-level)', () => {
    it('workspace renders the lock chip with the reason instead of Editar/Excluir', () => {
        expect(workspaceSrc).toMatch(/const st = resolveQuotationActionState\(q, canManage\);/);
        expect(workspaceSrc).toMatch(/st\.locked \? \(/);
        expect(workspaceSrc).toMatch(/title=\{st\.lockReason\}/);
        expect(workspaceSrc).toMatch(/disabled=\{busy \|\| !st\.editable\}/);
        expect(workspaceSrc).toMatch(/disabled=\{busy \|\| !st\.deletable\}/);
    });
    it('classic list prefers the server flag and only falls back to its legacy client-side check when the flag is absent', () => {
        expect(classicSrc).toMatch(/const isUsedInBatch = q\.isSubmittedForApproval === true \|\| \(q\.isSubmittedForApproval == null && group\.approvalBatches/);
        expect(classicSrc).toMatch(/title=\{q\.submissionLockReason \|\|/);
        expect(classicSrc).toMatch(/q\.submittedBatchNumber != null \? `Submetida \(Lote #\$\{q\.submittedBatchNumber\}\)` : 'Protegida por auditoria'/);
    });
});
