import { describe, it, expect } from 'vitest';
import { deriveWorkflowSnapshot } from './requestWorkflowSnapshot';
import snapshotSrc from './requestWorkflowSnapshot.ts?raw';
import pageSrc from '../pages/Requests/RequestEdit.tsx?raw';
import hookSrc from '../pages/Requests/hooks/useRequestDetail.ts?raw';

// docs §G.19.3 — after a successful P.O. correction the detail badge stayed "Devolvido para Compras" on TEST
// (REQ-27/07/2026-156) while responsibility had moved to Finance: the modal's success handler re-read only part of
// the detail (persisted status name, no display label, no groups). The page now reloads the whole workflow state
// from the server; these tests cover the derivation for both correction outcomes and guard the wiring.

const GROUP = '44bd391c-9d32-496f-b237-30a597a4844e';

describe('deriveWorkflowSnapshot — server payload is the only source', () => {
    it('post-paid outcome: scalar PO_ISSUED, no display override → persisted name, groups refreshed', () => {
        const snap = deriveWorkflowSnapshot({
            statusCode: 'PO_ISSUED', statusName: 'P.O. Emitida', statusBadgeColor: 'green', displayStatusName: null,
            poGroups: [{ id: GROUP, status: 'PO_ISSUED' }], statusHistory: [{ actionTaken: 'REREGISTER_PO' }], attachments: [{ id: 'a' }],
        });
        expect(snap.status).toBe('PO_ISSUED');
        expect(snap.statusFullName).toBe('P.O. Emitida');
        expect(snap.statusBadgeColor).toBe('green');
        expect(snap.poGroups).toEqual([{ id: GROUP, status: 'PO_ISSUED' }]);
        expect(snap.statusHistory).toHaveLength(1);
        expect(snap.attachments).toHaveLength(1);
    });

    it('advance-required outcome (REQ-156): scalar ADVANCE_PAYMENT_REQUIRED → its name, not the previous return label', () => {
        const snap = deriveWorkflowSnapshot({
            statusCode: 'ADVANCE_PAYMENT_REQUIRED', statusName: 'Adiantamento Necessário', statusBadgeColor: 'yellow', displayStatusName: null,
            poGroups: [{ id: GROUP, status: 'ADVANCE_PAYMENT_REQUIRED', totalAmount: 1097000 }],
        });
        expect(snap.status).toBe('ADVANCE_PAYMENT_REQUIRED');
        expect(snap.statusFullName).toBe('Adiantamento Necessário');
        expect(snap.statusFullName).not.toBe('Devolvido para Compras');
        expect(snap.poGroups[0].status).toBe('ADVANCE_PAYMENT_REQUIRED');
    });

    it('the server group-aware display label wins over the persisted name (Phase 4B.2 precedence)', () => {
        const snap = deriveWorkflowSnapshot({ statusCode: 'APPROVED', statusName: 'Aprovado', displayStatusName: 'Aguardando P.O.' });
        expect(snap.statusFullName).toBe('Aguardando P.O.');
    });

    it('missing collections become empty arrays / null (never undefined state)', () => {
        const snap = deriveWorkflowSnapshot({ statusCode: 'DRAFT', statusName: 'Rascunho' });
        expect(snap.poGroups).toEqual([]);
        expect(snap.statusHistory).toEqual([]);
        expect(snap.attachments).toEqual([]);
        expect(snap.quotations).toEqual([]);
        expect(snap.selectedQuotationId).toBeNull();
        expect(snap.statusBadgeColor).toBe('');
    });

    it('hard-codes no destination status', () => {
        expect(snapshotSrc).not.toMatch(/PO_ISSUED|ADVANCE_PAYMENT_REQUIRED|WAITING_PO_CORRECTION|PAYMENT_SCHEDULED/);
    });
});

describe('detail refresh wiring after a successful correction (source-level)', () => {
    it('the hook exposes refreshWorkflowState built on deriveWorkflowSnapshot and uses the same derivation on load', () => {
        expect(hookSrc).toMatch(/const refreshWorkflowState = useCallback\(async \(\) => \{[\s\S]*?api\.requests\.get\(id\)[\s\S]*?applyWorkflowSnapshot\(data\)/);
        expect(hookSrc).toMatch(/const snap = deriveWorkflowSnapshot\(data\)/);
        expect(hookSrc).toMatch(/setStatusFullName\(snap\.statusFullName\)/);
        expect(hookSrc).toMatch(/setPoGroups\(snap\.poGroups\)/);
        expect(hookSrc).toMatch(/\n\s+refreshWorkflowState,\n/);
    });

    it('the CorrectPoModal success handler reloads the whole state and no longer keeps the persisted name only', () => {
        const start = pageSrc.indexOf('<CorrectPoModal');
        const end = pageSrc.indexOf('<QuickSupplierModal');
        const block = pageSrc.slice(start, end);
        expect(block).toMatch(/onSuccess=\{async \(msg\) => \{[\s\S]*?await refreshWorkflowState\(\);/);
        // The projection-owned badge/guidance (QUOTATION override) re-read even when the scalar did not change.
        expect(block).toMatch(/setProjectionReloadTick\(t => t \+ 1\)/);
        expect(pageSrc).toMatch(/\}, \[id, requestTypeCode, status, projectionReloadTick\]\);/);
        expect(block).not.toMatch(/setStatusFullName\(data\.statusName\)/);
        expect(block).not.toMatch(/api\.requests\.get\(id\)/);
    });

    it('the action panel receives the server legacy-return decision from the detail payload', () => {
        expect(pageSrc).toMatch(/legacyReturnCorrection=\{detail\?\.legacyReturnCorrection \?\? null\}/);
    });
});
