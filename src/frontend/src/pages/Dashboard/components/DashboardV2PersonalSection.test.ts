import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './DashboardV2PersonalSection.tsx?raw';

// Dashboard V2 B5.2 — "Minha Operação" (PESSOAL): server-sourced canonical personal actions only.
// No client-side actionability, no urgency/date math, honest empty state, dark-mode safe.
describe('DashboardV2PersonalSection — structure', () => {
  it('fetches the personal section from the server (no client recompute)', () => {
    expect(src).toMatch(/api\.dashboardV2\.getPersonal\(/);
  });

  it('renders "Minha Operação" under the [Pessoal] plane', () => {
    expect(src).toMatch(/Minha Operação/);
    expect(src).toMatch(/label: 'Pessoal'/);
    expect(src).not.toMatch(/Compartilhado/);
    expect(src).not.toMatch(/Gerencial/);
  });

  it('renders the server summary values directly (ActionableActions + ActionableRequests)', () => {
    expect(src).toMatch(/summary\.actionableActions/);
    expect(src).toMatch(/summary\.actionableRequests/);
    // Domain breakdown comes straight from the server byDomain array (not fabricated).
    expect(src).toMatch(/summary\.byDomain\.map/);
    expect(src).not.toMatch(/byDomain\s*=\s*\[/); // never build domain rows client-side
  });

  it('renders the bounded server actions list', () => {
    expect(src).toMatch(/data\.actions\.map/);
    expect(src).toMatch(/action\.requestNumber/);
    expect(src).toMatch(/action\.title/);
  });

  it('navigates only when the server provides a TargetPath (else non-clickable)', () => {
    expect(src).toMatch(/a\.targetPath \? \(\) => navigate\(a\.targetPath!\) : undefined/);
    expect(src).toMatch(/const clickable = !!onOpen/);
    expect(src).toMatch(/aria-disabled="true"/);
    expect(src).toMatch(/if \(clickable\) \{/);
  });

  it('maps action + domain codes to PT labels without renaming backend constants', () => {
    for (const c of ['SUBMIT_DRAFT', 'ADD_QUOTATION', 'SUBMIT_BATCH', 'RESOLVE_ADJUSTMENT', 'AREA_APPROVAL']) {
      expect(src).toMatch(new RegExp(c));
    }
    expect(src).toMatch(/BUYER: 'Compras'/);
    expect(src).toMatch(/REQUESTER: 'Solicitante'/);
    expect(src).toMatch(/APPROVAL: 'Aprovação de Área'/);
  });

  it('honest empty state stays visible at zero and points to the shared queues', () => {
    expect(src).toMatch(/actionableActions === 0/);
    expect(src).toMatch(/Nenhuma ação atribuída pessoalmente no momento/);
    expect(src).toMatch(/filas compartilhadas/);
    // The section is not suppressed when empty (still returns the <section>).
  });

  it('does NOT reconstruct role/status actionability or urgency', () => {
    expect(src).not.toMatch(/NeedByDate|needByDate/);
    expect(src).not.toMatch(/roles\.|RoleConstants|statusCode ===/);
    expect(src).not.toMatch(/Urgentes|Atrasados|Próximos/);
  });

  it('dark-mode safe: only defined tokens', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
    expect(src).toMatch(/--color-text-main|--color-text-muted|--color-bg-surface/);
  });

  it('uses the shared section-state hook + skeleton + error primitives (no per-section fetch logic)', () => {
    expect(src).toMatch(/useSectionData/);
    expect(src).toMatch(/status === 'loading'/);
    expect(src).toMatch(/<DashboardSectionSkeleton/);
    expect(src).toMatch(/<DashboardSectionError onRetry=\{retry\}/);
    // Error is a distinct state now, never a silent null.
    expect(src).not.toMatch(/if \(loading \|\| !data\) return null/);
  });
});
