import { describe, it, expect } from 'vitest';
import { SUPPLIER_CREDIT_EVENT_LABELS, resolveSupplierCreditEventLabel } from './workflowEventLabels';
import { HISTORY_ACTION_LABELS } from '../pages/Requests/components/print/requestPrintModel';

// DEV round 1 (2026-11-12): the request history showed raw SUPPLIER_CREDIT_* codes while Finanças → Histórico
// already had Portuguese labels. Every renderer now reads the same map.
describe('supplier credit event labels', () => {
    it('covers the five audit codes written by the backend', () => {
        expect(Object.keys(SUPPLIER_CREDIT_EVENT_LABELS).sort()).toEqual([
            'SUPPLIER_CREDIT_APPLIED',
            'SUPPLIER_CREDIT_RELEASED',
            'SUPPLIER_CREDIT_RESERVATION_CHANGED',
            'SUPPLIER_CREDIT_RESERVED',
            'SUPPLIER_CREDIT_REVERSED',
        ]);
        expect(resolveSupplierCreditEventLabel('SUPPLIER_CREDIT_RESERVED')).toBe('Crédito reservado');
        expect(resolveSupplierCreditEventLabel('SUPPLIER_CREDIT_APPLIED')).toBe('Crédito aplicado');
        expect(resolveSupplierCreditEventLabel('SUPPLIER_CREDIT_RELEASED')).toBe('Reserva de crédito libertada');
        expect(resolveSupplierCreditEventLabel('SUPPLIER_CREDIT_REVERSED')).toBe('Crédito revertido');
    });

    it('returns null for any other code (the renderers keep their own fallback)', () => {
        expect(resolveSupplierCreditEventLabel('PAYMENT_COMPLETED')).toBeNull();
        expect(resolveSupplierCreditEventLabel(null)).toBeNull();
    });

    it('is reused by the Print View history map', () => {
        for (const [code, label] of Object.entries(SUPPLIER_CREDIT_EVENT_LABELS)) {
            expect(HISTORY_ACTION_LABELS[code]).toBe(label);
        }
    });
});
