import { useState, useEffect, useRef } from 'react';
import { api } from '../../../lib/api';
import { ItemAllocation, ItemAssignment } from '../WizardStepAllocation';

export const useBudgetPreview = (
    requestId: string,
    itemAwards: Record<string, string>,
    itemAssignments: Record<string, ItemAssignment>,
    itemAllocations: Record<string, ItemAllocation[]>,
    assignedCount: number,
    totalCount: number
) => {
    const [budgetPreview, setBudgetPreview] = useState<any>(null);
    const cacheRef = useRef<Record<string, any>>({});
    
    useEffect(() => {
        const hasAllocations = assignedCount > 0;
        if (!hasAllocations) {
            setBudgetPreview(null);
            return;
        }

        const payload = {
            itemAwards,
            itemAssignments,
            itemAllocations
        };
        const cacheKey = JSON.stringify(payload);

        if (cacheRef.current[cacheKey]) {
            setBudgetPreview(cacheRef.current[cacheKey]);
            return;
        }

        let isSubscribed = true;

        const timerId = setTimeout(async () => {
            try {
                const result = await api.requests.getBudgetPreview(requestId, payload);
                
                if (isSubscribed) {
                    cacheRef.current[cacheKey] = result;
                    setBudgetPreview(result);
                }
            } catch (err) {
                if (isSubscribed) {
                    console.error("Failed to fetch budget preview during allocation", err);
                }
            }
        }, 500);

        return () => {
            clearTimeout(timerId);
            isSubscribed = false;
        };
    }, [itemAllocations, itemAwards, itemAssignments, requestId, assignedCount, totalCount]);

    const getBudgetStatusForLine = (plantId: number | null, costCenterId: number | null) => {
        if (!budgetPreview || !plantId || !costCenterId) return null;
        const allocs = budgetPreview.allocations || [];
        return allocs.find((a: any) => a.plantId === plantId && a.costCenterId === costCenterId);
    };

    return { budgetPreview, getBudgetStatusForLine };
};
