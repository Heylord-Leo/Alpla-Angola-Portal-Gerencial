import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';

// v2.244.0 Approval Center V2 — URL-synchronized tab state for /approvals.
// The active tab is DERIVED directly from useSearchParams on every render (never read once with
// useMemo([])) so it is inherently: URL-reactive on same-route navigation, refresh-safe, and
// Back/Forward-safe (the browser mutates the URL → useSearchParams re-renders → the derived tab
// follows). An unknown/absent value falls back to PENDENTES. No remount hack, no reload.

export const APPROVAL_TABS = ['PENDENTES', 'HISTORICO', 'ANALISES'] as const;
export type ApprovalTab = typeof APPROVAL_TABS[number];

export const DEFAULT_APPROVAL_TAB: ApprovalTab = 'PENDENTES';

export function normalizeApprovalTab(raw: string | null | undefined): ApprovalTab {
  const upper = (raw ?? '').toUpperCase();
  return (APPROVAL_TABS as readonly string[]).includes(upper) ? (upper as ApprovalTab) : DEFAULT_APPROVAL_TAB;
}

export function useApprovalTab(): [ApprovalTab, (tab: ApprovalTab) => void] {
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = normalizeApprovalTab(searchParams.get('tab'));

  const setTab = useCallback((next: ApprovalTab) => {
    const params = new URLSearchParams(searchParams);
    // PENDENTES is the default: keep the URL clean (/approvals) so it stays the canonical entry.
    if (next === DEFAULT_APPROVAL_TAB) params.delete('tab');
    else params.set('tab', next);
    // Push (default) so Back/Forward walks the tab history; same-route, no reload.
    setSearchParams(params);
  }, [searchParams, setSearchParams]);

  return [tab, setTab];
}
