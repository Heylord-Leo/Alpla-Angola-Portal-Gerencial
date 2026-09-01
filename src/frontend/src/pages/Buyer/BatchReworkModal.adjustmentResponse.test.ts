import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards, matching the
// BatchReworkModal.adjustmentContext.test.ts pattern.
import modalSrc from './BatchReworkModal.tsx?raw';
import detailSrc from './BatchDetailModal.tsx?raw';
import apiSrc from '../../lib/api.ts?raw';

// Adjustment V2 Phase 4 — the BUYER's mandatory "Resposta ao reajuste". Required ONLY when the
// batch carries an open structured cycle (hasOpenAdjustmentCycle); legacy batches keep the
// comment-only resubmit. The response is registered as one BUYER resolution server-side; the Buyer
// never edits the Requester-owned reason classification here.

describe('Phase 4 — mandatory Buyer response on a V2 cycle', () => {
    it('gates the requirement on the batch open-cycle flag, not on every resubmit', () => {
        expect(modalSrc).toMatch(/responseRequired\s*=\s*!!batch\?\.hasOpenAdjustmentCycle/);
        expect(modalSrc).toMatch(/responseMissing\s*=\s*responseRequired\s*&&\s*adjustmentResponse\.trim\(\)\.length === 0/);
    });

    it('renders the "Resposta ao reajuste" textarea only when required', () => {
        expect(modalSrc).toMatch(/\{responseRequired && \(/);
        expect(modalSrc).toMatch(/Resposta ao reajuste/);
        expect(modalSrc).toMatch(/<textarea/);
    });

    it('blocks BOTH resubmit paths until the response is provided', () => {
        // save+resubmit is disabled via isValid, which folds in !responseMissing …
        expect(modalSrc).toMatch(/const isValid = .*&& !responseMissing;/);
        // … and both explicit handlers reject a missing response before calling the API.
        const missingGuards = modalSrc.match(/if \(responseMissing\) \{/g) ?? [];
        expect(missingGuards.length).toBeGreaterThanOrEqual(2);
    });

    it('passes the trimmed response into resubmit only when required (undefined for legacy)', () => {
        const calls = modalSrc.match(/responseRequired \? adjustmentResponse\.trim\(\) : undefined/g) ?? [];
        expect(calls.length).toBe(2); // handleSaveAndResubmit + handleResubmitOnly
    });

    it('retains the typed response across a failed resubmit (only cleared on (re)open)', () => {
        // The state is reset in the open effect, never in the error handlers.
        expect(modalSrc).toMatch(/setAdjustmentResponse\(''\);/);
        expect(modalSrc).not.toMatch(/catch[^}]*setAdjustmentResponse\(''\)/s);
    });

    it('never lets the Buyer edit the reason classification (read-only motive only)', () => {
        expect(modalSrc).toMatch(/batch\.adjustmentReason/); // shown read-only
        expect(modalSrc).not.toMatch(/AdjustmentReasonPicker/); // no reason authoring surface here
    });
});

describe('Phase 4 — api mapping and details projection', () => {
    it('api.resubmitApprovalBatch forwards adjustmentResponse in the body', () => {
        expect(apiSrc).toMatch(/resubmitApprovalBatch:\s*async\s*\([^)]*adjustmentResponse\?: string/);
        expect(apiSrc).toMatch(/body: JSON\.stringify\(\{ comment, adjustmentResponse \}\)/);
    });

    it('batch details shows the Buyer response once resolved (no raw codes)', () => {
        expect(detailSrc).toMatch(/adj\.responseNote/);
        expect(detailSrc).toMatch(/Resposta ao reajuste \(Comprador\)/);
        expect(detailSrc).toMatch(/adj\.respondedByName/);
        expect(detailSrc).toMatch(/adj\.respondedAtUtc/);
    });
});
