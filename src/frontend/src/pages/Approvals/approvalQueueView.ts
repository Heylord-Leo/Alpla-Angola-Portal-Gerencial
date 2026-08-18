import { ApprovalQueueItemDto } from '../../types';
import { formatCurrencyAO } from '../../lib/utils';

// ============================================================================
// Approval Center queue view model — ONE deterministic pipeline shared by the
// live list and the post-action refresh, so no filter silently resets another:
//
//   raw queue → scope (Área/Final) → search → chip filters → sort → grouped
//
// The frontend NEVER recomputes approval totals — it renders the backend's
// authoritative `actionableAmount` (ApprovalQueueAmountResolver) and only
// decides how to *display* it (value, "not defined", inconsistency warning).
// ============================================================================

export type SortMode = 'default' | 'oldest' | 'value_desc' | 'value_asc';

export const HIGH_VALUE_THRESHOLD = 500000;

/** Accent-insensitive, lowercased, trimmed — the matching normal form. */
export function normalizeText(value: string | null | undefined): string {
    if (!value) return '';
    return value
        .normalize('NFD')
        .replace(/[̀-ͯ]/g, '') // strip diacritics
        .toLowerCase()
        .trim();
}

/** Digits only — lets "096" match "REQ-17/07/2026-096" regardless of formatting. */
function digitsOnly(value: string | null | undefined): string {
    return (value || '').replace(/\D/g, '');
}

/**
 * True when the queue row matches the free-text search. Case/accent-insensitive,
 * trimmed, partial substring across all searchable fields; request numbers also
 * match on their digits (with or without the REQ-dd/mm/yyyy- formatting).
 * Batch rows also match on their lot ("Lote #2", "lote 2", or a bare "#2").
 *
 * Search operates on BATCH-LEVEL rows: a request number shared by two lots returns
 * both rows; "Lote #2" returns only that lot.
 */
export function matchesApprovalSearch(req: ApprovalQueueItemDto, rawQuery: string): boolean {
    const q = normalizeText(rawQuery);
    if (!q) return true;

    // Lot-style query ("lote #2", "lote 2", "#2") is EXCLUSIVE: it matches only rows whose own
    // lotNumber equals the requested lot and never falls through to the request-number digit match
    // (otherwise "Lote #2" would also match Lote #1 via the shared request number's digits).
    const lotQuery = q.match(/^(?:lote\s*#?\s*|#\s*)(\d+)$/);
    if (lotQuery) {
        return req.lotNumber != null && Number(lotQuery[1]) === req.lotNumber;
    }
    // Bare "lote" lists every lot (batch) row.
    if (q === 'lote' || q === 'lotes') {
        return req.lotNumber != null;
    }

    const fields = [
        req.requestNumber,
        req.requesterName,
        req.departmentName,
        req.requestTypeCode,
        req.requestTypeName,
        req.statusName,
        req.requestStatusName,
        req.supplierDisplay,
        req.plantName,
        req.companyName,
        req.costCenterCode,
        req.costCenterName,
        req.lotNumber != null ? `lote #${req.lotNumber}` : null,
    ];

    if (fields.some(f => normalizeText(f).includes(q))) return true;

    // Request-number digit match: "096" or "17072026096" → REQ-17/07/2026-096.
    // Returns every lot row sharing that request number.
    const qDigits = digitsOnly(rawQuery);
    if (qDigits.length > 0 && digitsOnly(req.requestNumber).includes(qDigits)) return true;

    return false;
}

/** Amount used for value sort / high-value filter: authoritative actionable amount, else 0. */
function sortableAmount(req: ApprovalQueueItemDto): number {
    return req.actionableAmount ?? 0;
}

export function filterByChips(
    list: ApprovalQueueItemDto[],
    activeFilters: string[],
    getUrgency: (needBy: string | null | undefined, statusCode: string) => { priority: number } | null,
): ApprovalQueueItemDto[] {
    return list.filter(req => {
        if (activeFilters.includes('urgent')) {
            const urgency = getUrgency(req.needByDateUtc, req.batchStatus);
            if (!urgency || urgency.priority < 2) return false;
        }
        if (activeFilters.includes('high_value')) {
            // Unresolved amounts (null) are never "high value".
            if ((req.actionableAmount ?? 0) < HIGH_VALUE_THRESHOLD) return false;
        }
        if (activeFilters.includes('has_alert')) {
            if (req.requestTypeCode === 'QUOTATION' && req.selectedQuotationId) return false;
            if (req.requestTypeCode !== 'QUOTATION') return false;
        }
        return true;
    });
}

export function sortList(list: ApprovalQueueItemDto[], sortMode: SortMode): ApprovalQueueItemDto[] {
    const res = [...list];
    switch (sortMode) {
        case 'oldest':
            return res.sort((a, b) => new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime());
        case 'value_desc':
            return res.sort((a, b) => sortableAmount(b) - sortableAmount(a));
        case 'value_asc':
            return res.sort((a, b) => sortableAmount(a) - sortableAmount(b));
        default:
            return res; // backend default order preserved
    }
}

/**
 * The full deterministic pipeline for one section (already scope-selected by the caller).
 * Search → chip filters → sort. Kept pure so the live memo and the post-action refresh
 * apply identical rules.
 */
export function processQueueSection(
    scoped: ApprovalQueueItemDto[],
    opts: {
        search: string;
        activeFilters: string[];
        sortMode: SortMode;
        getUrgency: (needBy: string | null | undefined, statusCode: string) => { priority: number } | null;
    },
): ApprovalQueueItemDto[] {
    const searched = scoped.filter(r => matchesApprovalSearch(r, opts.search));
    const filtered = filterByChips(searched, opts.activeFilters, opts.getUrgency);
    return sortList(filtered, opts.sortMode);
}

export type CardAmountState = 'value' | 'undefined' | 'inconsistent';

export interface CardAmount {
    /** Display string: a formatted amount, or the "not defined" label. */
    display: string;
    state: CardAmountState;
    /** True when a warning icon should be shown (unresolved or inconsistent). */
    warning: boolean;
}

/**
 * How to render one card's money. Uses the backend `actionableAmount` only:
 *  - null  → "Valor ainda não definido" (never a fake 0);
 *  - set   → formatted amount, flagged when the backend reports an inconsistency.
 * A genuine 0 renders as a real "AOA 0,00", distinct from the undefined state.
 */
export function resolveCardAmount(req: ApprovalQueueItemDto): CardAmount {
    if (req.actionableAmount == null) {
        // Candidate model: a batch row awaiting AREA approval legitimately has no amount yet —
        // the Buyer sent OPTIONS and the Area Approver still has to pick the winners. That is
        // the expected business state, not an anomaly, so no warning icon.
        if (req.approvalStage === 'AREA' && req.lotNumber != null) {
            return { display: 'A definir pelo Aprovador de Área', state: 'undefined', warning: false };
        }
        return { display: 'Valor ainda não definido', state: 'undefined', warning: true };
    }
    const display = formatCurrencyAO(req.actionableAmount, req.currencyCode || 'AOA');
    if (req.hasAmountInconsistency) {
        return { display, state: 'inconsistent', warning: true };
    }
    return { display, state: 'value', warning: false };
}

/**
 * Queue-total rule — mirrors the backend ApprovalQueueAmountResolver.SumActionable exactly:
 * sum of actionable amounts, excluding unresolved (null). Genuine zeros contribute 0.
 * Card amounts and this summary can therefore never use different monetary rules.
 */
export function sumActionable(list: ApprovalQueueItemDto[]): number {
    return list.reduce((sum, req) => sum + (req.actionableAmount ?? 0), 0);
}
