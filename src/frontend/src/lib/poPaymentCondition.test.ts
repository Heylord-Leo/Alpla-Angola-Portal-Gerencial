import { describe, it, expect } from 'vitest';
import { resolveOcrPaymentConditionProposal, isImmediatePaymentWording } from './poPaymentCondition';

// v2.249.6 — REQ-21/10/2026-595: the LUANDAY proforma states "C001 - pagamento imediato s/desconto"; the wizard pre-selected "Pós-Pago".

describe('resolveOcrPaymentConditionProposal', () => {
    it('a document refused as a P.O. proposes nothing, whatever the classification', () => {
        const p = resolveOcrPaymentConditionProposal({ code: 'POST_PAID', status: 'recommended', rawText: 'C001 - pagamento imediato s/desconto' }, true);
        expect(p.apply).toBe(false);
        expect(p.code).toBeNull();
        expect(p.reason).toBe('DOCUMENT_REFUSED');
        expect(p.hint).toContain('não é a P.O.');
    });

    it('"pagamento imediato" is never auto-applied and never implies an advance percentage', () => {
        const p = resolveOcrPaymentConditionProposal({ code: 'POST_PAID', status: 'recommended', rawText: 'C001 - pagamento imediato s/desconto' }, false);
        expect(p.apply).toBe(false);
        expect(p.reason).toBe('AMBIGUOUS_WORDING');
        expect(p.code).toBe('POST_PAID');            // shown as what the extraction read, not selected
        expect(p.advancePercent).toBeNull();
        expect(p.hint).toContain('C001 - pagamento imediato s/desconto');
        expect(p.hint).toContain('selecione');
        const adv = resolveOcrPaymentConditionProposal({ code: 'ADVANCE_PARTIAL', status: 'recommended', rawText: 'Pronto pagamento', advancePercent: 50 }, false);
        expect(adv.apply).toBe(false);
        expect(adv.advancePercent).toBeNull();
    });

    it('recognises the immediate-payment wordings', () => {
        for (const t of ['pagamento imediato', 'PRONTO PAGAMENTO', 'Pagamento a pronto', 'Cash', 'À vista', 'Contado'])
            expect(isImmediatePaymentWording(t)).toBe(true);
        for (const t of ['30 dias', 'Pagamento após entrega', '50% adiantado e 50% após entrega', null, ''])
            expect(isImmediatePaymentWording(t)).toBe(false);
    });

    it('applies a confident, unambiguous classification (with the clamped advance percentage for ADVANCE_PARTIAL)', () => {
        expect(resolveOcrPaymentConditionProposal({ code: 'POST_PAID', status: 'recommended', rawText: 'Pagamento a 30 dias' }, false))
            .toMatchObject({ apply: true, code: 'POST_PAID', advancePercent: null, reason: 'APPLIED', hint: null });
        expect(resolveOcrPaymentConditionProposal({ code: 'ADVANCE_PARTIAL', status: 'recommended', rawText: '50% adiantado e 50% na entrega', advancePercent: '50' }, false))
            .toMatchObject({ apply: true, code: 'ADVANCE_PARTIAL', advancePercent: 50 });
        expect(resolveOcrPaymentConditionProposal({ code: 'ADVANCE_PARTIAL', status: 'recommended', rawText: '30% antes e 70% após', advancePercent: 130 }, false).advancePercent).toBe(99);
        expect(resolveOcrPaymentConditionProposal({ code: 'ADVANCE_FULL', status: 'recommended', rawText: '100% antecipado' }, false)).toMatchObject({ apply: true, code: 'ADVANCE_FULL', advancePercent: null });
    });

    it('a low-confidence ("suggested") classification is shown, not applied', () => {
        const p = resolveOcrPaymentConditionProposal({ code: 'POST_PAID', status: 'suggested', rawText: 'Condições habituais' }, false);
        expect(p.apply).toBe(false);
        expect(p.reason).toBe('LOW_CONFIDENCE');
        expect(p.hint).toContain('Pós-Pago');
    });

    it('no classification / unknown code: nothing applied, the raw text (if any) is shown', () => {
        expect(resolveOcrPaymentConditionProposal({ code: null, status: null, rawText: null }, false)).toMatchObject({ apply: false, code: null, reason: 'NONE', hint: null });
        const p = resolveOcrPaymentConditionProposal({ code: 'NET30', status: 'recommended', rawText: 'Net 30' }, false);
        expect(p).toMatchObject({ apply: false, code: null, reason: 'UNKNOWN_CODE' });
        expect(p.hint).toContain('Net 30');
    });
});
