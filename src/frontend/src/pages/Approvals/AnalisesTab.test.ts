import { describe, it, expect } from 'vitest';
import { formatDuration } from './tabs/formatDuration';
import tab from './tabs/AnalisesTab.tsx?raw';
import apiSrc from '../../lib/api.ts?raw';

// v2.244.0 Phase 3 — duration formatter unit tests + ANÁLISES source guards (§30).

describe('formatDuration (§21)', () => {
  it('renders seconds, minutes, hours and days human-readably', () => {
    expect(formatDuration(45)).toBe('45s');
    expect(formatDuration(15 * 60)).toBe('15 min');
    expect(formatDuration(2 * 3600 + 35 * 60)).toBe('2h 35m');
    expect(formatDuration(3 * 3600)).toBe('3h');
    expect(formatDuration(28 * 3600)).toBe('1d 4h');
    expect(formatDuration(3 * 86400 + 12 * 3600)).toBe('3d 12h');
  });
  it('handles null / NaN safely', () => {
    expect(formatDuration(null)).toBe('—');
    expect(formatDuration(undefined)).toBe('—');
    expect(formatDuration(Number.NaN)).toBe('—');
  });
});

describe('api.approvals.getAnalytics', () => {
  it('calls the read-only analytics endpoint (GET, no mutation)', () => {
    expect(apiSrc).toMatch(/getAnalytics:\s*async/);
    expect(apiSrc).toMatch(/\/api\/v1\/approvals\/analytics\?/);
    const slice = apiSrc.slice(apiSrc.indexOf('getAnalytics:'), apiSrc.indexOf('getAnalytics:') + 700);
    expect(slice).not.toMatch(/method:\s*'(POST|PUT|DELETE|PATCH)'/);
  });
});

describe('ANÁLISES tab (§30)', () => {
  it('loads from the analytics API and never recomputes metrics client-side', () => {
    expect(tab).toMatch(/api\.approvals\.getAnalytics\(query\)/);
    // Durations are formatted (formatDuration), rates come straight from the DTO — no manual math.
    expect(tab).toMatch(/formatDuration\(data\.duration\.averageSeconds\)/);
    expect(tab).not.toMatch(/reduce\(/); // no client-side aggregation of raw samples
  });
  it('defaults to a 30-day window and picks resolution by range', () => {
    expect(tab).toMatch(/useState<DatePreset>\('30'\)/);
    expect(tab).toMatch(/function resolutionFor/);
    expect(tab).toMatch(/p === '90'.*'week'|'week'/);
  });
  it('maps filters into the analytics query', () => {
    expect(tab).toMatch(/dateFrom:\s*dateFromPreset\(preset\)/);
    expect(tab).toMatch(/resolution:\s*resolutionFor\(preset\)/);
    expect(tab).toMatch(/requestType:\s*requestType \|\| undefined/);
  });
  it('is race-safe and handles loading / error / empty states', () => {
    expect(tab).toMatch(/const reqToken = useRef\(0\)/);
    expect(tab).toMatch(/token === reqToken\.current/);
    expect(tab).toMatch(/Carregando análises/);
    expect(tab).toMatch(/Não foi possível carregar as análises/);
    expect(tab).toMatch(/Sem dados de aprovação neste período/);
    expect(tab).not.toMatch(/alert\(/);
  });
  it('renders KPIs incl. a P90 explanation, Área-vs-Final, decision mix, trend and approver table', () => {
    expect(tab).toMatch(/Tempo médio/);
    expect(tab).toMatch(/P90: 90% das aprovações/);
    expect(tab).toMatch(/Tempo por etapa \(gargalo\)/);
    expect(tab).toMatch(/Aprovação de Área/);
    expect(tab).toMatch(/Aprovação Final/);
    expect(tab).toMatch(/Resultado das decisões/);
    expect(tab).toMatch(/function Trend/);
    expect(tab).toMatch(/Aprovadores/);
  });
  it('approver speed is stage-wait, framed as workload not ranking', () => {
    expect(tab).toMatch(/averageDecisionSeconds/);
    expect(tab).toMatch(/espera na etapa do aprovador/);
    expect(tab).toMatch(/não ranking/);
  });
  it('charts carry text labels, not colour-only meaning (accessible)', () => {
    expect(tab).toMatch(/role="img" aria-label=/);
    expect(tab).toMatch(/formatDuration\(secs\)/); // bar value shown as text
  });
});
