import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties, ReactNode } from 'react';
import { Loader2, RefreshCw, Inbox, Download, Search } from 'lucide-react';
import { api } from '../../../lib/api';
import type { ApprovalHistoryPage, ApprovalHistoryQuery, ApprovalDecisionCode, ApprovalStageCode } from '../../../types/approvalHistory';
import { ApprovalTimelineDrawer } from './ApprovalTimelineDrawer';

// v2.244.0 Approval Center V2 — Phase 2. Searchable, filterable, paged approval-decision history with
// CSV export (same filters/scope as the table) and a per-request audit-timeline drawer. All data comes
// from read-only backend endpoints over RequestStatusHistories; no client-only search, no mutation.

const PAGE_SIZE = 25;

// §19 — the default window is 30 days (preset '30'); older history stays reachable via "Tudo".
type DatePreset = 'today' | '7' | '30' | '90' | 'all';

const chip: CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 6, padding: '6px 12px', borderRadius: 8,
  border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', cursor: 'pointer',
  fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-secondary, var(--color-text-main))', whiteSpace: 'nowrap',
};
const chipActive: CSSProperties = { background: 'var(--color-primary)', color: '#fff', borderColor: 'var(--color-primary)' };

function dateFromPreset(preset: DatePreset): string | undefined {
  if (preset === 'all') return undefined;
  const d = new Date();
  if (preset === 'today') d.setHours(0, 0, 0, 0);
  else if (preset === '7') d.setDate(d.getDate() - 7);
  else if (preset === '30') d.setDate(d.getDate() - 30);
  else if (preset === '90') d.setDate(d.getDate() - 90);
  return d.toISOString();
}

const DECISION_LABEL: Record<ApprovalDecisionCode, string> = {
  APPROVED: 'Aprovado', REJECTED: 'Rejeitado', RETURNED: 'Devolvido', RESUBMITTED: 'Reenviado',
};
function decisionStyle(decision: ApprovalDecisionCode | null): CSSProperties {
  const map: Record<string, [string, string]> = {
    APPROVED: ['#166534', 'color-mix(in srgb, #16a34a 16%, transparent)'],
    REJECTED: ['#991b1b', 'color-mix(in srgb, #dc2626 16%, transparent)'],
    RETURNED: ['#92400e', 'color-mix(in srgb, #d97706 18%, transparent)'],
    RESUBMITTED: ['#3730a3', 'color-mix(in srgb, #6366f1 16%, transparent)'],
  };
  const [color, background] = decision ? map[decision] : ['var(--color-text-muted)', 'transparent'];
  return { color, background, fontSize: '0.68rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.03em', padding: '2px 8px', borderRadius: 999, whiteSpace: 'nowrap' };
}

export function HistoricoTab() {
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [stage, setStage] = useState<ApprovalStageCode | ''>('');
  const [decision, setDecision] = useState<ApprovalDecisionCode | ''>('');
  const [requestType, setRequestType] = useState<'' | 'QUOTATION' | 'PAYMENT'>('');
  const [preset, setPreset] = useState<DatePreset>('30');
  const [sort, setSort] = useState('dateDesc');
  const [page, setPage] = useState(1);

  const [data, setData] = useState<ApprovalHistoryPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const [timelineFor, setTimelineFor] = useState<{ id: string; number: string } | null>(null);

  const reqToken = useRef(0);

  // Debounce the free-text search (server-side; never client-only over one page).
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 400);
    return () => clearTimeout(t);
  }, [search]);

  // Any filter change resets to page 1.
  useEffect(() => { setPage(1); }, [debouncedSearch, stage, decision, requestType, preset, sort]);

  const query = useMemo<ApprovalHistoryQuery>(() => ({
    search: debouncedSearch || undefined,
    stage: stage || undefined,
    decision: decision || undefined,
    requestType: requestType || undefined,
    dateFrom: dateFromPreset(preset),
    sort,
    page,
    pageSize: PAGE_SIZE,
  }), [debouncedSearch, stage, decision, requestType, preset, sort, page]);

  const load = useCallback(async () => {
    const token = ++reqToken.current;
    setLoading(true); setError(false);
    try {
      const res = await api.approvals.getHistory(query);
      if (token === reqToken.current) { setData(res); setLoading(false); }
    } catch {
      if (token === reqToken.current) { setError(true); setLoading(false); }
    }
  }, [query]);

  useEffect(() => { load(); }, [load]);

  const handleExport = async () => {
    setExporting(true); setExportError(null);
    try {
      // Same filters/scope as the table (paging/sort aside).
      const blob = await api.approvals.exportHistory({ ...query, page: undefined, pageSize: undefined });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      const now = new Date();
      const stamp = `${now.getFullYear()}${String(now.getMonth() + 1).padStart(2, '0')}${String(now.getDate()).padStart(2, '0')}-${String(now.getHours()).padStart(2, '0')}${String(now.getMinutes()).padStart(2, '0')}`;
      a.download = `approval-history-${stamp}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);
    } catch {
      setExportError('Falha ao exportar. Tente novamente.');
    } finally {
      setExporting(false);
    }
  };

  const total = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 0;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      {/* Filter bar */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
          <div style={{ position: 'relative', flex: '1 1 260px', minWidth: 220 }}>
            <Search size={15} style={{ position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--color-text-muted)' }} />
            <input
              type="text" value={search} onChange={e => setSearch(e.target.value)}
              placeholder="Buscar por nº, título, solicitante, aprovador, departamento…"
              aria-label="Buscar histórico de aprovações"
              style={{ width: '100%', padding: '8px 12px 8px 32px', borderRadius: 8, border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', color: 'var(--color-text-main)', fontSize: '0.82rem' }}
            />
          </div>
          <button
            type="button" onClick={handleExport} disabled={exporting || total === 0}
            style={{ ...chip, opacity: exporting || total === 0 ? 0.55 : 1, cursor: exporting || total === 0 ? 'default' : 'pointer' }}
          >
            {exporting ? <Loader2 size={14} className="spin-icon" /> : <Download size={14} />} Exportar CSV
          </button>
        </div>

        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          <FilterGroup label="Etapa">
            <Chip active={stage === ''} onClick={() => setStage('')}>Todas</Chip>
            <Chip active={stage === 'AREA'} onClick={() => setStage('AREA')}>Área</Chip>
            <Chip active={stage === 'FINAL'} onClick={() => setStage('FINAL')}>Final</Chip>
          </FilterGroup>
          <FilterGroup label="Decisão">
            <Chip active={decision === ''} onClick={() => setDecision('')}>Todas</Chip>
            <Chip active={decision === 'APPROVED'} onClick={() => setDecision('APPROVED')}>Aprovado</Chip>
            <Chip active={decision === 'REJECTED'} onClick={() => setDecision('REJECTED')}>Rejeitado</Chip>
            <Chip active={decision === 'RETURNED'} onClick={() => setDecision('RETURNED')}>Devolvido</Chip>
            <Chip active={decision === 'RESUBMITTED'} onClick={() => setDecision('RESUBMITTED')}>Reenviado</Chip>
          </FilterGroup>
          <FilterGroup label="Tipo">
            <Chip active={requestType === ''} onClick={() => setRequestType('')}>Todos</Chip>
            <Chip active={requestType === 'QUOTATION'} onClick={() => setRequestType('QUOTATION')}>Cotação</Chip>
            <Chip active={requestType === 'PAYMENT'} onClick={() => setRequestType('PAYMENT')}>Pagamento</Chip>
          </FilterGroup>
          <FilterGroup label="Período">
            <Chip active={preset === 'today'} onClick={() => setPreset('today')}>Hoje</Chip>
            <Chip active={preset === '7'} onClick={() => setPreset('7')}>7 dias</Chip>
            <Chip active={preset === '30'} onClick={() => setPreset('30')}>30 dias</Chip>
            <Chip active={preset === '90'} onClick={() => setPreset('90')}>90 dias</Chip>
            <Chip active={preset === 'all'} onClick={() => setPreset('all')}>Tudo</Chip>
          </FilterGroup>
        </div>
      </div>

      {/* Result summary */}
      {!loading && !error && data && (
        <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', alignItems: 'center', fontSize: '0.78rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>
          <span><strong style={{ color: 'var(--color-text-main)' }}>{total}</strong> {total === 1 ? 'decisão encontrada' : 'decisões encontradas'}</span>
          <span>Aprovadas: <strong style={{ color: 'var(--color-text-main)' }}>{data.approvedCount}</strong></span>
          <span>Rejeitadas: <strong style={{ color: 'var(--color-text-main)' }}>{data.rejectedCount}</strong></span>
          <span>Devolvidas: <strong style={{ color: 'var(--color-text-main)' }}>{data.returnedCount}</strong></span>
          {data.resubmittedCount > 0 && (
            <span>Reenviadas: <strong style={{ color: 'var(--color-text-main)' }}>{data.resubmittedCount}</strong></span>
          )}
          <span style={{ marginLeft: 'auto' }}>
            <label style={{ marginRight: 6 }}>Ordenar:</label>
            <select value={sort} onChange={e => setSort(e.target.value)} aria-label="Ordenar histórico"
              style={{ padding: '4px 8px', borderRadius: 6, border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)', color: 'var(--color-text-main)', fontSize: '0.75rem', fontWeight: 700 }}>
              <option value="dateDesc">Mais recentes</option>
              <option value="dateAsc">Mais antigos</option>
              <option value="valueDesc">Maior valor</option>
            </select>
          </span>
        </div>
      )}
      {exportError && (
        <div role="alert" style={{ fontSize: '0.78rem', color: 'var(--color-status-red, #dc2626)', fontWeight: 600 }}>{exportError}</div>
      )}

      {/* Table / states */}
      {loading ? (
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, justifyContent: 'center', padding: '40px 0', color: 'var(--color-text-muted)' }}>
          <Loader2 size={16} className="spin-icon" /> Carregando histórico…
        </div>
      ) : error ? (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10, padding: '32px 0', color: 'var(--color-status-red, #dc2626)' }}>
          <span style={{ fontWeight: 700 }}>Não foi possível carregar o histórico.</span>
          <button type="button" onClick={load} style={{ ...chip, cursor: 'pointer' }}><RefreshCw size={14} /> Tentar novamente</button>
        </div>
      ) : total === 0 ? (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8, padding: '40px 0', color: 'var(--color-text-muted)' }}>
          <Inbox size={26} />
          <span>Nenhuma decisão encontrada para os filtros atuais.</span>
        </div>
      ) : (
        <>
          <div style={{ border: '1px solid var(--color-border)', borderRadius: 10, overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem' }}>
              <thead>
                <tr style={{ background: 'var(--color-bg-page)', textAlign: 'left' }}>
                  {['Data/Hora', 'Pedido', 'Tipo', 'Etapa', 'Decisão', 'Solicitante', 'Aprovador', 'Departamento', 'Valor', ''].map((h, i) => (
                    <th key={i} style={{ padding: '9px 12px', fontSize: '0.66rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)', whiteSpace: 'nowrap' }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data!.items.map(row => (
                  <tr
                    key={row.id} role="button" tabIndex={0}
                    onClick={() => setTimelineFor({ id: row.requestId, number: row.requestNumber })}
                    onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setTimelineFor({ id: row.requestId, number: row.requestNumber }); } }}
                    aria-label={`Abrir linha do tempo de ${row.requestNumber}`}
                    style={{ borderTop: '1px solid var(--color-border)', cursor: 'pointer' }}
                  >
                    <td style={{ padding: '9px 12px', whiteSpace: 'nowrap', color: 'var(--color-text-muted)', fontWeight: 600 }}>{new Date(row.decisionAtUtc).toLocaleString('pt-PT')}</td>
                    <td style={{ padding: '9px 12px', whiteSpace: 'nowrap' }}>
                      <span style={{ fontWeight: 800, color: 'var(--color-primary)' }}>{row.requestNumber}</span>
                      {row.batchNumber != null && <span style={{ marginLeft: 6, fontSize: '0.62rem', fontWeight: 800, color: 'var(--color-primary)', opacity: 0.8 }}>Lote #{row.batchNumber}</span>}
                    </td>
                    <td style={{ padding: '9px 12px', whiteSpace: 'nowrap', color: 'var(--color-text-muted)', fontWeight: 700 }}>{row.requestTypeCode}</td>
                    <td style={{ padding: '9px 12px', whiteSpace: 'nowrap' }}>
                      {row.approvalLevel && (
                        <span style={{ fontSize: '0.66rem', fontWeight: 800, textTransform: 'uppercase', padding: '2px 7px', borderRadius: 4, border: '1px solid var(--color-border)', color: 'var(--color-text-secondary, var(--color-text-main))' }}>
                          {row.approvalLevel === 'FINAL' ? 'Final' : 'Área'}
                        </span>
                      )}
                    </td>
                    <td style={{ padding: '9px 12px' }}><span style={decisionStyle(row.decision)}>{row.decision ? DECISION_LABEL[row.decision] : '—'}</span></td>
                    <td style={{ padding: '9px 12px', maxWidth: 160, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{row.requesterName}</td>
                    <td style={{ padding: '9px 12px', maxWidth: 160, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', fontWeight: 700 }}>{row.approverName}</td>
                    <td style={{ padding: '9px 12px', maxWidth: 150, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: 'var(--color-text-muted)' }}>{row.departmentName ?? '—'}</td>
                    <td style={{ padding: '9px 12px', whiteSpace: 'nowrap', textAlign: 'right', fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>
                      {row.amount != null ? `${row.amount.toLocaleString('pt-PT')} ${row.currencyCode ?? ''}` : '—'}
                    </td>
                    <td style={{ padding: '9px 12px', color: 'var(--color-text-muted)', whiteSpace: 'nowrap', fontSize: '0.72rem', fontWeight: 700 }}>Ver</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12 }}>
              <button type="button" disabled={page <= 1} onClick={() => setPage(p => Math.max(1, p - 1))}
                style={{ ...chip, opacity: page <= 1 ? 0.5 : 1, cursor: page <= 1 ? 'default' : 'pointer' }}>Anterior</button>
              <span style={{ fontSize: '0.78rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>Página {page} de {totalPages}</span>
              <button type="button" disabled={page >= totalPages} onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                style={{ ...chip, opacity: page >= totalPages ? 0.5 : 1, cursor: page >= totalPages ? 'default' : 'pointer' }}>Próxima</button>
            </div>
          )}
        </>
      )}

      {timelineFor && (
        <ApprovalTimelineDrawer requestId={timelineFor.id} requestNumber={timelineFor.number} onClose={() => setTimelineFor(null)} />
      )}
    </div>
  );
}

function FilterGroup({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div role="group" aria-label={label} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      <span style={{ fontSize: '0.64rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>{label}</span>
      {children}
    </div>
  );
}

function Chip({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button type="button" aria-pressed={active} onClick={onClick} style={{ ...chip, ...(active ? chipActive : {}) }}>
      {children}
    </button>
  );
}

export default HistoricoTab;
