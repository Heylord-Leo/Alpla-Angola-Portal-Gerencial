import { describe, it, expect } from 'vitest';
import { resolveLegacyReturnCorrectionGroup } from './legacyFinanceReturn';
import panelSrc from '../pages/Requests/components/RequestStatusActionPanels.tsx?raw';

// docs §G.19.3 — the "CORRIGIR P.O" button for a legacy request-level Finance return follows the SERVER decision
// (RequestDetailsDto.legacyReturnCorrection); the v2.247.6 client-side inference from the scalar status is gone.

const GROUP = '44bd391c-9d32-496f-b237-30a597a4844e';
const decision = { poGroupId: GROUP, justificationRequired: true, reason: 'LEGACY_RETURN_PENDING_CORRECTION' };

describe('resolveLegacyReturnCorrectionGroup', () => {
    it('returns the live group the server named', () => {
        const g = { id: GROUP, status: 'PO_ISSUED', supplierNameSnapshot: 'Qlerius' };
        expect(resolveLegacyReturnCorrectionGroup(decision, [g])).toBe(g);
    });

    it('returns null without a decision, when not required, when the group is absent or cancelled', () => {
        const g = { id: GROUP, status: 'PO_ISSUED' };
        expect(resolveLegacyReturnCorrectionGroup(null, [g])).toBeNull();
        expect(resolveLegacyReturnCorrectionGroup(undefined, [g])).toBeNull();
        expect(resolveLegacyReturnCorrectionGroup({ ...decision, justificationRequired: false }, [g])).toBeNull();
        expect(resolveLegacyReturnCorrectionGroup(decision, [{ id: 'other', status: 'PO_ISSUED' }])).toBeNull();
        expect(resolveLegacyReturnCorrectionGroup(decision, [{ id: GROUP, status: 'CANCELLED' }])).toBeNull();
        expect(resolveLegacyReturnCorrectionGroup(decision, undefined)).toBeNull();
    });

    it('does not depend on the scalar status at all (no status parameter, no status literal)', () => {
        expect(resolveLegacyReturnCorrectionGroup.length).toBe(2);
    });
});

describe('RequestStatusActionPanels wiring (source-level)', () => {
    it('maps the server decision, never the scalar status, onto the correction button', () => {
        expect(panelSrc).toMatch(/resolveLegacyReturnCorrectionGroup\(legacyReturnCorrection, poGroups\)/);
        expect(panelSrc).not.toMatch(/resolveLegacyReturnCorrectionGroup\(status, poGroups\)/);
        expect(panelSrc).not.toMatch(/status !== 'WAITING_PO_CORRECTION' \|\| !poGroups/);
        expect(panelSrc).toMatch(/legacyReturnCorrection\?: LegacyFinanceReturnCorrectionDto \| null/);
    });

    it('the current-code correction buttons (groups already WAITING_PO_CORRECTION) are untouched', () => {
        expect(panelSrc).toMatch(/poGroups\.filter\(g => g\.status === 'WAITING_PO_CORRECTION'\)/);
    });
});
