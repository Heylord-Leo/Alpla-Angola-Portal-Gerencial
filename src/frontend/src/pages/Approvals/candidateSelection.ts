// Candidate-model helpers for the AREA approval wizard: the Buyer submitted OPTIONS
// (ApprovalBatchItemCandidate frozen snapshots) and the Area Approver selects exactly ONE
// winner per item. These helpers mirror the backend's rules (ApprovalBatchController.
// ResolveWinnerSelections) so the wizard blocks locally what the server would reject —
// the backend remains authoritative.

import { ApprovalBatchItemCandidate, ApprovalBatchItemSummary } from '../../types';

/** Mirror of RequestConstants.FinancialIntegrity.CalculateTolerance:
 * max(AbsoluteFloor 1.00, amount × 0.1%). */
export function financialTolerance(amount: number): number {
    return Math.max(1.0, Math.abs(amount) * 0.001);
}

export function isCandidateBatchItem(batchItem: ApprovalBatchItemSummary | any): boolean {
    return (batchItem?.candidates?.length ?? 0) > 0;
}

/** Lowest LineTotal among the item's candidates in the SAME currency as the given candidate. */
export function cheapestSameCurrency(
    candidates: ApprovalBatchItemCandidate[],
    currency: string
): number | null {
    const totals = candidates.filter(c => c.currency === currency).map(c => c.lineTotal);
    return totals.length > 0 ? Math.min(...totals) : null;
}

/** True when choosing this candidate requires a mandatory justification: it is more expensive
 * than the cheapest same-currency option beyond the FinancialIntegrity tolerance. Ties within
 * tolerance never require one. */
export function selectionRequiresJustification(
    batchItem: ApprovalBatchItemSummary | any,
    candidateId: string
): boolean {
    const candidates: ApprovalBatchItemCandidate[] = batchItem?.candidates || [];
    const selected = candidates.find(c => c.id === candidateId);
    if (!selected) return false;
    const cheapest = cheapestSameCurrency(candidates, selected.currency);
    if (cheapest === null) return false;
    return selected.lineTotal > cheapest + financialTolerance(cheapest);
}

export interface TentativeSelectionSummary {
    /** Batch items in scope (candidate-based + legacy). */
    itemCount: number;
    /** Items with a winner: tentative radio selection OR legacy pre-decided winner. */
    decidedCount: number;
    pendingCount: number;
    /** Distinct supplier names across the currently selected candidates (+ legacy winners
     * resolvable by the caller are NOT included here — snapshot-known suppliers only). */
    supplierNames: string[];
    /** Selected total per currency, from FROZEN candidate snapshots (legacy items excluded —
     * they carry no snapshot; the caller may append their live totals separately). */
    totalByCurrency: Record<string, number>;
    /** True once every candidate-based item has a selection (legacy items are always decided). */
    allDecided: boolean;
}

/** Live tentative summary for the selection step. `selections` maps ApprovalBatchItemId →
 * selected candidate id. Only FROZEN snapshot values contribute — never live quotation data. */
export function computeTentativeSummary(
    batchItems: (ApprovalBatchItemSummary | any)[],
    selections: Record<string, string>
): TentativeSelectionSummary {
    let decided = 0;
    const suppliers = new Set<string>();
    const totalByCurrency: Record<string, number> = {};

    // Local tentative selection first; the SERVER-decided winner (Area decision already stamped
    // — the Final-stage read) is the fallback, so decided batches summarize without any local state.
    const effectiveSelection = (bi: any): string | null =>
        selections[bi.id] ?? bi.selectedCandidateId ?? null;

    batchItems.forEach(bi => {
        if (isCandidateBatchItem(bi)) {
            const selectedId = effectiveSelection(bi);
            const candidate = selectedId
                ? (bi.candidates as ApprovalBatchItemCandidate[]).find(c => c.id === selectedId)
                : undefined;
            if (candidate) {
                decided++;
                suppliers.add(candidate.supplierName);
                totalByCurrency[candidate.currency] = (totalByCurrency[candidate.currency] || 0) + candidate.lineTotal;
            }
        } else if (bi.selectedQuotationItemId) {
            // Legacy buyer-selected item: already decided; no snapshot to sum here.
            decided++;
        }
    });

    return {
        itemCount: batchItems.length,
        decidedCount: decided,
        pendingCount: batchItems.length - decided,
        supplierNames: Array.from(suppliers),
        totalByCurrency,
        allDecided: batchItems
            .filter(bi => isCandidateBatchItem(bi))
            .every(bi => {
                const selectedId = effectiveSelection(bi);
                return !!selectedId && (bi.candidates as ApprovalBatchItemCandidate[]).some(c => c.id === selectedId);
            })
    };
}

/** Builds the area-approve Selections payload (identity + justification only — never values). */
export function buildSelectionsPayload(
    batchItems: (ApprovalBatchItemSummary | any)[],
    selections: Record<string, string>,
    justifications: Record<string, string>
): { approvalBatchItemId: string; selectedCandidateId: string; winnerSelectionJustification?: string }[] {
    return batchItems
        .filter(bi => isCandidateBatchItem(bi) && !!selections[bi.id])
        .map(bi => {
            const justification = (justifications[bi.id] || '').trim();
            return justification
                ? { approvalBatchItemId: bi.id, selectedCandidateId: selections[bi.id], winnerSelectionJustification: justification }
                : { approvalBatchItemId: bi.id, selectedCandidateId: selections[bi.id] };
        });
}
