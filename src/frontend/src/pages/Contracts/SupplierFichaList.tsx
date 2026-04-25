import React, { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { motion, AnimatePresence } from 'framer-motion';
import { Search, FileText, Clock, CheckCircle, AlertTriangle, XCircle, Ban, ChevronLeft, ChevronRight, ClipboardList } from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback } from '../../components/ui/Feedback';
import './SupplierFicha.css';

interface SupplierFichaItem {
  id: number;
  portalCode: string;
  primaveraCode: string | null;
  name: string;
  taxId: string | null;
  registrationStatus: string;
  origin: string | null;
  isActive: boolean;
  createdAtUtc: string;
  address: string | null;
  contactName1: string | null;
  contactEmail1: string | null;
  bankIban: string | null;
  paymentTerms: string | null;
  paymentMethod: string | null;
  documentCount: number;
  missingDocCount: number;
  uploadedDocTypes: string[];
}

interface KpiData {
  total: number;
  draft: number;
  pendingCompletion: number;
  active: number;
  suspended: number;
  blocked: number;
}

const STATUS_LABELS: Record<string, string> = {
  DRAFT: 'Rascunho',
  PENDING_COMPLETION: 'Pendente',
  PENDING_APPROVAL: 'Em Aprovação',
  ADJUSTMENT_REQUESTED: 'Reajuste',
  ACTIVE: 'Ativo',
  SUSPENDED: 'Suspenso',
  BLOCKED: 'Bloqueado',
};

const STATUS_COLORS: Record<string, string> = {
  DRAFT: 'var(--color-status-gray, #6b7280)',
  PENDING_COMPLETION: 'var(--color-status-amber, #d97706)',
  PENDING_APPROVAL: 'var(--color-status-indigo, #4f46e5)',
  ADJUSTMENT_REQUESTED: 'var(--color-status-orange, #ea580c)',
  ACTIVE: 'var(--color-status-emerald, #059669)',
  SUSPENDED: 'var(--color-status-amber, #d97706)',
  BLOCKED: 'var(--color-status-red, #dc2626)',
};

const KPI_CONFIG = [
  { key: '', field: 'total' as const, label: 'Total', icon: <ClipboardList size={18} />, color: 'var(--color-primary)' },
  { key: 'DRAFT', field: 'draft' as const, label: 'Rascunho', icon: <FileText size={18} />, color: 'var(--color-status-gray, #6b7280)' },
  { key: 'PENDING_COMPLETION', field: 'pendingCompletion' as const, label: 'Pendente', icon: <Clock size={18} />, color: 'var(--color-status-amber, #d97706)' },
  { key: 'ACTIVE', field: 'active' as const, label: 'Ativo', icon: <CheckCircle size={18} />, color: 'var(--color-status-emerald, #059669)' },
  { key: 'SUSPENDED', field: 'suspended' as const, label: 'Suspenso', icon: <AlertTriangle size={18} />, color: 'var(--color-status-orange, #ea580c)' },
  { key: 'BLOCKED', field: 'blocked' as const, label: 'Bloqueado', icon: <Ban size={18} />, color: 'var(--color-status-red, #dc2626)' },
];

const SupplierFichaList: React.FC = () => {
  const navigate = useNavigate();
  const [items, setItems] = useState<SupplierFichaItem[]>([]);
  const [kpi, setKpi] = useState<KpiData>({ total: 0, draft: 0, pendingCompletion: 0, active: 0, suspended: 0, blocked: 0 });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [sortBy, setSortBy] = useState('createdAt');
  const [sortDir, setSortDir] = useState('desc');
  const pageSize = 15;

  const fetchData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.lookups.getSupplierFichas({
        search: search || undefined,
        status: statusFilter || undefined,
        sortBy,
        sortDir,
        page,
        pageSize,
      });
      setItems(data.items);
      setTotalCount(data.totalCount);
      setKpi(data.kpi);
    } catch (err) {
      console.error('Error loading supplier fichas:', err);
      setError('Não foi possível carregar as fichas de fornecedor. Tente novamente.');
    } finally {
      setLoading(false);
    }
  }, [search, statusFilter, sortBy, sortDir, page]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const handleSearch = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearch(e.target.value);
    setPage(1);
  };

  const handleStatusFilter = (status: string) => {
    setStatusFilter(prev => (prev === status ? '' : status));
    setPage(1);
  };

  const handleSort = (field: string) => {
    if (sortBy === field) {
      setSortDir(prev => (prev === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(field);
      setSortDir('asc');
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  const getDocCompleteness = (item: SupplierFichaItem) => {
    const total = 4;
    const uploaded = item.documentCount;
    return { uploaded, total, percent: Math.round((uploaded / total) * 100) };
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem', paddingTop: '1.5rem' }}>
      {/* Section Header (smaller, inside Contracts workspace) */}
      <div>
        <h2 style={{
          fontSize: '1.1rem',
          fontWeight: 800,
          color: 'var(--color-primary)',
          margin: 0,
        }}>
          Fichas de Fornecedor
        </h2>
        <p style={{
          fontSize: '0.8rem',
          color: 'var(--color-text-muted)',
          margin: '4px 0 0',
          fontWeight: 500,
        }}>
          Controle de cadastro, documentação e aprovação de fornecedores
        </p>
      </div>

      {/* KPI Cards — clickable as filters */}
      <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
        {KPI_CONFIG.map(cfg => {
          const isActive = statusFilter === cfg.key;
          const value = kpi[cfg.field];
          return (
            <motion.button
              key={cfg.key}
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              onClick={() => handleStatusFilter(cfg.key)}
              aria-label={`Filtrar por ${cfg.label}: ${value}`}
              aria-pressed={isActive}
              style={{
                flex: 1, minWidth: '140px',
                padding: '20px', borderRadius: 'var(--radius-lg)',
                backgroundColor: isActive ? `rgba(var(--color-primary-rgb), 0.06)` : 'var(--color-bg-surface)',
                border: `1.5px solid ${isActive ? 'var(--color-primary)' : 'var(--color-border)'}`,
                boxShadow: isActive ? '0 0 0 2px rgba(var(--color-primary-rgb), 0.15)' : 'var(--shadow-sm)',
                display: 'flex', flexDirection: 'column', gap: '8px',
                position: 'relative', overflow: 'hidden',
                cursor: 'pointer', transition: 'all 0.2s',
                fontFamily: 'var(--font-family-body)',
                textAlign: 'left',
              }}
              onMouseOver={(e) => {
                if (!isActive) {
                  e.currentTarget.style.transform = 'translateY(-2px)';
                  e.currentTarget.style.boxShadow = 'var(--shadow-md)';
                  e.currentTarget.style.borderColor = 'var(--color-primary)';
                }
              }}
              onMouseOut={(e) => {
                if (!isActive) {
                  e.currentTarget.style.transform = 'none';
                  e.currentTarget.style.boxShadow = 'var(--shadow-sm)';
                  e.currentTarget.style.borderColor = 'var(--color-border)';
                }
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <span style={{
                  fontWeight: 600, fontSize: '0.75rem', textTransform: 'uppercase',
                  letterSpacing: '0.03em', color: 'var(--color-text-muted)',
                }}>
                  {cfg.label}
                </span>
                <div style={{
                  width: 36, height: 36,
                  backgroundColor: `color-mix(in srgb, ${cfg.color} 12%, transparent)`,
                  color: cfg.color,
                  borderRadius: 'var(--radius-md)',
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                }}>
                  {cfg.icon}
                </div>
              </div>
              <div style={{
                fontSize: '2rem', fontWeight: 700, lineHeight: 1,
                color: cfg.color, fontFamily: 'var(--font-family-display)',
              }}>
                {value}
              </div>
            </motion.button>
          );
        })}
      </div>

      {/* Search & Active Filter */}
      <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', flexWrap: 'wrap' }}>
        <div style={{
          display: 'flex', alignItems: 'center', gap: '8px', flex: 1, minWidth: '200px',
          backgroundColor: 'var(--color-bg-surface)', padding: '10px 16px',
          borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)',
          transition: 'border-color 0.15s, box-shadow 0.15s',
        }}>
          <Search size={16} style={{ color: 'var(--color-text-muted)', flexShrink: 0 }} />
          <input
            placeholder="Pesquisar por nome, NIF, código..."
            value={search}
            onChange={handleSearch}
            aria-label="Pesquisar fornecedores"
            style={{
              flex: 1, border: 'none', outline: 'none', backgroundColor: 'transparent',
              fontSize: '0.9rem', color: 'var(--color-text-main)', fontFamily: 'var(--font-family-body)',
            }}
          />
        </div>
        {statusFilter && (
          <button
            onClick={() => { setStatusFilter(''); setPage(1); }}
            style={{
              display: 'flex', alignItems: 'center', gap: '6px',
              padding: '8px 14px', borderRadius: 'var(--radius-md)',
              border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)',
              fontSize: '0.8rem', fontWeight: 600, cursor: 'pointer',
              color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)',
            }}
          >
            Limpar filtro: {STATUS_LABELS[statusFilter]} ✕
          </button>
        )}
      </div>

      {/* Error State */}
      {error && (
        <Feedback type="error" message={error} />
      )}

      {/* Table */}
      <div style={{ overflowX: 'auto', borderRadius: 'var(--radius-lg)', border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)' }}>
        {loading ? (
          /* Skeleton loading state */
          <div style={{ padding: '32px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
            {[1,2,3,4,5].map(i => (
              <div key={i} style={{
                display: 'flex', gap: '16px', alignItems: 'center',
                animation: 're-shimmer 1.8s ease-in-out infinite',
              }}>
                <div style={{ width: '30%', height: 16, borderRadius: 4, background: 'linear-gradient(90deg, var(--color-bg-page) 25%, var(--color-border) 50%, var(--color-bg-page) 75%)', backgroundSize: '800px 100%', animation: 're-shimmer 1.8s ease-in-out infinite' }} />
                <div style={{ width: '15%', height: 16, borderRadius: 4, background: 'linear-gradient(90deg, var(--color-bg-page) 25%, var(--color-border) 50%, var(--color-bg-page) 75%)', backgroundSize: '800px 100%', animation: 're-shimmer 1.8s ease-in-out infinite' }} />
                <div style={{ width: '10%', height: 16, borderRadius: 4, background: 'linear-gradient(90deg, var(--color-bg-page) 25%, var(--color-border) 50%, var(--color-bg-page) 75%)', backgroundSize: '800px 100%', animation: 're-shimmer 1.8s ease-in-out infinite' }} />
                <div style={{ width: '15%', height: 16, borderRadius: 4, background: 'linear-gradient(90deg, var(--color-bg-page) 25%, var(--color-border) 50%, var(--color-bg-page) 75%)', backgroundSize: '800px 100%', animation: 're-shimmer 1.8s ease-in-out infinite' }} />
                <div style={{ width: '10%', height: 16, borderRadius: 4, background: 'linear-gradient(90deg, var(--color-bg-page) 25%, var(--color-border) 50%, var(--color-bg-page) 75%)', backgroundSize: '800px 100%', animation: 're-shimmer 1.8s ease-in-out infinite' }} />
              </div>
            ))}
            <style>{`@keyframes re-shimmer { 0% { background-position: -400px 0; } 100% { background-position: 400px 0; } }`}</style>
          </div>
        ) : items.length === 0 ? (
          /* Empty state — matches TableEmptyState pattern */
          <div style={{
            padding: '64px 32px', textAlign: 'center', color: 'var(--color-text-muted)',
            display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
          }}>
            <div style={{ opacity: 0.3, marginBottom: '16px' }}>
              <ClipboardList size={48} />
            </div>
            <p style={{ fontWeight: 800, fontSize: '0.9rem', margin: '0 0 8px 0', color: 'var(--color-text-main)' }}>
              Nenhum fornecedor encontrado
            </p>
            <p style={{ fontSize: '0.85rem', margin: 0 }}>
              {search || statusFilter ? 'Tente ajustar os filtros de pesquisa.' : 'As fichas importadas aparecerão aqui.'}
            </p>
          </div>
        ) : (
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }}>
            <thead>
              <tr style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '2px solid var(--color-border)' }}>
                <th
                  onClick={() => handleSort('name')}
                  aria-sort={sortBy === 'name' ? (sortDir === 'asc' ? 'ascending' : 'descending') : undefined}
                  style={{
                    padding: '12px 16px', textAlign: 'left', fontWeight: 700,
                    color: 'var(--color-text-muted)', fontSize: '0.75rem',
                    textTransform: 'uppercase', letterSpacing: '0.05em', whiteSpace: 'nowrap',
                    cursor: 'pointer', userSelect: 'none',
                  }}
                >
                  Fornecedor {sortBy === 'name' && (sortDir === 'asc' ? '↑' : '↓')}
                </th>
                <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 700, color: 'var(--color-text-muted)', fontSize: '0.75rem', textTransform: 'uppercase', letterSpacing: '0.05em', whiteSpace: 'nowrap' }}>NIF</th>
                <th
                  onClick={() => handleSort('status')}
                  aria-sort={sortBy === 'status' ? (sortDir === 'asc' ? 'ascending' : 'descending') : undefined}
                  style={{
                    padding: '12px 16px', textAlign: 'left', fontWeight: 700,
                    color: 'var(--color-text-muted)', fontSize: '0.75rem',
                    textTransform: 'uppercase', letterSpacing: '0.05em', whiteSpace: 'nowrap',
                    cursor: 'pointer', userSelect: 'none',
                  }}
                >
                  Estado {sortBy === 'status' && (sortDir === 'asc' ? '↑' : '↓')}
                </th>
                <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 700, color: 'var(--color-text-muted)', fontSize: '0.75rem', textTransform: 'uppercase', letterSpacing: '0.05em', whiteSpace: 'nowrap' }}>Documentos</th>
                <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 700, color: 'var(--color-text-muted)', fontSize: '0.75rem', textTransform: 'uppercase', letterSpacing: '0.05em', whiteSpace: 'nowrap' }}>Contacto</th>
                <th
                  onClick={() => handleSort('createdAt')}
                  aria-sort={sortBy === 'createdAt' ? (sortDir === 'asc' ? 'ascending' : 'descending') : undefined}
                  style={{
                    padding: '12px 16px', textAlign: 'left', fontWeight: 700,
                    color: 'var(--color-text-muted)', fontSize: '0.75rem',
                    textTransform: 'uppercase', letterSpacing: '0.05em', whiteSpace: 'nowrap',
                    cursor: 'pointer', userSelect: 'none',
                  }}
                >
                  Criação {sortBy === 'createdAt' && (sortDir === 'asc' ? '↑' : '↓')}
                </th>
                <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 700, color: 'var(--color-text-muted)', fontSize: '0.75rem', textTransform: 'uppercase', letterSpacing: '0.05em', whiteSpace: 'nowrap' }}>Ações</th>
              </tr>
            </thead>
            <tbody>
              <AnimatePresence>
                {items.map((item, i) => {
                  const docInfo = getDocCompleteness(item);
                  const statusColor = STATUS_COLORS[item.registrationStatus] || 'var(--color-text-muted)';
                  return (
                    <motion.tr
                      key={item.id}
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      exit={{ opacity: 0 }}
                      transition={{ delay: i * 0.02 }}
                      onClick={() => navigate(`/contracts/fichas/${item.id}`)}
                      style={{
                        cursor: 'pointer', borderBottom: '1px solid var(--color-border)',
                        transition: 'background-color 0.15s',
                      }}
                      onMouseEnter={e => (e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.04)')}
                      onMouseLeave={e => (e.currentTarget.style.backgroundColor = 'transparent')}
                    >
                      <td style={{ padding: '12px 16px' }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                          <strong style={{ fontWeight: 600, fontSize: '0.875rem' }}>{item.name}</strong>
                          <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>{item.portalCode}</span>
                        </div>
                      </td>
                      <td style={{ padding: '12px 16px' }}>
                        <span style={{ fontFamily: 'monospace', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
                          {item.taxId || '—'}
                        </span>
                      </td>
                      <td style={{ padding: '12px 16px' }}>
                        <span style={{
                          display: 'inline-flex', alignItems: 'center', gap: '4px',
                          padding: '4px 12px', borderRadius: 'var(--radius-full)',
                          fontSize: '0.75rem', fontWeight: 700, letterSpacing: '0.02em',
                          color: statusColor,
                          backgroundColor: `color-mix(in srgb, ${statusColor} 12%, transparent)`,
                        }}>
                          {STATUS_LABELS[item.registrationStatus] || item.registrationStatus}
                        </span>
                      </td>
                      <td style={{ padding: '12px 16px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                          <div style={{
                            width: 60, height: 6,
                            background: 'var(--color-border)', borderRadius: 3, overflow: 'hidden',
                          }}>
                            <div style={{
                              height: '100%', width: `${docInfo.percent}%`,
                              background: 'var(--color-status-emerald, #059669)',
                              borderRadius: 3, transition: 'width 0.3s ease',
                            }} />
                          </div>
                          <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                            {docInfo.uploaded}/{docInfo.total}
                          </span>
                        </div>
                      </td>
                      <td style={{ padding: '12px 16px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
                        {item.contactName1 || '—'}
                      </td>
                      <td style={{ padding: '12px 16px', whiteSpace: 'nowrap', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                        {new Date(item.createdAtUtc).toLocaleDateString('pt-PT')}
                      </td>
                      <td style={{ padding: '12px 16px' }}>
                        <button
                          onClick={(e) => { e.stopPropagation(); navigate(`/contracts/fichas/${item.id}`); }}
                          style={{
                            padding: '5px 14px', fontSize: '0.75rem', fontWeight: 700,
                            color: 'var(--color-primary)', background: 'transparent',
                            border: '1.5px solid var(--color-primary)', borderRadius: 'var(--radius-sm)',
                            cursor: 'pointer', transition: 'all 0.15s', fontFamily: 'var(--font-family-body)',
                          }}
                          onMouseEnter={e => { e.currentTarget.style.background = 'var(--color-primary)'; e.currentTarget.style.color = '#fff'; }}
                          onMouseLeave={e => { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = 'var(--color-primary)'; }}
                        >
                          Abrir
                        </button>
                      </td>
                    </motion.tr>
                  );
                })}
              </AnimatePresence>
            </tbody>
          </table>
        )}
      </div>

      {/* Pagination — Portal standard pattern */}
      {totalPages > 1 && (
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0.5rem 0' }}>
          <span style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
            {totalCount} registo{totalCount !== 1 ? 's' : ''} encontrado{totalCount !== 1 ? 's' : ''}
          </span>
          <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
            <button
              disabled={page <= 1}
              onClick={() => setPage(p => Math.max(1, p - 1))}
              style={{
                padding: '8px', borderRadius: 'var(--radius-md)',
                border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                cursor: page <= 1 ? 'default' : 'pointer', opacity: page <= 1 ? 0.5 : 1,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
              }}
            >
              <ChevronLeft size={16} />
            </button>
            <span style={{ fontSize: '0.85rem', fontWeight: 600 }}>
              {page} / {totalPages}
            </span>
            <button
              disabled={page >= totalPages}
              onClick={() => setPage(p => p + 1)}
              style={{
                padding: '8px', borderRadius: 'var(--radius-md)',
                border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                cursor: page >= totalPages ? 'default' : 'pointer', opacity: page >= totalPages ? 0.5 : 1,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
              }}
            >
              <ChevronRight size={16} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default SupplierFichaList;
