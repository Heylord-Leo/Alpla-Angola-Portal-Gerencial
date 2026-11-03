import { describe, it, expect } from 'vitest';
// Node-only vitest (no jsdom/RTL) — source-level structural guards.
import src from './BuyerRequestWorkspace.tsx?raw';

// v2.249.1 — "Itens & Cobertura" row menu. The previous inline dropdown (position:absolute inside a
// `position:relative` cell) was clipped by the table's `overflowX: auto` wrapper and by the viewport on the
// last rows. The row now uses the shared portal KebabMenu (fixed, viewport-aware placement) with exactly
// the server-authorized action.

const tabItems = src.split('function TabItems(')[1]?.split('// ── Tab 2:')[0] ?? '';

describe('BuyerRequestWorkspace — item row menu (TabItems)', () => {
    it('uses the shared portal KebabMenu', () => {
        expect(src).toMatch(/import \{ KebabMenu \} from '\.\.\/\.\.\/components\/ui\/KebabMenu'/);
        expect(tabItems).toMatch(/<KebabMenu\s/);
    });

    it('has no inline absolutely-positioned dropdown nor local open-state', () => {
        expect(tabItems).not.toMatch(/openKebab/);
        expect(tabItems).not.toMatch(/position: 'absolute'/);
        expect(tabItems).not.toMatch(/position: 'relative'/);
        // The table keeps its horizontal scroll wrapper (unchanged globally).
        expect(tabItems).toMatch(/overflowX: 'auto'/);
    });

    it('offers exactly one action — "Desconsiderar item" — gated by the server flag canCloseNotQuoted', () => {
        // Menu options only (the coverage KPI cards also use a `label:` key).
        const menu = tabItems.split('<KebabMenu')[1]?.split('/>')[0] ?? '';
        const labels = menu.match(/label: '[^']+'/g) ?? [];
        expect(labels).toEqual(["label: 'Desconsiderar item'"]);
        expect(tabItems).toMatch(/\{it\.canCloseNotQuoted && \(\s*<KebabMenu/);
        expect(tabItems).toMatch(/onClick: \(\) => onDisregard\(it\)/);
    });

    it('names the trigger per line for assistive tech', () => {
        expect(tabItems).toMatch(/ariaLabel=\{`Ações do item \$\{it\.lineNumber\}`\}/);
    });
});
