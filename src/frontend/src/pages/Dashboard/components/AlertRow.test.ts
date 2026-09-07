import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './AlertRow.tsx?raw';

// Dashboard V2 B8.2a — the shared compact alert row (preview + drawer). Max two text lines; the
// description is never rendered (it only duplicates the generated urgency in B8). Navigation is gated on
// the server's canNavigate + targetPath.
describe('AlertRow — compact, non-duplicating, server-driven', () => {
  it('severity is not color-only: an icon AND the severity label text are shown', () => {
    expect(src).toMatch(/alertSeverityMeta\(alert\.severity\)/);
    expect(src).toMatch(/sev\.isCritical \? AlertOctagon : AlertTriangle/);
    expect(src).toMatch(/\{sev\.label\}/);
  });

  it('maps domain and plane through the label helpers (raw codes never rendered)', () => {
    expect(src).toMatch(/alertDomainLabel\(alert\.domain\)/);
    expect(src).toMatch(/alertPlaneMeta\(alert\.plane\)/);
  });

  it('urgency wording uses the server daysDelta via the helper (no local date math)', () => {
    expect(src).toMatch(/alertUrgencyText\(alert\.daysDelta\)/);
    expect(src).not.toMatch(/new Date\(|Date\.now\(/);
  });

  it('does NOT render the description (eliminates the duplicated "Vencido há X" line)', () => {
    expect(src).not.toMatch(/alert\.description/);
  });

  it('renders exactly the title as the second line', () => {
    expect(src).toMatch(/\{alert\.title\}/);
  });

  it('navigation is gated strictly on canNavigate + targetPath; otherwise a read-only row', () => {
    expect(src).toMatch(/const clickable = alert\.canNavigate && !!alert\.targetPath/);
    expect(src).toMatch(/<Link/);
    expect(src).toMatch(/aria-disabled="true"/);
    // No blanket navigate() click handler on rows.
    expect(src).not.toMatch(/onClick=\{\(\) => navigate/);
  });

  it('does not reconstruct roles / status / actionability client-side', () => {
    expect(src).not.toMatch(/RoleConstants|isBuyer|isFinance/i);
    expect(src).not.toMatch(/statusCode|Request\.Status|WAITING_|PAYMENT_/);
  });

  it('mentions no money / FX', () => {
    expect(src).not.toMatch(/AOA|USD|EUR|câmbio|montante|R\$/i);
  });

  it('dark-mode safe: only defined tokens', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
    expect(src).toMatch(/--color-text-main|--color-text-muted|--color-bg-page/);
  });
});
