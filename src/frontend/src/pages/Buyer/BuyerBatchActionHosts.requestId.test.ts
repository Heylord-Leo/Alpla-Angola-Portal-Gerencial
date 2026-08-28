import { describe, it, expect } from 'vitest';
import { toWizardActiveRequest } from './QuotationWizard/workspaceWizardRequest';
// Vite `?raw` imports (typed as string via vite/client) — the frontend vitest runs in
// `environment: 'node'` with no jsdom/RTL, so the structural half of this guard is source-level,
// following the BuyerRequestWorkspace.wizardMount.test.ts pattern.
import hostsSrc from './BuyerBatchActionHosts.tsx?raw';
import reworkModalSrc from './BatchReworkModal.tsx?raw';

// Regression guard for the Buyer Workspace batch-rework "requestId = undefined" bug
// (REQ-21/07/2026-132). The Workspace rework host loads a RequestDetailsDto (api.requests.get) whose
// GUID is `id` (there is no `requestId`) and forwards it as the modal's `group`. BatchReworkModal
// follows the classic group contract and calls updateApprovalBatch/resubmitApprovalBatch with
// `group.requestId`, so BOTH buttons ("Reenviar sem alterações" and "Salvar Correções e Reenviar")
// posted to /api/v1/requests/undefined/batches/{batchId}/resubmit — HTTP 404, the `{requestId:guid}`
// route never matches. The fix stamps `requestId` from the authoritative fetch key ONCE at the host
// boundary (same normalization as the wizard host fix for REQ-24/08/2026-293).

const GUID = '072a46bd-54ac-4a3b-a961-6fb21ce366cc';

describe('Workspace batch-action host boundary — RequestDetailsDto → classic group contract', () => {
    it('an id-only DTO is normalized to expose requestId (the exact regression)', () => {
        const dto = { id: GUID, approvalBatches: [{ id: 'b1', status: 'AREA_ADJUSTMENT' }], quotations: [] };
        expect('requestId' in dto).toBe(false);
        const group = toWizardActiveRequest(dto, GUID);
        expect(group.requestId).toBe(GUID);
    });

    it('keeps the original `id` and every other DTO field unchanged (additive-only)', () => {
        const dto = { id: GUID, requestNumber: 'REQ-21/07/2026-132', lineItems: [{ id: 'li1' }], quotations: [] };
        const group = toWizardActiveRequest(dto, GUID);
        expect(group.id).toBe(GUID);
        expect(group.requestNumber).toBe('REQ-21/07/2026-132');
        expect(group.lineItems).toEqual([{ id: 'li1' }]);
        expect(group.quotations).toEqual([]);
    });

    it('the host prop (fetch key) is the authoritative requestId', () => {
        const group = toWizardActiveRequest({ id: GUID }, GUID);
        expect(group.requestId).toBe(GUID);
        expect(group.requestId).not.toBeUndefined();
    });
});

describe('BuyerBatchActionHosts applies the normalization before forwarding (source-level guard)', () => {
    it('the loaded request is stamped at the boundary: setRequest(toWizardActiveRequest(r, requestId))', () => {
        expect(hostsSrc).toMatch(/setRequest\(\s*toWizardActiveRequest\(\s*r\s*,\s*requestId\s*\)\s*\)/);
    });

    it('the raw DTO is never stored un-normalized', () => {
        expect(hostsSrc).not.toMatch(/setRequest\(\s*r\s*\)/);
    });
});

describe('BatchReworkModal keeps the classic group contract (shared with the classic screen)', () => {
    it('still calls updateApprovalBatch and resubmitApprovalBatch with group.requestId', () => {
        expect(reworkModalSrc).toMatch(/updateApprovalBatch\(\s*group\.requestId\b/);
        expect(reworkModalSrc).toMatch(/resubmitApprovalBatch\(\s*group\.requestId\b/);
    });

    it('has no scattered `group.requestId ?? group.id` fallbacks — normalization lives at the host boundary', () => {
        expect(reworkModalSrc).not.toMatch(/group\.requestId\s*\?\?\s*group\.id/);
        expect(reworkModalSrc).not.toMatch(/group\.requestId\s*\|\|\s*group\.id/);
    });
});
