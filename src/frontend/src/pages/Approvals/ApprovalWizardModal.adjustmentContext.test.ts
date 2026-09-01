import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards.
import wizardSrc from './ApprovalWizardModal.tsx?raw';
import panelSrc from './ApprovalDetailPanel.tsx?raw';

// Phase 4 UX: the structured "Motivos do Reajuste" picker belongs ONLY to the "Solicitar Reajuste"
// action, never to normal review/approve/reject. In the Area wizard it is gated behind an
// adjustmentMode entered via the button; in the Final/quick-action modal it is already gated to the
// REQUEST_ADJUSTMENT action.

describe('Area wizard — picker only in the adjustment action state', () => {
    it('renders the picker only on REVIEW AND when adjustmentMode is active', () => {
        expect(wizardSrc).toMatch(/currentStepConfig\.key === 'REVIEW' && adjustmentMode && activeBatch && request\?\.requestTypeCode !== 'PAYMENT'/);
        // The old always-on gate (no adjustmentMode) must be gone.
        expect(wizardSrc).not.toMatch(/currentStepConfig\.key === 'REVIEW' && activeBatch && request\?\.requestTypeCode !== 'PAYMENT' && \(\s*<div style=\{\{ marginTop: 16 \}\}>/);
    });

    it('"Solicitar Reajuste" ENTERS adjustment mode (reveals the picker) — it does not submit', () => {
        expect(wizardSrc).toMatch(/onClick=\{\(\) => \{ setAdjustmentMode\(true\); setStepValidationError\(null\); \}\}[\s\S]*?Solicitar Reajuste/);
    });

    it('"Confirmar Reajuste" submits REQUEST_ADJUSTMENT; Cancelar exits and clears the selection', () => {
        expect(wizardSrc).toMatch(/onClick=\{\(\) => handleSubmit\('REQUEST_ADJUSTMENT'\)\}[\s\S]*?Confirmar Reajuste/);
        expect(wizardSrc).toMatch(/setAdjustmentMode\(false\); setAdjustmentReasonCodes\(\[\]\); setAdjustmentItemIds\(\[\]\);[\s\S]*?Cancelar/);
    });

    it('Approve and Reject stay independent of the adjustment picker', () => {
        expect(wizardSrc).toMatch(/onClick=\{\(\) => handleSubmit\('APPROVE'\)\}/);
        expect(wizardSrc).toMatch(/onClick=\{\(\) => handleSubmit\('REJECT'\)\}/);
        // Navigating back off the review step exits adjustment mode.
        expect(wizardSrc).toMatch(/setAdjustmentMode\(false\); \/\/ leaving the review step/);
    });

    it('adjustment validation is unchanged and scoped to REQUEST_ADJUSTMENT (approval needs no reasons)', () => {
        expect(wizardSrc).toMatch(/const isBatchAdjustment = action === 'REQUEST_ADJUSTMENT' && !!activeBatch;/);
        expect(wizardSrc).toMatch(/validateAdjustmentSelection\(adjustmentReasonCodes, adjustmentItemIds, comment\)/);
        expect(wizardSrc).toMatch(/\(action === 'REJECT' \|\| action === 'REQUEST_ADJUSTMENT'\) && !comment\.trim\(\)/);
    });

    it('exactly one adjustment picker instance in the wizard (no duplicate)', () => {
        expect((wizardSrc.match(/<AdjustmentReasonPicker/g) ?? []).length).toBe(1);
    });
});

describe('Final / quick-action modal — picker already gated to the adjustment action', () => {
    it('renders the picker only when the modal action is REQUEST_ADJUSTMENT', () => {
        expect(panelSrc).toMatch(/showApprovalModal\.type === 'REQUEST_ADJUSTMENT' && activeBatch && data\.requestTypeCode !== 'PAYMENT'/);
    });
    it('exactly one adjustment picker instance in the panel (no duplicate)', () => {
        expect((panelSrc.match(/<AdjustmentReasonPicker/g) ?? []).length).toBe(1);
    });
});
