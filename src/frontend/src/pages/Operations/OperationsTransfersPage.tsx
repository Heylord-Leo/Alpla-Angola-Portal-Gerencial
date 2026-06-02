/**
 * Operations — Transferências Logísticas
 *
 * Phase 5 Frontend List Integration + Phase 5.1 Quick Viewer Drawer:
 * - Filter panel with plant, date range, status, search, page size
 * - Paginated transfer list (table) from GET /api/operations/transfers
 * - Click transfer opens a right-side Quick Viewer Drawer with timeline
 * - Manual lookup fallback (collapsible)
 *
 * Reuses Phase 3 timeline rendering (SummaryCard, TimelineSection, TimelineEventCard).
 *
 * @since v2.164.0 — Phase 3 manual lookup
 * @since v2.166.0 — Phase 5 list integration
 * @since v2.167.0 — Phase 5.1 Quick Viewer Drawer UX + status 5 mapping fix
 * @since v2.168.0 — Phase 5.2 Business-oriented summary UX
 * @since v2.169.0 — Phase 5.3 Status-aware stage derivation
 * @since v2.170.0 — Phase 5.4 List stage column + filter extension
 * @since v2.171.0 — Phase 6 Transfer Details in Quick Viewer Drawer
 * @since v2.173.0 — Phase 7 Runtime Visual Validation & UX Refinement
 */

import React, { useState, useCallback, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
    Search, CheckCircle2, Clock, AlertTriangle, XCircle, X,
    Loader2, Package, ChevronRight, ChevronDown, ChevronLeft,
    Database, Server, Hash, Gauge, Timer, Layers, List, Settings2,
    Activity, Box, Truck, ClipboardCheck, Wrench, BarChart3
} from 'lucide-react';
import { ApiError } from '../../lib/api';
import { fetchOperationsTimeline, fetchOperationsTransfers, fetchOperationsTransferDetails } from '../../lib/operationsApi';
import type {
    OperationsTimelineResponse, OperationsTimelineEvent,
    OperationsTransferListResponse, OperationsTransferListItem,
    OperationsTransferListFilters, OperationsTransferDetail,
    OperationsTransferMaterial, OperationsTransferQuantity,
    OperationsTransferLoading, OperationsTransferGoodsReceipt,
    OperationsTransferTechRefs
} from '../../types/operations.types';

// ─── Constants ───

const PLANTS = [
    { value: 'VIANA1', label: 'VIANA1 — Viana 1' },
    { value: 'VIANA2', label: 'VIANA2 — Viana 2' },
    { value: 'VIANA3', label: 'VIANA3 — Viana 3' },
];

const STATUS_OPTIONS = [
    { value: '', label: 'Todos' },
    { value: 'ACTIVE', label: 'Ativos' },
    { value: 'SUBMITTED', label: 'Submetidos' },
    { value: 'PARTIALLY_DELIVERED', label: 'Parcialmente entregues' },
    { value: 'COMPLETED', label: 'Concluídos' },
    { value: 'CANCELLED', label: 'Cancelados' },
];

const PAGE_SIZE_OPTIONS = [10, 25, 50, 100];

const PIPELINE_BADGE: Record<string, { label: string; bg: string; color: string }> = {
    STANDARD: { label: 'Standard', bg: 'rgba(37, 99, 235, 0.1)', color: '#2563eb' },
    INHOUSE:  { label: 'InHouse',  bg: 'rgba(124, 58, 237, 0.1)', color: '#7c3aed' },
    PARTIAL:  { label: 'Partial',  bg: 'rgba(234, 88, 12, 0.1)',  color: '#ea580c' },
};

const SEVERITY_STYLE: Record<string, { border: string; bg: string; iconColor: string }> = {
    success: { border: '#16a34a', bg: 'rgba(22, 163, 74, 0.06)',  iconColor: '#16a34a' },
    info:    { border: '#2563eb', bg: 'rgba(37, 99, 235, 0.04)',  iconColor: '#2563eb' },
    warning: { border: '#d97706', bg: 'rgba(217, 119, 6, 0.06)',  iconColor: '#d97706' },
    error:   { border: '#dc2626', bg: 'rgba(220, 38, 38, 0.06)',  iconColor: '#dc2626' },
};

const SEVERITY_BADGE: Record<string, { bg: string; color: string }> = {
    success: { bg: 'rgba(22, 163, 74, 0.1)',  color: '#16a34a' },
    info:    { bg: 'rgba(37, 99, 235, 0.1)',  color: '#2563eb' },
    warning: { bg: 'rgba(217, 119, 6, 0.1)',  color: '#d97706' },
    error:   { bg: 'rgba(220, 38, 38, 0.1)',  color: '#dc2626' },
};

// ─── Helpers ───

function formatDateShort(iso: string | null): string | null {
    if (!iso) return null;
    try {
        const d = new Date(iso);
        if (isNaN(d.getTime())) return null;
        return d.toLocaleDateString('pt-PT', { day: '2-digit', month: '2-digit', year: 'numeric' });
    } catch { return null; }
}

function formatDateFull(dateStr: string): string {
    try {
        const d = new Date(dateStr);
        if (isNaN(d.getTime())) return dateStr;
        const day = String(d.getDate()).padStart(2, '0');
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const year = d.getFullYear();
        const hours = String(d.getHours()).padStart(2, '0');
        const minutes = String(d.getMinutes()).padStart(2, '0');
        return `${day}/${month}/${year} ${hours}:${minutes}`;
    } catch { return dateStr; }
}

function toISODate(d: Date): string {
    return d.toISOString().slice(0, 10);
}

function getDefaultFilters(): OperationsTransferListFilters {
    const now = new Date();
    const from = new Date(now);
    from.setDate(from.getDate() - 30);
    return {
        plant: 'VIANA1',
        dateFrom: toISODate(from),
        dateTo: toISODate(now),
        status: '',
        articleSearch: '',
        poSearch: '',
        page: 1,
        pageSize: 25,
    };
}

// ─── Main Styles ───

const labelStyle: React.CSSProperties = {
    fontSize: '0.75rem',
    fontWeight: 700,
    color: 'var(--color-text-muted)',
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    fontFamily: 'var(--font-family-body)',
};

const inputStyle: React.CSSProperties = {
    padding: '0.6rem 0.8rem',
    fontSize: '0.875rem',
    fontFamily: 'var(--font-family-body)',
    border: '1px solid var(--color-border)',
    borderRadius: 'var(--radius-md)',
    backgroundColor: 'var(--color-bg-surface)',
    color: 'var(--color-text-main)',
    outline: 'none',
    transition: 'border-color 0.15s',
};

const thStyle: React.CSSProperties = {
    padding: '10px 14px',
    textAlign: 'left',
    fontWeight: 700,
    color: 'var(--color-text-muted)',
    fontSize: '0.72rem',
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    whiteSpace: 'nowrap',
};

const tdStyle: React.CSSProperties = {
    padding: '10px 14px',
    fontSize: '0.84rem',
    fontFamily: 'var(--font-family-body)',
    color: 'var(--color-text-main)',
    whiteSpace: 'nowrap',
};

const DRAWER_WIDTH = 600;

// ─── Component ───

export default function OperationsTransfersPage() {
    // ─── Filter state ───
    const [filters, setFilters] = useState<OperationsTransferListFilters>(getDefaultFilters);
    const [filterError, setFilterError] = useState('');

    // ─── List state ───
    const [listData, setListData] = useState<OperationsTransferListResponse | null>(null);
    const [listLoading, setListLoading] = useState(false);
    const [listError, setListError] = useState<string | null>(null);

    // ─── Selection + Timeline + Detail state (drawer) ───
    const [selectedTransfer, setSelectedTransfer] = useState<OperationsTransferListItem | null>(null);
    const [drawerOpen, setDrawerOpen] = useState(false);
    const [timelineData, setTimelineData] = useState<OperationsTimelineResponse | null>(null);
    const [timelineLoading, setTimelineLoading] = useState(false);
    const [timelineError, setTimelineError] = useState<string | null>(null);
    const [detailData, setDetailData] = useState<OperationsTransferDetail | null>(null);
    const [detailLoading, setDetailLoading] = useState(false);
    const [detailError, setDetailError] = useState<string | null>(null);

    // ─── Manual lookup state ───
    const [manualOpen, setManualOpen] = useState(false);
    const [manualPlant, setManualPlant] = useState('');
    const [manualId, setManualId] = useState('');
    const [manualLoading, setManualLoading] = useState(false);
    const [manualError, setManualError] = useState<string | null>(null);
    const [manualData, setManualData] = useState<OperationsTimelineResponse | null>(null);
    const [manualValidation, setManualValidation] = useState('');

    // ─── Update a single filter field ───
    const updateFilter = useCallback((key: keyof OperationsTransferListFilters, value: string | number) => {
        setFilters(prev => {
            const next = { ...prev, [key]: value };
            // Reset page on filter change (except page itself)
            if (key !== 'page') next.page = 1;
            return next;
        });
    }, []);

    // ─── Validate filters ───
    const validateFilters = useCallback((): boolean => {
        if (!filters.plant) { setFilterError('Selecione uma planta.'); return false; }
        if (!filters.dateFrom) { setFilterError('Informe a data inicial.'); return false; }
        if (!filters.dateTo) { setFilterError('Informe a data final.'); return false; }
        if (filters.dateFrom > filters.dateTo) { setFilterError('A data inicial deve ser anterior ou igual à data final.'); return false; }
        const from = new Date(filters.dateFrom);
        const to = new Date(filters.dateTo);
        const diffDays = Math.ceil((to.getTime() - from.getTime()) / (1000 * 60 * 60 * 24));
        if (diffDays > 90) { setFilterError('O intervalo máximo permitido é de 90 dias.'); return false; }
        setFilterError('');
        return true;
    }, [filters]);

    // ─── Search list ───
    const handleSearch = useCallback(async (pageOverride?: number) => {
        if (!validateFilters()) return;

        const searchFilters = { ...filters, page: pageOverride ?? 1 };
        if (pageOverride === undefined) {
            setFilters(prev => ({ ...prev, page: 1 }));
        }

        // Clear previous selection + timeline on new search
        setSelectedTransfer(null);
        setDrawerOpen(false);
        setTimelineData(null);
        setTimelineError(null);

        setListError(null);
        setListLoading(true);

        try {
            const result = await fetchOperationsTransfers(searchFilters);
            setListData(result);
            if (pageOverride !== undefined) {
                setFilters(prev => ({ ...prev, page: pageOverride }));
            }
        } catch (err) {
            if (err instanceof ApiError) {
                switch (err.status) {
                    case 400: setListError(err.message || 'Dados de entrada inválidos.'); break;
                    case 503: setListError('Integração AlplaPROD indisponível ou não configurada.'); break;
                    default: setListError('Não foi possível carregar as transferências neste momento.'); break;
                }
            } else {
                setListError('Não foi possível carregar as transferências neste momento.');
            }
            setListData(null);
        } finally {
            setListLoading(false);
        }
    }, [filters, validateFilters]);

    // ─── Page change ───
    const handlePageChange = useCallback((newPage: number) => {
        handleSearch(newPage);
    }, [handleSearch]);

    // ─── Select transfer → load details + timeline in parallel ───
    const handleSelectTransfer = useCallback(async (item: OperationsTransferListItem) => {
        setSelectedTransfer(item);
        setDrawerOpen(true);
        setTimelineData(null);
        setTimelineError(null);
        setTimelineLoading(true);
        setDetailData(null);
        setDetailError(null);
        setDetailLoading(true);

        // Load both in parallel — independent error handling
        const [detailResult, timelineResult] = await Promise.allSettled([
            fetchOperationsTransferDetails(item.plant, item.idBestellung),
            fetchOperationsTimeline(item.plant, item.idBestellung),
        ]);

        // Handle detail result
        if (detailResult.status === 'fulfilled') {
            setDetailData(detailResult.value);
        } else {
            const err = detailResult.reason;
            if (err instanceof ApiError) {
                switch (err.status) {
                    case 400: setDetailError(err.message || 'Dados de entrada inválidos.'); break;
                    case 404: setDetailError('Detalhes não encontrados para este pedido.'); break;
                    case 503: setDetailError('Integração AlplaPROD indisponível.'); break;
                    default: setDetailError('Não foi possível carregar os detalhes.'); break;
                }
            } else {
                setDetailError('Não foi possível carregar os detalhes.');
            }
        }
        setDetailLoading(false);

        // Handle timeline result
        if (timelineResult.status === 'fulfilled') {
            setTimelineData(timelineResult.value);
        } else {
            const err = timelineResult.reason;
            if (err instanceof ApiError) {
                switch (err.status) {
                    case 400: setTimelineError(err.message || 'Dados de entrada inválidos.'); break;
                    case 404: setTimelineError('Pedido de compra não encontrado para a planta selecionada.'); break;
                    case 503: setTimelineError('Integração AlplaPROD indisponível ou não configurada.'); break;
                    default: setTimelineError('Não foi possível carregar a timeline neste momento.'); break;
                }
            } else {
                setTimelineError('Não foi possível carregar a timeline neste momento.');
            }
        }
        setTimelineLoading(false);
    }, []);

    // ─── Close drawer ───
    const handleCloseDrawer = useCallback(() => {
        setDrawerOpen(false);
        setSelectedTransfer(null);
        setTimelineData(null);
        setTimelineError(null);
        setDetailData(null);
        setDetailError(null);
    }, []);

    // ─── Escape key to close drawer ───
    useEffect(() => {
        const handleEsc = (e: KeyboardEvent) => {
            if (e.key === 'Escape' && drawerOpen) handleCloseDrawer();
        };
        document.addEventListener('keydown', handleEsc);
        return () => document.removeEventListener('keydown', handleEsc);
    }, [drawerOpen, handleCloseDrawer]);

    // ─── Manual lookup ───
    const handleManualSearch = useCallback(async () => {
        if (!manualPlant) { setManualValidation('Selecione uma planta.'); return; }
        const id = parseInt(manualId, 10);
        if (!manualId || isNaN(id) || id <= 0) {
            setManualValidation('Informe um IdBestellung válido (número inteiro positivo).');
            return;
        }
        setManualValidation('');
        setManualError(null);
        setManualData(null);
        setManualLoading(true);

        try {
            const result = await fetchOperationsTimeline(manualPlant, id);
            setManualData(result);
        } catch (err) {
            if (err instanceof ApiError) {
                switch (err.status) {
                    case 400: setManualError(err.message || 'Dados de entrada inválidos.'); break;
                    case 404: setManualError('Pedido de compra não encontrado para a planta selecionada.'); break;
                    case 503: setManualError('Integração AlplaPROD indisponível ou não configurada.'); break;
                    default: setManualError('Não foi possível carregar a timeline neste momento.'); break;
                }
            } else {
                setManualError('Não foi possível carregar a timeline neste momento.');
            }
        } finally {
            setManualLoading(false);
        }
    }, [manualPlant, manualId]);

    const handleManualKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') handleManualSearch();
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
            {/* ═══ Page Header ═══ */}
            <div>
                <h1 style={{
                    fontFamily: 'var(--font-family-display)',
                    fontSize: '1.75rem',
                    fontWeight: 800,
                    color: 'var(--color-text-main)',
                    margin: 0,
                    letterSpacing: '-0.02em'
                }}>
                    Transferências Logísticas
                </h1>
                <p style={{
                    fontFamily: 'var(--font-family-body)',
                    fontSize: '0.9rem',
                    color: 'var(--color-text-muted)',
                    margin: '0.5rem 0 0 0',
                    lineHeight: 1.5
                }}>
                    Pesquise transferências entre plantas AlplaPROD e visualize a timeline de cada pedido.
                </p>
            </div>

            {/* ═══ Filter Panel ═══ */}
            <FilterPanel
                filters={filters}
                onUpdate={updateFilter}
                onSearch={() => handleSearch()}
                loading={listLoading}
                error={filterError}
            />

            {/* ═══ List Results ═══ */}
            <AnimatePresence mode="wait">
                {listLoading && (
                    <motion.div key="list-loading" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
                        style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '2.5rem', gap: '0.75rem' }}>
                        <Loader2 size={24} className="spin-icon" style={{ color: 'var(--color-primary)' }} />
                        <span style={{ fontSize: '0.9rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)' }}>
                            A carregar transferências...
                        </span>
                    </motion.div>
                )}

                {listError && !listLoading && (
                    <ErrorState key="list-error" message={listError} />
                )}

                {listData && !listLoading && !listError && (
                    <motion.div key="list-results" initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: -8 }} transition={{ duration: 0.3 }}
                        style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>

                        {/* List metadata */}
                        <div style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                            flexWrap: 'wrap', gap: '0.5rem'
                        }}>
                            <span style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)' }}>
                                <strong>{listData.totalCount}</strong> transferência{listData.totalCount !== 1 ? 's' : ''} encontrada{listData.totalCount !== 1 ? 's' : ''}
                                {' '}— pipeline <PipelineBadgeInline model={listData.pipelineModel} />
                                {' '}— {listData.queryDurationMs}ms
                            </span>
                        </div>

                        {/* Table */}
                        <TransferTable
                            items={listData.items}
                            selectedId={selectedTransfer?.idBestellung ?? null}
                            selectedPlant={selectedTransfer?.plant ?? null}
                            onSelect={handleSelectTransfer}
                        />

                        {/* Pagination */}
                        {listData.totalPages > 0 && (
                            <PaginationControls
                                page={listData.page}
                                totalPages={listData.totalPages}
                                totalCount={listData.totalCount}
                                onPageChange={handlePageChange}
                            />
                        )}
                    </motion.div>
                )}

                {!listLoading && !listError && !listData && (
                    <motion.div key="list-empty-initial" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
                        style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', padding: '2.5rem', gap: '0.75rem' }}>
                        <List size={36} strokeWidth={1.5} style={{ color: 'var(--color-text-muted)', opacity: 0.4 }} />
                        <p style={{ fontSize: '0.9rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)', margin: 0, textAlign: 'center', maxWidth: '400px', lineHeight: 1.5 }}>
                            Configure os filtros acima e clique em <strong>Pesquisar transferências</strong> para visualizar os resultados.
                        </p>
                    </motion.div>
                )}
            </AnimatePresence>

            {/* ═══ Quick Viewer Drawer ═══ */}
            <QuickViewerDrawer
                open={drawerOpen}
                selectedTransfer={selectedTransfer}
                timelineData={timelineData}
                timelineLoading={timelineLoading}
                timelineError={timelineError}
                detailData={detailData}
                detailLoading={detailLoading}
                detailError={detailError}
                onClose={handleCloseDrawer}
            />

            {/* ═══ Manual Lookup Fallback ═══ */}
            <div style={{
                backgroundColor: 'var(--color-bg-page)',
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-lg)',
                overflow: 'hidden',
            }}>
                <button
                    onClick={() => setManualOpen(!manualOpen)}
                    style={{
                        display: 'flex', alignItems: 'center', gap: '0.5rem',
                        width: '100%', padding: '0.85rem 1.25rem',
                        background: 'none', border: 'none', cursor: 'pointer',
                        fontFamily: 'var(--font-family-body)',
                        fontSize: '0.85rem', fontWeight: 700,
                        color: 'var(--color-text-muted)',
                        textAlign: 'left',
                    }}
                >
                    <Settings2 size={16} />
                    Consulta manual por IdBestellung
                    {manualOpen ? <ChevronDown size={16} style={{ marginLeft: 'auto' }} /> : <ChevronRight size={16} style={{ marginLeft: 'auto' }} />}
                </button>

                <AnimatePresence>
                    {manualOpen && (
                        <motion.div
                            initial={{ height: 0, opacity: 0 }} animate={{ height: 'auto', opacity: 1 }}
                            exit={{ height: 0, opacity: 0 }} transition={{ duration: 0.2 }}
                            style={{ overflow: 'hidden' }}
                        >
                            <div style={{ padding: '0 1.25rem 1.25rem', display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                                <p style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)', margin: 0, fontStyle: 'italic' }}>
                                    IDs conhecidos para teste: VIANA1/26, VIANA2/26, VIANA3/5
                                </p>
                                <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap', alignItems: 'flex-end' }}>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', minWidth: '200px', flex: '1 1 200px' }}>
                                        <label htmlFor="manual-plant" style={labelStyle}>Planta</label>
                                        <select id="manual-plant" value={manualPlant} onChange={e => setManualPlant(e.target.value)}
                                            onKeyDown={handleManualKeyDown} style={{ ...inputStyle, cursor: 'pointer', appearance: 'auto' }}>
                                            <option value="">— Selecione —</option>
                                            {PLANTS.map(p => <option key={p.value} value={p.value}>{p.label}</option>)}
                                        </select>
                                    </div>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', minWidth: '160px', flex: '1 1 160px' }}>
                                        <label htmlFor="manual-id" style={labelStyle}>Pedido de compra</label>
                                        <input id="manual-id" type="number" min={1} value={manualId}
                                            onChange={e => setManualId(e.target.value)} onKeyDown={handleManualKeyDown}
                                            placeholder="Ex.: 26" style={inputStyle} />
                                    </div>
                                    <button onClick={handleManualSearch} disabled={manualLoading}
                                        style={{
                                            display: 'flex', alignItems: 'center', gap: '0.5rem',
                                            padding: '0.6rem 1.1rem', fontSize: '0.82rem', fontWeight: 700,
                                            fontFamily: 'var(--font-family-body)',
                                            backgroundColor: manualLoading ? 'var(--color-text-muted)' : 'var(--color-primary)',
                                            color: '#fff', border: 'none', borderRadius: 'var(--radius-md)',
                                            cursor: manualLoading ? 'not-allowed' : 'pointer',
                                            transition: 'all 0.2s', whiteSpace: 'nowrap', alignSelf: 'flex-end'
                                        }}>
                                        {manualLoading ? <Loader2 size={14} className="spin-icon" /> : <Search size={14} />}
                                        Consultar timeline
                                    </button>
                                </div>
                                {manualValidation && (
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontSize: '0.82rem', color: '#dc2626', fontFamily: 'var(--font-family-body)' }}>
                                        <AlertTriangle size={14} /> {manualValidation}
                                    </div>
                                )}
                            </div>

                            {/* Manual results */}
                            <div style={{ padding: '0 1.25rem 1.25rem' }}>
                                <AnimatePresence mode="wait">
                                    {manualLoading && (
                                        <motion.div key="m-load" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
                                            style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '2rem', gap: '0.75rem' }}>
                                            <Loader2 size={24} className="spin-icon" style={{ color: 'var(--color-primary)' }} />
                                            <span style={{ fontSize: '0.9rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)' }}>A consultar timeline...</span>
                                        </motion.div>
                                    )}
                                    {manualError && !manualLoading && <ErrorState key="m-err" message={manualError} />}
                                    {manualData && !manualLoading && !manualError && (
                                        <motion.div key="m-data" initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }}
                                            exit={{ opacity: 0, y: -8 }} transition={{ duration: 0.3 }}
                                            style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
                                            <SummaryCard data={manualData} />
                                            <TimelineSection events={manualData.events} />
                                        </motion.div>
                                    )}
                                </AnimatePresence>
                            </div>
                        </motion.div>
                    )}
                </AnimatePresence>
            </div>
        </div>
    );
}

// ─── Filter Panel ───

function FilterPanel({ filters, onUpdate, onSearch, loading, error }: {
    filters: OperationsTransferListFilters;
    onUpdate: (key: keyof OperationsTransferListFilters, value: string | number) => void;
    onSearch: () => void;
    loading: boolean;
    error: string;
}) {
    const handleKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') onSearch();
    };

    return (
        <div style={{
            backgroundColor: 'var(--color-bg-page)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-lg)',
            padding: '1.25rem 1.5rem',
            display: 'flex', flexDirection: 'column', gap: '0.85rem',
        }}>
            {/* Row 1: Plant, DateFrom, DateTo, Status */}
            <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap', alignItems: 'flex-end' }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', minWidth: '200px', flex: '1 1 200px' }}>
                    <label htmlFor="ops-plant" style={labelStyle}>Planta *</label>
                    <select id="ops-plant" value={filters.plant} onChange={e => onUpdate('plant', e.target.value)}
                        onKeyDown={handleKeyDown} style={{ ...inputStyle, cursor: 'pointer', appearance: 'auto' }}>
                        {PLANTS.map(p => <option key={p.value} value={p.value}>{p.label}</option>)}
                    </select>
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', minWidth: '160px', flex: '1 1 160px' }}>
                    <label htmlFor="ops-date-from" style={labelStyle}>Data inicial *</label>
                    <input id="ops-date-from" type="date" value={filters.dateFrom}
                        onChange={e => onUpdate('dateFrom', e.target.value)} onKeyDown={handleKeyDown} style={inputStyle} />
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', minWidth: '160px', flex: '1 1 160px' }}>
                    <label htmlFor="ops-date-to" style={labelStyle}>Data final *</label>
                    <input id="ops-date-to" type="date" value={filters.dateTo}
                        onChange={e => onUpdate('dateTo', e.target.value)} onKeyDown={handleKeyDown} style={inputStyle} />
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', minWidth: '160px', flex: '1 1 160px' }}>
                    <label htmlFor="ops-status" style={labelStyle}>Status do pedido</label>
                    <select id="ops-status" value={filters.status} onChange={e => onUpdate('status', e.target.value)}
                        onKeyDown={handleKeyDown} style={{ ...inputStyle, cursor: 'pointer', appearance: 'auto' }}>
                        {STATUS_OPTIONS.map(s => <option key={s.value} value={s.value}>{s.label}</option>)}
                    </select>
                </div>
            </div>

            {/* Row 2: PO search, Article search, Page size, Search button */}
            <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap', alignItems: 'flex-end' }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', minWidth: '160px', flex: '1 1 160px' }}>
                    <label htmlFor="ops-po-search" style={labelStyle}>Pesquisar PO / Journal</label>
                    <input id="ops-po-search" type="text" value={filters.poSearch}
                        onChange={e => onUpdate('poSearch', e.target.value)} onKeyDown={handleKeyDown}
                        placeholder="IdBestellung ou nº journal" style={inputStyle} />
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', minWidth: '160px', flex: '1 1 160px' }}>
                    <label htmlFor="ops-article-search" style={labelStyle}>Pesquisar material</label>
                    <input id="ops-article-search" type="text" value={filters.articleSearch}
                        onChange={e => onUpdate('articleSearch', e.target.value)} onKeyDown={handleKeyDown}
                        placeholder="Nome do material ou alias" style={inputStyle} />
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', minWidth: '100px', flex: '0 0 100px' }}>
                    <label htmlFor="ops-page-size" style={labelStyle}>Por página</label>
                    <select id="ops-page-size" value={filters.pageSize}
                        onChange={e => onUpdate('pageSize', Number(e.target.value))}
                        style={{ ...inputStyle, cursor: 'pointer', appearance: 'auto' }}>
                        {PAGE_SIZE_OPTIONS.map(n => <option key={n} value={n}>{n}</option>)}
                    </select>
                </div>
                <button id="ops-search-btn" onClick={onSearch} disabled={loading}
                    style={{
                        display: 'flex', alignItems: 'center', gap: '0.5rem',
                        padding: '0.6rem 1.25rem', fontSize: '0.84rem', fontWeight: 700,
                        fontFamily: 'var(--font-family-body)',
                        backgroundColor: loading ? 'var(--color-text-muted)' : 'var(--color-primary)',
                        color: '#fff', border: 'none', borderRadius: 'var(--radius-md)',
                        cursor: loading ? 'not-allowed' : 'pointer',
                        transition: 'all 0.2s', whiteSpace: 'nowrap', alignSelf: 'flex-end',
                    }}>
                    {loading ? <Loader2 size={16} className="spin-icon" /> : <Search size={16} />}
                    Pesquisar transferências
                </button>
            </div>

            {/* Validation error */}
            {error && (
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontSize: '0.82rem', color: '#dc2626', fontFamily: 'var(--font-family-body)' }}>
                    <AlertTriangle size={14} /> {error}
                </div>
            )}
        </div>
    );
}

// ─── List-level Stage Approximation ───

/**
 * Derives an approximate operational stage for a list row from available fields.
 *
 * This is a lightweight approximation — the drawer timeline is the source of truth.
 * Based on T_Bestellungen.Status (mainStatus) from the list API:
 *
 *   7, 8    → Concluído
 *   5       → Parcialmente entregue
 *   2       → Aguardando recebimento (most submitted POs have pending receipts)
 *   6       → Em processamento
 *   3       → Cancelado
 *   1       → Pendente
 *   default → A verificar
 *
 * @since v2.170.0
 */
function deriveListStage(item: OperationsTransferListItem): { label: string; bg: string; color: string } {
    const status = item.mainStatus;
    const meaning = item.statusMeaning?.toLowerCase() ?? '';

    // Completed
    if (status === 7 || status === 8 || meaning === 'concluído')
        return { label: 'Concluído', bg: '#dcfce7', color: '#15803d' };

    // Partially delivered
    if (status === 5 || meaning.startsWith('parcialmente'))
        return { label: 'Parcialmente entregue', bg: '#fef3c7', color: '#92400e' };

    // Submitted — in validated cases, these POs have reached pending receipt
    if (status === 2 || meaning === 'submetido')
        return { label: 'Aguardando recebimento', bg: '#dbeafe', color: '#1e40af' };

    // Active / In processing
    if (status === 6 || meaning === 'ativo')
        return { label: 'Em processamento', bg: '#e0e7ff', color: '#3730a3' };

    // Cancelled
    if (status === 3 || meaning === 'cancelado')
        return { label: 'Cancelado', bg: '#fee2e2', color: '#991b1b' };

    // Pending (status 1)
    if (status === 1)
        return { label: 'Pendente', bg: '#f3f4f6', color: '#6b7280' };

    // Unknown / fallback
    return { label: 'A verificar', bg: '#f3f4f6', color: '#6b7280' };
}

// ─── Transfer Table ───

function TransferTable({ items, selectedId, selectedPlant, onSelect }: {
    items: OperationsTransferListItem[];
    selectedId: number | null;
    selectedPlant: string | null;
    onSelect: (item: OperationsTransferListItem) => void;
}) {
    if (items.length === 0) {
        return (
            <div style={{
                display: 'flex', flexDirection: 'column', alignItems: 'center',
                padding: '2.5rem', gap: '0.75rem',
                border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)',
            }}>
                <Package size={36} strokeWidth={1.5} style={{ color: 'var(--color-text-muted)', opacity: 0.4 }} />
                <p style={{ fontSize: '0.9rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)', margin: 0, textAlign: 'center' }}>
                    Nenhuma transferência encontrada para os filtros selecionados.
                </p>
            </div>
        );
    }

    const headers = ['Pedido', 'Journal', 'Pipeline', 'Status PO', 'Situação', 'Criado', 'Atualizado', 'Material', 'Qtd', 'Ev.'];

    return (
        <div style={{ overflowX: 'auto', borderRadius: 'var(--radius-lg)', border: '1px solid var(--color-border)' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.84rem' }}>
                <thead>
                    <tr style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '2px solid var(--color-border)' }}>
                        {headers.map(h => <th key={h} style={thStyle}>{h}</th>)}
                    </tr>
                </thead>
                <tbody>
                    <AnimatePresence>
                        {items.map((item, i) => {
                            const isSelected = item.idBestellung === selectedId && item.plant === selectedPlant;
                            const pipeBadge = PIPELINE_BADGE[item.pipelineModel] || PIPELINE_BADGE.STANDARD;
                            const sevBadge = SEVERITY_BADGE[item.severity] || SEVERITY_BADGE.info;

                            return (
                                <motion.tr
                                    key={`${item.plant}-${item.idBestellung}`}
                                    initial={{ opacity: 0 }}
                                    animate={{ opacity: 1 }}
                                    exit={{ opacity: 0 }}
                                    transition={{ delay: i * 0.02 }}
                                    onClick={() => onSelect(item)}
                                    style={{
                                        cursor: 'pointer',
                                        borderBottom: '1px solid var(--color-border)',
                                        transition: 'background-color 0.15s',
                                        backgroundColor: isSelected ? 'rgba(37, 99, 235, 0.08)' : 'transparent',
                                    }}
                                    onMouseEnter={e => {
                                        if (!isSelected) e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.04)';
                                    }}
                                    onMouseLeave={e => {
                                        e.currentTarget.style.backgroundColor = isSelected ? 'rgba(37, 99, 235, 0.08)' : 'transparent';
                                    }}
                                >
                                    {/* Pedido */}
                                    <td style={{ ...tdStyle, fontWeight: 700, fontFamily: 'var(--font-family-display)', color: 'var(--color-primary)' }}>
                                        #{item.idBestellung}
                                    </td>
                                    {/* Journal */}
                                    <td style={{ ...tdStyle, color: 'var(--color-text-muted)' }}>
                                        {item.journalNummer || '—'}
                                    </td>
                                    {/* Pipeline */}
                                    <td style={tdStyle}>
                                        <span style={{
                                            padding: '0.2rem 0.55rem', borderRadius: 'var(--radius-full)',
                                            fontSize: '0.68rem', fontWeight: 800, textTransform: 'uppercase',
                                            letterSpacing: '0.04em',
                                            backgroundColor: pipeBadge.bg, color: pipeBadge.color,
                                            fontFamily: 'var(--font-family-body)',
                                        }}>
                                            {pipeBadge.label}
                                        </span>
                                    </td>
                                    {/* Status PO */}
                                    <td style={tdStyle}>
                                        <span style={{
                                            display: 'inline-flex', alignItems: 'center', gap: '4px',
                                            padding: '0.2rem 0.55rem', borderRadius: 'var(--radius-full)',
                                            fontSize: '0.72rem', fontWeight: 700,
                                            backgroundColor: sevBadge.bg, color: sevBadge.color,
                                            fontFamily: 'var(--font-family-body)',
                                        }}>
                                            {item.statusMeaning || '—'}
                                        </span>
                                    </td>
                                    {/* Situação (operational stage approximation) */}
                                    <td style={tdStyle}>
                                        {(() => {
                                            const stage = deriveListStage(item);
                                            return (
                                                <span style={{
                                                    display: 'inline-flex', alignItems: 'center', gap: '4px',
                                                    padding: '0.2rem 0.55rem', borderRadius: 'var(--radius-full)',
                                                    fontSize: '0.68rem', fontWeight: 600,
                                                    backgroundColor: stage.bg, color: stage.color,
                                                    fontFamily: 'var(--font-family-body)',
                                                    whiteSpace: 'nowrap',
                                                }}>
                                                    {stage.label}
                                                </span>
                                            );
                                        })()}
                                    </td>
                                    {/* Criado */}
                                    <td style={{ ...tdStyle, color: 'var(--color-text-muted)' }}>
                                        {formatDateShort(item.createdDate) || '—'}
                                    </td>
                                    {/* Atualizado */}
                                    <td style={{ ...tdStyle, color: 'var(--color-text-muted)' }}>
                                        {formatDateShort(item.updatedDate) || '—'}
                                    </td>
                                    {/* Material */}
                                    <td style={{ ...tdStyle, maxWidth: '220px', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                        {item.materialName || item.articleAlias || '—'}
                                    </td>
                                    {/* Quantidade */}
                                    <td style={{ ...tdStyle, fontWeight: 600 }}>
                                        {item.quantity != null ? item.quantity.toLocaleString('pt-PT') : '—'}
                                    </td>
                                    {/* Eventos (count only) */}
                                    <td style={{ ...tdStyle, color: 'var(--color-text-muted)', fontSize: '0.78rem' }}>
                                        {item.expectedEventCount}
                                    </td>
                                </motion.tr>
                            );
                        })}
                    </AnimatePresence>
                </tbody>
            </table>
        </div>
    );
}

// ─── Pagination Controls ───

function PaginationControls({ page, totalPages, totalCount, onPageChange }: {
    page: number;
    totalPages: number;
    totalCount: number;
    onPageChange: (page: number) => void;
}) {
    return (
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0.25rem 0', flexWrap: 'wrap', gap: '0.5rem' }}>
            <span style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)' }}>
                {totalCount} transferência{totalCount !== 1 ? 's' : ''}
            </span>
            <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                <button disabled={page <= 1} onClick={() => onPageChange(page - 1)}
                    style={{
                        padding: '6px', borderRadius: 'var(--radius-md)',
                        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                        cursor: page <= 1 ? 'default' : 'pointer', opacity: page <= 1 ? 0.5 : 1,
                        display: 'flex', alignItems: 'center',
                    }}>
                    <ChevronLeft size={16} />
                </button>
                <span style={{ fontSize: '0.85rem', fontWeight: 600, fontFamily: 'var(--font-family-body)' }}>
                    {page} / {totalPages}
                </span>
                <button disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}
                    style={{
                        padding: '6px', borderRadius: 'var(--radius-md)',
                        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                        cursor: page >= totalPages ? 'default' : 'pointer', opacity: page >= totalPages ? 0.5 : 1,
                        display: 'flex', alignItems: 'center',
                    }}>
                    <ChevronRight size={16} />
                </button>
            </div>
        </div>
    );
}

// ─── Pipeline Badge Inline ───

function PipelineBadgeInline({ model }: { model: string }) {
    const badge = PIPELINE_BADGE[model] || PIPELINE_BADGE.STANDARD;
    return (
        <span style={{
            padding: '0.15rem 0.5rem', borderRadius: 'var(--radius-full)',
            fontSize: '0.68rem', fontWeight: 800, textTransform: 'uppercase',
            letterSpacing: '0.04em', backgroundColor: badge.bg, color: badge.color,
            fontFamily: 'var(--font-family-body)',
        }}>
            {badge.label}
        </span>
    );
}

// ─── Quick Viewer Drawer ───

function QuickViewerDrawer({ open, selectedTransfer, timelineData, timelineLoading, timelineError, detailData, detailLoading, detailError, onClose }: {
    open: boolean;
    selectedTransfer: OperationsTransferListItem | null;
    timelineData: OperationsTimelineResponse | null;
    timelineLoading: boolean;
    timelineError: string | null;
    detailData: OperationsTransferDetail | null;
    detailLoading: boolean;
    detailError: string | null;
    onClose: () => void;
}) {
    // Prevent body scroll when drawer is open
    useEffect(() => {
        if (open) {
            document.body.style.overflow = 'hidden';
        } else {
            document.body.style.overflow = '';
        }
        return () => { document.body.style.overflow = ''; };
    }, [open]);

    return (
        <AnimatePresence>
            {open && (
                <>
                    {/* Backdrop */}
                    <motion.div
                        key="drawer-backdrop"
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        transition={{ duration: 0.2 }}
                        onClick={onClose}
                        style={{
                            position: 'fixed',
                            top: 'var(--header-height)',
                            left: 0, right: 0, bottom: 0,
                            backgroundColor: 'rgba(0, 0, 0, 0.35)',
                            zIndex: 1000,
                        }}
                    />

                    {/* Drawer panel */}
                    <motion.div
                        key="drawer-panel"
                        initial={{ x: '100%' }}
                        animate={{ x: 0 }}
                        exit={{ x: '100%' }}
                        transition={{ type: 'spring', stiffness: 400, damping: 35 }}
                        style={{
                            position: 'fixed',
                            top: 'var(--header-height)',
                            right: 0,
                            height: 'calc(100vh - var(--header-height))',
                            width: `min(${DRAWER_WIDTH}px, 100vw)`,
                            backgroundColor: 'var(--color-bg-surface)',
                            borderLeft: '1px solid var(--color-border)',
                            boxShadow: '-8px 0 30px rgba(0, 0, 0, 0.12)',
                            zIndex: 1001,
                            display: 'flex', flexDirection: 'column',
                            overflow: 'hidden',
                        }}
                    >
                        {/* ── Drawer Header ── */}
                        <div style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                            padding: '1rem 1.25rem',
                            borderBottom: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-page)',
                            flexShrink: 0,
                        }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', minWidth: 0, flex: 1 }}>
                                <Package size={20} strokeWidth={2.5} style={{ color: 'var(--color-primary)', flexShrink: 0 }} />
                                <div style={{ minWidth: 0 }}>
                                    <div style={{
                                        fontFamily: 'var(--font-family-display)',
                                        fontSize: '1.05rem', fontWeight: 800,
                                        color: 'var(--color-text-main)',
                                        letterSpacing: '-0.01em',
                                        overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                                    }}>
                                        Pedido #{selectedTransfer?.idBestellung}
                                    </div>
                                    <div style={{
                                        display: 'flex', alignItems: 'center', gap: '0.5rem',
                                        fontSize: '0.78rem', color: 'var(--color-text-muted)',
                                        fontFamily: 'var(--font-family-body)', marginTop: '0.15rem',
                                    }}>
                                        {selectedTransfer?.journalNummer && (
                                            <span>Journal: {selectedTransfer.journalNummer}</span>
                                        )}
                                        <span>{selectedTransfer?.plant}</span>
                                    </div>
                                </div>
                            </div>

                            {/* Badges + Close */}
                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', flexShrink: 0 }}>
                                {selectedTransfer && (() => {
                                    const pipeBadge = PIPELINE_BADGE[selectedTransfer.pipelineModel] || PIPELINE_BADGE.STANDARD;
                                    const sevBadge = SEVERITY_BADGE[selectedTransfer.severity] || SEVERITY_BADGE.info;
                                    return (
                                        <>
                                            <span style={{
                                                padding: '0.2rem 0.5rem', borderRadius: 'var(--radius-full)',
                                                fontSize: '0.62rem', fontWeight: 800, textTransform: 'uppercase',
                                                letterSpacing: '0.04em',
                                                backgroundColor: pipeBadge.bg, color: pipeBadge.color,
                                                fontFamily: 'var(--font-family-body)',
                                            }}>
                                                {pipeBadge.label}
                                            </span>
                                            <span style={{
                                                padding: '0.2rem 0.5rem', borderRadius: 'var(--radius-full)',
                                                fontSize: '0.62rem', fontWeight: 700,
                                                backgroundColor: sevBadge.bg, color: sevBadge.color,
                                                fontFamily: 'var(--font-family-body)',
                                            }}>
                                                {selectedTransfer.statusMeaning}
                                            </span>
                                        </>
                                    );
                                })()}
                                <button
                                    onClick={onClose}
                                    aria-label="Fechar painel"
                                    style={{
                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        width: '32px', height: '32px',
                                        borderRadius: 'var(--radius-md)',
                                        border: '1px solid var(--color-border)',
                                        backgroundColor: 'transparent',
                                        cursor: 'pointer', transition: 'background-color 0.15s',
                                        color: 'var(--color-text-muted)',
                                    }}
                                    onMouseEnter={e => e.currentTarget.style.backgroundColor = 'rgba(220, 38, 38, 0.08)'}
                                    onMouseLeave={e => e.currentTarget.style.backgroundColor = 'transparent'}
                                >
                                    <X size={16} />
                                </button>
                            </div>
                        </div>

                        {/* ── Drawer Body (scrollable) ── */}
                        <div style={{
                            flex: 1, overflowY: 'auto',
                            padding: '1.25rem',
                            display: 'flex', flexDirection: 'column', gap: '1.25rem',
                        }}>
                            {/* ── Summary Card (from timeline) ── */}
                            {timelineData && !timelineLoading && !timelineError && (
                                <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.3 }}>
                                    <SummaryCard data={timelineData} detailData={detailData} />
                                </motion.div>
                            )}

                            {/* ── Detail Cards (independent loading) ── */}
                            {detailLoading && (
                                <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                                    style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '2rem', gap: '0.75rem' }}>
                                    <Loader2 size={20} className="spin-icon" style={{ color: 'var(--color-primary)' }} />
                                    <span style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)' }}>
                                        A carregar detalhes...
                                    </span>
                                </motion.div>
                            )}

                            {detailError && !detailLoading && (
                                <div style={{
                                    padding: '0.75rem 1rem', borderRadius: 'var(--radius-md)',
                                    backgroundColor: 'rgba(220, 38, 38, 0.06)', border: '1px solid rgba(220, 38, 38, 0.15)',
                                    fontSize: '0.82rem', color: '#dc2626', fontFamily: 'var(--font-family-body)',
                                }}>
                                    {detailError}
                                </div>
                            )}

                            {detailData && !detailLoading && !detailError && (
                                <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }}
                                    transition={{ duration: 0.3, delay: 0.1 }}
                                    style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                                    <DetailHeaderCard header={detailData.header} />
                                    <DetailMaterialCard material={detailData.material} />
                                    <DetailQuantityCard quantity={detailData.quantity} />
                                    <DetailLoadingCard loading={detailData.loading} pipelineModel={detailData.pipelineModel} />
                                    <DetailReceiptCard receipt={detailData.goodsReceipt} />
                                    <DetailTechRefsCard refs={detailData.technicalReferences} />
                                </motion.div>
                            )}

                            {/* ── Timeline Section (independent loading) ── */}
                            {timelineLoading && !detailLoading && (
                                <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                                    style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '2rem', gap: '0.75rem' }}>
                                    <Loader2 size={20} className="spin-icon" style={{ color: 'var(--color-primary)' }} />
                                    <span style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)' }}>
                                        A carregar timeline...
                                    </span>
                                </motion.div>
                            )}
                            {timelineError && !timelineLoading && <ErrorState message={timelineError} />}

                            {timelineData && !timelineLoading && !timelineError && (
                                <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }}
                                    transition={{ duration: 0.3, delay: 0.2 }}>
                                    <TimelineSection events={timelineData.events} />
                                </motion.div>
                            )}
                        </div>
                    </motion.div>
                </>
            )}
        </AnimatePresence>
    );
}

// ─── Business Stage Derivation (v2.175.0) ───

/**
 * Stage derivation logic (v2.175.0):
 *
 * Considers ALL events (completed + pending) for timeline-based priority.
 * Additionally uses PO status and detail quantity data to distinguish
 * partial receipt from full receipt (Phase 7.1 business rule).
 *
 * A pending GR_CREATED outranks a completed EDI_SYNCED because the
 * business flow has already advanced to the receipt stage.
 */
const STAGE_PRIORITY: string[] = [
    'PO_CREATED',
    'PO_REVISION',
    'EDI_CREATED',
    'EDI_EXPORTED',
    'EDI_SYNCED',
    'CALLOFF_CREATED',
    'LOADING_PLANNED',
    'LOADING_ORDER',
    'INHOUSE_DELIVERY',
    'GR_CREATED',
    'GR_COMPLETED',
];

/** Check if an event's status represents "completed" (mainStatus 21 or statusMeaning "Concluído"). */
function isEventStatusCompleted(event: OperationsTimelineEvent): boolean {
    return event.isCompleted || event.mainStatus === 21 || event.statusMeaning === 'Concluído';
}

/**
 * Resolve the base Portuguese business label for a given event.
 * GR_COMPLETED and GR_CREATED return receipt-related labels here,
 * but the caller may override them based on quantity/PO status data
 * (see deriveCurrentStage).
 */
function resolveStageLabel(event: OperationsTimelineEvent): string {
    switch (event.eventCode) {
        case 'GR_COMPLETED':
            // Base label — caller overrides for partial receipt
            return 'Recebimento concluído';
        case 'GR_CREATED':
            return isEventStatusCompleted(event) ? 'Recebimento concluído' : 'Aguardando recebimento';
        case 'INHOUSE_DELIVERY':
            return 'Entrega interna criada';
        case 'LOADING_ORDER':
            return isEventStatusCompleted(event) ? 'Carregamento concluído' : 'Carregamento em andamento';
        case 'LOADING_PLANNED':
            return 'Carregamento planejado';
        case 'CALLOFF_CREATED':
            return 'Abruf criado';
        case 'EDI_SYNCED':
            return 'Enviado para planta solicitante';
        case 'EDI_EXPORTED':
            return 'EDI exportado';
        case 'EDI_CREATED':
            return 'Documento EDI criado';
        case 'PO_REVISION':
            return 'Pedido revisado';
        case 'PO_CREATED':
            return 'Pedido criado';
        default:
            return event.eventLabelPT || event.eventCode;
    }
}

/**
 * Derive the most advanced operational stage from timeline events,
 * enriched with PO status and detail quantity data when available.
 *
 * Business rules (v2.175.0):
 * 1. If PO status is "Parcialmente entregue" → "Parcialmente recebido"
 * 2. If detail qty: orderedQty > 0, receivedQty > 0, receivedQty < orderedQty → "Parcialmente recebido"
 * 3. "Recebimento concluído" only when PO is fully complete
 *    (PO status "Concluído", or receivedQty >= orderedQty, or openQty === 0)
 * 4. GR_CREATED pending → "Aguardando recebimento"
 * 5. Fallback to timeline-based stage priority
 */
function deriveCurrentStage(
    events: OperationsTimelineEvent[],
    detailData?: OperationsTransferDetail | null,
): string {
    if (events.length === 0) return 'Sem eventos encontrados';

    // ── Step 1: Derive PO status from PO_CREATED event ──
    const poEvent = events.find(e => e.eventCode === 'PO_CREATED');
    const poStatusMeaning = poEvent?.statusMeaning?.toLowerCase() ?? '';

    // ── Step 2: Find the highest-priority timeline event ──
    let bestIndex = -1;
    let bestEvent: OperationsTimelineEvent | null = null;

    for (const event of events) {
        const idx = STAGE_PRIORITY.indexOf(event.eventCode);
        if (idx > bestIndex) {
            bestIndex = idx;
            bestEvent = event;
        }
    }

    // Get the base label from the timeline
    const baseLabel = bestEvent ? resolveStageLabel(bestEvent) : 'Sem eventos encontrados';

    // ── Step 3: Apply partial receipt business rules ──
    // Only relevant when we're at the receipt stage (GR_CREATED or GR_COMPLETED)
    const isReceiptStage = bestEvent?.eventCode === 'GR_CREATED' || bestEvent?.eventCode === 'GR_COMPLETED';

    if (isReceiptStage) {
        // Rule 1: PO status explicitly says "Parcialmente entregue"
        if (poStatusMeaning.startsWith('parcialmente')) {
            return 'Parcialmente recebido';
        }

        // Rule 2: Detail quantity shows partial receipt
        const qty = detailData?.quantity;
        if (qty) {
            const ordered = qty.orderedQuantity;
            const received = qty.receivedQuantity;
            const open = qty.openQuantity;

            if (ordered != null && ordered > 0 && received != null && received > 0 && received < ordered) {
                return 'Parcialmente recebido';
            }

            // Rule 3: Confirm full completion only when quantity data confirms it
            if (baseLabel === 'Recebimento concluído') {
                const isFullyReceived =
                    poStatusMeaning === 'concluído' ||
                    (received != null && ordered != null && received >= ordered) ||
                    (open != null && open === 0);

                if (!isFullyReceived) {
                    // Quantity data exists but doesn't confirm full completion
                    if (received != null && received > 0) {
                        return 'Parcialmente recebido';
                    }
                    return 'Aguardando recebimento';
                }
            }
        } else {
            // No detail data available — use PO status as fallback for partial
            // If PO is not completed and base label says completed, trust GR event status
        }
    }

    return baseLabel;
}

/** Derive PO status from the PO_CREATED event's statusMeaning (backend-resolved). */
function derivePOStatus(events: OperationsTimelineEvent[]): { label: string; severity: string } {
    const poEvent = events.find(e => e.eventCode === 'PO_CREATED');
    if (poEvent?.statusMeaning) {
        return { label: poEvent.statusMeaning, severity: poEvent.severity };
    }
    return { label: 'Desconhecido', severity: 'info' };
}

// ─── Summary Card (Business-oriented, v2.175.0) ───

function SummaryCard({ data, detailData }: { data: OperationsTimelineResponse; detailData?: OperationsTransferDetail | null }) {
    const pipelineBadge = PIPELINE_BADGE[data.pipelineModel] || PIPELINE_BADGE.STANDARD;
    const poStatus = derivePOStatus(data.events);
    const currentStage = deriveCurrentStage(data.events, detailData);
    const statusSev = SEVERITY_BADGE[poStatus.severity] || SEVERITY_BADGE.info;

    return (
        <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.25, delay: 0.05 }}
            style={{
                backgroundColor: 'var(--color-bg-page)',
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-lg)',
                padding: '1.25rem 1.5rem',
                display: 'flex', flexDirection: 'column', gap: '1rem',
            }}
        >
            {/* Header row */}
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '0.75rem' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                    <Package size={20} strokeWidth={2.5} style={{ color: 'var(--color-primary)' }} />
                    <span style={{ fontFamily: 'var(--font-family-display)', fontSize: '1.1rem', fontWeight: 800, color: 'var(--color-text-main)', letterSpacing: '-0.01em' }}>
                        Pedido #{data.idBestellung}
                    </span>
                    {data.journalNummer && (
                        <span style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)' }}>
                            Journal: {data.journalNummer}
                        </span>
                    )}
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    {/* Status badge */}
                    <span style={{
                        display: 'inline-flex', alignItems: 'center', gap: '4px',
                        padding: '0.25rem 0.65rem', borderRadius: 'var(--radius-full)',
                        fontSize: '0.72rem', fontWeight: 700,
                        backgroundColor: statusSev.bg, color: statusSev.color,
                        fontFamily: 'var(--font-family-body)',
                    }}>
                        {poStatus.label}
                    </span>
                    {/* Pipeline badge */}
                    <span style={{
                        padding: '0.25rem 0.65rem', borderRadius: 'var(--radius-full)',
                        fontSize: '0.72rem', fontWeight: 800, textTransform: 'uppercase',
                        letterSpacing: '0.05em', backgroundColor: pipelineBadge.bg, color: pipelineBadge.color,
                        fontFamily: 'var(--font-family-body)',
                    }}>
                        {pipelineBadge.label}
                    </span>
                </div>
            </div>

            {/* Business metadata grid */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '0.75rem' }}>
                <MetaField icon={<Activity size={14} />} label="Etapa atual" value={currentStage} />
                <MetaField icon={<Layers size={14} />} label="Planta" value={data.plant} />
                <MetaField icon={<Hash size={14} />} label="Eventos encontrados" value={String(data.events.length)} />
                <MetaField icon={<Server size={14} />} label="Servidor" value={data.plantServer} />
                <MetaField icon={<Timer size={14} />} label="Duração da consulta" value={`${data.queryDurationMs} ms`} />
                <MetaField icon={<Database size={14} />} label="Base de dados" value={data.plantDatabase} />
            </div>

            {/* Technical footnote — pipeline model steps (de-emphasized) */}
            <div style={{
                fontSize: '0.72rem', color: 'var(--color-text-muted)',
                fontFamily: 'var(--font-family-body)', opacity: 0.7,
                borderTop: '1px solid var(--color-border)', paddingTop: '0.5rem',
            }}>
                Etapas possíveis do modelo: {data.expectedEventCount}
            </div>
        </motion.div>
    );
}

function MetaField({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
    return (
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <div style={{ color: 'var(--color-text-muted)', display: 'flex' }}>{icon}</div>
            <div>
                <div style={{ fontSize: '0.65rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)', marginBottom: '0.15rem' }}>{label}</div>
                <div style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)', fontFamily: 'var(--font-family-body)' }}>{value}</div>
            </div>
        </div>
    );
}

// ─── Timeline Section (reused from Phase 3) ───

function TimelineSection({ events }: { events: OperationsTimelineEvent[] }) {
    if (events.length === 0) {
        return (
            <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)', fontSize: '0.9rem' }}>
                Nenhum evento encontrado para este pedido.
            </div>
        );
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '1rem' }}>
                <Gauge size={18} strokeWidth={2.5} style={{ color: 'var(--color-primary)' }} />
                <span style={{ fontFamily: 'var(--font-family-display)', fontSize: '1rem', fontWeight: 800, color: 'var(--color-text-main)', letterSpacing: '-0.01em' }}>
                    Timeline ({events.length} eventos)
                </span>
            </div>

            <div style={{ position: 'relative', paddingLeft: '2rem' }}>
                {/* Vertical connector line */}
                <div style={{
                    position: 'absolute', left: '11px', top: '12px', bottom: '12px',
                    width: '2px', backgroundColor: 'var(--color-border)', borderRadius: '1px',
                }} />

                {events.map((event, index) => (
                    <TimelineEventCard key={`${event.eventCode}-${event.sortOrder}-${index}`} event={event} index={index} />
                ))}
            </div>
        </div>
    );
}

function TimelineEventCard({ event, index }: { event: OperationsTimelineEvent; index: number }) {
    const sev = SEVERITY_STYLE[event.severity] || SEVERITY_STYLE.info;

    return (
        <motion.div
            initial={{ opacity: 0, x: -10 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.2, delay: index * 0.04 }}
            style={{ position: 'relative', marginBottom: '0.5rem', marginLeft: '1rem' }}
        >
            {/* Timeline dot */}
            <div style={{
                position: 'absolute', left: '-2.05rem', top: '1rem',
                width: '10px', height: '10px', borderRadius: 'var(--radius-full)',
                backgroundColor: event.isCompleted ? sev.border : 'var(--color-bg-surface)',
                border: `2px solid ${sev.border}`, zIndex: 2,
            }} />

            {/* Card */}
            <div style={{
                backgroundColor: event.isTechnical ? 'transparent' : sev.bg,
                borderLeft: `3px solid ${sev.border}`,
                borderRadius: 'var(--radius-md)',
                padding: event.isTechnical ? '0.6rem 0.85rem' : '0.85rem 1rem',
                border: event.isTechnical ? `1px dashed var(--color-border)` : undefined,
                borderLeftWidth: '3px', borderLeftStyle: 'solid', borderLeftColor: sev.border,
            }}>
                {/* Header */}
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', flexWrap: 'wrap' }}>
                    {event.isCompleted
                        ? <CheckCircle2 size={event.isTechnical ? 14 : 16} style={{ color: sev.iconColor, flexShrink: 0 }} />
                        : <Clock size={event.isTechnical ? 14 : 16} style={{ color: sev.iconColor, flexShrink: 0 }} />
                    }
                    <span style={{
                        fontFamily: 'var(--font-family-body)',
                        fontSize: event.isTechnical ? '0.8rem' : '0.88rem',
                        fontWeight: event.isCompleted ? 700 : 600,
                        color: event.isTechnical ? 'var(--color-text-muted)' : 'var(--color-text-main)',
                        flex: 1,
                    }}>
                        {event.eventLabelPT}
                    </span>

                    <div style={{ display: 'flex', gap: '0.35rem', alignItems: 'center', flexShrink: 0 }}>
                        {event.isTechnical && (
                            <span style={{
                                fontSize: '0.6rem', fontWeight: 700, textTransform: 'uppercase',
                                letterSpacing: '0.05em', padding: '0.15rem 0.45rem',
                                borderRadius: 'var(--radius-full)',
                                backgroundColor: 'rgba(107, 114, 128, 0.12)', color: '#6b7280',
                                fontFamily: 'var(--font-family-body)',
                            }}>
                                Técnico
                            </span>
                        )}
                        <span style={{
                            fontSize: '0.6rem', fontWeight: 700, textTransform: 'uppercase',
                            letterSpacing: '0.05em', padding: '0.15rem 0.45rem',
                            borderRadius: 'var(--radius-full)',
                            backgroundColor: sev.bg, color: sev.border,
                            fontFamily: 'var(--font-family-body)',
                        }}>
                            {event.severity}
                        </span>
                    </div>
                </div>

                {/* Details row */}
                <div style={{
                    display: 'flex', flexWrap: 'wrap', gap: '0.75rem', marginTop: '0.4rem',
                    fontSize: '0.78rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)',
                }}>
                    {event.eventDate && (
                        <span style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
                            <Clock size={12} /> {formatDateFull(event.eventDate)}
                        </span>
                    )}
                    {event.eventUser && (
                        <span style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
                            👤 {event.eventUser}
                        </span>
                    )}
                    {event.statusMeaning && (
                        <span style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
                            <ChevronRight size={12} /> {event.statusMeaning}
                        </span>
                    )}
                    {event.sourceTable && (
                        <span style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
                            <Database size={11} /> {event.sourceTable}
                        </span>
                    )}
                </div>

                {/* Extended info */}
                {(event.referenceNumber || event.materialName || event.quantity != null || event.notes) && (
                    <div style={{
                        display: 'flex', flexWrap: 'wrap', gap: '0.75rem', marginTop: '0.35rem',
                        fontSize: '0.76rem', color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)',
                    }}>
                        {event.referenceNumber && <span><strong>Ref:</strong> {event.referenceNumber}</span>}
                        {event.materialName && <span><strong>Material:</strong> {event.materialName}</span>}
                        {event.quantity != null && <span><strong>Qtd:</strong> {event.quantity}</span>}
                        {event.notes && <span style={{ fontStyle: 'italic' }}>📝 {event.notes}</span>}
                    </div>
                )}
            </div>
        </motion.div>
    );
}

// ─── Shared State Components ───

function ErrorState({ message }: { message: string }) {
    return (
        <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -8 }}
            style={{
                display: 'flex', alignItems: 'center', gap: '0.75rem',
                padding: '1rem 1.25rem', borderRadius: 'var(--radius-md)',
                backgroundColor: 'rgba(220, 38, 38, 0.06)',
                border: '1px solid rgba(220, 38, 38, 0.15)',
            }}
        >
            <XCircle size={20} style={{ color: '#dc2626', flexShrink: 0 }} />
            <span style={{ fontSize: '0.88rem', fontWeight: 600, color: '#dc2626', fontFamily: 'var(--font-family-body)' }}>
                {message}
            </span>
        </motion.div>
    );
}

// ─── Detail Card Helpers ───

const detailCardStyle: React.CSSProperties = {
    borderRadius: 'var(--radius-md)',
    border: '1px solid var(--color-border)',
    backgroundColor: 'var(--color-bg-surface)',
    overflow: 'hidden',
};

const detailCardHeaderStyle: React.CSSProperties = {
    display: 'flex', alignItems: 'center', gap: '0.6rem',
    padding: '0.65rem 1rem',
    backgroundColor: 'var(--color-bg-page)',
    borderBottom: '1px solid var(--color-border)',
    fontFamily: 'var(--font-family-display)',
    fontSize: '0.78rem', fontWeight: 800,
    color: 'var(--color-text-main)',
    textTransform: 'uppercase' as const,
    letterSpacing: '0.04em',
};

const detailRowStyle: React.CSSProperties = {
    display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
    padding: '0.5rem 1rem',
    fontSize: '0.82rem', fontFamily: 'var(--font-family-body)',
    borderBottom: '1px solid rgba(0,0,0,0.04)',
};

function DetailRow({ label, value, highlight, mono }: { label: string; value: React.ReactNode; highlight?: boolean; mono?: boolean }) {
    // Hide row if value is null, undefined, or empty string — but show '—' for explicit dashes
    if (value == null || value === '') return null;
    return (
        <div style={detailRowStyle}>
            <span style={{ color: 'var(--color-text-muted)', fontWeight: 500, minWidth: '40%' }}>{label}</span>
            <span style={{
                color: highlight ? 'var(--color-primary)' : 'var(--color-text-main)',
                fontWeight: highlight ? 700 : 600,
                textAlign: 'right',
                fontFamily: mono ? 'var(--font-family-mono, monospace)' : undefined,
                fontSize: mono ? '0.78rem' : undefined,
            }}>{value}</span>
        </div>
    );
}

// ─── Detail Header Card (Order Info) ───

function DetailHeaderCard({ header }: { header: OperationsTransferDetail['header'] }) {
    const sevBadge = SEVERITY_BADGE[header.severity || 'info'] || SEVERITY_BADGE.info;

    return (
        <div style={detailCardStyle}>
            <div style={detailCardHeaderStyle}>
                <Package size={14} style={{ color: 'var(--color-primary)' }} />
                Informações do Pedido
            </div>
            <div>
                <DetailRow label="Nº pedido" value={`#${header.idBestellung}`} highlight />
                <DetailRow label="Journal" value={header.journalNummer} />
                <DetailRow label="Status" value={
                    header.statusMeaning ? (
                        <span style={{
                            padding: '0.15rem 0.45rem', borderRadius: 'var(--radius-full)',
                            fontSize: '0.72rem', fontWeight: 700,
                            backgroundColor: sevBadge.bg, color: sevBadge.color,
                        }}>
                            {header.statusMeaning}
                        </span>
                    ) : null
                } />
                <DetailRow label="Criado em" value={formatDateShort(header.createdDate)} />
                <DetailRow label="Criado por" value={header.createdBy} />
                <DetailRow label="Atualizado em" value={formatDateShort(header.updatedDate)} />
                <DetailRow label="Atualizado por" value={header.updatedBy} />
                {header.notes && (
                    <DetailRow label="Observações" value={
                        <span style={{ fontStyle: 'italic', fontSize: '0.8rem' }}>
                            {header.notes}
                        </span>
                    } />
                )}
            </div>
        </div>
    );
}

// ─── Detail Material Card ───

function DetailMaterialCard({ material }: { material: OperationsTransferMaterial }) {
    const hasMaterial = material.materialName || material.articleAlias || material.color;
    if (!hasMaterial) return null;

    return (
        <div style={detailCardStyle}>
            <div style={detailCardHeaderStyle}>
                <Box size={14} style={{ color: 'var(--color-primary)' }} />
                Material / Artigo
            </div>
            <div>
                <DetailRow label="Material" value={material.materialName} highlight />
                <DetailRow label="Alias" value={material.articleAlias} />
                <DetailRow label="Cor" value={material.color} />
                <DetailRow label="Tipo artigo" value={material.articleTypeName} />
                <DetailRow label="Classificação" value={material.classification} />
                {material.idArtikelVarianten && (
                    <DetailRow label="ID Variante" value={`#${material.idArtikelVarianten}`} mono />
                )}
            </div>
        </div>
    );
}

// ─── Detail Quantity Card ───

function DetailQuantityCard({ quantity }: { quantity: OperationsTransferQuantity }) {
    const hasData = quantity.orderedQuantity != null || quantity.receivedQuantity != null || quantity.palletQuantity != null;
    if (!hasData) return null;

    const unit = quantity.quantityUnit || '';
    const fmtQty = (v: number | null) => {
        if (v == null) return null;
        return `${v.toLocaleString('pt-PT')}${unit ? ` ${unit}` : ''}`;
    };

    return (
        <div style={detailCardStyle}>
            <div style={detailCardHeaderStyle}>
                <BarChart3 size={14} style={{ color: '#7c3aed' }} />
                Quantidades
            </div>
            <div>
                <DetailRow label="Qtd. pedida" value={fmtQty(quantity.orderedQuantity)} highlight />
                <DetailRow label="Qtd. recebida" value={fmtQty(quantity.receivedQuantity)} />
                {quantity.openQuantity != null && quantity.openQuantity > 0 && (
                    <DetailRow label="Qtd. em aberto" value={
                        <span style={{ color: '#d97706', fontWeight: 700 }}>
                            {fmtQty(quantity.openQuantity)}
                        </span>
                    } />
                )}
                <DetailRow label="Qtd. embalagem (VPK)" value={quantity.palletQuantity != null ? quantity.palletQuantity.toLocaleString('pt-PT') : null} />
                <DetailRow label="Embalagem" value={quantity.packagingName} />
            </div>
        </div>
    );
}

// ─── Detail Loading Card ───

function DetailLoadingCard({ loading, pipelineModel }: { loading: OperationsTransferLoading; pipelineModel: string }) {
    const isInhouse = pipelineModel === 'INHOUSE';

    // Check if there's any loading/delivery data
    const hasStandardData = loading.idLadeAuftrag != null || loading.ladeDatum != null || loading.truckNumber;
    const hasInhouseData = loading.idInhouseLieferung != null || loading.lieferscheinDatum != null;
    const hasData = isInhouse ? hasInhouseData : hasStandardData;

    if (!hasData) return null;

    return (
        <div style={detailCardStyle}>
            <div style={detailCardHeaderStyle}>
                <Truck size={14} style={{ color: '#ea580c' }} />
                {isInhouse ? 'Entrega Interna' : 'Carregamento / Entrega'}
            </div>
            <div>
                {isInhouse ? (
                    <>
                        <DetailRow label="ID Entrega" value={loading.idInhouseLieferung ? `#${loading.idInhouseLieferung}` : null} mono />
                        <DetailRow label="Data guia" value={formatDateShort(loading.lieferscheinDatum)} />
                        <DetailRow label="Data produção" value={formatDateShort(loading.prodTag)} />
                        <DetailRow label="Journal" value={loading.inhouseJournalNummer} />
                    </>
                ) : (
                    <>
                        <DetailRow label="Data carregamento" value={formatDateShort(loading.ladeDatum)} highlight />
                        <DetailRow label="Status carregamento" value={loading.loadingStatusMeaning} />
                        <DetailRow label="Nº camião" value={loading.truckNumber} />
                        <DetailRow label="Descrição camião" value={loading.truckDescription} />
                        <DetailRow label="Nº guia de remessa" value={loading.deliveryNumber} />
                    </>
                )}
            </div>
        </div>
    );
}

// ─── Detail Receipt Card ───

function DetailReceiptCard({ receipt }: { receipt: OperationsTransferGoodsReceipt }) {
    if (receipt.receiptCount === 0 && receipt.idWareneingang == null) return null;

    const statusColor = receipt.isCompleted ? '#16a34a' : '#d97706';

    return (
        <div style={detailCardStyle}>
            <div style={detailCardHeaderStyle}>
                <ClipboardCheck size={14} style={{ color: '#16a34a' }} />
                Recebimento de Mercadoria
            </div>
            <div>
                <DetailRow label="Status" value={
                    <span style={{
                        padding: '0.15rem 0.45rem', borderRadius: 'var(--radius-full)',
                        fontSize: '0.72rem', fontWeight: 700,
                        backgroundColor: receipt.isCompleted ? 'rgba(22, 163, 74, 0.1)' : 'rgba(217, 119, 6, 0.1)',
                        color: statusColor,
                    }}>
                        {receipt.isCompleted ? 'Concluído' : (receipt.receiptStatusMeaning || 'Pendente')}
                    </span>
                } />
                <DetailRow label="Qtd. recebida" value={receipt.receivedQuantity != null ? receipt.receivedQuantity.toLocaleString('pt-PT') : null} />
                <DetailRow label="Nº recebimentos" value={receipt.receiptCount > 0 ? receipt.receiptCount.toString() : null} />
                <DetailRow label="Data recebimento" value={formatDateShort(receipt.receiptDate)} />
                <DetailRow label="Último recebimento" value={formatDateShort(receipt.lastReceiptDate)} />
            </div>
        </div>
    );
}

// ─── Detail Tech Refs Card (collapsed by default) ───

function DetailTechRefsCard({ refs }: { refs: OperationsTransferTechRefs }) {
    const [expanded, setExpanded] = useState(false);

    return (
        <div style={detailCardStyle}>
            <div
                style={{
                    ...detailCardHeaderStyle,
                    cursor: 'pointer',
                    userSelect: 'none',
                    justifyContent: 'space-between',
                }}
                onClick={() => setExpanded(p => !p)}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                    <Wrench size={14} style={{ color: 'var(--color-text-muted)' }} />
                    Referências Técnicas
                </div>
                <motion.div animate={{ rotate: expanded ? 180 : 0 }} transition={{ duration: 0.2 }}>
                    <ChevronDown size={14} style={{ color: 'var(--color-text-muted)' }} />
                </motion.div>
            </div>
            <AnimatePresence>
                {expanded && (
                    <motion.div
                        initial={{ height: 0, opacity: 0 }}
                        animate={{ height: 'auto', opacity: 1 }}
                        exit={{ height: 0, opacity: 0 }}
                        transition={{ duration: 0.2 }}
                        style={{ overflow: 'hidden' }}
                    >
                        <div style={{ fontSize: '0.76rem', fontFamily: 'var(--font-family-mono, monospace)' }}>
                            <DetailRow label="IdBestellung" value={refs.idBestellung} />
                            <DetailRow label="IdBestellPosition" value={refs.idBestellPosition} />
                            <DetailRow label="IdJournal" value={refs.idJournal} />
                            <DetailRow label="JournalNummer" value={refs.journalNummer} />
                            <DetailRow label="IdAuftragsAbruf" value={refs.idAuftragsAbruf} />
                            <DetailRow label="IdAbrufe" value={refs.idAbrufe} />
                            <DetailRow label="IdLadePlanung" value={refs.idLadePlanung} />
                            <DetailRow label="IdLadeAuftrag" value={refs.idLadeAuftrag} />
                            <DetailRow label="IdWareneingang" value={refs.idWareneingang} />
                            <DetailRow label="IdInhouseLieferung" value={refs.idInhouseLieferung} />
                            <DetailRow label="Reference" value={refs.referenceNumber} />
                        </div>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}
