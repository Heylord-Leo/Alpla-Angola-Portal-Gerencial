import { ItemAllocation } from '../WizardStepAllocation';
import { validateReconciliationJustification } from '../../../lib/reconciliationJustificationValidator';
import { isCandidateBatchItem, selectionRequiresJustification } from '../candidateSelection';

export const useWizardValidation = (
    activeItems: any[],
    itemAllocations: Record<string, ItemAllocation[]>,
    itemAwards: Record<string, string>,
    budgetJustification: string,
    isBudgetJustificationRequired: boolean,
    isPayment: boolean,
    extraItemDecisions: Record<string, { decision: 'APPROVE' | 'REJECT' | 'ADJUST' | null; comment: string }> = {},
    extraItemsCount: number = 0,
    approvedExtraItems: any[] = [],
    isBatchReviewMode: boolean = false,
    quotations: any[] = [],
    /** The active ApprovalBatch (batch-review mode only) — its unresolvedLegacyLines is the
     * batch-scoped signal that actually replaces the request-wide extraItemDecisions/extraItemsCount
     * check in that mode (see isStep3Valid). */
    activeBatch: any | null = null,
    /** Candidate model — Area winner selections (ApprovalBatchItemId → candidate id). */
    batchSelections: Record<string, string> = {},
    /** Candidate model — per-item winner justifications (mandatory for non-cheapest picks). */
    selectionJustifications: Record<string, string> = {}
) => {

    /** Candidate model: one batch item is "decided" when exactly one of ITS candidates is
     * selected AND, for a non-cheapest pick, a meaningful justification exists. Legacy items
     * (no candidates) fall back to the historical winner-chain check. */
    const isBatchItemDecided = (item: any): boolean => {
        const batchItem = (activeBatch?.items || []).find((bi: any) => bi.requestLineItemId === item.id);
        if (batchItem && isCandidateBatchItem(batchItem)) {
            // Server-decided (Area decision already stamped — the Final-stage read): the item is
            // decided and the read-only review needs no local selection state.
            if (batchItem.selectedCandidateId
                && (batchItem.candidates || []).some((c: any) => c.id === batchItem.selectedCandidateId)) {
                return true;
            }
            const selectedCandidateId = batchSelections[batchItem.id];
            if (!selectedCandidateId) return false;
            const candidate = (batchItem.candidates || []).find((c: any) => c.id === selectedCandidateId);
            if (!candidate) return false;
            if (selectionRequiresJustification(batchItem, selectedCandidateId)) {
                return validateReconciliationJustification(selectionJustifications[batchItem.id] || '').isValid;
            }
            return true;
        }

        // Legacy buyer-selected item — the winner chain must resolve against real quotation data.
        const selectedQuotationItemId = item.selectedQuotationItemId;
        if (!selectedQuotationItemId) return false;
        return quotations.some((quotation: any) =>
            quotation.items?.some((quotationItem: any) => quotationItem.id === selectedQuotationItemId)
        );
    };
    
    const isAssigned = (itemId: string) => {
        const allocations = itemAllocations[itemId] || [];
        if (allocations.length === 0) return false;
        
        const totalPercentage = allocations.reduce((sum, a) => sum + (a.percentage || 0), 0);
        const allFilled = allocations.every(a => a.plantId && a.costCenterId && a.percentage > 0);
        
        return totalPercentage === 100 && allFilled;
    };

    const isStep2Valid = (): boolean => {
        const allBaseItemsAssigned = activeItems.every((item: any) => isAssigned(item.id));
        const allExtraItemsAssigned = approvedExtraItems.every((item: any) => isAssigned(item.id));
        return allBaseItemsAssigned && allExtraItemsAssigned;
    };

    const isStep3Valid = (): boolean => {
        if (isPayment) return true;

        let allItemsAwarded = false;
        if (isBatchReviewMode) {
            // Fail closed: if quotations is missing or empty, batch mode cannot validate winner chain
            if (!quotations || quotations.length === 0) {
                return false;
            }

            // Candidate model: each item must have exactly one selected candidate (with a valid
            // justification when required); legacy items keep the strict winner-chain check.
            allItemsAwarded = activeItems.length > 0 && activeItems.every(isBatchItemDecided);
        } else {
            allItemsAwarded = activeItems.every((item: any) => !!itemAwards[item.id] || !!item.selectedQuotationItemId);
        }

        if (isBatchReviewMode) {
            // Extra-item decisions are finalized by the Buyer at CreateBatch/UpdateBatch time
            // (IBatchExtraItemDecisionService) — the Area Approver reviews the already-resolved
            // batch, never re-decides scope, and no UI here ever populates extraItemDecisions.
            // The only thing that can legitimately block progression is a genuinely unresolved
            // legacy EXTRA_ITEM line (a batch created before this rule existed, with no recorded
            // decision at all) — a valid, already-decided EXTRA_ITEM/IGNORED line must never block.
            const allExtrasDecided = (activeBatch?.unresolvedLegacyLines?.length ?? 0) === 0;
            return allItemsAwarded && allExtrasDecided;
        }

        // All extra items must have a decision
        const allExtrasDecided = Object.keys(extraItemDecisions).length === extraItemsCount &&
            Object.values(extraItemDecisions).every(d => d.decision !== null);

        // If rejected, comment is required
        const rejectedHaveComments = Object.values(extraItemDecisions).every(d =>
            d.decision !== 'REJECT' || (d.decision === 'REJECT' && d.comment.trim().length > 0)
        );

        return allItemsAwarded && allExtrasDecided && rejectedHaveComments;
    };

    const isStep4Valid = (): boolean => {
        if (isBudgetJustificationRequired) {
            return budgetJustification.trim().length >= 20;
        }
        return true;
    };

    const step2AssignedCount = activeItems.filter((item: any) => isAssigned(item.id)).length + 
                               approvedExtraItems.filter((item: any) => isAssigned(item.id)).length;
    const step3AwardedCount = activeItems.filter((item: any) => {
        if (isBatchReviewMode) {
            if (!quotations || quotations.length === 0) return false;
            return isBatchItemDecided(item);
        }
        return !!itemAwards[item.id] || !!item.selectedQuotationItemId;
    }).length;

    return {
        isAssigned,
        isStep2Valid,
        isStep3Valid,
        isStep4Valid,
        step2AssignedCount,
        step3AwardedCount
    };
};
