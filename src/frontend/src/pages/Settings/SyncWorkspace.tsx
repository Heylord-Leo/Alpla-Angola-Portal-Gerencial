import React, { useState, useCallback, useEffect, useRef, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { api } from '../../lib/api';
import {
    CatalogSyncPreviewDto,
    CatalogSyncPreviewItemDto,
    SupplierSyncPreviewDto,
    SupplierSyncPreviewItemDto,
    SyncMatchStatus,
    SyncImportResultDto
} from '../../types';
import { SupplierImportReviewModal } from '../../components/Settings/SupplierImportReviewModal';
import {
    ArrowLeft,
    RefreshCw,
    Search,
    Download,
    CheckSquare,
    Square,
    AlertTriangle,
    CheckCircle2,
    ChevronLeft,
    ChevronRight,
    Filter,
    Database,
    Building2,
    Loader2,
    Info,
    X
} from 'lucide-react';
import {
    SortableFilterHeader,
    SortConfig,
    ColumnFilterConfig,
    normalizeFilterText
} from '../../components/shared/SortableFilterHeader';

type EntityType = 'catalog' | 'suppliers';

interface CompanyOption {
    id: number;
    name: string;
}

const COMPANIES: CompanyOption[] = [
    { id: 1, name: 'AlplaPLASTICO' },
    { id: 2, name: 'AlplaSOPRO' }
];

const PAGE_SIZE = 50;

const STATUS_CONFIG: Record<SyncMatchStatus, { label: string; color: string; icon: React.ReactNode; bgClass: string }> = {
    New: { label: 'Novo', color: '#22c55e', icon: <CheckCircle2 size={14} />, bgClass: 'sync-status-new' },
    Exists: { label: 'Existente', color: '#64748b', icon: <Info size={14} />, bgClass: 'sync-status-exists' },
    Conflict: { label: 'Conflito', color: '#f59e0b', icon: <AlertTriangle size={14} />, bgClass: 'sync-status-conflict' },
};

// ─── Status enum options for filter (localized) ─────────────────────────────

const STATUS_ENUM_OPTIONS = [
    { value: 'New', label: '🟢 Novo' },
    { value: 'Exists', label: '⚪ Existente' },
    { value: 'Conflict', label: '🟠 Conflito' },
];

// ─── Column configurations ──────────────────────────────────────────────────

interface ColumnDef<T> {
    key: string;
    label: string;
    filterConfig: ColumnFilterConfig;
    accessor: (item: T) => string | undefined | null;
}

const CATALOG_COLUMNS: ColumnDef<CatalogSyncPreviewItemDto>[] = [
    { key: 'status', label: 'Status', filterConfig: { type: 'enum', enumOptions: STATUS_ENUM_OPTIONS }, accessor: (i) => i.status },
    { key: 'primaveraCode', label: 'Código Primavera', filterConfig: { type: 'text' }, accessor: (i) => i.primaveraCode },
    { key: 'primaveraDescription', label: 'Descrição (Primavera)', filterConfig: { type: 'text' }, accessor: (i) => i.primaveraDescription },
    { key: 'primaveraFamily', label: 'Família', filterConfig: { type: 'text' }, accessor: (i) => i.primaveraFamily },
    { key: 'primaveraBaseUnit', label: 'Unidade', filterConfig: { type: 'text' }, accessor: (i) => i.primaveraBaseUnit },
    { key: 'portalCode', label: 'Código Portal', filterConfig: { type: 'text' }, accessor: (i) => i.portalCode },
    { key: 'portalDescription', label: 'Descrição (Portal)', filterConfig: { type: 'text' }, accessor: (i) => i.portalDescription },
];

const SUPPLIER_COLUMNS: ColumnDef<SupplierSyncPreviewItemDto>[] = [
    { key: 'status', label: 'Status', filterConfig: { type: 'enum', enumOptions: STATUS_ENUM_OPTIONS }, accessor: (i) => i.status },
    { key: 'primaveraCode', label: 'Código Primavera', filterConfig: { type: 'text' }, accessor: (i) => i.primaveraCode },
    { key: 'primaveraName', label: 'Nome (Primavera)', filterConfig: { type: 'text' }, accessor: (i) => i.primaveraName },
    { key: 'primaveraTaxId', label: 'NIF (Primavera)', filterConfig: { type: 'text' }, accessor: (i) => i.primaveraTaxId },
    { key: 'portalName', label: 'Nome (Portal)', filterConfig: { type: 'text' }, accessor: (i) => i.portalName },
    { key: 'portalTaxId', label: 'NIF (Portal)', filterConfig: { type: 'text' }, accessor: (i) => i.portalTaxId },
];

// ─── Filtering & Sorting Utilities ──────────────────────────────────────────

type ColumnFilters = Record<string, string | string[]>;

function applyColumnFilters<T>(
    items: T[],
    filters: ColumnFilters,
    columns: ColumnDef<T>[]
): T[] {
    const activeFilters = Object.entries(filters).filter(([, v]) =>
        Array.isArray(v) ? v.length > 0 : typeof v === 'string' && v.length > 0
    );
    if (activeFilters.length === 0) return items;

    return items.filter(item => {
        return activeFilters.every(([key, filterValue]) => {
            const col = columns.find(c => c.key === key);
            if (!col) return true;
            const cellValue = col.accessor(item) ?? '';

            if (col.filterConfig.type === 'enum' && Array.isArray(filterValue)) {
                return filterValue.includes(cellValue);
            }

            if (col.filterConfig.type === 'text' && typeof filterValue === 'string') {
                const normCell = normalizeFilterText(cellValue);
                const normFilter = normalizeFilterText(filterValue);
                return normCell.includes(normFilter);
            }

            return true;
        });
    });
}

function applySorting<T>(
    items: T[],
    sort: SortConfig | null,
    columns: ColumnDef<T>[]
): T[] {
    if (!sort) return items;
    const col = columns.find(c => c.key === sort.key);
    if (!col) return items;

    const sorted = [...items].sort((a, b) => {
        const aVal = normalizeFilterText(col.accessor(a) ?? '');
        const bVal = normalizeFilterText(col.accessor(b) ?? '');
        return aVal.localeCompare(bVal);
    });

    return sort.direction === 'desc' ? sorted.reverse() : sorted;
}

// ─── Main Component ─────────────────────────────────────────────────────────

/**
 * Unified Primavera → Portal synchronization workspace.
 * Parameterized via route :entityType for both 'catalog' and 'suppliers'.
 * 
 * Features:
 * - Company selector (ALPLAPLASTICO / ALPLASOPRO)
 * - Server-side search + pagination
 * - Status filter (New / Exists / Conflict)
 * - Select new records for import
 * - Import with progress feedback and audit
 * - Excel-like column header sorting & filtering (client-side on current page)
 */
export function SyncWorkspace() {
    const { entityType } = useParams<{ entityType: string }>();
    const navigate = useNavigate();
    const entity: EntityType = entityType === 'suppliers' ? 'suppliers' : 'catalog';
    const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    // ─── State ──────────────────────────────────────────────────────────
    const [companyId, setCompanyId] = useState<number>(1);
    const [search, setSearch] = useState('');
    const [debouncedSearch, setDebouncedSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState<string>('');
    const [page, setPage] = useState(1);
    const [loading, setLoading] = useState(false);
    const [importing, setImporting] = useState(false);

    // Catalog state
    const [catalogPreview, setCatalogPreview] = useState<CatalogSyncPreviewDto | null>(null);
    // Supplier state
    const [supplierPreview, setSupplierPreview] = useState<SupplierSyncPreviewDto | null>(null);

    // Selection state
    const [selectedCodes, setSelectedCodes] = useState<Set<string>>(new Set());

    // Import result state
    const [importResult, setImportResult] = useState<SyncImportResultDto | null>(null);

    // Supplier review modal state
    const [showReviewModal, setShowReviewModal] = useState(false);

    // ─── Column sort & filter state (persisted across pages) ────────────
    const [sortConfig, setSortConfig] = useState<SortConfig | null>(null);
    const [columnFilters, setColumnFilters] = useState<ColumnFilters>({});

    // ─── Config ─────────────────────────────────────────────────────────
    const breadcrumb = entity === 'catalog' ? 'Sincronizar Catálogo' : 'Sincronizar Fornecedores';

    // ─── Debounced search ───────────────────────────────────────────────
    useEffect(() => {
        if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
        searchTimerRef.current = setTimeout(() => {
            setDebouncedSearch(search);
            setPage(1);
        }, 400);
        return () => { if (searchTimerRef.current) clearTimeout(searchTimerRef.current); };
    }, [search]);

    // ─── Data loading ───────────────────────────────────────────────────
    const loadPreview = useCallback(async () => {
        setLoading(true);
        setImportResult(null);
        setSelectedCodes(new Set());

        try {
            const params = {
                search: debouncedSearch || undefined,
                statusFilter: statusFilter || undefined,
                page,
                pageSize: PAGE_SIZE
            };

            if (entity === 'catalog') {
                const data = await api.sync.catalog.preview(companyId, params);
                setCatalogPreview(data);
                setSupplierPreview(null);
            } else {
                const data = await api.sync.suppliers.preview(companyId, params);
                setSupplierPreview(data);
                setCatalogPreview(null);
            }
        } catch (err: any) {
            console.error('Sync preview error:', err);
        } finally {
            setLoading(false);
        }
    }, [entity, companyId, debouncedSearch, statusFilter, page]);

    useEffect(() => {
        loadPreview();
    }, [loadPreview]);

    // ─── Column filter handlers ─────────────────────────────────────────
    // Status column filter is routed to the server-side statusFilter state
    // (shared with the top dropdown). All other columns remain client-side.
    const handleFilterChange = useCallback((key: string, value: string | string[] | undefined) => {
        if (key === 'status') {
            // Route to server-side statusFilter (comma-separated for multi-select)
            if (value === undefined || (Array.isArray(value) && value.length === 0)) {
                setStatusFilter('');
            } else if (Array.isArray(value)) {
                setStatusFilter(value.join(','));
            } else {
                setStatusFilter(String(value));
            }
            setPage(1);
            return;
        }
        setColumnFilters(prev => {
            const next = { ...prev };
            if (value === undefined) {
                delete next[key];
            } else {
                next[key] = value;
            }
            return next;
        });
    }, []);

    const clearAllColumnState = useCallback(() => {
        setSortConfig(null);
        setColumnFilters({});
        setStatusFilter('');
        setPage(1);
    }, []);

    // Derive status header filter state from the server-side statusFilter
    const statusHeaderFilter: string[] = useMemo(() => {
        if (!statusFilter) return [];
        return statusFilter.split(',').map(s => s.trim()).filter(Boolean);
    }, [statusFilter]);

    // Merged column filters map: includes status derived from statusFilter for header display
    const mergedColumnFilters: ColumnFilters = useMemo(() => {
        const merged = { ...columnFilters };
        if (statusHeaderFilter.length > 0) {
            merged['status'] = statusHeaderFilter;
        }
        return merged;
    }, [columnFilters, statusHeaderFilter]);

    const clientFilterCount = Object.keys(columnFilters).length;
    const hasColumnState = sortConfig !== null || clientFilterCount > 0 || statusFilter !== '';
    const activeFilterCount = clientFilterCount + (statusFilter ? 1 : 0);

    // ─── Processed items (filtered + sorted) ────────────────────────────
    const rawItems = (entity === 'catalog' ? catalogPreview?.items : supplierPreview?.items) ?? [];

    const processedItems = useMemo(() => {
        if (entity === 'catalog') {
            const items = rawItems as CatalogSyncPreviewItemDto[];
            const filtered = applyColumnFilters(items, columnFilters, CATALOG_COLUMNS);
            return applySorting(filtered, sortConfig, CATALOG_COLUMNS);
        } else {
            const items = rawItems as SupplierSyncPreviewItemDto[];
            const filtered = applyColumnFilters(items, columnFilters, SUPPLIER_COLUMNS);
            return applySorting(filtered, sortConfig, SUPPLIER_COLUMNS);
        }
    }, [entity, rawItems, columnFilters, sortConfig]);

    // ─── Selection handlers ─────────────────────────────────────────────
    const newItems = processedItems.filter((i: any) => i.status === 'New');

    const toggleCode = (code: string) => {
        setSelectedCodes(prev => {
            const next = new Set(prev);
            if (next.has(code)) next.delete(code);
            else next.add(code);
            return next;
        });
    };

    const toggleAll = () => {
        const allNewCodes = newItems.map((i: any) =>
            entity === 'catalog'
                ? (i as CatalogSyncPreviewItemDto).primaveraCode
                : (i as SupplierSyncPreviewItemDto).primaveraCode
        );
        if (selectedCodes.size === allNewCodes.length && allNewCodes.length > 0) {
            setSelectedCodes(new Set());
        } else {
            setSelectedCodes(new Set(allNewCodes));
        }
    };

    // ─── Import handler ─────────────────────────────────────────────────
    const handleImport = async () => {
        if (selectedCodes.size === 0) return;

        // For suppliers, open the review modal instead of importing directly
        if (entity === 'suppliers') {
            setShowReviewModal(true);
            return;
        }

        // Catalog: direct import (unchanged)
        setImporting(true);
        setImportResult(null);

        try {
            const body = { selectedPrimaveraCodes: Array.from(selectedCodes) };
            const result = await api.sync.catalog.import(companyId, body);

            setImportResult(result);
            setSelectedCodes(new Set());
            // Reload to show updated statuses
            await loadPreview();
        } catch (err: any) {
            console.error('Import error:', err);
        } finally {
            setImporting(false);
        }
    };

    // ─── Reviewed import callback (suppliers only) ───────────────────────
    const handleReviewedImportSuccess = useCallback((result: SyncImportResultDto, importedCodes: string[]) => {
        setShowReviewModal(false);
        setImportResult(result);
        // Clear only successfully imported codes from selection
        setSelectedCodes(prev => {
            const next = new Set(prev);
            importedCodes.forEach(c => next.delete(c));
            return next;
        });
        // Reload to show updated statuses
        loadPreview();
    }, [loadPreview]);

    // ─── Computed ────────────────────────────────────────────────────────
    const preview = entity === 'catalog' ? catalogPreview : supplierPreview;
    const totalRecords = preview?.totalPrimaveraRecords ?? 0;
    const totalPages = Math.ceil(totalRecords / PAGE_SIZE);

    return (
        <div className="sync-workspace" id="sync-workspace-root">
            {/* ── Header ── */}
            <div className="sync-header">
                <button
                    className="sync-back-btn"
                    onClick={() => navigate('/settings/master-data')}
                    title="Voltar para Dados Mestres"
                >
                    <ArrowLeft size={18} />
                    <span>Dados Mestres</span>
                </button>
                <div className="sync-header-content">
                    <div className="sync-header-title">
                        <Database size={22} />
                        <h1>{breadcrumb}</h1>
                    </div>
                    <p className="sync-header-subtitle">
                        Compare os registros do Primavera com o Portal e importe novos itens de forma controlada.
                    </p>
                </div>
            </div>

            {/* ── Controls bar ── */}
            <div className="sync-controls">
                {/* Company selector */}
                <div className="sync-control-group">
                    <Building2 size={16} />
                    <select
                        id="sync-company-select"
                        value={companyId}
                        onChange={e => { setCompanyId(Number(e.target.value)); setPage(1); }}
                        className="sync-select"
                    >
                        {COMPANIES.map(c => (
                            <option key={c.id} value={c.id}>{c.name}</option>
                        ))}
                    </select>
                </div>

                {/* Search */}
                <div className="sync-control-group sync-search-group">
                    <Search size={16} />
                    <input
                        id="sync-search-input"
                        type="text"
                        placeholder={entity === 'catalog' ? 'Buscar por código ou descrição...' : 'Buscar por código, nome ou NIF...'}
                        value={search}
                        onChange={e => setSearch(e.target.value)}
                        className="sync-search-input"
                    />
                </div>

                {/* Status filter — synced with header Status column filter */}
                <div className="sync-control-group">
                    <Filter size={16} />
                    <select
                        id="sync-status-filter"
                        value={['New', 'Exists', 'Conflict'].includes(statusFilter) ? statusFilter : ''}
                        onChange={e => { setStatusFilter(e.target.value); setPage(1); }}
                        className="sync-select"
                    >
                        <option value="">{statusFilter && !['New', 'Exists', 'Conflict'].includes(statusFilter) ? '⏺ Filtrado por coluna' : 'Todos os Status'}</option>
                        <option value="New">🟢 Novos</option>
                        <option value="Exists">⚪ Existentes</option>
                        <option value="Conflict">🟠 Conflitos</option>
                    </select>
                </div>

                {/* Refresh */}
                <button className="sync-btn sync-btn-secondary" onClick={loadPreview} disabled={loading}>
                    <RefreshCw size={16} className={loading ? 'sync-spin' : ''} />
                    Atualizar
                </button>
            </div>

            {/* ── Summary cards ── */}
            {preview && (
                <div className="sync-summary-cards">
                    <div className="sync-summary-card sync-summary-total">
                        <span className="sync-summary-label">Total Primavera</span>
                        <span className="sync-summary-value">{preview.totalPrimaveraRecords}</span>
                    </div>
                    <div className="sync-summary-card sync-summary-new">
                        <span className="sync-summary-label">Novos</span>
                        <span className="sync-summary-value">{preview.newCount}</span>
                    </div>
                    <div className="sync-summary-card sync-summary-exists">
                        <span className="sync-summary-label">Existentes</span>
                        <span className="sync-summary-value">{preview.existsCount}</span>
                    </div>
                    <div className="sync-summary-card sync-summary-conflict">
                        <span className="sync-summary-label">Conflitos</span>
                        <span className="sync-summary-value">{preview.conflictCount}</span>
                    </div>
                </div>
            )}

            {/* ── Import result banner ── */}
            {importResult && (
                <div className={`sync-import-result ${importResult.errors.length > 0 ? 'sync-result-warning' : 'sync-result-success'}`}>
                    <CheckCircle2 size={18} />
                    <div>
                        <strong>Importação concluída:</strong>{' '}
                        {importResult.created} criados, {importResult.skipped} ignorados.
                        {importResult.errors.length > 0 && (
                            <ul className="sync-result-errors">
                                {importResult.errors.map((e, i) => <li key={i}>{e}</li>)}
                            </ul>
                        )}
                    </div>
                </div>
            )}

            {/* ── Column filter/sort active banner ── */}
            {hasColumnState && !loading && processedItems.length >= 0 && (
                <div className="sync-column-state-banner" id="sync-column-state-banner">
                    <div className="sync-column-state-info">
                        <Filter size={14} />
                        <span>
                            Ordenação e filtros de coluna aplicados apenas aos itens visíveis nesta página.
                            {activeFilterCount > 0 && (
                                <> — <strong>{activeFilterCount} filtro{activeFilterCount > 1 ? 's' : ''} de coluna ativo{activeFilterCount > 1 ? 's' : ''}</strong></>
                            )}
                            {sortConfig && (
                                <> — <strong>Ordenado por: {
                                    (entity === 'catalog' ? CATALOG_COLUMNS : SUPPLIER_COLUMNS)
                                        .find(c => c.key === sortConfig.key)?.label ?? sortConfig.key
                                } ({sortConfig.direction === 'asc' ? '↑' : '↓'})</strong></>
                            )}
                            {' '}
                            <span className="sync-column-state-visible">
                                ({processedItems.length} de {rawItems.length} itens visíveis)
                            </span>
                        </span>
                    </div>
                    <button
                        className="sync-btn sync-btn-ghost sync-column-state-clear"
                        onClick={clearAllColumnState}
                        type="button"
                    >
                        <X size={14} />
                        Limpar tudo
                    </button>
                </div>
            )}

            {/* ── Import action bar ── */}
            {newItems.length > 0 && (
                <div className="sync-action-bar">
                    <button className="sync-btn sync-btn-ghost" onClick={toggleAll}>
                        {selectedCodes.size === newItems.length
                            ? <><CheckSquare size={16} /> Desmarcar Todos</>
                            : <><Square size={16} /> Selecionar Todos Novos ({newItems.length})</>
                        }
                    </button>
                    <button
                        className="sync-btn sync-btn-primary"
                        onClick={handleImport}
                        disabled={selectedCodes.size === 0 || importing}
                    >
                        {importing
                            ? <><Loader2 size={16} className="sync-spin" /> Importando...</>
                            : <><Download size={16} /> Importar Selecionados ({selectedCodes.size})</>
                        }
                    </button>
                </div>
            )}

            {/* ── Data table ── */}
            <div className="sync-table-container">
                {loading ? (
                    <div className="sync-loading">
                        <Loader2 size={28} className="sync-spin" />
                        <span>Carregando dados do Primavera...</span>
                    </div>
                ) : rawItems.length === 0 ? (
                    <div className="sync-empty">
                        <Database size={40} />
                        <p>Nenhum registro encontrado{debouncedSearch ? ` para "${debouncedSearch}"` : ''}.</p>
                    </div>
                ) : entity === 'catalog' ? (
                    <CatalogTable
                        items={processedItems as CatalogSyncPreviewItemDto[]}
                        selectedCodes={selectedCodes}
                        onToggle={toggleCode}
                        sortConfig={sortConfig}
                        columnFilters={mergedColumnFilters}
                        onSortChange={setSortConfig}
                        onFilterChange={handleFilterChange}
                    />
                ) : (
                    <SupplierTable
                        items={processedItems as SupplierSyncPreviewItemDto[]}
                        selectedCodes={selectedCodes}
                        onToggle={toggleCode}
                        sortConfig={sortConfig}
                        columnFilters={mergedColumnFilters}
                        onSortChange={setSortConfig}
                        onFilterChange={handleFilterChange}
                    />
                )}
            </div>

            {/* ── Pagination ── */}
            {totalPages > 1 && (
                <div className="sync-pagination">
                    <button
                        className="sync-btn sync-btn-ghost"
                        disabled={page <= 1}
                        onClick={() => setPage(p => Math.max(1, p - 1))}
                    >
                        <ChevronLeft size={16} /> Anterior
                    </button>
                    <span className="sync-page-info">
                        Página {page} de {totalPages}
                    </span>
                    <button
                        className="sync-btn sync-btn-ghost"
                        disabled={page >= totalPages}
                        onClick={() => setPage(p => p + 1)}
                    >
                        Próxima <ChevronRight size={16} />
                    </button>
                </div>
            )}

            {/* ── Supplier Import Review Modal ── */}
            {entity === 'suppliers' && (
                <SupplierImportReviewModal
                    isOpen={showReviewModal}
                    onClose={() => setShowReviewModal(false)}
                    onImportSuccess={handleReviewedImportSuccess}
                    selectedSuppliers={
                        (supplierPreview?.items ?? []).filter(
                            s => selectedCodes.has(s.primaveraCode) && s.status === 'New'
                        )
                    }
                    companyId={companyId}
                />
            )}
        </div>
    );
}

// ─── Catalog Table ──────────────────────────────────────────────────────────

interface TableInteractionProps {
    sortConfig: SortConfig | null;
    columnFilters: ColumnFilters;
    onSortChange: (sort: SortConfig | null) => void;
    onFilterChange: (key: string, value: string | string[] | undefined) => void;
}

function CatalogTable({
    items,
    selectedCodes,
    onToggle,
    sortConfig,
    columnFilters,
    onSortChange,
    onFilterChange
}: {
    items: CatalogSyncPreviewItemDto[];
    selectedCodes: Set<string>;
    onToggle: (code: string) => void;
} & TableInteractionProps) {
    return (
        <table className="sync-table" id="sync-catalog-table">
            <thead>
                <tr>
                    <th className="sync-th-check"></th>
                    {CATALOG_COLUMNS.map(col => (
                        <SortableFilterHeader
                            key={col.key}
                            label={col.label}
                            sortKey={col.key}
                            filterConfig={col.filterConfig}
                            currentSort={sortConfig}
                            currentFilter={columnFilters[col.key]}
                            onSortChange={onSortChange}
                            onFilterChange={onFilterChange}
                        />
                    ))}
                    <th>Detalhe</th>
                </tr>
            </thead>
            <tbody>
                {items.length === 0 ? (
                    <tr>
                        <td colSpan={9} className="sync-table-no-results">
                            <Filter size={16} />
                            <span>Nenhum item corresponde aos filtros de coluna aplicados.</span>
                        </td>
                    </tr>
                ) : items.map((item, idx) => {
                    const cfg = STATUS_CONFIG[item.status];
                    const isNew = item.status === 'New';
                    const isSelected = selectedCodes.has(item.primaveraCode);
                    return (
                        <tr
                            key={`${item.primaveraCode}-${idx}`}
                            className={`sync-row ${cfg.bgClass} ${isSelected ? 'sync-row-selected' : ''}`}
                            onClick={() => isNew && onToggle(item.primaveraCode)}
                            style={{ cursor: isNew ? 'pointer' : 'default' }}
                        >
                            <td className="sync-td-check">
                                {isNew && (
                                    isSelected
                                        ? <CheckSquare size={16} className="sync-check-active" />
                                        : <Square size={16} className="sync-check-inactive" />
                                )}
                            </td>
                            <td>
                                <span className="sync-status-badge" style={{ color: cfg.color }}>
                                    {cfg.icon}
                                    <span>{cfg.label}</span>
                                </span>
                            </td>
                            <td className="sync-td-code">{item.primaveraCode}</td>
                            <td>{item.primaveraDescription ?? '—'}</td>
                            <td>{item.primaveraFamily ?? '—'}</td>
                            <td>{item.primaveraBaseUnit ?? '—'}</td>
                            <td className="sync-td-portal">{item.portalCode ?? '—'}</td>
                            <td className="sync-td-portal">{item.portalDescription ?? '—'}</td>
                            <td className="sync-td-detail">
                                {item.conflictDetail && (
                                    <span className="sync-conflict-detail">
                                        <AlertTriangle size={12} />
                                        {item.conflictDetail}
                                    </span>
                                )}
                            </td>
                        </tr>
                    );
                })}
            </tbody>
        </table>
    );
}

// ─── Supplier Table ─────────────────────────────────────────────────────────

function SupplierTable({
    items,
    selectedCodes,
    onToggle,
    sortConfig,
    columnFilters,
    onSortChange,
    onFilterChange
}: {
    items: SupplierSyncPreviewItemDto[];
    selectedCodes: Set<string>;
    onToggle: (code: string) => void;
} & TableInteractionProps) {
    return (
        <table className="sync-table" id="sync-suppliers-table">
            <thead>
                <tr>
                    <th className="sync-th-check"></th>
                    {SUPPLIER_COLUMNS.map(col => (
                        <SortableFilterHeader
                            key={col.key}
                            label={col.label}
                            sortKey={col.key}
                            filterConfig={col.filterConfig}
                            currentSort={sortConfig}
                            currentFilter={columnFilters[col.key]}
                            onSortChange={onSortChange}
                            onFilterChange={onFilterChange}
                        />
                    ))}
                    <th>Detalhe</th>
                </tr>
            </thead>
            <tbody>
                {items.length === 0 ? (
                    <tr>
                        <td colSpan={8} className="sync-table-no-results">
                            <Filter size={16} />
                            <span>Nenhum item corresponde aos filtros de coluna aplicados.</span>
                        </td>
                    </tr>
                ) : items.map((item, idx) => {
                    const cfg = STATUS_CONFIG[item.status];
                    const isNew = item.status === 'New';
                    const isSelected = selectedCodes.has(item.primaveraCode);
                    return (
                        <tr
                            key={`${item.primaveraCode}-${idx}`}
                            className={`sync-row ${cfg.bgClass} ${isSelected ? 'sync-row-selected' : ''}`}
                            onClick={() => isNew && onToggle(item.primaveraCode)}
                            style={{ cursor: isNew ? 'pointer' : 'default' }}
                        >
                            <td className="sync-td-check">
                                {isNew && (
                                    isSelected
                                        ? <CheckSquare size={16} className="sync-check-active" />
                                        : <Square size={16} className="sync-check-inactive" />
                                )}
                            </td>
                            <td>
                                <span className="sync-status-badge" style={{ color: cfg.color }}>
                                    {cfg.icon}
                                    <span>{cfg.label}</span>
                                </span>
                            </td>
                            <td className="sync-td-code">{item.primaveraCode}</td>
                            <td>{item.primaveraName ?? '—'}</td>
                            <td>{item.primaveraTaxId ?? '—'}</td>
                            <td className="sync-td-portal">{item.portalName ?? '—'}</td>
                            <td className="sync-td-portal">{item.portalTaxId ?? '—'}</td>
                            <td className="sync-td-detail">
                                {item.conflictDetail && (
                                    <span className="sync-conflict-detail">
                                        <AlertTriangle size={12} />
                                        {item.conflictDetail}
                                    </span>
                                )}
                            </td>
                        </tr>
                    );
                })}
            </tbody>
        </table>
    );
}
