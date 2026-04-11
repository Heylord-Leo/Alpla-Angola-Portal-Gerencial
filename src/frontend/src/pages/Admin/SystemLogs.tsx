import React, { useState, useEffect, useCallback } from 'react';
import { apiFetch, API_BASE_URL } from '../../lib/api';
import { Z_INDEX } from '../../constants/ui';
import { DropdownPortal } from '../../components/ui/DropdownPortal';
import {
  FileText, Search, Eye, AlertTriangle, AlertCircle, Info, XCircle,
  ChevronLeft, ChevronRight, X, RefreshCw, ShieldAlert, Download, Copy, Play, Pause, Activity
} from 'lucide-react';
import { BarChart, Bar, XAxis, Tooltip as RechartsTooltip, ResponsiveContainer, YAxis } from 'recharts';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { StandardTable } from '../../components/ui/StandardTable';

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

interface ActivityItem {
  periodLabel: string;
  infoCount: number;
  warningCount: number;
  errorCount: number;
  totalCount: number;
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
  const [activity, setActivity] = useState<ActivityItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  
  const [autoRefresh, setAutoRefresh] = useState(false);

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

      // Fetch activity
      const actRes = await apiFetch(`${API_BASE_URL}/api/admin/logs/activity?${params}`);
      if (actRes.ok) {
        setActivity(await actRes.json());
      }

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

  const applyPreset = (preset: string) => {
    clearFilters();
    if (preset === '1H') {
      const start = new Date();
      start.setHours(start.getHours() - 1);
      setStartDate(start.toISOString().split('T')[0]); // Fallback as datestring works poorly for hour here in UI without time input, but let's just trigger load
      setTimeout(() => fetchLogs(1), 0);
    }
    if (preset === 'ERRORS_24H') {
      setLevel('Error');
      const start = new Date();
      start.setDate(start.getDate() - 1);
      setStartDate(start.toISOString().split('T')[0]);
      setTimeout(() => fetchLogs(1), 0);
    }
  };

  const handleExport = () => {
    const params = new URLSearchParams();
    if (level) params.set('level', level);
    if (source) params.set('source', source);
    if (eventType) params.set('eventType', eventType);
    if (correlationId) params.set('correlationId', correlationId);
    if (search) params.set('search', search);
    if (startDate) params.set('startDate', new Date(startDate).toISOString());
    if (endDate) params.set('endDate', new Date(endDate + 'T23:59:59').toISOString());
    
    window.location.href = `${API_BASE_URL}/api/admin/logs/export?${params.toString()}`;
  };

  useEffect(() => {
    if (!autoRefresh) return;
    const interval = setInterval(() => {
      fetchLogs(1); // poll newest page 1
    }, 10000); // 10s
    return () => clearInterval(interval);
  }, [autoRefresh, fetchLogs]);

  const copyToClipboard = (text: string) => {
    navigator.clipboard.writeText(text);
  };

  const detailParsedPayload = React.useMemo(() => {
    if (!detail?.payload) return null;
    try { return JSON.parse(detail.payload); } catch { return null; }
  }, [detail]);
  const isOcrLog = detail?.eventType === 'OCR_EXECUTION' && detailParsedPayload !== null;

  // ─── Styles ─────────────────────────────────────────────────────────────────
  const s = {
    page: { display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', minWidth: 0 } as React.CSSProperties,
    header: { display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px', width: '100%', minWidth: 0 } as React.CSSProperties,
    title: { margin: 0, fontSize: '2.5rem', fontWeight: 900, color: 'var(--color-primary)', fontFamily: 'var(--font-family-display)', textTransform: 'uppercase', letterSpacing: '-0.02em', display: 'flex', alignItems: 'center', gap: 16 } as React.CSSProperties,
    subtitle: { margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase', fontSize: '0.875rem' } as React.CSSProperties,
    filtersCard: { backgroundColor: 'var(--color-bg-surface)', padding: '16px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', display: 'flex', flexDirection: 'column', gap: '16px' } as React.CSSProperties,
    filtersGrid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 12 } as React.CSSProperties,
    input: { width: '100%', padding: '10px 12px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)', fontSize: '0.85rem', boxSizing: 'border-box', fontWeight: 600, outline: 'none' } as React.CSSProperties,
    select: { width: '100%', padding: '10px 12px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)', fontSize: '0.85rem', background: 'var(--color-bg-surface)', boxSizing: 'border-box', fontWeight: 600, outline: 'none' } as React.CSSProperties,
    btnPrimary: { padding: '0.75rem 1.5rem', background: 'var(--color-primary)', color: '#fff', border: 'none', borderRadius: 'var(--radius-md)', fontWeight: 800, cursor: 'pointer', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: 8, textTransform: 'uppercase' } as React.CSSProperties,
    btnSecondary: { padding: '0.75rem 1rem', background: 'var(--color-bg-surface)', color: 'var(--color-text-muted)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)', fontWeight: 700, cursor: 'pointer', fontSize: '0.85rem', textTransform: 'uppercase' } as React.CSSProperties,
    pagination: { display: 'flex', alignItems: 'center', gap: 8, marginTop: 16, justifyContent: 'flex-end' } as React.CSSProperties,
    modalOverlay: { position: 'fixed' as const, inset: 0, background: 'rgba(0,0,0,0.6)', display: 'flex', alignItems: 'flex-start', justifyContent: 'flex-end', zIndex: Z_INDEX.MODAL, padding: 24 },
    modalBox: { background: 'var(--color-bg-surface)', width: '100%', maxWidth: 560, maxHeight: '90vh', overflowY: 'auto' as const, borderRadius: 0, border: '2px solid var(--color-border-heavy)', boxShadow: 'var(--shadow-brutal)', padding: 24 },
    labelSm: { fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.05em', marginBottom: 4, display: 'block' },
    codeBlock: { background: 'var(--color-bg-main)', padding: 12, border: '1px solid var(--color-border)', fontSize: '0.75rem', fontFamily: 'monospace', whiteSpace: 'pre-wrap' as const, wordBreak: 'break-word' as const, overflowX: 'auto' as const },
  };

    // Columns moved directly to render function for StandardTable

  // ─── Render ──────────────────────────────────────────────────────────────────
  return (
    <PageContainer>
      {/* Header */}
      <PageHeader
        title="Logs do Sistema"
        icon={<FileText size={32} strokeWidth={2.5} />}
        subtitle="Registo de eventos operacionais auditáveis."
        actions={
          <button onClick={() => applyPreset('ERRORS_24H')} style={{...s.btnSecondary, padding: '6px 12px', fontSize: 13}}>Erros (Últimas 24h)</button>
        }
      />

      {/* Activity Graph */}
      {activity.length > 0 && (
         <div style={{...s.filtersCard, padding: '16px 20px', height: '140px'}}>
             <div style={{ fontSize: 12, fontWeight: 800, color: 'var(--color-primary)', textTransform: 'uppercase', marginBottom: 8, display: 'flex', gap: 8, alignItems: 'center' }}>
                 <Activity size={14} /> Histograma de Atividade
             </div>
             <ResponsiveContainer width="100%" height="100%">
               <BarChart data={activity} margin={{ top: 0, right: 0, left: -24, bottom: 0 }}>
                 <XAxis dataKey="periodLabel" tick={{ fontSize: 10, fill: '#6b7280' }} axisLine={false} tickLine={false} />
                 <YAxis tick={{ fontSize: 10, fill: '#6b7280' }} axisLine={false} tickLine={false} />
                 <RechartsTooltip cursor={{ fill: '#f3f4f6' }} contentStyle={{ fontSize: 12, borderRadius: 4, padding: '8px' }} />
                 <Bar dataKey="errorCount" name="Erros" stackId="a" fill="var(--color-status-red)" />
                 <Bar dataKey="warningCount" name="Avisos" stackId="a" fill="var(--color-status-orange)" />
                 <Bar dataKey="infoCount" name="Info" stackId="a" fill="var(--color-status-blue)" />
               </BarChart>
             </ResponsiveContainer>
         </div>
      )}

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
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <button type="submit" style={s.btnPrimary} disabled={loading}>
            <Search size={14} /> {loading ? 'Carregando...' : 'Pesquisar'}
          </button>
          <button type="button" style={s.btnSecondary} onClick={clearFilters}>Limpar</button>
          
          <div style={{ flex: 1 }} />
          
          <button type="button" style={{ ...s.btnSecondary, display: 'flex', alignItems: 'center', gap: 4, background: autoRefresh ? '#ecfdf5' : '#fff', color: autoRefresh ? '#059669' : '' }} onClick={() => setAutoRefresh(!autoRefresh)}>
            {autoRefresh ? <Pause size={14} /> : <Play size={14} />}
            {autoRefresh ? 'Live Tail' : 'Desligado'}
          </button>
          <button type="button" style={{ ...s.btnSecondary }} onClick={handleExport} title="Export CSV">
            <Download size={15} /> Exportar
          </button>
          <button type="button" style={{ ...s.btnSecondary }} onClick={() => fetchLogs(page)}>
            <RefreshCw size={13} className={loading && !autoRefresh ? "animate-spin" : ""} />
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
      <StandardTable
        loading={loading}
        loadingState={<div style={{ padding: '64px', textAlign: 'center', color: 'var(--color-text-muted)' }}>Carregando...</div>}
        isEmpty={!data?.items || data.items.length === 0}
        emptyState={
          <div style={{ padding: '64px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
            Nenhum log encontrado com os filtros aplicados.
          </div>
        }
      >
        <thead>
          <tr style={{ borderBottom: '2px solid var(--color-border)', textAlign: 'left', color: 'var(--color-text-muted)' }}>
            <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Data / Hora</th>
            <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Nível</th>
            <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Fonte</th>
            <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Tipo de Evento</th>
            <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Mensagem</th>
            <th style={{ padding: '16px', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase' }}>Correlation ID</th>
            <th style={{ padding: '16px', width: '48px' }}></th>
          </tr>
        </thead>
        <tbody style={{ backgroundColor: 'white' }}>
          {data?.items.map((entry) => (
            <tr key={entry.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
              <td style={{ padding: '16px' }}>
                <span style={{ whiteSpace: 'nowrap', color: 'var(--color-text-muted)', fontSize: '0.75rem', fontWeight: 600 }}>{formatDate(entry.timestampUtc)}</span>
              </td>
              <td style={{ padding: '16px' }}><LevelBadge level={entry.level} /></td>
              <td style={{ padding: '16px' }}><SourceBadge source={entry.source} /></td>
              <td style={{ padding: '16px' }}>
                <span style={{ fontFamily: 'monospace', fontSize: '0.7rem', color: 'var(--color-text-main)', fontWeight: 600 }}>{entry.eventType}</span>
              </td>
              <td style={{ padding: '16px' }}>
                <div style={{ maxWidth: 300 }}>
                  <span style={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                    {entry.message}
                  </span>
                  {(entry.hasExceptionDetail || entry.hasPayload) && (
                    <span style={{ fontSize: '0.65rem', color: 'var(--color-status-red)', fontWeight: 800, marginLeft: 4, textTransform: 'uppercase' }}>+ detalhe</span>
                  )}
                </div>
              </td>
              <td style={{ padding: '16px' }}>
                <span style={{ fontFamily: 'monospace', fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>{entry.correlationId ?? '—'}</span>
              </td>
              <td style={{ padding: '16px', textAlign: 'right' }}>
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
      </StandardTable>

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
        <DropdownPortal>
          <div style={s.modalOverlay} onClick={e => { if (e.target === e.currentTarget) setSelectedId(null); }}>
            <div style={s.modalBox}>
              {/* Modal header */}
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
                <h2 style={{ fontSize: 16, fontWeight: 800, margin: 0, textTransform: 'uppercase', color: 'var(--color-primary)' }}>Detalhe do Log #{selectedId}</h2>
                <button onClick={() => setSelectedId(null)} style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#6b7280' }}>
                  <X size={24} />
                </button>
              </div>

              {detailLoading && (
                <div style={{ padding: '40px 0', textAlign: 'center', color: '#9ca3af', fontSize: 13, fontWeight: 600 }}>
                  <RefreshCw size={24} className="animate-spin" style={{ margin: '0 auto 12px', display: 'block' }} />
                  Carregando detalhes...
                </div>
              )}
              
              {!detailLoading && !detail && (
                <div style={{ padding: '40px 0', textAlign: 'center', color: '#ef4444', fontSize: 13, fontWeight: 700 }}>
                  <AlertCircle size={24} style={{ margin: '0 auto 12px', display: 'block' }} />
                  Erro ao carregar detalhes do log.
                </div>
              )}

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
                      <div style={{ fontSize: 13, fontWeight: 700 }}>{formatDate(detail.timestampUtc)}</div>
                    </div>
                    <div>
                      <div style={s.labelSm}>Tipo de Evento</div>
                      <code style={{ fontSize: 12, background: 'var(--color-bg-page)', padding: '2px 6px', borderRadius: 4, fontWeight: 700 }}>{detail.eventType}</code>
                    </div>
                    <div>
                      <div style={s.labelSm}>Fonte</div>
                      <div style={{ fontSize: 12, color: '#374151', fontWeight: 600 }}>{detail.source}</div>
                    </div>
                    <div>
                      <div style={s.labelSm}>Correlation ID</div>
                      <code style={{ fontSize: 12, background: 'var(--color-bg-page)', padding: '2px 6px', borderRadius: 4 }}>{detail.correlationId ?? '—'}</code>
                    </div>
                    <div>
                      <div style={s.labelSm}>Utilizador</div>
                      <div style={{ fontSize: 13, color: '#374151', fontWeight: 600 }}>{detail.userEmail ?? '—'}</div>
                    </div>
                  </div>

                  {/* Message */}
                  <div>
                    <div style={s.labelSm}>Mensagem</div>
                    <div style={{ fontSize: 13, color: '#111', lineHeight: 1.5, fontWeight: 500 }}>{detail.message}</div>
                  </div>

                  {/* Exception Detail */}
                  {detail.exceptionDetail && (
                    <div>
                      <div style={{ ...s.labelSm, color: 'var(--color-status-red)' }}>Detalhe da Exceção</div>
                      <div style={s.codeBlock}>{detail.exceptionDetail}</div>
                    </div>
                  )}

                  {/* OCR Summary */}
                  {isOcrLog && (
                    <div style={{ background: 'var(--color-bg-page)', border: '1px solid #cbd5e1', padding: 16 }}>
                      <div style={{ ...s.labelSm, color: 'var(--color-primary)', marginBottom: 12 }}>Resumo de Execução OCR</div>
                      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(150px, 1fr))', gap: 12 }}>
                        
                        <div>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#64748b', textTransform: 'uppercase' }}>File Name</div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: '#0f172a', wordBreak: 'break-all' }}>{detailParsedPayload.fileName || '—'}</div>
                        </div>

                        <div>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#64748b', textTransform: 'uppercase' }}>Status</div>
                          <div style={{ marginTop: 2 }}>
                            {detailParsedPayload.executionStatus === 'Success' && <span style={{ background: '#ecfdf5', color: '#059669', padding: '2px 6px', borderRadius: 4, fontWeight: 700, fontSize: 11 }}>SUCESSO</span>}
                            {detailParsedPayload.executionStatus === 'Partial' && <span style={{ background: '#fffbeb', color: '#d97706', padding: '2px 6px', borderRadius: 4, fontWeight: 700, fontSize: 11 }}>PARCIAL</span>}
                            {detailParsedPayload.executionStatus === 'Failed' && <span style={{ background: '#fef2f2', color: '#dc2626', padding: '2px 6px', borderRadius: 4, fontWeight: 700, fontSize: 11 }}>FALHA</span>}
                            {!['Success', 'Partial', 'Failed'].includes(detailParsedPayload.executionStatus) && <span style={{ background: 'var(--color-bg-page)', color: '#4b5563', padding: '2px 6px', borderRadius: 4, fontWeight: 700, fontSize: 11 }}>{detailParsedPayload.executionStatus || '—'}</span>}
                          </div>
                        </div>

                        <div>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#64748b', textTransform: 'uppercase' }}>Tipo Documento</div>
                          <div style={{ marginTop: 2 }}>
                            {detailParsedPayload.documentType === 'contract' ? (
                               <span style={{ background: '#e0e7ff', color: '#4338ca', padding: '2px 6px', borderRadius: 4, fontWeight: 700, fontSize: 11 }}>CONTRATO</span>
                            ) : (
                               <span style={{ background: '#f1f5f9', color: '#475569', padding: '2px 6px', borderRadius: 4, fontWeight: 700, fontSize: 11 }}>{(detailParsedPayload.documentType || '—').toUpperCase()}</span>
                            )}
                          </div>
                        </div>

                        <div>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#64748b', textTransform: 'uppercase' }}>Estratégia / Rota</div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: '#0f172a' }}>{detailParsedPayload.detectedStrategy || '—'} <span style={{ color: '#94a3b8' }}>/</span> {detailParsedPayload.routingPath || '—'}</div>
                        </div>

                        <div>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#64748b', textTransform: 'uppercase' }}>Empresa Identificada (OCR)</div>
                          <div style={{ fontSize: 13, fontWeight: 700, color: detailParsedPayload.billedCompany ? 'var(--color-primary)' : '#dc2626' }}>
                            {detailParsedPayload.billedCompany || '⚠ NÃO IDENTIFICADA'}
                          </div>
                          {!detailParsedPayload.billedCompany && (
                            <div style={{ fontSize: 10, color: '#dc2626', marginTop: 2 }}>O OCR não conseguiu extrair a empresa do documento. Verifique o console do browser para diagnósticos de matching.</div>
                          )}
                        </div>

                        <div>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#64748b', textTransform: 'uppercase' }}>Provedor / Modelo</div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: '#0f172a' }}>{detailParsedPayload.provider || '—'} <span style={{ color: '#94a3b8' }}>/</span> {detailParsedPayload.model || '—'}</div>
                        </div>

                        <div>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#64748b', textTransform: 'uppercase' }}>Tokens</div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: '#0f172a' }}>{detailParsedPayload.totalTokens ?? 0} <span style={{ fontSize: 11, color: '#64748b' }}>({detailParsedPayload.promptTokens ?? 0} P / {detailParsedPayload.completionTokens ?? 0} C)</span></div>
                        </div>

                        <div>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#64748b', textTransform: 'uppercase' }}>Páginas & Texto Nato</div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: '#0f172a' }}>Págs: {detailParsedPayload.pagesProcessed ?? 0} <span style={{ color: '#94a3b8' }}>|</span> Nato: {detailParsedPayload.nativeTextDetected ? 'Sim' : 'Não'}</div>
                        </div>

                        <div>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#64748b', textTransform: 'uppercase' }}>Chunks</div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: '#0f172a' }}>{detailParsedPayload.chunkCount ?? 0}</div>
                        </div>

                      </div>

                      {detailParsedPayload.errorSummary && (
                        <div style={{ marginTop: 12, paddingTop: 12, borderTop: '1px solid #cbd5e1' }}>
                          <div style={{ fontSize: 10, fontWeight: 700, color: '#dc2626', textTransform: 'uppercase' }}>Resumo de Erro</div>
                          <div style={{ fontSize: 12, fontWeight: 600, color: '#7f1d1d', marginTop: 4 }}>{detailParsedPayload.errorSummary}</div>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Payload */}
                  {detail.payload && (
                    <div style={{ position: 'relative' }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
                          <span style={s.labelSm}>Payload (sanitizado)</span>
                          <button onClick={() => copyToClipboard(detail.payload!)} style={{ background: 'none', border: 'none', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 4, fontSize: 11, color: '#1d4ed8', fontWeight: 600 }}>
                            <Copy size={12} /> Copiar JSON
                          </button>
                      </div>
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
                          style={{ fontFamily: 'monospace', fontSize: 11, background: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: 4, padding: '1px 6px', cursor: 'pointer', color: '#1d4ed8', fontWeight: 700 }}
                          onClick={() => { setCorrelationId(detail.correlationId!); setSelectedId(null); fetchLogs(1); }}
                        >
                          {detail.correlationId}
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              )}
              
              {/* Footer action */}
              <div style={{ marginTop: 24, display: 'flex', justifyContent: 'flex-end' }}>
                <button
                  onClick={() => setSelectedId(null)}
                  style={{ ...s.btnSecondary, background: 'var(--color-bg-page)' }}
                >
                  FECHAR
                </button>
              </div>
            </div>
          </div>
        </DropdownPortal>
      )}
    </PageContainer>
  );
}
