import { SavedQuotationDto } from '../../types';
import { ExtraItemLine } from './BatchExtraItemsDecisionPanel';

/** Minimal shape both PartialApprovalBatchModal and BatchReworkModal's "eligible item" rows share
 * — just enough to resolve which quotation each currently-selected winner belongs to. */
export interface WinnerSelection {
    quotationItemId: string | null;
    quotationId: string | null;
}

/**
 * Computes the genuine EXTRA_ITEM lines belonging to any quotation that currently contributes a
 * selected winner — never request-wide. Single source of truth shared by batch creation and
 * batch rework, so "relevant" is defined identically everywhere and mirrors the backend's own
 * contributing-quotation rule (BatchExtraItemDecisionService.LoadExtraLinesAsync).
 */
export interface ParsedExtraItemError {
    kind: 'pending' | 'locked' | 'generic';
    pendingItems?: { description: string; supplierName?: string | null }[];
    lockedQuotationItemId?: string | null;
    lockedReason?: string | null;
    message?: string;
    /**
     * Best-effort only. Confirmed by reading ApprovalBatchController.ExtraItemDecisionErrorResult's
     * fallback branch: an invalid EXCLUDE comment currently returns a plain ProblemDetails 400
     * (`{ title, detail, status: 400 }`) with NO structured quotationItemId in Extensions — the
     * `detail` string only happens to quote the line's description
     * (e.g. "Comentário obrigatório para excluir o item 'X': ..."). This match is a UX nicety that
     * degrades gracefully (falls back to a generic banner) if the description isn't found verbatim
     * — it is NOT an authoritative field-error mapping. A robust fix requires the backend to add
     * Extensions["quotationItemId"] to that response, mirroring the two structured 409s.
     */
    genericMatchedQuotationItemId?: string | null;
}

/** Shared client-side classification of a CreateBatch/UpdateBatch error response, used identically
 * by batch creation and batch rework so a structured 409 is never handled differently by the two
 * entry points. See ParsedExtraItemError's field docs for the one deliberately best-effort case. */
export function parseExtraItemDecisionError(err: any, lines: ExtraItemLine[]): ParsedExtraItemError {
    const details = err?.details;

    if (details?.code === 'EXTRA_ITEMS_PENDING_DECISION' && Array.isArray(details.pendingItems)) {
        return {
            kind: 'pending',
            pendingItems: details.pendingItems.map((p: any) => ({ description: p.description, supplierName: p.supplierName }))
        };
    }

    if (details?.code === 'EXTRA_ITEM_INCLUSION_LOCKED') {
        return { kind: 'locked', lockedQuotationItemId: details.quotationItemId || null, lockedReason: details.reason || null };
    }

    const message: string = err?.message || 'Ocorreu um erro ao processar a solicitação.';
    const quotedMatch = message.match(/'([^']+)'/);
    let genericMatchedQuotationItemId: string | null = null;
    if (quotedMatch) {
        const found = lines.find(l => l.description === quotedMatch[1]);
        if (found) genericMatchedQuotationItemId = found.quotationItemId;
    }
    return { kind: 'generic', message, genericMatchedQuotationItemId };
}

function contributingQuotationIdSet(selections: WinnerSelection[]): Set<string> {
    const ids = new Set<string>();
    selections.forEach(s => {
        if (s.quotationId) ids.add(s.quotationId);
    });
    return ids;
}

function toExtraItemLine(qi: any, q: SavedQuotationDto): ExtraItemLine {
    return {
        quotationItemId: qi.id,
        quotationId: q.id,
        description: qi.description,
        quantity: qi.quantity,
        unitPrice: qi.unitPrice || 0,
        taxableBase: qi.taxableBase ?? null,
        ivaAmount: qi.ivaAmount || 0,
        ivaRatePercent: qi.ivaRatePercent || 0,
        lineTotal: qi.lineTotal || 0,
        supplierName: q.supplierNameSnapshot || 'Fornecedor',
        reconciliationJustification: qi.reconciliationJustification || null
    };
}

export function computeRelevantExtraLines(selections: WinnerSelection[], quotations: SavedQuotationDto[]): ExtraItemLine[] {
    const contributingQuotationIds = contributingQuotationIdSet(selections);
    const lines: ExtraItemLine[] = [];
    quotations.forEach(q => {
        if (!contributingQuotationIds.has(q.id)) return;
        (q.items || []).forEach(qi => {
            if (qi.reconciliationStatus === 'EXTRA_ITEM') {
                lines.push(toExtraItemLine(qi, q));
            }
        });
    });
    return lines;
}
