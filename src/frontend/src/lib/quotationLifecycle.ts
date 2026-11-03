/**
 * v2.249.5 — lifecycle context of a saved quotation and its items, from the SERVER projection
 * (QuotationAdjudicationProjection → SavedQuotationDto.lifecycleStatus / SavedQuotationItemDto.adjudicationStatus).
 * Pure labels: the client never derives history from totals, dates or the request status. Historical adjudication
 * (what happened) is kept apart from current reuse eligibility (isReuseBlocked / isReuseAuthorized).
 * Docs: docs/PO_EVIDENCE_AND_QUOTATION_LIFECYCLE_v2.249.5.md §3.
 */

export type QuotationLifecycleStatus =
    | 'OPEN' | 'IN_APPROVAL' | 'AWARDED' | 'NOT_AWARDED' | 'PARTIALLY_HISTORICAL' | 'HISTORICAL_CANCELLED' | 'REJECTED';

export type QuotationItemAdjudicationStatus =
    | 'NOT_QUOTED' | 'NOT_SUBMITTED' | 'IN_APPROVAL' | 'AWARDED' | 'CANDIDATE_NOT_AWARDED'
    | 'AWARD_CANCELLED' | 'CANDIDATE_CANCELLED' | 'REJECTED';

export type LifecycleTone = 'historical' | 'awarded' | 'in-approval' | 'not-awarded' | 'rejected' | 'partial' | 'open';

export interface QuotationLifecycleLike {
    lifecycleStatus?: string | null;
    activeAwardedItemCount?: number;
    historicalItemCount?: number;
    historicalBatchNumber?: number | null;
    historicalReleaseKind?: string | null;
    quotedItemCount?: number;
    itemCount?: number;
    isSelected?: boolean;
}

export interface QuotationItemLifecycleLike {
    adjudicationStatus?: string | null;
    adjudicationBatchNumber?: number | null;
    adjudicationReleaseKind?: string | null;
    lineCurrentState?: string | null;
    lineCurrentSupplierName?: string | null;
    isReuseBlocked?: boolean;
    isReuseAuthorized?: boolean;
    reconciliationStatus?: string;
}

export interface LifecycleDescription {
    tone: LifecycleTone;
    /** Short chip text. */
    label: string;
    /** One sentence for the title / expanded header. */
    detail: string;
    /** True when the quotation (as a whole) is history: no item of it is a current candidate or award. */
    isHistorical: boolean;
}

function releaseSentence(kind: string | null | undefined, plural: boolean): string {
    const items = plural ? 'os itens foram devolvidos' : 'o item foi devolvido';
    if (kind === 'RESTART_QUOTATION') return `a P.O. foi cancelada e ${items} à cotação`;
    if (kind === 'CANCEL_ITEMS') return `a P.O. foi cancelada e ${plural ? 'os itens foram encerrados' : 'o item foi encerrado'}`;
    return `a adjudicação foi cancelada`;
}

/** Card-level description. Null when the quotation is simply open (nothing happened yet) — no chip is shown. */
export function describeQuotationLifecycle(q: QuotationLifecycleLike): LifecycleDescription | null {
    const status = (q.lifecycleStatus || '') as QuotationLifecycleStatus | '';
    const batch = q.historicalBatchNumber ? ` (lote ${q.historicalBatchNumber})` : '';
    switch (status) {
        case 'HISTORICAL_CANCELLED': {
            const plural = (q.historicalItemCount ?? 0) > 1;
            return {
                tone: 'historical', isHistorical: true,
                label: 'Histórica — adjudicação cancelada',
                detail: `Cotação adjudicada${batch}; ${releaseSentence(q.historicalReleaseKind, plural)}. Mantida como histórico; não é uma opção atual.`,
            };
        }
        case 'PARTIALLY_HISTORICAL': {
            const total = q.quotedItemCount ?? q.itemCount ?? 0;
            return {
                tone: 'partial', isHistorical: false,
                label: `Parcialmente histórica — ${q.historicalItemCount ?? 0} de ${total} item(ns) com adjudicação cancelada`,
                detail: `${q.historicalItemCount ?? 0} item(ns) desta cotação tiveram a adjudicação cancelada${batch}; ${q.activeAwardedItemCount ?? 0} item(ns) continuam adjudicados ou candidatos.`,
            };
        }
        case 'AWARDED':
            return { tone: 'awarded', isHistorical: false, label: `Adjudicada · ${q.activeAwardedItemCount ?? 0} item(ns)`, detail: 'Itens desta cotação adjudicados num lote aprovado em vigor.' };
        case 'IN_APPROVAL':
            return { tone: 'in-approval', isHistorical: false, label: 'Em aprovação', detail: 'Itens desta cotação são candidatos num lote em aprovação.' };
        case 'NOT_AWARDED':
            return { tone: 'not-awarded', isHistorical: false, label: 'Não adjudicada', detail: 'Candidata num lote decidido; nenhum item desta cotação foi adjudicado.' };
        case 'REJECTED':
            return { tone: 'rejected', isHistorical: false, label: 'Lote rejeitado', detail: 'O lote em que esta cotação foi submetida foi rejeitado.' };
        default:
            return null;
    }
}

export interface ItemLifecycleDescription {
    tone: LifecycleTone;
    label: string;
    /** Second line: what the request line did afterwards and the CURRENT reuse eligibility (kept separate from history). */
    detail: string | null;
    isHistorical: boolean;
}

function lineSentence(it: QuotationItemLifecycleLike): string | null {
    switch (it.lineCurrentState) {
        case 'REQUOTED_WITH_OTHER':
            return it.lineCurrentSupplierName
                ? `Item devolvido à cotação e recotado com ${it.lineCurrentSupplierName}.`
                : 'Item devolvido à cotação e recotado com outro fornecedor.';
        case 'PENDING_REQUOTE': return 'Item devolvido à cotação; nova cotação pendente.';
        case 'CLOSED_AFTER_APPROVAL': return 'Item encerrado após a aprovação.';
        default: return null;
    }
}

function reuseSentence(it: QuotationItemLifecycleLike): string | null {
    if (it.isReuseAuthorized) return 'Reutilização autorizada.';
    if (it.isReuseBlocked) return 'Reutilização sujeita a autorização.';
    return null;
}

/** Row-level description. Never says "Ignorada": a NOT_QUOTED / IGNORED placeholder reads "Não cotado". */
export function describeQuotationItemAdjudication(it: QuotationItemLifecycleLike): ItemLifecycleDescription | null {
    const status = (it.adjudicationStatus || '') as QuotationItemAdjudicationStatus | '';
    const batch = it.adjudicationBatchNumber ? ` (lote ${it.adjudicationBatchNumber})` : '';
    const join = (...parts: Array<string | null>) => { const p = parts.filter(Boolean); return p.length ? p.join(' ') : null; };
    switch (status) {
        case 'NOT_QUOTED':
            return { tone: 'open', isHistorical: false, label: 'Não cotado', detail: 'Registo de reconciliação: o fornecedor não cotou esta linha.' };
        case 'NOT_SUBMITTED':
            return { tone: 'open', isHistorical: false, label: 'Não submetida', detail: reuseSentence(it) };
        case 'IN_APPROVAL':
            return { tone: 'in-approval', isHistorical: false, label: `Em aprovação${batch}`, detail: null };
        case 'AWARDED':
            return { tone: 'awarded', isHistorical: false, label: `Adjudicada${batch}`, detail: null };
        case 'CANDIDATE_NOT_AWARDED':
            return { tone: 'not-awarded', isHistorical: false, label: `Candidata — não adjudicada${batch}`, detail: null };
        case 'AWARD_CANCELLED':
            return { tone: 'historical', isHistorical: true, label: `Adjudicação cancelada${batch}`, detail: join(lineSentence(it), reuseSentence(it)) };
        case 'CANDIDATE_CANCELLED':
            return { tone: 'historical', isHistorical: true, label: `Candidata — lote devolvido/cancelado${batch}`, detail: join(lineSentence(it), reuseSentence(it)) };
        case 'REJECTED':
            return { tone: 'rejected', isHistorical: false, label: `Lote rejeitado${batch}`, detail: reuseSentence(it) };
        default:
            return null;
    }
}

// ── v2.249.5 §9.2 — SELECTION follows the D3 reuse eligibility, the historical label is display only ──

export interface QuotationSelectabilityLike extends QuotationLifecycleLike {
    items?: QuotationItemLifecycleLike[] | null;
}

export interface QuotationSelectability {
    selectable: boolean;
    reason: 'ELIGIBLE' | 'REUSE_AUTHORIZED' | 'REUSE_AUTHORIZATION_REQUIRED' | 'NO_ITEM_FACTS';
    /** Sentence for a tooltip / inline note. Null when nothing needs saying. */
    hint: string | null;
}

const CONSIDERED_RECONCILIATION = new Set(['MAPPED', 'SUBSTITUTE', 'EXTRA_ITEM']);

/**
 * Whether the card may offer "Selecionar" for this quotation. Mirrors the server guard of POST …/quotations/{id}/select:
 * one considered item that is eligible (never used in a cancelled/released batch) or whose reuse was explicitly authorised
 * (D3) makes the quotation selectable — a historical adjudication never blocks an authorised candidate; a quotation whose
 * every item is reuse-blocked is not selectable. Without item facts (legacy payload) the lifecycle decides.
 */
export function describeQuotationSelectability(q: QuotationSelectabilityLike): QuotationSelectability {
    const items = (q.items || []).filter(it => !it.reconciliationStatus || CONSIDERED_RECONCILIATION.has(it.reconciliationStatus.toUpperCase()));
    if (items.length === 0) {
        const historical = !!describeQuotationLifecycle(q)?.isHistorical;
        return { selectable: !historical, reason: 'NO_ITEM_FACTS', hint: historical ? 'Cotação histórica: adjudicação cancelada, sem itens atuais.' : null };
    }
    if (items.some(it => !it.isReuseBlocked && !it.isReuseAuthorized)) return { selectable: true, reason: 'ELIGIBLE', hint: null };
    if (items.some(it => it.isReuseAuthorized)) {
        return { selectable: true, reason: 'REUSE_AUTHORIZED', hint: 'Reutilização autorizada: a cotação pode ser selecionada apesar da adjudicação anterior cancelada.' };
    }
    return { selectable: false, reason: 'REUSE_AUTHORIZATION_REQUIRED', hint: 'Seleção bloqueada: todos os itens desta cotação foram usados num lote cancelado/devolvido e exigem autorização de reutilização.' };
}

/** Colours per tone (inline-style consumers). */
export const LIFECYCLE_TONE_STYLE: Record<LifecycleTone, { bg: string; fg: string; border: string }> = {
    historical: { bg: '#f1f5f9', fg: '#475569', border: '#94a3b8' },
    awarded: { bg: '#4f46e5', fg: '#ffffff', border: '#4f46e5' },
    'in-approval': { bg: '#fef3c7', fg: '#92400e', border: '#f59e0b' },
    'not-awarded': { bg: '#f8fafc', fg: '#64748b', border: '#cbd5e1' },
    rejected: { bg: '#fee2e2', fg: '#991b1b', border: '#fca5a5' },
    partial: { bg: '#fff7ed', fg: '#9a3412', border: '#fdba74' },
    open: { bg: '#f8fafc', fg: '#64748b', border: '#e2e8f0' },
};
