import { describe, it, expect } from 'vitest';
// Node-env vitest (no jsdom/RTL) — pure-function unit tests for the tab hook + source-level guards
// for the v2.244.0 Approval Center V2 tab shell / PENDENTES re-home. No backend or business-rule
// behavior is exercised here; those live unchanged in the moved PENDENTES tab.
import { normalizeApprovalTab, APPROVAL_TABS, DEFAULT_APPROVAL_TAB } from './hooks/useApprovalTab';

import shell from './ApprovalCenter.tsx?raw';
import hook from './hooks/useApprovalTab.ts?raw';
import pendentes from './tabs/PendentesTab.tsx?raw';
import historico from './tabs/HistoricoTab.tsx?raw';
import analises from './tabs/AnalisesTab.tsx?raw';

describe('useApprovalTab — tab normalization (invalid → PENDENTES)', () => {
  it('F: unknown / absent value falls back to PENDENTES', () => {
    expect(DEFAULT_APPROVAL_TAB).toBe('PENDENTES');
    expect(normalizeApprovalTab(null)).toBe('PENDENTES');
    expect(normalizeApprovalTab(undefined)).toBe('PENDENTES');
    expect(normalizeApprovalTab('')).toBe('PENDENTES');
    expect(normalizeApprovalTab('bogus')).toBe('PENDENTES');
  });
  it('A: default (no ?tab) resolves to PENDENTES', () => {
    expect(normalizeApprovalTab(null)).toBe('PENDENTES');
  });
  it('B/C: HISTORICO and ANALISES select their tab (case-insensitive)', () => {
    expect(normalizeApprovalTab('HISTORICO')).toBe('HISTORICO');
    expect(normalizeApprovalTab('historico')).toBe('HISTORICO');
    expect(normalizeApprovalTab('ANALISES')).toBe('ANALISES');
    expect(normalizeApprovalTab('analises')).toBe('ANALISES');
    expect(normalizeApprovalTab('PENDENTES')).toBe('PENDENTES');
  });
  it('exposes exactly the three ordered tabs', () => {
    expect([...APPROVAL_TABS]).toEqual(['PENDENTES', 'HISTORICO', 'ANALISES']);
  });
});

describe('useApprovalTab — URL sync mechanics', () => {
  it('D/E: derives the tab from useSearchParams each render (no read-once useMemo([]) bug)', () => {
    expect(hook).toMatch(/useSearchParams/);
    // The active tab is a plain derivation on every render (read-then-normalize), not memoized on []
    expect(hook).toMatch(/const tab = normalizeApprovalTab\(searchParams\.get\('tab'\)\)/);
  });
  it('E: navigates via setSearchParams (history push → Back/Forward), never a reload', () => {
    expect(hook).toMatch(/setSearchParams\(params\)/);
    expect(hook).not.toMatch(/window\.location/);
    expect(hook).not.toMatch(/location\.href/);
    expect(hook).not.toMatch(/location\.reload/);
  });
  it('keeps the default tab URL clean (deletes ?tab for PENDENTES)', () => {
    expect(hook).toMatch(/params\.delete\('tab'\)/);
    expect(hook).toMatch(/params\.set\('tab', next\)/);
  });
});

describe('ApprovalCenter shell — three tabs, accessible, default PENDENTES', () => {
  it('renders an accessible tablist with the three tabs', () => {
    expect(shell).toMatch(/role="tablist"/);
    expect(shell).toMatch(/role="tab"/);
    expect(shell).toMatch(/aria-selected=\{active\}/);
    expect(shell).toMatch(/role="tabpanel"/);
    expect(shell).toMatch(/PENDENTES/);
    expect(shell).toMatch(/HISTORICO/);
    expect(shell).toMatch(/ANALISES/);
  });
  it('drives the active tab from the URL-synced hook and mounts only the active tab', () => {
    expect(shell).toMatch(/useApprovalTab\(\)/);
    expect(shell).toMatch(/activeTab === 'PENDENTES' && <PendentesTab \/>/);
    expect(shell).toMatch(/activeTab === 'HISTORICO' && <HistoricoTab \/>/);
    expect(shell).toMatch(/activeTab === 'ANALISES' && <AnalisesTab \/>/);
  });
  it('keeps the page identity (header + tour) and both exports', () => {
    expect(shell).toMatch(/Centro de Aprovações/);
    expect(shell).toMatch(/GuidedTourContextButton/);
    expect(shell).toMatch(/export function ApprovalCenter/);
    expect(shell).toMatch(/export default ApprovalCenter/);
  });
});

describe('PENDENTES tab — preserved behavior (H..O, R)', () => {
  it('H: still consumes the pending-approvals API (no backend change)', () => {
    expect(pendentes).toMatch(/api\.requests\.getPendingApprovals\(\)/);
    expect(pendentes).toMatch(/fetchPendingContractApprovals\(\)/);
    expect(pendentes).toMatch(/api\.lookups\.getPendingSupplierApprovals\(\)/);
  });
  it('I: Area and Final stages remain distinct sections with their own counts', () => {
    expect(pendentes).toMatch(/Aguardando minha aprovação de área/);
    expect(pendentes).toMatch(/Aguardando minha aprovação final/);
    expect(pendentes).toMatch(/data\?\.areaApprovals\?\.length/);
    expect(pendentes).toMatch(/data\?\.finalApprovals\?\.length/);
  });
  it('J: search is preserved', () => {
    expect(pendentes).toMatch(/searchValue=\{searchQuery\}/);
    expect(pendentes).toMatch(/onSearchChange=\{setSearchQuery\}/);
  });
  it('K: sort modes (urgência / mais antigo / maior valor) preserved', () => {
    expect(pendentes).toMatch(/sort_default/);
    expect(pendentes).toMatch(/sort_oldest/);
    expect(pendentes).toMatch(/sort_value/);
  });
  it('L: urgent / alert / area / final filters preserved', () => {
    expect(pendentes).toMatch(/filter_urgent/);
    expect(pendentes).toMatch(/filter_has_alert/);
    expect(pendentes).toMatch(/filter_area_only/);
    expect(pendentes).toMatch(/filter_final_only/);
  });
  it('M: drawer opens the exact (requestId, approvalBatchId, stage) context', () => {
    expect(pendentes).toMatch(/setSelectedApprovalBatchId\(item\.approvalBatchId \?\? null\)/);
    expect(pendentes).toMatch(/setSelectedApprovalStage\(item\.approvalStage === 'FINAL' \? 'FINAL' : 'AREA'\)/);
    expect(pendentes).toMatch(/activeBatchId=\{selectedApprovalBatchId\}/);
    expect(pendentes).toMatch(/approvalStage=\{selectedApprovalStage \|\| 'AREA'\}/);
  });
  it('N: supplier ficha approvals preserved', () => {
    expect(pendentes).toMatch(/SupplierApprovalPanel/);
    expect(pendentes).toMatch(/Fichas de Fornecedor/);
    expect(pendentes).toMatch(/dgApproveSupplier/);
  });
  it('O: contract approvals preserved', () => {
    expect(pendentes).toMatch(/ContractApprovalPanel/);
    expect(pendentes).toMatch(/Contratos Pendentes/);
    expect(pendentes).toMatch(/contractTechnicalApprove/);
    expect(pendentes).toMatch(/contractFinalApprove/);
  });
  it('R: no approval mutation is issued from the queue (decisions stay in the drawer panels)', () => {
    // The queue tab must not directly call approve/reject batch endpoints.
    expect(pendentes).not.toMatch(/api\.requests\.approve/i);
    expect(pendentes).not.toMatch(/batches\/.+\/approve/);
  });
});

describe('PENDENTES tab — compact operational layout (P, Q) + aging', () => {
  it('P: queue uses a bounded internal scroll area (no page-long wall)', () => {
    expect(pendentes).toMatch(/QUEUE_SCROLL_MAX_HEIGHT/);
    expect(pendentes).toMatch(/overflowY: 'auto'/);
  });
  it('Q: every actionable row is rendered (map over requests, no slice/first-N truncation)', () => {
    expect(pendentes).toMatch(/requests\.map\(\(req, i\)/);
    expect(pendentes).not.toMatch(/requests\.slice\(/);
  });
  it('surfaces aging derived from the existing createdAtUtc (no new backend)', () => {
    expect(pendentes).toMatch(/function formatAge/);
    expect(pendentes).toMatch(/const age = formatAge\(req\.createdAtUtc\)/);
    expect(pendentes).toMatch(/Mais antigo/);
  });
  it('rows are keyboard-accessible (role=button + Enter/Space)', () => {
    expect(pendentes).toMatch(/role="button"/);
    expect(pendentes).toMatch(/onKeyDown=/);
  });
});

describe('tabs — HISTÓRICO (Phase 2) + ANÁLISES (Phase 3) implemented, read-only', () => {
  it('HISTÓRICO consumes the read-only history API and no mutation endpoint', () => {
    expect(historico).toMatch(/api\.approvals\.getHistory/);
    expect(historico).not.toMatch(/method:\s*'(POST|PUT|DELETE|PATCH)'/);
    expect(historico).not.toMatch(/\.approve\(|\.reject\(/);
  });
  it('ANÁLISES consumes the read-only analytics API and no mutation endpoint', () => {
    expect(analises).toMatch(/api\.approvals\.getAnalytics/);
    expect(analises).not.toMatch(/method:\s*'(POST|PUT|DELETE|PATCH)'/);
    expect(analises).not.toMatch(/\/(area|final)-approval\//);
  });
});
