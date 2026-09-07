import { describe, it, expect } from 'vitest';
import { DASHBOARD_SECTION_HELP } from './dashboardSectionHelp';
// Source guards over the components (?raw): every required header wires SectionInfo from the central map.
import personalSrc from './components/DashboardV2PersonalSection.tsx?raw';
import buyerSrc from './components/DashboardV2BuyerSection.tsx?raw';
import financeSrc from './components/DashboardV2FinanceSection.tsx?raw';
import receivingSrc from './components/DashboardV2ReceivingSection.tsx?raw';
import pipelineSrc from './components/DashboardV2PipelineSection.tsx?raw';
import stageAgingSrc from './components/DashboardV2StageAgingSection.tsx?raw';
import financialSrc from './components/FinancialSummary.tsx?raw';
import quickSrc from './components/QuickActions.tsx?raw';
import dashboardSrc from './Dashboard.tsx?raw';

describe('dashboardSectionHelp — content model', () => {
  it('defines an entry for every required Dashboard section', () => {
    for (const key of ['personal', 'buyerPersonal', 'buyerShared', 'buyerWorkload', 'finance', 'receiving', 'gerencial', 'pipeline', 'stageAging', 'financialSummary', 'quickActions']) {
      expect(DASHBOARD_SECTION_HELP[key]).toBeTruthy();
      expect(DASHBOARD_SECTION_HELP[key].title).toMatch(/\S/);
    }
  });

  it('the legacy Bottleneck help entry was removed in B9.6 (replaced by canonical stageAging)', () => {
    expect(DASHBOARD_SECTION_HELP.bottlenecks).toBeUndefined();
    expect(DASHBOARD_SECTION_HELP.stageAging.interpretation).toMatch(/tempo na etapa atual, e não a idade do pedido/);
  });

  it('the Financial help is now canonical (B7): not temporary, explains currency separation + no FX', () => {
    expect(DASHBOARD_SECTION_HELP.financialSummary.temporary).toBeFalsy();
    expect(DASHBOARD_SECTION_HELP.financialSummary.interpretation).toMatch(/(não|nunca) são somados entre si/);
    expect(DASHBOARD_SECTION_HELP.financialSummary.caveat).toMatch(/conversão cambial/);
  });

  it('Team Load help carries the "not a productivity ranking" caveat', () => {
    expect(DASHBOARD_SECTION_HELP.buyerWorkload.caveat).toMatch(/não é um ranking de produtividade/i);
  });

  it('canonical Stage Aging help states age = time in current stage, not request age', () => {
    expect(DASHBOARD_SECTION_HELP.stageAging.interpretation).toMatch(/não a idade do pedido/);
    expect(DASHBOARD_SECTION_HELP.stageAging.caveat).toMatch(/SLA/);
  });

  it('Financial help (B7.3) covers current exposure vs paid history and refuses cross-measure summing', () => {
    expect(DASHBOARD_SECTION_HELP.financialSummary.measures).toMatch(/histórico/i);
    expect(DASHBOARD_SECTION_HELP.financialSummary.example).toMatch(/não devem ser somadas/);
    expect(DASHBOARD_SECTION_HELP.financialSummary.caveat).toMatch(/reembolsos não são deduzidos/);
  });

  it('Quick Actions help does not fabricate an analytical metric (no "measures"/"observe")', () => {
    expect(DASHBOARD_SECTION_HELP.quickActions.measures).toBeUndefined();
    expect(DASHBOARD_SECTION_HELP.quickActions.observe).toBeUndefined();
  });

  it('Pipeline help does not label a high count as a bottleneck', () => {
    expect(DASHBOARD_SECTION_HELP.pipeline.caveat).toMatch(/não é, por si só, um gargalo/);
  });

  it('help copy avoids backend terminology', () => {
    const all = JSON.stringify(DASHBOARD_SECTION_HELP);
    expect(all).not.toMatch(/RequestPoGroup|ActionClass|BuyerQueueProjectionBuilder|OperationalState/);
  });

  it('every header wires SectionInfo from the central map (no scattered prose)', () => {
    expect(personalSrc).toMatch(/<SectionInfo \{\.\.\.DASHBOARD_SECTION_HELP\.personal\}/);
    expect(buyerSrc).toMatch(/DASHBOARD_SECTION_HELP\.buyerPersonal/);
    expect(buyerSrc).toMatch(/DASHBOARD_SECTION_HELP\.buyerShared/);
    expect(buyerSrc).toMatch(/DASHBOARD_SECTION_HELP\.buyerWorkload/);
    expect(financeSrc).toMatch(/DASHBOARD_SECTION_HELP\.finance/);
    expect(receivingSrc).toMatch(/DASHBOARD_SECTION_HELP\.receiving/);
    expect(pipelineSrc).toMatch(/DASHBOARD_SECTION_HELP\.pipeline/);
    expect(stageAgingSrc).toMatch(/DASHBOARD_SECTION_HELP\.stageAging/);
    expect(financialSrc).toMatch(/DASHBOARD_SECTION_HELP\.financialSummary/);
    expect(quickSrc).toMatch(/DASHBOARD_SECTION_HELP\.quickActions/);
    expect(dashboardSrc).toMatch(/DASHBOARD_SECTION_HELP\.gerencial/);
  });

  it('does not add a SectionInfo to "Como funciona o processo" (the section is self-explanatory)', () => {
    // The expandable explainer needs no icon-explaining-an-explanation.
    const comoIdx = dashboardSrc.indexOf('Como funciona o processo');
    expect(comoIdx).toBeGreaterThan(-1);
    const around = dashboardSrc.slice(comoIdx - 200, comoIdx + 200);
    expect(around).not.toMatch(/SectionInfo/);
  });
});
