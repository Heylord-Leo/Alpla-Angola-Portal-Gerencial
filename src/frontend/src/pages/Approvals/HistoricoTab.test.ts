import { describe, it, expect } from 'vitest';
// Node-env vitest (no jsdom/RTL) — source-level guards for the v2.244.0 Phase 2 HISTÓRICO tab,
// its timeline drawer, and the approval-history API surface. (§26)
import tab from './tabs/HistoricoTab.tsx?raw';
import drawer from './tabs/ApprovalTimelineDrawer.tsx?raw';
import apiSrc from '../../lib/api.ts?raw';

describe('api.approvals history surface (v2.244.0 Phase 2)', () => {
  it('calls the read-only history, export and timeline endpoints', () => {
    expect(apiSrc).toMatch(/getHistory:\s*async/);
    expect(apiSrc).toMatch(/\/api\/v1\/approvals\/history\?/);
    expect(apiSrc).toMatch(/exportHistory:\s*async/);
    expect(apiSrc).toMatch(/\/api\/v1\/approvals\/history\/export\?/);
    expect(apiSrc).toMatch(/getTimeline:\s*async/);
    expect(apiSrc).toMatch(/\/api\/v1\/approvals\/history\/\$\{requestId\}/);
  });
  it('table and export share ONE filter builder (same filters/scope)', () => {
    expect(apiSrc).toMatch(/function buildApprovalHistoryParams/);
    // Both getHistory and exportHistory call the shared builder.
    expect((apiSrc.match(/buildApprovalHistoryParams\(q\)/g) || []).length).toBeGreaterThanOrEqual(2);
  });
  it('history/export/timeline are GET only (no mutation verbs on these calls)', () => {
    const slice = apiSrc.slice(apiSrc.indexOf('getHistory:'), apiSrc.indexOf('getTimeline:') + 400);
    expect(slice).not.toMatch(/method:\s*'(POST|PUT|DELETE|PATCH)'/);
  });
});

describe('HISTÓRICO tab (§26)', () => {
  it('loads from the history API (no client-only search)', () => {
    expect(tab).toMatch(/api\.approvals\.getHistory\(query\)/);
    // search is a server param, not an in-memory filter over one page
    expect(tab).toMatch(/search:\s*debouncedSearch \|\| undefined/);
    expect(tab).not.toMatch(/\.filter\(.*search/i);
  });
  it('defaults to a 30-day window and exposes a "Tudo" escape', () => {
    expect(tab).toMatch(/useState<DatePreset>\('30'\)/);
    expect(tab).toMatch(/preset === 'all'/);
  });
  it('maps every filter into the query object', () => {
    expect(tab).toMatch(/stage:\s*stage \|\| undefined/);
    expect(tab).toMatch(/decision:\s*decision \|\| undefined/);
    expect(tab).toMatch(/requestType:\s*requestType \|\| undefined/);
    expect(tab).toMatch(/dateFrom:\s*dateFromPreset\(preset\)/);
  });
  it('resets to page 1 whenever any filter changes', () => {
    expect(tab).toMatch(/setPage\(1\)/);
    expect(tab).toMatch(/\[debouncedSearch, stage, decision, requestType, preset, sort\]/);
  });
  it('paginates server-side (page state drives the query)', () => {
    expect(tab).toMatch(/page,\s*\n?\s*pageSize: PAGE_SIZE/);
    expect(tab).toMatch(/Página \{page\} de \{totalPages\}/);
    expect(tab).toMatch(/setPage\(p => Math\.min\(totalPages/);
  });
  it('is race-safe (monotonic request token discards stale responses)', () => {
    expect(tab).toMatch(/const reqToken = useRef\(0\)/);
    expect(tab).toMatch(/token === reqToken\.current/);
  });
  it('renders loading, error and empty states', () => {
    expect(tab).toMatch(/Carregando histórico/);
    expect(tab).toMatch(/Não foi possível carregar o histórico/);
    expect(tab).toMatch(/Nenhuma decisão encontrada/);
  });
  it('shows decision + stage badges and a result summary', () => {
    expect(tab).toMatch(/decisionStyle\(row\.decision\)/);
    expect(tab).toMatch(/decisões encontradas/);
  });
  it('a row opens the audit timeline drawer', () => {
    expect(tab).toMatch(/setTimelineFor\(\{ id: row\.requestId, number: row\.requestNumber \}\)/);
    expect(tab).toMatch(/<ApprovalTimelineDrawer/);
    expect(tab).toMatch(/role="button"/);
  });
  it('CSV export uses a blob download and preserves the active filters', () => {
    expect(tab).toMatch(/api\.approvals\.exportHistory\(\{ \.\.\.query/);
    expect(tab).toMatch(/createObjectURL\(blob\)/);
    expect(tab).toMatch(/a\.download = `approval-history-/);
  });
  it('export failure surfaces inline feedback, never alert()', () => {
    expect(tab).toMatch(/setExportError/);
    expect(tab).not.toMatch(/alert\(/);
  });
  it('does not call any approval mutation endpoint', () => {
    expect(tab).not.toMatch(/method:\s*'(POST|PUT|DELETE|PATCH)'/);
    // Guard the real mutation shapes (not the "Rejeitado"/"REJECTED" decision labels).
    expect(tab).not.toMatch(/\/(area|final)-approval\//);
    expect(tab).not.toMatch(/api\.\w+\.(approve|reject)\b/i);
  });
});

describe('audit timeline drawer (§25 UI)', () => {
  it('fetches the read-only timeline and is race-safe + Esc-closable', () => {
    expect(drawer).toMatch(/api\.approvals\.getTimeline\(requestId\)/);
    expect(drawer).toMatch(/let alive = true/);
    expect(drawer).toMatch(/ev\.key === 'Escape'/);
  });
  it('renders actor, timestamp, comment and status transition, chronological as delivered', () => {
    expect(drawer).toMatch(/e\.actorName/);
    expect(drawer).toMatch(/toLocaleString\('pt-PT'\)/);
    expect(drawer).toMatch(/e\.comment/);
    expect(drawer).toMatch(/e\.previousStatusCode.*e\.newStatusCode|previousStatusCode.*newStatusCode/);
    // No client-side re-sort — the backend already orders ascending.
    expect(drawer).not.toMatch(/\.sort\(/);
  });
  it('is read-only (no mutation verbs)', () => {
    expect(drawer).not.toMatch(/method:\s*'(POST|PUT|DELETE|PATCH)'/);
  });
});
