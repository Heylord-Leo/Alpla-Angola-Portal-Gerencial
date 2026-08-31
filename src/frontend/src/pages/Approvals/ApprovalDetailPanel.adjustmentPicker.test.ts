import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (the frontend has no jsdom/RTL, so the runtime
// wiring seam is pinned against the source string, matching the ApprovalDetailPanel.finalGhostGuard
// pattern). This locks the DEV-acceptance fix: the ACTUAL "Reajuste" quick-action (the ApprovalModal
// path used by the Area quick-action AND all of Final) collects structured reasons before submit.
import panelSrc from './ApprovalDetailPanel.tsx?raw';

describe('Phase 3 — structured adjustment on the ApprovalModal quick-action path', () => {
    it('imports the shared picker and reason library', () => {
        expect(panelSrc).toMatch(/import \{ AdjustmentReasonPicker \} from '\.\/AdjustmentReasonPicker'/);
        expect(panelSrc).toMatch(/import \{[^}]*\bbuildAdjustmentReasons\b[^}]*\bvalidateAdjustmentSelection\b[^}]*\} from '\.\.\/\.\.\/lib\/adjustmentReasons'/);
    });

    it('mounts AdjustmentReasonPicker inside the ApprovalModal, gated to a QUOTATION batch adjustment', () => {
        // The picker is a child of ApprovalModal (not the separate wizard) and only for a batch
        // REQUEST_ADJUSTMENT that is not PAYMENT — i.e. exactly the real quick-action surface.
        expect(panelSrc).toMatch(/showApprovalModal\.type === 'REQUEST_ADJUSTMENT' && activeBatch && data\.requestTypeCode !== 'PAYMENT'/);
        expect(panelSrc).toMatch(/<AdjustmentReasonPicker/);
    });

    it('labels affected items business-readably from activeItems (Scenario C — never bare "Item")', () => {
        // Built from the request line items (lineNumber/itemCatalogCode/description) via the shared
        // helper — not the batch rows' unpopulated description that rendered a bare "Item".
        expect(panelSrc).toMatch(/items=\{activeItems\.map/);
        expect(panelSrc).toMatch(/affectedItemLabel\(\{ lineNumber: item\.lineNumber, itemCatalogCode: item\.itemCatalogCode, description: item\.description \}\)/);
        expect(panelSrc).not.toMatch(/it\.requestLineItemDescription \|\| 'Item'/);
    });

    it('validates and builds the structured payload before submitting (buildAdjustmentReasons used)', () => {
        expect(panelSrc).toMatch(/validateAdjustmentSelection\(adjustmentReasonCodes, adjustmentItemIds, approvalComment\)/);
        expect(panelSrc).toMatch(/buildAdjustmentReasons\(adjustmentReasonCodes, adjustmentItemIds\)/);
    });

    it('routes a batch REQUEST_ADJUSTMENT through the structured adapter; other actions stay direct', () => {
        expect(panelSrc).toMatch(/if \(action === 'REQUEST_ADJUSTMENT' && activeBatch\) \{\s*void submitBatchAdjustment\(\)/);
        // Legacy request-level / non-batch adjustment and every other action keep the comment-only call.
        expect(panelSrc).toMatch(/void handleWizardSubmit\(action, itemAwards, itemAssignments, approvalComment\)/);
    });

    it('Area and Final both open this shared ApprovalModal REQUEST_ADJUSTMENT quick-action', () => {
        const opens = panelSrc.match(/setShowApprovalModal\(\{ show: true, type: 'REQUEST_ADJUSTMENT' \}\)/g) ?? [];
        expect(opens.length).toBeGreaterThanOrEqual(2); // Area quick-action + Final quick-action
    });

    it('resets the picker state on modal close and after a successful submit', () => {
        expect(panelSrc).toMatch(/resetAdjustmentPicker\(\)/);
        expect(panelSrc).toMatch(/if \(ok\) resetAdjustmentPicker\(\)/);
    });
});
