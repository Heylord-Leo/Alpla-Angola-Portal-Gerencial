import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './DashboardV2FinanceSection.tsx?raw';

// Dashboard V2 Finance section (B3.2): server-sourced counts, Compartilhado/Gerencial planes,
// operational drill-down only for the entitled (shared) plane, no money, dark-mode safe.
describe('DashboardV2FinanceSection — structure', () => {
  it('fetches the Finance section from the server (no client recompute)', () => {
    expect(src).toMatch(/api\.dashboardV2\.getFinance\(\)/);
  });

  it('renders under COMPARTILHADO/GERENCIAL, never Pessoal/Minha Operação', () => {
    expect(src).toMatch(/Fila compartilhada — Finanças/);
    expect(src).toMatch(/Visão gerencial — Finanças/);
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
    for (const f of ['actionableGroups', 'actionableRequests', 'needsSchedulingGroups', 'needsPaymentGroups', 'dueTodayGroups', 'overdueGroups', 'paidWaitingReceivingGroups']) {
      expect(src).toMatch(new RegExp(`s\\.${f}`));
    }
    // Must not compute actionable as needsScheduling + needsPayment.
    expect(src).not.toMatch(/needsSchedulingGroups\s*\+\s*.*needsPaymentGroups/);
  });

  it('operational cards drill into existing /finance/payments filters via the shared helper', () => {
    expect(src).toMatch(/financePaymentsHref/);
    expect(src).toMatch(/navigate\(financePaymentsHref\(c\.drill!\)\)/);
    // drill only when operational (shared plane).
    expect(src).toMatch(/operational && c\.drill \?/);
  });

  it('managerial cards are NOT clickable (plain div, no navigation)', () => {
    // clickable path is a <button>; non-clickable returns a div with aria-disabled and no onClick.
    expect(src).toMatch(/aria-disabled="true"/);
    expect(src).toMatch(/if \(clickable\) \{/);
  });

  it('FiscalDocumentPending nuance: actionable tooltip does not enumerate only two classes', () => {
    expect(src).toMatch(/pelo menos uma ação disponível para Finanças no momento/);
  });

  it('no monetary amounts / currency fields in the Finance section', () => {
    expect(src).not.toMatch(/amount|currency|Currency|Amount/);
  });

  it('dark-mode safe: only defined tokens (no undefined --color-text / --color-bg-elevated)', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
    expect(src).toMatch(/--color-text-main|--color-text-muted|--color-bg-surface/);
  });

  it('error isolation: a failed fetch does not throw (renders nothing)', () => {
    expect(src).toMatch(/\.catch\(\(\) => \{ if \(alive\) setData\(null\); \}\)/);
  });
});
