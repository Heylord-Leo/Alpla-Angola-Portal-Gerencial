/**
 * Viana 3 Production Monitor — TV Signage Page (READ-ONLY)
 *
 * Routes: /operations/viana3-monitor                      → default screen (historical URL)
 *         /operations/viana3-monitor/screen/:screenKey     → one dedicated TV screen
 * Example: /operations/viana3-monitor/screen/s7k2mq?fullscreen=true&refresh=120
 *
 * The production-area TV is for viewing only: no drag handles, no edit buttons,
 * no menus, no layout controls. Card order, card sizes and hidden cards come from
 * the shared layout saved in the Portal (edited on a computer at
 * /operations/viana3-monitor/layout by OPERATIONS / System Administrator). The
 * layout is re-read with every data refresh, so a change made in the editor
 * reaches every TV within one refresh interval without touching the TV.
 *
 * Rendering is delegated to the shared board (viana3/Viana3Board.tsx), composed on
 * a fixed 1920×1080 stage and scaled to the TV viewport. All production data,
 * business rules, refresh/stale indicators and provisional labels are unchanged.
 */

import React, { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { useSearchParams, useParams } from 'react-router-dom';
import { fetchViana3Monitor, fetchViana3MonitorScreenLayout } from '../../lib/operationsApi';
import { fetchViana3CardDesignBundle } from '../../lib/viana3CardDesignApi';
import type { Viana3CardDesignBundle } from '../../types/viana3CardDesign.types';
import { ApiError } from '../../lib/api';
import type { Viana3MonitorResponse, Viana3MonitorLayout } from '../../types/operations.types';
import { useEnvironment } from '../../contexts/EnvironmentContext';
import {
    Viana3Board, Viana3Stage, injectViana3CSS, getFreshness, clampRefresh, DEFAULT_REFRESH, C,
} from './viana3/Viana3Board';
import { resolveLayout } from './viana3/viana3Layout';

export default function Viana3ProductionMonitorPage() {
    const [searchParams] = useSearchParams();
    const { screenKey: screenKeyParam } = useParams<{ screenKey?: string }>();
    const screenKey = (screenKeyParam || 'default').trim().toLowerCase();
    const isFullscreen = searchParams.get('fullscreen') === 'true';
    const refreshOverride = searchParams.get('refresh');

    const [data, setData] = useState<Viana3MonitorResponse | null>(null);
    const [layout, setLayout] = useState<Viana3MonitorLayout | null>(null);
    const [designs, setDesigns] = useState<Viana3CardDesignBundle | null>(null);
    const [screenMissing, setScreenMissing] = useState(false);
    const [lastSuccessMs, setLastSuccessMs] = useState<number | null>(null);
    const [hasError, setHasError] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [countdown, setCountdown] = useState(DEFAULT_REFRESH);
    const [isFetching, setIsFetching] = useState(false);

    const refreshTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
    const countdownTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
    const isFetchingRef = useRef(false);

    const effectiveRefresh = useMemo(() => {
        if (refreshOverride) {
            const parsed = parseInt(refreshOverride, 10);
            if (!isNaN(parsed)) return clampRefresh(parsed);
        }
        if (data?.refreshSeconds) return clampRefresh(data.refreshSeconds);
        return DEFAULT_REFRESH;
    }, [refreshOverride, data?.refreshSeconds]);

    const fetchData = useCallback(async () => {
        if (isFetchingRef.current) return;
        isFetchingRef.current = true;
        setIsFetching(true);
        try {
            // Data and shared layout are read together; a layout failure never blocks the data
            // (the last known layout — or the default — stays in use).
            const [dataResult, layoutResult, designResult] = await Promise.allSettled([
                fetchViana3Monitor({ refreshSeconds: refreshOverride ? parseInt(refreshOverride, 10) : undefined }),
                fetchViana3MonitorScreenLayout(screenKey),
                fetchViana3CardDesignBundle(),
            ]);

            // Designs are display preferences: a failure keeps the last known designs (or the built-in cards).
            if (designResult.status === 'fulfilled') setDesigns(designResult.value);

            if (layoutResult.status === 'fulfilled') {
                setLayout(layoutResult.value);
                setScreenMissing(false);
            } else if (layoutResult.reason instanceof ApiError && layoutResult.reason.status === 404) {
                // The screen was deleted (or the URL is wrong): say so instead of showing another screen's layout.
                setScreenMissing(true);
            }

            if (dataResult.status === 'fulfilled') {
                const result = dataResult.value;
                setData(result);
                setLastSuccessMs(Date.now());
                setHasError(false);
                setErrorMessage(null);
                setCountdown(clampRefresh(result.refreshSeconds || DEFAULT_REFRESH));
            } else {
                const err: unknown = dataResult.reason;
                setHasError(true);
                setErrorMessage(err instanceof Error ? err.message : 'Erro de conexão');
            }
        } finally {
            setIsLoading(false);
            isFetchingRef.current = false;
            setIsFetching(false);
        }
    }, [refreshOverride, screenKey]);

    useEffect(() => { injectViana3CSS(); }, []);

    useEffect(() => {
        if (!isFullscreen) return;
        document.body.classList.add('v3-fullscreen-mode');
        return () => { document.body.classList.remove('v3-fullscreen-mode'); };
    }, [isFullscreen]);

    useEffect(() => { fetchData(); }, [fetchData]);

    useEffect(() => {
        if (refreshTimerRef.current) clearInterval(refreshTimerRef.current);
        if (countdownTimerRef.current) clearInterval(countdownTimerRef.current);

        countdownTimerRef.current = setInterval(() => {
            setCountdown(p => (p <= 1 ? effectiveRefresh : p - 1));
        }, 1000);

        refreshTimerRef.current = setInterval(() => {
            fetchData();
            setCountdown(effectiveRefresh);
        }, effectiveRefresh * 1000);

        return () => {
            if (refreshTimerRef.current) clearInterval(refreshTimerRef.current);
            if (countdownTimerRef.current) clearInterval(countdownTimerRef.current);
        };
    }, [effectiveRefresh, fetchData]);

    const { isTest } = useEnvironment();
    const freshness = getFreshness(data, lastSuccessMs, hasError && data == null);
    const resolved = useMemo(() => resolveLayout(layout, data?.machines ?? []), [layout, data]);

    const rootStyle: React.CSSProperties = {
        minHeight: '100vh', height: '100vh', width: '100vw',
        background: '#0a0f1e',
        color: '#e2e8f0',
        fontFamily: "'Inter', 'Segoe UI', -apple-system, sans-serif",
        display: 'flex', flexDirection: 'column', overflow: 'hidden',
    };

    // ─── Loading / hard error (no data yet) ───
    if (isLoading) {
        return (
            <div style={{ ...rootStyle, alignItems: 'center', justifyContent: 'center' }}>
                <div style={{ fontSize: '28px', fontWeight: 700, color: C.muted }}>A carregar o monitor de produção…</div>
            </div>
        );
    }

    if (screenMissing) {
        return (
            <div style={{ ...rootStyle, alignItems: 'center', justifyContent: 'center', textAlign: 'center', padding: '40px' }}>
                <div style={{ fontSize: '64px', marginBottom: '16px' }}>⚠</div>
                <div style={{ fontSize: '30px', fontWeight: 800, color: '#fca5a5' }}>Ecrã da TV não encontrado</div>
                <div style={{ fontSize: '18px', color: C.muted, marginTop: '10px', maxWidth: '720px' }}>
                    O ecrã «{screenKey}» não existe ou foi eliminado no Portal. Confirme o URL no editor de layout da TV.
                </div>
                <div style={{ fontSize: '14px', color: C.dim, marginTop: '18px' }}>
                    Nova verificação em {countdown}s
                </div>
            </div>
        );
    }

    if (hasError && !data) {
        return (
            <div style={{ ...rootStyle, alignItems: 'center', justifyContent: 'center', textAlign: 'center', padding: '40px' }}>
                <div style={{ fontSize: '64px', marginBottom: '16px' }}>⚠</div>
                <div style={{ fontSize: '30px', fontWeight: 800, color: '#fca5a5' }}>Fonte indisponível</div>
                <div style={{ fontSize: '18px', color: C.muted, marginTop: '10px', maxWidth: '720px' }}>
                    {errorMessage || 'Não foi possível obter dados do AlplaPROD.'}
                </div>
                <div style={{ fontSize: '14px', color: C.dim, marginTop: '18px' }}>
                    Nova tentativa em {countdown}s
                </div>
            </div>
        );
    }

    return (
        <div style={rootStyle}>
            <Viana3Stage background="#0a0f1e">
                <Viana3Board
                    data={data!}
                    layout={resolved}
                    freshness={freshness}
                    countdown={countdown}
                    refreshSeconds={effectiveRefresh}
                    isFetching={isFetching}
                    connectionLost={hasError && !!data}
                    connectionMessage={errorMessage}
                    isTest={isTest}
                    showDiagnosticsLink={!isFullscreen}
                    screenName={layout && !layout.isDefault ? layout.name : null}
                    designs={designs}
                />
            </Viana3Stage>
        </div>
    );
}
