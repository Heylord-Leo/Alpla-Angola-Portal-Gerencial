import { describe, it, expect } from 'vitest';
import { BUYER_REQUEST_WORKSPACE_STEPS } from './buyerRequestWorkspaceTour';
import { TOUR_REGISTRY } from '../guidedTourRegistry';
import workspaceSrc from '../../../pages/Buyer/BuyerRequestWorkspace.tsx?raw';

// Page tour for /buyer/requests/:id (added with the Workspace quotation Edit/Delete actions). Every step targets a
// stable data-tour anchor that exists in the page source; dynamic steps (quotations table, actions cell) are skipped
// by filterActiveSteps when absent, so restricted users or empty data never see a target error.
describe('page-buyer-request-workspace tour', () => {
    it('is registered as a page tour on the Buyer request workspace route', () => {
        const def = TOUR_REGISTRY.find(t => t.id === 'page-buyer-request-workspace');
        expect(def).toBeDefined();
        expect(def!.level).toBe('page');
        expect(def!.routes).toEqual(['/buyer/requests']);
        expect(def!.steps).toBe(BUYER_REQUEST_WORKSPACE_STEPS);
    });

    it('every step targets an anchor present in BuyerRequestWorkspace.tsx and never dynamic text', () => {
        for (const step of BUYER_REQUEST_WORKSPACE_STEPS) {
            const target = String(step.target);
            const m = /^\[data-tour="([a-z-]+)"\]$/.exec(target);
            expect(m, `selector shape: ${target}`).not.toBeNull();
            expect(workspaceSrc).toContain(`data-tour="${m![1]}"`);
            expect(step.title && step.content).toBeTruthy();
        }
    });

    it('explains the quotation actions and their restrictions (assigned Buyer, quotation window, batch refusal, document consequence)', () => {
        const actions = BUYER_REQUEST_WORKSPACE_STEPS.find(s => String(s.target).includes('quotation-actions'));
        const text = String(actions!.content);
        expect(text).toContain('Editar');
        expect(text).toContain('Excluir');
        expect(text).toContain('Comprador responsável');
        expect(text).toContain('lote de aprovação');
        expect(text).toContain('documento associado');
    });
});
