import { useState, useEffect, useCallback, useRef } from 'react';
import { api } from '../lib/api';
import { useAuth } from '../features/auth/AuthContext';

/**
 * v2.242.0 — count of Finance-returned PO corrections awaiting the authenticated Buyer, for the footer
 * sticker. CROSS-TYPE and personal: it reads the canonical `personal-po-corrections/count` endpoint,
 * which counts distinct requests with any WAITING_PO_CORRECTION group personally owned by the current
 * Buyer — QUOTATION via Request.BuyerId, PAYMENT via RequestPoGroup.PoResponsibleBuyerId. It is
 * group-based, so a mixed request such as REQ-275 (scalar aggregates to PO_PARTIALLY_UPLOADED) is
 * counted, and a stale scalar whose group is already PO_ISSUED is excluded. Unassigned corrections
 * (no resolvable owner) never count here. Buyer-role gated; 2-minute background poll. It replaces the
 * earlier QUOTATION-only Buyer-queue summary source so PAYMENT corrections (e.g. REQ-254 owned by
 * Celestina) are no longer missed.
 */
export function usePendingPoCorrectionsCount() {
    const [count, setCount] = useState(0);
    const [loading, setLoading] = useState(true);
    const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
    const { user } = useAuth();

    const fetchCount = useCallback(async () => {
        try {
            if (!user?.roles?.includes('Buyer')) {
                setCount(0);
                setLoading(false);
                return;
            }
            const personalCount = await api.requests.personalPoCorrectionsCount();
            setCount(personalCount ?? 0);
        } catch (error) {
            console.debug('[usePendingPoCorrectionsCount] Failed to fetch count:', error);
        } finally {
            setLoading(false);
        }
    }, [user]);

    useEffect(() => {
        fetchCount();
        intervalRef.current = setInterval(fetchCount, 2 * 60 * 1000);
        return () => {
            if (intervalRef.current) clearInterval(intervalRef.current);
        };
    }, [fetchCount]);

    return { count, loading, refresh: fetchCount };
}
