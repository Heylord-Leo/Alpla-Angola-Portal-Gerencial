/**
 * Viana 3 Production Monitor — card-design model (pure functions, no React).
 *
 *   • item catalogue keys and the generated DEFAULT design (the current card look);
 *   • design resolution per machine: own → template → built-in (null);
 *   • number formatting (presentation only — values are never changed);
 *   • readability check of a design on every screen placement of a machine.
 */

import type { Viana3CardDesignBundle, Viana3CardDesignDocument, Viana3CardDesignItem, Viana3DesignNumberFormat } from '../../../types/viana3CardDesign.types';
import type { Viana3CardSize } from '../../../types/operations.types';
import { STAGE_WIDTH, STAGE_HEIGHT, sizeSpec, clampColumns } from './viana3Layout';

export const DESIGN_SCHEMA_VERSION = 1;
/** Default (and minimum) canvas width in design units. */
export const DESIGN_W = 600;
/** Canvas height is fixed: widening the canvas never moves or rescales existing items. */
export const DESIGN_H = 440;
export const DESIGN_MIN_W = 600;
export const DESIGN_MAX_W = 1200;
export const DESIGN_W_STEP = 10;

/** Canvas size of a document (older documents without a width, or out-of-range values, fall back to the default). */
export function canvasSize(doc: Pick<Viana3CardDesignDocument, 'canvasWidth' | 'canvasHeight'> | null | undefined): { w: number; h: number } {
    const raw = Number(doc?.canvasWidth);
    const w = Number.isFinite(raw) && raw >= DESIGN_MIN_W && raw <= DESIGN_MAX_W ? Math.round(raw) : DESIGN_W;
    return { w, h: DESIGN_H };
}

/** Smallest canvas width that still contains every item (rounded up to the step); never below the default. */
export function minCanvasWidthFor(doc: Viana3CardDesignDocument): number {
    const extent = doc.items.reduce((m, i) => Math.max(m, i.x + i.w), 0);
    return Math.min(DESIGN_MAX_W, Math.max(DESIGN_MIN_W, Math.ceil(extent / DESIGN_W_STEP) * DESIGN_W_STEP));
}
export const MAX_FREE_ITEMS = 6;

/** Readability thresholds in stage pixels (1920×1080 TV composition). Provisional. */
export const READABILITY_OK_PX = 14;
export const READABILITY_WARN_PX = 11;

export const CATALOGUE_KEYS = [
    'machineName', 'status', 'lotLine', 'planWindow', 'illustration', 'palletStack', 'preformLabel',
    'stockAvailable', 'stockPositions', 'stockBlocked', 'planned', 'reported', 'scanned', 'deviation',
    'wasteBottles', 'wastePreforms', 'previousOrders',
] as const;

export type CatalogueKey = typeof CATALOGUE_KEYS[number];

export const ROTATABLE_KEYS = new Set<string>(['illustration']);

export function isFreeKey(key: string): boolean { return /^free-(text|image)-\d+$/.test(key); }
export function isFreeImageKey(key: string): boolean { return key.startsWith('free-image-'); }
export function isFreeTextKey(key: string): boolean { return key.startsWith('free-text-'); }
export function isRotatable(key: string): boolean { return isFreeKey(key) || ROTATABLE_KEYS.has(key); }
export function acceptsImage(key: string): boolean { return key === 'illustration' || isFreeImageKey(key); }

/** Items whose text size matters for readability (the kinds that render text). */
export const TEXT_BEARING_KEYS = new Set<string>([
    'machineName', 'status', 'lotLine', 'planWindow', 'preformLabel', 'stockAvailable', 'stockPositions', 'stockBlocked',
    'planned', 'reported', 'scanned', 'deviation', 'wasteBottles', 'wastePreforms', 'previousOrders',
]);

/** Default font size (design units) of each item, as rendered by the built-in card. */
export const DEFAULT_FONT: Record<string, number> = {
    machineName: 24, status: 13, lotLine: 14, planWindow: 12, preformLabel: 12, stockAvailable: 26, stockPositions: 13,
    stockBlocked: 12, planned: 13, reported: 13, scanned: 13, deviation: 14, wasteBottles: 12, wastePreforms: 12, previousOrders: 12,
};

function item(key: string, x: number, y: number, w: number, h: number, extra?: Partial<Viana3CardDesignItem>): Viana3CardDesignItem {
    return { key, enabled: true, x, y, w, h, rotation: 0, z: 1, style: {}, props: {}, ...extra };
}

/** The current card look expressed as a design (used when neither own design nor template exists, and as the editor's starting point). */
export function buildDefaultDesign(): Viana3CardDesignDocument {
    return {
        schemaVersion: DESIGN_SCHEMA_VERSION,
        canvasWidth: DESIGN_W,
        canvasHeight: DESIGN_H,
        background: null,
        items: [
            item('machineName', 14, 12, 440, 28),
            item('status', 466, 12, 120, 28),
            item('lotLine', 14, 42, 440, 18),
            item('planWindow', 14, 62, 440, 16),
            item('palletStack', 14, 86, 112, 84),
            item('illustration', 132, 82, 300, 96),
            item('preformLabel', 440, 86, 146, 16),
            item('stockAvailable', 440, 104, 146, 36),
            item('stockPositions', 440, 142, 146, 16),
            item('stockBlocked', 440, 160, 146, 18),
            item('planned', 14, 190, 572, 22),
            item('reported', 14, 216, 572, 22),
            item('scanned', 14, 242, 572, 22),
            item('deviation', 14, 270, 240, 24),
            item('wasteBottles', 14, 300, 282, 62),
            item('wastePreforms', 304, 300, 282, 62),
            item('previousOrders', 14, 368, 572, 66),
        ],
    };
}

/** Effective design of a machine: own → template → null (built-in card). */
export function resolveDesign(bundle: Viana3CardDesignBundle | null | undefined, machineId: number): Viana3CardDesignDocument | null {
    if (!bundle) return null;
    const own = bundle.machines.find(m => m.machineId === machineId);
    return own?.design ?? bundle.template ?? null;
}

export function cloneDesign(doc: Viana3CardDesignDocument): Viana3CardDesignDocument {
    return JSON.parse(JSON.stringify(doc)) as Viana3CardDesignDocument;
}

export function designsEqual(a: Viana3CardDesignDocument | null, b: Viana3CardDesignDocument | null): boolean {
    return JSON.stringify(a) === JSON.stringify(b);
}

export function nextFreeKey(doc: Viana3CardDesignDocument, kind: 'text' | 'image'): string | null {
    const free = doc.items.filter(i => isFreeKey(i.key));
    if (free.length >= MAX_FREE_ITEMS) return null;
    for (let n = 1; n <= MAX_FREE_ITEMS * 2; n++) {
        const key = `free-${kind}-${n}`;
        if (!doc.items.some(i => i.key === key)) return key;
    }
    return null;
}

// ─── Number formatting (presentation only) ───

export function formatDesignNumber(value: number | null | undefined, format: Viana3DesignNumberFormat | null | undefined): string {
    if (value === null || value === undefined || isNaN(value)) return '—';
    switch (format ?? 'THOUSANDS') {
        case 'INT': return Math.round(value).toString();
        case 'DECIMAL1': return value.toLocaleString('pt-PT', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
        case 'COMPACT': {
            const abs = Math.abs(value);
            if (abs >= 1_000_000) return `${(value / 1_000_000).toLocaleString('pt-PT', { maximumFractionDigits: 2 })} M`;
            if (abs >= 1_000) return `${(value / 1_000).toLocaleString('pt-PT', { maximumFractionDigits: 1 })} mil`;
            return Math.round(value).toLocaleString('pt-PT');
        }
        case 'THOUSANDS':
        default: return Math.round(value).toLocaleString('pt-PT');
    }
}

// ─── Readability ───

export interface DesignPlacement {
    screenKey: string;
    screenName: string;
    columns: number;
    rows: number;
    size: Viana3CardSize;
}

export interface ReadabilityIssue {
    screenKey: string;
    screenName: string;
    scale: number;
    /** Smallest rendered text in stage pixels. */
    minTextPx: number;
    level: 'OK' | 'WARN' | 'BAD';
    smallestItems: string[];
    overlaps: [string, string][];
    outside: string[];
}

/** Card box on the 1920×1080 stage for a placement (same geometry as Viana3Board). */
export function cardBoxFor(columns: number, rows: number, size: Viana3CardSize): { w: number; h: number } {
    const cols = clampColumns(columns);
    const spec = sizeSpec(size);
    const colSpan = Math.min(spec.colSpan, cols);
    const rowSpan = spec.rowSpan;
    const gap = 14;
    const gridW = STAGE_WIDTH - 2 * 28;
    const gridH = STAGE_HEIGHT - 24 - 66 - 3 - 40 - 2 * 14; // test strip, top bar, progress, footer, padding
    const colW = (gridW - gap * (cols - 1)) / cols;
    const rowH = (gridH - gap * (Math.max(1, rows) - 1)) / Math.max(1, rows);
    return { w: colW * colSpan + gap * (colSpan - 1), h: rowH * rowSpan + gap * (rowSpan - 1) };
}

export function designScaleFor(box: { w: number; h: number }, canvas: { w: number; h: number } = { w: DESIGN_W, h: DESIGN_H }): number {
    return Math.min(box.w / canvas.w, box.h / canvas.h);
}

/** Canvas width whose aspect matches the card box of a placement (fills the card without side bands), clamped to the allowed range. */
export function suggestedCanvasWidth(p: DesignPlacement): number {
    const box = cardBoxFor(p.columns, p.rows, p.size);
    const ideal = Math.round((DESIGN_H * box.w / box.h) / DESIGN_W_STEP) * DESIGN_W_STEP;
    return Math.min(DESIGN_MAX_W, Math.max(DESIGN_MIN_W, ideal));
}

export function effectiveFontSize(it: Viana3CardDesignItem): number {
    return it.style.fontSize ?? DEFAULT_FONT[it.key] ?? (isFreeTextKey(it.key) ? 14 : 12);
}

function rectsOverlap(a: Viana3CardDesignItem, b: Viana3CardDesignItem): boolean {
    return a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
}

export function computeReadability(doc: Viana3CardDesignDocument, placements: DesignPlacement[]): ReadabilityIssue[] {
    const enabled = doc.items.filter(i => i.enabled);
    const textItems = enabled.filter(i => TEXT_BEARING_KEYS.has(i.key) || isFreeTextKey(i.key));
    const overlaps: [string, string][] = [];
    for (let i = 0; i < enabled.length; i++)
        for (let j = i + 1; j < enabled.length; j++)
            if (rectsOverlap(enabled[i], enabled[j])) overlaps.push([enabled[i].key, enabled[j].key]);
    const canvas = canvasSize(doc);
    const outside = enabled.filter(i => i.x < 0 || i.y < 0 || i.x + i.w > canvas.w + 0.01 || i.y + i.h > canvas.h + 0.01).map(i => i.key);

    return placements.map(p => {
        const scale = designScaleFor(cardBoxFor(p.columns, p.rows, p.size), canvas);
        let minPx = Infinity;
        const smallest: string[] = [];
        for (const it of textItems) {
            const px = effectiveFontSize(it) * scale;
            if (px < minPx - 0.01) { minPx = px; smallest.length = 0; smallest.push(it.key); }
            else if (Math.abs(px - minPx) <= 0.01) smallest.push(it.key);
        }
        if (!isFinite(minPx)) minPx = 0;
        const level: ReadabilityIssue['level'] = textItems.length === 0 ? 'OK' : minPx >= READABILITY_OK_PX ? 'OK' : minPx >= READABILITY_WARN_PX ? 'WARN' : 'BAD';
        return { screenKey: p.screenKey, screenName: p.screenName, scale, minTextPx: minPx, level, smallestItems: smallest, overlaps, outside };
    });
}
