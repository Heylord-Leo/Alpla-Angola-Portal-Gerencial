import { describe, it, expect } from 'vitest';
import {
    ADJUSTMENT_REASONS,
    reasonLabel,
    isItemRequired,
    buildAdjustmentReasons,
    validateAdjustmentSelection,
    batchStatusLabel,
    cycleStateLabel,
    sourceStageLabel,
    cycleResponsibleLabel,
    affectedItemLabel,
} from './adjustmentReasons';

// Adjustment V2 (Phase 3) — pure logic behind the approver "Solicitar Reajuste" structured step.
// The picker UI is thin over these functions (the node/vitest env has no DOM), so this is where the
// selection → payload mapping, validation, and label vocabulary are pinned.

describe('adjustment reason catalog', () => {
    it('matches the approved 9 buyer + 6 requester catalog', () => {
        expect(ADJUSTMENT_REASONS.filter(r => r.owner === 'BUYER')).toHaveLength(9);
        expect(ADJUSTMENT_REASONS.filter(r => r.owner === 'REQUESTER')).toHaveLength(6);
        // SUPPLIER and SUPPLIER_DELIVERY_TIME are distinct catalog codes (decision OD5).
        expect(ADJUSTMENT_REASONS.find(r => r.code === 'SUPPLIER')).toBeTruthy();
        expect(ADJUSTMENT_REASONS.find(r => r.code === 'SUPPLIER_DELIVERY_TIME')).toBeTruthy();
    });

    it('renders friendly labels, never raw codes', () => {
        for (const r of ADJUSTMENT_REASONS) {
            expect(r.label).toBeTruthy();
            expect(r.label).not.toEqual(r.code);
            expect(r.label).not.toMatch(/_/); // codes are SCREAMING_SNAKE_CASE
        }
        expect(reasonLabel('PRICE_NEGOTIATION')).toBe('Preço / negociação');
        expect(reasonLabel('SUPPLIER_DELIVERY_TIME')).toBe('Prazo de entrega do fornecedor');
    });

    it('marks exactly the item-required reasons', () => {
        expect(['REQUESTED_QUANTITY', 'SPECIFICATION', 'REQUESTED_UNIT', 'REMOVE_REQUEST_ITEM'].every(isItemRequired)).toBe(true);
        expect(['PRICE_NEGOTIATION', 'NEW_QUOTATION', 'NEEDED_BY_DATE', 'MISSING_ITEM', 'OTHER'].some(isItemRequired)).toBe(false);
    });
});

describe('buildAdjustmentReasons', () => {
    it('whole-lot when only buyer reasons are selected (null item, wholeBatch true)', () => {
        const { wholeBatch, reasons } = buildAdjustmentReasons(['PRICE_NEGOTIATION', 'NEW_QUOTATION'], []);
        expect(wholeBatch).toBe(true);
        expect(reasons).toEqual([
            { reasonCode: 'PRICE_NEGOTIATION', requestLineItemId: null },
            { reasonCode: 'NEW_QUOTATION', requestLineItemId: null },
        ]);
    });

    it('emits one row per item for item-required reasons and keeps others whole-lot', () => {
        const { wholeBatch, reasons } = buildAdjustmentReasons(['REQUESTED_QUANTITY', 'PRICE_NEGOTIATION'], ['item-a', 'item-b']);
        expect(wholeBatch).toBe(false); // an item-required reason forces item scoping
        expect(reasons).toContainEqual({ reasonCode: 'REQUESTED_QUANTITY', requestLineItemId: 'item-a' });
        expect(reasons).toContainEqual({ reasonCode: 'REQUESTED_QUANTITY', requestLineItemId: 'item-b' });
        expect(reasons).toContainEqual({ reasonCode: 'PRICE_NEGOTIATION', requestLineItemId: null });
        expect(reasons).toHaveLength(3);
    });
});

describe('validateAdjustmentSelection', () => {
    it('requires a comment', () => {
        expect(validateAdjustmentSelection(['PRICE_NEGOTIATION'], [], '   ')).toMatch(/coment/i);
    });
    it('requires at least one reason', () => {
        expect(validateAdjustmentSelection([], [], 'motivo')).toMatch(/motivo/i);
    });
    it('requires an item for item-required reasons', () => {
        expect(validateAdjustmentSelection(['REQUESTED_QUANTITY'], [], 'preciso alterar a quantidade')).toMatch(/item/i);
    });
    it('passes for a valid buyer whole-lot selection', () => {
        expect(validateAdjustmentSelection(['PRICE_NEGOTIATION'], [], 'valor acima do orçamento')).toBeNull();
    });
    it('passes for a valid item-scoped selection', () => {
        expect(validateAdjustmentSelection(['REQUESTED_QUANTITY'], ['item-a'], 'reduzir a quantidade')).toBeNull();
    });
});

describe('batch-details friendly labels (Phase 3 quick improvement)', () => {
    it('maps ApprovalBatch.Status to friendly PT labels, never raw codes', () => {
        expect(batchStatusLabel('WAITING_AREA_APPROVAL')).toBe('Aguardando Aprovação da Área');
        expect(batchStatusLabel('AREA_ADJUSTMENT')).toBe('Reajuste solicitado na Aprovação da Área');
        expect(batchStatusLabel('WAITING_FINAL_APPROVAL')).toBe('Aguardando Aprovação Final');
        expect(batchStatusLabel('FINAL_ADJUSTMENT')).toBe('Reajuste solicitado na Aprovação Final');
        expect(batchStatusLabel('CANCELLED')).toBe('Cancelado');
        for (const s of ['WAITING_AREA_APPROVAL', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT', 'CANCELLED']) {
            expect(batchStatusLabel(s)).not.toMatch(/_/);
        }
    });

    it('maps cycle state, source stage, and responsibility to friendly labels', () => {
        expect(cycleStateLabel('WAITING_BUYER')).toBe('Aguardando ação do Comprador');
        expect(cycleStateLabel('WAITING_REQUESTER')).toBe('Aguardando ação do Solicitante');
        expect(sourceStageLabel('AREA')).toBe('Aprovação de Área');
        expect(sourceStageLabel('FINAL')).toBe('Aprovação Final');
        expect(cycleResponsibleLabel('WAITING_BUYER')).toBe('Comprador');
        expect(cycleResponsibleLabel('WAITING_REQUESTER')).toBe('Solicitante');
    });
});

describe('affectedItemLabel — business-readable affected-item labels (Scenario C)', () => {
    it('builds "#<line> — <code> — <description>" from all fields', () => {
        expect(affectedItemLabel({ lineNumber: 2, itemCatalogCode: 'ITM-001', description: 'Laptop Dell Latitude 5440' }))
            .toBe('#2 — ITM-001 — Laptop Dell Latitude 5440');
    });

    it('collapses absent parts (line + description, or code + description)', () => {
        expect(affectedItemLabel({ lineNumber: 5, description: 'Monitor 24"' })).toBe('#5 — Monitor 24"');
        expect(affectedItemLabel({ itemCatalogCode: 'ITM-9', description: 'Cabo HDMI' })).toBe('ITM-9 — Cabo HDMI');
    });

    it('is never a bare "Item" when any business identifier is present, and never a GUID', () => {
        for (const it of [
            { lineNumber: 3 },
            { description: 'Teclado' },
            { itemCatalogCode: 'ITM-7' },
            { lineNumber: 1, itemCatalogCode: 'ITM-1', description: 'X' },
        ]) {
            const label = affectedItemLabel(it as any);
            expect(label).not.toBe('Item');
            expect(label).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}/i); // no GUID
        }
    });

    it('only falls back to a plain label when nothing is available', () => {
        expect(affectedItemLabel({})).toBe('Item');
        expect(affectedItemLabel({ lineNumber: 4, description: null, itemCatalogCode: null })).toBe('#4');
    });
});
