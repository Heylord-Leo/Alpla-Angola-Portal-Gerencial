import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './DashboardV2AlertsSection.tsx?raw';

// Dashboard V2 B8.2a — the Alerts section is a compact SUMMARY: severity badges + a max-6-row preview +
// a "Ver todos os alertas" drawer affordance. It must never render the full (up to 100) list inline.
describe('DashboardV2AlertsSection — compact summary + preview', () => {
  it('fetches alerts once from the server with the AbortSignal (no refetch elsewhere)', () => {
    expect(src).toMatch(/api\.dashboardV2\.getAlerts\(signal\)/);
    expect((src.match(/getAlerts\(/g) || []).length).toBe(1);
  });

  it('renders the "Atenção Necessária" heading with a SectionInfo affordance', () => {
    expect(src).toMatch(/Atenção Necessária/);
    expect(src).toMatch(/<SectionInfo \{\.\.\.DASHBOARD_SECTION_HELP\.alerts\}/);
  });

  it('previews at most 6 alerts, in server order (slice, never sort)', () => {
    expect(src).toMatch(/const PREVIEW_LIMIT = 6/);
    expect(src).toMatch(/data\.alerts\.slice\(0, PREVIEW_LIMIT\)/);
    expect(src).toMatch(/preview\.map\(/);
    // The full list is NOT mapped inline in the Dashboard.
    expect(src).not.toMatch(/data\.alerts\.map\(/);
    expect(src).not.toMatch(/\.sort\(/);
  });

  it('summary badges come from the full-population counts, never from the preview/list length', () => {
    expect(src).toMatch(/s\.criticalCount/);
    expect(src).toMatch(/s\.attentionCount/);
    expect(src).toMatch(/s\.totalAlertCount/);
    expect(src).not.toMatch(/alerts\.length.*críticos|críticos.*alerts\.length/);
  });

  it('shows "Ver todos os alertas" only when the total exceeds the preview, and it opens the drawer', () => {
    expect(src).toMatch(/const showViewAll = s\.totalAlertCount > preview\.length/);
    expect(src).toMatch(/showViewAll &&/);
    expect(src).toMatch(/Ver todos os alertas/);
    expect(src).toMatch(/setDrawerOpen\(true\)/);
  });

  it('uses the concise preview footer copy (not the long inline truncation sentence)', () => {
    expect(src).toMatch(/alertPreviewFooterText\(preview\.length, s\.totalAlertCount\)/);
    expect(src).not.toMatch(/Mostrando os/);
  });

  it('mounts the drawer with the FULL returned list + summary, gated on drawer state', () => {
    expect(src).toMatch(/drawerOpen && <AlertsDrawer alerts=\{data\.alerts\} summary=\{s\}/);
  });

  it('hides the section only when the server says not entitled (summary === null)', () => {
    expect(src).toMatch(/if \(!data\.summary\) return null/);
  });

  it('entitled + zero alerts keeps the section visible with the exact empty-state copy and no drawer button', () => {
    expect(src).toMatch(/const isEmpty = data\.alerts\.length === 0/);
    expect(src).toMatch(/Não há alertas ativos no seu escopo\./);
    expect(src).toMatch(/Isso não significa que não haja trabalho — consulte as filas operacionais acima\./);
    expect(src).not.toMatch(/Nenhuma pendência/);
    expect(src).not.toMatch(/Nada para fazer/);
  });

  it('delegates row rendering to the shared AlertRow (no inline row markup / no description line)', () => {
    expect(src).toMatch(/import \{ AlertRow \}/);
    expect(src).not.toMatch(/alert\.description/);
  });

  it('preserves the shared loading/error/skeleton primitives and single AbortController fetch', () => {
    expect(src).toMatch(/useSectionData/);
    expect(src).toMatch(/status === 'loading'/);
    expect(src).toMatch(/<DashboardSectionSkeleton/);
    expect(src).toMatch(/<DashboardSectionError onRetry=\{retry\}/);
  });

  it('does not consume the legacy cockpit alerts feed', () => {
    expect(src).not.toMatch(/cockpit|AlertList/i);
  });

  it('dark-mode safe: only defined tokens', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
    expect(src).toMatch(/--color-text-main|--color-text-muted|--color-bg-surface/);
  });
});
