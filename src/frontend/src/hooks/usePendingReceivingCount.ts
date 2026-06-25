import { useState, useEffect, useCallback, useRef } from 'react';
import { api } from '../lib/api';
import { useAuth } from "../features/auth/AuthContext";

export function usePendingReceivingCount() {
    const [count, setCount] = useState(0);
    const [loading, setLoading] = useState(true);
    const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
    const { user } = useAuth();

    const fetchCount = useCallback(async () => {
        try {
            const hasReceivingRole = user?.roles?.includes('Receiving');
            const hasBuyerRole = user?.roles?.includes('Buyer');
            
            // Only fetch if user actually has the roles that care about receiving
            if (!hasReceivingRole && !hasBuyerRole) {
                setCount(0);
                setLoading(false);
                return;
            }

            const statuses = await api.lookups.getRequestStatuses();
            const waitingDeliveryStatus = statuses.find(s => s.code === 'WAITING_SUPPLIER_DELIVERY');
            
            if (waitingDeliveryStatus) {
                const listData = await api.requests.list('', {
                    statusIds: waitingDeliveryStatus.id.toString(),
                    myTasksOnly: true
                }, 1, 1);
                
                setCount(listData.pagedResult?.totalCount || 0);
            }
        } catch (error) {
            console.debug('[usePendingReceivingCount] Failed to fetch count:', error);
        } finally {
            setLoading(false);
        }
    }, [user]);

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

    return {
        count,
        loading,
        refresh: fetchCount
    };
}
