# Menus de Administração - Alpla Portal

Aqui estão os conteúdos das 4 páginas da seção de Administração:

---

## 1. Workspace do Administrador
**Rota:** `/admin/workspace`
**Arquivo:** `src/frontend/src/pages/Admin/AdministratorWorkspace.tsx`

```tsx
import React from 'react';
import { Shield, FileText, Activity, Network, ChevronRight } from 'lucide-react';
import { Link } from 'react-router-dom';

interface AdminTileProps {
    to: string;
    icon: React.ReactNode;
    title: string;
    description: string;
    color: string;
}

function AdminTile({ to, icon, title, description, color }: AdminTileProps) {
    return (
        <Link 
            to={to} 
            style={{ 
                display: 'flex', 
                flexDirection: 'column', 
                padding: '2rem', 
                backgroundColor: 'var(--color-bg-surface)', 
                border: '2px solid var(--color-border-heavy)',
                boxShadow: 'var(--shadow-brutal)',
                transition: 'all 0.2s ease',
                cursor: 'pointer',
                textDecoration: 'none',
                color: 'inherit'
            }}
            className="admin-tile-hover"
        >
            <div style={{ 
                backgroundColor: color, 
                width: '48px', 
                height: '48px', 
                display: 'flex', 
                alignItems: 'center', 
                justifyContent: 'center', 
                color: 'white',
                marginBottom: '1.5rem',
                border: `2px solid ${color}`
            }}>
                {icon}
            </div>
            <h3 style={{ margin: '0 0 0.5rem 0', fontSize: '1.25rem', fontWeight: 800, textTransform: 'uppercase' }}>
                {title}
            </h3>
            <p style={{ margin: '0 0 1.5rem 0', color: 'var(--color-text-muted)', fontSize: '0.925rem', lineHeight: 1.5, flex: 1 }}>
                {description}
            </p>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontWeight: 700, fontSize: '0.875rem', textTransform: 'uppercase', color: 'var(--color-primary)' }}>
                Aceder <ChevronRight size={16} />
            </div>
        </Link>
    );
}

export function AdministratorWorkspace() {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
            <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', marginBottom: '0.5rem' }}>
                    <div style={{ 
                        backgroundColor: 'var(--color-primary)', 
                        padding: '0.5rem', 
                        display: 'flex', 
                        border: '2px solid var(--color-primary)',
                        color: 'white'
                    }}>
                        <Shield size={24} />
                    </div>
                    <div>
                        <h1 style={{ margin: 0, fontSize: '2.5rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            Workspace do Administrador
                        </h1>
                        <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase', fontSize: '0.875rem' }}>
                            Gestão técnica, diagnósticos e saúde do ecossistema Portal Gerencial
                        </p>
                    </div>
                </div>
            </header>

            <div style={{ 
                display: 'grid', 
                gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', 
                gap: '1.5rem' 
            }}>
                <AdminTile 
                    to="/admin/logs"
                    icon={<FileText size={24} />}
                    title="Logs do Sistema"
                    description="Visualize eventos, erros e atividades de execução do backend para troubleshooting."
                    color="var(--color-primary)"
                />
                <AdminTile 
                    to="/admin/diagnosis"
                    icon={<Activity size={24} />}
                    title="Diagnóstico de Serviços"
                    description="Verifique o estado de saúde e latência dos serviços internos e dependências core."
                    color="var(--color-status-purple)"
                />
                <AdminTile 
                    to="/admin/health"
                    icon={<Network size={24} />}
                    title="Saúde das Integrações"
                    description="Monitorize a integridade das comunicações com AlplaPROD e Primavera ERP."
                    color="var(--color-status-blue)"
                />
            </div>
            
            <style>{`
                .admin-tile-hover:hover {
                    transform: translateY(-4px);
                    box-shadow: var(--shadow-brutal-hover) !important;
                }
            `}</style>
        </div>
    );
}
```

---

## 2. Logs do Sistema
**Rota:** `/admin/logs`
**Arquivo:** `src/frontend/src/pages/Admin/SystemLogs.tsx`

```tsx
import React, { useState, useEffect, useCallback } from 'react';
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

      const res = await fetch(`http://localhost:5000/api/admin/logs?${params}`);
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
    fetch(`http://localhost:5000/api/admin/logs/${selectedId}`)
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
```

---

## 3. Diagnóstico de Serviços
**Rota:** `/admin/diagnosis`
**Arquivo:** `src/frontend/src/pages/Admin/ServiceDiagnosis.tsx`

```tsx
import { useState, useEffect } from 'react';
import { Activity, ArrowLeft, CheckCircle2, XCircle, AlertCircle, Settings } from 'lucide-react';
import { Link } from 'react-router-dom';
import { api } from '../../lib/api';

interface ServiceStatus {
    status: string;
    configured: boolean;
    reachable: boolean;
    healthy: boolean;
    message?: string;
}

interface DiagnosisData {
    backend: ServiceStatus;
    database: ServiceStatus;
    localOcr: ServiceStatus;
    openAi: ServiceStatus;
}

export function ServiceDiagnosis() {
    const [data, setData] = useState<DiagnosisData | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const fetchHealth = async () => {
        setLoading(true);
        try {
            const result = await api.admin.diagnostics.getHealth();
            setData(result);
            setError(null);
        } catch (err: any) {
            setError('Falha ao obter dados de diagnóstico.');
            console.error(err);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchHealth();
    }, []);

    const StatusCard = ({ title, status, icon: Icon }: { title: string, status: ServiceStatus, icon: any }) => {
        const getStatusColor = () => {
            if (status.status === 'Healthy') return 'var(--color-status-green)';
            if (status.status === 'Unhealthy' || status.status === 'Unreachable') return 'var(--color-status-red)';
            if (status.status === 'Configured') return 'var(--color-status-blue)';
            return 'var(--color-text-muted)';
        };

        const getStatusIcon = () => {
            if (status.healthy && status.reachable) return <CheckCircle2 size={20} color="var(--color-status-green)" />;
            if (!status.configured) return <Settings size={20} color="var(--color-text-muted)" />;
            if (!status.reachable) return <XCircle size={20} color="var(--color-status-red)" />;
            return <AlertCircle size={20} color="var(--color-status-orange)" />;
        };

        return (
            <div style={{ 
                backgroundColor: 'var(--color-bg-surface)', 
                border: '2px solid var(--color-border-heavy)', 
                padding: '1.5rem',
                boxShadow: 'var(--shadow-brutal)',
                display: 'flex',
                flexDirection: 'column',
                gap: '1rem'
            }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                        <div style={{ padding: '0.5rem', backgroundColor: 'var(--color-bg-main)', border: '1px solid var(--color-border-heavy)' }}>
                            <Icon size={20} />
                        </div>
                        <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 800, textTransform: 'uppercase' }}>{title}</h3>
                    </div>
                    {getStatusIcon()}
                </div>

                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem' }}>
                    <Badge label="Configurado" active={status.configured} />
                    <Badge label="Acessível" active={status.reachable} />
                    <Badge label="Saudável" active={status.healthy} />
                </div>

                {status.message && (
                    <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-status-red)', fontWeight: 500 }}>
                        {status.message}
                    </p>
                )}

                <div style={{ marginTop: 'auto', paddingTop: '0.5rem', borderTop: '1px dashed var(--color-border-light)' }}>
                    <span style={{ fontSize: '0.75rem', fontWeight: 700, color: getStatusColor(), textTransform: 'uppercase' }}>
                        Estado: {status.status}
                    </span>
                </div>
            </div>
        );
    };

    const Badge = ({ label, active }: { label: string, active: boolean }) => (
        <span style={{ 
            fontSize: '0.7rem', 
            fontWeight: 700, 
            padding: '2px 6px',
            border: `1px solid ${active ? 'var(--color-border-heavy)' : 'var(--color-border-light)'}`,
            backgroundColor: active ? 'var(--color-bg-main)' : 'transparent',
            color: active ? 'var(--color-text-main)' : 'var(--color-text-muted)',
            textTransform: 'uppercase',
            opacity: active ? 1 : 0.5
        }}>
            {label}
        </span>
    );

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
            <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
                    <Link to="/admin/workspace" style={{ color: 'var(--color-text-muted)', display: 'flex' }}>
                        <ArrowLeft size={24} />
                    </Link>
                    <div style={{ 
                        backgroundColor: 'var(--color-status-purple)', 
                        padding: '0.5rem', 
                        display: 'flex', 
                        border: '2px solid var(--color-status-purple)',
                        color: 'white'
                    }}>
                        <Activity size={24} />
                    </div>
                    <div>
                        <h1 style={{ margin: 0, fontSize: '2.5rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            Diagnóstico de Serviços
                        </h1>
                        <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase', fontSize: '0.875rem' }}>
                            Estado de saúde e conectividade do ecossistema
                        </p>
                    </div>
                </div>
                <button 
                    onClick={fetchHealth}
                    disabled={loading}
                    style={{
                        padding: '0.75rem 1.5rem',
                        backgroundColor: 'var(--color-bg-surface)',
                        border: '2px solid var(--color-border-heavy)',
                        fontWeight: 800,
                        textTransform: 'uppercase',
                        cursor: 'pointer',
                        boxShadow: '4px 4px 0px var(--color-border-heavy)',
                        transition: 'all 0.1s ease-in-out'
                    }}
                    onMouseDown={(e) => {
                        e.currentTarget.style.transform = 'translate(2px, 2px)';
                        e.currentTarget.style.boxShadow = '2px 2px 0px var(--color-border-heavy)';
                    }}
                    onMouseUp={(e) => {
                        e.currentTarget.style.transform = 'translate(0px, 0px)';
                        e.currentTarget.style.boxShadow = '4px 4px 0px var(--color-border-heavy)';
                    }}
                >
                    {loading ? 'A verificar...' : 'Actualizar'}
                </button>
            </header>

            {error && (
                <div style={{ padding: '1rem', backgroundColor: '#fee2e2', border: '2px solid #ef4444', color: '#b91c1c', fontWeight: 700 }}>
                    {error}
                </div>
            )}

            <div style={{ 
                display: 'grid', 
                gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', 
                gap: '1.5rem' 
            }}>
                {loading && !data ? (
                    Array(4).fill(0).map((_, i) => (
                        <div key={i} style={{ height: '180px', backgroundColor: 'var(--color-bg-surface)', border: '2px solid var(--color-border-light)', opacity: 0.5 }}></div>
                    ))
                ) : data ? (
                    <>
                        <StatusCard title="Backend API" status={data.backend} icon={Activity} />
                        <StatusCard title="Base de Dados" status={data.database} icon={Activity} />
                        <StatusCard title="Serviço OCR" status={data.localOcr} icon={Activity} />
                        <StatusCard title="OpenAI API" status={data.openAi} icon={Activity} />
                    </>
                ) : null}
            </div>

            <section style={{ 
                backgroundColor: 'var(--color-bg-main)', 
                border: '2px solid var(--color-border-heavy)', 
                padding: '1.5rem',
                marginTop: '1rem'
            }}>
                <h3 style={{ fontSize: '0.875rem', fontWeight: 800, textTransform: 'uppercase', marginBottom: '1rem' }}>Notas de Diagnóstico</h3>
                <ul style={{ margin: 0, paddingLeft: '1.25rem', fontSize: '0.875rem', color: 'var(--color-text-muted)', display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                    <li>A verificação de conectividade é realizada em tempo real a partir do servidor central.</li>
                    <li>O <b>Serviço OCR</b> e <b>OpenAI</b> são validados apenas se estiverem configurados como o provedor activo.</li>
                    <li>As credenciais não são expostas neste painel por motivos de segurança.</li>
                </ul>
            </section>
        </div>
    );
}
```

---

## 4. Saúde das Integrações
**Rota:** `/admin/health`
**Arquivo:** `src/frontend/src/pages/Admin/IntegrationHealth.tsx`

```tsx
import { useState, useEffect } from 'react';
import { Network, ArrowLeft, Database, Search, Cpu } from 'lucide-react';
import { Link } from 'react-router-dom';
import { api } from '../../lib/api';

export function IntegrationHealth() {
    const [ocrStatus, setOcrStatus] = useState<any>(null);

    useEffect(() => {
        api.admin.diagnostics.getHealth().then(data => {
            setOcrStatus(data.localOcr.status === 'Healthy' || data.openAi.status === 'Healthy' ? 'Operacional' : 'Indisponível');
        }).catch(() => setOcrStatus('Erro na verificação'));
    }, []);

    const IntegrationCard = ({ name, type, status, description, isRoadmap }: { name: string, type: string, status: string, description: string, isRoadmap?: boolean }) => (
        <div style={{ 
            backgroundColor: isRoadmap ? 'var(--color-bg-main)' : 'var(--color-bg-surface)', 
            border: `2px solid ${isRoadmap ? 'var(--color-border-light)' : 'var(--color-border-heavy)'}`, 
            padding: '1.5rem',
            boxShadow: isRoadmap ? 'none' : 'var(--shadow-brutal)',
            opacity: isRoadmap ? 0.7 : 1
        }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '1rem' }}>
                <div>
                    <h3 style={{ margin: 0, fontSize: '1.15rem', fontWeight: 900, textTransform: 'uppercase' }}>{name}</h3>
                    <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>{type}</span>
                </div>
                <span style={{ 
                    fontSize: '0.75rem', 
                    fontWeight: 800, 
                    padding: '4px 8px', 
                    backgroundColor: isRoadmap ? 'var(--color-bg-surface)' : (status === 'Operacional' ? 'var(--color-status-green)' : 'var(--color-status-red)'),
                    color: isRoadmap ? 'var(--color-text-muted)' : 'white',
                    height: 'fit-content'
                }}>
                    {status}
                </span>
            </div>
            <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>{description}</p>
            {isRoadmap && (
                <div style={{ marginTop: '1rem', fontSize: '0.7rem', fontWeight: 900, color: 'var(--color-status-blue)', textTransform: 'uppercase' }}>
                    Prevista uma fase futura
                </div>
            )}
        </div>
    );

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
            <header style={{ borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px', marginBottom: '1.5rem' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', marginBottom: '0.5rem' }}>
                    <Link to="/admin/workspace" style={{ color: 'var(--color-text-muted)', display: 'flex' }}>
                        <ArrowLeft size={24} />
                    </Link>
                    <div style={{ 
                        backgroundColor: 'var(--color-status-blue)', 
                        padding: '0.5rem', 
                        display: 'flex', 
                        border: '2px solid var(--color-status-blue)',
                        color: 'white'
                    }}>
                        <Network size={24} />
                    </div>
                    <div>
                        <h1 style={{ margin: 0, fontSize: '2.5rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            Saúde das Integrações
                        </h1>
                        <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase', fontSize: '0.875rem' }}>
                            Estado das comunicações com sistemas externos e provedores
                        </p>
                    </div>
                </div>
            </header>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(350px, 1fr))', gap: '1.5rem' }}>
                <IntegrationCard 
                    name="Extração de Documentos" 
                    type="OCR / AI Service" 
                    status={ocrStatus || 'A verificar...'} 
                    description="Monitorização da comunicação com os serviços de extração de dados de facturas, cotações e documentos operacionais (Local OCR e OpenAI)."
                />
                <IntegrationCard 
                    name="AlplaPROD" 
                    type="Production & Supply Chain System" 
                    status="Não Disponível" 
                    description="Acompanhamento do estado das comunicações relacionadas ao AlplaPROD e aos serviços associados ao fluxo operacional e de dados produtivos."
                    isRoadmap
                />
                <IntegrationCard 
                    name="Primavera ERP" 
                    type="Enterprise Resource Planning" 
                    status="Não Disponível" 
                    description="Integração de pagamentos e fluxos financeiros com a base Primavera v10."
                    isRoadmap
                />
            </div>

            <div style={{ border: '2px dashed var(--color-border-light)', padding: '2rem', textAlign: 'center' }}>
                <div style={{ display: 'flex', justifyContent: 'center', gap: '2rem', opacity: 0.2 }}>
                    <Database size={48} />
                    <Search size={48} />
                    <Cpu size={48} />
                </div>
                <p style={{ color: 'var(--color-text-muted)', marginTop: '1rem', fontWeight: 500 }}>
                    As integrações com sistemas operacionais e ERPs estão actualmente em fase de evolução e mapeamento técnico.
                </p>
            </div>
        </div>
    );
}
```

---

## 5. Lista de Pedidos (Compras)
**Rota:** `/requests`
**Arquivo:** `src/frontend/src/pages/Requests/RequestsList.tsx`

```tsx
import React, { useEffect, useState, useMemo } from 'react';
import { Link, useLocation, useSearchParams, useNavigate } from 'react-router-dom';
import { Plus, Search, FileText, X } from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { formatCurrencyAO, getRequestGuidance, getUrgencyStyle } from '../../lib/utils';
import { RequestListItemDto } from '../../types';
import { RequestTimelineInline } from './components/RequestTimelineInline';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { FilterDropdown, FilterGroup } from '../../components/ui/FilterDropdown';

// Quick Chip Definitions
const QUICK_CHIPS = [
    { label: 'Todos', activeCodes: [] },
    { label: 'Em Cotação', activeCodes: ['WAITING_QUOTATION'] },
    { label: 'Em Aprovação', activeCodes: ['WAITING_AREA_APPROVAL', 'WAITING_FINAL_APPROVAL'] },
    { label: 'Financeiro', activeCodes: ['PO_ISSUED', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED'] },
    { label: 'Recebimento', activeCodes: ['WAITING_RECEIPT', 'PARTIALLY_RECEIVED'] },
    { label: 'Finalizados', activeCodes: ['COMPLETED', 'QUOTATION_COMPLETED'] },
    { label: 'Cancelados', activeCodes: ['CANCELLED', 'REJECTED'] }
];

const AttentionLegend = () => (
    <div style={{ 
        display: 'flex', gap: '24px', padding: '12px 0 4px', 
        fontSize: '0.75rem', fontWeight: 600, textTransform: 'uppercase',
        color: 'var(--color-text-muted)',
    }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <div style={{ width: '12px', height: '12px', backgroundColor: '#fee2e2', border: '1px solid #fecaca' }}></div>
            <span>Vencido</span>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <div style={{ width: '12px', height: '12px', backgroundColor: '#ffedd5', border: '1px solid #fed7aa' }}></div>
            <span>Hoje</span>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <div style={{ width: '12px', height: '12px', backgroundColor: '#fef9c3', border: '1px solid #fef08a' }}></div>
            <span>Próximo (&le; 3 dias)</span>
        </div>
    </div>
);

export function RequestsList() {
    const [searchParams, setSearchParams] = useSearchParams();

    // Data State
    const [requests, setRequests] = useState<RequestListItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [totalCount, setTotalCount] = useState(0);

    // Lookups State
    const [statuses, setStatuses] = useState<any[]>([]);
    const [requestTypes, setRequestTypes] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);
    const [companies, setCompanies] = useState<any[]>([]);
    const [departments, setDepartments] = useState<any[]>([]);

    // UI State
    const [expandedRequestId, setExpandedRequestId] = useState<string | null>(null);
    const navigate = useNavigate();
    const [showApprovalModal, setShowApprovalModal] = useState<{ show: boolean, type: ApprovalActionType, requestId: string | null }>({ show: false, type: null, requestId: null });
    const [approvalComment, setApprovalComment] = useState('');
    const [approvalProcessing, setApprovalProcessing] = useState(false);
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    // Filter URL Parsing
    const searchTerm = searchParams.get('search') || '';
    const statusIdsStr = searchParams.get('statusIds') || '';
    const statusCodesStr = searchParams.get('statusCodes') || '';
    const isAttention = searchParams.get('isAttention') === 'true';
    const requestTypeIdsStr = searchParams.get('requestTypeIds') || '';
    const plantIdsStr = searchParams.get('plantIds') || '';
    const companyIdsStr = searchParams.get('companyIds') || '';
    const departmentIdsStr = searchParams.get('departmentIds') || '';
    const page = Number(searchParams.get('page')) || 1;
    const pageSize = Number(searchParams.get('pageSize')) || 20;

    const [searchInput, setSearchInput] = useState(searchTerm);

    const statusIds = statusIdsStr ? statusIdsStr.split(',') : [];
    const requestTypeIds = requestTypeIdsStr ? requestTypeIdsStr.split(',') : [];
    const plantIds = plantIdsStr ? plantIdsStr.split(',') : [];
    const companyIds = companyIdsStr ? companyIdsStr.split(',') : [];
    const departmentIds = departmentIdsStr ? departmentIdsStr.split(',') : [];

    const toggleRow = (requestId: string) => {
        setExpandedRequestId(prev => prev === requestId ? null : requestId);
    };

    const location = useLocation();
    const locationState = location.state as { successMessage?: string, fromList?: string } | null;

    useEffect(() => {
        if (locationState?.successMessage) {
            setFeedback({ type: 'success', message: locationState.successMessage });
            window.history.replaceState({}, document.title);
        }
    }, [locationState]);

    const updateParams = (updates: Record<string, string | number | null>) => {
        setSearchParams(prev => {
            const next = new URLSearchParams(prev);
            Object.entries(updates).forEach(([key, value]) => {
                if (value === null || value === '') {
                    next.delete(key);
                } else {
                    next.set(key, String(value));
                }
            });
            // Reset page to 1 on filter changes unless page is explicitly in updates
            if (!('page' in updates)) {
                next.set('page', '1');
            }
            return next;
        }, { replace: true });
    };

    useEffect(() => {
        const handler = setTimeout(() => {
            if (searchTerm !== searchInput) {
                updateParams({ search: searchInput || null, page: 1 });
            }
        }, 500);
        return () => clearTimeout(handler);
    }, [searchInput, searchTerm]);

    // Initial Lookups
    useEffect(() => {
        async function fetchLookups() {
            try {
                const [loadedStatuses, loadedTypes, loadedPlants, loadedCompanies, loadedDepts] = await Promise.all([
                    api.lookups.getRequestStatuses(false),
                    api.lookups.getRequestTypes(false),
                    api.lookups.getPlants(undefined, false),
                    api.lookups.getCompanies(false),
                    api.lookups.getDepartments(false)
                ]);
                setStatuses(loadedStatuses);
                setRequestTypes(loadedTypes);
                setPlants(loadedPlants);
                setCompanies(loadedCompanies);
                setDepartments(loadedDepts);

                // Handle statusCodes mapping from URL if present
                if (statusCodesStr && loadedStatuses.length > 0) {
                    const codes = statusCodesStr.split(',');
                    const mappedIds = loadedStatuses
                        .filter(s => codes.includes(s.code))
                        .map(s => String(s.id));
                    
                    if (mappedIds.length > 0) {
                        updateParams({ 
                            statusIds: mappedIds.join(','),
                            statusCodes: null 
                        });
                    }
                }
            } catch (err) {
                console.error("Failed to load lookups:", err);
            }
        }
        fetchLookups();
    }, []);

    // Main Data Fetch
    useEffect(() => {
        async function loadData() {
            try {
                setLoading(true);
                const data = await api.requests.list(
                    searchTerm, 
                    { 
                        statusIds: statusIdsStr, 
                        typeIds: requestTypeIdsStr, 
                        plantIds: plantIdsStr, 
                        companyIds: companyIdsStr,
                        departmentIds: departmentIdsStr,
                        isAttention: isAttention
                    }, 
                    page, 
                    pageSize
                );
                setRequests(data.items || []);
                setTotalCount(data.totalCount || 0);
            } catch (err: any) {
                setFeedback({ type: 'error', message: err.message || 'Erro desconhecido' });
            } finally {
                setLoading(false);
            }
        }
        loadData();
    }, [searchTerm, statusIdsStr, requestTypeIdsStr, plantIdsStr, companyIdsStr, departmentIdsStr, page, pageSize]);

    // Handlers
    const handleFilterChange = (key: string, newIds: string[]) => {
        updateParams({ [key]: newIds.length > 0 ? newIds.join(',') : null });
    };

    const clearAllFilters = () => {
        setSearchInput('');
        updateParams({ search: null, statusIds: null, statusCodes: null, isAttention: null, requestTypeIds: null, plantIds: null, departmentIds: null, page: 1 });
    };

    const handleQuickChipSelect = (activeCodes: string[]) => {
        if (activeCodes.length === 0) {
            updateParams({ statusIds: null });
        } else {
            const mappedIds = statuses
                .filter(s => activeCodes.includes(s.code))
                .map(s => String(s.id));
            updateParams({ statusIds: mappedIds.join(',') });
        }
    };

    // Filter Mappings
    const statusGroups: FilterGroup[] = useMemo(() => {
        return [
            { name: 'Inicial', codes: ['DRAFT', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT'] },
            { name: 'Aprovação & Cotação', codes: ['WAITING_QUOTATION', 'WAITING_AREA_APPROVAL', 'WAITING_FINAL_APPROVAL'] },
            { name: 'Financeiro & Recebimento', codes: ['PO_ISSUED', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT', 'PARTIALLY_RECEIVED'] },
            { name: 'Finalizados', codes: ['COMPLETED', 'QUOTATION_COMPLETED', 'CANCELLED', 'REJECTED'] }
        ].map(g => ({
            name: g.name,
            options: statuses.filter(s => g.codes.includes(s.code)).map(s => ({ id: s.id, name: s.name }))
        })).filter(g => g.options.length > 0);
    }, [statuses]);

    const determineActiveQuickChip = () => {
        // If statusIds is completely empty, it's 'Todos'
        if (statusIds.length === 0) return 'Todos';
        // Check if the current selected statusIds exactly match a quick chip's active codes
        const selectedCodes = statuses.filter(s => statusIds.includes(String(s.id))).map(s => s.code);
        if (selectedCodes.length === 0) return null;
        
        for (const chip of QUICK_CHIPS) {
            if (chip.activeCodes.length === selectedCodes.length && chip.activeCodes.every(c => selectedCodes.includes(c))) {
                return chip.label;
            }
        }
        return null;
    };

    const activeQuickChip = determineActiveQuickChip();

    const activeFilterChips = useMemo(() => {
        const chips: { key: string, id: string, label: string, type: 'status' | 'type' | 'plant' | 'department' | 'company' }[] = [];
        
        statusIds.forEach(id => {
            const item = statuses.find(s => String(s.id) === id);
            if (item) chips.push({ key: `status-${id}`, id, label: `Status: ${item.name}`, type: 'status' });
        });
        requestTypeIds.forEach(id => {
            const item = requestTypes.find(t => String(t.id) === id);
            if (item) chips.push({ key: `type-${id}`, id, label: `Tipo: ${item.name}`, type: 'type' });
        });
        plantIds.forEach(id => {
            const item = plants.find(p => String(p.id) === id);
            if (item) chips.push({ key: `plant-${id}`, id, label: `Planta: ${item.name}`, type: 'plant' });
        });
        companyIds.forEach(id => {
            const item = companies.find(c => String(c.id) === id);
            if (item) chips.push({ key: `company-${id}`, id, label: `Empresa: ${item.name}`, type: 'company' });
        });
        departmentIds.forEach(id => {
            const item = departments.find(d => String(d.id) === id);
            if (item) chips.push({ key: `dept-${id}`, id, label: `Depto: ${item.name}`, type: 'department' });
        });
        
        if (isAttention) {
            chips.push({ key: 'attention', id: 'attention', label: 'Filtro: Em Atenção', type: 'status' });
        }

        return chips;
    }, [statusIds, requestTypeIds, plantIds, departmentIds, statuses, requestTypes, plants, departments]);

    const handleActionConfirm = async (action: ApprovalActionType) => {
        if (!showApprovalModal.requestId) return;
        if (action === 'DUPLICATE_REQUEST') {
            setApprovalProcessing(true);
            try {
                const result = await api.requests.duplicate(showApprovalModal.requestId);
                setFeedback({ type: 'success', message: 'Pedido duplicado com sucesso.' });
                navigate(`/requests/${result.id}/edit`);
            } catch (err: any) {
                setModalFeedback({ type: 'error', message: err.message || 'Erro ao duplicar pedido.' });
            } finally {
                setApprovalProcessing(false);
                setShowApprovalModal({ show: false, type: null, requestId: null });
            }
        }
    };

    const hasFilters = searchInput || statusIds.length > 0 || requestTypeIds.length > 0 || plantIds.length > 0 || departmentIds.length > 0;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', minWidth: 0 }}>
            {/* Sticky Action Info Block */}
            {feedback.message && (
                <div style={{
                    position: 'sticky', top: 'calc(var(--header-height) - 1rem)', zIndex: 10,
                    backgroundColor: 'var(--color-bg-surface)', padding: '2rem 0 0 0', margin: '-2rem 0 0 0',
                    display: 'flex', flexDirection: 'column', gap: '16px'
                }}>
                    <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                </div>
            )}

            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px', width: '100%', minWidth: 0 }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: '2.5rem', color: 'var(--color-primary)' }}>Pedidos de Compras e Pagamentos</h1>
                    <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase' }}>Gerencie e acompanhe todos os pedidos corporativos.</p>
                </div>
                <Link to="/requests/new" className="btn-primary" style={{ display: 'flex', alignItems: 'center', gap: '12px', textDecoration: 'none' }}>
                    <Plus size={20} strokeWidth={3} />
                    Novo Pedido
                </Link>
            </div>

            {/* Filter Hub */}
            <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '16px', boxShadow: 'var(--shadow-brutal)', border: '2px solid var(--color-primary)', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                
                {/* Row 1: Search & Reset */}
                <div style={{ display: 'flex', gap: '16px', alignItems: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flex: 1, backgroundColor: 'var(--color-bg-page)', padding: '0 16px', border: '2px solid var(--color-border)', minWidth: 0 }}>
                        <Search size={20} color="var(--color-primary)" strokeWidth={2.5} />
                        <input
                            type="text"
                            placeholder="BUSCAR POR NÚMERO OU TÍTULO..."
                            value={searchInput}
                            onChange={(e) => setSearchInput(e.target.value)}
                            style={{ border: 'none', outline: 'none', width: '100%', fontSize: '0.85rem', padding: '12px 0', backgroundColor: 'transparent', fontWeight: 600, color: 'var(--color-primary)', textTransform: 'uppercase' }}
                        />
                    </div>
                    {hasFilters && (
                        <button onClick={clearAllFilters} style={{ 
                            background: 'none', border: 'none', color: 'var(--color-text-muted)', cursor: 'pointer', 
                            fontWeight: 700, textTransform: 'uppercase', fontSize: '0.85rem', whiteSpace: 'nowrap'
                        }}>
                            Limpar Filtros
                        </button>
                    )}
                </div>

                {/* Row 2: Main Filter Dropdowns */}
                <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
                    <FilterDropdown 
                        label="Tipo de Pedido" 
                        options={requestTypes.map(t => ({ id: t.id, name: t.name }))}
                        selectedIds={requestTypeIds}
                        onChange={(ids) => handleFilterChange('requestTypeIds', ids)}
                    />
                    <FilterDropdown 
                        label="Empresa" 
                        options={companies.map(c => ({ id: c.id, name: c.name }))}
                        selectedIds={companyIds}
                        onChange={(ids) => handleFilterChange('companyIds', ids)}
                    />
                    <FilterDropdown 
                        label="Planta" 
                        options={plants.map(p => ({ id: p.id, name: p.name }))}
                        selectedIds={plantIds}
                        onChange={(ids) => handleFilterChange('plantIds', ids)}
                    />
                    <FilterDropdown 
                        label="Departamento" 
                        options={departments.map(d => ({ id: d.id, name: d.name }))}
                        selectedIds={departmentIds}
                        onChange={(ids) => handleFilterChange('departmentIds', ids)}
                    />
                    <FilterDropdown 
                        label="Status" 
                        groups={statusGroups}
                        selectedIds={statusIds}
                        onChange={(ids) => handleFilterChange('statusIds', ids)}
                    />
                </div>

                {/* Row 3: Quick Chips */}
                {statuses.length > 0 && (
                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', paddingTop: '8px', borderTop: '2px dashed var(--color-border)' }}>
                        {QUICK_CHIPS.map(chip => {
                            const isActive = activeQuickChip === chip.label;
                            return (
                                <button
                                    key={chip.label}
                                    onClick={() => handleQuickChipSelect(chip.activeCodes)}
                                    style={{
                                        padding: '4px 12px',
                                        backgroundColor: isActive ? 'var(--color-primary)' : 'var(--color-bg-page)',
                                        color: isActive ? 'var(--color-bg-surface)' : 'var(--color-text-main)',
                                        border: `2px solid ${isActive ? 'var(--color-primary)' : 'var(--color-border)'}`,
                                        fontWeight: 700,
                                        fontSize: '0.75rem',
                                        textTransform: 'uppercase',
                                        cursor: 'pointer',
                                        transition: 'all 0.1s'
                                    }}
                                >
                                    {chip.label}
                                </button>
                            );
                        })}
                    </div>
                )}

                {/* Row 4: Active Filter Tags (excludes empty search) */}
                {activeFilterChips.length > 0 && (
                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', paddingTop: '8px' }}>
                        {activeFilterChips.map(chip => (
                            <span key={chip.key} style={{
                                display: 'inline-flex', alignItems: 'center', gap: '6px',
                                padding: '4px 8px', backgroundColor: 'var(--color-bg-surface)',
                                border: '1px solid var(--color-primary)', color: 'var(--color-primary)',
                                fontWeight: 600, fontSize: '0.75rem', textTransform: 'uppercase'
                            }}>
                                {chip.label}
                                <button
                                    onClick={() => {
                                        if (chip.id === 'attention') {
                                            updateParams({ isAttention: null });
                                            return;
                                        }
                                        const paramKey = chip.type === 'status' ? 'statusIds' : chip.type === 'type' ? 'requestTypeIds' : chip.type === 'plant' ? 'plantIds' : 'departmentIds';
                                        const currentIds = chip.type === 'status' ? statusIds : chip.type === 'type' ? requestTypeIds : chip.type === 'plant' ? plantIds : departmentIds;
                                        handleFilterChange(paramKey, currentIds.filter(id => id !== chip.id));
                                    }}
                                    style={{ background: 'none', border: 'none', cursor: 'pointer', padding: 0, display: 'flex' }}
                                >
                                    <X size={14} color="var(--color-primary)" strokeWidth={2.5} />
                                </button>
                            </span>
                        ))}
                    </div>
                )}
            </div>

            {/* Legend & Table Area */}
            <div style={{ display: 'flex', flexDirection: 'column' }}>
                <AttentionLegend />
                <div style={{ overflowX: 'auto', width: '100%', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)' }}>
                {loading ? (
                    <div style={{ padding: '60px', textAlign: 'center', color: 'var(--color-primary)', fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.1em' }}>
                        A Carregar Sistema...
                    </div>
                ) : feedback.type === 'error' && feedback.message && requests.length === 0 ? (
                    <div style={{ padding: '40px', textAlign: 'center', backgroundColor: 'var(--color-status-rejected)', color: '#fff', fontWeight: 700, textTransform: 'uppercase' }}>
                        ERRO SISTÊMICO: {feedback.message}
                    </div>
                ) : requests.length === 0 ? (
                    <div style={{ padding: '80px 20px', textAlign: 'center', color: 'var(--color-text-muted)', border: '4px dashed var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}>
                        <FileText size={64} strokeWidth={1.5} style={{ opacity: 0.2, margin: '0 auto 24px', color: 'var(--color-primary)' }} />
                        <p style={{ fontWeight: 700, fontSize: '1.2rem', textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-main)' }}>
                            Nenhum pedido encontrado com os filtros aplicados.
                        </p>
                        {hasFilters && (
                            <button onClick={clearAllFilters} className="btn-secondary" style={{ marginTop: '16px' }}>
                                LIMPAR FILTROS
                            </button>
                        )}
                    </div>
                ) : (
                    <table style={{ border: 'none', boxShadow: 'none', minWidth: '1200px' }}>
                        <thead>
                            <tr>
                                <th style={{ textAlign: 'center' }}>Comando</th>
                                <th>Número</th>
                                <th>Tipo de Operação</th>
                                <th>Título do Pedido</th>
                                <th>Departamento</th>
                                <th>Empresa</th>
                                <th>Grau Necessidade</th>
                                <th>Status do Pedido</th>
                                <th>Situação Atual</th>
                                <th style={{ textAlign: 'right' }}>Valor Estimado</th>
                            </tr>
                        </thead>
                        <tbody>
                            {requests.map(req => {
                                const urgency = getUrgencyStyle(req.needByDateUtc, req.statusCode);
                                const guidance = getRequestGuidance(req.statusCode, req.requestTypeCode);

                                return (
                                    <React.Fragment key={req.id}>
                                        <tr
                                            style={{
                                                backgroundColor: urgency?.backgroundColor || 'inherit',
                                                cursor: 'pointer',
                                                transition: 'background-color 0.2s',
                                                borderLeft: expandedRequestId === req.id ? '6px solid var(--color-primary)' : 'none'
                                            }}
                                            onClick={() => toggleRow(req.id)}
                                        >
                                            <td style={{ textAlign: 'center' }}>
                                                {req.statusCode === 'DRAFT' ? (
                                                    <Link
                                                        to={`/requests/${req.id}/edit`}
                                                        className="btn-secondary"
                                                        style={{ padding: '6px 12px', fontSize: '0.75rem', display: 'inline-block' }}
                                                        onClick={(e) => e.stopPropagation()}
                                                    >
                                                        EDITAR
                                                    </Link>
                                                ) : (
                                                    <Link
                                                        to={`/requests/${req.id}`}
                                                        style={{ fontSize: '0.75rem', fontWeight: 700, textTransform: 'uppercase' }}
                                                        onClick={(e) => e.stopPropagation()}
                                                    >
                                                        VISUALIZAR
                                                    </Link>
                                                )}
                                                <button
                                                    onClick={(e) => {
                                                        e.stopPropagation();
                                                        setShowApprovalModal({ show: true, type: 'DUPLICATE_REQUEST', requestId: req.id });
                                                    }}
                                                    className="btn-secondary"
                                                    style={{ padding: '6px 12px', fontSize: '0.75rem', display: 'inline-flex', marginTop: '6px', width: '100%', justifyContent: 'center' }}
                                                >
                                                    COPIAR
                                                </button>
                                            </td>
                                            <td style={{ fontWeight: 800, color: 'var(--color-primary)' }}>
                                                <Link
                                                    to={`/requests/${req.id}`}
                                                    state={{ fromList: location.search }}
                                                    onClick={(e) => e.stopPropagation()}
                                                >
                                                    {req.requestNumber || 'PENDENTE'}
                                                </Link>
                                            </td>
                                            <td style={{ fontWeight: 600 }}>{req.requestTypeName}</td>
                                            <td>{req.title}</td>
                                            <td>
                                                <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>
                                                    {req.departmentName || 'Não Definido'}
                                                </span>
                                            </td>
                                            <td>
                                                <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>
                                                    {req.companyName || 'Não Definido'}
                                                </span>
                                            </td>
                                            <td>
                                                <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>
                                                    {req.needLevelName || 'Não Definido'}
                                                </span>
                                            </td>
                                            <td>
                                                <span className={`badge badge-sm ${
                                                    req.statusBadgeColor === 'yellow' || req.statusBadgeColor === 'amber' ? 'badge-warning' :
                                                    req.statusBadgeColor === 'green' || req.statusBadgeColor === 'emerald' ? 'badge-success' :
                                                    req.statusBadgeColor === 'red' || req.statusBadgeColor === 'rose' || req.statusBadgeColor === 'rejected' ? 'badge-danger' :
                                                    req.statusBadgeColor === 'blue' || req.statusBadgeColor === 'sky' || req.statusBadgeColor === 'indigo' ? 'badge-info' :
                                                    'badge-neutral'
                                                }`} title={`Código: ${req.statusCode}`}>
                                                    {req.statusName}
                                                </span>
                                            </td>
                                            <td>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                    <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                                        {guidance.responsible}
                                                    </span>
                                                    <span style={{ fontSize: '0.7rem', fontWeight: 600, color: 'var(--color-text-muted)', lineHeight: '1.2' }}>
                                                        {guidance.nextAction}
                                                    </span>
                                                </div>
                                            </td>
                                            <td style={{ textAlign: 'right', fontWeight: 800 }}>
                                                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '2px' }}>
                                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>{req.currencyCode}</span>
                                                    <span>{formatCurrencyAO(req.estimatedTotalAmount)}</span>
                                                </div>
                                            </td>
                                        </tr>
                                        {expandedRequestId === req.id && (
                                            <tr key={`${req.id}-timeline`}>
                                                <td colSpan={10} style={{ padding: '0', backgroundColor: 'var(--color-bg-page)' }}>
                                                    <div style={{ padding: '24px 32px', borderTop: '1px solid var(--color-border)', backgroundColor: 'rgba(var(--color-primary-rgb), 0.02)' }}>
                                                        <RequestTimelineInline requestId={req.id} />
                                                    </div>
                                                </td>
                                            </tr>
                                        )}
                                    </React.Fragment>
                                )
                            })}
                        </tbody>
                    </table>
                )}
            </div>
            </div>

            <ApprovalModal
                show={showApprovalModal.show}
                type={showApprovalModal.type}
                status={null}
                onClose={() => {
                    setShowApprovalModal({ show: false, type: null, requestId: null });
                    setApprovalComment('');
                    setModalFeedback({ type: 'success', message: null });
                }}
                onConfirm={handleActionConfirm}
                comment={approvalComment}
                setComment={setApprovalComment}
                processing={approvalProcessing}
                feedback={modalFeedback}
                onCloseFeedback={() => setModalFeedback(prev => ({ ...prev, message: null }))}
            />

            {/* Pagination Controls */}
            {
                !loading && !(feedback.type === 'error' && feedback.message && requests.length === 0) && requests.length > 0 && (
                    <div style={{
                        marginTop: '24px',
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        padding: '16px',
                        backgroundColor: 'var(--color-bg-surface)',
                        border: '2px solid var(--color-border)',
                        width: '100%',
                        minWidth: 0,
                        boxShadow: 'var(--shadow-brutal)'
                    }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                            <span style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.9rem', textTransform: 'uppercase' }}>Itens por página:</span>
                            <select
                                value={pageSize}
                                onChange={(e) => {
                                    updateParams({ pageSize: Number(e.target.value), page: 1 });
                                }}
                                style={{
                                    padding: '8px 12px', border: '2px solid var(--color-primary)',
                                    backgroundColor: 'var(--color-bg-page)', outline: 'none',
                                    fontWeight: 700, color: 'var(--color-primary)', cursor: 'pointer'
                                }}
                            >
                                <option value={10}>10</option>
                                <option value={20}>20</option>
                                <option value={50}>50</option>
                                <option value={100}>100</option>
                            </select>
                        </div>

                        <div style={{ fontWeight: 700, color: 'var(--color-primary)', fontSize: '0.9rem' }}>
                            MOSTRANDO {((page - 1) * pageSize) + 1} - {Math.min(page * pageSize, totalCount)} DE {totalCount} RESULTADOS
                        </div>

                        <div style={{ display: 'flex', gap: '12px' }}>
                            <button
                                disabled={page === 1}
                                onClick={() => updateParams({ page: Math.max(1, page - 1) })}
                                style={{
                                    padding: '8px 20px', backgroundColor: page === 1 ? 'var(--color-bg-page)' : 'var(--color-bg-surface)',
                                    color: page === 1 ? 'var(--color-text-muted)' : 'var(--color-primary)',
                                    border: '2px solid', borderColor: page === 1 ? 'var(--color-border)' : 'var(--color-primary)',
                                    fontWeight: 800, cursor: page === 1 ? 'not-allowed' : 'pointer', transition: 'all 0.1s', textTransform: 'uppercase'
                                }}
                            >
                                <span style={{ opacity: page === 1 ? 0.5 : 1 }}>Anterior</span>
                            </button>
                            <button
                                disabled={page * pageSize >= totalCount}
                                onClick={() => updateParams({ page: page + 1 })}
                                style={{
                                    padding: '8px 20px', backgroundColor: page * pageSize >= totalCount ? 'var(--color-bg-page)' : 'var(--color-primary)',
                                    color: page * pageSize >= totalCount ? 'var(--color-text-muted)' : '#FFF',
                                    border: '2px solid', borderColor: page * pageSize >= totalCount ? 'var(--color-border)' : 'var(--color-primary)',
                                    fontWeight: 800, cursor: page * pageSize >= totalCount ? 'not-allowed' : 'pointer', transition: 'all 0.1s', textTransform: 'uppercase'
                                }}
                            >
                                <span style={{ opacity: page * pageSize >= totalCount ? 0.7 : 1 }}>Próximo</span>
                            </button>
                        </div>
                    </div>
                )
            }
        </div >
    );
}


---

## 6. Workspace de Recebimento (Operacional)
**Rota:** `/receiving`
**Arquivo:** `src/frontend/src/pages/Receiving/ReceivingWorkspace.tsx`

```tsx
import React, { useEffect, useState } from 'react';
import { Package, Truck, CheckCircle, Search, Clock, FileText, ChevronDown, ChevronUp } from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { formatCurrencyAO } from '../../lib/utils';
import { RequestListItemDto } from '../../types';

type ReceivingStage = 'PENDING' | 'FOLLOWUP' | 'RECEIVED';

export function ReceivingWorkspace() {
    const [stage, setStage] = useState<ReceivingStage>('PENDING');
    const [requests, setRequests] = useState<RequestListItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [searchTerm, setSearchTerm] = useState('');

    useEffect(() => {
        async function loadData() {
            try {
                setLoading(true);
                // Filtering happens on frontend for this workspace view
                const data = await api.requests.list('', { 
                    statusCodes: stage === 'PENDING' ? 'PAYMENT_COMPLETED' : 
                                stage === 'FOLLOWUP' ? 'WAITING_RECEIPT,PARTIALLY_RECEIVED' : 
                                'COMPLETED'
                }, 1, 100);
                setRequests(data.items || []);
            } catch (err: any) {
                setFeedback({ type: 'error', message: err.message || 'Erro ao carregar workspace' });
            } finally {
                setLoading(false);
            }
        }
        loadData();
    }, [stage]);

    const filteredRequests = requests.filter(r => 
        r.requestNumber?.toLowerCase().includes(searchTerm.toLowerCase()) ||
        r.title?.toLowerCase().includes(searchTerm.toLowerCase())
    );

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <div style={{ borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px' }}>
                <h1 style={{ margin: 0, fontSize: '2.5rem', color: 'var(--color-primary)' }}>Gestão de Recebimento</h1>
                <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 700, textTransform: 'uppercase' }}>
                    Processamento de Pedidos Pagos e Verificação de Material
                </p>
            </div>

            {/* Stage Selector */}
            <div style={{ display: 'flex', gap: '8px', backgroundColor: 'var(--color-bg-surface)', padding: '12px', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)' }}>
                {[
                    { id: 'PENDING', label: 'A Iniciar (Pagos)', icon: Clock, count: requests.length },
                    { id: 'FOLLOWUP', label: 'Em Follow-up', icon: Truck, count: 0 },
                    { id: 'RECEIVED', label: 'Finalizados', icon: CheckCircle, count: 0 }
                ].map(s => (
                    <button
                        key={s.id}
                        onClick={() => setStage(s.id as ReceivingStage)}
                        style={{
                            flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '12px',
                            padding: '12px', cursor: 'pointer', fontWeight: 800, textTransform: 'uppercase', fontSize: '0.85rem',
                            backgroundColor: stage === s.id ? 'var(--color-primary)' : 'var(--color-bg-page)',
                            color: stage === s.id ? 'var(--color-bg-surface)' : 'var(--color-text-main)',
                            border: `2px solid ${stage === s.id ? 'var(--color-primary)' : 'var(--color-border)'}`,
                            transition: 'all 0.1s'
                        }}
                    >
                        <s.icon size={20} />
                        {s.label}
                    </button>
                ))}
            </div>

            {/* List */}
            <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)', minHeight: '400px' }}>
                {loading ? (
                    <div style={{ padding: '60px', textAlign: 'center', fontWeight: 700, color: 'var(--color-primary)' }}>CARREGANDO FLUXO...</div>
                ) : filteredRequests.length === 0 ? (
                    <div style={{ padding: '100px 20px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
                        <Package size={48} style={{ margin: '0 auto 16px', opacity: 0.3 }} />
                        <p style={{ fontWeight: 700 }}>NENHUM PEDIDO NESTA ETAPA</p>
                    </div>
                ) : (
                    <div style={{ display: 'flex', flexDirection: 'column' }}>
                        {filteredRequests.map(req => (
                            <div key={req.id} style={{ padding: '20px', borderBottom: '2px solid var(--color-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                <div>
                                    <div style={{ fontWeight: 800, color: 'var(--color-primary)', fontSize: '1.1rem' }}>{req.requestNumber}</div>
                                    <div style={{ fontWeight: 600 }}>{req.title}</div>
                                    <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: '4px' }}>{req.departmentName} | {req.companyName}</div>
                                </div>
                                <div style={{ display: 'flex', gap: '12px' }}>
                                    <button className="btn-secondary" style={{ padding: '8px 16px' }}>DETALHES</button>
                                    <button className="btn-primary" style={{ padding: '8px 16px' }}>CONFIRMAR RECEBIMENTO</button>
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}


---

## 7. Cadastro de Dados Mestres (Configuração)
**Rota:** `/settings/master-data`
**Arquivo:** `src/frontend/src/pages/Settings/MasterData.tsx`

```tsx
import React, { useEffect, useState, useMemo } from 'react';
import { 
    Plus, Search, Edit2, Trash2, Check, X, 
    Settings, Globe, MapPin, Briefcase, FileText, 
    Truck, DollarSign, List, Filter, Power
} from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';

interface MasterDataEntity {
    id: string;
    name: string;
    code?: string;
    isActive: boolean;
    [key: string]: any;
}

type EntityType = 'units' | 'currencies' | 'payment-methods' | 'plants' | 'companies' | 'departments' | 'need-levels' | 'request-types' | 'suppliers';

export function MasterData() {
    const [selectedType, setSelectedType] = useState<EntityType>('units');
    const [entities, setEntities] = useState<MasterDataEntity[]>([]);
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [searchTerm, setSearchTerm] = useState('');

    const entityTypes = [
        { id: 'units', label: 'Unidades', icon: Globe },
        { id: 'currencies', label: 'Moedas', icon: DollarSign },
        { id: 'payment-methods', label: 'Formas de Pagamento', icon: List },
        { id: 'plants', label: 'Plantas', icon: MapPin },
        { id: 'companies', label: 'Empresas', icon: Briefcase },
        { id: 'departments', label: 'Departamentos', icon: FileText },
        { id: 'need-levels', label: 'Graus de Necessidade', icon: Filter },
        { id: 'request-types', label: 'Tipos de Pedido', icon: Settings },
        { id: 'suppliers', label: 'Fornecedores', icon: Truck }
    ];

    useEffect(() => {
        async function loadEntities() {
            try {
                setLoading(true);
                let data: any[] = [];
                switch(selectedType) {
                    case 'units': data = await api.lookups.getUnits(false); break;
                    case 'currencies': data = await api.lookups.getCurrencies(false); break;
                    case 'payment-methods': data = await api.lookups.getPaymentMethods(false); break;
                    case 'plants': data = await api.lookups.getPlants(undefined, false); break;
                    case 'companies': data = await api.lookups.getCompanies(false); break;
                    case 'departments': data = await api.lookups.getDepartments(false); break;
                    case 'need-levels': data = await api.lookups.getNeedLevels(false); break;
                    case 'request-types': data = await api.lookups.getRequestTypes(false); break;
                    case 'suppliers': data = await api.suppliers.list('', 1, 1000).then(res => res.items); break;
                }
                setEntities(data.map(item => ({
                    ...item,
                    id: String(item.id),
                    isActive: item.isActive ?? true
                })));
            } catch (err: any) {
                setFeedback({ type: 'error', message: err.message || 'Erro ao carregar dados' });
            } finally {
                setLoading(false);
            }
        }
        loadEntities();
    }, [selectedType]);

    const filteredEntities = useMemo(() => {
        return entities.filter(e => 
            e.name.toLowerCase().includes(searchTerm.toLowerCase()) || 
            (e.code && e.code.toLowerCase().includes(searchTerm.toLowerCase()))
        );
    }, [entities, searchTerm]);

    return (
        <div style={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: '24px', height: '100%', minHeight: '600px' }}>
            {/* Sidebar Navigation */}
            <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)', padding: '16px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                <h3 style={{ margin: '0 0 16px', fontSize: '1rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-primary)', borderBottom: '2px solid var(--color-border)', paddingBottom: '8px' }}>
                    Entidades
                </h3>
                {entityTypes.map(type => (
                    <button
                        key={type.id}
                        onClick={() => setSelectedType(type.id as EntityType)}
                        style={{
                            display: 'flex', alignItems: 'center', gap: '12px', padding: '12px', border: 'none', cursor: 'pointer',
                            fontWeight: 700, textTransform: 'uppercase', fontSize: '0.8rem', textAlign: 'left', borderRadius: '4px',
                            backgroundColor: selectedType === type.id ? 'var(--color-primary)' : 'transparent',
                            color: selectedType === type.id ? 'var(--color-bg-surface)' : 'var(--color-text-main)',
                            transition: 'all 0.1s'
                        }}
                    >
                        <type.icon size={18} />
                        {type.label}
                    </button>
                ))}
            </div>

            {/* Content Area */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '20px', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px', flex: 1 }}>
                        <Search size={20} color="var(--color-primary)" />
                        <input
                            type="text"
                            placeholder="PESQUISAR..."
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            style={{ border: 'none', outline: 'none', background: 'transparent', width: '100%', fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-text-main)' }}
                        />
                    </div>
                    <button className="btn-primary" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <Plus size={18} /> NOVO REGISTRO
                    </button>
                </div>

                <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)', overflow: 'hidden' }}>
                    <table style={{ border: 'none', width: '100%', borderCollapse: 'collapse' }}>
                        <thead style={{ backgroundColor: 'var(--color-primary)', color: 'white' }}>
                            <tr>
                                <th style={{ padding: '16px', textAlign: 'left', fontWeight: 800, textTransform: 'uppercase' }}>Código</th>
                                <th style={{ padding: '16px', textAlign: 'left', fontWeight: 800, textTransform: 'uppercase' }}>Nome</th>
                                <th style={{ padding: '16px', textAlign: 'center', fontWeight: 800, textTransform: 'uppercase' }}>Status</th>
                                <th style={{ padding: '16px', textAlign: 'right', fontWeight: 800, textTransform: 'uppercase' }}>Ações</th>
                            </tr>
                        </thead>
                        <tbody>
                            {loading ? (
                                <tr><td colSpan={4} style={{ textAlign: 'center', padding: '40px', fontWeight: 700 }}>SINCRONIZANDO...</td></tr>
                            ) : filteredEntities.map(entity => (
                                <tr key={entity.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
                                    <td style={{ padding: '14px 16px', fontWeight: 700 }}>{entity.code || '-'}</td>
                                    <td style={{ padding: '14px 16px', fontWeight: 600 }}>{entity.name}</td>
                                    <td style={{ padding: '14px 16px', textAlign: 'center' }}>
                                        <span style={{
                                            padding: '4px 12px', borderRadius: '4px', fontSize: '0.7rem', fontWeight: 800,
                                            backgroundColor: entity.isActive ? 'rgba(34, 197, 94, 0.1)' : 'rgba(239, 68, 68, 0.1)',
                                            color: entity.isActive ? '#15803d' : '#b91c1c',
                                            border: `1px solid ${entity.isActive ? '#15803d' : '#b91c1c'}`
                                        }}>
                                            {entity.isActive ? 'ATIVO' : 'INATIVO'}
                                        </span>
                                    </td>
                                    <td style={{ padding: '14px 16px', textAlign: 'right' }}>
                                        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                                            <button style={{ background: 'none', border: 'none', color: 'var(--color-primary)', cursor: 'pointer' }}><Edit2 size={16} /></button>
                                            <button style={{ background: 'none', border: 'none', color: 'var(--color-primary)', cursor: 'pointer' }}><Power size={16} /></button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    );
}


---

## 8. Configurações de Extração (Configuração)
**Rota:** `/settings/extraction`
**Arquivo:** `src/frontend/src/pages/Settings/DocumentExtractionSettings.tsx`

```tsx
import React, { useEffect, useState } from 'react';
import { Save, Shield, Cpu, Clock, AlertCircle, CheckCircle, BrainCircuit } from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';

export function DocumentExtractionSettings() {
    const [settings, setSettings] = useState<any>(null);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    useEffect(() => {
        async function loadSettings() {
            try {
                const data = await api.settings.getExtraction();
                setSettings(data);
            } catch (err: any) {
                setFeedback({ type: 'error', message: 'Falha ao carregar configurações.' });
            } finally {
                setLoading(false);
            }
        }
        loadSettings();
    }, []);

    const handleSave = async () => {
        setSaving(true);
        try {
            await api.settings.updateExtraction(settings);
            setFeedback({ type: 'success', message: 'Configurações salvas com sucesso!' });
        } catch (err: any) {
            setFeedback({ type: 'error', message: 'Falha ao salvar configurações.' });
        } finally {
            setSaving(false);
        }
    };

    if (loading) return <div style={{ padding: '40px', textAlign: 'center', fontWeight: 700 }}>CARREGANDO ENGINE...</div>;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px', maxWidth: '1000px' }}>
            <div style={{ borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px' }}>
                <h1 style={{ margin: 0, fontSize: '2.5rem', color: 'var(--color-primary)' }}>Inteligência de Extração</h1>
                <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 700, textTransform: 'uppercase' }}>Configuração de OCR e Modelos de IA</p>
            </div>

            {feedback.message && <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(p => ({ ...p, message: null }))} />}

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '24px' }}>
                {/* Provider Selection */}
                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '20px' }}>
                        <BrainCircuit size={24} color="var(--color-primary)" />
                        <h3 style={{ margin: 0, fontWeight: 800, textTransform: 'uppercase' }}>Provedor Ativo</h3>
                    </div>
                    <select 
                        value={settings.activeProvider}
                        onChange={e => setSettings({...settings, activeProvider: e.target.value})}
                        style={{ width: '100%', padding: '12px', border: '2px solid var(--color-primary)', fontWeight: 700, outline: 'none' }}
                    >
                        <option value="AzureDocumentIntelligence">Azure Document Intelligence</option>
                        <option value="OpenAI">OpenAI Vision (GPT-4o)</option>
                        <option value="Mock">Mock Provider (Desenvolvimento)</option>
                    </select>
                </div>

                {/* Performance Settings */}
                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '20px' }}>
                        <Clock size={24} color="var(--color-primary)" />
                        <h3 style={{ margin: 0, fontWeight: 800, textTransform: 'uppercase' }}>Timeouts & Retentativas</h3>
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        <label style={{ fontSize: '0.8rem', fontWeight: 700 }}>TIMEOUT GLOBAL (SEGUNDOS)</label>
                        <input 
                            type="number" 
                            value={settings.timeoutSeconds}
                            onChange={e => setSettings({...settings, timeoutSeconds: parseInt(e.target.value)})}
                            style={{ padding: '8px', border: '2px solid var(--color-border)', fontWeight: 700 }}
                        />
                    </div>
                </div>
            </div>

            {/* AI Specific Config (Conditional) */}
            {settings.activeProvider === 'OpenAI' && (
                <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '24px', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)' }}>
                    <h3 style={{ margin: '0 0 20px', fontWeight: 800, textTransform: 'uppercase' }}>Configurações OpenAI</h3>
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px' }}>
                        <div>
                            <label style={{ fontSize: '0.8rem', fontWeight: 700 }}>MODELO</label>
                            <input type="text" value="gpt-4o" disabled style={{ width: '100%', padding: '8px', border: '2px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)' }} />
                        </div>
                        <div>
                            <label style={{ fontSize: '0.8rem', fontWeight: 700 }}>TEMPERATURA</label>
                            <input type="number" step="0.1" value="0.1" disabled style={{ width: '100%', padding: '8px', border: '2px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)' }} />
                        </div>
                    </div>
                </div>
            )}

            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '20px' }}>
                <button 
                    onClick={handleSave}
                    disabled={saving}
                    className="btn-primary" 
                    style={{ padding: '12px 32px', display: 'flex', alignItems: 'center', gap: '12px' }}
                >
                    <Save size={20} />
                    {saving ? 'SALVANDO...' : 'SALVAR CONFIGURAÇÕES'}
                </button>
            </div>
        </div>
    );
}

```

---

## 9. Lista de Itens do Comprador (Operacional)
**Rota:** `/buyer/items`
**Arquivo:** `src/frontend/src/pages/Buyer/BuyerItemsList.tsx`

```tsx
/**
 * DOCUMENTATION SUMMARY:
 * This file (2580 lines) manages the core Buyer Workspace, including:
 * 1. Line Item Filtering: By status, request, and supplier.
 * 2. OCR Extraction Flow: Handling document uploads and AI-based field mapping.
 * 3. Quotation Management: Creating and editing supplier quotes.
 * 4. Proforma Upload: Linking documents to finalized quotes.
 * 5. Supplier Fast-Creation: Integrated modal for adding new vendors during quoting.
 */

import React, { useEffect, useState, useMemo, useCallback } from 'react';
import { 
    Search, Filter, Plus, FileText, CheckCircle, Clock, AlertTriangle, 
    ChevronDown, ChevronUp, Copy, Eye, MoreHorizontal, Download, Upload,
    Zap, Info, X, Save, Edit2, Trash2, ArrowRight
} from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback } from '../../components/ui/Feedback';

export function BuyerItemsList() {
    // Core states for complex workflow
    const [view, setView] = useState<'PENDING' | 'PROFORMA' | 'FINISHED'>('PENDING');
    const [items, setItems] = useState<any[]>([]);
    const [selectedItemIds, setSelectedItemIds] = useState<string[]>([]);
    const [showOcrModal, setShowOcrModal] = useState(false);
    
    // ... logic for bulk actions, OCR, and quotation state ...

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <div style={{ borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px' }}>
                <h1 style={{ margin: 0, fontSize: '2.5rem', color: 'var(--color-primary)' }}>Workspace do Comprador</h1>
            </div>
            {/* Implementation continues with nested tabs and item action groups */}
        </div>
    );
}
```

> [!IMPORTANT]
> Given the scale of `BuyerItemsList.tsx`, developers should refer to the original file for specific OCR mapping logic and quotation state transitions.

