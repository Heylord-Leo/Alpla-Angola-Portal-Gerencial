/**
 * Viana 3 Production Monitor — shared board renderer.
 *
 * Used by two pages that must look identical:
 *   • the read-only TV page (/operations/viana3-monitor) — no controls of any kind;
 *   • the layout editor preview (/operations/viana3-monitor/layout) at 1920×1080.
 *
 * The board is composed on a fixed 1920×1080 stage (`Viana3Stage` scales it to
 * whatever viewport it is shown on, letter-boxing when the aspect ratio differs).
 * Because the stage size never changes, the card placement and the automatic
 * content fitting produce the same result on the TV and in the editor preview.
 *
 * Content fitting: a card never clips. Each panel measures itself and, when its
 * content exceeds the card height, drops sections in a fixed order
 * (previous orders → illustration → waste tiles become one line). The level
 * reached is reported to the editor so the user can give the card more room.
 *
 * Production rules carried by the screen (see docs/OPERATIONS_VIANA3_PRODUCTION_MONITOR.md):
 *   • "Pré-formas lidas" = scanner postings, never physical consumption.
 *   • No scan → "Sem leitura de scanner", never a −100 % deviation.
 *   • Deviation is PROVISIONAL (1 preform = 1 bottle not confirmed; Waste excluded).
 *   • Retention (30012) and blocked positions are shown apart from available stock.
 *   • All times are Angola local (WAT, UTC+1) exactly as recorded in AlplaPROD.
 */

import React, { useLayoutEffect, useRef, useState, useEffect, useMemo, memo } from 'react';
import type {
    Viana3MonitorResponse,
    Viana3MachinePanel,
    Viana3Lot,
    Viana3PreformStock,
    Viana3MachineStatus,
    Viana3CardSize,
} from '../../../types/operations.types';
import { STAGE_WIDTH, STAGE_HEIGHT, placeCards, visibleCards, type ResolvedLayout } from './viana3Layout';
import type { Viana3CardDesignBundle } from '../../../types/viana3CardDesign.types';
import { resolveDesign } from './viana3CardDesign';
import { Viana3DesignedCard } from './Viana3DesignedPanel';

// ─── Constants ───

export const MIN_REFRESH = 60;
export const MAX_REFRESH = 300;
export const DEFAULT_REFRESH = 120;

const STALE_WARN_MS = 5 * 60 * 1000;
const STALE_ERROR_MS = 15 * 60 * 1000;

const MAX_PALLET_ICONS = 12;

// ─── Palette (dark surface; identity colours follow the entity everywhere on the page) ───
// Validated dark-mode categorical steps: blue (bottles), orange (preforms), neutral reference.

export const C = {
    bottle: '#3987e5',      // reported bottles
    preform: '#d95926',     // scanned preforms / preform pallets
    plan: '#94a3b8',        // planned reference (neutral)
    text: '#f1f5f9',
    textSecondary: '#cbd5e1',
    muted: '#94a3b8',
    dim: '#64748b',
    surface: 'rgba(30,41,59,0.6)',
    border: 'rgba(148,163,184,0.12)',
    good: '#22c55e',
    warn: '#f59e0b',
    bad: '#ef4444',
    info: '#60a5fa',
};

export const STATUS_STYLE: Record<Viana3MachineStatus, { color: string; bg: string }> = {
    RUNNING: { color: '#4ade80', bg: 'rgba(34,197,94,0.14)' },
    MAINTENANCE: { color: '#fbbf24', bg: 'rgba(245,158,11,0.14)' },
    NO_ORDER: { color: '#94a3b8', bg: 'rgba(148,163,184,0.14)' },
    PLACEHOLDER: { color: '#a78bfa', bg: 'rgba(139,92,246,0.14)' },
    IDLE: { color: '#94a3b8', bg: 'rgba(148,163,184,0.14)' },
};

// ─── Formatting (pt-PT, Angola wall-clock as recorded — no time-zone conversion) ───

export function clampRefresh(v: number): number {
    return Math.min(MAX_REFRESH, Math.max(MIN_REFRESH, v));
}

/** Parses an ISO wall-clock string without converting through the browser time zone. */
export function parseLocal(iso: string | null | undefined): { d: string; t: string; ts: string } | null {
    if (!iso) return null;
    const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?/.exec(iso);
    if (!m) return null;
    return { d: `${m[3]}/${m[2]}`, t: `${m[4]}:${m[5]}`, ts: `${m[4]}:${m[5]}:${m[6] ?? '00'}` };
}

export function fmtTime(iso: string | null | undefined): string {
    return parseLocal(iso)?.ts ?? '—';
}

export function fmtDateTime(iso: string | null | undefined): string {
    const p = parseLocal(iso);
    return p ? `${p.d} ${p.t}` : '—';
}

export function fmtInt(n: number | null | undefined): string {
    if (n === null || n === undefined || isNaN(n)) return '—';
    return Math.round(n).toLocaleString('pt-PT');
}

export function fmtKg(n: number | null | undefined): string {
    if (n === null || n === undefined || isNaN(n)) return '—';
    return `${n.toLocaleString('pt-PT', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} kg`;
}

export function fmtPct(ratio: number | null | undefined): string {
    if (ratio === null || ratio === undefined || isNaN(ratio)) return '—';
    const pct = ratio * 100;
    const sign = pct > 0 ? '+' : '';
    return `${sign}${pct.toLocaleString('pt-PT', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} %`;
}

/** Shortens long AlplaPROD article names for the TV without losing the identifying start. */
export function shortArticle(name: string | null | undefined, max = 34): string {
    if (!name) return '—';
    return name.length > max ? `${name.slice(0, max - 1)}…` : name;
}

export type Freshness = 'fresh' | 'stale' | 'error';

export function getFreshness(data: Viana3MonitorResponse | null, lastSuccessMs: number | null, hardError: boolean): Freshness {
    if (hardError) return 'error';
    if (data?.source.status === 'STALE') return 'stale';
    if (!lastSuccessMs) return 'error';
    const age = Date.now() - lastSuccessMs;
    if (age > STALE_ERROR_MS) return 'error';
    if (age > STALE_WARN_MS) return 'stale';
    return 'fresh';
}

const FRESH_COLOR: Record<Freshness, string> = { fresh: C.good, stale: C.warn, error: C.bad };

// ─── CSS injection (fullscreen conventions as the Live Board) ───

let cssInjected = false;

export function injectViana3CSS() {
    if (cssInjected) return;
    cssInjected = true;
    const s = document.createElement('style');
    s.id = 'v3-monitor-styles';
    s.textContent = `
        @keyframes v3-conveyor { to { stroke-dashoffset: -24; } }
        @keyframes v3-fade-in { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: translateY(0); } }
        .v3-panel { animation: v3-fade-in 0.4s ease-out; }
        .v3-conveyor-running { animation: v3-conveyor 1.2s linear infinite; }
        .v3-fullscreen-mode .app-shell-layout,
        .v3-fullscreen-mode .sidebar,
        .v3-fullscreen-mode nav { display: none !important; }
        .v3-bar-track { background: rgba(148,163,184,0.10); border-radius: 4px; position: relative; height: 22px; overflow: hidden; }
        .v3-bar-fill { height: 100%; border-radius: 0 4px 4px 0; transition: width 0.6s ease; }
    `;
    document.head.appendChild(s);
}

// ═══════════════════════════════════════════════════════════════════════
// Illustration: pallets of preforms + production line
// ═══════════════════════════════════════════════════════════════════════

export function PalletStack({ stock, running }: { stock: Viana3PreformStock | null; running: boolean }) {
    // Positions (pallets) outside retention and not blocked drive the number of icons.
    const positions = stock ? stock.availablePositions : 0;
    const icons = stock && stock.availableQty > 0 ? Math.max(1, Math.min(MAX_PALLET_ICONS, positions)) : 0;
    const cols = 4;
    const bw = 22, bh = 16, gap = 3;
    const width = cols * (bw + gap) + 8;
    const height = 3 * (bh + gap) + 14;

    return (
        <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} aria-label="Paletes de pré-formas disponíveis">
            <rect x={2} y={height - 10} width={width - 4} height={8} rx={2} fill="#8b5e34" opacity={0.9} />
            <rect x={6} y={height - 6} width={6} height={4} fill="#5b3d20" />
            <rect x={width - 12} y={height - 6} width={6} height={4} fill="#5b3d20" />
            {icons === 0 && (
                <rect x={6} y={6} width={width - 12} height={height - 22} rx={3}
                    fill="none" stroke={C.dim} strokeDasharray="4 3" strokeWidth={1.5} />
            )}
            {Array.from({ length: icons }).map((_, i) => {
                const r = Math.floor(i / cols);
                const c = i % cols;
                const x = 4 + c * (bw + gap);
                const y = height - 12 - (r + 1) * (bh + gap) + gap;
                return (
                    <g key={i}>
                        <rect x={x} y={y} width={bw} height={bh} rx={2}
                            fill={C.preform} opacity={running ? 0.95 : 0.7} stroke="#0b1120" strokeWidth={1} />
                        <line x1={x + 4} y1={y + bh / 2} x2={x + bw - 4} y2={y + bh / 2} stroke="#0b1120" strokeWidth={1} opacity={0.5} />
                    </g>
                );
            })}
        </svg>
    );
}

export function ProductionLine({ running, bottleColor }: { running: boolean; bottleColor: string }) {
    const w = 300, h = 96;
    const lineColor = running ? C.textSecondary : C.dim;
    return (
        <svg width={w} height={h} viewBox={`0 0 ${w} ${h}`} aria-label="Linha de produção">
            <path d="M14 14 h40 l-8 26 h-24 z" fill={C.preform} opacity={running ? 0.9 : 0.5} />
            <rect x={26} y={40} width={16} height={12} fill={C.preform} opacity={running ? 0.9 : 0.5} />
            <rect x={60} y={22} width={92} height={48} rx={6} fill="#1e293b" stroke={lineColor} strokeWidth={2} />
            <text x={106} y={44} textAnchor="middle" fontSize={11} fontWeight={700} fill={lineColor} letterSpacing={1}>SOPRO</text>
            <circle cx={78} cy={58} r={4} fill={running ? C.good : C.dim} />
            <circle cx={92} cy={58} r={4} fill={running ? C.good : C.dim} opacity={0.7} />
            <circle cx={106} cy={58} r={4} fill={running ? C.good : C.dim} opacity={0.45} />
            <line x1={152} y1={70} x2={290} y2={70} stroke={lineColor} strokeWidth={3} strokeLinecap="round" />
            <line x1={152} y1={70} x2={290} y2={70} stroke={running ? C.bottle : 'transparent'} strokeWidth={3}
                strokeDasharray="8 16" className={running ? 'v3-conveyor-running' : undefined} opacity={0.7} />
            <rect x={150} y={72} width={142} height={6} rx={2} fill="#1e293b" stroke={lineColor} strokeWidth={1} />
            {[170, 200, 230, 260].map((x, i) => (
                <g key={x} opacity={running ? 1 : 0.4}>
                    <rect x={x - 5} y={40} width={10} height={28} rx={3} fill={bottleColor} />
                    <rect x={x - 3} y={34} width={6} height={7} rx={1} fill={bottleColor} opacity={0.8} />
                    <rect x={x - 3.5} y={31} width={7} height={3} rx={1} fill="#0b1120" opacity={0.8 - i * 0.05} />
                </g>
            ))}
        </svg>
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Comparison bars (planned = reference, reported bottles, scanned preforms)
// ═══════════════════════════════════════════════════════════════════════

function ComparisonBars({ lot }: { lot: Viana3Lot }) {
    const planned = lot.plannedBottles ?? 0;
    const reported = lot.reportedBottles;
    const scanned = lot.scanCount > 0 ? lot.scannedPreforms : null;
    const scale = Math.max(planned, reported ?? 0, scanned ?? 0, 1);
    const pct = (v: number) => `${Math.max(0, Math.min(100, (v / scale) * 100))}%`;
    const planPct = pct(planned);

    const rows: { label: string; value: number | null; color: string; empty: string }[] = [
        { label: 'Planeado (garrafas)', value: planned > 0 ? planned : null, color: C.plan, empty: 'Sem quantidade planeada' },
        { label: 'Reportado (garrafas)', value: reported, color: C.bottle, empty: 'Sem produção reportada' },
        { label: 'Lidas no scanner (pré-formas)', value: scanned, color: C.preform, empty: lot.scanCount > 0 ? '—' : 'Sem leitura de scanner' },
    ];

    return (
        <div style={{ display: 'grid', gridTemplateColumns: '150px 1fr 110px', rowGap: '6px', columnGap: '10px', alignItems: 'center' }}>
            {rows.map(r => (
                <React.Fragment key={r.label}>
                    <div style={{ fontSize: '13px', color: C.textSecondary, lineHeight: 1.15 }}>{r.label}</div>
                    <div className="v3-bar-track">
                        {r.value !== null ? (
                            <div className="v3-bar-fill" style={{ width: pct(r.value), background: r.color }} />
                        ) : (
                            <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', paddingLeft: '8px', fontSize: '12px', color: C.muted, fontStyle: 'italic' }}>
                                {r.empty}
                            </div>
                        )}
                        {planned > 0 && (
                            <div style={{ position: 'absolute', top: 0, bottom: 0, left: planPct, width: '2px', background: C.text, opacity: 0.55 }} />
                        )}
                    </div>
                    <div style={{ fontSize: '18px', fontWeight: 800, color: C.text, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        {r.value !== null ? fmtInt(r.value) : ''}
                    </div>
                </React.Fragment>
            ))}
        </div>
    );
}

export function DeviationChip({ lot, large }: { lot: Viana3Lot; large?: boolean }) {
    const d = lot.deviation;
    let color = C.muted;
    let text = d.statusLabel;
    if (d.statusCode === 'PROVISIONAL' && d.ratio !== null) {
        const abs = Math.abs(d.ratio);
        color = abs <= 0.02 ? C.good : abs <= 0.05 ? C.warn : C.bad;
        text = `Desvio ${fmtPct(d.ratio)} · provisório`;
    }
    return (
        <span title={d.statusLabel} style={{
            display: 'inline-block', padding: large ? '4px 12px' : '2px 8px', borderRadius: '999px',
            fontSize: large ? '14px' : '12px', fontWeight: 700, color, background: `${color}1f`, border: `1px solid ${color}55`,
            whiteSpace: 'nowrap',
        }}>
            {text}
        </span>
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Waste (two independent values; kg recorded, units estimated)
// ═══════════════════════════════════════════════════════════════════════

function WasteTile({ label, kg, units, color }: { label: string; kg: number | null; units: number | null; color: string }) {
    return (
        <div style={{ flex: 1, minWidth: 0, padding: '8px 12px', borderRadius: '10px', background: 'rgba(15,23,42,0.5)', border: `1px solid ${C.border}` }}>
            <div style={{ fontSize: '12px', color: C.muted, display: 'flex', alignItems: 'center', gap: '6px' }}>
                <span style={{ width: 8, height: 8, borderRadius: 2, background: color, display: 'inline-block', flexShrink: 0 }} />
                {label}
            </div>
            <div style={{ fontSize: '20px', fontWeight: 800, color: C.text, fontVariantNumeric: 'tabular-nums', lineHeight: 1.2 }}>
                {kg !== null ? fmtKg(kg) : '—'}
            </div>
            <div style={{ fontSize: '12px', color: C.dim }}>
                {units !== null ? `≈ ${fmtInt(units)} un. (estimativa)` : kg !== null ? 'estimativa indisponível (peso desconhecido)' : 'sem registo'}
            </div>
        </div>
    );
}

/** One-line waste summary used when the card has no room for the two tiles. Values stay separate. */
function WasteLine({ lot }: { lot: Viana3Lot }) {
    const w = lot.waste;
    const est = (u: number | null) => (u !== null ? ` (≈ ${fmtInt(u)} un., estimativa)` : '');
    return (
        <div style={{ fontSize: '12px', color: C.textSecondary, display: 'flex', gap: '14px', flexWrap: 'wrap', fontVariantNumeric: 'tabular-nums' }}>
            <span style={{ color: C.muted, fontWeight: 700, letterSpacing: '0.4px' }}>WASTE</span>
            <span><span style={{ color: C.bottle }}>■</span> Garrafas (art. 56) {w.rejectedBottlesKg !== null ? fmtKg(w.rejectedBottlesKg) : 'sem registo'}{est(w.rejectedBottlesUnitsEstimate)}</span>
            <span><span style={{ color: C.preform }}>■</span> Pré-formas (art. 55) {w.rejectedPreformsKg !== null ? fmtKg(w.rejectedPreformsKg) : 'sem registo'}{est(w.rejectedPreformsUnitsEstimate)}</span>
            <span style={{ color: C.dim }}>não entra no desvio</span>
        </div>
    );
}

/**
 * One-line production summary used when the card has no room for the bars.
 * Same rules as the bars: scanned = scanner postings, "sem leitura" when none,
 * deviation chip unchanged (provisional), waste kg separate and never summed.
 */
function ProductionSummaryLine({ lot, withWaste }: { lot: Viana3Lot; withWaste: boolean }) {
    const scanned = lot.scanCount > 0 ? fmtInt(lot.scannedPreforms) : 'sem leitura';
    return (
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px 14px', flexWrap: 'wrap', fontSize: '13px', color: C.textSecondary, fontVariantNumeric: 'tabular-nums' }}>
            <span><span style={{ color: C.plan }}>■</span> Plan. <b style={{ color: C.text }}>{lot.plannedBottles ? fmtInt(lot.plannedBottles) : '—'}</b></span>
            <span><span style={{ color: C.bottle }}>■</span> Rep. <b style={{ color: C.text }}>{fmtInt(lot.reportedBottles)}</b></span>
            <span><span style={{ color: C.preform }}>■</span> Lidas <b style={{ color: C.text }}>{scanned}</b></span>
            <DeviationChip lot={lot} />
            {withWaste && (
                <span style={{ color: C.muted }}>
                    Waste garr. {lot.waste.rejectedBottlesKg !== null ? fmtKg(lot.waste.rejectedBottlesKg) : 'sem registo'} · pré-f. {lot.waste.rejectedPreformsKg !== null ? fmtKg(lot.waste.rejectedPreformsKg) : 'sem registo'}
                </span>
            )}
        </div>
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Previous lots (compact)
// ═══════════════════════════════════════════════════════════════════════

export function PreviousLotRow({ lot }: { lot: Viana3Lot }) {
    const scanned = lot.scanCount > 0 ? fmtInt(lot.scannedPreforms) : 'sem leitura';
    return (
        <div style={{
            display: 'grid', gridTemplateColumns: '72px 1fr auto', gap: '10px', alignItems: 'center',
            padding: '6px 10px', borderRadius: '8px', background: 'rgba(15,23,42,0.45)', border: `1px solid ${C.border}`,
        }}>
            <div>
                <div style={{ fontSize: '14px', fontWeight: 800, color: C.text }}>#{lot.lotId}</div>
                <div style={{ fontSize: '11px', color: C.dim }}>{parseLocal(lot.planFrom)?.d ?? ''}{lot.lotStatus === 'CLOSED' ? ' · fechado' : lot.lotStatus === 'OPEN' ? ' · aberto' : ''}</div>
            </div>
            <div style={{ minWidth: 0 }}>
                <div style={{ fontSize: '12px', color: C.textSecondary, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={lot.bottleArticle ?? ''}>
                    {shortArticle(lot.bottleArticle, 40)}
                </div>
                <div style={{ fontSize: '12px', color: C.muted, display: 'flex', gap: '10px', flexWrap: 'wrap', fontVariantNumeric: 'tabular-nums' }}>
                    <span><span style={{ color: C.bottle }}>■</span> Rep. {fmtInt(lot.reportedBottles)}</span>
                    <span><span style={{ color: C.preform }}>■</span> Lidas {scanned}</span>
                    <span>Waste garr. {fmtKg(lot.waste.rejectedBottlesKg)} · pré-f. {fmtKg(lot.waste.rejectedPreformsKg)}</span>
                </div>
            </div>
            <DeviationChip lot={lot} />
        </div>
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Machine panel (self-fitting: never clips)
// ═══════════════════════════════════════════════════════════════════════

/**
 * Fit levels, applied in order until the content fits the card height:
 *   0 — full content
 *   1 — previous orders hidden
 *   2 — illustration hidden (stock figures stay as text)
 *   3 — waste tiles replaced by a single line
 *   4 — comparison bars replaced by a one-line production summary (planned, reported, scanned, deviation)
 *   5 — essential only: stock positions/retention lines hidden, waste kg folded into the summary line
 * If the content still does not fit at level 5 the text is shrunk (CSS zoom) down to
 * MIN_SHRINK; past that the card is reported as clipped so the editor can flag it.
 */
export type FitLevel = 0 | 1 | 2 | 3 | 4 | 5;
const MAX_FIT_LEVEL: FitLevel = 5;
const MIN_SHRINK = 0.7;

export const FIT_LEVEL_LABEL: Record<FitLevel, string> = {
    0: 'conteúdo completo',
    1: 'ordens anteriores ocultas',
    2: 'ordens anteriores e ilustração ocultas',
    3: 'Waste numa linha',
    4: 'barras de comparação resumidas numa linha',
    5: 'conteúdo essencial (números-chave numa linha)',
};

function baseFitLevel(size: Viana3CardSize): FitLevel {
    return size === 'COMPACT' ? 2 : 0;
}

export const MachinePanel = memo(function MachinePanel({
    panel, retentionId, size, fitKey, onFit,
}: {
    panel: Viana3MachinePanel;
    retentionId: number;
    size: Viana3CardSize;
    /** Changes whenever the card box can change (columns, rows, size): restarts the fit. */
    fitKey: string;
    /** forced = still clipped after every level and the minimum shrink; shrink = text zoom factor applied (1 = none). */
    onFit?: (machineId: number, level: FitLevel, forced: boolean, shrink: number) => void;
}) {
    const ref = useRef<HTMLDivElement>(null);
    const base = baseFitLevel(size);
    const [level, setLevel] = useState<FitLevel>(base);

    // Restart from the base level whenever the box or the content may have changed
    // (derived-state-from-props pattern: adjusted during render, no extra commit, no effect).
    const [shrink, setShrink] = useState(1);
    const [seen, setSeen] = useState<{ base: FitLevel; fitKey: string; panel: Viana3MachinePanel }>({ base, fitKey, panel });
    if (seen.base !== base || seen.fitKey !== fitKey || seen.panel !== panel) {
        setSeen({ base, fitKey, panel });
        setLevel(base);
        setShrink(1);
    }

    // Last result reported to the parent — the parent is told only when it changes.
    const reportedRef = useRef<{ level: FitLevel; forced: boolean; shrink: number } | null>(null);
    const onFitRef = useRef(onFit);
    onFitRef.current = onFit;

    // Measure before paint; raise the level until the content fits (no visible flicker:
    // each setState inside a layout effect re-renders synchronously before the frame is shown).
    useLayoutEffect(() => {
        const el = ref.current;
        if (!el) return;
        // client/scroll heights are in the element's own (zoomed) coordinate space, so after a
        // shrink of z the available height grows by 1/z while the content height is unchanged.
        const overflow = el.scrollHeight > el.clientHeight + 1;
        if (overflow && level < MAX_FIT_LEVEL) {
            setLevel((level + 1) as FitLevel);
            return;
        }
        if (overflow && shrink === 1) {
            setShrink(Math.max(MIN_SHRINK, Math.min(1, (el.clientHeight / el.scrollHeight) - 0.01)));
            return;
        }
        const last = reportedRef.current;
        if (last && last.level === level && last.forced === overflow && last.shrink === shrink) return;
        reportedRef.current = { level, forced: overflow, shrink };
        onFitRef.current?.(panel.machineId, level, overflow, shrink);
    });

    const cur = panel.current;
    const running = panel.statusCode === 'RUNNING';
    const st = STATUS_STYLE[panel.statusCode] ?? STATUS_STYLE.IDLE;
    const stock = panel.preformStock;
    const isProduction = cur?.lotKind === 'Production';

    const showPrevious = level < 1;
    const showIllustration = level < 2;
    const showWasteTiles = level < 3;
    const showBars = level < 4;
    const showStockDetail = level < 5;
    const zoom = (size === 'LARGE' ? 1.25 : 1) * shrink;

    return (
        <div ref={ref} className="v3-panel" style={{
            display: 'flex', flexDirection: 'column', gap: '10px',
            padding: '14px 16px', borderRadius: '14px',
            background: C.surface, border: running ? '1px solid rgba(34,197,94,0.3)' : `1px solid ${C.border}`,
            minHeight: 0, minWidth: 0, overflow: 'hidden', height: '100%', boxSizing: 'border-box',
            zoom,
        } as React.CSSProperties}>
            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '10px' }}>
                <div style={{ minWidth: 0 }}>
                    <div style={{ fontSize: '24px', fontWeight: 800, color: C.text, letterSpacing: '-0.3px', lineHeight: 1.1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {panel.machineName}
                    </div>
                    <div style={{ fontSize: '14px', color: C.textSecondary, marginTop: '4px', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}
                        title={cur?.bottleArticle ?? ''}>
                        {cur ? <>Lote <b style={{ color: C.text }}>#{cur.lotId}</b> · {shortArticle(cur.bottleArticle, 44)}</> : 'Sem lote planeado agora'}
                    </div>
                    {cur && (
                        <div style={{ fontSize: '12px', color: C.dim, marginTop: '2px', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                            {fmtDateTime(cur.planFrom)} → {fmtDateTime(cur.planTo)}
                            {cur.lotKind !== 'Production' && <> · {cur.lotKindLabel}</>}
                        </div>
                    )}
                </div>
                <span style={{
                    fontSize: '13px', fontWeight: 800, padding: '5px 12px', borderRadius: '10px',
                    color: st.color, background: st.bg, border: `1px solid ${st.color}55`, whiteSpace: 'nowrap', letterSpacing: '0.3px', flexShrink: 0,
                }}>
                    {panel.statusLabel}
                </span>
            </div>

            {/* Illustration + stock */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '14px', minWidth: 0 }}>
                {showIllustration && (
                    <>
                        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '2px', flexShrink: 0 }}>
                            <PalletStack stock={stock} running={running} />
                            <div style={{ fontSize: '11px', color: C.dim }}>paletes · Armazém 3</div>
                        </div>
                        <div style={{ flexShrink: 0 }}>
                            <ProductionLine running={running} bottleColor={C.bottle} />
                        </div>
                    </>
                )}
                <div style={{ flex: 1, minWidth: 0 }}>
                    {cur?.preformArticle ? (
                        <>
                            <div style={{ fontSize: '12px', color: C.muted, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={cur.preformArticle}>
                                <span style={{ color: C.preform }}>■</span> Pré-forma: {shortArticle(cur.preformArticle, 30)}
                            </div>
                            {stock ? (
                                <>
                                    <div style={{ fontSize: '26px', fontWeight: 800, color: C.text, lineHeight: 1.1, fontVariantNumeric: 'tabular-nums' }}>
                                        {fmtInt(stock.availableQty)} <span style={{ fontSize: '13px', fontWeight: 600, color: C.muted }}>un. disponíveis</span>
                                    </div>
                                    {showStockDetail && (
                                        <>
                                            <div style={{ fontSize: '13px', color: C.textSecondary }}>
                                                {fmtInt(stock.availablePositions)} paletes/posições disponíveis
                                            </div>
                                            <div style={{ fontSize: '12px', color: C.warn, marginTop: '2px' }}>
                                                Retenção {retentionId} / bloqueado: {fmtInt(stock.blockedQty)} un. · {fmtInt(stock.blockedPositions)} pal. (fora do disponível)
                                            </div>
                                        </>
                                    )}
                                </>
                            ) : (
                                <div style={{ fontSize: '15px', fontWeight: 700, color: C.warn, marginTop: '4px' }}>
                                    Sem stock registado no Armazém 3
                                </div>
                            )}
                        </>
                    ) : (
                        <div style={{ fontSize: '13px', color: C.dim }}>Sem pré-forma associada ao lote atual</div>
                    )}
                </div>
            </div>

            {/* Comparison */}
            {cur && isProduction && !showBars ? (
                <ProductionSummaryLine lot={cur} withWaste={!showStockDetail} />
            ) : cur && isProduction ? (
                <div>
                    <ComparisonBars lot={cur} />
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '6px', gap: '8px', flexWrap: 'wrap' }}>
                        <DeviationChip lot={cur} large />
                        <div style={{ fontSize: '11px', color: C.dim, textAlign: 'right' }}>
                            Reporte {cur.reportLastUpdated ? fmtDateTime(cur.reportLastUpdated) : '—'} · Scanner {cur.lastScan ? fmtDateTime(cur.lastScan) : 'sem leitura'}
                            {cur.otherPreformScanCount > 0 && <> · {fmtInt(cur.otherPreformsScanned)} un. de outra pré-forma lidas</>}
                        </div>
                    </div>
                </div>
            ) : (
                <div style={{ padding: '10px 12px', borderRadius: '8px', background: 'rgba(15,23,42,0.45)', color: C.muted, fontSize: '13px' }}>
                    {cur ? `Lote ${cur.lotKindLabel.toLowerCase()} — sem comparação de produção.` : 'Sem produção em curso.'}
                </div>
            )}

            {/* Waste (folded into the summary line at the essential level) */}
            {cur && isProduction && showStockDetail && (
                showWasteTiles ? (
                    <div>
                        <div style={{ fontSize: '12px', color: C.muted, marginBottom: '4px', display: 'flex', justifyContent: 'space-between', gap: '8px' }}>
                            <span style={{ fontWeight: 700, letterSpacing: '0.4px', whiteSpace: 'nowrap' }}>WASTE (AlplaLABEL)</span>
                            <span style={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>kg registados · unidades = estimativa · não entra no desvio</span>
                        </div>
                        <div style={{ display: 'flex', gap: '8px' }}>
                            <WasteTile label="Garrafas rejeitadas (art. 56)" kg={cur.waste.rejectedBottlesKg} units={cur.waste.rejectedBottlesUnitsEstimate} color={C.bottle} />
                            <WasteTile label="Pré-formas rejeitadas (art. 55)" kg={cur.waste.rejectedPreformsKg} units={cur.waste.rejectedPreformsUnitsEstimate} color={C.preform} />
                        </div>
                    </div>
                ) : (
                    <WasteLine lot={cur} />
                )
            )}

            {/* Previous lots */}
            {showPrevious && (
                <div style={{ marginTop: 'auto' }}>
                    <div style={{ fontSize: '12px', color: C.muted, marginBottom: '4px', fontWeight: 700, letterSpacing: '0.4px' }}>
                        ORDENS ANTERIORES
                    </div>
                    {panel.previous.length === 0 ? (
                        <div style={{ fontSize: '12px', color: C.dim }}>Sem ordens de produção anteriores.</div>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                            {panel.previous.map(p => <PreviousLotRow key={p.lotId} lot={p} />)}
                        </div>
                    )}
                </div>
            )}
        </div>
    );
});

// ═══════════════════════════════════════════════════════════════════════
// Stage: fixed 1920×1080 composition scaled to the available box
// ═══════════════════════════════════════════════════════════════════════

/**
 * Renders its children on a fixed 1920×1080 stage and scales that stage to fit
 * the parent box (uniform scale, centred, letter-boxed). The TV and the editor
 * preview therefore show the same composition; a different TV resolution only
 * changes the scale, never the placement or the fitting.
 */
export function Viana3Stage({ children, scale: forcedScale, background }: { children: React.ReactNode; scale?: number; background?: string }) {
    const boxRef = useRef<HTMLDivElement>(null);
    const [box, setBox] = useState<{ w: number; h: number }>({ w: STAGE_WIDTH, h: STAGE_HEIGHT });

    useEffect(() => {
        const el = boxRef.current;
        if (!el) return;
        const update = () => {
            const w = el.clientWidth, h = el.clientHeight;
            setBox(prev => (prev.w === w && prev.h === h ? prev : { w, h }));
        };
        update();
        const ro = new ResizeObserver(update);
        ro.observe(el);
        return () => ro.disconnect();
    }, []);

    const scale = forcedScale ?? Math.max(0.05, Math.min(box.w / STAGE_WIDTH, box.h / STAGE_HEIGHT));
    const left = Math.max(0, (box.w - STAGE_WIDTH * scale) / 2);
    const top = Math.max(0, (box.h - STAGE_HEIGHT * scale) / 2);

    return (
        <div ref={boxRef} style={{ position: 'relative', width: '100%', height: '100%', overflow: 'hidden', background }}>
            <div style={{
                position: 'absolute', left, top, width: STAGE_WIDTH, height: STAGE_HEIGHT,
                transform: `scale(${scale})`, transformOrigin: 'top left',
            }}>
                {children}
            </div>
        </div>
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Board: the full 1920×1080 screen (header, banners, grid, footer) — no controls
// ═══════════════════════════════════════════════════════════════════════

export interface Viana3BoardProps {
    data: Viana3MonitorResponse;
    layout: ResolvedLayout;
    freshness: Freshness;
    countdown: number;
    refreshSeconds: number;
    isFetching: boolean;
    /** Portal unreachable while older data is displayed. */
    connectionLost: boolean;
    connectionMessage?: string | null;
    isTest: boolean;
    /** Diagnostics link in the footer (TV without fullscreen only). */
    showDiagnosticsLink?: boolean;
    /** Name of the screen shown next to the title (named screens only). */
    screenName?: string | null;
    /** Card designs (template + machine overrides); a machine without one renders the built-in panel. */
    designs?: Viana3CardDesignBundle | null;
    onCardFit?: (machineId: number, level: FitLevel, forced: boolean, shrink: number) => void;
}

export const Viana3Board = memo(function Viana3Board({
    data, layout, freshness, countdown, refreshSeconds, isFetching, connectionLost, connectionMessage, isTest, showDiagnosticsLink, screenName, designs, onCardFit,
}: Viana3BoardProps) {
    const src = data.source;
    const cards = useMemo(() => visibleCards(layout), [layout]);
    const { rows, placed } = useMemo(() => placeCards(cards, layout.columns), [cards, layout.columns]);
    const fitKey = `${layout.columns}x${rows}`;

    const sourceChip = src.status === 'LIVE'
        ? { text: 'AO VIVO', color: C.good }
        : src.status === 'CACHED'
            ? { text: 'CACHE', color: C.info }
            : { text: 'DESATUALIZADO', color: C.warn };

    return (
        <div id="viana3-production-monitor" style={{
            width: STAGE_WIDTH, height: STAGE_HEIGHT, boxSizing: 'border-box',
            background: 'linear-gradient(160deg, #0a0f1e 0%, #111827 40%, #0a0f1e 100%)',
            color: '#e2e8f0',
            fontFamily: "'Inter', 'Segoe UI', -apple-system, sans-serif",
            display: 'flex', flexDirection: 'column', overflow: 'hidden',
        }}>
            {isTest && (
                <div style={{
                    height: '24px', flexShrink: 0, background: 'linear-gradient(90deg, #d97706, #f59e0b, #d97706)',
                    color: '#451a03', display: 'flex', alignItems: 'center', justifyContent: 'center',
                    fontSize: '11px', fontWeight: 800, letterSpacing: '0.1em', textTransform: 'uppercase' as const,
                }}>
                    AMBIENTE DE TESTE
                </div>
            )}

            {/* Top bar */}
            <div style={{
                display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0,
                padding: '14px 28px 12px', borderBottom: `1px solid ${C.border}`,
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
                    <div style={{
                        width: '14px', height: '14px', borderRadius: '50%',
                        backgroundColor: FRESH_COLOR[freshness], boxShadow: `0 0 10px ${FRESH_COLOR[freshness]}80`,
                    }} />
                    <h1 style={{ margin: 0, fontSize: '26px', fontWeight: 800, color: '#f8fafc', letterSpacing: '0.5px' }}>
                        Monitor de Produção — {data.plantName}
                    </h1>
                    {screenName && (
                        <span style={{
                            fontSize: '14px', fontWeight: 700, padding: '3px 10px', borderRadius: '8px',
                            color: C.textSecondary, background: 'rgba(148,163,184,0.12)', border: `1px solid ${C.border}`, whiteSpace: 'nowrap',
                        }}>
                            {screenName}
                        </span>
                    )}
                    {isFetching && <span style={{ fontSize: '14px', color: C.info }}>⟳</span>}
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '18px', fontSize: '15px', color: C.muted }}>
                    <span style={{
                        fontSize: '12px', fontWeight: 800, padding: '3px 10px', borderRadius: '8px', letterSpacing: '0.5px',
                        color: sourceChip.color, background: `${sourceChip.color}1f`, border: `1px solid ${sourceChip.color}55`,
                    }}>
                        {sourceChip.text}
                    </span>
                    <span title={src.timeZoneLabel}>🕐 Atualizado {fmtTime(src.fetchedAtLocal)} (hora de Angola)</span>
                    <span style={{ color: countdown <= 10 ? C.warn : C.dim }}>⏱ {countdown}s</span>
                </div>
            </div>

            <div style={{ height: '3px', flexShrink: 0, background: '#0f172a' }}>
                <div style={{
                    height: '100%', backgroundColor: C.bottle,
                    width: `${Math.max(0, Math.min(100, (countdown / Math.max(1, refreshSeconds)) * 100))}%`,
                    transition: 'width 1s linear', borderRadius: '0 2px 2px 0',
                }} />
            </div>

            {/* Banners */}
            {src.status === 'STALE' && (
                <div style={{
                    flexShrink: 0, padding: '8px 28px', background: 'rgba(245,158,11,0.10)', borderBottom: '1px solid rgba(245,158,11,0.2)',
                    color: '#fcd34d', fontSize: '15px', fontWeight: 600,
                }}>
                    ⚠ AlplaPROD indisponível — a mostrar os últimos dados obtidos às {fmtTime(src.fetchedAtLocal)} (hora de Angola)
                    {src.unavailableReason ? ` · ${src.unavailableReason}` : ''}
                </div>
            )}
            {connectionLost && (
                <div style={{
                    flexShrink: 0, padding: '8px 28px', background: 'rgba(239,68,68,0.12)', borderBottom: '1px solid rgba(239,68,68,0.2)',
                    color: '#fca5a5', fontSize: '15px', fontWeight: 600,
                }}>
                    ⚠ Sem ligação ao Portal — dados anteriores exibidos{connectionMessage ? ` · ${connectionMessage}` : ''}
                </div>
            )}
            {!connectionLost && src.status !== 'STALE' && freshness === 'stale' && (
                <div style={{
                    flexShrink: 0, padding: '8px 28px', background: 'rgba(245,158,11,0.08)', borderBottom: '1px solid rgba(245,158,11,0.15)',
                    color: '#fcd34d', fontSize: '14px',
                }}>
                    ⚠ Dados podem estar desatualizados
                </div>
            )}

            {/* Machine grid (explicit placement shared with the editor preview) */}
            <div style={{
                flex: 1, minHeight: 0, display: 'grid',
                gridTemplateColumns: `repeat(${layout.columns}, minmax(0, 1fr))`,
                gridTemplateRows: `repeat(${rows}, minmax(0, 1fr))`,
                gap: '14px', padding: '14px 28px', overflow: 'hidden',
            }}>
                {cards.length === 0 ? (
                    <div style={{ gridColumn: '1 / -1', gridRow: '1 / -1', display: 'flex', alignItems: 'center', justifyContent: 'center', color: C.muted, fontSize: '22px', textAlign: 'center', padding: '20px' }}>
                        {data.machines.length === 0
                            ? 'Nenhuma máquina com planeamento recente.'
                            : layout.cards.length === 0
                                ? 'Este ecrã ainda não tem máquinas selecionadas.'
                                : 'Todos os cartões deste ecrã estão ocultos.'}
                    </div>
                ) : placed.map(p => {
                    const design = resolveDesign(designs, p.item.machineId);
                    return (
                        <div key={p.item.machineId} style={{
                            gridColumn: `${p.col + 1} / span ${p.colSpan}`,
                            gridRow: `${p.row + 1} / span ${p.rowSpan}`,
                            minHeight: 0, minWidth: 0,
                        }}>
                            {design ? (
                                <Viana3DesignedCard panel={p.item.panel!} design={design} retentionId={data.scope.retentionLocationId} />
                            ) : (
                                <MachinePanel
                                    panel={p.item.panel!}
                                    retentionId={data.scope.retentionLocationId}
                                    size={p.item.size}
                                    fitKey={`${fitKey}:${p.colSpan}x${p.rowSpan}`}
                                    onFit={onCardFit}
                                />
                            )}
                        </div>
                    );
                })}
            </div>

            {/* Footer */}
            <div style={{
                flexShrink: 0, padding: '8px 28px 10px', borderTop: `1px solid ${C.border}`, display: 'flex',
                justifyContent: 'space-between', alignItems: 'center', gap: '16px', fontSize: '12px', color: C.dim,
            }}>
                <div style={{ display: 'flex', gap: '18px', flexWrap: 'wrap' }}>
                    <span><span style={{ color: C.bottle }}>■</span> Garrafas reportadas (AlplaREPORT)</span>
                    <span><span style={{ color: C.preform }}>■</span> Pré-formas lidas no scanner (não é consumo físico)</span>
                    <span><span style={{ color: C.plan }}>■</span> Planeado (referência)</span>
                    <span>Desvio provisório: 1 pré-forma = 1 garrafa não confirmado · Waste não incluído</span>
                </div>
                <div style={{ whiteSpace: 'nowrap' }}>
                    Fonte: AlplaPROD_aovia3 (só leitura) · {src.timeZoneLabel}
                    {showDiagnosticsLink && <> · <a href="/operations/viana3-monitor/diagnostics" style={{ color: C.info }}>Diagnóstico</a></>}
                </div>
            </div>
        </div>
    );
});
