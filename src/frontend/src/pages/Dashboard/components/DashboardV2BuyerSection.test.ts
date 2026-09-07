import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './DashboardV2BuyerSection.tsx?raw';

// Dashboard V2 Buyer section: three planes, server-sourced, unassigned pinned & never a person.
describe('DashboardV2BuyerSection — structure', () => {
  it('fetches the canonical Buyer section from the server (no client recompute)', () => {
    expect(src).toMatch(/api\.dashboardV2\.getBuyer\(/);
  });

  it('renders the three planes with explicit ownership pills', () => {
    expect(src).toMatch(/Minha Operação — Compras/);
    expect(src).toMatch(/Fila compartilhada de Compras/);
    expect(src).toMatch(/Carga da Equipe de Compras/);
    expect(src).toMatch(/kind="pessoal"/);
    expect(src).toMatch(/kind="compartilhado"/);
    expect(src).toMatch(/kind="gerencial"/);
  });

  it('gates each plane on server data (personal/shared/workload) — no faked personalization', () => {
    expect(src).toMatch(/hasPersonalWork\(personal\)/);
    expect(src).toMatch(/hasSharedWork\(shared\)/);
    expect(src).toMatch(/displayWorkloadRows\(workload\?\.rows\)/);
  });

  it('pins the UNASSIGNED bucket as a distinct row labelled "Sem atribuição", never a buyer', () => {
    expect(src).toMatch(/workload\.unassigned &&/);
    expect(src).toMatch(/Sem atribuição/);
    expect(src).toMatch(/pinned/);
  });

  it('drills down via the centralized buyerQueueHref helper (canonical /buyer/items route)', () => {
    expect(src).toMatch(/buyerQueueHref\(\{ ownership: 'me' \}\)/);
    expect(src).toMatch(/buyerQueueHref\(\{ ownership: 'unassigned' \}\)/);
    expect(src).toMatch(/buyerQueueHref\(\{ buyerId: r\.buyerId \}\)/);
  });

  it('never navigates to the non-existent /buyer/queue route (feedback #2)', () => {
    expect(src).not.toMatch(/\/buyer\/queue/);
  });

  // ── Acceptance feedback #1: in-cell column bars ──
  it('renders per-column, independently-scaled in-cell bars (not a chart library)', () => {
    expect(src).toMatch(/workloadColumnMaxes\(scaleRows\)/);
    expect(src).toMatch(/barPercent\(value, maxes\[c\.key\]\)/);
    expect(src).toMatch(/pct > 0 &&/);            // zero => no bar
    expect(src).toMatch(/aria-hidden/);           // bar is decorative
    expect(src).not.toMatch(/from ['"]recharts['"]|chart\.js/); // no charting lib
  });

  it('scale spans every displayed row including the pinned unassigned bucket', () => {
    expect(src).toMatch(/scaleRows = \[\.\.\.\(workload\.unassigned \? \[workload\.unassigned\] : \[\]\), \.\.\.rows\]/);
  });

  it('keeps a distinct semantic color per column (blue/teal/amber/green/red)', () => {
    expect(src).toMatch(/#3b82f6/); // Atribuídos blue
    expect(src).toMatch(/#0e9aa7/); // Acionáveis teal/cyan
    expect(src).toMatch(/#d97706/); // Itens pendentes amber
    expect(src).toMatch(/#16a34a/); // Itens prontos green
    expect(src).toMatch(/#dc2626/); // Atenção red
  });

  // ── Acceptance feedback #1: header tooltips ──
  it('uses the project ModernTooltip primitive for every header (keyboard + click)', () => {
    expect(src).toMatch(/import \{ ModernTooltip \} from/);
    expect(src).toMatch(/<ModernTooltip/);
    expect(src).toMatch(/openOnClick/);
    expect(src).toMatch(/ariaLabel=\{`\$\{title\} — informação`\}/);
  });

  it('exposes the six required header tooltips with unit metadata', () => {
    expect(src).toMatch(/Comprador/);
    expect(src).toMatch(/Pedidos atribuídos/);
    expect(src).toMatch(/Pedidos acionáveis/);
    expect(src).toMatch(/title: 'Itens pendentes'/);
    expect(src).toMatch(/title: 'Itens prontos'/);
    expect(src).toMatch(/Requer atenção/);
    expect(src).toMatch(/Unidade: \{unit\}/);
    expect(src).toMatch(/unit: 'pedidos'/);
    expect(src).toMatch(/unit: 'itens'/);
  });

  it('Attention tooltip matches the actual metric semantics (overdue + critical-today, action open)', () => {
    expect(src).toMatch(/hoje ou já venceu/);
    expect(src).toMatch(/enquanto ainda existir uma ação de Compras aberta/);
  });

  // ── Acceptance feedback #4: dark-mode contrast (defined theme tokens only) ──
  it('tooltip content sets explicit theme tokens so dark mode stays high-contrast', () => {
    const content = src.split('content={(')[1]?.split(')}')[0] ?? '';
    expect(content).toMatch(/color: 'var\(--color-text-main\)'/);   // title/body high-contrast in both themes
    expect(content).toMatch(/color: 'var\(--color-text-muted\)'/);  // unit: readable but secondary
  });

  it('table header uses a defined surface token (page), not a near-white slab, in dark mode', () => {
    expect(src).toMatch(/backgroundColor: 'var\(--color-bg-page\)'/);
    expect(src).not.toMatch(/--color-bg-elevated/);   // undefined token removed
  });

  it('uses only defined text tokens (no undefined var(--color-text))', () => {
    // --color-text-main / --color-text-muted are fine; the bare --color-text is undefined in tokens.css.
    expect(src).not.toMatch(/var\(--color-text\)/);
  });

  it('keeps the info-icon trigger accessible (ariaLabel) — behavior unchanged', () => {
    expect(src).toMatch(/ariaLabel=\{`\$\{title\} — informação`\}/);
  });
});
