import { ItemAllocation } from '../WizardStepAllocation';

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
    quotations: any[] = []
) => {
    
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

            allItemsAwarded = activeItems.length > 0 && activeItems.every((item: any) => {
                // Strictly use lineItem.selectedQuotationItemId (no fallback to itemAwards or local state)
                const selectedQuotationItemId = item.selectedQuotationItemId;
                if (!selectedQuotationItemId) return false;

                // Every active batch item must resolve to a valid QuotationItem in quotations
                return quotations.some((quotation: any) =>
                    quotation.items?.some((quotationItem: any) => quotationItem.id === selectedQuotationItemId)
                );
            });
        } else {
            allItemsAwarded = activeItems.every((item: any) => !!itemAwards[item.id] || !!item.selectedQuotationItemId);
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
            const selectedQuotationItemId = item.selectedQuotationItemId;
            if (!selectedQuotationItemId) return false;
            return quotations.some((quotation: any) =>
                quotation.items?.some((quotationItem: any) => quotationItem.id === selectedQuotationItemId)
            );
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
