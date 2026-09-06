import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './AlertsDrawer.tsx?raw';

// Dashboard V2 B8.2a — the full-list informational drawer. It renders the ALREADY-loaded alerts (no
// refetch), makes backend truncation explicit, scrolls internally, and is accessible (dialog semantics,
// Escape, focus management, scroll lock). Filters are intentionally omitted (server cap would make them
// misleading).
describe('AlertsDrawer — informational, transparent, accessible', () => {
  it('does NOT fetch — it renders the alerts passed in as props', () => {
    expect(src).not.toMatch(/api\.dashboardV2|apiFetch|useSectionData|fetch\(/);
    expect(src).toMatch(/alerts\.map\(\(a\) => <AlertRow/);
  });

  it('renders the returned list in server order (no client re-sort)', () => {
    expect(src).not.toMatch(/\.sort\(/);
  });

  it('makes backend truncation explicit only when the server flagged it', () => {
    expect(src).toMatch(/summary\.isTruncated &&/);
    expect(src).toMatch(/alertBackendTruncationText\(summary\.displayedAlertCount, summary\.totalAlertCount\)/);
  });

  it('header summary uses the full-population counts', () => {
    expect(src).toMatch(/alertSummaryText\(summary\.criticalCount, summary\.attentionCount\)/);
  });

  it('scrolls INSIDE the drawer (bounded), not the page body', () => {
    expect(src).toMatch(/overflowY: 'auto'/);
    expect(src).toMatch(/minHeight: 0/);
    // Background scroll is locked while open.
    expect(src).toMatch(/document\.body\.style\.overflow = 'hidden'/);
  });

  it('is an accessible modal dialog (role, aria-modal, labelled title)', () => {
    expect(src).toMatch(/role="dialog"/);
    expect(src).toMatch(/aria-modal="true"/);
    expect(src).toMatch(/aria-labelledby="alerts-drawer-title"/);
    expect(src).toMatch(/id="alerts-drawer-title"/);
  });

  it('Escape closes; focus moves to the close button and is restored on unmount', () => {
    expect(src).toMatch(/e\.key === 'Escape'/);
    expect(src).toMatch(/closeRef\.current\?\.focus\(\)/);
    expect(src).toMatch(/previouslyFocused\.current\?\.focus/);
  });

  it('has an accessible close button and focus-visible styling', () => {
    expect(src).toMatch(/aria-label="Fechar"/);
    expect(src).toMatch(/:focus-visible/);
  });

  it('backdrop closes and is not an interactive control', () => {
    expect(src).toMatch(/onClick=\{onClose\}/);
    expect(src).toMatch(/aria-hidden="true"/);
  });

  it('honors reduced-motion for the slide-in', () => {
    expect(src).toMatch(/prefers-reduced-motion/);
  });

  it('offers NO client-side filters (correctness under the server cap)', () => {
    // No filter state and no filter controls — the list is the raw returned set.
    expect(src).not.toMatch(/useState/);
    expect(src).not.toMatch(/\.filter\(/);
    expect(src).not.toMatch(/severityFilter|domainFilter/i);
  });

  it('closes on navigation (passes onClose to the row)', () => {
    expect(src).toMatch(/onNavigate=\{onClose\}/);
  });

  it('dark-mode safe: only defined tokens', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
    expect(src).toMatch(/--color-bg-surface|--color-text-main|--color-border/);
  });
});
