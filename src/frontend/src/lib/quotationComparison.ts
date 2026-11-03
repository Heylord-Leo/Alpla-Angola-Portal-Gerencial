/**
 * v2.249.5 — "Menor valor" done per REQUEST ITEM, never across documents of different scope.
 *
 * Found in TEST on REQ-628: the card-level chip compared the TOTAL of a 1-item re-quotation (MAQSIS) with the TOTAL of a
 * cancelled 2-item adjudication (TREVOTECH) — different items, one of them history. Rules here:
 *  - candidates are quotation items mapped to a request line (MAPPED / SUBSTITUTE) that are CURRENT options: not a
 *    historical award/candidacy (AWARD_CANCELLED / CANDIDATE_CANCELLED), not of a HISTORICAL_CANCELLED quotation, not
 *    reuse-blocked;
 *  - items of one line are compared on the UNIT price, only among candidates with the same currency and the same unit;
 *  - when the compared candidates do not all quote the SAME QUANTITY the line is never "Menor preço unit.": volume pricing
 *    makes those offers incomparable, so every candidate of that line gets the informational QUANTITY_DIFFERS status (the
 *    lowest unit price is only flagged as indicative, never counted as a win);
 *  - ties mark every lowest candidate;
 *  - a CARD-level "Menor valor" exists only when every current quotation covers the identical line scope with identical
 *    quantities, units and currency — then totals are comparable. Otherwise the card says "comparação por item".
 * The Area approver's winner-selection rules (WizardStepAward) are untouched: this is display only.
 */

export interface ComparableQuotationItem {
    id: string;
    mappedRequestLineItemId?: string | null;
    reconciliationStatus?: string;
    unitCode?: string | null;
    quantity: number;
    unitPrice: number;
    currencyCode?: string;
    isReuseBlocked?: boolean;
    adjudicationStatus?: string | null;
}

export interface ComparableQuotation {
    id: string;
    currency: string;
    totalAmount: number;
    lifecycleStatus?: string | null;
    items?: ComparableQuotationItem[];
}

export type ItemComparisonStatus =
    | 'LOWEST'            // strictly lowest unit price among comparable candidates of the line (same currency, unit AND quantity)
    | 'TIE_LOWEST'        // shares the lowest unit price (same currency, unit AND quantity)
    | 'HIGHER'
    | 'QUANTITY_DIFFERS'  // comparable candidates exist but not all quote the same quantity: informational only, never a win
    | 'ONLY_CANDIDATE'    // the single current candidate of its line
    | 'NOT_COMPARABLE'    // other candidates exist but none shares currency + unit
    | 'HISTORICAL'        // cancelled award / candidacy, reuse-blocked, or historical quotation
    | 'NOT_COMPARED';     // not mapped to a request line (EXTRA_ITEM, NOT_QUOTED, IGNORED)

export interface ItemComparison {
    quotationItemId: string;
    lineId: string | null;
    status: ItemComparisonStatus;
    /** How many CURRENT candidates the line has (across quotations), including this one. */
    candidateCount: number;
    /** Candidates of the line that share this item's currency + unit (including this one). */
    comparableCount: number;
    /** True when the compared candidates do not all quote the same quantity (status QUANTITY_DIFFERS). */
    quantityDiffers: boolean;
    /** QUANTITY_DIFFERS only: this item has the lowest (or tied-lowest) unit price among the compared candidates — indicative. */
    indicativeLowest?: boolean;
    reason?: 'HISTORICAL_ADJUDICATION' | 'REUSE_BLOCKED' | 'HISTORICAL_QUOTATION' | 'CURRENCY' | 'UNIT' | 'UNMAPPED';
}

export type CardComparisonReason = 'IDENTICAL_SCOPE' | 'DIFFERENT_SCOPE' | 'MIXED_CURRENCY' | 'SINGLE' | 'HISTORICAL' | 'NO_CANDIDATES';

export interface CardComparison {
    quotationId: string;
    /** True only when totals are comparable (identical scope) and this quotation's total is the lowest (ties allowed). */
    lowestTotal: boolean;
    /** Lines on which this quotation holds the lowest (or tied-lowest) unit price at the SAME quantity. */
    lowestLineCount: number;
    /** Lines on which the comparison is only indicative (quantities differ) and this quotation has the lowest unit price. */
    indicativeLineCount: number;
    /** Current candidate lines of this quotation. */
    candidateLineCount: number;
    reason: CardComparisonReason;
}

export interface QuotationComparisonResult {
    byItemId: Map<string, ItemComparison>;
    byQuotationId: Map<string, CardComparison>;
    /** True when every current quotation covers the same lines with the same quantities/units/currency. */
    identicalScope: boolean;
    /** True when at least one line is compared only indicatively because the quantities differ. */
    anyQuantityDiffers: boolean;
}

const CANDIDATE_RECONCILIATION = new Set(['MAPPED', 'SUBSTITUTE']);
const HISTORICAL_ITEM = new Set(['AWARD_CANCELLED', 'CANDIDATE_CANCELLED']);

const unitKey = (u: string | null | undefined) => (u || '').trim().toUpperCase();
const currencyOf = (q: ComparableQuotation, it: ComparableQuotationItem) => (it.currencyCode || q.currency || '').trim().toUpperCase();

export function compareQuotationsPerLine(quotations: ComparableQuotation[]): QuotationComparisonResult {
    const byItemId = new Map<string, ItemComparison>();
    const byQuotationId = new Map<string, CardComparison>();

    type Cand = { q: ComparableQuotation; it: ComparableQuotationItem; lineId: string; currency: string; unit: string };
    const perLine = new Map<string, Cand[]>();
    const historicalQuotation = (q: ComparableQuotation) => q.lifecycleStatus === 'HISTORICAL_CANCELLED';

    // 1. classify every item
    for (const q of quotations) {
        for (const it of q.items || []) {
            const lineId = it.mappedRequestLineItemId || null;
            const mapped = !!lineId && CANDIDATE_RECONCILIATION.has((it.reconciliationStatus || 'MAPPED').toUpperCase());
            if (!mapped) {
                byItemId.set(it.id, { quotationItemId: it.id, lineId, status: 'NOT_COMPARED', candidateCount: 0, comparableCount: 0, quantityDiffers: false, reason: 'UNMAPPED' });
                continue;
            }
            if (historicalQuotation(q) || HISTORICAL_ITEM.has((it.adjudicationStatus || '').toUpperCase()) || it.isReuseBlocked) {
                const reason = it.isReuseBlocked && !HISTORICAL_ITEM.has((it.adjudicationStatus || '').toUpperCase()) && !historicalQuotation(q)
                    ? 'REUSE_BLOCKED' : historicalQuotation(q) ? 'HISTORICAL_QUOTATION' : 'HISTORICAL_ADJUDICATION';
                byItemId.set(it.id, { quotationItemId: it.id, lineId, status: 'HISTORICAL', candidateCount: 0, comparableCount: 0, quantityDiffers: false, reason });
                continue;
            }
            const cand: Cand = { q, it, lineId: lineId!, currency: currencyOf(q, it), unit: unitKey(it.unitCode) };
            const list = perLine.get(cand.lineId) || [];
            list.push(cand);
            perLine.set(cand.lineId, list);
        }
    }

    // 2. compare per line on the unit price, within the same currency + unit; quantities must match for a firm result
    const lowestLinesByQuotation = new Map<string, number>();
    const indicativeLinesByQuotation = new Map<string, number>();
    const candidateLinesByQuotation = new Map<string, Set<string>>();
    let anyQuantityDiffers = false;
    for (const [lineId, cands] of perLine) {
        for (const c of cands) {
            const set = candidateLinesByQuotation.get(c.q.id) || new Set<string>();
            set.add(lineId); candidateLinesByQuotation.set(c.q.id, set);
        }
        if (cands.length === 1) {
            const c = cands[0];
            byItemId.set(c.it.id, { quotationItemId: c.it.id, lineId, status: 'ONLY_CANDIDATE', candidateCount: 1, comparableCount: 1, quantityDiffers: false });
            continue;
        }
        for (const c of cands) {
            const sameCurrency = cands.filter(o => o.currency === c.currency);
            const comparable = sameCurrency.filter(o => o.unit === c.unit);
            if (comparable.length < 2) {
                byItemId.set(c.it.id, {
                    quotationItemId: c.it.id, lineId, status: 'NOT_COMPARABLE', candidateCount: cands.length, comparableCount: comparable.length,
                    quantityDiffers: false, reason: sameCurrency.length < 2 ? 'CURRENCY' : 'UNIT',
                });
                continue;
            }
            const min = Math.min(...comparable.map(o => Number(o.it.unitPrice)));
            const lowestOnes = comparable.filter(o => Number(o.it.unitPrice) === min);
            const isMin = Number(c.it.unitPrice) === min;
            const quantityDiffers = new Set(comparable.map(o => Number(o.it.quantity))).size > 1;
            if (quantityDiffers) {
                // Volume pricing: no winner on this line, only an indicative flag on the lowest unit price.
                anyQuantityDiffers = true;
                if (isMin) indicativeLinesByQuotation.set(c.q.id, (indicativeLinesByQuotation.get(c.q.id) || 0) + 1);
                byItemId.set(c.it.id, { quotationItemId: c.it.id, lineId, status: 'QUANTITY_DIFFERS', candidateCount: cands.length, comparableCount: comparable.length, quantityDiffers: true, indicativeLowest: isMin });
                continue;
            }
            let status: ItemComparisonStatus = 'HIGHER';
            if (isMin) status = lowestOnes.length > 1 ? 'TIE_LOWEST' : 'LOWEST';
            if (status !== 'HIGHER') lowestLinesByQuotation.set(c.q.id, (lowestLinesByQuotation.get(c.q.id) || 0) + 1);
            byItemId.set(c.it.id, { quotationItemId: c.it.id, lineId, status, candidateCount: cands.length, comparableCount: comparable.length, quantityDiffers: false });
        }
    }

    // 3. card level — totals comparable only on identical scope (same lines, quantities, units and currency)
    const current = quotations.filter(q => !historicalQuotation(q) && (candidateLinesByQuotation.get(q.id)?.size || 0) > 0);
    const scopeKey = (q: ComparableQuotation) => {
        const parts: string[] = [];
        for (const it of q.items || []) {
            const cmp = byItemId.get(it.id);
            if (!cmp || cmp.status === 'HISTORICAL' || cmp.status === 'NOT_COMPARED') continue;
            parts.push(`${cmp.lineId}|${Number(it.quantity)}|${unitKey(it.unitCode)}|${currencyOf(q, it)}`);
        }
        return parts.sort().join(';');
    };
    const keys = new Set(current.map(scopeKey));
    const currencies = new Set(current.map(q => (q.currency || '').trim().toUpperCase()));
    const identicalScope = current.length > 1 && keys.size === 1 && currencies.size === 1;
    const minTotal = identicalScope ? Math.min(...current.map(q => Number(q.totalAmount))) : null;

    for (const q of quotations) {
        const candidateLineCount = candidateLinesByQuotation.get(q.id)?.size || 0;
        const lowestLineCount = lowestLinesByQuotation.get(q.id) || 0;
        const indicativeLineCount = indicativeLinesByQuotation.get(q.id) || 0;
        let reason: CardComparisonReason;
        if (historicalQuotation(q)) reason = 'HISTORICAL';
        else if (candidateLineCount === 0) reason = 'NO_CANDIDATES';
        else if (current.length < 2) reason = 'SINGLE';
        else if (currencies.size > 1) reason = 'MIXED_CURRENCY';
        else reason = identicalScope ? 'IDENTICAL_SCOPE' : 'DIFFERENT_SCOPE';
        byQuotationId.set(q.id, {
            quotationId: q.id,
            lowestTotal: reason === 'IDENTICAL_SCOPE' && Number(q.totalAmount) === minTotal,
            lowestLineCount, indicativeLineCount, candidateLineCount, reason,
        });
    }

    return { byItemId, byQuotationId, identicalScope, anyQuantityDiffers };
}

/** Chip text for the card. Null = no card-level claim. */
export function describeCardComparison(c: CardComparison | undefined, currency: string): { label: string; kind: 'LOWEST_TOTAL' | 'PER_ITEM' | 'INDICATIVE' } | null {
    if (!c) return null;
    if (c.lowestTotal) return { label: `Menor valor (${currency})`, kind: 'LOWEST_TOTAL' };
    if (c.reason === 'DIFFERENT_SCOPE' || c.reason === 'MIXED_CURRENCY') {
        if (c.lowestLineCount > 0) return { label: `Menor preço unit. em ${c.lowestLineCount} de ${c.candidateLineCount} item(ns)`, kind: 'PER_ITEM' };
        if (c.indicativeLineCount > 0) return { label: `Menor preço unit. (indicativo, qtd. diferente) em ${c.indicativeLineCount} de ${c.candidateLineCount} item(ns)`, kind: 'INDICATIVE' };
        return null;
    }
    return null;
}

/** Explanation shown once per list when totals are not comparable. Null when nothing needs saying. */
export function describeScopeNotice(result: QuotationComparisonResult, quotations: ComparableQuotation[]): string | null {
    const cards = quotations.map(q => result.byQuotationId.get(q.id)).filter(Boolean) as CardComparison[];
    const sentences: string[] = [];
    if (cards.some(c => c.reason === 'MIXED_CURRENCY')) sentences.push('Cotações em moedas diferentes: os totais não são comparáveis. A comparação é feita por item, entre cotações na mesma moeda e unidade.');
    else if (cards.some(c => c.reason === 'DIFFERENT_SCOPE')) sentences.push('As cotações cobrem itens ou quantidades diferentes: o total não é comparável. A comparação de preços é feita por item.');
    if (result.anyQuantityDiffers) sentences.push('Há itens cotados em quantidades diferentes: nesses itens o menor preço unitário é apenas indicativo (o preço pode depender do volume).');
    return sentences.length ? sentences.join(' ') : null;
}

/** Row label for an item comparison. Null = nothing to show. */
export function describeItemComparison(c: ItemComparison | undefined): { label: string; tone: 'lowest' | 'tie' | 'muted' } | null {
    if (!c) return null;
    switch (c.status) {
        case 'LOWEST': return { label: 'Menor preço unit.', tone: 'lowest' };
        case 'TIE_LOWEST': return { label: 'Empate no menor preço unit.', tone: 'tie' };
        case 'QUANTITY_DIFFERS': return { label: c.indicativeLowest ? 'Quantidades diferentes — menor preço unit. apenas indicativo' : 'Quantidades diferentes — comparação apenas indicativa', tone: 'muted' };
        case 'NOT_COMPARABLE': return { label: c.reason === 'CURRENCY' ? 'Moeda diferente — não comparável' : 'Unidade diferente — não comparável', tone: 'muted' };
        case 'ONLY_CANDIDATE': return { label: 'Única cotação atual para o item', tone: 'muted' };
        default: return null;
    }
}
