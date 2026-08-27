import { describe, it, expect } from 'vitest';
import { toWizardActiveRequest } from './workspaceWizardRequest';

// Regression guard for the Buyer Workspace quotation-import "requestId = undefined" bug
// (REQ-24/08/2026-293). The Workspace loads a RequestDetailsDto (api.requests.get) whose GUID is `id`
// (there is no `requestId`), then feeds it to the shared wizard controller which reads
// `wizardActiveRequest.requestId` for upload/OCR/save/etc. Without normalization the upload posted to
// `/api/v1/attachments/upload/undefined` (HTTP 400 "The value 'undefined' is not valid."). This locks
// the host-boundary normalization: a Workspace request with only `id` must yield a defined `requestId`.

describe('toWizardActiveRequest — Workspace → shared wizard active-request contract', () => {
    it('stamps requestId from the GUID the wizard was opened for (id-only DTO → defined requestId)', () => {
        const dto = { id: 'abc', lineItems: [], requestNumber: 'REQ-24/08/2026-293' };
        const active = toWizardActiveRequest(dto, 'abc');
        expect(active.requestId).toBe('abc');
    });

    it('never leaves requestId undefined for a workspace request that only has id (the exact regression)', () => {
        const active = toWizardActiveRequest({ id: 'xyz' } as { id: string }, 'xyz');
        expect(active.requestId).toBeDefined();
        expect(active.requestId).toBe('xyz');
    });

    it('preserves the original DTO fields (id, lineItems, requestNumber, title) alongside requestId', () => {
        const dto = { id: 'g1', lineItems: [{ id: 'li1' }], requestNumber: 'REQ-1', title: 'T' };
        const active = toWizardActiveRequest(dto, 'g1');
        expect(active.id).toBe('g1');
        expect(active.requestNumber).toBe('REQ-1');
        expect(active.title).toBe('T');
        expect(active.lineItems).toEqual([{ id: 'li1' }]);
    });

    it('the passed GUID (fetch key) is the authoritative requestId', () => {
        // The classic queue-group carries `requestId`; the Workspace DTO does not. The fetch key is the
        // authoritative GUID, so the normalized object always exposes it as `requestId`.
        const active = toWizardActiveRequest({ id: 'guid-1', requestNumber: 'REQ-2' }, 'guid-1');
        expect(active.requestId).toBe('guid-1');
    });
});
