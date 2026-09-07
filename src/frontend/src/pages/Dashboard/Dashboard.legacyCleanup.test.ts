import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './Dashboard.tsx?raw';

// Dashboard V2 B5.2 — the legacy personal block ("Minha Fila de Trabalho") is replaced by the canonical
// "Minha Operação" plane, and the stale-alert AlertList is hidden until B8. The B1–B4 V2 sections and the
// retained managerial sections stay.
describe('Dashboard — B5.2 legacy personal cleanup', () => {
  it('mounts the canonical personal plane', () => {
    expect(src).toMatch(/import \{ DashboardV2PersonalSection \}/);
    expect(src).toMatch(/<DashboardV2PersonalSection \/>/);
  });

  it('no longer mounts the legacy MyWorkQueue personal block', () => {
    expect(src).not.toMatch(/MyWorkQueue/);
  });

  it('legacy personal metric labels are gone from the dashboard shell', () => {
    // These lived in MyWorkQueue; they must not reappear inline in Dashboard.tsx.
    expect(src).not.toMatch(/Minha Fila de Trabalho/);
    expect(src).not.toMatch(/Aguardando Minha Ação|Aguardando minha ação/);
    // "Urgentes"/"Atrasados"/"Próximos da Data" personal cards removed.
    expect(src).not.toMatch(/Urgentes/);
    expect(src).not.toMatch(/Atrasados/);
    expect(src).not.toMatch(/Próximos da Data/);
  });

  it('hides the stale-alert AlertList (deferred to B8), backend untouched', () => {
    expect(src).not.toMatch(/<AlertList/);
    expect(src).not.toMatch(/import \{ AlertList \}/);
  });

  it('keeps the B1–B4 V2 sections (no regression)', () => {
    expect(src).toMatch(/<DashboardV2BuyerSection \/>/);
    expect(src).toMatch(/<DashboardV2FinanceSection \/>/);
    expect(src).toMatch(/<DashboardV2ReceivingSection \/>/);
  });

  it('mounts the canonical Alerts section (B8.2) instead of the legacy AlertList', () => {
    expect(src).toMatch(/import \{ DashboardV2AlertsSection \}/);
    expect(src).toMatch(/<DashboardV2AlertsSection \/>/);
  });

  it('places Alerts after the shared work queues and before the managerial analytics', () => {
    const receiving = src.indexOf('<DashboardV2ReceivingSection');
    const alerts = src.indexOf('<DashboardV2AlertsSection');
    const gerencial = src.indexOf('Visão Gerencial');
    const pipeline = src.indexOf('<DashboardV2PipelineSection');
    expect(alerts).toBeGreaterThan(receiving);   // after the last shared queue
    expect(gerencial).toBeGreaterThan(alerts);   // before the Gerencial heading
    expect(pipeline).toBeGreaterThan(alerts);    // before the managerial Pipeline
  });

  it('mounts the canonical Pipeline section (B6.2) and drops the legacy scalar pipeline cards', () => {
    expect(src).toMatch(/import \{ DashboardV2PipelineSection \}/);
    expect(src).toMatch(/<DashboardV2PipelineSection \/>/);
    // The legacy scalar Request.Status pipeline cards config is gone.
    expect(src).not.toMatch(/pipelineCards/);
    expect(src).not.toMatch(/Ag\. Cotação|Aprov\. Área|Aprov\. Final/);
  });

  it('retains managerial analytical sections under a Gerencial framing (not personal)', () => {
    expect(src).toMatch(/Visão Gerencial/);
    expect(src).toMatch(/<QuickActions \/>/);
  });

  it('replaces the legacy BottleneckTable with the canonical Stage Aging section (B9.5)', () => {
    expect(src).toMatch(/import \{ DashboardV2StageAgingSection \}/);
    expect(src).toMatch(/<DashboardV2StageAgingSection \/>/);
    // Legacy Gargalos is no longer imported or rendered.
    expect(src).not.toMatch(/<BottleneckTable/);
    expect(src).not.toMatch(/import \{ BottleneckTable \}/);
    expect(src).not.toMatch(/cockpit\.bottlenecks/);
  });

  it('B9.6 — the legacy cockpit-summary dependency is fully removed (no fetch, state or DTO)', () => {
    expect(src).not.toMatch(/getCockpitSummary/);
    expect(src).not.toMatch(/setCockpit|\[cockpit,/);
    expect(src).not.toMatch(/CockpitSummaryDto/);
    expect(src).not.toMatch(/cockpit\.bottlenecks/);
    // No page-level loading/error gate coupled to the removed cockpit fetch.
    expect(src).not.toMatch(/if \(isLoading\)/);
    expect(src).not.toMatch(/if \(!cockpit\) return null/);
  });

  it('replaces the legacy mixed-currency Financial Summary with the canonical section (B7.2)', () => {
    expect(src).toMatch(/import \{ DashboardV2FinancialSection \}/);
    expect(src).toMatch(/<DashboardV2FinancialSection \/>/);
    expect(src).not.toMatch(/<FinancialSummary/);
    expect(src).not.toMatch(/import \{ FinancialSummary \}/);
    expect(src).not.toMatch(/financialByStatus/);
  });

  it('personal plane is ordered before the shared queues', () => {
    const personal = src.indexOf('<DashboardV2PersonalSection');
    const buyer = src.indexOf('<DashboardV2BuyerSection');
    expect(personal).toBeGreaterThan(-1);
    expect(buyer).toBeGreaterThan(personal);
  });

  it('does not reintroduce undefined dark-mode tokens', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
  });
});
