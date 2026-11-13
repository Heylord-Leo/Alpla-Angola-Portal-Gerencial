import { formatCurrencyAO } from './utils';
import type { SupplierCreditDecision } from '../components/po/SupplierCreditCoverageBlock';

/**
 * "100% Antecipado" notice of the P.O. wizards (initial registration and correction).
 *
 * DEV round 1 feedback (2026-11-12): the orange warning always said the FULL P.O. amount would be required as an
 * advance, while the supplier-credit summary below it showed a lower bank amount. The notice now states the three
 * figures consistently: the total advance obligation, the part covered by supplier account credit and the actual
 * bank advance. Without credit the original wording is kept unchanged.
 *
 * Pure presentation: the server re-validates the coverage (availability, normalisation, flag); the figures here are
 * the Buyer's current decision, clamped to the P.O. total so a provisional over-coverage never shows a negative bank
 * part (the coverage block itself flags the invalid amount).
 */
export interface AdvanceObligationNotice {
    total: number;
    credit: number;
    bank: number;
    kind: 'NO_CREDIT' | 'FULL_CREDIT' | 'PARTIAL_CREDIT';
    text: string;
}

/** The credit coverage the Buyer's current wizard decision implies, or null when the P.O. is paid by bank only. */
export function resolveDecisionCoverage(decision: SupplierCreditDecision | null | undefined): number | null {
    if (!decision) return null;
    if (decision.correctionAction === 'REMOVE') return null;
    const active = decision.correctionAction === 'KEEP' || decision.correctionAction === 'CHANGE' || decision.useCredit;
    if (!active) return null;
    return decision.amount != null && isFinite(decision.amount) && decision.amount > 0 ? decision.amount : null;
}

export function describeAdvanceObligation(total: number, creditCoverage: number | null | undefined, currencyCode: string | null | undefined): AdvanceObligationNotice {
    const ccy = currencyCode || '';
    const money = (v: number) => `${formatCurrencyAO(v)} ${ccy}`.trim();
    const safeTotal = isFinite(total) && total > 0 ? Math.round(total * 100) / 100 : 0;
    const credit = creditCoverage != null && isFinite(creditCoverage) && creditCoverage > 0
        ? Math.min(safeTotal, Math.round(creditCoverage * 100) / 100)
        : 0;
    const bank = Math.max(0, Math.round((safeTotal - credit) * 100) / 100);

    if (credit <= 0) {
        return { total: safeTotal, credit: 0, bank: safeTotal, kind: 'NO_CREDIT', text: `O valor total de ${money(safeTotal)} será exigido como adiantamento.` };
    }
    if (bank <= 0) {
        return {
            total: safeTotal, credit, bank: 0, kind: 'FULL_CREDIT',
            text: `Adiantamento total de ${money(safeTotal)}, coberto integralmente por crédito em conta corrente (${money(credit)}). Não há adiantamento bancário.`,
        };
    }
    return {
        total: safeTotal, credit, bank, kind: 'PARTIAL_CREDIT',
        text: `Adiantamento total de ${money(safeTotal)}: ${money(credit)} cobertos por crédito em conta corrente e ${money(bank)} a adiantar por banco.`,
    };
}
