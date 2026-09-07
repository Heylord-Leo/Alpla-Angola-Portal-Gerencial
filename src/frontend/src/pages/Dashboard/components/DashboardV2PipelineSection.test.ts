import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './DashboardV2PipelineSection.tsx?raw';

// Dashboard V2 B6.2 — canonical Operational Pipeline (GERENCIAL). Server-sourced stage counts, grouped
// by domain, unit-labelled; a request may span stages; no client-side status/actionability logic.
describe('DashboardV2PipelineSection — structure', () => {
  it('fetches the pipeline from the server (no client recompute)', () => {
    expect(src).toMatch(/api\.dashboardV2\.getPipeline\(/);
  });

  it('renders under [Gerencial] with a SectionInfo affordance', () => {
    expect(src).toMatch(/Visão do Pipeline/);
    expect(src).toMatch(/label: 'Gerencial'/);
    expect(src).toMatch(/<SectionInfo/);
  });

  it('headline uses uniqueActiveRequests, labelled "Pedidos ativos" (never a stage sum)', () => {
    expect(src).toMatch(/Pedidos ativos/);
    expect(src).toMatch(/data\.uniqueActiveRequests/);
    // Must not sum stage counts for the headline.
    expect(src).not.toMatch(/reduce\(/);
  });

  it('shows the overlap explanation near the headline', () => {
    expect(src).toMatch(/soma das etapas pode exceder o total de pedidos ativos/);
  });

  it('groups stages by domain via the shared helper (no hardcoded stage list)', () => {
    expect(src).toMatch(/groupStages\(data\.stages\)/);
    expect(src).toMatch(/group\.label/);
  });

  it('stage cards render server values through the unit helpers', () => {
    expect(src).toMatch(/primaryEntityText\(stage\)/);
    expect(src).toMatch(/secondaryRequestText\(stage\)/);
    // No client-side count arithmetic in the card.
    expect(src).not.toMatch(/entityCount \+|\+ requestCount/);
  });

  it('Reajuste gets the loop affordance; only stages with targetPath are clickable', () => {
    expect(src).toMatch(/isAdjustmentStage\(stage\)/);
    expect(src).toMatch(/s\.targetPath \? \(\) => navigate\(s\.targetPath!\) : undefined/);
    expect(src).toMatch(/const clickable = !!onOpen/);
    expect(src).toMatch(/aria-disabled="true"/);
  });

  it('de-emphasizes the terminal Concluído count', () => {
    expect(src).toMatch(/stage\.stage === 'COMPLETED'/);
  });

  it('does NOT inspect request/group status or compute pipeline membership/urgency', () => {
    expect(src).not.toMatch(/statusCode|Request\.Status|WAITING_|PAYMENT_|NeedByDate/);
    expect(src).not.toMatch(/Vencido|overdue|atraso/i);
  });

  it('inner container scrolls (never the page body); wraps on narrow screens', () => {
    expect(src).toMatch(/flexWrap: 'wrap'/);
    expect(src).toMatch(/overflowX: 'auto'/);
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
