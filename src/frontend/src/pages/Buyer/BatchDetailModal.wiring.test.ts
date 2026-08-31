import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL). Business label mapping is
// covered non-vacuously in src/lib/adjustmentReasons.test.ts; this pins the read-only batch-details
// UI wiring for the "Lotes & Aprovações" tab (Phase 3 quick improvement).
import modalSrc from './BatchDetailModal.tsx?raw';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';

describe('Batch details modal — read-only content', () => {
    it('renders the friendly batch status (batchStatusLabel), never raw codes', () => {
        expect(modalSrc).toMatch(/batchStatusLabel\(batch\.status\)/);
    });

    it('shows the structured cycle summary only when an adjustment exists', () => {
        expect(modalSrc).toMatch(/const adj = batch\.adjustment/);
        expect(modalSrc).toMatch(/\{adj \?/); // conditional render on the cycle
        // Cycle fields via friendly labels
        expect(modalSrc).toMatch(/sourceStageLabel\(adj\.sourceStage\)/);
        expect(modalSrc).toMatch(/cycleStateLabel\(adj\.status\)/);
        expect(modalSrc).toMatch(/cycleResponsibleLabel\(adj\.status\)/);
        expect(modalSrc).toMatch(/reasonLabel\(r\.reasonCode\)/);
    });

    it('displays the approver comment and requester', () => {
        expect(modalSrc).toMatch(/adj\.approverComment/);
        expect(modalSrc).toMatch(/adj\.requestedByName/);
    });

    it('renders affected item line numbers when item-scoped', () => {
        expect(modalSrc).toMatch(/Itens afetados/);
        expect(modalSrc).toMatch(/r\.lineNumber/);
    });

    it('shows a neutral message when the batch has no structured cycle (legacy-safe)', () => {
        expect(modalSrc).toMatch(/Este lote não possui um ciclo de reajuste estruturado\./);
    });

    it('is purely informational — no submit/mutation/action wiring (read-only)', () => {
        // Real action signals only (not explanatory prose): no form submit, no mutating API calls,
        // no workflow action handlers. The single close button (onClose) is not an action.
        expect(modalSrc).not.toMatch(/onSubmit|handleSubmit|type="submit"/);
        expect(modalSrc).not.toMatch(/api\.(requests|buyer)\./);
    });
});

describe('Lotes & Aprovações tab — click opens the detail modal', () => {
    it('the batch entry is a clickable button that selects the batch', () => {
        expect(workspaceSrc).toMatch(/function TabBatches/);
        expect(workspaceSrc).toMatch(/onClick=\{\(\) => setSelected\(b\)\}/);
        expect(workspaceSrc).toMatch(/<BatchDetailModal batch=\{selected\} onClose=\{\(\) => setSelected\(null\)\}/);
    });

    it('shows the friendly batch status on the card', () => {
        expect(workspaceSrc).toMatch(/batchStatusLabel\(b\.status\)/);
    });
});
