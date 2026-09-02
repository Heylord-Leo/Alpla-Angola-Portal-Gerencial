import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { BuyerWorkloadStrip } from './BuyerWorkloadStrip';
import { useNavigate, useSearchParams, useLocation } from 'react-router-dom';
import {
  StickyNote, Eye, MessageSquarePlus, XCircle, UserPlus, ArrowRight,
  Building2, User as UserIcon, LayoutList, FilePlus2, PieChart,
  Clock, AlertTriangle, SlidersHorizontal, ExternalLink, ChevronLeft, ChevronRight, UserCheck,
} from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { KPICard } from '../../components/ui/KPICard';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { ModernTooltip } from '../../components/ui/ModernTooltip';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { ModalWrapper } from '../../components/common/ModalWrapper';
import { GuidedTourContextButton } from '../../features/guided-tour/GuidedTourContextButton';
import { LiveGuideLauncher } from '../../features/guided-tour/live-guide/LiveGuideLauncher';
import { RequestDrawerPresentation } from '../Requests/components/modern/RequestDrawerPresentation';
import { api } from '../../lib/api';
import type { BuyerQueueItem, BuyerQueuePage, BuyerQueueSummary } from '../../types/buyerQueue';
import {
  QUEUE_CARDS, cardCount, QUEUE_SORT_OPTIONS, QUEUE_DEFAULT_SORT,
  OWNERSHIP_TABS, DEFAULT_OWNERSHIP, countAdvancedFilters, resolveNoteTooltip,
  operationalStateColor, deadlineChip, NEED_LEVEL_LABEL, coverageProgress, pctOfTotal,
  resolvePlantOnCompanyChange, resolveNeedLevel, needLevelApiValue, NEED_LEVEL_ALL, isOwnRequest,
} from './buyerQueueView';
import { useAuth } from '../../features/auth/AuthContext';

const PAGE_SIZE = 20;

const CARD_ICONS: Record<string, React.ReactNode> = {
  all: <LayoutList size={20} />,
  needs_quotation: <FilePlus2 size={20} />,
  partial: <PieChart size={20} />,
  awaiting: <Clock size={20} />,
  attention: <AlertTriangle size={20} />,
};

// Row grid template shared by the header and every data row (tabular scanning).
const ROW_GRID = 'minmax(210px, 2.3fr) minmax(150px, 1.25fr) minmax(120px, 1fr) minmax(230px, 2.1fr) 208px';

export function BuyerQueueList() {
  const navigate = useNavigate();
  const location = useLocation();
  const [params, setParams] = useSearchParams();
  const { user } = useAuth();
  const currentUserId = user?.id ?? null; // canonical identity for the "Meu pedido" indicator

  // URL-driven state (single source of truth).
  const ownership = params.get('ownership') || DEFAULT_OWNERSHIP;
  const card = params.get('card') || 'all';
  const search = params.get('search') || '';
  const sort = params.get('sort') || QUEUE_DEFAULT_SORT;
  const buyer = params.get('buyer') || '';
  const company = params.get('company') || '';
  const plant = params.get('plant') || '';
  const department = params.get('department') || '';
  // Product default: a fresh queue (no needLevel param) opens on CRITICAL. 'ALL' is the explicit "Todos".
  const needLevel = resolveNeedLevel(params.get('needLevel'));
  const deadline = params.get('deadline') || '';
  const includeCompleted = params.get('includeCompleted') === 'true';
  const page = parseInt(params.get('page') || '1', 10);

  const [showAdvanced, setShowAdvanced] = useState(false);
  const [searchInput, setSearchInput] = useState(search);
  const [queue, setQueue] = useState<BuyerQueuePage | null>(null);
  const [summary, setSummary] = useState<BuyerQueueSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [companies, setCompanies] = useState<any[]>([]);
  const [plants, setPlants] = useState<any[]>([]);
  const [departments, setDepartments] = useState<any[]>([]);

  // Modals
  const [detailRequestId, setDetailRequestId] = useState<string | null>(null);
  const [noteModal, setNoteModal] = useState<{ id: string; number: string } | null>(null);
  const [noteText, setNoteText] = useState('');
  const [cancelItem, setCancelItem] = useState<BuyerQueueItem | null>(null);
  const [cancelReason, setCancelReason] = useState('');
  const [busy, setBusy] = useState(false);

  const selectedCard = useMemo(() => QUEUE_CARDS.find(c => c.id === card) ?? QUEUE_CARDS[0], [card]);
  const advancedCount = countAdvancedFilters({ company, plant, department, needLevel, deadline, includeCompleted });
  const plantsForCompany = useMemo(
    () => (company ? plants.filter((p: any) => String(p.companyId) === company) : plants),
    [company, plants]);

  // ── URL helpers (any filter change resets page to 1) ──
  const updateParams = useCallback((changes: Record<string, string | null>, resetPage = true) => {
    const p = new URLSearchParams(params);
    Object.entries(changes).forEach(([k, v]) => { if (v === null || v === '' || v === 'false') p.delete(k); else p.set(k, v); });
    if (resetPage) p.set('page', '1');
    setParams(p, { replace: false });
  }, [params, setParams]);

  // Debounced search → URL.
  const searchDebounce = useRef<number | undefined>(undefined);
  useEffect(() => { setSearchInput(search); }, [search]);
  const onSearchChange = (val: string) => {
    setSearchInput(val);
    window.clearTimeout(searchDebounce.current);
    searchDebounce.current = window.setTimeout(() => updateParams({ search: val || null }), 350);
  };

  // ── Data load ──
  const loadQueue = useCallback(() => {
    setLoading(true);
    setError(null);
    api.buyerQueue.getQueue({
      ownership, buyer: buyer || undefined, query: search || undefined, sort,
      company: company ? Number(company) : undefined,
      plant: plant ? Number(plant) : undefined,
      department: department ? Number(department) : undefined,
      operationalState: selectedCard.apply.operationalState,
      priority: selectedCard.apply.priority,
      deadline: deadline || undefined,
      needLevel: needLevelApiValue(needLevel),
      includeCompleted, page, pageSize: PAGE_SIZE,
    }).then(setQueue).catch(e => setError(e?.message || 'Erro ao carregar a fila.')).finally(() => setLoading(false));
  }, [ownership, buyer, search, sort, company, plant, department, selectedCard, deadline, needLevel, includeCompleted, page]);

  const loadSummary = useCallback(() => {
    // Summary scope = authorization + ownership + search + org filters (NOT the selected card).
    api.buyerQueue.getSummary({
      ownership, query: search || undefined,
      company: company ? Number(company) : undefined,
      plant: plant ? Number(plant) : undefined,
      department: department ? Number(department) : undefined,
      needLevel: needLevelApiValue(needLevel),
      includeCompleted,
    }).then(setSummary).catch(() => setSummary(null));
  }, [ownership, search, company, plant, department, needLevel, includeCompleted]);

  useEffect(() => { loadQueue(); }, [loadQueue]);
  useEffect(() => { loadSummary(); }, [loadSummary]);

  useEffect(() => {
    api.lookups.getCompanies().then(setCompanies).catch(() => setCompanies([]));
    api.lookups.getPlants().then(setPlants).catch(() => setPlants([]));
    api.lookups.getDepartments().then(setDepartments).catch(() => setDepartments([]));
  }, []);

  const refreshAll = () => { loadQueue(); loadSummary(); };

  // ── Actions ──
  const selectCard = (cardId: string) => updateParams({ card: cardId === 'all' ? null : cardId });

  // Company→Plant dependency: changing company restricts plant options and atomically clears an
  // incompatible plant in a single URL update (proven Finance pattern).
  const onCompanyChange = (v: string) => {
    const plantsOfNew = v ? plants.filter((p: any) => String(p.companyId) === v) : plants;
    updateParams({ company: v || null, plant: resolvePlantOnCompanyChange(plant || null, plantsOfNew) });
  };

  const doClaim = async (item: BuyerQueueItem) => {
    setBusy(true);
    try { await api.requests.assignBuyer(item.requestId); refreshAll(); }
    catch (e: any) { setError(e?.message || 'Falha ao assumir o pedido.'); }
    finally { setBusy(false); }
  };

  const submitNote = async () => {
    if (!noteModal || !noteText.trim()) return;
    setBusy(true);
    try { await api.requests.addNote(noteModal.id, noteText.trim()); setNoteModal(null); setNoteText(''); refreshAll(); }
    catch (e: any) { setError(e?.message || 'Falha ao adicionar observação.'); }
    finally { setBusy(false); }
  };

  const submitCancel = async () => {
    if (!cancelItem) return;
    setBusy(true);
    try { await api.requests.cancel(cancelItem.requestId, cancelReason.trim() || 'Cancelado pelo comprador.'); setCancelItem(null); setCancelReason(''); refreshAll(); }
    catch (e: any) { setError(e?.message || 'Falha ao cancelar o pedido.'); }
    finally { setBusy(false); }
  };

  const clearFilters = () => {
    const p = new URLSearchParams(params);
    ['search', 'sort', 'company', 'plant', 'department', 'needLevel', 'deadline', 'operationalState', 'priority', 'card', 'includeCompleted'].forEach(k => p.delete(k));
    p.set('page', '1');
    setParams(p);
    setSearchInput('');
  };

  const totalCount = queue?.totalCount ?? 0;
  const totalPages = queue?.totalPages ?? 1;
  const from = totalCount === 0 ? 0 : (page - 1) * PAGE_SIZE + 1;
  const to = Math.min(page * PAGE_SIZE, totalCount);
  const hasActiveFilters = !!(search || company || plant || department || (needLevel && needLevel !== NEED_LEVEL_ALL) || deadline || includeCompleted || card !== 'all' || sort !== QUEUE_DEFAULT_SORT);

  return (
    <PageContainer>
      <PageHeader
        title="Gestão de Cotações"
        subtitle="Fila operacional de pedidos de cotação — priorize, acompanhe a cobertura e atue por pedido."
        actions={
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <GuidedTourContextButton tourId="page-buyer-items" label="Tour da Tela" />
            <LiveGuideLauncher guideId="quotation-management-live-guide" />
          </div>
        }
      />

      {/* KPI work-queue cards — counts ONLY from summary; selecting narrows the list. */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 16 }}>
        {QUEUE_CARDS.map(c => {
          const isSel = card === c.id;
          const count = cardCount(c, summary);
          const pct = c.id === 'all' ? null : pctOfTotal(count, summary?.total ?? 0);
          return (
            <KPICard
              key={c.id}
              title={c.title}
              icon={CARD_ICONS[c.id]}
              value={count}
              color={c.color}
              onClick={() => selectCard(c.id)}
              borderColor={isSel ? c.color : undefined}
              bgColor={isSel ? `color-mix(in srgb, ${c.color} 6%, var(--color-bg-surface))` : 'var(--color-bg-surface)'}
              style={{
                ['--kpi-padding' as any]: '18px',
                ['--kpi-value-size' as any]: '2rem',
                ['--kpi-icon-size' as any]: '38px',
                minHeight: 118,
                boxShadow: isSel ? `0 6px 18px color-mix(in srgb, ${c.color} 20%, transparent)` : undefined,
              }}
              subtitle={
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: '0.72rem', fontWeight: 700 }}>
                  {isSel && <span style={{ width: 7, height: 7, borderRadius: 999, background: c.color, display: 'inline-block' }} />}
                  <span style={{ color: isSel ? c.color : 'var(--color-text-muted)' }}>
                    {isSel ? 'Filtro ativo' : (pct !== null ? `${pct}% do total` : 'Total na fila')}
                  </span>
                </span>
              }
            />
          );
        })}
      </div>

      {/* Toolbar — search is the widest control; tabs + sort + advanced toggle on the same row. */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 10 }}>
          <div style={{ position: 'relative', flex: '1 1 320px', minWidth: 260 }}>
            <input
              value={searchInput}
              onChange={(e) => onSearchChange(e.target.value)}
              placeholder="Buscar por número ou título do pedido…"
              style={{
                width: '100%', padding: '10px 14px', borderRadius: 10, border: '1px solid var(--color-border)',
                background: 'var(--color-bg-surface)', color: 'var(--color-text-main)', fontSize: '0.88rem',
              }}
            />
          </div>

          {/* Ownership pill tabs */}
          <div style={{ display: 'inline-flex', background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 10, padding: 3, gap: 2 }}>
            {OWNERSHIP_TABS.map(t => {
              const active = ownership === t.id;
              return (
                <button key={t.id} onClick={() => updateParams({ ownership: t.id === DEFAULT_OWNERSHIP ? null : t.id })}
                  style={{
                    padding: '7px 14px', borderRadius: 8, border: 'none', cursor: 'pointer', fontSize: '0.8rem', fontWeight: 700,
                    background: active ? 'var(--color-primary)' : 'transparent',
                    color: active ? '#fff' : 'var(--color-text-muted)',
                  }}>{t.label}</button>
              );
            })}
          </div>

          <select value={sort} onChange={(e) => updateParams({ sort: e.target.value === QUEUE_DEFAULT_SORT ? null : e.target.value })} style={selectStyle} aria-label="Ordenar">
            {QUEUE_SORT_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>

          <button onClick={() => setShowAdvanced(v => !v)} style={{ ...secondaryBtn, ...(showAdvanced || advancedCount > 0 ? { borderColor: 'var(--color-primary)', color: 'var(--color-primary)' } : {}) }}>
            <SlidersHorizontal size={15} /> Mais filtros{advancedCount > 0 ? ` (${advancedCount})` : ''}
          </button>
          {hasActiveFilters && <button onClick={clearFilters} style={{ ...ghostBtn, color: 'var(--color-status-red)' }}>Limpar filtros</button>}
        </div>

        {showAdvanced && (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 14, padding: 16, background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 12 }}>
            <LabeledSelect label="Empresa" value={company} onChange={onCompanyChange} options={[{ v: '', l: 'Todas' }, ...companies.map((c: any) => ({ v: String(c.id), l: c.name }))]} />
            <LabeledSelect label="Planta" value={plant} onChange={(v) => updateParams({ plant: v })} options={[{ v: '', l: 'Todas' }, ...plantsForCompany.map((p: any) => ({ v: String(p.id), l: p.name }))]} />
            <LabeledSelect label="Departamento" value={department} onChange={(v) => updateParams({ department: v })} options={[{ v: '', l: 'Todos' }, ...departments.map((d: any) => ({ v: String(d.id), l: d.name }))]} />
            <LabeledSelect label="Grau de necessidade" value={needLevel} onChange={(v) => updateParams({ needLevel: v })} options={[{ v: NEED_LEVEL_ALL, l: 'Todos' }, { v: 'CRITICO', l: 'Crítico' }, { v: 'URGENTE', l: 'Urgente' }, { v: 'NORMAL', l: 'Normal' }, { v: 'BAIXO', l: 'Baixo' }]} />
            <LabeledSelect label="Prazo" value={deadline} onChange={(v) => updateParams({ deadline: v })} options={[{ v: '', l: 'Todos' }, { v: 'OVERDUE', l: 'Vencido' }, { v: 'DUE_TODAY', l: 'Vence hoje' }, { v: 'APPROACHING', l: 'Prazo próximo' }]} />
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, alignSelf: 'flex-end', fontSize: '0.82rem', color: 'var(--color-text-main)', cursor: 'pointer', paddingBottom: 8 }}>
              <input type="checkbox" checked={includeCompleted} onChange={(e) => updateParams({ includeCompleted: e.target.checked ? 'true' : null })} />
              Incluir concluídos
            </label>
          </div>
        )}
      </div>

      {/* Buyer workload distribution (Dashboard V2 slice B2) — managerial visibility only; renders
          nothing otherwise. Reflects the list's structural filters; clicking filters the list. */}
      <BuyerWorkloadStrip
        company={company ? Number(company) : undefined}
        plant={plant ? Number(plant) : undefined}
        department={department ? Number(department) : undefined}
        needLevel={needLevelApiValue(needLevel)}
        activeBuyerId={buyer || undefined}
        activeUnassigned={ownership === 'unassigned'}
        onSelectBuyer={(id) => updateParams({ buyer: id, ownership: null })}
        onSelectUnassigned={() => updateParams({ ownership: 'unassigned', buyer: null })}
        onClear={() => updateParams({ buyer: null, ownership: null })}
      />

      {/* Results meta */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8 }}>
        <span style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
          {loading ? 'Carregando…' : `${totalCount} pedido${totalCount === 1 ? '' : 's'}`}
        </span>
        <button onClick={() => navigate('/buyer/items/classic')} style={classicLink} title="Workbench clássico — ações ainda não migradas para o Workspace (editar/excluir cotação, encerrar sem cotação, reutilizar cotação, cancelar lote/pedido)">
          <ExternalLink size={13} /> Tela clássica
        </button>
      </div>

      {error && <div style={{ padding: 12, background: 'var(--color-status-red-surface)', color: 'var(--color-status-red)', borderRadius: 8, fontSize: '0.85rem' }}>{error}</div>}

      {/* Column header (tabular scanning) */}
      {(queue?.items.length ?? 0) > 0 && (
        <div style={{ display: 'grid', gridTemplateColumns: ROW_GRID, gap: 16, padding: '0 18px', ...headerRowStyle }} className="bq-colheader">
          <span>Pedido</span><span>Pessoas</span><span>Prazo</span><span>Situação &amp; próxima ação</span><span style={{ textAlign: 'right' }}>Ações</span>
        </div>
      )}

      {/* Request rows */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        {!loading && queue?.items.length === 0 && (
          <div style={{ padding: '64px 20px', textAlign: 'center', border: '1px dashed var(--color-border)', borderRadius: 16, background: 'var(--color-bg-surface)', color: 'var(--color-text-muted)' }}>
            Nenhum pedido corresponde aos filtros atuais.
          </div>
        )}
        {queue?.items.map(item => (
          <RequestRow
            key={item.requestId}
            item={item}
            busy={busy}
            isOwn={isOwnRequest(item.buyerId, currentUserId)}
            onDetails={() => setDetailRequestId(item.requestId)}
            onNote={() => { setNoteModal({ id: item.requestId, number: item.requestNumber }); setNoteText(''); }}
            onCancel={() => { setCancelItem(item); setCancelReason(''); }}
            onClaim={() => doClaim(item)}
            onOpenWorkspace={() => navigate(`/buyer/requests/${item.requestId}`, { state: { from: location.pathname + location.search } })}
          />
        ))}
      </div>

      {/* Pagination */}
      {totalCount > 0 && (
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12, paddingTop: 6 }}>
          <span style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
            Mostrando <strong>{from}–{to}</strong> de <strong>{totalCount}</strong> pedido{totalCount === 1 ? '' : 's'} · {PAGE_SIZE}/página
          </span>
          {totalPages > 1 && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              <button disabled={page <= 1} onClick={() => updateParams({ page: String(page - 1) }, false)} style={pagerBtn(page <= 1)} aria-label="Anterior"><ChevronLeft size={16} /></button>
              {pageNumbers(page, totalPages).map((n, i) => n === '…'
                ? <span key={`e${i}`} style={{ padding: '0 6px', color: 'var(--color-text-muted)' }}>…</span>
                : <button key={n} onClick={() => updateParams({ page: String(n) }, false)} style={pageNumBtn(n === page)}>{n}</button>)}
              <button disabled={page >= totalPages} onClick={() => updateParams({ page: String(page + 1) }, false)} style={pagerBtn(page >= totalPages)} aria-label="Próxima"><ChevronRight size={16} /></button>
            </div>
          )}
        </div>
      )}

      {/* Detail drawer (existing experience — temporary until the Workspace phase) */}
      <RequestDrawerPresentation isOpen={!!detailRequestId} requestId={detailRequestId} onClose={() => setDetailRequestId(null)} />

      {/* Add-note modal */}
      {noteModal && (
        <ModalWrapper title={`Adicionar observação — ${noteModal.number}`} onClose={() => setNoteModal(null)}>
          <div style={{ padding: 20, display: 'flex', flexDirection: 'column', gap: 12 }}>
            <textarea autoFocus value={noteText} onChange={(e) => setNoteText(e.target.value)} placeholder="Escreva uma observação sobre este pedido…" rows={4}
              style={{ width: '100%', padding: 10, borderRadius: 8, border: '1px solid var(--color-border)', background: 'var(--color-bg-page)', color: 'var(--color-text-main)', resize: 'vertical', fontFamily: 'inherit' }} />
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
              <button onClick={() => setNoteModal(null)} style={secondaryBtn}>Cancelar</button>
              <button onClick={submitNote} disabled={!noteText.trim() || busy} style={{ ...primaryBtn, opacity: !noteText.trim() || busy ? 0.6 : 1 }}>Adicionar</button>
            </div>
          </div>
        </ModalWrapper>
      )}

      {/* Cancel confirmation (destructive) */}
      {cancelItem && (
        <ConfirmationDialog
          title="Cancelar pedido" variant="destructive" confirmText="Cancelar pedido" cancelText="Voltar"
          onConfirm={submitCancel} onCancel={() => setCancelItem(null)}
          message={
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div>Tem a certeza de que deseja cancelar <strong>{cancelItem.requestNumber}</strong>{cancelItem.title ? ` — ${cancelItem.title}` : ''}? Esta ação não pode ser desfeita.</div>
              <textarea value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} placeholder="Motivo (opcional)" rows={2}
                style={{ width: '100%', padding: 8, borderRadius: 8, border: '1px solid var(--color-border)', background: 'var(--color-bg-page)', color: 'var(--color-text-main)', resize: 'vertical', fontFamily: 'inherit' }} />
            </div>
          }
        />
      )}
    </PageContainer>
  );
}

// ── Request row (one complete Request; never a line item) ──
function RequestRow({ item, busy, isOwn, onDetails, onNote, onCancel, onClaim, onOpenWorkspace }: {
  item: BuyerQueueItem; busy: boolean; isOwn: boolean;
  onDetails: () => void; onNote: () => void; onCancel: () => void; onClaim: () => void; onOpenWorkspace: () => void;
}) {
  const stateColor = operationalStateColor(item);
  const dChip = deadlineChip(item);
  const noteTip = resolveNoteTooltip(item);
  const nextAction = item.nextActions.find(a => a.actionable) || item.nextActions[0];
  const cov = coverageProgress(item.coveredCount, item.activeItemCount);
  const isBlocking = item.attentionSignals.some(s => s.severity === 'BLOCKING');
  const unassigned = item.ownershipState === 'UNASSIGNED';

  const kebab = [
    { label: 'Ver detalhes', icon: <Eye size={15} />, onClick: onDetails },
    { label: 'Adicionar observação', icon: <MessageSquarePlus size={15} />, onClick: onNote },
    ...(item.canCancel ? [{ label: 'Cancelar pedido', icon: <XCircle size={15} />, onClick: onCancel }] : []),
  ];

  return (
    <div className="bq-row" style={{
      display: 'grid', gridTemplateColumns: ROW_GRID, gap: 16, alignItems: 'center', padding: '14px 18px',
      // Only genuine BLOCKING exceptions (ADJUSTMENT_REQUIRED) get a tinted surface. Ordinary overdue
      // rows keep a normal surface — the red left accent + "Vencido" chip carry the urgency instead.
      background: isBlocking ? `color-mix(in srgb, ${stateColor} 5%, var(--color-bg-surface))` : 'var(--color-bg-surface)',
      border: '1px solid var(--color-border)', borderLeft: `${isBlocking ? 5 : 3}px solid ${stateColor}`, borderRadius: 12,
    }}>
      {/* PEDIDO */}
      <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 3 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
          <span style={{ fontWeight: 800, color: 'var(--color-primary)', fontSize: '0.9rem' }}>{item.requestNumber}</span>
          {noteTip && (
            <ModernTooltip content={
              <div style={{ maxWidth: 320 }}>
                <div style={{ fontWeight: 700, marginBottom: 4 }}>{noteTip.title}</div>
                <div style={{ fontSize: '0.82rem', whiteSpace: 'pre-wrap' }}>{noteTip.body}</div>
                {noteTip.extra && <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', marginTop: 6 }}>{noteTip.extra}</div>}
              </div>
            }>
              <span aria-label="Tem observações" style={{ display: 'inline-flex', color: 'var(--color-status-amber)' }}><StickyNote size={14} /></span>
            </ModernTooltip>
          )}
        </div>
        <span title={item.title ?? undefined} style={{ color: 'var(--color-text-main)', fontWeight: 600, fontSize: '0.85rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{item.title}</span>
        {(item.companyName || item.plantName) && (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: '0.72rem', color: 'var(--color-text-muted)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            <Building2 size={12} /> {[item.companyName, item.plantName, item.departmentName].filter(Boolean).join(' · ')}
          </span>
        )}
      </div>

      {/* PESSOAS — Solicitante (quem pediu) vs Comprador (quem é dono da compra) */}
      <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 5 }}>
        <div style={{ minWidth: 0 }}>
          <div style={zoneLabel}>Solicitante</div>
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: '0.8rem', fontWeight: 600, color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '100%' }} title={item.requesterName ?? undefined}>
            <UserIcon size={12} /> {item.requesterName || '—'}
          </span>
        </div>
        <div style={{ minWidth: 0 }}>
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <span style={zoneLabel}>Comprador</span>
            {isOwn && (
              <ModernTooltip content="Este pedido está atribuído a você">
                <span aria-label="Meu pedido" style={{ display: 'inline-flex', alignItems: 'center', gap: 3, padding: '1px 6px', borderRadius: 999, background: 'color-mix(in srgb, var(--color-primary) 12%, transparent)', color: 'var(--color-primary)', fontSize: '0.62rem', fontWeight: 800, letterSpacing: '0.02em' }}>
                  <UserCheck size={10} /> Meu pedido
                </span>
              </ModernTooltip>
            )}
          </div>
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: '0.8rem', fontWeight: 600, color: unassigned ? 'var(--color-status-orange)' : 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '100%' }} title={item.buyerName ?? undefined}>
            <UserIcon size={12} /> {item.buyerName || (unassigned ? 'Não atribuído' : '—')}
          </span>
        </div>
      </div>

      {/* PRAZO + GRAU */}
      <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={{ fontSize: '0.8rem', color: 'var(--color-text-main)', fontWeight: 600 }}>{fmtDate(item.needByDateUtc)}</span>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
          {dChip && <span style={{ padding: '2px 8px', borderRadius: 999, background: `color-mix(in srgb, ${dChip.color} 14%, transparent)`, color: dChip.color, fontWeight: 700, fontSize: '0.68rem' }}>{dChip.label}</span>}
          {item.needLevelCode && <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>{NEED_LEVEL_LABEL[item.needLevelCode] ?? item.needLevelCode}</span>}
        </div>
      </div>

      {/* SITUAÇÃO & PRÓXIMA AÇÃO + COBERTURA */}
      <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 6 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '3px 10px', borderRadius: 8, background: `color-mix(in srgb, ${stateColor} 12%, transparent)`, color: stateColor, fontWeight: 700, fontSize: '0.74rem' }}>
            {item.operationalStateLabel}
          </span>
          {nextAction && (
            <span style={{ fontSize: '0.76rem', color: nextAction.actionable ? 'var(--color-text-main)' : 'var(--color-text-muted)', fontWeight: nextAction.actionable ? 700 : 500, fontStyle: nextAction.actionable ? 'normal' : 'italic' }}>
              {nextAction.actionable && <span aria-hidden style={{ color: 'var(--color-text-muted)', marginRight: 4 }}>›</span>}{nextAction.label}
            </span>
          )}
        </div>
        {/* coverage mini-bar (server "treated" — never implies approved) */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <div style={{ display: 'flex', gap: 2 }} aria-hidden>
            {Array.from({ length: cov.segments }).map((_, i) => (
              <span key={i} style={{ width: 12, height: 6, borderRadius: 2, background: i < cov.filled ? 'var(--color-primary)' : 'color-mix(in srgb, var(--color-text-muted) 22%, transparent)' }} />
            ))}
          </div>
          <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
            {item.coveredCount}/{item.activeItemCount} tratados{item.pendingCount > 0 ? ` · ${item.pendingCount} pendente${item.pendingCount === 1 ? '' : 's'}` : ''}
          </span>
        </div>
      </div>

      {/* AÇÕES */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 8 }}>
        {unassigned && item.canClaim ? (
          <button onClick={onClaim} disabled={busy} style={{ ...primaryBtn, width: 156, justifyContent: 'center' }}><UserPlus size={15} /> Atribuir a Mim</button>
        ) : (
          <button onClick={onOpenWorkspace} style={{ ...primaryBtn, width: 156, justifyContent: 'center' }}>Abrir Workspace <ArrowRight size={15} /></button>
        )}
        <KebabMenu options={kebab} />
      </div>
    </div>
  );
}

function fmtDate(iso?: string | null): string {
  if (!iso) return 'Sem prazo';
  try { return new Date(iso).toLocaleDateString('pt-PT', { day: '2-digit', month: 'short', year: 'numeric' }); }
  catch { return '—'; }
}

function pageNumbers(current: number, total: number): (number | '…')[] {
  const out: (number | '…')[] = [];
  const push = (n: number | '…') => out.push(n);
  const window = 1;
  for (let n = 1; n <= total; n++) {
    if (n === 1 || n === total || (n >= current - window && n <= current + window)) push(n);
    else if (out[out.length - 1] !== '…') push('…');
  }
  return out;
}

function LabeledSelect({ label, value, onChange, options }: { label: string; value: string; onChange: (v: string) => void; options: { v: string; l: string }[] }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4, minWidth: 150 }}>
      <label style={{ fontSize: '0.72rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>{label}</label>
      <select value={value} onChange={(e) => onChange(e.target.value)} style={selectStyle}>
        {options.map(o => <option key={o.v} value={o.v}>{o.l}</option>)}
      </select>
    </div>
  );
}

const headerRowStyle: React.CSSProperties = {
  fontSize: '0.68rem', fontWeight: 800, letterSpacing: '0.04em', textTransform: 'uppercase', color: 'var(--color-text-muted)',
};
const zoneLabel: React.CSSProperties = {
  fontSize: '0.62rem', fontWeight: 800, letterSpacing: '0.03em', textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: 1,
};
const selectStyle: React.CSSProperties = {
  padding: '9px 12px', borderRadius: 10, border: '1px solid var(--color-border)',
  background: 'var(--color-bg-surface)', color: 'var(--color-text-main)', fontSize: '0.82rem', fontWeight: 600, cursor: 'pointer',
};
const secondaryBtn: React.CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 6, padding: '9px 14px', borderRadius: 10, border: '1px solid var(--color-border)',
  background: 'var(--color-bg-surface)', color: 'var(--color-text-muted)', fontSize: '0.8rem', fontWeight: 700, cursor: 'pointer',
};
const ghostBtn: React.CSSProperties = {
  padding: '9px 12px', borderRadius: 10, border: 'none', background: 'transparent', fontSize: '0.8rem', fontWeight: 700, cursor: 'pointer',
};
const primaryBtn: React.CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 6, padding: '9px 14px', borderRadius: 10, border: 'none',
  background: 'var(--color-primary)', color: '#fff', fontSize: '0.8rem', fontWeight: 700, cursor: 'pointer', whiteSpace: 'nowrap',
};
const classicLink: React.CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 5, padding: '5px 10px', borderRadius: 8, border: '1px dashed var(--color-border)',
  background: 'transparent', color: 'var(--color-text-muted)', fontSize: '0.74rem', fontWeight: 600, cursor: 'pointer',
};
const pagerBtn = (disabled: boolean): React.CSSProperties => ({
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 34, height: 34, borderRadius: 8,
  border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', color: disabled ? 'var(--color-text-muted)' : 'var(--color-text-main)',
  cursor: disabled ? 'not-allowed' : 'pointer', opacity: disabled ? 0.5 : 1,
});
const pageNumBtn = (active: boolean): React.CSSProperties => ({
  minWidth: 34, height: 34, padding: '0 8px', borderRadius: 8, cursor: 'pointer', fontSize: '0.8rem', fontWeight: 700,
  border: `1px solid ${active ? 'var(--color-primary)' : 'var(--color-border)'}`,
  background: active ? 'var(--color-primary)' : 'var(--color-bg-surface)', color: active ? '#fff' : 'var(--color-text-main)',
});

export default BuyerQueueList;
