import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards.
import modalSrc from './BatchReworkModal.tsx?raw';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';
import classicSrc from './BuyerItemsList.tsx?raw';
import controllerSrc from './QuotationWizard/buyerQuotationWizardController.ts?raw';
import hostSrc from './QuotationWizard/hooks/useWorkspaceWizardHost.ts?raw';
import apiSrc from '../../lib/api.ts?raw';

// Phase 4 (Revised Quotation): "Gerenciar Cotações" resolves the batch's CONTRIBUTING quotation(s)
// and opens the chosen one as a REVISION — seeded from the original but persisted as a NEW quotation
// identity (SaveQuotation add) with reworkBatchId. The original quotation / frozen candidate are never
// mutated. One → direct, many → chooser, zero → controlled feedback.

describe('Phase 4 — modal resolves quotations and delegates with reworkBatchId', () => {
    it('the button resolves the batch quotations and passes them up with batch.id', () => {
        expect(modalSrc).toMatch(/onManageQuotations\?\.\(resolveBatchContributingQuotations\(batch, \(group\?\.quotations as SavedQuotationDto\[\]\) \|\| \[\]\), batch\.id\)/);
    });
    it('the modal embeds no quotation editor and does not open the wizard itself', () => {
        // (importing a pure helper from the QuotationWizard folder is fine; opening the wizard is not)
        expect(modalSrc).not.toMatch(/openAddQuotation|openReviseQuotation|handleOpenWizard/);
    });
});

describe('Phase 4 — Workspace host revises for this request/batch', () => {
    it('one → opens a revision with ws.requestId and reworkBatchId', () => {
        expect(workspaceSrc).toMatch(/if \(quotations\.length === 1\) wizardHost\.openReviseQuotation\(ws\.requestId, quotations\[0\], reworkBatchId\)/);
    });
    it('many → chooser (carrying reworkBatchId); zero → controlled feedback; never a blank add', () => {
        expect(workspaceSrc).toMatch(/setQuoteChooser\(\{ quotations, reworkBatchId \}\)/);
        expect(workspaceSrc).toMatch(/else flash\('Não foi possível identificar a cotação associada a este lote\.'\)/);
        const wsCallback = workspaceSrc.match(/onManageQuotations=\{\(quotations, reworkBatchId\) => \{[\s\S]*?\}\}/)?.[0] ?? '';
        expect(wsCallback).not.toMatch(/openAddQuotation/);
    });
});

describe('Phase 4 — Classic host revises for the current group/batch', () => {
    it('one → handleOpenWizard with the quotation AND reworkBatchId (seed-as-NEW)', () => {
        expect(classicSrc).toMatch(/if \(quotations\.length === 1\) wizardController\.handleOpenWizard\(g, quotationEditMode\(quotations\[0\]\), quotations\[0\], reworkBatchId\)/);
    });
    it('many → chooser (carrying reworkBatchId); zero → controlled feedback; no bare close', () => {
        expect(classicSrc).toMatch(/setQuoteChooser\(\{ group: g, quotations, reworkBatchId \}\)/);
        expect(classicSrc).not.toMatch(/onManageQuotations=\{\(\) => setBatchReworkModal\(/);
    });
});

describe('Phase 4 — seed-as-NEW + reworkBatchId plumbing', () => {
    it('handleOpenWizard opens a NEW (not EDIT) wizard with revision provenance when reworkBatchId is present', () => {
        expect(controllerSrc).toMatch(/if \(reworkBatchId\) \{[\s\S]*?quotationWizardState\.openWizard\('NEW', draft, undefined, mode, reworkBatchId, editQuotation\.id\);/);
        expect(controllerSrc).toMatch(/quotationWizardState\.openWizard\('EDIT', draft, editQuotation\.id, mode\)/); // ordinary edit path preserved
    });
    it('save forwards reworkBatchId + revisesQuotationId to the ADD path (SaveQuotation), never UpdateQuotation', () => {
        expect(controllerSrc).toMatch(/api\.requests\.saveQuotation\(requestId, \{[\s\S]*?\}, undefined, quotationWizardState\.reworkBatchId \?\? undefined, quotationWizardState\.revisesQuotationId \?\? undefined\)/);
    });
    it('the host revision helper seeds from the original and forwards reworkBatchId', () => {
        expect(hostSrc).toMatch(/const openReviseQuotation = async \(requestId: string, quotation: SavedQuotationDto, reworkBatchId: string\)/);
        expect(hostSrc).toMatch(/controller\.handleOpenWizard\(toWizardActiveRequest\(request, requestId\), quotationEditMode\(quotation\), quotation, reworkBatchId\)/);
    });
    it('api.saveQuotation sends reworkBatchId + revisesQuotationId only when provided', () => {
        expect(apiSrc).toMatch(/saveQuotation: async \(requestId: string, quotation: any, replaceQuotationId\?: string, reworkBatchId\?: string, revisesQuotationId\?: string\)/);
        expect(apiSrc).toMatch(/if \(reworkBatchId\) qs\.set\('reworkBatchId', reworkBatchId\)/);
        expect(apiSrc).toMatch(/if \(revisesQuotationId\) qs\.set\('revisesQuotationId', revisesQuotationId\)/);
    });
});
