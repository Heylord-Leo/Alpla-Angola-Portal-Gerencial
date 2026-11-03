// ─────────────────────────────────────────────────────────────────────────────
// v2.249.6 — quotation pricing rules shared by the Buyer wizard, the saved-quotation cards and the batch modal.
//
//   1. LINE-RESOLVED PRICING (mirror of the Domain QuotationLinePricingPolicy, which the batch endpoints enforce with
//      409 QUOTATION_PRICING_NOT_LINE_RESOLVED): everything after the quotation prices PER LINE (candidate snapshot, P.O. group
//      total, approved amount). A quotation-level ("global") discount — SavedQuotationDto.discountAmount — is applied only to the
//      quotation's own totals; no line carries a share of it. Such a quotation cannot be submitted for approval. Found in TEST on
//      REQ-21/10/2026-595: 416.850,00 approved for a 345.775,50 quotation.
//   2. OCR DISCOUNT RESOLUTION (used by useOcrProcessor when the draft is built): the per-line discount against the document's
//      own line total, flagged "uncertain" instead of silently rewritten; the header "Desconto Comercial" recognised as the
//      aggregate of the line discounts (never a second discount) or kept as a genuine document-level discount.
//   3. EXPLICIT ALLOCATION of a global discount to the lines (the wizard's "Distribuir pelas linhas"): proportional to each
//      considered line's net, largest-remainder rounding so the shares add up exactly; the per-line adjustment reason is
//      pre-filled with the arithmetic and stays editable. Never automatic.
//
// Pure, node-vitest friendly: no React, no fetch. Docs: docs/QUOTATION_DISCOUNT_PRICING_AND_PO_WIZARD_REFUSAL_v2.249.6.md.
// ─────────────────────────────────────────────────────────────────────────────

export const QUOTATION_PRICING_NOT_LINE_RESOLVED = 'QUOTATION_PRICING_NOT_LINE_RESOLVED';
export const LINE_SUM_TOLERANCE = 1.0;
export const CONSIDERED_RECONCILIATION_STATUSES = ['MAPPED', 'SUBSTITUTE', 'EXTRA_ITEM'];

export type LinePricingReason = 'GLOBAL_DISCOUNT_UNALLOCATED' | 'LINE_TOTAL_MISMATCH';
/** Explicit classification (mirror of QuotationLinePricingPolicy.Kinds). LEGACY_TOTAL_NOT_COMPUTED = total 0 with positive
 *  considered lines: a legacy row the current write path cannot produce (server-computed totals); resolved for compatibility,
 *  never mistaken for ZERO_VALUE (no considered value at all) nor for incomplete pricing. */
export type LinePricingKind = 'LINE_RESOLVED' | 'ZERO_VALUE' | 'LEGACY_TOTAL_NOT_COMPUTED' | LinePricingReason;

export interface LinePricingEvaluation {
    isLineResolved: boolean;
    reasonCode: LinePricingReason | null;
    kind: LinePricingKind;
    quotationTotal: number;
    globalDiscount: number;
    consideredLineTotalSum: number;
    /** consideredLineTotalSum − quotationTotal (signed). */
    difference: number;
    /** 'SERVER' when the DTO carried the server evaluation, 'CLIENT' when derived here from the DTO figures. */
    source: 'SERVER' | 'CLIENT';
}

export interface QuotationPricingLike {
    discountAmount?: number | null;
    totalAmount?: number | null;
    isLinePricingResolved?: boolean | null;
    linePricingReasonCode?: string | null;
    linePricingKind?: string | null;
    consideredLineTotalSum?: number | null;
    items?: Array<{ lineTotal?: number | null; reconciliationStatus?: string | null }> | null;
}

const round2 = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100;

export function isConsideredStatus(status: string | null | undefined): boolean {
    return !status || CONSIDERED_RECONCILIATION_STATUSES.includes(status);
}

/** The line-resolved verdict. Prefers the server evaluation when the DTO carries it; derives it from the figures otherwise. */
export function evaluateQuotationLinePricing(q: QuotationPricingLike): LinePricingEvaluation {
    const globalDiscount = Number(q.discountAmount) || 0;
    const quotationTotal = Number(q.totalAmount) || 0;
    const derivedSum = round2((q.items ?? []).filter(i => isConsideredStatus(i.reconciliationStatus)).reduce((s, i) => s + (Number(i.lineTotal) || 0), 0));
    const sum = typeof q.consideredLineTotalSum === 'number' ? q.consideredLineTotalSum : derivedSum;
    const difference = round2(sum - quotationTotal);

    if (typeof q.isLinePricingResolved === 'boolean') {
        const reasonCode = (q.linePricingReasonCode as LinePricingReason | null) ?? null;
        const kind = (q.linePricingKind as LinePricingKind | null) ?? (reasonCode ?? classifyResolved(quotationTotal, sum));
        return {
            isLineResolved: q.isLinePricingResolved,
            reasonCode, kind,
            quotationTotal, globalDiscount, consideredLineTotalSum: sum, difference, source: 'SERVER',
        };
    }
    if (globalDiscount > 0) {
        return { isLineResolved: false, reasonCode: 'GLOBAL_DISCOUNT_UNALLOCATED', kind: 'GLOBAL_DISCOUNT_UNALLOCATED', quotationTotal, globalDiscount, consideredLineTotalSum: sum, difference, source: 'CLIENT' };
    }
    if (quotationTotal > 0 && Math.abs(difference) > LINE_SUM_TOLERANCE) {
        return { isLineResolved: false, reasonCode: 'LINE_TOTAL_MISMATCH', kind: 'LINE_TOTAL_MISMATCH', quotationTotal, globalDiscount, consideredLineTotalSum: sum, difference, source: 'CLIENT' };
    }
    return { isLineResolved: true, reasonCode: null, kind: classifyResolved(quotationTotal, sum), quotationTotal, globalDiscount, consideredLineTotalSum: sum, difference, source: 'CLIENT' };
}

/** The resolved kinds: a positive total is LINE_RESOLVED; a zero total is ZERO_VALUE (no considered value) or LEGACY_TOTAL_NOT_COMPUTED. */
function classifyResolved(quotationTotal: number, consideredSum: number): LinePricingKind {
    if (quotationTotal > 0) return 'LINE_RESOLVED';
    return consideredSum > 0 ? 'LEGACY_TOTAL_NOT_COMPUTED' : 'ZERO_VALUE';
}

export interface QuotationPricingState {
    /** Short chip text. */
    label: string;
    /** One sentence for the title / detail line. */
    detail: string;
    evaluation: LinePricingEvaluation;
}

function money(v: number, currency: string): string {
    return `${new Intl.NumberFormat('pt-AO', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v)} ${currency}`;
}

/** Null when the quotation is line-resolved (no chip). */
export function describeQuotationPricingState(q: QuotationPricingLike, currency: string): QuotationPricingState | null {
    const e = evaluateQuotationLinePricing(q);
    if (e.isLineResolved) return null;
    if (e.reasonCode === 'GLOBAL_DISCOUNT_UNALLOCATED') {
        return {
            label: 'Desconto global não distribuído',
            detail: `Desconto comercial global de ${money(e.globalDiscount, currency)} aplicado só ao total da cotação (${money(e.quotationTotal, currency)}); as linhas somam ${money(e.consideredLineTotalSum, currency)}. Não pode ser enviada para aprovação até o desconto estar distribuído pelas linhas (Editar → Desconto Comercial Global → Distribuir pelas linhas).`,
            evaluation: e,
        };
    }
    return {
        label: 'Totais por linha divergentes',
        detail: `A soma das linhas (${money(e.consideredLineTotalSum, currency)}) não coincide com o total da cotação (${money(e.quotationTotal, currency)}). Abra a cotação em Editar e guarde-a para recalcular os totais antes de a enviar para aprovação.`,
        evaluation: e,
    };
}

/** Reads the batch endpoint's 409 (ApiError.details = ProblemDetails) into the per-quotation problems, or null when it is another error. */
export function readLinePricingConflict(err: any): { detail: string; quotations: Array<{ quotationId: string; supplierName: string; reasonCode: string; globalDiscount: number; quotationTotal: number; consideredLineTotalSum: number }> } | null {
    const details = err?.details;
    const code = details?.code ?? err?.errorCode;
    if (code !== QUOTATION_PRICING_NOT_LINE_RESOLVED) return null;
    const quotations = Array.isArray(details?.quotations) ? details.quotations.map((x: any) => ({
        quotationId: String(x?.quotationId ?? ''), supplierName: String(x?.supplierName ?? ''), reasonCode: String(x?.reasonCode ?? ''),
        globalDiscount: Number(x?.globalDiscount) || 0, quotationTotal: Number(x?.quotationTotal) || 0, consideredLineTotalSum: Number(x?.consideredLineTotalSum) || 0,
    })) : [];
    return { detail: String(details?.detail ?? err?.message ?? ''), quotations };
}

// ── 2. OCR discount resolution (draft building) ──────────────────────────────

export interface RawOcrLineDiscountInput {
    quantity: number;
    unitPrice: number;
    discountAmount: number;
    discountPercent: number;
    /** The document's own printed line total (after discount, before tax), 0 when the provider gave none. */
    totalPrice: number;
}

export interface ResolvedLineDiscount {
    discountAmount: number;
    discountPercent: number | undefined;
    /** The extracted discount and the document's own line total disagree beyond tolerance: the Buyer must confirm the discount. */
    discountUncertain: boolean;
    /** What the printed line total implies (gross − totalPrice), for the hint. Null when no line total was extracted. */
    impliedDiscount: number | null;
    strategy: 'KEPT' | 'IMPLIED_FROM_LINE_TOTAL' | 'FROM_PERCENT' | 'NONE';
}

/**
 * Per-line discount against the document's own line total. Previous behaviour ("Strategy A") silently REPLACED the extracted
 * discount by gross − totalPrice whenever they disagreed by more than 1 — which zeroes a real discount when the provider reads
 * the GROSS value into totalPrice. Now: agreement keeps the extracted discount; a disagreement keeps the EXTRACTED amount when
 * one exists and marks the line uncertain (the Buyer confirms in the wizard); only a line extracted with NO discount at all takes
 * the implied one (the provider dropped the discount column but read the net line total).
 */
export function resolveOcrLineDiscount(raw: RawOcrLineDiscountInput): ResolvedLineDiscount {
    const qty = raw.quantity || 0;
    const price = raw.unitPrice || 0;
    const gross = round2(qty * price);
    const rawAmt = Math.max(0, raw.discountAmount || 0);
    const rawPct = raw.discountPercent || 0;
    const total = raw.totalPrice || 0;
    const pctOf = (amt: number) => (gross > 0 ? Math.round((amt / gross) * 10000) / 100 : undefined);

    if (total > 0 && gross > 0) {
        const implied = round2(gross - total);
        const extractedProducesTotal = Math.abs(round2(gross - rawAmt) - total) <= 1;
        if (extractedProducesTotal) {
            return { discountAmount: rawAmt, discountPercent: rawAmt > 0 ? (rawPct > 0 ? rawPct : pctOf(rawAmt)) : undefined, discountUncertain: false, impliedDiscount: implied, strategy: rawAmt > 0 ? 'KEPT' : 'NONE' };
        }
        if (rawAmt > 0) {
            // Extracted discount vs printed line total disagree: never overwrite silently — the Buyer confirms.
            return { discountAmount: rawAmt, discountPercent: rawPct > 0 ? rawPct : pctOf(rawAmt), discountUncertain: true, impliedDiscount: implied, strategy: 'KEPT' };
        }
        if (implied > 0) {
            // No discount extracted but the document's line total is below gross: the provider dropped the column.
            return { discountAmount: implied, discountPercent: pctOf(implied), discountUncertain: true, impliedDiscount: implied, strategy: 'IMPLIED_FROM_LINE_TOTAL' };
        }
        return { discountAmount: 0, discountPercent: undefined, discountUncertain: false, impliedDiscount: implied, strategy: 'NONE' };
    }

    if (rawPct > 0 && gross > 0) {
        const expected = round2(gross * (rawPct / 100));
        if (Math.abs(rawAmt - expected) > 0.01) {
            return { discountAmount: expected, discountPercent: rawPct, discountUncertain: rawAmt > 0, impliedDiscount: null, strategy: 'FROM_PERCENT' };
        }
        return { discountAmount: rawAmt, discountPercent: rawPct, discountUncertain: false, impliedDiscount: null, strategy: 'KEPT' };
    }

    if (rawAmt > 0) return { discountAmount: rawAmt, discountPercent: pctOf(rawAmt), discountUncertain: false, impliedDiscount: null, strategy: 'KEPT' };
    return { discountAmount: 0, discountPercent: undefined, discountUncertain: false, impliedDiscount: null, strategy: 'NONE' };
}

export type HeaderDiscountKind = 'NONE' | 'AGGREGATE_OF_LINES' | 'DOCUMENT_LEVEL';

export interface ResolvedHeaderDiscount {
    /** The global (quotation-level) discount to put on the draft. 0 when the header value is only the sum of the line discounts. */
    globalDiscount: number;
    kind: HeaderDiscountKind;
    headerDiscount: number;
    lineDiscountSum: number;
    /** pt-PT sentence for the wizard; null when there is nothing to explain. */
    note: string | null;
}

/** Tolerance for "the header 'Desconto Comercial' equals the sum of the line discounts" (per-line rounding). */
export const HEADER_DISCOUNT_AGGREGATE_TOLERANCE = 1.0;

/**
 * The header discountAmount the provider reports from the summary block ("Desconto Comercial 67.690,00") is, on Primavera-style
 * documents, the SUM of the line discounts, not an additional discount. Applying it as a global discount on top of the line
 * discounts counts the discount twice; applying it alone (lines without discount) hides it from every per-line value.
 */
export function resolveOcrHeaderDiscount(headerDiscount: number, lineDiscounts: number[]): ResolvedHeaderDiscount {
    const header = Math.max(0, headerDiscount || 0);
    const lineSum = round2(lineDiscounts.reduce((s, d) => s + Math.max(0, d || 0), 0));
    if (header <= 0) return { globalDiscount: 0, kind: 'NONE', headerDiscount: header, lineDiscountSum: lineSum, note: null };
    if (lineSum > 0 && Math.abs(header - lineSum) <= HEADER_DISCOUNT_AGGREGATE_TOLERANCE) {
        return {
            globalDiscount: 0, kind: 'AGGREGATE_OF_LINES', headerDiscount: header, lineDiscountSum: lineSum,
            note: `O "Desconto Comercial" do resumo do documento (${header.toFixed(2)}) corresponde à soma dos descontos das linhas (${lineSum.toFixed(2)}); foi mantido nas linhas e não aplicado como desconto global.`,
        };
    }
    return {
        globalDiscount: header, kind: 'DOCUMENT_LEVEL', headerDiscount: header, lineDiscountSum: lineSum,
        note: lineSum > 0
            ? `O documento indica um desconto de ${header.toFixed(2)} no resumo além dos descontos das linhas (${lineSum.toFixed(2)}). Confirme se é um desconto adicional ao nível do documento; um desconto global tem de ser distribuído pelas linhas antes de a cotação ser enviada para aprovação.`
            : `O documento indica um desconto comercial de ${header.toFixed(2)} no resumo e as linhas vieram sem desconto. Verifique primeiro se o documento imprime descontos por linha: nesse caso introduza em cada linha o valor impresso (não uma distribuição). Só um desconto que exista apenas no resumo deve ser distribuído pelas linhas; em qualquer caso um desconto global tem de estar nas linhas antes de a cotação ser enviada para aprovação.`,
    };
}

// ── 3. Explicit allocation of a global discount to the lines ─────────────────

/** Largest-remainder proportional split: 2-decimal shares that add up EXACTLY to `amount`; zero bases get nothing. */
export function allocateAmountProportionally(amount: number, bases: number[]): number[] {
    const total = bases.reduce((s, b) => s + Math.max(0, b || 0), 0);
    const n = bases.length;
    if (n === 0 || amount <= 0 || total <= 0) return bases.map(() => 0);
    const cents = Math.round(amount * 100);
    const raw = bases.map(b => (Math.max(0, b || 0) / total) * cents);
    const floors = raw.map(r => Math.floor(r));
    let remainder = cents - floors.reduce((s, f) => s + f, 0);
    const order = raw.map((r, i) => ({ i, frac: r - Math.floor(r), base: bases[i] }))
        .sort((a, b) => (b.frac - a.frac) || (b.base - a.base) || (a.i - b.i));
    for (const o of order) { if (remainder <= 0) break; if ((bases[o.i] || 0) > 0) { floors[o.i] += 1; remainder -= 1; } }
    return floors.map(c => c / 100);
}

export interface AllocatableDraftLine {
    quantity?: number;
    unitPrice?: number;
    discountAmount?: number;
    discountPercent?: number;
    reconciliationStatus?: string | null;
    lineAdjustmentJustification?: string | null;
    ivaRateId?: number | null;
    totalPrice?: number;
}

export type AllocationRefusal = 'NOTHING_TO_ALLOCATE' | 'NO_ELIGIBLE_LINES' | 'EXCEEDS_ELIGIBLE_NET';

export interface AllocationResult<T extends AllocatableDraftLine> {
    items: T[];
    /** The allocated amount (= the global discount, exactly) — 0 when refused. */
    allocated: number;
    shares: number[];
    /** Lines that received a share (net > 0, considered status when statuses exist). */
    allocatedLineCount: number;
    /** Sum of the nets of the lines eligible to receive a share (considered status, net > 0). */
    eligibleNet: number;
    /** Null when the allocation was applied; otherwise why nothing was changed (items are returned untouched). */
    refusal: AllocationRefusal | null;
}

export function lineNet(line: AllocatableDraftLine): number {
    return Math.max(0, round2((line.quantity || 0) * (line.unitPrice || 0)) - (line.discountAmount || 0));
}

/** pt-PT sentence for a refused allocation (shown in the document step; the global discount stays as it was). */
export function describeAllocationRefusal(refusal: AllocationRefusal, amount: number, eligibleNet: number, currency: string): string {
    switch (refusal) {
        case 'NOTHING_TO_ALLOCATE':
            return 'Não há desconto global por distribuir (já está nas linhas ou é zero).';
        case 'NO_ELIGIBLE_LINES':
            return 'Não há linhas consideradas com valor líquido para receber o desconto global: as linhas ignoradas / não cotadas e as linhas de valor zero não recebem desconto. Corrija as linhas ou o desconto.';
        case 'EXCEEDS_ELIGIBLE_NET':
            return `O desconto global de ${money(amount, currency)} excede o valor líquido das linhas consideradas (${money(eligibleNet, currency)}); nenhuma linha pode ficar com valor negativo. Corrija o desconto global ou os descontos por linha (um desconto só pode ser aplicado uma vez).`;
    }
}

/**
 * The wizard's explicit "Distribuir pelas linhas": adds a proportional share of the global discount to each line's own
 * discount (bases = the lines' current nets), pre-fills the per-line adjustment reason with the arithmetic when the line has
 * none, and returns the lines; the caller zeroes the draft's global discount and recomputes the totals. Deterministic,
 * disclosed, Buyer-triggered — never run automatically.
 *
 * Boundaries (all refusals return the SAME line objects, untouched): nothing to allocate (amount ≤ 0); no eligible line
 * (IGNORED / NOT_QUOTED and zero-net lines never receive a share); an amount above the eligible net (no line may go negative;
 * the share of a line is by construction ≤ its net, enforced again after rounding). The result is proportional to the lines'
 * NET values: it reproduces the document only when the document's discount is itself summary-only / proportional — when the
 * document prints per-line discount amounts, those amounts must be entered per line instead (the step says so).
 */
export function allocateGlobalDiscountToLines<T extends AllocatableDraftLine>(lines: T[], globalDiscount: number, currency: string): AllocationResult<T> {
    const amount = round2(Math.max(0, globalDiscount || 0));
    const eligible = lines.map(l => (isConsideredStatus(l.reconciliationStatus) ? lineNet(l) : 0));
    const eligibleNet = round2(eligible.reduce((s, n) => s + n, 0));
    const zeroShares = lines.map(() => 0);
    if (amount <= 0) return { items: lines, allocated: 0, shares: zeroShares, allocatedLineCount: 0, eligibleNet, refusal: 'NOTHING_TO_ALLOCATE' };
    if (eligibleNet <= 0) return { items: lines, allocated: 0, shares: zeroShares, allocatedLineCount: 0, eligibleNet, refusal: 'NO_ELIGIBLE_LINES' };
    if (amount > eligibleNet + 0.005) return { items: lines, allocated: 0, shares: zeroShares, allocatedLineCount: 0, eligibleNet, refusal: 'EXCEEDS_ELIGIBLE_NET' };

    const shares = allocateAmountProportionally(amount, eligible);
    // Defensive: a share can never exceed the line's own net (would make the line negative). Mathematically impossible for
    // amount ≤ eligibleNet with largest-remainder rounding; refused rather than clamped if it ever happened.
    if (shares.some((s, i) => s > eligible[i] + 0.005)) return { items: lines, allocated: 0, shares: zeroShares, allocatedLineCount: 0, eligibleNet, refusal: 'EXCEEDS_ELIGIBLE_NET' };

    const items = lines.map((l, i) => {
        const share = shares[i];
        if (share <= 0) return l;
        const gross = round2((l.quantity || 0) * (l.unitPrice || 0));
        const newDiscount = round2((l.discountAmount || 0) + share);
        const reason = (l.lineAdjustmentJustification || '').trim()
            ? l.lineAdjustmentJustification
            : `Desconto comercial global de ${money(amount, currency)} do documento distribuído proporcionalmente ao valor líquido das linhas: ${money(share, currency)} nesta linha (${(gross > 0 ? (newDiscount / gross) * 100 : 0).toFixed(2)}% do bruto).`;
        return { ...l, discountAmount: newDiscount, discountPercent: gross > 0 ? Math.round((newDiscount / gross) * 10000) / 100 : undefined, lineAdjustmentJustification: reason };
    });
    return { items, allocated: amount, shares, allocatedLineCount: shares.filter(s => s > 0).length, eligibleNet, refusal: null };
}

// ── 4. Draft-level arithmetic shared with the wizard state hook ───────────────

/** Minimal IVA-rate shape the draft arithmetic needs. */
export interface IvaRateLike { id: number; ratePercent: number; }

export interface DraftPricingLine extends AllocatableDraftLine {
    discountUncertain?: boolean;
}

export interface DraftPricingLike<TLine extends DraftPricingLine = DraftPricingLine> {
    items: TLine[];
    discountAmount: number;
    totalAmount: number;
    currency?: string;
    globalDiscountKind?: 'NONE' | 'AGGREGATE_OF_LINES' | 'DOCUMENT_LEVEL' | null;
    globalDiscountNote?: string | null;
}

/** One line's VAT-inclusive total from its own fields (same arithmetic as the server: Round2 per line, net never negative). */
export function recomputeDraftLineTotal(item: AllocatableDraftLine, rates: IvaRateLike[]): number {
    const gross = round2((item.quantity || 0) * (item.unitPrice || 0));
    const net = Math.max(0, gross - (item.discountAmount || 0));
    const rate = rates.find(r => r.id === item.ivaRateId);
    const iva = round2(net * ((rate ? rate.ratePercent : 0) / 100));
    return round2(net + iva);
}

/**
 * The whole draft's running total (every line, regardless of reconciliation status — the document step has no statuses yet),
 * with the global discount reducing the taxable base and the IVA proportionally — the same arithmetic as the server's
 * quotation totals, so that after an allocation Σ line totals equals this total.
 */
export function computeDraftQuotationTotal(d: DraftPricingLike, rates: IvaRateLike[]): number {
    let net = 0;
    let ivaTotal = 0;
    d.items.forEach(item => {
        const gross = round2((item.quantity || 0) * (item.unitPrice || 0));
        const itemNet = Math.max(0, gross - (item.discountAmount || 0));
        net += itemNet;
        const rate = rates.find(r => r.id === item.ivaRateId);
        ivaTotal += round2(itemNet * ((rate ? rate.ratePercent : 0) / 100));
    });
    const discount = d.discountAmount || 0;
    const taxableBase = Math.max(0, net - discount);
    const discountRatio = net > 0 ? (taxableBase / net) : 1;
    const adjustedIva = round2(ivaTotal * discountRatio);
    return Math.max(0, round2(taxableBase + adjustedIva));
}

export interface DraftAllocationOutcome<TDraft> {
    draft: TDraft;
    allocatedLineCount: number;
    refusal: AllocationRefusal | null;
    /** The pt-PT note written to draft.globalDiscountNote (success or refusal). */
    note: string;
}

/**
 * "Distribuir pelas linhas" on a whole draft, as a pure transform (the hook only wraps it in setState):
 * success → shares added to the lines, every line total recomputed, global discount zeroed (so a REPEAT press finds nothing
 * to allocate and returns the same draft: the discount can never be applied twice), kind NONE, note with the arithmetic,
 * quotation total recomputed; refusal → the SAME draft object except the note explaining why (global discount untouched).
 */
export function applyGlobalDiscountAllocationToDraft<TLine extends DraftPricingLine, TDraft extends DraftPricingLike<TLine>>(draft: TDraft, rates: IvaRateLike[]): DraftAllocationOutcome<TDraft> {
    const currency = draft.currency || 'AOA';
    const amount = round2(Math.max(0, draft.discountAmount || 0));
    const result = allocateGlobalDiscountToLines(draft.items, amount, currency);
    if (result.refusal) {
        const note = describeAllocationRefusal(result.refusal, amount, result.eligibleNet, currency);
        return { draft: result.refusal === 'NOTHING_TO_ALLOCATE' ? draft : { ...draft, globalDiscountNote: note }, allocatedLineCount: 0, refusal: result.refusal, note };
    }
    const items = result.items.map(item => ({ ...item, discountUncertain: false, totalPrice: recomputeDraftLineTotal(item, rates) })) as TLine[];
    const note = `Desconto comercial global de ${money(amount, currency)} distribuído por ${result.allocatedLineCount} linha(s), proporcionalmente ao valor líquido de cada linha (soma exata). Se o documento imprimir descontos por linha, substitua estes valores pelos impressos. Reveja os descontos por linha antes de guardar.`;
    const next = { ...draft, items, discountAmount: 0, globalDiscountKind: 'NONE' as const, globalDiscountNote: note };
    next.totalAmount = computeDraftQuotationTotal(next, rates);
    return { draft: next, allocatedLineCount: result.allocatedLineCount, refusal: null, note };
}
