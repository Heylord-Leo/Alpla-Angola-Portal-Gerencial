import { useState, useEffect, useCallback, useRef } from 'react';
import { api } from '../lib/api';
import { fetchPendingContractApprovals } from '../lib/contractsApi';

/**
 * Lightweight hook that returns the total pending approvals count
 * for the current user (requests + contracts + supplier fichas).
 * 
 * Reuses the existing API endpoints without creating backend changes.
 * The count is refreshed:
 *  - On mount
 *  - Every 2 minutes (background polling)
 *  - Manually via the returned `refresh` function
 */
export function usePendingApprovalsCount() {
    const [count, setCount] = useState(0);
    const [loading, setLoading] = useState(true);
    const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

    const fetchCount = useCallback(async () => {
        try {
            const [requestsData, contractsData, supplierData] = await Promise.all([
                api.requests.getPendingApprovals().catch(() => null),
                fetchPendingContractApprovals().catch(() => null),
                api.lookups.getPendingSupplierApprovals().catch(() => ({ pendingFichas: [] }))
            ]);

            const requestCount = requestsData
                ? (requestsData.areaApprovals?.length || 0) + (requestsData.finalApprovals?.length || 0)
                : 0;

            const contractCount = contractsData
                ? (contractsData.technicalApprovals?.length || 0) + (contractsData.finalApprovals?.length || 0)
                : 0;

            const supplierCount = supplierData?.pendingFichas?.length || 0;

            setCount(requestCount + contractCount + supplierCount);
        } catch (error) {
            // Fail silently — the sidebar badge is informational only
            console.debug('[usePendingApprovalsCount] Failed to fetch count:', error);
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => {
        fetchCount();

        // Background refresh every 2 minutes
        intervalRef.current = setInterval(fetchCount, 2 * 60 * 1000);

        return () => {
            if (intervalRef.current) {
                clearInterval(intervalRef.current);
            }
        };
    }, [fetchCount]);

    return { count, loading, refresh: fetchCount };
}
