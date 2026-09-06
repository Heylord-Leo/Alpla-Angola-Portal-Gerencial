import { describe, it, expect } from 'vitest';
import {
  alertDomainLabel,
  alertPlaneMeta,
  alertSeverityMeta,
  alertUrgencyText,
  alertSummaryText,
  alertPreviewFooterText,
  alertBackendTruncationText,
} from './alertsView';

// Dashboard V2 B8 — pure presentation helpers. Behavioral tests (no DOM): these functions only format
// server values; they must never invent semantics.
describe('alertsView — domain labels', () => {
  it('maps BUYER → Compras and FINANCE → Finanças (raw code never surfaces)', () => {
    expect(alertDomainLabel('BUYER')).toBe('Compras');
    expect(alertDomainLabel('FINANCE')).toBe('Finanças');
  });
  it('falls back to the raw code for an unknown domain (no crash)', () => {
    expect(alertDomainLabel('SOMETHING_NEW')).toBe('SOMETHING_NEW');
  });
});

describe('alertsView — plane labels', () => {
  it('maps the three planes to their PT labels', () => {
    expect(alertPlaneMeta('PESSOAL').label).toBe('Pessoal');
    expect(alertPlaneMeta('COMPARTILHADO').label).toBe('Compartilhado');
    expect(alertPlaneMeta('GERENCIAL').label).toBe('Gerencial');
  });
  it('each plane has a chip color', () => {
    expect(alertPlaneMeta('PESSOAL').color).toMatch(/^#/);
    expect(alertPlaneMeta('COMPARTILHADO').color).toMatch(/^#/);
    expect(alertPlaneMeta('GERENCIAL').color).toMatch(/^#/);
  });
});

describe('alertsView — severity', () => {
  it('CRITICAL → Crítico (isCritical) and ATTENTION → Atenção', () => {
    const crit = alertSeverityMeta('CRITICAL');
    const att = alertSeverityMeta('ATTENTION');
    expect(crit.label).toBe('Crítico');
    expect(crit.isCritical).toBe(true);
    expect(att.label).toBe('Atenção');
    expect(att.isCritical).toBe(false);
  });
  it('provides a distinct color per severity (a non-color cue, the label, always accompanies it)', () => {
    expect(alertSeverityMeta('CRITICAL').color).not.toBe(alertSeverityMeta('ATTENTION').color);
  });
});

describe('alertsView — urgency wording from server daysDelta', () => {
  it('overdue uses "Vencido há X dias" (singular at 1)', () => {
    expect(alertUrgencyText(-1)).toBe('Vencido há 1 dia');
    expect(alertUrgencyText(-5)).toBe('Vencido há 5 dias');
  });
  it('today / tomorrow / +2', () => {
    expect(alertUrgencyText(0)).toBe('Vence hoje');
    expect(alertUrgencyText(1)).toBe('Vence amanhã');
    expect(alertUrgencyText(2)).toBe('Vence em 2 dias');
  });
});

describe('alertsView — summary text (from counts, never the list)', () => {
  it('formats "N críticos · M em atenção"', () => {
    expect(alertSummaryText(3, 5)).toBe('3 críticos · 5 em atenção');
  });
  it('singular "crítico" at 1', () => {
    expect(alertSummaryText(1, 0)).toBe('1 crítico · 0 em atenção');
  });
  it('zero state is honest', () => {
    expect(alertSummaryText(0, 0)).toBe('0 críticos · 0 em atenção');
  });
});

describe('alertsView — preview footer (Dashboard)', () => {
  it('states the honest shown/total ratio for the preview', () => {
    expect(alertPreviewFooterText(6, 125)).toBe('Exibindo 6 de 125 alertas ativos.');
  });
});

describe('alertsView — backend truncation notice (drawer)', () => {
  it('states the honest returned/total ratio so the drawer is never implied complete', () => {
    expect(alertBackendTruncationText(100, 125)).toBe('A API retornou os 100 alertas mais prioritários de 125 ativos.');
  });
});
