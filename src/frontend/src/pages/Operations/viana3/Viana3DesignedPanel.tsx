/**
 * Viana 3 Production Monitor — designed machine card renderer.
 *
 * Renders a card from a design document (600×440 design units) with the live machine
 * data. Used by the TV, by the layout-editor preview and by the card-design editor
 * (which overlays its own selection/drag layer on top). The canvas is scaled uniformly
 * into the card box, so a designed card never clips.
 *
 * Production rules are enforced here regardless of the design:
 *   • "Lidas no scanner" = scanner postings; no postings → "Sem leitura de scanner", never 0 / −100 %;
 *   • the deviation chip always carries its provisional label;
 *   • Waste shows recorded kg; units are labelled estimate; the two figures are never combined;
 *   • blocked/retention stock is shown apart and never enters the available figure;
 *   • number formats change presentation only.
 */

import React, { memo, useEffect, useRef, useState } from 'react';
import type { Viana3MachinePanel, Viana3Lot } from '../../../types/operations.types';
import type { Viana3CardDesignDocument, Viana3CardDesignItem } from '../../../types/viana3CardDesign.types';
import { C, STATUS_STYLE, fmtDateTime, fmtKg, shortArticle, PalletStack, ProductionLine, DeviationChip, PreviousLotRow } from './Viana3Board';
import { DESIGN_W, DESIGN_H, canvasSize, formatDesignNumber, isFreeImageKey, isFreeTextKey, effectiveFontSize } from './viana3CardDesign';
import { viana3DesignAssetUrl } from '../../../lib/viana3CardDesignApi';

const ALIGN: Record<string, React.CSSProperties['textAlign']> = { LEFT: 'left', CENTER: 'center', RIGHT: 'right' };

function num(lot: Viana3Lot | null, v: number | null | undefined, it: Viana3CardDesignItem): string {
    if (!lot) return '—';
    return formatDesignNumber(v, it.props.numberFormat);
}

/** Scales fixed-size SVG content (w0×h0) into the item box, centred. */
function FitSvg({ w0, h0, w, h, children }: { w0: number; h0: number; w: number; h: number; children: React.ReactNode }) {
    const s = Math.min(w / w0, h / h0);
    return (
        <div style={{ width: '100%', height: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', overflow: 'hidden' }}>
            <div style={{ width: w0, height: h0, transform: `scale(${s})`, transformOrigin: 'center center', flexShrink: 0 }}>{children}</div>
        </div>
    );
}

function BarRow({ it, label, value, color, planned, scale, empty }: {
    it: Viana3CardDesignItem; label: string; value: number | null; color: string; planned: number; scale: number; empty: string;
}) {
    const fs = effectiveFontSize(it);
    const pct = (v: number) => `${Math.max(0, Math.min(100, (v / Math.max(scale, 1)) * 100))}%`;
    return (
        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(90px, 26%) 1fr auto', columnGap: 8, alignItems: 'center', width: '100%', height: '100%' }}>
            <div style={{ fontSize: fs, color: it.style.color ?? C.textSecondary, lineHeight: 1.1, overflow: 'hidden', whiteSpace: 'nowrap', textOverflow: 'ellipsis' }}>{label}</div>
            <div className="v3-bar-track" style={{ height: '100%', maxHeight: 22 }}>
                {value !== null ? (
                    <div className="v3-bar-fill" style={{ width: pct(value), background: it.style.accent ?? color }} />
                ) : (
                    <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', paddingLeft: 8, fontSize: Math.max(8, fs - 1), color: C.muted, fontStyle: 'italic' }}>{empty}</div>
                )}
                {planned > 0 && <div style={{ position: 'absolute', top: 0, bottom: 0, left: pct(planned), width: 2, background: C.text, opacity: 0.55 }} />}
            </div>
            <div style={{ fontSize: fs + 5, fontWeight: it.style.fontWeight ?? 800, color: it.style.color ?? C.text, textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 60 }}>
                {value !== null ? formatDesignNumber(value, it.props.numberFormat) : ''}
            </div>
        </div>
    );
}

function WasteTileItem({ it, label, kg, units, color }: { it: Viana3CardDesignItem; label: string; kg: number | null; units: number | null; color: string }) {
    const fs = effectiveFontSize(it);
    const showSecondary = it.props.showSecondary !== false;
    return (
        <div style={{ width: '100%', height: '100%', padding: '6px 10px', boxSizing: 'border-box', borderRadius: it.style.radius ?? 10, background: it.style.background ?? 'rgba(15,23,42,0.5)', border: `1px solid ${it.style.borderColor ?? C.border}`, overflow: 'hidden', display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
            <div style={{ fontSize: fs, color: it.style.color ?? C.muted, display: 'flex', alignItems: 'center', gap: 6, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                <span style={{ width: 8, height: 8, borderRadius: 2, background: it.style.accent ?? color, display: 'inline-block', flexShrink: 0 }} />
                {label}
            </div>
            <div style={{ fontSize: fs + 8, fontWeight: it.style.fontWeight ?? 800, color: it.style.color ?? C.text, fontVariantNumeric: 'tabular-nums', lineHeight: 1.2 }}>
                {kg !== null ? fmtKg(kg) : '—'}
            </div>
            {showSecondary && (
                <div style={{ fontSize: fs, color: C.dim, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                    {units !== null ? `≈ ${formatDesignNumber(units, it.props.numberFormat)} un. (estimativa)` : kg !== null ? 'estimativa indisponível (peso desconhecido)' : 'sem registo'}
                </div>
            )}
        </div>
    );
}

function TextItem({ it, children, defaultColor, defaultWeight, nowrap = true }: { it: Viana3CardDesignItem; children: React.ReactNode; defaultColor: string; defaultWeight?: number; nowrap?: boolean }) {
    return (
        <div style={{
            width: '100%', height: '100%', display: 'flex', alignItems: 'center', overflow: 'hidden',
            justifyContent: it.style.align === 'CENTER' ? 'center' : it.style.align === 'RIGHT' ? 'flex-end' : 'flex-start',
            fontSize: effectiveFontSize(it), fontWeight: it.style.fontWeight ?? defaultWeight ?? 400, color: it.style.color ?? defaultColor,
            textAlign: ALIGN[it.style.align ?? 'LEFT'], lineHeight: 1.15,
        }}>
            <span style={{ whiteSpace: nowrap ? 'nowrap' : 'pre-wrap', overflow: 'hidden', textOverflow: 'ellipsis', width: '100%' }}>{children}</span>
        </div>
    );
}

function ImageItem({ it }: { it: Viana3CardDesignItem }) {
    if (!it.props.imageAssetId) return <div style={{ width: '100%', height: '100%', border: `1px dashed ${C.dim}`, borderRadius: 6, display: 'flex', alignItems: 'center', justifyContent: 'center', color: C.dim, fontSize: 11 }}>sem imagem</div>;
    return <img src={viana3DesignAssetUrl(it.props.imageAssetId)} alt="" draggable={false} style={{ width: '100%', height: '100%', objectFit: it.props.imageFit === 'COVER' ? 'cover' : 'contain', display: 'block', borderRadius: it.style.radius ?? 0 }} />;
}

/** Renders one item's content (the positioned box is handled by the canvas). */
function ItemContent({ it, panel, retentionId, barScale }: { it: Viana3CardDesignItem; panel: Viana3MachinePanel; retentionId: number; barScale: number }) {
    const cur = panel.current;
    const running = panel.statusCode === 'RUNNING';
    const isProduction = cur?.lotKind === 'Production';
    const stock = panel.preformStock;
    const label = it.props.label;
    const fs = effectiveFontSize(it);

    if (isFreeTextKey(it.key)) return <TextItem it={it} defaultColor={C.text} defaultWeight={600} nowrap={false}>{it.props.text ?? ''}</TextItem>;
    if (isFreeImageKey(it.key)) return <ImageItem it={it} />;

    switch (it.key) {
        case 'machineName':
            return <TextItem it={it} defaultColor={C.text} defaultWeight={800}>{label ? `${label} ${panel.machineName}` : panel.machineName}</TextItem>;
        case 'status': {
            const st = STATUS_STYLE[panel.statusCode] ?? STATUS_STYLE.IDLE;
            return (
                <div style={{ width: '100%', height: '100%', display: 'flex', alignItems: 'center', justifyContent: it.style.align === 'LEFT' ? 'flex-start' : it.style.align === 'CENTER' ? 'center' : 'flex-end' }}>
                    <span style={{ fontSize: fs, fontWeight: it.style.fontWeight ?? 800, padding: '4px 12px', borderRadius: it.style.radius ?? 10, color: it.style.color ?? st.color, background: it.style.background ?? st.bg, border: `1px solid ${it.style.borderColor ?? `${st.color}55`}`, whiteSpace: 'nowrap', letterSpacing: '0.3px' }}>
                        {panel.statusLabel}
                    </span>
                </div>
            );
        }
        case 'lotLine':
            return <TextItem it={it} defaultColor={C.textSecondary}>{cur ? <>{label ?? 'Lote'} <b style={{ color: it.style.color ?? C.text }}>#{cur.lotId}</b> · {shortArticle(cur.bottleArticle, 44)}</> : 'Sem lote planeado agora'}</TextItem>;
        case 'planWindow':
            return <TextItem it={it} defaultColor={C.dim}>{cur ? <>{label ? `${label} ` : ''}{fmtDateTime(cur.planFrom)} → {fmtDateTime(cur.planTo)}{cur.lotKind !== 'Production' && <> · {cur.lotKindLabel}</>}</> : ''}</TextItem>;
        case 'illustration':
            if (it.props.imageAssetId) return <ImageItem it={it} />;
            return <FitSvg w0={300} h0={96} w={it.w} h={it.h}><ProductionLine running={running} bottleColor={it.style.accent ?? C.bottle} /></FitSvg>;
        case 'palletStack':
            return (
                <div style={{ width: '100%', height: '100%', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 2 }}>
                    <div style={{ flex: 1, minHeight: 0, width: '100%' }}><FitSvg w0={108} h0={71} w={it.w} h={it.props.showSecondary === false ? it.h : it.h - 14}><PalletStack stock={stock} running={running} /></FitSvg></div>
                    {it.props.showSecondary !== false && <div style={{ fontSize: Math.max(8, fs - 1), color: it.style.color ?? C.dim, whiteSpace: 'nowrap' }}>{label ?? 'paletes · Armazém 3'}</div>}
                </div>
            );
        case 'preformLabel':
            return <TextItem it={it} defaultColor={C.muted}>{cur?.preformArticle ? <><span style={{ color: it.style.accent ?? C.preform }}>■</span> {label ?? 'Pré-forma:'} {shortArticle(cur.preformArticle, 30)}</> : 'Sem pré-forma associada ao lote atual'}</TextItem>;
        case 'stockAvailable':
            if (!cur?.preformArticle) return <TextItem it={it} defaultColor={C.dim}>—</TextItem>;
            if (!stock) return <TextItem it={it} defaultColor={C.warn} defaultWeight={700}>Sem stock registado no Armazém 3</TextItem>;
            return (
                <div style={{ width: '100%', height: '100%', display: 'flex', alignItems: 'baseline', gap: 6, overflow: 'hidden', justifyContent: it.style.align === 'CENTER' ? 'center' : it.style.align === 'RIGHT' ? 'flex-end' : 'flex-start' }}>
                    <span style={{ fontSize: fs, fontWeight: it.style.fontWeight ?? 800, color: it.style.color ?? C.text, lineHeight: 1.1, fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{num(cur, stock.availableQty, it)}</span>
                    {it.props.showSecondary !== false && <span style={{ fontSize: Math.max(8, fs / 2), fontWeight: 600, color: C.muted, whiteSpace: 'nowrap' }}>{it.props.unitSuffix ?? label ?? 'un. disponíveis'}</span>}
                </div>
            );
        case 'stockPositions':
            return <TextItem it={it} defaultColor={C.textSecondary}>{stock ? `${num(cur, stock.availablePositions, it)} ${label ?? 'paletes/posições disponíveis'}` : ''}</TextItem>;
        case 'stockBlocked':
            return <TextItem it={it} defaultColor={C.warn}>{stock ? `${label ?? `Retenção ${retentionId} / bloqueado:`} ${num(cur, stock.blockedQty, it)} un. · ${num(cur, stock.blockedPositions, it)} pal. (fora do disponível)` : ''}</TextItem>;
        case 'planned':
        case 'reported':
        case 'scanned': {
            // Non-production lot (maintenance, no order, placeholder): the explanation is shown once, in the
            // planned slot; the other production items stay empty instead of repeating it.
            if (!cur || !isProduction) return it.key === 'planned'
                ? <TextItem it={it} defaultColor={C.muted}>{cur ? `Lote ${cur.lotKindLabel.toLowerCase()} — sem comparação de produção.` : 'Sem produção em curso.'}</TextItem>
                : null;
            const planned = cur.plannedBottles ?? 0;
            if (it.key === 'planned') return <BarRow it={it} label={label ?? 'Planeado (garrafas)'} value={planned > 0 ? planned : null} color={C.plan} planned={planned} scale={barScale} empty="Sem quantidade planeada" />;
            if (it.key === 'reported') return <BarRow it={it} label={label ?? 'Reportado (garrafas)'} value={cur.reportedBottles} color={C.bottle} planned={planned} scale={barScale} empty="Sem produção reportada" />;
            return <BarRow it={it} label={label ?? 'Lidas no scanner (pré-formas)'} value={cur.scanCount > 0 ? cur.scannedPreforms : null} color={C.preform} planned={planned} scale={barScale} empty="Sem leitura de scanner" />;
        }
        case 'deviation':
            if (!cur || !isProduction) return null;
            return (
                <div style={{ width: '100%', height: '100%', display: 'flex', alignItems: 'center', justifyContent: it.style.align === 'CENTER' ? 'center' : it.style.align === 'RIGHT' ? 'flex-end' : 'flex-start', fontSize: fs, color: it.style.color ?? undefined }}>
                    <DeviationChip lot={cur} large />
                </div>
            );
        case 'wasteBottles':
            if (!cur || !isProduction) return null;
            return <WasteTileItem it={it} label={label ?? 'Garrafas rejeitadas (art. 56)'} kg={cur.waste.rejectedBottlesKg} units={cur.waste.rejectedBottlesUnitsEstimate} color={C.bottle} />;
        case 'wastePreforms':
            if (!cur || !isProduction) return null;
            return <WasteTileItem it={it} label={label ?? 'Pré-formas rejeitadas (art. 55)'} kg={cur.waste.rejectedPreformsKg} units={cur.waste.rejectedPreformsUnitsEstimate} color={C.preform} />;
        case 'previousOrders':
            return (
                <div style={{ width: '100%', height: '100%', display: 'flex', flexDirection: 'column', gap: 3, overflow: 'hidden' }}>
                    <div style={{ fontSize: fs, color: it.style.color ?? C.muted, fontWeight: it.style.fontWeight ?? 700, letterSpacing: '0.4px', flexShrink: 0 }}>{label ?? 'ORDENS ANTERIORES'}</div>
                    {panel.previous.length === 0 ? (
                        <div style={{ fontSize: fs, color: C.dim }}>Sem ordens de produção anteriores.</div>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 3, flex: 1, minHeight: 0, overflow: 'hidden' }}>
                            {panel.previous.map(p => <PreviousLotRow key={p.lotId} lot={p} />)}
                        </div>
                    )}
                </div>
            );
        default:
            return <TextItem it={it} defaultColor={C.dim}>{it.key}</TextItem>;
    }
}

export interface Viana3DesignCanvasProps {
    panel: Viana3MachinePanel;
    design: Viana3CardDesignDocument;
    retentionId: number;
    /** Uniform scale applied to the 600×440 canvas. */
    scale: number;
    /** Editor: render disabled items faded instead of hiding them. */
    showDisabled?: boolean;
    /** Editor: item boxes get a data attribute for the overlay layer. */
    className?: string;
    /**
     * Draw the card frame (background, border, radius) on the canvas itself (editor: the canvas IS the card).
     * The TV/preview card passes false: the frame belongs to the cell-filling wrapper, the canvas is a transparent
     * content layer so the design space never imposes a size on the outer card.
     */
    frame?: boolean;
}

/** Card frame shared by the editor canvas and the TV card: same radius/border/background as the built-in MachinePanel. */
function designFrameStyle(design: Viana3CardDesignDocument, panel: Viana3MachinePanel): React.CSSProperties {
    const running = panel.statusCode === 'RUNNING';
    return {
        borderRadius: 14,
        background: design.background ?? C.surface,
        border: running ? '1px solid rgba(34,197,94,0.3)' : `1px solid ${C.border}`,
    };
}

/** The design canvas (canvasWidth×440, default 600×440) at an explicit scale (the wrapper must reserve canvas×scale). */
export const Viana3DesignCanvas = memo(function Viana3DesignCanvas({ panel, design, retentionId, scale, showDisabled, className, frame = true }: Viana3DesignCanvasProps) {
    const cur = panel.current;
    const barScale = Math.max(cur?.plannedBottles ?? 0, cur?.reportedBottles ?? 0, cur && cur.scanCount > 0 ? cur.scannedPreforms ?? 0 : 0, 1);
    const items = [...design.items].sort((a, b) => a.z - b.z);
    const canvas = canvasSize(design);
    return (
        <div className={`${frame ? 'v3-panel ' : ''}${className ?? ''}`} style={{
            width: canvas.w, height: canvas.h, position: 'relative', overflow: 'hidden', boxSizing: 'border-box',
            transform: `scale(${scale})`, transformOrigin: 'top left',
            ...(frame ? designFrameStyle(design, panel) : {}),
            fontFamily: "'Inter', 'Segoe UI', -apple-system, sans-serif", color: C.text,
        }}>
            {items.map(it => {
                if (!it.enabled && !showDisabled) return null;
                return (
                    <div key={it.key} data-design-item={it.key} style={{
                        position: 'absolute', left: it.x, top: it.y, width: it.w, height: it.h, zIndex: it.z,
                        transform: it.rotation ? `rotate(${it.rotation}deg)` : undefined, transformOrigin: 'center center',
                        opacity: (it.enabled ? (it.style.opacity ?? 1) : 0.25),
                        boxSizing: 'border-box', overflow: 'hidden',
                    }}>
                        <ItemContent it={it} panel={panel} retentionId={retentionId} barScale={barScale} />
                    </div>
                );
            })}
        </div>
    );
});

/**
 * The designed card inside a board cell. The OUTER frame (background, border, radius) fills the whole grid area the
 * layout assigned — exactly like the built-in MachinePanel — so a designed card and a standard card of the same layout
 * size occupy equal outer areas. The 600×440 design space only defines item coordinates: the content layer is scaled
 * uniformly (never stretched, never clipped) and centred inside the frame, which keeps the saved positions and the
 * readability scale (`designScaleFor`) unchanged.
 */
export const Viana3DesignedCard = memo(function Viana3DesignedCard({ panel, design, retentionId }: { panel: Viana3MachinePanel; design: Viana3CardDesignDocument; retentionId: number }) {
    const ref = useRef<HTMLDivElement>(null);
    const [box, setBox] = useState({ w: DESIGN_W, h: DESIGN_H });
    useEffect(() => {
        const el = ref.current;
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
    const canvas = canvasSize(design);
    const scale = Math.max(0.05, Math.min(box.w / canvas.w, box.h / canvas.h));
    const left = Math.max(0, (box.w - canvas.w * scale) / 2);
    const top = Math.max(0, (box.h - canvas.h * scale) / 2);
    return (
        <div ref={ref} className="v3-panel" data-designed-card={panel.machineId} style={{
            width: '100%', height: '100%', minWidth: 0, minHeight: 0, position: 'relative', overflow: 'hidden', boxSizing: 'border-box',
            ...designFrameStyle(design, panel),
        }}>
            <div style={{ position: 'absolute', left, top, width: canvas.w * scale, height: canvas.h * scale }}>
                <Viana3DesignCanvas panel={panel} design={design} retentionId={retentionId} scale={scale} frame={false} />
            </div>
        </div>
    );
});
