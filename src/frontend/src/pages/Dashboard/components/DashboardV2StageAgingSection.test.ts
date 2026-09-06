import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './DashboardV2StageAgingSection.tsx?raw';

// Dashboard V2 B9.5 — canonical "Gargalos" Stage Aging section. Managerial, read-only, honest about
// thresholdless stages and unknown age; never request-age wording.
describe('DashboardV2StageAgingSection — structure', () => {
  it('fetches stage-aging from the server with the AbortSignal', () => {
    expect(src).toMatch(/api\.dashboardV2\.getStageAging\(signal\)/);
    expect((src.match(/getStageAging\(/g) || []).length).toBe(1);
  });

  it('renders "Gargalos do Processo" under [Gerencial] with SectionInfo', () => {
    expect(src).toMatch(/Gargalos do Processo/);
    expect(src).toMatch(/label: 'Gerencial'/);
    expect(src).toMatch(/<SectionInfo \{\.\.\.DASHBOARD_SECTION_HELP\.stageAging\}/);
  });

  it('hides the section when the server says not entitled (summary === null)', () => {
    expect(src).toMatch(/if \(!data\.summary\) return null/);
  });

  it('shows the honest zero-work empty state (never "não há gargalos")', () => {
    expect(src).toMatch(/s\.totalActiveEntities === 0/);
    expect(src).toMatch(/Não há etapas com medição de permanência ativa no seu escopo\./);
    expect(src).not.toMatch(/não há gargalos/i);
  });

  it('summary uses backend summary counts (critical / attention / unknown), not row derivation', () => {
    expect(src).toMatch(/s\.criticalEntities/);
    expect(src).toMatch(/s\.attentionEntities/);
    expect(src).toMatch(/s\.unknownAgeEntities/);
  });

  it('displays stages risk-ranked via the pure helper without mutating server data', () => {
    expect(src).toMatch(/rankByBottleneck\(data\.stages\)/);
    expect(src).not.toMatch(/data\.stages\.sort\(/);
  });

  it('delegates all wording to the pure view helpers (no business transforms in JSX)', () => {
    expect(src).toMatch(/entityCountText\(stage\)/);
    expect(src).toMatch(/compositionText\(stage\)/);
    expect(src).toMatch(/oldestAgeText\(stage\.oldestAgeDays\)/);
    expect(src).toMatch(/compositionSegments\(stage\)/);
  });

  it('renders the composition bar only when it carries severity meaning', () => {
    expect(src).toMatch(/hasMeaningfulComposition\(stage\) && \(/);
  });

  it('rows are READ-ONLY — no navigation, no link, no pointer/hover affordance', () => {
    expect(src).not.toMatch(/<Link/);
    expect(src).not.toMatch(/navigate\(/);
    expect(src).not.toMatch(/ChevronRight/);
    expect(src).not.toMatch(/cursor: 'pointer'/);
    expect(src).toMatch(/aria-disabled="true"/);
  });

  it('never uses request-age wording', () => {
    expect(src).not.toMatch(/pedido com .* dias/i);
    expect(src).not.toMatch(/idade do pedido/i);
  });

  it('uses the shared loading/error/skeleton primitives', () => {
    expect(src).toMatch(/useSectionData/);
    expect(src).toMatch(/status === 'loading'/);
    expect(src).toMatch(/<DashboardSectionSkeleton/);
    expect(src).toMatch(/<DashboardSectionError onRetry=\{retry\}/);
  });

  it('does not consume legacy cockpit bottlenecks', () => {
    expect(src).not.toMatch(/cockpit|BottleneckTable/i);
  });

  it('dark-mode safe: only defined tokens', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
    expect(src).toMatch(/--color-text-main|--color-text-muted|--color-bg-surface/);
  });
});
