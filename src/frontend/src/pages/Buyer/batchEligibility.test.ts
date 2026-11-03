import { describe, it, expect } from 'vitest';
import { isQuotationItemSelectableForApproval, isQuotationItemReuseBlocked, isLineItemEligibleForQuotation } from './batchEligibility';

// v2.249.2 — the submission modal must agree with the server: a batch item RELEASED by a P.O.-group cancellation no
// longer holds its candidates (the server's reuse rule governs them: isReuseBlocked → explicit authorization), while
// a held item of an active/approved batch still does.

const QI = 'qi-trevotech';

function group(batchStatus: string, item: Record<string, unknown>) {
    return { approvalBatches: [{ status: batchStatus, items: [item] }] };
}

describe('isQuotationItemSelectableForApproval — release awareness', () => {
    it('a candidate of a HELD item of an APPROVED batch is not selectable', () => {
        expect(isQuotationItemSelectableForApproval(QI, group('APPROVED', { selectedQuotationItemId: QI, candidates: [] }))).toBe(false);
        expect(isQuotationItemSelectableForApproval(QI, group('APPROVED', { selectedQuotationItemId: null, candidates: [{ quotationItemId: QI }] }))).toBe(false);
        expect(isQuotationItemSelectableForApproval(QI, group('WAITING_AREA_APPROVAL', { selectedQuotationItemId: null, candidates: [{ quotationItemId: QI }] }))).toBe(false);
    });
    it('a candidate/winner of a RELEASED item (releasedAtUtc set) is no longer held by the batch', () => {
        expect(isQuotationItemSelectableForApproval(QI, group('APPROVED', { selectedQuotationItemId: QI, candidates: [{ quotationItemId: QI }], releasedAtUtc: '2026-10-28T10:00:00Z', releaseKind: 'RESTART_QUOTATION' }))).toBe(true);
    });
    it('CANCELLED / REJECTED batches never hold items (unchanged)', () => {
        expect(isQuotationItemSelectableForApproval(QI, group('CANCELLED', { selectedQuotationItemId: QI, candidates: [] }))).toBe(true);
        expect(isQuotationItemSelectableForApproval(QI, group('REJECTED', { selectedQuotationItemId: QI, candidates: [] }))).toBe(true);
    });
});

describe('isQuotationItemReuseBlocked — server-annotated reuse rule', () => {
    it('follows the backend flag only (never inferred client-side)', () => {
        expect(isQuotationItemReuseBlocked({ id: QI, isReuseBlocked: true })).toBe(true);
        expect(isQuotationItemReuseBlocked({ id: QI, isReuseBlocked: false, isReuseAuthorized: true })).toBe(false);
        expect(isQuotationItemReuseBlocked({ id: QI })).toBe(false);
        expect(isQuotationItemReuseBlocked(undefined)).toBe(false);
    });
});

describe('isLineItemEligibleForQuotation (unchanged)', () => {
    it('only null/undefined/QUOTATION_PENDING lines accept a new quotation', () => {
        expect(isLineItemEligibleForQuotation({ quotationLifecycleStatus: 'QUOTATION_PENDING' })).toBe(true);
        expect(isLineItemEligibleForQuotation({})).toBe(true);
        expect(isLineItemEligibleForQuotation({ quotationLifecycleStatus: 'QUOTATION_APPROVED' })).toBe(false);
        expect(isLineItemEligibleForQuotation({ quotationLifecycleStatus: 'BATCH_ASSIGNED' })).toBe(false);
    });
});
