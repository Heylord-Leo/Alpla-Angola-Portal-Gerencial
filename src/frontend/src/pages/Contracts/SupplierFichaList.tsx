import React, { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../lib/api';
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
  DRAFT: 'var(--status-draft)',
  PENDING_COMPLETION: 'var(--status-pending)',
  PENDING_APPROVAL: '#6366f1',
  ADJUSTMENT_REQUESTED: '#f97316',
  ACTIVE: 'var(--status-active)',
  SUSPENDED: 'var(--status-warning)',
  BLOCKED: 'var(--status-blocked)',
};

const SupplierFichaList: React.FC = () => {
  const navigate = useNavigate();
  const [items, setItems] = useState<SupplierFichaItem[]>([]);
  const [kpi, setKpi] = useState<KpiData>({ total: 0, draft: 0, pendingCompletion: 0, active: 0, suspended: 0, blocked: 0 });
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [sortBy, setSortBy] = useState('createdAt');
  const [sortDir, setSortDir] = useState('desc');
  const pageSize = 15;

  const fetchData = useCallback(async () => {
    setLoading(true);
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
    <div className="ficha-list-container">
      {/* Page Header */}
      <div className="ficha-list-header">
        <div className="ficha-list-header-text">
          <h1>Ficha de Fornecedor</h1>
          <p className="ficha-list-subtitle">Controlo de registo e documentação de fornecedores</p>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="ficha-kpi-grid">
        <button
          className={`ficha-kpi-card ${statusFilter === '' ? 'ficha-kpi-card--active' : ''}`}
          onClick={() => handleStatusFilter('')}
        >
          <span className="ficha-kpi-value">{kpi.total}</span>
          <span className="ficha-kpi-label">Total</span>
        </button>
        <button
          className={`ficha-kpi-card ficha-kpi-card--draft ${statusFilter === 'DRAFT' ? 'ficha-kpi-card--active' : ''}`}
          onClick={() => handleStatusFilter('DRAFT')}
        >
          <span className="ficha-kpi-value">{kpi.draft}</span>
          <span className="ficha-kpi-label">Rascunho</span>
        </button>
        <button
          className={`ficha-kpi-card ficha-kpi-card--pending ${statusFilter === 'PENDING_COMPLETION' ? 'ficha-kpi-card--active' : ''}`}
          onClick={() => handleStatusFilter('PENDING_COMPLETION')}
        >
          <span className="ficha-kpi-value">{kpi.pendingCompletion}</span>
          <span className="ficha-kpi-label">Pendente</span>
        </button>
        <button
          className={`ficha-kpi-card ficha-kpi-card--active-status ${statusFilter === 'ACTIVE' ? 'ficha-kpi-card--active' : ''}`}
          onClick={() => handleStatusFilter('ACTIVE')}
        >
          <span className="ficha-kpi-value">{kpi.active}</span>
          <span className="ficha-kpi-label">Ativo</span>
        </button>
        <button
          className={`ficha-kpi-card ficha-kpi-card--suspended ${statusFilter === 'SUSPENDED' ? 'ficha-kpi-card--active' : ''}`}
          onClick={() => handleStatusFilter('SUSPENDED')}
        >
          <span className="ficha-kpi-value">{kpi.suspended}</span>
          <span className="ficha-kpi-label">Suspenso</span>
        </button>
        <button
          className={`ficha-kpi-card ficha-kpi-card--blocked ${statusFilter === 'BLOCKED' ? 'ficha-kpi-card--active' : ''}`}
          onClick={() => handleStatusFilter('BLOCKED')}
        >
          <span className="ficha-kpi-value">{kpi.blocked}</span>
          <span className="ficha-kpi-label">Bloqueado</span>
        </button>
      </div>

      {/* Search Bar */}
      <div className="ficha-search-bar">
        <div className="ficha-search-input-wrapper">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="11" cy="11" r="8" />
            <line x1="21" y1="21" x2="16.65" y2="16.65" />
          </svg>
          <input
            type="text"
            placeholder="Pesquisar por nome, NIF, código..."
            value={search}
            onChange={handleSearch}
            className="ficha-search-input"
          />
        </div>
        {statusFilter && (
          <button className="ficha-filter-clear" onClick={() => { setStatusFilter(''); setPage(1); }}>
            Limpar filtro: {STATUS_LABELS[statusFilter]}
            <span>✕</span>
          </button>
        )}
      </div>

      {/* Table */}
      <div className="ficha-table-wrapper">
        {loading ? (
          <div className="ficha-loading">
            <div className="ficha-loading-spinner" />
            <p>Carregando fichas...</p>
          </div>
        ) : items.length === 0 ? (
          <div className="ficha-empty">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" strokeWidth="1.5">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
              <polyline points="14 2 14 8 20 8" />
              <line x1="16" y1="13" x2="8" y2="13" />
              <line x1="16" y1="17" x2="8" y2="17" />
              <polyline points="10 9 9 9 8 9" />
            </svg>
            <p>Nenhum fornecedor encontrado</p>
          </div>
        ) : (
          <table className="ficha-table">
            <thead>
              <tr>
                <th onClick={() => handleSort('name')} className="ficha-th-sortable">
                  Fornecedor {sortBy === 'name' && (sortDir === 'asc' ? '↑' : '↓')}
                </th>
                <th>NIF</th>
                <th onClick={() => handleSort('status')} className="ficha-th-sortable">
                  Estado {sortBy === 'status' && (sortDir === 'asc' ? '↑' : '↓')}
                </th>
                <th>Documentos</th>
                <th>Contacto</th>
                <th onClick={() => handleSort('createdAt')} className="ficha-th-sortable">
                  Criação {sortBy === 'createdAt' && (sortDir === 'asc' ? '↑' : '↓')}
                </th>
                <th>Ações</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                const docInfo = getDocCompleteness(item);
                return (
                  <tr key={item.id} className="ficha-table-row" onClick={() => navigate(`/contracts/fichas/${item.id}`)}>
                    <td>
                      <div className="ficha-supplier-name">
                        <strong>{item.name}</strong>
                        <span className="ficha-portal-code">{item.portalCode}</span>
                      </div>
                    </td>
                    <td>
                      <span className="ficha-nif">{item.taxId || '—'}</span>
                    </td>
                    <td>
                      <span
                        className="ficha-status-badge"
                        style={{ '--badge-color': STATUS_COLORS[item.registrationStatus] || 'var(--text-muted)' } as React.CSSProperties}
                      >
                        {STATUS_LABELS[item.registrationStatus] || item.registrationStatus}
                      </span>
                    </td>
                    <td>
                      <div className="ficha-doc-progress">
                        <div className="ficha-doc-bar">
                          <div className="ficha-doc-bar-fill" style={{ width: `${docInfo.percent}%` }} />
                        </div>
                        <span className="ficha-doc-count">{docInfo.uploaded}/{docInfo.total}</span>
                      </div>
                    </td>
                    <td>
                      <span className="ficha-contact">{item.contactName1 || '—'}</span>
                    </td>
                    <td>
                      <span className="ficha-date">
                        {new Date(item.createdAtUtc).toLocaleDateString('pt-PT')}
                      </span>
                    </td>
                    <td>
                      <button
                        className="ficha-action-btn"
                        onClick={(e) => { e.stopPropagation(); navigate(`/contracts/fichas/${item.id}`); }}
                      >
                        Abrir
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="ficha-pagination">
          <button
            disabled={page <= 1}
            onClick={() => setPage(p => Math.max(1, p - 1))}
            className="ficha-page-btn"
          >
            ← Anterior
          </button>
          <span className="ficha-page-info">
            Página {page} de {totalPages} ({totalCount} registos)
          </span>
          <button
            disabled={page >= totalPages}
            onClick={() => setPage(p => p + 1)}
            className="ficha-page-btn"
          >
            Seguinte →
          </button>
        </div>
      )}
    </div>
  );
};

export default SupplierFichaList;
