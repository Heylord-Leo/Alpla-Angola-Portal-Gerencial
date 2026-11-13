import { describe, it, expect } from 'vitest';
import { describeAdvanceObligation, resolveDecisionCoverage } from './advanceObligationNotice';

// The thousands separator of the pt-AO locale differs between Node's ICU (space / narrow space) and browsers
// (dot); the wording is asserted with group separators stripped so the suite is environment-independent.
const norm = (s: string) => s.replace(/(\d)[\s.  ](?=\d{3})/g, '$1');

// DEV round 1 (2026-11-12): the "100% Antecipado" notice must state the total advance, the credit coverage and
// the real bank advance consistently — the observed cases were 11 274,23 RND fully covered (bank 0) and
// 15 840,00 RND with 10 000,00 RND credit (bank 5 840,00).
describe('describeAdvanceObligation', () => {
    it('keeps the original wording when no credit is selected', () => {
        const n = describeAdvanceObligation(15840, null, 'RND');
        expect(n.kind).toBe('NO_CREDIT');
        expect(n.bank).toBe(15840);
        expect(norm(n.text)).toBe('O valor total de 15840,00 RND será exigido como adiantamento.');
    });

    it('states full credit coverage with zero bank advance', () => {
        const n = describeAdvanceObligation(11274.23, 11274.23, 'RND');
        expect(n.kind).toBe('FULL_CREDIT');
        expect(n.credit).toBe(11274.23);
        expect(n.bank).toBe(0);
        expect(norm(n.text)).toContain('Adiantamento total de 11274,23 RND');
        expect(norm(n.text)).toContain('coberto integralmente por crédito em conta corrente (11274,23 RND)');
        expect(n.text).toContain('Não há adiantamento bancário');
    });

    it('states the split for partial credit coverage', () => {
        const n = describeAdvanceObligation(15840, 10000, 'RND');
        expect(n.kind).toBe('PARTIAL_CREDIT');
        expect(n.credit).toBe(10000);
        expect(n.bank).toBe(5840);
        expect(norm(n.text)).toBe('Adiantamento total de 15840,00 RND: 10000,00 RND cobertos por crédito em conta corrente e 5840,00 RND a adiantar por banco.');
    });

    it('clamps a provisional over-coverage to the total (never a negative bank part)', () => {
        const n = describeAdvanceObligation(1000, 1500, 'AOA');
        expect(n.kind).toBe('FULL_CREDIT');
        expect(n.credit).toBe(1000);
        expect(n.bank).toBe(0);
    });

    it('treats a zero or invalid coverage as no credit', () => {
        expect(describeAdvanceObligation(500, 0, 'AOA').kind).toBe('NO_CREDIT');
        expect(describeAdvanceObligation(500, NaN, 'AOA').kind).toBe('NO_CREDIT');
    });
});

describe('resolveDecisionCoverage', () => {
    it('reads the first-registration decision', () => {
        expect(resolveDecisionCoverage({ useCredit: true, amount: 350000, correctionAction: null })).toBe(350000);
        expect(resolveDecisionCoverage({ useCredit: false, amount: 350000, correctionAction: null })).toBeNull();
        expect(resolveDecisionCoverage({ useCredit: true, amount: null, correctionAction: null })).toBeNull();
    });

    it('reads the correction decision: Manter / Alterar keep the hold, Remover drops it', () => {
        expect(resolveDecisionCoverage({ useCredit: false, amount: 1000, correctionAction: 'KEEP' })).toBe(1000);
        expect(resolveDecisionCoverage({ useCredit: false, amount: 1500, correctionAction: 'CHANGE' })).toBe(1500);
        expect(resolveDecisionCoverage({ useCredit: false, amount: null, correctionAction: 'REMOVE' })).toBeNull();
        expect(resolveDecisionCoverage({ useCredit: true, amount: 1000, correctionAction: 'REMOVE' })).toBeNull();
    });

    it('is null without a decision', () => {
        expect(resolveDecisionCoverage(null)).toBeNull();
        expect(resolveDecisionCoverage(undefined)).toBeNull();
    });
});
