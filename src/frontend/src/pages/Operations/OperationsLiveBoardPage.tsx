/**
 * Operations Live Transfer Board — TV Signage Page (v2 Redesign)
 *
 * Route: /operations/live-board/:plant
 * Example: /operations/live-board/VIANA1?fullscreen=true&refresh=60
 *
 * A signage-optimized board for TV / digital signage at 3–5 m viewing distance.
 * Designed for zero interaction — auto-refresh, auto-paging, no scrollbars.
 *
 * v2.179.0 Redesign:
 * - Large KPI summary bar with icons
 * - Compact cards with short route (V2→V1), large PO, SVG timeline icons
 * - Auto-paging carousel (max 4 cards/column, 8s rotation)
 * - Strong attention cards with pulse + short operational messages
 * - Visual empty states with large icons
 * - No scrollbars — everything fits on screen
 * - Larger typography for long-distance readability
 *
 * @since v2.178.0 — Phase Live 3 (original)
 * @since v2.179.0 — TV signage UX redesign
 */

import React, { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { fetchOperationsLiveBoard } from '../../lib/operationsApi';
import type {
    OperationsLiveBoardResponse,
    OperationsLiveBoardTransfer,
    OperationsLiveBoardStep,
} from '../../types/operations.types';

// ─── Constants ───

const MIN_REFRESH = 30;
const MAX_REFRESH = 300;
const DEFAULT_REFRESH = 60;
const CARDS_PER_PAGE = 4;
const PAGE_ROTATE_MS = 8000; // 8 seconds per page

const STALE_WARN_MS = 5 * 60 * 1000;
const STALE_ERROR_MS = 15 * 60 * 1000;

// ─── Plant short names (for compact route display) ───

const PLANT_SHORT: Record<string, string> = {
    VIANA1: 'V1',
    VIANA2: 'V2',
    VIANA3: 'V3',
};

function shortPlant(code: string): string {
    return PLANT_SHORT[code] || code;
}

// ─── SVG Icons (inline, no dependencies) ───

const ICON = {
    document: (color: string, size = 22) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
            <polyline points="14 2 14 8 20 8" />
            <line x1="16" y1="13" x2="8" y2="13" />
            <line x1="16" y1="17" x2="8" y2="17" />
        </svg>
    ),
    truck: (color: string, size = 22) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <rect x="1" y="3" width="15" height="13" />
            <polygon points="16 8 20 8 23 11 23 16 16 16 16 8" />
            <circle cx="5.5" cy="18.5" r="2.5" />
            <circle cx="18.5" cy="18.5" r="2.5" />
        </svg>
    ),
    inbox: (color: string, size = 22) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <polyline points="22 12 16 12 14 15 10 15 8 12 2 12" />
            <path d="M5.45 5.11L2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" />
        </svg>
    ),
    halfCircle: (color: string, size = 22) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20z" />
            <path d="M12 2a10 10 0 0 1 0 20" fill={color} opacity="0.3" />
        </svg>
    ),
    checkCircle: (color: string, size = 22) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
            <polyline points="22 4 12 14.01 9 11.01" />
        </svg>
    ),
    warning: (color: string, size = 22) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
            <line x1="12" y1="9" x2="12" y2="13" />
            <line x1="12" y1="17" x2="12.01" y2="17" />
        </svg>
    ),
    packageIn: (color: string, size = 22) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M16.5 9.4l-9-5.19M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" />
            <polyline points="3.27 6.96 12 12.01 20.73 6.96" />
            <line x1="12" y1="22.08" x2="12" y2="12" />
        </svg>
    ),
    truckOut: (color: string, size = 22) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <rect x="1" y="3" width="15" height="13" />
            <polygon points="16 8 20 8 23 11 23 16 16 16 16 8" />
            <circle cx="5.5" cy="18.5" r="2.5" />
            <circle cx="18.5" cy="18.5" r="2.5" />
        </svg>
    ),
    clock: (color: string, size = 18) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" />
            <polyline points="12 6 12 12 16 14" />
        </svg>
    ),
    emptyBox: (color: string, size = 64) => (
        <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" opacity="0.5">
            <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" />
            <polyline points="3.27 6.96 12 12.01 20.73 6.96" />
            <line x1="12" y1="22.08" x2="12" y2="12" />
        </svg>
    ),
};

// Timeline step icon mapping
const STEP_ICON_MAP: Record<string, (color: string, size?: number) => React.ReactNode> = {
    ORDERED: ICON.document,
    SENT: ICON.truck,
    RECEIVING: ICON.inbox,
    PARTIAL: ICON.halfCircle,
    COMPLETED: ICON.checkCircle,
};

// ─── Status color mapping ───

const STATUS_COLORS: Record<string, { bg: string; text: string; border: string; glow: string }> = {
    info:    { bg: '#1e3a5f', text: '#93c5fd', border: '#3b82f6', glow: 'rgba(59,130,246,0.3)' },
    success: { bg: '#14532d', text: '#86efac', border: '#22c55e', glow: 'rgba(34,197,94,0.3)' },
    warning: { bg: '#713f12', text: '#fcd34d', border: '#f59e0b', glow: 'rgba(245,158,11,0.3)' },
    error:   { bg: '#7f1d1d', text: '#fca5a5', border: '#ef4444', glow: 'rgba(239,68,68,0.3)' },
};

// ─── Helpers ───

function clampRefresh(v: number): number {
    return Math.max(MIN_REFRESH, Math.min(MAX_REFRESH, v));
}

function formatTime(iso: string): string {
    try {
        return new Date(iso).toLocaleTimeString('pt-PT', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    } catch {
        return '--:--:--';
    }
}

function shortAge(minutes: number | null): string {
    if (minutes == null || minutes < 0) return '';
    if (minutes < 60) return `${minutes}min`;
    const h = Math.floor(minutes / 60);
    return h >= 24 ? `${Math.floor(h / 24)}d ${h % 24}h` : `${h}h`;
}

function formatQty(n: number | null): string {
    if (n == null) return '—';
    return n.toLocaleString('pt-PT');
}

type Freshness = 'fresh' | 'stale' | 'error';

function getFreshness(lastMs: number | null, noData: boolean): Freshness {
    if (noData || lastMs == null) return 'error';
    const age = Date.now() - lastMs;
    if (age > STALE_ERROR_MS) return 'error';
    if (age > STALE_WARN_MS) return 'stale';
    return 'fresh';
}

const FRESH_COLOR: Record<Freshness, string> = {
    fresh: '#22c55e', stale: '#f59e0b', error: '#ef4444',
};

// ─── Injected CSS ───

const KEYFRAMES_ID = 'lb-keyframes-v2';

function injectCSS(): void {
    if (document.getElementById(KEYFRAMES_ID)) return;
    const s = document.createElement('style');
    s.id = KEYFRAMES_ID;
    s.textContent = `
        @keyframes lb-spin { to { transform: rotate(360deg); } }
        @keyframes lb-pulse {
            0%, 100% { box-shadow: 0 0 0 0 rgba(245,158,11,0); }
            50% { box-shadow: 0 0 20px 6px rgba(245,158,11,0.25); }
        }
        @keyframes lb-glow-active {
            0%, 100% { filter: drop-shadow(0 0 2px rgba(245,158,11,0.3)); }
            50% { filter: drop-shadow(0 0 6px rgba(245,158,11,0.7)); }
        }
        @keyframes lb-fade-in {
            from { opacity: 0; transform: translateY(8px); }
            to { opacity: 1; transform: translateY(0); }
        }
        .lb-card-enter { animation: lb-fade-in 0.4s ease-out; }
        .lb-fullscreen-mode .app-shell-layout,
        .lb-fullscreen-mode .sidebar,
        .lb-fullscreen-mode nav { display: none !important; }
    `;
    document.head.appendChild(s);
}

// ─── KPI Card (top summary) ───

function KpiCard({ icon, label, value, color }: {
    icon: React.ReactNode; label: string; value: number; color: string;
}) {
    return (
        <div style={{
            display: 'flex', alignItems: 'center', gap: '14px',
            padding: '12px 22px', borderRadius: '14px',
            background: `${color}12`, border: `1px solid ${color}30`,
        }}>
            <div style={{ opacity: 0.9 }}>{icon}</div>
            <div>
                <div style={{ fontSize: '32px', fontWeight: 800, color, lineHeight: 1, letterSpacing: '-1px' }}>
                    {value}
                </div>
                <div style={{ fontSize: '13px', color: '#94a3b8', fontWeight: 500, marginTop: '2px', textTransform: 'uppercase' as const, letterSpacing: '0.5px' }}>
                    {label}
                </div>
            </div>
        </div>
    );
}

// ─── Mini Timeline (SVG icons, horizontal) ───

function MiniTimeline({ steps }: { steps: OperationsLiveBoardStep[] }) {
    const stateColor = (st: string) =>
        st === 'done' ? '#22c55e' : st === 'active' ? '#f59e0b' : '#475569';

    return (
        <div style={{ display: 'flex', alignItems: 'center', gap: '0', marginTop: '8px' }}>
            {steps.map((step, i) => {
                const color = stateColor(step.state);
                const iconFn = STEP_ICON_MAP[step.code];
                const isActive = step.state === 'active';
                const connectorDone = i > 0 && (step.state === 'done' || steps[i - 1].state === 'done');

                return (
                    <React.Fragment key={step.code}>
                        {i > 0 && (
                            <div style={{
                                flex: 1, height: '3px', minWidth: '8px',
                                backgroundColor: connectorDone ? '#22c55e' : '#334155',
                                borderRadius: '2px',
                            }} />
                        )}
                        <div style={{
                            display: 'flex', flexDirection: 'column', alignItems: 'center',
                            ...(isActive ? { animation: 'lb-glow-active 2s ease-in-out infinite' } : {}),
                        }}>
                            <div style={{
                                width: '32px', height: '32px',
                                borderRadius: '50%',
                                display: 'flex', alignItems: 'center', justifyContent: 'center',
                                backgroundColor: `${color}20`,
                                border: isActive ? `2px solid ${color}` : `1px solid ${color}40`,
                            }}>
                                {iconFn ? iconFn(color, 16) : <span style={{ color, fontSize: '14px' }}>•</span>}
                            </div>
                        </div>
                    </React.Fragment>
                );
            })}
        </div>
    );
}

// ─── Transfer Card (compact, TV-optimized) ───

function TransferCard({ transfer }: { transfer: OperationsLiveBoardTransfer }) {
    const isDone = transfer.currentStage === 'COMPLETED';
    const sc = STATUS_COLORS[transfer.statusColor] || STATUS_COLORS.info;
    const route = `${shortPlant(transfer.originPlant)} → ${shortPlant(transfer.destinationPlant)}`;

    // Short attention message
    const attMsg = transfer.isAttention && transfer.ageMinutes != null
        ? `⚠ ${shortAge(transfer.ageMinutes)} aguardando`
        : transfer.isAttention && transfer.attentionReason
            ? `⚠ ${transfer.attentionReason}`
            : null;

    return (
        <div className="lb-card-enter" style={{
            borderRadius: '14px', padding: '14px 18px',
            background: isDone ? 'rgba(34,197,94,0.05)' : transfer.isAttention ? 'rgba(245,158,11,0.06)' : 'rgba(30,41,59,0.6)',
            border: transfer.isAttention
                ? `2px solid ${sc.border}`
                : isDone ? '1px solid rgba(34,197,94,0.25)' : '1px solid rgba(148,163,184,0.1)',
            opacity: isDone ? 0.65 : 1,
            ...(transfer.isAttention ? { animation: 'lb-pulse 3s ease-in-out infinite' } : {}),
        }}>
            {/* Row 1: PO + route + badge */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: '10px' }}>
                    <span style={{ fontSize: '24px', fontWeight: 800, color: '#f1f5f9', letterSpacing: '-0.5px' }}>
                        #{transfer.idBestellung}
                    </span>
                    <span style={{ fontSize: '15px', color: '#64748b', fontWeight: 600, letterSpacing: '1px' }}>
                        {route}
                    </span>
                </div>
                <span style={{
                    fontSize: '13px', fontWeight: 700, padding: '4px 14px', borderRadius: '10px',
                    backgroundColor: sc.bg, color: sc.text, border: `1px solid ${sc.border}50`,
                    whiteSpace: 'nowrap',
                }}>
                    {transfer.currentStageLabel}
                </span>
            </div>

            {/* Row 2: Material + Qty */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
                <span style={{
                    fontSize: '14px', color: '#94a3b8', flex: 1, overflow: 'hidden',
                    textOverflow: 'ellipsis', whiteSpace: 'nowrap', marginRight: '12px',
                }}>
                    {transfer.materialName || '—'}
                </span>
                <span style={{ fontSize: '16px', color: '#e2e8f0', fontWeight: 600, whiteSpace: 'nowrap' }}>
                    {transfer.receivedQuantity != null && transfer.receivedQuantity > 0
                        ? `${formatQty(transfer.receivedQuantity)}/${formatQty(transfer.orderedQuantity)}`
                        : formatQty(transfer.orderedQuantity)
                    }
                </span>
            </div>

            {/* Row 3: Attention message OR age */}
            {attMsg ? (
                <div style={{ fontSize: '14px', color: '#fbbf24', fontWeight: 600, marginBottom: '2px' }}>
                    {attMsg}
                </div>
            ) : transfer.ageMinutes != null && transfer.ageMinutes > 0 ? (
                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '12px', color: '#64748b', marginBottom: '2px' }}>
                    {ICON.clock('#64748b', 13)}
                    <span>{shortAge(transfer.ageMinutes)}</span>
                </div>
            ) : null}

            {/* Row 4: Timeline */}
            <MiniTimeline steps={transfer.steps} />
        </div>
    );
}

// ─── Empty State ───

function EmptyColumn({ icon, message }: { icon: React.ReactNode; message: string }) {
    return (
        <div style={{
            flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center',
            justifyContent: 'center', gap: '16px', padding: '40px 20px',
        }}>
            {icon}
            <span style={{ fontSize: '18px', color: '#475569', fontWeight: 500, textAlign: 'center' }}>
                {message}
            </span>
        </div>
    );
}

// ─── Paged Column ───

function PagedColumn({ title, icon, transfers, emptyIcon, emptyMsg, accent }: {
    title: string;
    icon: React.ReactNode;
    transfers: OperationsLiveBoardTransfer[];
    emptyIcon: React.ReactNode;
    emptyMsg: string;
    accent: string;
}) {
    const [page, setPage] = useState(0);
    const totalPages = Math.max(1, Math.ceil(transfers.length / CARDS_PER_PAGE));

    // Auto-rotate pages
    useEffect(() => {
        if (totalPages <= 1) { setPage(0); return; }
        const timer = setInterval(() => {
            setPage(p => (p + 1) % totalPages);
        }, PAGE_ROTATE_MS);
        return () => clearInterval(timer);
    }, [totalPages]);

    // Reset page if data changes
    useEffect(() => { setPage(0); }, [transfers.length]);

    const visible = transfers.slice(page * CARDS_PER_PAGE, (page + 1) * CARDS_PER_PAGE);
    const hiddenCount = Math.max(0, transfers.length - CARDS_PER_PAGE);

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', flex: 1, overflow: 'hidden' }}>
            {/* Column header */}
            <div style={{
                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                paddingBottom: '10px', borderBottom: `2px solid ${accent}30`,
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    {icon}
                    <span style={{ fontSize: '20px', fontWeight: 700, color: '#e2e8f0', textTransform: 'uppercase', letterSpacing: '1px' }}>
                        {title}
                    </span>
                    <span style={{
                        fontSize: '16px', fontWeight: 800, padding: '2px 12px', borderRadius: '10px',
                        backgroundColor: `${accent}20`, color: accent, minWidth: '28px', textAlign: 'center',
                    }}>
                        {transfers.length}
                    </span>
                </div>
                {totalPages > 1 && (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <span style={{ fontSize: '13px', color: '#64748b' }}>
                            {page * CARDS_PER_PAGE + 1}–{Math.min((page + 1) * CARDS_PER_PAGE, transfers.length)} de {transfers.length}
                        </span>
                        {/* Page dots */}
                        <div style={{ display: 'flex', gap: '4px' }}>
                            {Array.from({ length: totalPages }).map((_, i) => (
                                <div key={i} style={{
                                    width: i === page ? '16px' : '6px', height: '6px',
                                    borderRadius: '3px',
                                    backgroundColor: i === page ? accent : '#334155',
                                    transition: 'all 0.3s',
                                }} />
                            ))}
                        </div>
                    </div>
                )}
            </div>

            {/* Cards or empty */}
            {transfers.length === 0 ? (
                <EmptyColumn icon={emptyIcon} message={emptyMsg} />
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', flex: 1 }}>
                    {visible.map(t => (
                        <TransferCard key={`${t.direction}-${t.idBestellung}-${page}`} transfer={t} />
                    ))}
                </div>
            )}

            {/* Overflow indicator */}
            {hiddenCount > 0 && totalPages > 1 && (
                <div style={{ textAlign: 'center', fontSize: '13px', color: '#64748b', padding: '4px 0' }}>
                    +{hiddenCount} em fila
                </div>
            )}
        </div>
    );
}

// ─── Main Component ───

export default function OperationsLiveBoardPage() {
    const { plant } = useParams<{ plant: string }>();
    const [searchParams] = useSearchParams();

    const isFullscreen = searchParams.get('fullscreen') === 'true';
    const refreshOverride = searchParams.get('refresh');
    const maxInboundOverride = searchParams.get('maxInbound');
    const maxOutboundOverride = searchParams.get('maxOutbound');
    const completedWindowOverride = searchParams.get('completedWindowHours');

    const [data, setData] = useState<OperationsLiveBoardResponse | null>(null);
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

    const effectivePlant = plant || 'VIANA1';

    // ─── Fetch ───
    const fetchData = useCallback(async () => {
        if (isFetchingRef.current) return;
        isFetchingRef.current = true;
        setIsFetching(true);
        try {
            const result = await fetchOperationsLiveBoard({
                plant: effectivePlant,
                refreshSeconds: refreshOverride ? parseInt(refreshOverride, 10) : undefined,
                maxInbound: maxInboundOverride ? parseInt(maxInboundOverride, 10) : undefined,
                maxOutbound: maxOutboundOverride ? parseInt(maxOutboundOverride, 10) : undefined,
                completedWindowHours: completedWindowOverride ? parseInt(completedWindowOverride, 10) : undefined,
            });
            setData(result);
            setLastSuccessMs(Date.now());
            setHasError(false);
            setErrorMessage(null);
            setCountdown(clampRefresh(result.refreshSeconds || DEFAULT_REFRESH));
        } catch (err: unknown) {
            setHasError(true);
            setErrorMessage(err instanceof Error ? err.message : 'Erro de conexão');
        } finally {
            setIsLoading(false);
            isFetchingRef.current = false;
            setIsFetching(false);
        }
    }, [effectivePlant, refreshOverride, maxInboundOverride, maxOutboundOverride, completedWindowOverride]);

    // Inject CSS
    useEffect(() => { injectCSS(); }, []);

    // Fullscreen
    useEffect(() => {
        if (!isFullscreen) return;
        document.body.classList.add('lb-fullscreen-mode');
        return () => { document.body.classList.remove('lb-fullscreen-mode'); };
    }, [isFullscreen]);

    // Initial + auto-refresh
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

    const freshness = getFreshness(lastSuccessMs, hasError && data == null);

    // ─── Root style ───
    const rootStyle: React.CSSProperties = {
        minHeight: '100vh', height: '100vh',
        background: 'linear-gradient(160deg, #0a0f1e 0%, #111827 40%, #0a0f1e 100%)',
        color: '#e2e8f0',
        fontFamily: "'Inter', 'Segoe UI', -apple-system, sans-serif",
        display: 'flex', flexDirection: 'column', overflow: 'hidden',
        ...(isFullscreen ? {
            position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, zIndex: 9999,
        } : {}),
    };

    // ─── Loading ───
    if (isLoading && !data) {
        return (
            <div style={rootStyle}>
                <div style={{
                    flex: 1, display: 'flex', flexDirection: 'column',
                    alignItems: 'center', justifyContent: 'center', gap: '20px',
                }}>
                    <div style={{
                        width: '48px', height: '48px', border: '4px solid #1e293b',
                        borderTop: '4px solid #3b82f6', borderRadius: '50%',
                        animation: 'lb-spin 1s linear infinite',
                    }} />
                    <span style={{ fontSize: '20px', color: '#94a3b8' }}>
                        A carregar Live Board — {effectivePlant}
                    </span>
                </div>
            </div>
        );
    }

    // ─── Fatal error ───
    if (!data && hasError) {
        return (
            <div style={rootStyle}>
                <div style={{
                    flex: 1, display: 'flex', flexDirection: 'column',
                    alignItems: 'center', justifyContent: 'center', gap: '20px',
                }}>
                    {ICON.warning('#ef4444', 64)}
                    <span style={{ fontSize: '24px', fontWeight: 700, color: '#fca5a5' }}>
                        Sem conexão ao servidor
                    </span>
                    <span style={{ fontSize: '16px', color: '#64748b', maxWidth: '400px', textAlign: 'center' }}>
                        {errorMessage || 'Verifique a rede e tente novamente.'}
                    </span>
                    <span style={{ fontSize: '14px', color: '#475569' }}>
                        Próxima tentativa em {countdown}s
                    </span>
                </div>
            </div>
        );
    }

    // ─── Board ───
    const plantName = data?.plantName || effectivePlant;
    const summary = data?.summary;

    return (
        <div style={rootStyle} id="operations-live-board">

            {/* ── Top bar: Plant + Freshness + Countdown ── */}
            <div style={{
                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                padding: '16px 32px 14px', borderBottom: '1px solid rgba(148,163,184,0.1)',
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
                    <div style={{
                        width: '14px', height: '14px', borderRadius: '50%',
                        backgroundColor: FRESH_COLOR[freshness],
                        boxShadow: `0 0 10px ${FRESH_COLOR[freshness]}80`,
                    }} />
                    <h1 style={{
                        margin: 0, fontSize: '26px', fontWeight: 800, color: '#f8fafc',
                        letterSpacing: '1px', textTransform: 'uppercase',
                    }}>
                        📋 Live Board — {plantName}
                    </h1>
                    {isFetching && (
                        <span style={{ fontSize: '13px', color: '#3b82f6', fontWeight: 500 }}>⟳</span>
                    )}
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '20px', fontSize: '14px', color: '#64748b' }}>
                    {data && <span>🕐 {formatTime(data.lastUpdated)}</span>}
                    <span style={{ color: countdown <= 10 ? '#f59e0b' : '#475569' }}>
                        ⏱ {countdown}s
                    </span>
                </div>
            </div>

            {/* Countdown bar */}
            <div style={{ height: '3px', background: '#0f172a' }}>
                <div style={{
                    height: '100%', backgroundColor: '#3b82f6',
                    width: `${(countdown / effectiveRefresh) * 100}%`,
                    transition: 'width 1s linear', borderRadius: '0 2px 2px 0',
                }} />
            </div>

            {/* Error / Stale banner */}
            {hasError && data && (
                <div style={{
                    padding: '8px 32px', background: 'rgba(239,68,68,0.12)',
                    borderBottom: '1px solid rgba(239,68,68,0.2)',
                    color: '#fca5a5', fontSize: '14px',
                    display: 'flex', alignItems: 'center', gap: '8px',
                }}>
                    {ICON.warning('#ef4444', 16)}
                    <span>Sem conexão — dados anteriores exibidos</span>
                </div>
            )}
            {!hasError && freshness === 'stale' && (
                <div style={{
                    padding: '8px 32px', background: 'rgba(245,158,11,0.08)',
                    borderBottom: '1px solid rgba(245,158,11,0.15)',
                    color: '#fcd34d', fontSize: '14px',
                    display: 'flex', alignItems: 'center', gap: '8px',
                }}>
                    {ICON.warning('#f59e0b', 16)}
                    <span>Dados podem estar desatualizados</span>
                </div>
            )}

            {/* ── KPI Summary Bar ── */}
            {summary && (
                <div style={{
                    display: 'flex', justifyContent: 'center', gap: '16px',
                    padding: '16px 32px', flexWrap: 'wrap',
                    borderBottom: '1px solid rgba(148,163,184,0.08)',
                }}>
                    <KpiCard icon={ICON.packageIn('#3b82f6', 28)} label="Entradas" value={summary.inboundTotal} color="#3b82f6" />
                    <KpiCard icon={ICON.truckOut('#8b5cf6', 28)} label="Saídas" value={summary.outboundTotal} color="#8b5cf6" />
                    <KpiCard icon={ICON.warning('#f59e0b', 28)} label="Atenção" value={summary.attentionCount} color="#f59e0b" />
                    <KpiCard icon={ICON.checkCircle('#22c55e', 28)} label="Concluídos" value={summary.completedRecentCount} color="#22c55e" />
                </div>
            )}

            {/* ── Two-column cards area ── */}
            <div style={{
                flex: 1, display: 'grid', gridTemplateColumns: '1fr 1fr',
                gap: '28px', padding: '16px 32px', overflow: 'hidden',
            }}>
                <PagedColumn
                    title="Entradas"
                    icon={ICON.packageIn('#3b82f6', 24)}
                    transfers={data?.inbound || []}
                    emptyIcon={ICON.emptyBox('#3b82f6')}
                    emptyMsg="Sem entradas no momento"
                    accent="#3b82f6"
                />
                <PagedColumn
                    title="Saídas"
                    icon={ICON.truckOut('#8b5cf6', 24)}
                    transfers={data?.outbound || []}
                    emptyIcon={ICON.emptyBox('#8b5cf6')}
                    emptyMsg="Sem saídas no momento"
                    accent="#8b5cf6"
                />
            </div>

            {/* ── Bottom status ── */}
            <div style={{
                padding: '10px 32px', borderTop: '1px solid rgba(148,163,184,0.08)',
                display: 'flex', justifyContent: 'center', gap: '16px',
                fontSize: '12px', color: '#475569',
            }}>
                <span>Alpla Angola — Live Transfer Board v2.179.0</span>
                {data?.queryDurationMs != null && <span>⚡ {data.queryDurationMs}ms</span>}
            </div>
        </div>
    );
}
