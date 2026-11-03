// ─────────────────────────────────────────────────────────────────────────────
// v2.249.6 — what the P.O. wizards do with the payment condition the extraction proposes.
//
// The provider classifies the document's payment-terms text into POST_PAID | ADVANCE_FULL | ADVANCE_PARTIAL with a confidence
// (ExtractionMapper: status "recommended" ≥ 0.70, else "suggested") and returns the raw text. Both wizards used to PRE-SELECT the
// classified value whatever the confidence and whatever the document — in TEST on REQ-21/10/2026-595 a PROFORMA (refused as a
// P.O.) still pre-selected "Pós-Pago" from "C001 - pagamento imediato s/desconto".
//
// Rules here (pure):
//   • a document the server refuses as a P.O. proposes nothing — its terms are not the order's terms;
//   • "pagamento imediato" / "pronto pagamento" / "a pronto" / "cash" wording is NOT auto-applied: whether "immediate" means
//     payment at delivery (Pós-Pago) or payment before dispatch (Antecipado) is a business reading the Portal does not make for
//     the Buyer, and it never implies an advance percentage. The raw text is shown; the Buyer selects;
//   • a low-confidence classification ("suggested") is shown, not applied;
//   • an ADVANCE_PARTIAL percentage is applied only when the classification itself is applied, clamped to 1–99.
// ─────────────────────────────────────────────────────────────────────────────

export const PAYMENT_CONDITION_CODES = ['POST_PAID', 'ADVANCE_FULL', 'ADVANCE_PARTIAL'] as const;
export type PaymentConditionCode = typeof PAYMENT_CONDITION_CODES[number];

export interface OcrPaymentConditionInput {
    code?: string | null;
    /** ExtractionMapper status: "recommended" (confidence ≥ 0.70) | "suggested". */
    status?: string | null;
    rawText?: string | null;
    advancePercent?: number | string | null;
}

export type PaymentConditionProposalReason = 'APPLIED' | 'NONE' | 'DOCUMENT_REFUSED' | 'AMBIGUOUS_WORDING' | 'LOW_CONFIDENCE' | 'UNKNOWN_CODE';

export interface PaymentConditionProposal {
    /** True when the wizard may pre-select `code` (the Buyer can still change it). */
    apply: boolean;
    code: PaymentConditionCode | null;
    advancePercent: number | null;
    rawText: string | null;
    reason: PaymentConditionProposalReason;
    /** pt-PT sentence shown under the selector when the proposal is NOT applied but the document said something. */
    hint: string | null;
}

/** Matched on the accent-stripped, lower-cased text with ASCII word boundaries ("à vista" → "a vista"; "PP" alone is NOT a term:
 * it is also the Primavera proforma prefix). */
const IMMEDIATE_PAYMENT_WORDING = /(?:^|[^a-z])(pagamento\s+imediato|pronto\s+pagamento|a\s+pronto|cash|contado|a\s+vista|imediat[oa])(?:$|[^a-z])/;

export function isImmediatePaymentWording(rawText: string | null | undefined): boolean {
    if (!rawText) return false;
    const normalized = rawText.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    return IMMEDIATE_PAYMENT_WORDING.test(normalized);
}

function clampPercent(v: unknown): number | null {
    const n = Math.round(Number(v));
    if (!Number.isFinite(n)) return null;
    return Math.max(1, Math.min(99, n));
}

export function resolveOcrPaymentConditionProposal(input: OcrPaymentConditionInput, documentRefused: boolean): PaymentConditionProposal {
    const rawText = typeof input.rawText === 'string' && input.rawText.trim() ? input.rawText.trim() : null;
    const code = (PAYMENT_CONDITION_CODES as readonly string[]).includes(String(input.code ?? '')) ? (input.code as PaymentConditionCode) : null;

    if (documentRefused) {
        return { apply: false, code: null, advancePercent: null, rawText, reason: 'DOCUMENT_REFUSED', hint: rawText ? `Condição lida num documento que não é a P.O. («${rawText}»): não foi aplicada. Selecione a condição acordada na Ordem de Compra.` : null };
    }
    if (!code) {
        return { apply: false, code: null, advancePercent: null, rawText, reason: input.code ? 'UNKNOWN_CODE' : 'NONE', hint: rawText ? `Condição lida no documento: «${rawText}». Não foi classificada automaticamente; selecione manualmente.` : null };
    }
    if (isImmediatePaymentWording(rawText)) {
        return { apply: false, code, advancePercent: null, rawText, reason: 'AMBIGUOUS_WORDING', hint: `Condição lida no documento: «${rawText}». "Pagamento imediato" não indica por si se o pagamento é antes do envio (antecipado) ou na entrega (pós-pago) nem uma percentagem de adiantamento: selecione a condição acordada.` };
    }
    if ((input.status || '').toLowerCase() === 'suggested') {
        return { apply: false, code, advancePercent: null, rawText, reason: 'LOW_CONFIDENCE', hint: `Condição lida no documento${rawText ? ` («${rawText}»)` : ''} com baixa confiança: sugestão ${labelFor(code)}. Confirme selecionando manualmente.` };
    }
    return { apply: true, code, advancePercent: code === 'ADVANCE_PARTIAL' ? clampPercent(input.advancePercent) : null, rawText, reason: 'APPLIED', hint: null };
}

export function labelFor(code: PaymentConditionCode): string {
    switch (code) {
        case 'POST_PAID': return 'Pós-Pago';
        case 'ADVANCE_FULL': return '100% Antecipado';
        case 'ADVANCE_PARTIAL': return 'Antecipado Parcial';
    }
}
