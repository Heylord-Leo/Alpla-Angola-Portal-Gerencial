import { createContext, useContext, useEffect, useRef, useState, useCallback, ReactNode } from 'react';
import { API_BASE_URL } from '../lib/api';
import { buildInfo } from '../buildInfo';
import { versionSignal, UpdateReason } from '../lib/versionSignal';

// ============================================================================
// Frontend version monitor (Phase C).
//
// Proactive UX only — the AUTHORITATIVE protection is the backend enforcement middleware. This
// provider polls the anonymous, DB-free `/api/app/version` and flags update-required ONLY when the
// server returns a Valid build whose `buildId` differs from this frontend's compiled `buildId`
// (exact equality; never ordered). A failed probe is treated as transient (network/IIS recycle/etc.)
// and never opens the modal.
//
// It also subscribes to the shared signal bus, so a 409 CLIENT_VERSION_OUTDATED (from apiFetch) or a
// stale-chunk failure flips the same state.
// ============================================================================

interface VersionContextValue {
    updateRequired: boolean;
    reason: UpdateReason | null;
}

const VersionContext = createContext<VersionContextValue>({ updateRequired: false, reason: null });

export function useVersion(): VersionContextValue {
    return useContext(VersionContext);
}

const POLL_INTERVAL_MS = 5 * 60 * 1000; // 5 minutes while visible
const MIN_CHECK_GAP_MS = 3000;          // debounce rapid focus/visibility bursts

export function VersionProvider({ children }: { children: ReactNode }) {
    const [updateRequired, setUpdateRequired] = useState(false);
    const [reason, setReason] = useState<UpdateReason | null>(null);
    const lastCheck = useRef(0);

    const flag = useCallback((r: UpdateReason) => {
        setUpdateRequired(true);
        setReason(prev => prev ?? r);
    }, []);

    // Bus: 409 from apiFetch and stale-chunk failures both surface here.
    useEffect(() => versionSignal.subscribe(flag), [flag]);

    const check = useCallback(async () => {
        // A local/dev build has no real release identity — never compare or flag.
        if (!buildInfo.isRelease) return;
        if (versionSignal.isOutdated()) return;

        const now = Date.now();
        if (now - lastCheck.current < MIN_CHECK_GAP_MS) return;
        lastCheck.current = now;

        try {
            const res = await fetch(`${API_BASE_URL}/api/app/version`, {
                headers: { 'Cache-Control': 'no-cache' },
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            // Only a VALID + DIFFERENT server build is a real update. Anything else (degraded server
            // metadata, same build) is ignored.
            if (
                data &&
                data.buildMetadataStatus === 'Valid' &&
                typeof data.buildId === 'string' &&
                data.buildId !== buildInfo.buildId
            ) {
                versionSignal.markOutdated('version'); // notifies subscribers → flag('version')
            }
        } catch {
            // Transient (network loss / IIS recycle / deploy window). Do NOT flag outdated; the
            // periodic interval and focus/visibility events provide the retry cadence.
        }
    }, []);

    useEffect(() => {
        void check(); // on startup

        const onVisibility = () => {
            if (document.visibilityState === 'visible') void check();
        };
        const onFocus = () => void check();

        document.addEventListener('visibilitychange', onVisibility);
        window.addEventListener('focus', onFocus);

        // Poll only while visible (pause when hidden).
        const interval = window.setInterval(() => {
            if (document.visibilityState === 'visible') void check();
        }, POLL_INTERVAL_MS);

        return () => {
            document.removeEventListener('visibilitychange', onVisibility);
            window.removeEventListener('focus', onFocus);
            window.clearInterval(interval);
        };
    }, [check]);

    return (
        <VersionContext.Provider value={{ updateRequired, reason }}>
            {children}
        </VersionContext.Provider>
    );
}
