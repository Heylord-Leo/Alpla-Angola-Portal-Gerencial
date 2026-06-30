import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Upload, Plus, ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { EquipmentSummaryCards } from '../../components/it/EquipmentSummaryCards';
import { EquipmentTable } from '../../components/it/EquipmentTable';
import { EquipmentQuickViewDrawer } from '../../components/it/EquipmentQuickViewDrawer';
import { EquipmentFormModal } from '../../components/it/EquipmentFormModal';
import { BatchEquipmentModal } from '../../components/it/BatchEquipmentModal';
import { ImportEquipmentModal } from '../../components/it/ImportEquipmentModal';
import { ManageEquipmentTypesModal } from '../../components/it/ManageEquipmentTypesModal';
import { ManageEquipmentCatalogsModal } from '../../components/it/ManageEquipmentCatalogsModal';
import { SearchFilterBar } from '../../components/ui/SearchFilterBar';
import type { ITEquipmentSummary, ITEquipmentListResponse, ITEquipmentFilterOptions } from '../../types/itEquipment';

export default function ITEquipmentPage() {
    const { id: urlEquipmentId } = useParams<{ id?: string }>();
    const navigate = useNavigate();
    const [summary, setSummary] = useState<ITEquipmentSummary | null>(null);
    const [listData, setListData] = useState<ITEquipmentListResponse | null>(null);
    const [filterOptions, setFilterOptions] = useState<ITEquipmentFilterOptions | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState<string>('');
    const [typeFilter, setTypeFilter] = useState<string>('');
    const [plantFilter, setPlantFilter] = useState<string>('');
    const [manufacturerFilter, setManufacturerFilter] = useState<string>('');
    const [page, setPage] = useState(1);
    const [pageSize] = useState(30);
    const [sortBy, setSortBy] = useState<string>('');
    const [isDescending, setIsDescending] = useState(false);

    // Modals & drawers
    const [selectedEquipmentId, setSelectedEquipmentId] = useState<string | null>(urlEquipmentId ?? null);
    const [showCreateModal, setShowCreateModal] = useState(false);
    const [showBatchModal, setShowBatchModal] = useState(false);
    const [showImportModal, setShowImportModal] = useState(false);
    const [showFilters, setShowFilters] = useState(false);
    const [showTypesModal, setShowTypesModal] = useState(false);
    const [showCatalogsModal, setShowCatalogsModal] = useState(false);
    const [equipmentTypes, setEquipmentTypes] = useState<Array<{ value: string; label: string }>>([]);

    // Load dynamic equipment types for filter dropdown
    useEffect(() => {
        itEquipmentApi.types.list(true).then(types => {
            setEquipmentTypes(types.map(t => ({ value: t.code, label: t.displayName })));
        }).catch(() => {});
    }, []);

    const loadData = useCallback(async () => {
        try {
            setLoading(true);
            setError(null);
            const [summaryData, listResult, filters] = await Promise.all([
                itEquipmentApi.getSummary(),
                itEquipmentApi.list({
                    search: search || undefined,
                    statusCode: statusFilter || undefined,
                    equipmentType: typeFilter || undefined,
                    plant: plantFilter || undefined,
                    manufacturer: manufacturerFilter || undefined,
                    sortBy: sortBy || undefined,
                    isDescending,
                    page,
                    pageSize
                }),
                filterOptions ? Promise.resolve(filterOptions) : itEquipmentApi.getFilterOptions()
            ]);
            setSummary(summaryData);
            setListData(listResult);
            if (!filterOptions) setFilterOptions(filters);
        } catch (err: any) {
            setError(err.message || 'Erro ao carregar dados.');
        } finally {
            setLoading(false);
        }
    }, [search, statusFilter, typeFilter, plantFilter, manufacturerFilter, sortBy, isDescending, page, pageSize]);

    useEffect(() => { loadData(); }, [loadData]);

    const handleKpiClick = (status: string) => {
        setStatusFilter(prev => prev === status ? '' : status);
        setPage(1);
    };

    const handleSort = (field: string) => {
        if (sortBy === field) {
            setIsDescending(!isDescending);
        } else {
            setSortBy(field);
            setIsDescending(false);
        }
    };

    const handleRefresh = () => {
        setFilterOptions(null);
        loadData();
    };

    const clearFilters = () => {
        setSearch('');
        setStatusFilter('');
        setTypeFilter('');
        setPlantFilter('');
        setManufacturerFilter('');
        setPage(1);
    };

    const hasActiveFilters = !!(search || statusFilter || typeFilter || plantFilter || manufacturerFilter);
    const totalPages = listData ? Math.ceil(listData.totalCount / pageSize) : 0;

    return (
        <>
            {/* Page-level action bar */}
            <div data-tour="it-action-buttons" style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: '10px' }}>
                <button
                    onClick={handleRefresh}
                    style={{
                        display: 'flex', alignItems: 'center', gap: 6, padding: '8px 14px',
                        border: '1px solid var(--color-border)', borderRadius: 8,
                        backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-muted)',
                        fontSize: '0.85rem', fontWeight: 500, cursor: 'pointer', transition: 'all 0.2s',
                    }}
                >
                    <RefreshCw size={15} /> Atualizar
                </button>
                <button
                    onClick={() => setShowImportModal(true)}
                    style={{
                        display: 'flex', alignItems: 'center', gap: 6, padding: '8px 14px',
                        border: '1px solid var(--color-border)', borderRadius: 8,
                        backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-muted)',
                        fontSize: '0.85rem', fontWeight: 500, cursor: 'pointer', transition: 'all 0.2s',
                    }}
                >
                    <Upload size={15} /> Importar CSV
                </button>
                <button
                    onClick={() => navigate('/it/equipment/new')}
                    style={{
                        display: 'flex', alignItems: 'center', gap: 6, padding: '8px 16px',
                        backgroundColor: 'var(--color-primary)', border: 'none',
                        borderRadius: 8, cursor: 'pointer', color: '#fff', fontSize: '0.85rem',
                        fontWeight: 600, transition: 'all 0.2s',
                        boxShadow: '0 2px 8px rgba(var(--color-primary-rgb), 0.3)'
                    }}
                >
                    <Plus size={15} /> Novo Equipamento
                </button>
            </div>

            {/* KPI Cards */}
            {summary && (
                <div data-tour="it-summary-cards">
                    <EquipmentSummaryCards summary={summary} activeFilter={statusFilter} onFilterClick={handleKpiClick} />
                </div>
            )}

            {/* Search & Filter Bar — standard Portal component */}
            <SearchFilterBar
                searchPlaceholder="Buscar por asset tag, hostname, serial, dono, modelo, fabricante, MAC..."
                searchValue={search}
                onSearchChange={(val) => { setSearch(val); setPage(1); }}
                onToggleAdvancedFilters={() => setShowFilters(!showFilters)}
                showAdvancedFilters={showFilters}
                actions={
                    hasActiveFilters ? (
                        <button
                            onClick={clearFilters}
                            style={{
                                padding: '8px 12px', border: 'none', background: 'transparent',
                                cursor: 'pointer', color: '#ef4444', fontSize: '0.8rem', fontWeight: 600
                            }}
                        >
                            Limpar filtros
                        </button>
                    ) : undefined
                }
            />

            {/* Advanced Filter Dropdowns */}
            {showFilters && (
                <div style={{
                    display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                    gap: 12, padding: 16,
                    backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
                    borderRadius: 10
                }}>
                    <FilterSelect
                        label="Status"
                        value={statusFilter}
                        onChange={(v) => { setStatusFilter(v); setPage(1); }}
                        options={[
                            { value: 'AVAILABLE', label: 'Disponível' },
                            { value: 'IN_USE', label: 'Em uso' },
                            { value: 'RESERVED', label: 'Reservado' },
                            { value: 'IN_REPAIR', label: 'Em conserto' },
                            { value: 'LOST', label: 'Perdido' },
                            { value: 'RETIRED', label: 'Baixado' },
                            { value: 'DAMAGED', label: 'Danificado' },
                            { value: 'UNKNOWN', label: 'Desconhecido' },
                        ]}
                    />
                    <FilterSelect
                        label="Tipo"
                        value={typeFilter}
                        onChange={(v) => { setTypeFilter(v); setPage(1); }}
                        options={equipmentTypes}
                    />
                    <FilterSelect
                        label="Planta"
                        value={plantFilter}
                        onChange={(v) => { setPlantFilter(v); setPage(1); }}
                        options={(filterOptions?.plants ?? []).map(p => ({ value: p, label: p }))}
                    />
                    <FilterSelect
                        label="Fabricante"
                        value={manufacturerFilter}
                        onChange={(v) => { setManufacturerFilter(v); setPage(1); }}
                        options={(filterOptions?.manufacturers ?? []).map(m => ({ value: m, label: m }))}
                    />
                </div>
            )}

            {/* Error State */}
            {error && (
                <div style={{
                    padding: 16, backgroundColor: '#fef2f2', border: '1px solid #fecaca',
                    borderRadius: 10, color: '#dc2626', fontSize: '0.9rem'
                }}>
                    {error}
                </div>
            )}

            {/* Table */}
            <EquipmentTable
                data={listData}
                loading={loading}
                sortBy={sortBy}
                isDescending={isDescending}
                onSort={handleSort}
                onRowClick={(id) => setSelectedEquipmentId(id)}
            />

            {/* Pagination */}
            {listData && listData.totalCount > pageSize && (
                <div style={{
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                    padding: '12px 16px',
                    backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
                    borderRadius: 10
                }}>
                    <span style={{ color: 'var(--color-text-muted)', fontSize: '0.85rem' }}>
                        {listData.totalCount} equipamento{listData.totalCount !== 1 ? 's' : ''} encontrado{listData.totalCount !== 1 ? 's' : ''}
                    </span>
                    <div style={{ display: 'flex', gap: 4, alignItems: 'center' }}>
                        <button
                            disabled={page <= 1}
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            style={{
                                padding: '6px 10px', border: '1px solid var(--color-border)', borderRadius: 6,
                                cursor: page <= 1 ? 'default' : 'pointer', opacity: page <= 1 ? 0.4 : 1,
                                backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text)'
                            }}
                        >
                            <ChevronLeft size={14} />
                        </button>
                        <span style={{ padding: '0 12px', fontSize: '0.85rem', color: 'var(--color-text)' }}>
                            {page} / {totalPages}
                        </span>
                        <button
                            disabled={page >= totalPages}
                            onClick={() => setPage(p => p + 1)}
                            style={{
                                padding: '6px 10px', border: '1px solid var(--color-border)', borderRadius: 6,
                                cursor: page >= totalPages ? 'default' : 'pointer', opacity: page >= totalPages ? 0.4 : 1,
                                backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text)'
                            }}
                        >
                            <ChevronRight size={14} />
                        </button>
                    </div>
                </div>
            )}

            {/* Quick View Drawer */}
            {selectedEquipmentId && (
                <EquipmentQuickViewDrawer
                    equipmentId={selectedEquipmentId}
                    onClose={() => setSelectedEquipmentId(null)}
                    onRefresh={loadData}
                />
            )}

            {/* Create Modal */}
            {showCreateModal && (
                <EquipmentFormModal
                    onClose={() => setShowCreateModal(false)}
                    onSuccess={() => { setShowCreateModal(false); loadData(); }}
                />
            )}

            {/* Batch Modal */}
            {showBatchModal && (
                <BatchEquipmentModal
                    onClose={() => setShowBatchModal(false)}
                    onSuccess={() => {
                        setShowBatchModal(false);
                        loadData();
                    }}
                />
            )}

            {/* Import Modal */}
            {showImportModal && (
                <ImportEquipmentModal
                    onClose={() => setShowImportModal(false)}
                    onSuccess={() => { setShowImportModal(false); setFilterOptions(null); loadData(); }}
                />
            )}

            {/* Manage Types Modal */}
            {showTypesModal && (
                <ManageEquipmentTypesModal
                    onClose={() => { setShowTypesModal(false); /* Refresh types for filters */ itEquipmentApi.types.list(true).then(types => setEquipmentTypes(types.map(t => ({ value: t.code, label: t.displayName })))).catch(() => {}); }}
                />
            )}

            {/* Manage Catalogs Modal */}
            {showCatalogsModal && (
                <ManageEquipmentCatalogsModal
                    onClose={() => setShowCatalogsModal(false)}
                />
            )}
        </>
    );
}

// ─── Small Filter Select Component ───
function FilterSelect({ label, value, onChange, options }: {
    label: string;
    value: string;
    onChange: (v: string) => void;
    options: Array<{ value: string; label: string }>;
}) {
    return (
        <div>
            <label style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 4, display: 'block' }}>
                {label}
            </label>
            <select
                value={value}
                onChange={(e) => onChange(e.target.value)}
                style={{
                    width: '100%', padding: '8px 10px', border: '1px solid var(--color-border)',
                    borderRadius: 6, backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text)',
                    fontSize: '0.85rem', outline: 'none', cursor: 'pointer'
                }}
            >
                <option value="">Todos</option>
                {options.map(opt => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
            </select>
        </div>
    );
}
