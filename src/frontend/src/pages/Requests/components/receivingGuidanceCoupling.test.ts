import { describe, it, expect } from 'vitest';
// v2.245.0 UI consistency (§11) — the "múltiplos grupos operacionais" message must be coupled to
// ACTUAL multi-unit, not to any projection guidance. Source guards (node-env vitest).
import edit from '../RequestEdit.tsx?raw';
import panel from './RequestStatusActionPanels.tsx?raw';

describe('single-vs-multi workflow guidance', () => {
  it('I/M: hideLegacyGuidance is driven ONLY by multiUnitGuidance (single unit no longer hides legacy)', () => {
    expect(edit).toMatch(/hideLegacyGuidance=\{!!multiUnitGuidance\}/);
    expect(edit).not.toMatch(/hideLegacyGuidance=\{!!multiUnitGuidance \|\| !!singleUnitGuidance\}/);
  });
  it('multiUnitGuidance itself is gated on more than one projection unit', () => {
    expect(edit).toMatch(/workflowProjection\.units\.length <= 1\) return null/);
  });
  it('J: when legacy guidance is shown, the panel renders responsible + next action', () => {
    expect(panel).toMatch(/!hideLegacyGuidance \?/);
    expect(panel).toMatch(/guidance\.responsible/);
    expect(panel).toMatch(/guidance\.nextAction/);
  });
  it('K: the "múltiplos grupos operacionais" message is the else-branch (multi only)', () => {
    expect(panel).toMatch(/este pedido possui múltiplos grupos operacionais/);
    // it lives after the ternary's ":" (else branch), i.e., only when legacy guidance is hidden,
    // which is now only true for real multi-unit requests.
    const elseIdx = panel.indexOf('múltiplos grupos operacionais');
    const condIdx = panel.indexOf('!hideLegacyGuidance ?');
    expect(condIdx).toBeGreaterThan(-1);
    expect(elseIdx).toBeGreaterThan(condIdx);
  });
});
