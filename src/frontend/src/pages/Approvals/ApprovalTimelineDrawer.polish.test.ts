import { describe, it, expect } from 'vitest';
// Source guards for the v2.244.0 Phase-2 timeline-polish wiring (grouping usage, decision hierarchy,
// expandable details, human comment presentation) + the Reenviadas summary line.
import drawer from './tabs/ApprovalTimelineDrawer.tsx?raw';
import tab from './tabs/HistoricoTab.tsx?raw';

describe('ApprovalTimelineDrawer — polish wiring', () => {
  it('uses the pure grouping helper (presentation-only), not an inline reorder', () => {
    expect(drawer).toMatch(/groupTimelineEvents\(events\)/);
    expect(drawer).not.toMatch(/\.sort\(/);
  });
  it('renders a collapsible group with item count + Ver detalhes', () => {
    expect(drawer).toMatch(/itens enviados para aprovação/);
    expect(drawer).toMatch(/Ver detalhes/);
    expect(drawer).toMatch(/aria-expanded=\{open\}/);
  });
  it('expanded group preserves each original comment (CommentBlock per event, stable keys)', () => {
    expect(drawer).toMatch(/node\.events\.map\(ev =>/);
    expect(drawer).toMatch(/key=\{ev\.id\}/);
    expect(drawer).toMatch(/<CommentBlock comment=\{ev\.comment\}/);
  });
  it('decisions read louder than context events (weight/size branch on isDecision)', () => {
    expect(drawer).toMatch(/e\.isDecision \? '0\.9rem' : '0\.82rem'/);
    expect(drawer).toMatch(/e\.isDecision \? 800 : 600/);
  });
  it('known system comments show PT text with a raw "detalhes técnicos" toggle', () => {
    expect(drawer).toMatch(/localizeComment/);
    expect(drawer).toMatch(/detalhes técnicos/);
    expect(drawer).toMatch(/pres\.raw/);
  });
  it('keeps the status transition as secondary context', () => {
    expect(drawer).toMatch(/function StatusTransition/);
    expect(drawer).toMatch(/prev \?\? '—'\} → \{next \?\? '—'/);
  });
  it('remains read-only', () => {
    expect(drawer).not.toMatch(/method:\s*'(POST|PUT|DELETE|PATCH)'/);
  });
});

describe('HistoricoTab — Reenviadas summary (§11)', () => {
  it('L: shows Reenviadas from the backend DTO count when > 0', () => {
    expect(tab).toMatch(/data\.resubmittedCount > 0/);
    expect(tab).toMatch(/Reenviadas:/);
    expect(tab).toMatch(/\{data\.resubmittedCount\}/);
  });
});
