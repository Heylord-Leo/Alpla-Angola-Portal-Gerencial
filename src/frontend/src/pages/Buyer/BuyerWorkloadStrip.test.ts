import { describe, it, expect } from 'vitest';
import src from './BuyerWorkloadStrip.tsx?raw';

// Buyer-list workload distribution strip: managerial-only, server-sourced, click-to-filter.
describe('BuyerWorkloadStrip — structure', () => {
  it('renders nothing when the server returns no workload plane (managerial visibility only)', () => {
    expect(src).toMatch(/if \(!workload\) return null/);
  });

  it('uses server workload counts (no client recomputation)', () => {
    expect(src).toMatch(/api\.dashboardV2\.getBuyer\(/);
    expect(src).toMatch(/displayWorkloadRows\(workload\.rows\)/);
    expect(src).toMatch(/actionableRequests/);
  });

  it('passes the list structural filters through to the server', () => {
    expect(src).toMatch(/getBuyer\(\{ company, plant, department, needLevel \}\)/);
  });

  it('exposes buyer + unassigned selection and a clear affordance', () => {
    expect(src).toMatch(/onSelectBuyer/);
    expect(src).toMatch(/onSelectUnassigned/);
    expect(src).toMatch(/onClear/);
    expect(src).toMatch(/Sem atribuição/);
  });

  it('marks the active filter (aria-pressed)', () => {
    expect(src).toMatch(/aria-pressed=\{active\}/);
  });

  it('uses only defined text tokens (dark-mode safe — no undefined var(--color-text))', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
  });
});
