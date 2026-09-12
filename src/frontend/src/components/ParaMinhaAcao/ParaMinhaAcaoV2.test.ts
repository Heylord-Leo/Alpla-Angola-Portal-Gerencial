import { describe, it, expect } from 'vitest';
// Node-env vitest (no jsdom/RTL) — source-level guards for the Para Minha Ação V2 personal queue.
import widget from './ParaMinhaAcaoV2.tsx?raw';
import hook from './usePersonalActions.ts?raw';
import card from './PersonalActionCard.tsx?raw';
import apiSrc from '../../lib/api.ts?raw';
import dashboard from '../../pages/Requests/components/modern/RequestsDashboard.tsx?raw';

describe('api.requests.myActions (v2.243.0)', () => {
  it('calls GET /api/v1/requests/my-actions with the released params and default sort=priority', () => {
    expect(apiSrc).toMatch(/myActions:\s*async/);
    expect(apiSrc).toMatch(/\/api\/v1\/requests\/my-actions/);
    expect(apiSrc).toMatch(/params\.append\('actionType'/);
    expect(apiSrc).toMatch(/params\.append\('page'/);
    expect(apiSrc).toMatch(/params\.append\('pageSize'/);
    expect(apiSrc).toMatch(/opts\.sort \?\? 'priority'/);
    expect(apiSrc).toMatch(/targetRequestId/);
    expect(apiSrc).toMatch(/targetPoGroupId/);
    expect(apiSrc).toMatch(/targetActionType/);
  });
});

describe('usePersonalActions (v2.243.0)', () => {
  it('sends actionType (null → omitted), pageSize 20 and sort priority', () => {
    expect(hook).toMatch(/actionType:\s*type \?\? undefined/);
    expect(hook).toMatch(/pageSize:\s*PAGE_SIZE/);
    expect(hook).toMatch(/const PAGE_SIZE = 20/);
    expect(hook).toMatch(/sort:\s*'priority'/);
  });
  it('appends on load-more and replaces on category switch (page 1)', () => {
    expect(hook).toMatch(/append \? \[\.\.\.prev, \.\.\.\(res\.items \?\? \[\]\)\] : \(res\.items \?\? \[\]\)/);
    expect(hook).toMatch(/fetchPage\(actionType, page \+ 1, true\)/); // load more → next page, append
    expect(hook).toMatch(/fetchPage\(actionType, 1, false\)/);        // (re)load page 1, replace
  });
  it('resets items/page immediately on category change', () => {
    expect(hook).toMatch(/setItems\(\[\]\)/);
    expect(hook).toMatch(/setPage\(1\)/);
    expect(hook).toMatch(/setActionType\(next\)/);
  });
  it('is race-safe: discards stale responses by a monotonic token', () => {
    expect(hook).toMatch(/const token = \+\+tokenRef\.current/);
    expect(hook).toMatch(/token !== tokenRef\.current/);
  });
  it('StrictMode-safe: the mount effect RESTORES mountedRef.current=true on setup (not cleanup-only)', () => {
    // The DEV StrictMode setup→cleanup→setup cycle must re-arm mountedRef, else valid 200s are
    // discarded and loading never clears (the "Carregando ações…" stuck-state bug).
    expect(hook).toMatch(/mountedRef\.current = true;\s*[\r\n]+\s*return \(\) => \{ mountedRef\.current = false; \};/);
    // Regression guard against the buggy cleanup-only pattern.
    expect(hook).not.toMatch(/useEffect\(\(\) => \(\) => \{ mountedRef\.current = false; \}, \[\]\)/);
    // Loading must be finalized for the active request in finally (both loading + loadingMore).
    expect(hook).toMatch(/finally \{[\s\S]*setLoading\(false\); setLoadingMore\(false\)/);
  });
  it('does NOT re-sort backend items and derives hasMore from paging', () => {
    expect(hook).not.toMatch(/\.sort\(/);
    expect(hook).toMatch(/hasMore:\s*page < totalPages/);
  });
  it('guards double load-more and last page', () => {
    expect(hook).toMatch(/if \(loading \|\| loadingMore\) return;/);
    expect(hook).toMatch(/if \(page >= totalPages\) return;/);
  });
});

describe('ParaMinhaAcaoV2 (v2.243.0)', () => {
  it('renders a synthetic Todos chip (null actionType) plus backend categories in server order', () => {
    expect(widget).toMatch(/label="Todos" value=\{null\}/);
    expect(widget).toMatch(/s\.categories\.map\(c =>/);
    expect(widget).toMatch(/value=\{c\.actionType\}/);
    // Todos count = sum of category counts (no separate count endpoint).
    expect(widget).toMatch(/s\.categories\.reduce\(\(sum, c\) => sum \+ \(c\.count \?\? 0\)/);
  });
  it('has loading, error+retry, and empty states (no alerts)', () => {
    expect(widget).toMatch(/Carregando ações/);
    expect(widget).toMatch(/Não foi possível carregar as ações pendentes/);
    expect(widget).toMatch(/onClick=\{s\.retry\}/);
    expect(widget).toMatch(/Nenhuma ação pendente para você/);
    expect(widget).toMatch(/Nenhuma ação nesta categoria/);
    expect(widget).not.toMatch(/alert\(/);
  });
  it('renders a vertical action-card list with "Carregar mais" (not a carousel)', () => {
    expect(widget).toMatch(/s\.items\.map\(item =>/);
    expect(widget).toMatch(/<PersonalActionCard/);
    expect(widget).toMatch(/Carregar mais/);
    expect(widget).toMatch(/onClick=\{s\.loadMore\}/);
    // It is a vertical list + load-more, not the old index-cycling carousel.
    expect(widget).not.toMatch(/carouselIndex|ActionCarouselWidget/);
  });
  it('category chips are accessible (tablist/tab + aria-selected)', () => {
    expect(widget).toMatch(/role="tablist"/);
    expect(widget).toMatch(/role="tab"/);
    expect(widget).toMatch(/aria-selected=\{active\}/);
  });
});

describe('PersonalActionCard (v2.243.0)', () => {
  it('renders number/title and the action/overdue/need-level chips', () => {
    expect(card).toMatch(/\{item\.requestNumber\}/);
    expect(card).toMatch(/\{item\.requestTitle\}/);
    expect(card).toMatch(/text=\{item\.actionLabel\} tone="action"/);
    expect(card).toMatch(/item\.isOverdue &&.*Vencido/s);
    expect(card).toMatch(/NEED_LEVEL_LABEL\[item\.needLevelCode\]/);
  });
  it('shows supplier context with +N aggregation and PO/amount when present', () => {
    expect(card).toMatch(/affectedSuppliers\?\.\[0\]\?\.supplierName/);
    expect(card).toMatch(/\+\$\{extra\}/);                 // "primary +N"
    expect(card).toMatch(/item\.purchaseOrderNumber/);
    expect(card).toMatch(/item\.amount != null/);
  });
  it('CTA navigates via item.route, no hardcoded per-action flow', () => {
    expect(card).toMatch(/navigate\(item\.route\)/);
    expect(card).not.toMatch(/CorrectPoModal|REGISTER_PO|openCorrection/);
  });
});

describe('RequestsDashboard integration (v2.243.0)', () => {
  it('renders ParaMinhaAcaoV2 in place of the legacy ActionCarouselWidget', () => {
    expect(dashboard).toMatch(/<ParaMinhaAcaoV2 \/>/);
    expect(dashboard).not.toMatch(/<ActionCarouselWidget/);
  });
});

describe('Para Minha Ação V2 — compact UX refactor (v2.243.0)', () => {
  it('has a total-count header with subtitle', () => {
    expect(widget).toMatch(/Ações que aguardam sua intervenção/);
    expect(widget).toMatch(/headerTotal === 1 \? 'ação' : 'ações'/);
  });
  it('bounds the queue height with an internal scroll (does not dominate the dashboard)', () => {
    expect(widget).toMatch(/QUEUE_MAX_HEIGHT = 420/);
    expect(widget).toMatch(/maxHeight: QUEUE_MAX_HEIGHT, overflowY: 'auto'/);
  });
  it('uses a segmented filter (rounded-8, selected state = filled primary)', () => {
    expect(widget).toMatch(/borderRadius: 8/);
    expect(widget).toMatch(/active \? \{ background: 'var\(--color-primary\)', color: '#fff'/);
  });
  it('renders compact rows: the card CTA is an outline button, not a dominant filled block', () => {
    expect(card).toMatch(/border: '1px solid var\(--color-primary\)'/);
    expect(card).toMatch(/background: 'transparent'/);
    // Row is dense: shared bottom border + thin left accent, not a tall bordered card.
    expect(card).toMatch(/borderBottom: '1px solid var\(--color-border\)'/);
    expect(card).toMatch(/borderLeft: `3px solid \$\{targeted \? 'var\(--color-status-red\)' : accent\}`/);
  });
  it('uses color sparingly: amber for PO correction, red for overdue, else neutral', () => {
    expect(card).toMatch(/item\.actionType === 'PO_CORRECTION' \? 'var\(--color-status-amber\)'/);
    expect(card).toMatch(/item\.isOverdue \? 'var\(--color-status-red\)'/);
    expect(card).toMatch(/'var\(--color-border\)'/);
  });
  it('multi-supplier shows +N with a full-list tooltip (title), one row only', () => {
    expect(card).toMatch(/\+\$\{extra\}/);
    expect(card).toMatch(/title=\{supplierTitle\}/);
  });
});

describe('Para Minha Ação V2 — Phase 2 deep-link / target / highlight (v2.243.0)', () => {
  it('parses action/requestId/poGroupId from the URL (once, from first render)', () => {
    expect(widget).toMatch(/searchParams\.get\('action'\)/);
    expect(widget).toMatch(/searchParams\.get\('requestId'\)/);
    expect(widget).toMatch(/searchParams\.get\('poGroupId'\)/);
    expect(widget).toMatch(/useMemo<MyActionsTarget \| undefined>/);
    // action-only is valid (category-only); requestId/poGroupId optional.
    expect(widget).toMatch(/if \(!action\) return undefined;/);
    expect(widget).toMatch(/return \{ actionType: action, requestId, poGroupId \}/);
  });
  it('auto-selects the target category (hook initializes actionType from the target/action)', () => {
    expect(hook).toMatch(/useState<string \| null>\(initialTarget\?\.actionType \?\? null\)/);
    expect(widget).toMatch(/usePersonalActions\(initialTarget\)/);
  });
  it('forwards target params to /my-actions and NEVER computes the target page in frontend', () => {
    expect(hook).toMatch(/targetRequestId: t\?\.requestId/);
    expect(hook).toMatch(/targetPoGroupId: t\?\.poGroupId/);
    expect(hook).toMatch(/targetActionType: t\?\.actionType/);
    // Uses the backend-returned page as authoritative (no frontend page math).
    expect(hook).toMatch(/setPage\(res\.page \?\? targetPage\)/);
    expect(hook).not.toMatch(/Math\.(ceil|floor)\([^)]*pageSize/);
  });
  it('target is forwarded only on the initial (non-append) fetch; select arms/clears it', () => {
    expect(hook).toMatch(/const t = !append \? pendingTargetRef\.current : null;/);
    expect(hook).toMatch(/clearTarget = useCallback\(\(\) => \{ pendingTargetRef\.current = null; \}/);
    // select(next, target?) arms pendingTargetRef with the (optional) target for the next fetch.
    expect(hook).toMatch(/pendingTargetRef\.current = target \?\? null;/);
  });
  it('matches via the shared case-insensitive resolveTargetItem helper (GUID casing safe)', () => {
    expect(widget).toMatch(/resolveTargetItem\(s\.items, target\)/);
    expect(widget).toMatch(/import \{ resolveTargetItem, normalizeId \} from '\.\/targetMatch'/);
    // The detailed identity/aggregation/case-insensitivity semantics are unit-tested in targetMatch.test.ts.
  });
  it('scrolls the resolved target into view (smooth normal, auto under reduced-motion)', () => {
    expect(widget).toMatch(/scrollIntoView\(\{ block: 'center', behavior: prefersReduced\.current \? 'auto' : 'smooth' \}\)/);
    expect(widget).toMatch(/matchMedia\?\.\('\(prefers-reduced-motion: reduce\)'\)/);
    expect(widget).not.toMatch(/\.focus\(\)/); // never steal keyboard focus
  });
  it('applies a ~5s target emphasis with aria-current and a cleanup-safe timer', () => {
    expect(widget).toMatch(/HIGHLIGHT_MS = 5000/);
    expect(widget).toMatch(/setTimeout\(\(\) => setTargetedId\(null\), HIGHLIGHT_MS\)/);
    expect(widget).toMatch(/if \(timerRef\.current\) clearTimeout\(timerRef\.current\)/);
    // Unmount cleanup clears any pending highlight timer (implicit-return effect).
    expect(widget).toMatch(/useEffect\(\(\) => \(\) => \{ if \(timerRef\.current\) clearTimeout\(timerRef\.current\); \}, \[\]\)/);
    expect(card).toMatch(/aria-current=\{targeted \? 'true' : undefined\}/);
    expect(card).toMatch(/className=\{targeted \? 'pma-targeted' : undefined\}/);
  });
  it('cleans ONLY the transient TARGET ids (keeps action=<category>), preserving others (no reload)', () => {
    // v2.243.0 Phase 3: action is kept as shareable category state; only requestId/poGroupId are transient.
    expect(widget).toMatch(/for \(const k of \['requestId', 'poGroupId'\]\)/);
    expect(widget).not.toMatch(/for \(const k of \['action', 'requestId', 'poGroupId'\]\)/);
    expect(widget).toMatch(/navigate\(\{ pathname: location\.pathname, search: next\.toString\(\) \}, \{ replace: true \}\)/);
    // The resolve effect runs only for a real target (requestId present) — category-only never enters it.
    expect(widget).toMatch(/if \(!target\?\.requestId \|\| s\.loading \|\| s\.error\) return;/);
    // Resolved once per unique target (handled-key guard prevents re-trigger after cleanup).
    expect(widget).toMatch(/if \(!key \|\| key === handledTargetKeyRef\.current\) return;/);
  });

  it('category-only (action, no requestId) selects the category without target behavior', () => {
    // The hook selects the category from the action, even without a requestId.
    expect(hook).toMatch(/useState<string \| null>\(initialTarget\?\.actionType \?\? null\)/);
    // Target params are forwarded ONLY when a requestId is present.
    expect(hook).toMatch(/const hasTarget = !!\(initialTarget && initialTarget\.requestId\)/);
    expect(hook).toMatch(/pendingTargetRef = useRef<MyActionsTarget \| null>\(hasTarget \? initialTarget! : null\)/);
  });
  it('handles a stale/resolved target gracefully (inline note, no alert) and still cleans URL', () => {
    expect(widget).toMatch(/Esta ação já não está pendente ou não está disponível para si/);
    expect(widget).not.toMatch(/alert\(/);
    expect(widget).toMatch(/setNotFound\(true\)/);
  });
  it('manual category chip drives the URL (single source of truth; Back/Forward works)', () => {
    // Chip click navigates the URL; the URL-reactive effect performs the load + resets not-found.
    expect(widget).toMatch(/onClick=\{\(\) => selectCategory\(value\)\}/);
    expect(widget).toMatch(/if \(value\) next\.set\('action', value\); else next\.delete\('action'\)/);
    expect(widget).toMatch(/next\.delete\('requestId'\); next\.delete\('poGroupId'\)/);
    // Category chip navigation is a PUSH (no {replace}) so browser Back/Forward restores categories.
    expect(widget).toMatch(/navigate\(\{ pathname: location\.pathname, search: next\.toString\(\) \}\);/);
  });

  it('URL-reactive: same-route search change updates category/target (not useMemo-once)', () => {
    // The reactive effect depends on searchParams, not a []-memo, so same-route navigation reacts.
    expect(widget).toMatch(/\}, \[searchParams\]\);/);
    expect(widget).toMatch(/if \(appliedCatRef\.current !== cat\)/);         // category change → reload
    expect(widget).toMatch(/if \(key && key !== handledTargetKeyRef\.current && key !== pendingKeyRef\.current\)/); // same-cat new target
  });
});
