import { useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties } from 'react';
import { useSearchParams, useNavigate, useLocation } from 'react-router-dom';
import { usePersonalActions, MyActionsTarget } from './usePersonalActions';
import { resolveTargetItem, normalizeId } from './targetMatch';
import { PersonalActionCard } from './PersonalActionCard';
import { ListChecks, Inbox, RefreshCw, Loader2, Info } from 'lucide-react';

// v2.243.0 Para Minha Ação V2 — compact personal operational queue + URL-reactive deep-link targeting.
// The category and target are driven by the URL search params (useSearchParams), so SAME-ROUTE
// navigation (/requests → /requests?action=…) updates the category without a remount. The backend
// resolves the target's page; the frontend scrolls to and softly highlights it (~5s, reduced-motion
// safe), then cleans the transient ids from the URL (keeping action=<category>). Backend owns
// categories, priority ordering and paging; the frontend never computes the target page or re-sorts.

const QUEUE_MAX_HEIGHT = 420;
const HIGHLIGHT_MS = 5000;

const filterBase: CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 6, padding: '6px 12px', borderRadius: 8,
  border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', cursor: 'pointer',
  fontSize: '0.78rem', fontWeight: 700, color: 'var(--color-text-secondary, var(--color-text-main))', whiteSpace: 'nowrap',
};

function CountBadge({ n, active }: { n: number; active: boolean }) {
  return (
    <span style={{
      minWidth: 20, textAlign: 'center', padding: '0 6px', borderRadius: 999, fontSize: '0.7rem',
      fontWeight: 800, background: active ? 'rgba(255,255,255,0.24)' : 'color-mix(in srgb, var(--color-text-muted) 16%, transparent)',
      color: active ? '#fff' : 'var(--color-text-muted)',
    }}>{n}</span>
  );
}

const targetKeyOf = (t?: MyActionsTarget | null): string | null =>
  t?.requestId ? `${normalizeId(t.requestId)}|${normalizeId(t.poGroupId)}|${t.actionType ?? ''}` : null;

export function ParaMinhaAcaoV2() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const location = useLocation();

  // Initial URL (first render) — seeds the hook's first load; refresh/direct-load path.
  const initialTarget = useMemo<MyActionsTarget | undefined>(() => {
    const action = searchParams.get('action');
    if (!action) return undefined;
    const requestId = searchParams.get('requestId') || undefined;
    const poGroupId = searchParams.get('poGroupId') || undefined;
    return { actionType: action, requestId, poGroupId };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const s = usePersonalActions(initialTarget);
  const todosCount = s.categories.reduce((sum, c) => sum + (c.count ?? 0), 0);
  const headerTotal = s.actionType ? s.totalCount : todosCount;

  const prefersReduced = useRef(typeof window !== 'undefined' && !!window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches);
  const cardRefs = useRef(new Map<string, HTMLDivElement>());
  const registerCard = (id: string) => (el: HTMLDivElement | null) => {
    if (el) cardRefs.current.set(id, el); else cardRefs.current.delete(id);
  };

  const [targetedId, setTargetedId] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => () => { if (timerRef.current) clearTimeout(timerRef.current); }, []);

  // Refs seeded from the initial URL so the hook's own initial load is NOT duplicated on mount, and so
  // the URL-reactive effect only acts on genuine CHANGES (never on the cleanup transition).
  const appliedCatRef = useRef<string | null>(initialTarget?.actionType ?? null);
  const pendingResolveRef = useRef<MyActionsTarget | null>(initialTarget?.requestId ? initialTarget : null);
  const pendingKeyRef = useRef<string | null>(targetKeyOf(initialTarget));
  const handledTargetKeyRef = useRef<string | null>(null);

  // Remove ONLY the transient TARGET ids (keep action=<category> as shareable state). No reload.
  const cleanUrl = () => {
    const next = new URLSearchParams(location.search);
    let changed = false;
    for (const k of ['requestId', 'poGroupId']) if (next.has(k)) { next.delete(k); changed = true; }
    if (changed) navigate({ pathname: location.pathname, search: next.toString() }, { replace: true });
  };

  // URL-REACTIVE: react to same-route search-param changes (and the initial load).
  useEffect(() => {
    const action = searchParams.get('action');
    const requestId = searchParams.get('requestId') || undefined;
    const poGroupId = searchParams.get('poGroupId') || undefined;
    const cat = action ?? null;

    if (appliedCatRef.current !== cat) {
      // Category changed (incl. Todos↔category). Load the new category, forwarding a target if present.
      appliedCatRef.current = cat;
      setNotFound(false);
      const tgt: MyActionsTarget | undefined = (action && requestId) ? { actionType: cat, requestId, poGroupId } : undefined;
      if (tgt) { pendingResolveRef.current = tgt; pendingKeyRef.current = targetKeyOf(tgt); }
      s.select(cat, tgt);
      return;
    }
    // Same category, but a NEW target arrived (e.g., BuyerQueue link into the already-open category).
    if (requestId) {
      const key = targetKeyOf({ actionType: cat, requestId, poGroupId });
      if (key && key !== handledTargetKeyRef.current && key !== pendingKeyRef.current) {
        const tgt: MyActionsTarget = { actionType: cat, requestId, poGroupId };
        pendingResolveRef.current = tgt;
        pendingKeyRef.current = key;
        setNotFound(false);
        s.select(cat, tgt);
      }
    }
    // Cleanup transition (ids removed, cat unchanged) → neither branch fires → no reload / no loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  // Resolve the pending TARGET once its (backend-resolved) page has loaded — once per unique target.
  useEffect(() => {
    const target = pendingResolveRef.current;
    if (!target?.requestId || s.loading || s.error) return;
    const key = targetKeyOf(target);
    if (!key || key === handledTargetKeyRef.current) return;
    handledTargetKeyRef.current = key;
    pendingResolveRef.current = null;

    const match = resolveTargetItem(s.items, target);
    if (match) {
      setTargetedId(match.actionId);
      requestAnimationFrame(() => {
        cardRefs.current.get(match.actionId)?.scrollIntoView({ block: 'center', behavior: prefersReduced.current ? 'auto' : 'smooth' });
      });
      if (timerRef.current) clearTimeout(timerRef.current);
      timerRef.current = setTimeout(() => setTargetedId(null), HIGHLIGHT_MS);
    } else {
      setNotFound(true);
    }
    cleanUrl();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [s.loading, s.error, s.items]);

  // Manual chip → drive the URL (single source of truth; enables Back/Forward). The URL effect loads.
  const selectCategory = (value: string | null) => {
    const next = new URLSearchParams(location.search);
    if (value) next.set('action', value); else next.delete('action');
    next.delete('requestId'); next.delete('poGroupId');
    navigate({ pathname: location.pathname, search: next.toString() });
  };

  const Filter = ({ label, value, count }: { label: string; value: string | null; count: number }) => {
    const active = s.actionType === value;
    return (
      <button
        type="button" role="tab" aria-selected={active}
        onClick={() => selectCategory(value)}
        style={{ ...filterBase, ...(active ? { background: 'var(--color-primary)', color: '#fff', borderColor: 'var(--color-primary)' } : {}) }}
      >
        {label} <CountBadge n={count} active={active} />
      </button>
    );
  };

  return (
    <section aria-label="Para Minha Ação" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <ListChecks size={17} color="var(--color-primary)" />
            <h3 style={{ margin: 0, fontSize: '0.95rem', fontWeight: 800, color: 'var(--color-text-main)', textTransform: 'uppercase', letterSpacing: '0.03em' }}>
              Para Minha Ação
            </h3>
          </div>
          <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Ações que aguardam sua intervenção</span>
        </div>
        {!s.loading && !s.error && (
          <span style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>
            {headerTotal} {headerTotal === 1 ? 'ação' : 'ações'}
          </span>
        )}
      </div>

      <div role="tablist" aria-label="Categorias de ação" style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <Filter label="Todos" value={null} count={todosCount} />
        {s.categories.map(c => <Filter key={c.actionType} label={c.label} value={c.actionType} count={c.count} />)}
      </div>

      {notFound && (
        <div role="status" style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '8px 12px', borderRadius: 8, fontSize: '0.78rem', background: 'color-mix(in srgb, var(--color-status-amber) 12%, transparent)', color: 'var(--color-status-amber)' }}>
          <Info size={14} /> Esta ação já não está pendente ou não está disponível para si.
        </div>
      )}

      {s.loading ? (
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '22px 0', color: 'var(--color-text-muted)', justifyContent: 'center' }}>
          <Loader2 size={16} className="spin-icon" /> Carregando ações…
        </div>
      ) : s.error ? (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10, padding: '20px 0', color: 'var(--color-status-red)' }}>
          <span style={{ fontWeight: 600 }}>Não foi possível carregar as ações pendentes.</span>
          <button type="button" onClick={s.retry} style={{ ...filterBase, cursor: 'pointer' }}><RefreshCw size={14} /> Tentar novamente</button>
        </div>
      ) : s.totalCount === 0 ? (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8, padding: '22px 0', color: 'var(--color-text-muted)' }}>
          <Inbox size={24} />
          <span>{s.actionType ? 'Nenhuma ação nesta categoria.' : 'Nenhuma ação pendente para você.'}</span>
        </div>
      ) : (
        <div style={{ border: '1px solid var(--color-border)', borderRadius: 10, overflow: 'hidden' }}>
          <div style={{ maxHeight: QUEUE_MAX_HEIGHT, overflowY: 'auto' }}>
            {s.items.map(item => (
              <PersonalActionCard key={item.actionId} item={item} targeted={item.actionId === targetedId} cardRef={registerCard(item.actionId)} />
            ))}
            {s.hasMore && (
              <div style={{ display: 'flex', justifyContent: 'center', padding: '10px 0', background: 'var(--color-bg-surface)' }}>
                <button type="button" onClick={s.loadMore} disabled={s.loadingMore} style={{ ...filterBase, cursor: s.loadingMore ? 'default' : 'pointer', opacity: s.loadingMore ? 0.7 : 1 }}>
                  {s.loadingMore ? <><Loader2 size={14} className="spin-icon" /> Carregando…</> : <>Carregar mais ({s.totalCount - s.items.length})</>}
                </button>
              </div>
            )}
          </div>
        </div>
      )}
    </section>
  );
}
