import { describe, it, expect } from 'vitest';
import { describeQuotationItemAdjudication, describeQuotationLifecycle } from './quotationLifecycle';

describe('quotationLifecycle — card level', () => {
    it('TREVOTECH shape: awarded, P.O. cancelled, items returned to quotation → historical', () => {
        const d = describeQuotationLifecycle({ lifecycleStatus: 'HISTORICAL_CANCELLED', historicalItemCount: 2, historicalBatchNumber: 1, historicalReleaseKind: 'RESTART_QUOTATION' });
        expect(d).toMatchObject({ tone: 'historical', isHistorical: true, label: 'Histórica — adjudicação cancelada' });
        expect(d!.detail).toContain('adjudicada (lote 1)');
        expect(d!.detail).toContain('os itens foram devolvidos à cotação');
        expect(d!.detail).not.toMatch(/ignorad/i);
    });
    it('CANCEL_ITEMS wording differs from RESTART_QUOTATION', () => {
        const d = describeQuotationLifecycle({ lifecycleStatus: 'HISTORICAL_CANCELLED', historicalItemCount: 1, historicalReleaseKind: 'CANCEL_ITEMS' });
        expect(d!.detail).toContain('o item foi encerrado');
    });
    it('partially historical names the counts', () => {
        const d = describeQuotationLifecycle({ lifecycleStatus: 'PARTIALLY_HISTORICAL', historicalItemCount: 1, activeAwardedItemCount: 1, quotedItemCount: 2, historicalBatchNumber: 2 });
        expect(d).toMatchObject({ tone: 'partial', isHistorical: false });
        expect(d!.label).toBe('Parcialmente histórica — 1 de 2 item(ns) com adjudicação cancelada');
    });
    it('awarded / in approval / not awarded / rejected / open', () => {
        expect(describeQuotationLifecycle({ lifecycleStatus: 'AWARDED', activeAwardedItemCount: 1 })).toMatchObject({ tone: 'awarded', label: 'Adjudicada · 1 item(ns)' });
        expect(describeQuotationLifecycle({ lifecycleStatus: 'IN_APPROVAL' })).toMatchObject({ tone: 'in-approval', label: 'Em aprovação' });
        expect(describeQuotationLifecycle({ lifecycleStatus: 'NOT_AWARDED' })).toMatchObject({ tone: 'not-awarded', label: 'Não adjudicada' });
        expect(describeQuotationLifecycle({ lifecycleStatus: 'REJECTED' })).toMatchObject({ tone: 'rejected' });
        expect(describeQuotationLifecycle({ lifecycleStatus: 'OPEN' })).toBeNull();
        expect(describeQuotationLifecycle({})).toBeNull();
    });
});

describe('quotationLifecycle — item level', () => {
    it('award cancelled + line re-quoted with another supplier + reuse blocked: history and eligibility kept apart', () => {
        const d = describeQuotationItemAdjudication({ adjudicationStatus: 'AWARD_CANCELLED', adjudicationBatchNumber: 1, lineCurrentState: 'REQUOTED_WITH_OTHER', lineCurrentSupplierName: 'MAQSIS', isReuseBlocked: true });
        expect(d).toMatchObject({ tone: 'historical', isHistorical: true, label: 'Adjudicação cancelada (lote 1)' });
        expect(d!.detail).toBe('Item devolvido à cotação e recotado com MAQSIS. Reutilização sujeita a autorização.');
    });
    it('reuse authorized replaces the "sujeita a autorização" sentence', () => {
        const d = describeQuotationItemAdjudication({ adjudicationStatus: 'AWARD_CANCELLED', lineCurrentState: 'PENDING_REQUOTE', isReuseAuthorized: true });
        expect(d!.detail).toBe('Item devolvido à cotação; nova cotação pendente. Reutilização autorizada.');
    });
    it('candidate of a returned batch, closed line', () => {
        const d = describeQuotationItemAdjudication({ adjudicationStatus: 'CANDIDATE_CANCELLED', adjudicationBatchNumber: 3, lineCurrentState: 'CLOSED_AFTER_APPROVAL' });
        expect(d!.label).toBe('Candidata — lote devolvido/cancelado (lote 3)');
        expect(d!.detail).toBe('Item encerrado após a aprovação.');
    });
    it('placeholders read "Não cotado", never "Ignorada"', () => {
        const d = describeQuotationItemAdjudication({ adjudicationStatus: 'NOT_QUOTED', reconciliationStatus: 'IGNORED' });
        expect(d!.label).toBe('Não cotado');
        expect(JSON.stringify(d)).not.toMatch(/ignorad/i);
    });
    it('awarded / in approval / not awarded / not submitted / rejected / unknown', () => {
        expect(describeQuotationItemAdjudication({ adjudicationStatus: 'AWARDED', adjudicationBatchNumber: 2 })!.label).toBe('Adjudicada (lote 2)');
        expect(describeQuotationItemAdjudication({ adjudicationStatus: 'IN_APPROVAL' })!.label).toBe('Em aprovação');
        expect(describeQuotationItemAdjudication({ adjudicationStatus: 'CANDIDATE_NOT_AWARDED', adjudicationBatchNumber: 1 })!.label).toBe('Candidata — não adjudicada (lote 1)');
        expect(describeQuotationItemAdjudication({ adjudicationStatus: 'NOT_SUBMITTED' })!.label).toBe('Não submetida');
        expect(describeQuotationItemAdjudication({ adjudicationStatus: 'REJECTED', adjudicationBatchNumber: 4 })!.label).toBe('Lote rejeitado (lote 4)');
        expect(describeQuotationItemAdjudication({})).toBeNull();
    });
});
