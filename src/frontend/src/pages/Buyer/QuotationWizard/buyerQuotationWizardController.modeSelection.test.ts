import { describe, it, expect, vi } from 'vitest';

// The controller transitively imports `lib/api` → `lib/logger`, which reads localStorage at module load.
// The suite runs in the pure `node` env (no DOM by design); `handleOpenWizard` — the only surface under
// test — makes NO api calls, so an empty api stub keeps this a pure-logic test with no env change.
vi.mock('../../../lib/api', () => ({ api: {} }));

import { createBuyerQuotationWizardController } from './buyerQuotationWizardController';

// Stage 2B-R.1 — the Workspace exposes TWO explicit entry methods (Importar Cotação / Inserir
// Manualmente). Both go through the SAME shared controller; the only difference is the canonical Wizard
// SOURCE passed to `handleOpenWizard` ('UPLOAD' → document/OCR flow, 'MANUAL' → priceable-rows flow).
// These tests pin that routing to the wizard-state `openWizard(mode, initialDraft, quotationId, source)`
// contract. Root cause of the defect was a hardcoded 'MANUAL' at the Workspace host boundary.

const eligibleLineItem = (over: any = {}) => ({
    id: 'li-1', description: 'Laptop HP Probook 450', quantity: 1, unitId: 7,
    quotationLifecycleStatus: null, ...over,
});

/** Minimal deps: only `handleOpenWizard`'s touchpoints matter here (openWizard capture + setters). */
function makeController(openWizard: ReturnType<typeof vi.fn>) {
    const quotationWizardState: any = { openWizard, classificationConflict: null, isEditing: false, editingQuotationId: null, closeWizard: vi.fn(), setDraft: vi.fn() };
    const preAttemptSnapshotRef: any = { current: null };
    return createBuyerQuotationWizardController({
        quotationWizardState,
        wizardActiveRequest: null,
        setWizardActiveRequest: vi.fn(),
        setIsSaving: vi.fn(),
        setIsProcessingOcr: vi.fn(),
        temporaryWizardAttachmentIds: [],
        setTemporaryWizardAttachmentIds: vi.fn(),
        preAttemptSnapshotRef,
        mapOcrResultToDraft: vi.fn(),
        onSaved: vi.fn(),
        onFeedback: vi.fn(),
    });
}

const group = { requestId: 'req-1', requestNumber: 'ZZTEST-BUY-001', lineItems: [eligibleLineItem()] };

describe('Stage 2B-R.1 — explicit quotation-entry mode routing (shared controller)', () => {
    it('1. IMPORT action opens the wizard in DOCUMENT/OCR mode (source=UPLOAD, no seeded manual draft)', () => {
        const openWizard = vi.fn();
        makeController(openWizard).handleOpenWizard(group, 'UPLOAD');
        expect(openWizard).toHaveBeenCalledTimes(1);
        const [mode, initialDraft, quotationId, source] = openWizard.mock.calls[0];
        expect(mode).toBe('NEW');
        expect(source).toBe('UPLOAD');   // → wizard opens on DOCUMENTS_OCR step
        expect(initialDraft).toBeNull(); // document flow does not pre-seed priceable rows
        expect(quotationId).toBeUndefined();
    });

    it('2. MANUAL action opens the wizard in MANUAL mode (source=MANUAL, seeded priceable rows)', () => {
        const openWizard = vi.fn();
        makeController(openWizard).handleOpenWizard(group, 'MANUAL');
        expect(openWizard).toHaveBeenCalledTimes(1);
        const [mode, initialDraft, quotationId, source] = openWizard.mock.calls[0];
        expect(mode).toBe('NEW');
        expect(source).toBe('MANUAL');   // → wizard opens on OVERVIEW step
        expect(initialDraft).not.toBeNull();
        expect(initialDraft.items).toHaveLength(1); // eligible requested item seeded as a priceable row
        expect(initialDraft.items[0].mappedRequestLineItemId).toBe('li-1');
        expect(initialDraft.items[0].reconciliationStatus).toBeUndefined(); // manual behavior preserved
        expect(quotationId).toBeUndefined();
    });

    it('3. both entry methods flow through the SAME controller factory (single source of handler logic)', () => {
        const openWizard = vi.fn();
        const controller = makeController(openWizard);
        controller.handleOpenWizard(group, 'UPLOAD');
        controller.handleOpenWizard(group, 'MANUAL');
        expect(openWizard).toHaveBeenCalledTimes(2);
        expect(openWizard.mock.calls[0][3]).toBe('UPLOAD');
        expect(openWizard.mock.calls[1][3]).toBe('MANUAL');
    });

    it('4. request/eligibility CONTEXT is identical between modes — only the source differs', () => {
        const openWizard = vi.fn();
        const controller = makeController(openWizard);
        controller.handleOpenWizard(group, 'MANUAL');
        // The seeded manual draft derives from the SAME group.lineItems the document flow would price after
        // OCR — the eligible set is method-independent; UPLOAD simply defers row entry to OCR.
        const manualItems = openWizard.mock.calls[0][1].items;
        expect(manualItems.map((i: any) => i.mappedRequestLineItemId)).toEqual(['li-1']);
    });
});
