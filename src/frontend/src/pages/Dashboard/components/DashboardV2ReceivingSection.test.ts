import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './DashboardV2ReceivingSection.tsx?raw';

// Dashboard V2 Receiving section (B4.2): server-sourced counts, Compartilhado/Gerencial planes,
// operational drill-down only for the entitled (shared) plane, no aging, no money, dark-mode safe.
describe('DashboardV2ReceivingSection — structure', () => {
  it('fetches the Receiving section from the server (no client recompute)', () => {
    expect(src).toMatch(/api\.dashboardV2\.getReceiving\(/);
  });

  it('renders under COMPARTILHADO/GERENCIAL, never Pessoal/Minha Operação', () => {
    expect(src).toMatch(/Fila compartilhada — Recebimento/);
    expect(src).toMatch(/Visão gerencial — Recebimento/);
    expect(src).toMatch(/kind=\{operational \? 'compartilhado' : 'gerencial'\}/);
    expect(src).not.toMatch(/Minha Operação/);
    expect(src).not.toMatch(/Pessoal/);
  });

  it('entitlement comes from the server plane (shared operational, managerial view-only) — no role logic', () => {
    expect(src).toMatch(/const operational = !!data\.shared/);
    expect(src).toMatch(/data\.shared \?\? data\.managerial/);
    expect(src).not.toMatch(/RoleConstants|CurrentUserRoles|roles\.contains/i);
  });

  it('renders every count field straight from the server (ActionableGroups NOT derived)', () => {
    for (const f of ['actionableGroups', 'actionableRequests', 'readyForReceiptGroups', 'waitingReceiptGroups', 'followUpGroups', 'waitingSupplierDeliveryGroups']) {
      expect(src).toMatch(new RegExp(`s\\.${f}`));
    }
    // Must not compute actionableGroups by summing the buckets.
    expect(src).not.toMatch(/readyForReceiptGroups\s*\+/);
  });

  it('shows ActionableRequests as a distinct-request secondary line under Grupos acionáveis', () => {
    expect(src).toMatch(/actionableRequests.*pedido/);
    expect(src).toMatch(/Grupos acionáveis/);
  });

  it('operational cards drill into the canonical workspace via the shared helper', () => {
    expect(src).toMatch(/receivingWorkspaceHref/);
    expect(src).toMatch(/navigate\(receivingWorkspaceHref\(c\.drill!\)\)/);
    // drill only when operational (shared plane).
    expect(src).toMatch(/operational && c\.drill \?/);
  });

  it('managerial cards are NOT clickable (plain div, no navigation)', () => {
    expect(src).toMatch(/aria-disabled="true"/);
    expect(src).toMatch(/if \(clickable\) \{/);
  });

  it('no monetary amounts / currency fields in the Receiving section', () => {
    expect(src).not.toMatch(/amount|currency|Currency|Amount/);
  });

  it('no aging / SLA / overdue language (deferred; UpdatedAt unreliable)', () => {
    expect(src).not.toMatch(/atraso|overdue|vencid|SLA|dias|>\s*7|>\s*14|NeedByDate|CreatedAt/i);
  });

  it('dark-mode safe: only defined tokens (no undefined --color-text / --color-bg-elevated)', () => {
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
