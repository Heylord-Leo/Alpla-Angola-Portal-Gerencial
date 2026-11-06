/**
 * Viana 3 Production Monitor — shared layout model (pure functions, no React).
 *
 * The TV and the layout editor use the same resolution and placement so what the
 * editor previews at 1920×1080 is exactly what the TV renders:
 *   • resolveLayout: saved cards (in order) + machines not yet in the layout
 *     (appended visible, STANDARD). Hidden cards stay in the document — hiding
 *     never deletes anything.
 *   • placeCards: deterministic row-major placement of column/row spans on the
 *     stage grid (same result on every device).
 */

import type {
    Viana3CardSize,
    Viana3MachinePanel,
    Viana3MonitorLayout,
    Viana3MonitorLayoutCard,
} from '../../../types/operations.types';

export const STAGE_WIDTH = 1920;
export const STAGE_HEIGHT = 1080;

export const MIN_COLUMNS = 2;
export const MAX_COLUMNS = 4;
export const DEFAULT_COLUMNS = 3;
export const DEFAULT_SIZE: Viana3CardSize = 'STANDARD';

export const CARD_SIZES: { value: Viana3CardSize; label: string; hint: string; colSpan: number; rowSpan: number }[] = [
    { value: 'COMPACT', label: 'Compacto', hint: '1×1 · sem ilustração nem ordens anteriores', colSpan: 1, rowSpan: 1 },
    { value: 'STANDARD', label: 'Normal', hint: '1×1 · conteúdo completo', colSpan: 1, rowSpan: 1 },
    { value: 'WIDE', label: 'Largo', hint: '2×1 · ocupa duas colunas', colSpan: 2, rowSpan: 1 },
    { value: 'LARGE', label: 'Grande', hint: '2×2 · duas colunas e duas linhas, texto ampliado', colSpan: 2, rowSpan: 2 },
];

export function sizeSpec(size: Viana3CardSize) {
    return CARD_SIZES.find(s => s.value === size) ?? CARD_SIZES[1];
}

export function clampColumns(n: number): number {
    if (!Number.isFinite(n)) return DEFAULT_COLUMNS;
    return Math.max(MIN_COLUMNS, Math.min(MAX_COLUMNS, Math.round(n)));
}

export function isCardSize(v: unknown): v is Viana3CardSize {
    return typeof v === 'string' && CARD_SIZES.some(s => s.value === v);
}

/** A layout card joined with its live machine data (null when the machine has no recent planning). */
export interface ResolvedCard {
    machineId: number;
    size: Viana3CardSize;
    hidden: boolean;
    panel: Viana3MachinePanel | null;
    /** True when the card came from the saved document (false = appended from discovery). */
    fromLayout: boolean;
}

export interface ResolvedLayout {
    columns: number;
    cards: ResolvedCard[];
}

/**
 * Joins the saved layout with the machines present in the data payload.
 * Order: saved cards first (saved order), then — only when the screen includes new
 * machines (default screen behaviour) — the machines not listed in the document.
 * A screen that does not include new machines shows exactly its selected cards.
 */
export function resolveLayout(layout: Viana3MonitorLayout | null | undefined, machines: Viana3MachinePanel[]): ResolvedLayout {
    const includeNew = layout?.includeNewMachines ?? true;
    const byId = new Map<number, Viana3MachinePanel>();
    for (const m of machines) byId.set(m.machineId, m);

    const cards: ResolvedCard[] = [];
    const seen = new Set<number>();

    for (const c of layout?.cards ?? []) {
        if (!c || typeof c.machineId !== 'number' || c.machineId <= 0 || seen.has(c.machineId)) continue;
        seen.add(c.machineId);
        cards.push({
            machineId: c.machineId,
            size: isCardSize(c.size) ? c.size : DEFAULT_SIZE,
            hidden: !!c.hidden,
            panel: byId.get(c.machineId) ?? null,
            fromLayout: true,
        });
    }
    if (includeNew) {
        for (const m of machines) {
            if (seen.has(m.machineId)) continue;
            seen.add(m.machineId);
            cards.push({ machineId: m.machineId, size: DEFAULT_SIZE, hidden: false, panel: m, fromLayout: false });
        }
    }

    return { columns: clampColumns(layout?.columns ?? DEFAULT_COLUMNS), cards };
}

/** Machines present in the data but not listed on the screen (candidates for "Adicionar"). */
export function machinesOffScreen(cards: { machineId: number }[], machines: Viana3MachinePanel[]): Viana3MachinePanel[] {
    const on = new Set(cards.map(c => c.machineId));
    return machines.filter(m => !on.has(m.machineId));
}

/** Cards the TV actually draws: visible and with current data. */
export function visibleCards(resolved: ResolvedLayout): ResolvedCard[] {
    return resolved.cards.filter(c => !c.hidden && c.panel !== null);
}

export interface PlacedCard<T> {
    item: T;
    col: number;      // 0-based
    row: number;      // 0-based
    colSpan: number;
    rowSpan: number;
}

/**
 * Row-major sparse placement (the cursor only moves forward, like CSS grid
 * auto-placement without `dense`). Spans are clamped to the column count so a
 * layout saved for 4 columns still renders on 2.
 */
export function placeCards<T extends { size: Viana3CardSize }>(items: T[], columns: number): { rows: number; placed: PlacedCard<T>[] } {
    const cols = clampColumns(columns);
    const occupied: boolean[][] = [];
    const ensureRow = (r: number) => { while (occupied.length <= r) occupied.push(new Array<boolean>(cols).fill(false)); };
    const fits = (r: number, c: number, cs: number, rs: number) => {
        for (let dr = 0; dr < rs; dr++) {
            ensureRow(r + dr);
            for (let dc = 0; dc < cs; dc++) if (occupied[r + dr][c + dc]) return false;
        }
        return true;
    };

    const placed: PlacedCard<T>[] = [];
    let cr = 0, cc = 0;
    for (const item of items) {
        const spec = sizeSpec(item.size);
        const cs = Math.min(spec.colSpan, cols);
        const rs = spec.rowSpan;
        // advance until the span fits
        for (;;) {
            if (cc + cs > cols) { cr++; cc = 0; }
            ensureRow(cr);
            if (fits(cr, cc, cs, rs)) break;
            cc++;
        }
        for (let dr = 0; dr < rs; dr++) for (let dc = 0; dc < cs; dc++) occupied[cr + dr][cc + dc] = true;
        placed.push({ item, col: cc, row: cr, colSpan: cs, rowSpan: rs });
        cc += cs;
    }

    // rows actually used (trailing empty rows may exist from ensureRow look-ahead)
    let rows = 0;
    for (const p of placed) rows = Math.max(rows, p.row + p.rowSpan);
    return { rows: Math.max(1, rows), placed };
}

/** Canonical document for equality / saving (order, size, hidden; no live data). */
export function toLayoutCards(cards: { machineId: number; size: Viana3CardSize; hidden: boolean }[]): Viana3MonitorLayoutCard[] {
    return cards.map(c => ({ machineId: c.machineId, size: c.size, hidden: c.hidden }));
}

export function layoutsEqual(
    a: { columns: number; cards: { machineId: number; size: Viana3CardSize; hidden: boolean }[] },
    b: { columns: number; cards: { machineId: number; size: Viana3CardSize; hidden: boolean }[] },
): boolean {
    if (a.columns !== b.columns || a.cards.length !== b.cards.length) return false;
    for (let i = 0; i < a.cards.length; i++) {
        const x = a.cards[i], y = b.cards[i];
        if (x.machineId !== y.machineId || x.size !== y.size || x.hidden !== y.hidden) return false;
    }
    return true;
}
