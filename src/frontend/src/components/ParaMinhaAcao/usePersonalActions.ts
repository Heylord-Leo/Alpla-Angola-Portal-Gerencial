import { useState, useEffect, useCallback, useRef } from 'react';
import { api } from '../../lib/api';
import { MyActionCategory, PersonalActionItem } from '../../types/myActions';

const PAGE_SIZE = 20;

/** v2.243.0 Phase 2 — an optional deep-link target passed to the hook on mount. */
export interface MyActionsTarget {
  actionType: string | null;
  requestId?: string;
  poGroupId?: string;
}

export interface UsePersonalActionsState {
  /** Selected category actionType, or null for the synthetic "Todos". */
  actionType: string | null;
  categories: MyActionCategory[];
  items: PersonalActionItem[];
  page: number;
  totalPages: number;
  totalCount: number;
  loading: boolean;      // initial / category-switch load
  loadingMore: boolean;  // "Carregar mais"
  error: boolean;
  hasMore: boolean;
  select: (actionType: string | null, target?: MyActionsTarget | null) => void;
  loadMore: () => void;
  retry: () => void;
  /** Clears the pending deep-link target so subsequent page-1 fetches no longer forward target params. */
  clearTarget: () => void;
}

/**
 * v2.243.0 — data hook for Para Minha Ação V2. The backend owns categories, priority ordering and
 * per-category pagination; this hook only accumulates pages ("Carregar mais"), switches category, and
 * (Phase 2) forwards a deep-link target on the initial page-1 fetch so the backend resolves the page
 * containing the target. Race-safe via a monotonic token; StrictMode-safe via a re-armed mountedRef.
 *
 * <para>The pending target is consumed until <c>clearTarget()</c> is called (after the component has
 * resolved/scrolled/highlighted it) or the user manually switches category — so React StrictMode's
 * double initial-invoke keeps requesting the target page on BOTH invocations (the frontend never
 * computes the target page itself).</para>
 */
export function usePersonalActions(initialTarget?: MyActionsTarget): UsePersonalActionsState {
  // A deep-link may be category-only (action, no requestId → select the category) OR a target
  // (action + requestId → also forward target params so the backend resolves the target's page).
  const hasTarget = !!(initialTarget && initialTarget.requestId);
  const [actionType, setActionType] = useState<string | null>(initialTarget?.actionType ?? null);
  const [categories, setCategories] = useState<MyActionCategory[]>([]);
  const [items, setItems] = useState<PersonalActionItem[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState(false);

  const tokenRef = useRef(0);
  const mountedRef = useRef(true);
  const pendingTargetRef = useRef<MyActionsTarget | null>(hasTarget ? initialTarget! : null);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const fetchPage = useCallback(async (type: string | null, targetPage: number, append: boolean) => {
    const token = ++tokenRef.current;
    if (append) setLoadingMore(true); else { setLoading(true); setError(false); }
    // Forward the deep-link target only on an initial (non-append) fetch, while it is still pending.
    // The backend returns the page containing the target — the frontend never computes it.
    const t = !append ? pendingTargetRef.current : null;
    try {
      const res = await api.requests.myActions({
        actionType: type ?? undefined,
        page: targetPage,
        pageSize: PAGE_SIZE,
        sort: 'priority',
        targetRequestId: t?.requestId,
        targetPoGroupId: t?.poGroupId,
        targetActionType: t?.actionType ?? undefined,
      });
      if (!mountedRef.current || token !== tokenRef.current) return;
      setCategories(res.categories ?? []);
      setTotalPages(res.totalPages ?? 1);
      setTotalCount(res.totalCount ?? 0);
      setPage(res.page ?? targetPage);
      // Render in backend order; never re-sort. Append for load-more, replace otherwise (including the
      // backend-resolved target page — earlier pages are NOT synthesized in front of it).
      setItems(prev => append ? [...prev, ...(res.items ?? [])] : (res.items ?? []));
    } catch {
      if (!mountedRef.current || token !== tokenRef.current) return;
      if (!append) { setItems([]); setTotalCount(0); setTotalPages(1); }
      setError(true);
    } finally {
      if (mountedRef.current && token === tokenRef.current) { setLoading(false); setLoadingMore(false); }
    }
  }, []);

  useEffect(() => { fetchPage(actionType, 1, false); }, [actionType, fetchPage]);

  // v2.243.0 Phase 3 — URL-reactive selection. Category-only switch resets + refetches page 1; passing
  // a `target` (same-route deep-link) arms the target params and forces a reload even when the category
  // is unchanged, so a BuyerQueue link into the already-open category still resolves the target.
  const select = useCallback((next: string | null, target?: MyActionsTarget | null) => {
    pendingTargetRef.current = target ?? null;
    if (next === actionType) {
      if (!target) return; // same category, no target → nothing to do
      setItems([]);
      setPage(1);
      fetchPage(next, 1, false); // same category + new target → refetch (forwards target params)
      return;
    }
    setItems([]);
    setPage(1);
    setActionType(next); // different category → the [actionType] effect fetches (forwards target params)
  }, [actionType, fetchPage]);

  const loadMore = useCallback(() => {
    if (loading || loadingMore) return;
    if (page >= totalPages) return;
    fetchPage(actionType, page + 1, true);
  }, [loading, loadingMore, page, totalPages, actionType, fetchPage]);

  const retry = useCallback(() => { fetchPage(actionType, 1, false); }, [actionType, fetchPage]);
  const clearTarget = useCallback(() => { pendingTargetRef.current = null; }, []);

  return {
    actionType, categories, items, page, totalPages, totalCount,
    loading, loadingMore, error,
    hasMore: page < totalPages,
    select, loadMore, retry, clearTarget,
  };
}
