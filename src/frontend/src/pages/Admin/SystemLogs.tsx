import React, { useState, useEffect, useCallback } from 'react';
import { apiFetch, API_BASE_URL } from '../../lib/api';
import {
  FileText, Search, Eye, AlertTriangle, Info, XCircle,
  ChevronLeft, ChevronRight, X, RefreshCw, ShieldAlert
} from 'lucide-react';

// ─── Types ───────────────────────────────────────────────────────────────────

interface AdminLogItem {
  id: number;
  timestampUtc: string;
  level: string;
  source: string;
  eventType: string;
  message: string;
  correlationId?: string;
  userEmail?: string;
  hasExceptionDetail: boolean;
  hasPayload: boolean;
}

interface AdminLogDetail extends AdminLogItem {
  exceptionDetail?: string;
  payload?: string;
}

interface LogsResponse {
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  items: AdminLogItem[];
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

const LEVELS = ['Information', 'Warning', 'Error', 'Critical'];

const LEVEL_META: Record<string, { label: string; color: string; icon: React.ReactNode }> = {
  Information: { label: 'Info', color: 'var(--color-status-blue)', icon: <Info size={12} /> },
  Warning: { label: 'Aviso', color: 'var(--color-status-orange)', icon: <AlertTriangle size={12} /> },
  Error: { label: 'Erro', color: 'var(--color-status-red)', icon: <XCircle size={12} /> },
  Critical: { label: 'Crítico', color: 'var(--color-status-purple)', icon: <ShieldAlert size={12} /> },
};

function SourceBadge({ source }: { source: string }) {
  const isFrontend = source.startsWith('Frontend');
  if (!isFrontend) return <span style={{ color: '#6b7280' }}>{source}</span>;
  
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center',
      padding: '2px 6px', borderRadius: 4,
      background: '#ecfdf5', color: '#059669',
      border: '1px solid #10b98144',
      fontSize: 10, fontWeight: 800, textTransform: 'uppercase'
    }}>
      {source}
    </span>
  );
}

function LevelBadge({ level }: { level: string }) {
  const meta = LEVEL_META[level] ?? { label: level, color: '#6b7280', icon: <Info size={12} /> };
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', gap: 4,
      padding: '2px 8px', borderRadius: 4,
      background: meta.color + '18', color: meta.color,
      border: `1px solid ${meta.color}44`,
      fontSize: 11, fontWeight: 700, letterSpacing: '0.05em', textTransform: 'uppercase'
    }}>
      {meta.icon}{meta.label}
    </span>
  );
}

function formatDate(iso: string) {
  const d = new Date(iso + (iso.endsWith('Z') ? '' : 'Z'));
  return d.toLocaleString('pt-PT', { dateStyle: 'short', timeStyle: 'medium' });
}

// ─── Component ───────────────────────────────────────────────────────────────

export function SystemLogs() {
  // Filter state
  const [level, setLevel] = useState('');
  const [source, setSource] = useState('');
  const [eventType, setEventType] = useState('');
  const [correlationId, setCorrelationId] = useState('');
  const [search, setSearch] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [page, setPage] = useState(1);

  // Data state
  const [data, setData] = useState<LogsResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Detail modal state
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [detail, setDetail] = useState<AdminLogDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  // ── Fetch logs ──────────────────────────────────────────────────────────────
  const fetchLogs = useCallback(async (targetPage = 1) => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams();
      if (level) params.set('level', level);
      if (source) params.set('source', source);
      if (eventType) params.set('eventType', eventType);
      if (correlationId) params.set('correlationId', correlationId);
      if (search) params.set('search', search);
      if (startDate) params.set('startDate', new Date(startDate).toISOString());
      if (endDate) params.set('endDate', new Date(endDate + 'T23:59:59').toISOString());
      params.set('page', String(targetPage));
      params.set('pageSize', '50');

      const res = await apiFetch(`${API_BASE_URL}/api/admin/logs?${params}`);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const json: LogsResponse = await res.json();
      setData(json);
      setPage(targetPage);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Erro ao carregar logs.');
    } finally {
      setLoading(false);
    }
  }, [level, source, eventType, correlationId, search, startDate, endDate]);

  useEffect(() => { fetchLogs(1); }, []);

  // ── Fetch detail ─────────────────────────────────────────────────────────────
  useEffect(() => {
    if (selectedId === null) { setDetail(null); return; }
    setDetailLoading(true);
    apiFetch(`${API_BASE_URL}/api/admin/logs/${selectedId}`)
      .then(r => r.ok ? r.json() : Promise.reject(`HTTP ${r.status}`))
      .then((d: AdminLogDetail) => setDetail(d))
      .catch(() => setDetail(null))
      .finally(() => setDetailLoading(false));
  }, [selectedId]);

  const handleSearch = (e: React.FormEvent) => { e.preventDefault(); fetchLogs(1); };
  const clearFilters = () => {
    setLevel(''); setSource(''); setEventType('');
    setCorrelationId(''); setSearch(''); setStartDate(''); setEndDate('');
    setTimeout(() => fetchLogs(1), 0);
  };

  // ─── Styles ─────────────────────────────────────────────────────────────────
  const s = {
    page: { display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', minWidth: 0 } as React.CSSProperties,
    header: { display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px', width: '100%', minWidth: 0 } as React.CSSProperties,
    title: { margin: 0, fontSize: '2.5rem', fontWeight: 900, color: 'var(--color-primary)', fontFamily: 'var(--font-family-display)', textTransform: 'uppercase', letterSpacing: '-0.02em', display: 'flex', alignItems: 'center', gap: 16 } as React.CSSProperties,
    subtitle: { margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase', fontSize: '0.875rem' } as React.CSSProperties,
    filtersCard: { backgroundColor: 'var(--color-bg-surface)', padding: '16px', boxShadow: 'var(--shadow-brutal)', border: '2px solid var(--color-primary)', display: 'flex', flexDirection: 'column', gap: '16px' } as React.CSSProperties,
    filtersGrid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 12 } as React.CSSProperties,
    input: { width: '100%', padding: '10px 12px', border: '2px solid var(--color-border)', fontSize: '0.85rem', boxSizing: 'border-box', fontWeight: 600, outline: 'none' } as React.CSSProperties,
    select: { width: '100%', padding: '10px 12px', border: '2px solid var(--color-border)', fontSize: '0.85rem', background: '#fff', boxSizing: 'border-box', fontWeight: 600, outline: 'none' } as React.CSSProperties,
    btnPrimary: { padding: '0.75rem 1.5rem', background: 'var(--color-primary)', color: '#fff', border: 'none', fontWeight: 800, cursor: 'pointer', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: 8, textTransform: 'uppercase', boxShadow: '4px 4px 0px var(--color-border-heavy)' } as React.CSSProperties,
    btnSecondary: { padding: '0.75rem 1rem', background: '#fff', color: 'var(--color-text-muted)', border: '2px solid var(--color-border)', fontWeight: 700, cursor: 'pointer', fontSize: '0.85rem', textTransform: 'uppercase' } as React.CSSProperties,
    table: { width: '100%', borderCollapse: 'collapse' as const, fontSize: '0.85rem' },
    th: { padding: '12px', background: 'var(--color-bg-main)', color: 'var(--color-text-main)', fontWeight: 800, textAlign: 'left' as const, fontSize: '0.75rem', letterSpacing: '0.05em', textTransform: 'uppercase' as const, borderBottom: '2px solid var(--color-border-heavy)' },
    td: { padding: '12px', borderBottom: '1px solid var(--color-border-light)', verticalAlign: 'top' as const },
    trHover: { background: 'var(--color-bg-surface)' },
    pagination: { display: 'flex', alignItems: 'center', gap: 8, marginTop: 16, justifyContent: 'flex-end' } as React.CSSProperties,
    modalOverlay: { position: 'fixed' as const, inset: 0, background: 'rgba(0,0,0,0.6)', display: 'flex', alignItems: 'flex-start', justifyContent: 'flex-end', zIndex: 1000, padding: 24 },
    modalBox: { background: '#fff', width: '100%', maxWidth: 560, maxHeight: '90vh', overflowY: 'auto' as const, borderRadius: 0, border: '2px solid var(--color-border-heavy)', boxShadow: 'var(--shadow-brutal)', padding: 24 },
    labelSm: { fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.05em', marginBottom: 4, display: 'block' },
    codeBlock: { background: 'var(--color-bg-main)', padding: 12, border: '1px solid var(--color-border)', fontSize: '0.75rem', fontFamily: 'monospace', whiteSpace: 'pre-wrap' as const, wordBreak: 'break-word' as const, overflowX: 'auto' as const },
  };

  // ─── Render ──────────────────────────────────────────────────────────────────
  return (
    <div style={s.page}>
      {/* Header */}
      <div style={s.header}>
        <div style={s.title}>
          <FileText size={20} /> Logs do Sistema
        </div>
        <p style={s.subtitle}>
          Registo de eventos operacionais auditáveis. Utilize o Correlation ID para rastrear uma falha específica.
        </p>
      </div>

      {/* Filters */}
      <form onSubmit={handleSearch} style={s.filtersCard}>
        <div style={s.filtersGrid}>
          <div>
            <label style={s.labelSm}>Nível</label>
            <select style={s.select} value={level} onChange={e => setLevel(e.target.value)}>
              <option value="">Todos</option>
              {LEVELS.map(l => <option key={l} value={l}>{LEVEL_META[l]?.label ?? l}</option>)}
            </select>
          </div>
          <div>
            <label style={s.labelSm}>Fonte</label>
            <input style={s.input} placeholder="ex: DocumentExtraction..." value={source} onChange={e => setSource(e.target.value)} />
          </div>
          <div>
            <label style={s.labelSm}>Tipo de Evento</label>
            <input style={s.input} placeholder="ex: OCR_SETTINGS_SAVED" value={eventType} onChange={e => setEventType(e.target.value)} />
          </div>
          <div>
            <label style={s.labelSm}>Correlation ID</label>
            <input style={s.input} placeholder="ID exato" value={correlationId} onChange={e => setCorrelationId(e.target.value)} />
          </div>
          <div>
            <label style={s.labelSm}>Data Início</label>
            <input style={s.input} type="date" value={startDate} onChange={e => setStartDate(e.target.value)} />
          </div>
          <div>
            <label style={s.labelSm}>Data Fim</label>
            <input style={s.input} type="date" value={endDate} onChange={e => setEndDate(e.target.value)} />
          </div>
          <div style={{ gridColumn: '1 / -1' }}>
            <label style={s.labelSm}>Pesquisa livre</label>
            <input style={s.input} placeholder="Pesquisar na mensagem, fonte ou tipo..." value={search} onChange={e => setSearch(e.target.value)} />
          </div>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button type="submit" style={s.btnPrimary} disabled={loading}>
            <Search size={14} /> {loading ? 'A carregar...' : 'Pesquisar'}
          </button>
          <button type="button" style={s.btnSecondary} onClick={clearFilters}>Limpar</button>
          <button type="button" style={{ ...s.btnSecondary, marginLeft: 'auto' }} onClick={() => fetchLogs(page)}>
            <RefreshCw size={13} />
          </button>
        </div>
      </form>

      {/* Error */}
      {error && (
        <div style={{ background: 'var(--color-status-red)18', border: '2px solid var(--color-status-red)', padding: '10px 16px', marginBottom: 16, color: 'var(--color-status-red)', fontSize: '0.85rem', fontWeight: 700 }}>
          {error}
        </div>
      )}

      {/* Table */}
      <div style={{ overflowX: 'auto', border: '2px solid var(--color-border-heavy)', boxShadow: 'var(--shadow-brutal)' }}>
        <table style={s.table}>
          <thead>
            <tr>
              <th style={s.th}>Data / Hora</th>
              <th style={s.th}>Nível</th>
              <th style={s.th}>Fonte</th>
              <th style={s.th}>Tipo de Evento</th>
              <th style={s.th}>Mensagem</th>
              <th style={s.th}>Correlation ID</th>
              <th style={{ ...s.th, width: 48 }}></th>
            </tr>
          </thead>
          <tbody>
            {!loading && (!data || data.items.length === 0) && (
              <tr>
                <td colSpan={7} style={{ ...s.td, textAlign: 'center', color: 'var(--color-text-muted)', padding: 32, fontWeight: 600 }}>
                  {data ? 'Nenhum log encontrado com os filtros aplicados.' : 'Execute uma pesquisa para carregar logs.'}
                </td>
              </tr>
            )}
            {data?.items.map((entry, i) => (
              <tr key={entry.id} style={i % 2 === 0 ? {} : s.trHover}>
                <td style={{ ...s.td, whiteSpace: 'nowrap', color: 'var(--color-text-muted)', fontSize: '0.75rem', fontWeight: 600 }}>{formatDate(entry.timestampUtc)}</td>
                <td style={s.td}><LevelBadge level={entry.level} /></td>
                <td style={s.td}><SourceBadge source={entry.source} /></td>
                <td style={{ ...s.td, fontFamily: 'monospace', fontSize: '0.7rem', color: 'var(--color-text-main)', fontWeight: 600 }}>{entry.eventType}</td>
                <td style={{ ...s.td, maxWidth: 300 }}>
                  <span style={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                    {entry.message}
                  </span>
                  {(entry.hasExceptionDetail || entry.hasPayload) && (
                    <span style={{ fontSize: '0.65rem', color: 'var(--color-status-red)', fontWeight: 800, marginLeft: 4, textTransform: 'uppercase' }}>+ detalhe</span>
                  )}
                </td>
                <td style={{ ...s.td, fontFamily: 'monospace', fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>{entry.correlationId ?? '—'}</td>
                <td style={s.td}>
                  <button
                    id={`log-detail-btn-${entry.id}`}
                    title="Ver detalhe"
                    onClick={() => setSelectedId(entry.id)}
                    style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-primary)', padding: 4 }}
                  >
                    <Eye size={18} strokeWidth={2.5} />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {data && data.totalPages > 1 && (
        <div style={s.pagination}>
          <span style={{ fontSize: 12, color: '#6b7280' }}>
            {data.totalCount} registos · página {data.page} de {data.totalPages}
          </span>
          <button style={s.btnSecondary} disabled={page <= 1 || loading} onClick={() => fetchLogs(page - 1)}>
            <ChevronLeft size={14} />
          </button>
          <button style={s.btnSecondary} disabled={page >= data.totalPages || loading} onClick={() => fetchLogs(page + 1)}>
            <ChevronRight size={14} />
          </button>
        </div>
      )}
      {data && data.totalPages <= 1 && (
        <div style={{ fontSize: 12, color: '#9ca3af', marginTop: 12, textAlign: 'right' }}>
          {data.totalCount} registo(s)
        </div>
      )}

      {/* Detail Modal */}
      {selectedId !== null && (
        <div style={s.modalOverlay} onClick={e => { if (e.target === e.currentTarget) setSelectedId(null); }}>
          <div style={s.modalBox}>
            {/* Modal header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
              <h2 style={{ fontSize: 16, fontWeight: 800, margin: 0 }}>Detalhe do Log #{selectedId}</h2>
              <button onClick={() => setSelectedId(null)} style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#6b7280' }}>
                <X size={20} />
              </button>
            </div>

            {detailLoading && <p style={{ color: '#9ca3af', fontSize: 13 }}>A carregar...</p>}
            {!detailLoading && !detail && <p style={{ color: '#ef4444', fontSize: 13 }}>Erro ao carregar detalhe.</p>}

            {detail && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                {/* Summary row */}
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
                  <div>
                    <div style={s.labelSm}>Nível</div>
                    <LevelBadge level={detail.level} />
                  </div>
                  <div>
                    <div style={s.labelSm}>Data / Hora</div>
                    <div style={{ fontSize: 13 }}>{formatDate(detail.timestampUtc)}</div>
                  </div>
                  <div>
                    <div style={s.labelSm}>Tipo de Evento</div>
                    <code style={{ fontSize: 12, background: '#f3f4f6', padding: '2px 6px', borderRadius: 4 }}>{detail.eventType}</code>
                  </div>
                  <div>
                    <div style={s.labelSm}>Fonte</div>
                    <div style={{ fontSize: 12, color: '#374151' }}>{detail.source}</div>
                  </div>
                  <div>
                    <div style={s.labelSm}>Correlation ID</div>
                    <code style={{ fontSize: 12, background: '#f3f4f6', padding: '2px 6px', borderRadius: 4 }}>{detail.correlationId ?? '—'}</code>
                  </div>
                  <div>
                    <div style={s.labelSm}>Utilizador</div>
                    <div style={{ fontSize: 13, color: '#374151' }}>{detail.userEmail ?? '—'}</div>
                  </div>
                </div>

                {/* Message */}
                <div>
                  <div style={s.labelSm}>Mensagem</div>
                  <div style={{ fontSize: 13, color: '#111', lineHeight: 1.5 }}>{detail.message}</div>
                </div>

                {/* Exception Detail */}
                {detail.exceptionDetail && (
                  <div>
                    <div style={{ ...s.labelSm, color: '#dc2626' }}>Detalhe da Exceção</div>
                    <div style={s.codeBlock}>{detail.exceptionDetail}</div>
                  </div>
                )}

                {/* Payload */}
                {detail.payload && (
                  <div>
                    <div style={s.labelSm}>Payload (sanitizado)</div>
                    <div style={s.codeBlock}>
                      {(() => {
                        try { return JSON.stringify(JSON.parse(detail.payload!), null, 2); }
                        catch { return detail.payload; }
                      })()}
                    </div>
                  </div>
                )}

                {/* Correlation hint */}
                {detail.correlationId && (
                  <div style={{ borderTop: '1px solid #e5e7eb', paddingTop: 12 }}>
                    <div style={{ fontSize: 12, color: '#6b7280', display: 'flex', alignItems: 'center', gap: 6 }}>
                      <Info size={13} />
                      Para ver todos os eventos desta operação, pesquise pelo Correlation ID:&nbsp;
                      <button
                        style={{ fontFamily: 'monospace', fontSize: 11, background: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: 4, padding: '1px 6px', cursor: 'pointer', color: '#1d4ed8' }}
                        onClick={() => { setCorrelationId(detail.correlationId!); setSelectedId(null); fetchLogs(1); }}
                      >
                        {detail.correlationId}
                      </button>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
