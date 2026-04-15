import { useState, useEffect, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { Plus, Search, Filter, FolderKanban, Pin, PinOff, ArrowUpRight } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { api } from '../../../../lib/api';
import { RequestListItemDto, DashboardSummaryDto } from '../../../../types';
import { ActionCarouselWidget } from './ActionCarouselWidget';
import { RequestsTableWidget } from './RequestsTableWidget';
import { RequestDrawerPresentation } from './RequestDrawerPresentation';
import { FilterDropdown } from '../../../../components/ui/FilterDropdown';
import { CorrectPoModal } from '../../../../components/CorrectPoModal';
import { GuideModal, GuideModalSection } from '../../../../components/ui/GuideModal';
import { PlayCircle, Compass, MoreVertical, Info } from 'lucide-react';

export function RequestsDashboard() {
    const navigate = useNavigate();

    // Core Data
    const [requests, setRequests] = useState<RequestListItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
    const [totalCount, setTotalCount] = useState(0);

    // Feature Toggles
    const [isFloating, setIsFloating] = useState(true);

    // Navigation state (Drawer)
    const [drawerRequestId, setDrawerRequestId] = useState<string | null>(null);

    // P.O. Correction Modal state
    const [correctPoRequestId, setCorrectPoRequestId] = useState<string | null>(null);

    // Help Modal state
    const [currentHelpSection, setCurrentHelpSection] = useState<'action' | 'explorer' | 'main' | null>(null);

    // Filters and Pagination
    const [searchTerm, setSearchTerm] = useState('');
    const [debouncedSearch, setDebouncedSearch] = useState('');
    const [filterType, setFilterType] = useState<string>('all');
    const [showAdvancedFilters, setShowAdvancedFilters] = useState(false);
    const [lookups, setLookups] = useState<{ statuses: any[], requestTypes: any[], plants: any[], companies: any[], departments: any[] } | null>(null);
    const [statusIds, setStatusIds] = useState<string[]>([]);
    const [plantIds, setPlantIds] = useState<string[]>([]);
    const [companyIds, setCompanyIds] = useState<string[]>([]);
    const [departmentIds, setDepartmentIds] = useState<string[]>([]);

    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(20);
    const [sortConfig, setSortConfig] = useState<{ key: string | null; direction: 'asc' | 'desc' }>({ key: null, direction: 'asc' });

    // Debounce search
    useEffect(() => {
        const handler = setTimeout(() => {
            setDebouncedSearch(searchTerm);
            setPage(1);
        }, 500);
        return () => clearTimeout(handler);
    }, [searchTerm]);

    useEffect(() => {
        async function fetchLookups() {
            try {
                const [statuses, requestTypes, plants, companies, departments] = await Promise.all([
                    api.lookups.getRequestStatuses(false),
                    api.lookups.getRequestTypes(false),
                    api.lookups.getPlants(undefined, false),
                    api.lookups.getCompanies(false),
                    api.lookups.getDepartments(false)
                ]);
                setLookups({ statuses, requestTypes, plants, companies, departments });
            } catch (err) {
                console.error("Failed to load lookups:", err);
            }
        }
        fetchLookups();
    }, []);

    const loadData = useCallback(async () => {
        try {
            setLoading(true);
            let typeIds = undefined;
            if (filterType === 'QUOTATION') typeIds = '1';
            if (filterType === 'PAYMENT') typeIds = '2';

            const data = await api.requests.list(
                debouncedSearch || undefined,
                {
                    typeIds,
                    statusIds: statusIds.length > 0 ? statusIds.join(',') : undefined,
                    plantIds: plantIds.length > 0 ? plantIds.join(',') : undefined,
                    companyIds: companyIds.length > 0 ? companyIds.join(',') : undefined,
                    departmentIds: departmentIds.length > 0 ? departmentIds.join(',') : undefined,
                    sortBy: sortConfig.key || undefined,
                    isDescending: sortConfig.direction === 'desc'
                },
                page,
                pageSize
            );

            const pagedResult = data.pagedResult || (data as any).PagedResult;
            const summaryData = data.summary || (data as any).Summary;

            if (pagedResult) {
                setRequests(pagedResult.items || []);
                setTotalCount(pagedResult.totalCount || 0);
            } else {
                setRequests((data as any).items || []);
                setTotalCount((data as any).totalCount || 0);
            }
            if (summaryData) setSummary(summaryData);
        } catch (err) {
            console.error(err);
        } finally {
            setLoading(false);
        }
    }, [debouncedSearch, filterType, page, pageSize, statusIds, plantIds, companyIds, departmentIds, sortConfig]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleRowClick = (requestId: string) => {
        setDrawerRequestId(requestId);
    };

    const handleSort = (key: string) => {
        let direction: 'asc' | 'desc' = 'asc';
        if (sortConfig.key === key && sortConfig.direction === 'asc') direction = 'desc';
        setSortConfig({ key, direction });
        setPage(1);
    };

    const FILTER_TABS = [
        { id: 'all', label: 'Todos' },
        { id: 'QUOTATION', label: 'Cotações' },
        { id: 'PAYMENT', label: 'Pagamentos' },
    ];

    const totalFilteredValue = requests.reduce((sum, req) => sum + (req.estimatedTotalAmount || 0), 0);

    const statusGroups = useMemo(() => {
        if (!lookups || !lookups.statuses) return undefined;

        const gInicial = ["Rascunho", "Reajuste A.A", "Reajuste A.F"];
        const gAprovacao = ["Aguardando Cotação", "Aguardando Aprovação da Área", "Aguardando Aprovação Final", "Aprovado"];
        const gFinanceiro = ["P.O Emitida", "Pagamento Agendado", "Pagamento Realizado", "Aguardando Recibo"];
        const gFinalizados = ["Finalizado", "Cancelado", "Rejeitado"];

        const findOptions = (names: string[]) =>
            names.map(name => lookups.statuses.find(s => s.name.trim() === name))
                .filter(Boolean)
                .map(s => ({ id: s!.id, name: s!.name }));

        const groups = [
            { name: "INICIAL", options: findOptions(gInicial) },
            { name: "APROVAÇÃO & COTAÇÃO", options: findOptions(gAprovacao) },
            { name: "FINANCEIRO & RECEBIMENTO", options: findOptions(gFinanceiro) },
            { name: "FINALIZADOS", options: findOptions(gFinalizados) },
        ].filter(g => g.options.length > 0);

        const mappedNames = [...gInicial, ...gAprovacao, ...gFinanceiro, ...gFinalizados];
        const others = lookups.statuses
            .filter(s => !mappedNames.includes(s.name.trim()))
            .map(s => ({ id: s.id, name: s.name }));

        if (others.length > 0) {
            groups.push({ name: "OUTROS", options: others });
        }

        return groups.length > 0 ? groups : undefined;
    }, [lookups]);

    return (
        <div style={{
            minHeight: '100vh',
            padding: '24px 32px',
            maxWidth: '1400px',
            margin: '0 auto',
            display: 'flex',
            flexDirection: 'column',
            gap: '32px',
        }}>

            {/* ── Header ── */}
            <header style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                flexWrap: 'wrap',
                gap: '16px',
            }}>
                <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '4px' }}>
                        <h1 style={{
                            fontSize: '1.5rem',
                            fontWeight: 900,
                            color: 'var(--color-primary)',
                            margin: 0,
                            letterSpacing: '-0.01em',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                        }}>
                            Pedidos de Compras e Pagamentos
                            <button
                                onClick={() => setCurrentHelpSection('main')}
                                style={{
                                    background: 'none', border: 'none', cursor: 'pointer',
                                    color: '#94a3b8', display: 'flex', alignItems: 'center', justifyContent: 'center',
                                    padding: '4px', borderRadius: '50%', transition: 'all 0.2s'
                                }}
                                onMouseOver={(e) => { e.currentTarget.style.color = 'var(--color-primary)'; e.currentTarget.style.backgroundColor = '#EFF6FF'; }}
                                onMouseOut={(e) => { e.currentTarget.style.color = '#94a3b8'; e.currentTarget.style.backgroundColor = 'transparent'; }}
                                title="Ajuda sobre o Dashboard Principal"
                            >
                                <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="10"></circle><path d="M12 16v-4"></path><path d="M12 8h.01"></path></svg>
                            </button>
                        </h1>

                        {/* Toggle de Modo Flutuante */}
                        <button
                            onClick={() => setIsFloating(!isFloating)}
                            title={isFloating ? "Desativar modo flutuante" : "Ativar modo flutuante"}
                            style={{
                                padding: '4px 10px',
                                borderRadius: '6px',
                                transition: 'all 0.15s ease',
                                display: 'flex',
                                alignItems: 'center',
                                gap: '6px',
                                fontSize: '0.75rem',
                                fontWeight: 500,
                                cursor: 'pointer',
                                backgroundColor: isFloating ? '#EFF6FF' : '#ffffff',
                                color: isFloating ? '#2563eb' : '#64748b',
                                border: `1px solid ${isFloating ? '#bfdbfe' : '#e2e8f0'}`,
                            }}
                        >
                            {isFloating ? <Pin size={14} /> : <PinOff size={14} />}
                            <span>{isFloating ? 'Flutuante Ativo' : 'Flutuante Inativo'}</span>
                        </button>
                    </div>
                    <p style={{
                        color: 'var(--color-text-muted)',
                        fontSize: '0.85rem',
                        margin: 0,
                        fontWeight: 500,
                    }}>Gerencie e acompanhe todos os pedidos corporativos em tempo real.</p>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>


                    {!isFloating && (
                        <button
                            onClick={() => navigate('/requests/new')}
                            style={{
                                display: 'inline-flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                gap: '8px',
                                backgroundColor: 'var(--color-primary)',
                                border: '1px solid var(--color-primary)',
                                color: '#fff',
                                padding: '10px 18px',
                                borderRadius: 'var(--radius-md)',
                                fontWeight: 600,
                                fontSize: '0.85rem',
                                cursor: 'pointer',
                                transition: 'all 0.15s ease',
                                boxShadow: 'var(--shadow-sm)',
                            }}
                            onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-primary-hover)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-primary)'; }}
                        >
                            <Plus size={18} />
                            Novo Pedido
                        </button>
                    )}
                </div>
            </header>

            {/* ── Floating Action Button (New Request) ── */}
            <AnimatePresence>
                {isFloating && (
                    <motion.div
                        initial={{ opacity: 0, scale: 0.8 }}
                        animate={{ opacity: 1, scale: 1 }}
                        exit={{ opacity: 0, scale: 0.8 }}
                        style={{ position: 'fixed', top: '100px', right: '32px', zIndex: 40 }}
                    >
                        <button
                            onClick={() => navigate('/requests/new')}
                            style={{
                                display: 'inline-flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                gap: '8px',
                                backgroundColor: 'var(--color-primary)',
                                border: '1px solid var(--color-primary)',
                                color: '#fff',
                                padding: '12px 20px',
                                borderRadius: '50px',
                                fontWeight: 600,
                                fontSize: '0.9rem',
                                cursor: 'pointer',
                                transition: 'all 0.15s ease',
                                boxShadow: '0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05)',
                            }}
                            onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-primary-hover)'; e.currentTarget.style.transform = 'scale(0.98)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-primary)'; e.currentTarget.style.transform = 'scale(1)'; }}
                        >
                            <Plus size={20} />
                            Novo Pedido
                        </button>
                    </motion.div>
                )}
            </AnimatePresence>

            {/* ── Action Carousel & Stats ── */}
            {summary && (
                <ActionCarouselWidget
                    summary={summary}
                    onRowClick={handleRowClick}
                    onCorrectPoClick={(requestId) => setCorrectPoRequestId(requestId)}
                    onHelpClick={() => setCurrentHelpSection('action')}
                />
            )}

            {/* ── Explorer Section ── */}
            <section style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {/* Section header + filters */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', flexWrap: 'wrap', gap: '16px' }}>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        <div>
                            <h2 style={{
                                fontSize: '1.1rem',
                                fontWeight: 800,
                                color: 'var(--color-primary)',
                                margin: 0,
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                            }}>
                                Explorador de Pedidos
                                <button
                                    onClick={() => setCurrentHelpSection('explorer')}
                                    style={{
                                        background: 'none', border: 'none', cursor: 'pointer',
                                        color: '#94a3b8', display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        padding: '4px', borderRadius: '50%', transition: 'all 0.2s'
                                    }}
                                    onMouseOver={(e) => { e.currentTarget.style.color = 'var(--color-primary)'; e.currentTarget.style.backgroundColor = '#EFF6FF'; }}
                                    onMouseOut={(e) => { e.currentTarget.style.color = '#94a3b8'; e.currentTarget.style.backgroundColor = 'transparent'; }}
                                    title="Ajuda sobre esta seção"
                                >
                                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="10"></circle><path d="M12 16v-4"></path><path d="M12 8h.01"></path></svg>
                                </button>
                            </h2>
                            <p style={{
                                fontSize: '0.75rem',
                                color: 'var(--color-text-muted)',
                                margin: '4px 0 0',
                                fontWeight: 500,
                                textTransform: 'none',
                            }}>O restante do portfólio de compras visível para si.</p>
                        </div>

                        {/* Quick Filters */}
                        <div style={{
                            display: 'flex',
                            backgroundColor: 'rgba(241, 245, 249, 0.8)',
                            padding: '4px',
                            borderRadius: '8px',
                            width: 'fit-content',
                            border: '1px solid rgba(226, 232, 240, 0.5)',
                        }}>
                            {FILTER_TABS.map(tab => (
                                <button
                                    key={tab.id}
                                    onClick={() => setFilterType(tab.id)}
                                    style={{
                                        padding: '6px 16px',
                                        borderRadius: '6px',
                                        fontSize: '0.85rem',
                                        fontWeight: 600,
                                        border: 'none',
                                        cursor: 'pointer',
                                        transition: 'all 0.15s ease',
                                        backgroundColor: filterType === tab.id ? '#ffffff' : 'transparent',
                                        color: filterType === tab.id ? 'var(--color-primary)' : '#64748b',
                                        boxShadow: filterType === tab.id ? '0 1px 2px 0 rgba(0, 0, 0, 0.05)' : 'none',
                                    }}
                                    onMouseEnter={(e) => { if (filterType !== tab.id) { e.currentTarget.style.color = '#0f172a'; e.currentTarget.style.backgroundColor = 'rgba(226,232,240,0.5)' } }}
                                    onMouseLeave={(e) => { if (filterType !== tab.id) { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.backgroundColor = 'transparent' } }}
                                >
                                    {tab.label}
                                </button>
                            ))}
                        </div>
                    </div>

                    {/* Search + Filter Controls */}
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <div style={{ position: 'relative' }}>
                            <Search
                                size={16}
                                style={{
                                    position: 'absolute',
                                    left: '12px',
                                    top: '50%',
                                    transform: 'translateY(-50%)',
                                    color: '#94a3b8',
                                    pointerEvents: 'none',
                                }}
                            />
                            <input
                                type="text"
                                placeholder="Buscar por número, título ou solicitante..."
                                value={searchTerm}
                                onChange={(e) => setSearchTerm(e.target.value)}
                                style={{
                                    paddingLeft: '36px',
                                    paddingRight: '16px',
                                    paddingTop: '8px',
                                    paddingBottom: '8px',
                                    backgroundColor: '#ffffff',
                                    border: '1px solid #e2e8f0',
                                    borderRadius: '8px',
                                    fontSize: '0.85rem',
                                    width: '260px',
                                    boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)',
                                    transition: 'border-color 0.15s ease, box-shadow 0.15s ease',
                                    color: '#0f172a',
                                    outline: 'none',
                                }}
                                onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--color-primary)'; e.currentTarget.style.boxShadow = '0 0 0 2px rgba(37, 99, 235, 0.2)'; }}
                                onBlur={(e) => { e.currentTarget.style.borderColor = '#e2e8f0'; e.currentTarget.style.boxShadow = '0 1px 2px 0 rgba(0, 0, 0, 0.05)'; }}
                            />
                        </div>
                        <button
                            onClick={() => setShowAdvancedFilters(!showAdvancedFilters)}
                            style={{
                                padding: '8px',
                                backgroundColor: showAdvancedFilters ? '#f8fafc' : '#ffffff',
                                border: '1px solid #e2e8f0',
                                borderRadius: '8px',
                                color: '#475569',
                                cursor: 'pointer',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)',
                                transition: 'all 0.15s ease',
                            }}
                            onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = '#f8fafc'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = showAdvancedFilters ? '#f8fafc' : '#ffffff'; }}
                        >
                            <Filter size={18} />
                        </button>
                    </div>
                </div>

                {/* Advanced Filters Pane */}
                {showAdvancedFilters && lookups && (
                    <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap', padding: '16px', backgroundColor: 'rgba(248, 250, 252, 0.8)', borderRadius: '8px', border: '1px solid #f1f5f9' }}>
                        <FilterDropdown label="Status" groups={statusGroups} options={!statusGroups ? lookups.statuses.map(s => ({ id: s.id, name: s.name })) : undefined} selectedIds={statusIds} onChange={setStatusIds} />
                        <FilterDropdown label="Empresa" options={lookups.companies.map(c => ({ id: c.id, name: c.name }))} selectedIds={companyIds} onChange={setCompanyIds} />
                        <FilterDropdown label="Planta" options={lookups.plants.map(p => ({ id: p.id, name: p.name }))} selectedIds={plantIds} onChange={setPlantIds} />
                        <FilterDropdown label="Departamento" options={lookups.departments.map(d => ({ id: d.id, name: d.name }))} selectedIds={departmentIds} onChange={setDepartmentIds} />
                        {(statusIds.length > 0 || companyIds.length > 0 || plantIds.length > 0 || departmentIds.length > 0) && (
                            <button onClick={() => { setStatusIds([]); setCompanyIds([]); setPlantIds([]); setDepartmentIds([]); }} style={{ background: 'none', border: 'none', color: 'var(--color-primary)', cursor: 'pointer', fontWeight: 800, textTransform: 'uppercase', fontSize: '0.75rem', padding: '0 12px' }}>
                                Limpar Filtros
                            </button>
                        )}
                    </div>
                )}

                {/* Table */}
                <RequestsTableWidget
                    requests={requests}
                    loading={loading}
                    sortConfig={sortConfig}
                    onSort={handleSort}
                    onRowClick={handleRowClick}
                    page={page}
                    pageSize={pageSize}
                    totalCount={totalCount}
                    onPageChange={setPage}
                    onPageSizeChange={setPageSize}
                />

                {/* ── Total Value Footer (Inline) ── */}
                {!isFloating && (
                    <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '16px' }}>
                        <div style={{
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            padding: '16px',
                            borderRadius: '16px',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '16px',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                            <div style={{ width: '40px', height: '40px', backgroundColor: '#ECFDF5', color: 'var(--color-status-emerald)', borderRadius: '12px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                <ArrowUpRight size={20} />
                            </div>
                            <div>
                                <p style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', margin: 0 }}>Total Filtrado</p>
                                <p style={{ fontSize: '1.25rem', fontWeight: 800, color: 'var(--color-text-main)', margin: 0 }}>AOA {totalFilteredValue.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}</p>
                            </div>
                        </div>
                    </div>
                )}

                {/* ── Total Value Footer (Floating) ── */}
                <AnimatePresence>
                    {isFloating && (
                        <motion.div
                            initial={{ opacity: 0, y: 20 }}
                            animate={{ opacity: 1, y: 0 }}
                            exit={{ opacity: 0, y: 20 }}
                            style={{ position: 'fixed', bottom: '32px', right: '32px', zIndex: 90 }}
                        >
                            <div style={{
                                backgroundColor: 'rgba(255, 255, 255, 0.90)',
                                backdropFilter: 'blur(8px)',
                                border: '1px solid rgba(226, 232, 240, 0.6)',
                                padding: '16px',
                                borderRadius: '16px',
                                display: 'flex',
                                alignItems: 'center',
                                gap: '16px',
                                boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)'
                            }}>
                                <div style={{ width: '40px', height: '40px', backgroundColor: '#ECFDF5', color: 'var(--color-status-emerald)', borderRadius: '12px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                    <ArrowUpRight size={20} />
                                </div>
                                <div>
                                    <p style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', margin: 0 }}>Total Filtrado</p>
                                    <p style={{ fontSize: '1.25rem', fontWeight: 800, color: 'var(--color-text-main)', margin: 0 }}>AOA {totalFilteredValue.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}</p>
                                </div>
                            </div>
                        </motion.div>
                    )}
                </AnimatePresence>
            </section>

            {/* ── Drawer ── */}
            <RequestDrawerPresentation
                requestId={drawerRequestId}
                isOpen={!!drawerRequestId}
                onClose={() => {
                    setDrawerRequestId(null);
                    loadData();
                }}
            />

            {/* ── P.O. Correction Modal ── */}
            <CorrectPoModal
                show={!!correctPoRequestId}
                requestId={correctPoRequestId || ''}
                onClose={() => setCorrectPoRequestId(null)}
                onSuccess={(msg) => {
                    setCorrectPoRequestId(null);
                    loadData();
                }}
            />

            {/* ── Contextual Help Modals ── */}
            <GuideModal
                isOpen={currentHelpSection === 'main'}
                onClose={() => setCurrentHelpSection(null)}
                title="Pedidos Corporativos"
                subtitle="Visão geral do seu painel principal"
            >
                <GuideModalSection
                    icon={<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 3v18h18" /><path d="m19 9-5 5-4-4-3 3" /></svg>}
                    iconBgColor="#EFF6FF"
                    iconColor="var(--color-primary)"
                    title="Métricas de Desempenho (Cards)"
                >
                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                        No topo, você encontra indicadores instantâneos que refletem o volume global sob a sua visualização:
                    </p>
                    <ul style={{ margin: '0 0 16px 20px', fontSize: '14px', color: '#334155', lineHeight: 1.6 }}>
                        <li><strong>Total de Pedidos:</strong> Todos os pedidos abertos/relevantes atuais.</li>
                        <li><strong>Em Cotação:</strong> Demandas no setor de Compras aguardando valores e fornecedores.</li>
                        <li><strong>Pend. Aprovação:</strong> Pedidos travados com superiores aguardando parecer final.</li>
                        <li><strong>Rascunhos:</strong> Pedidos iniciados mas ainda não enviados formalmente.</li>
                        <li><strong>Pend. Finanças:</strong> Entregues, porém aguardando compensação/pagamento.</li>
                    </ul>
                </GuideModalSection>

                <GuideModalSection
                    icon={<Pin size={24} />}
                    iconBgColor="#F3F4F6"
                    iconColor="#4B5563"
                    title="Modo Flutuante"
                >
                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                        <strong>Como funciona:</strong> Ao invés de um botão gigantesco no topo consumindo espaço, o portal oferece o modo <strong>Flutuante</strong> para criar novos pedidos.
                    </p>
                    <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                        Quando o <em>Flutuante Ativo</em> está marcado, um botão ágil ficará preso no canto inferior direito da tela. Ao desligá-lo, o botão clássico <em>"Novo Pedido"</em> voltará a aparecer permanentemente preso ao cabeçalho da página. Escolha o estilo que preferir.
                    </p>
                </GuideModalSection>
            </GuideModal>

            <GuideModal
                isOpen={currentHelpSection === 'action'}
                onClose={() => setCurrentHelpSection(null)}
                title="Para Minha Ação"
                subtitle="Guia rápido sobre a fila de ações pendentes."
            >
                <GuideModalSection
                    icon={<PlayCircle size={24} />}
                    iconBgColor="#FFF1F2"
                    iconColor="var(--color-status-rose)"
                    title="Foco Operacional"
                >
                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                        <strong>O que é:</strong> Esta seção exibe os pedidos que foram explicitamente atribuídos a você ou que exigem uma ação imediata da sua parte (ex: aprovar, cotar, validar).
                    </p>
                    <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                        <em>Nota:</em> Este carrossel <strong>não</strong> mostra todos os pedidos do sistema. É um funil afunilado projetado para reduzir o ruído visual e destacar apenas o que precisa da sua intervenção neste exato momento.
                    </p>
                </GuideModalSection>

                <GuideModalSection
                    icon={<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M15 3h6v6"></path><path d="M10 14 21 3"></path><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path></svg>}
                    iconBgColor="#EFF6FF"
                    iconColor="var(--color-primary)"
                    title="Atalhos pelo Status"
                >
                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                        <strong>Como agir rápido:</strong> O status do pedido (a etiqueta colorida no canto inferior esquerdo do cartão) é mais que um indicador visual — é um <strong style={{ color: 'var(--color-primary)' }}>botão clicável de ação direta</strong>.
                    </p>
                    <ul style={{ margin: '0 0 16px 20px', fontSize: '14px', color: '#334155', lineHeight: 1.6 }}>
                        <li>Cotações Pendentes? Clique no status e vá direto para a tela de cotações com o pedido selecionado.</li>
                        <li>Falta Receber Carga? Clique e caia na tela de recebimento.</li>
                        <li>Falta Pagamento? Clique e transporte-se para a visualização financeira.</li>
                    </ul>
                    <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                        <em>Atenção:</em> O acesso a essas telas de destino operacionais depende do seu nível de permissão e escopo de usuário no sistema.
                    </p>
                </GuideModalSection>
            </GuideModal>

            <GuideModal
                isOpen={currentHelpSection === 'explorer'}
                onClose={() => setCurrentHelpSection(null)}
                title="Explorador de Pedidos"
                subtitle="Guia sobre a visualização estendida do portfólio."
            >
                <GuideModalSection
                    icon={<Compass size={24} />}
                    iconBgColor="#EFF6FF"
                    iconColor="var(--color-primary)"
                    title="Consulta e Filtros"
                >
                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                        <strong>O que é:</strong> O Explorador exibe o espectro completo das solicitações de compras visíveis ao seu nível hierárquico e departamento.
                    </p>
                    <p style={{ margin: 0, fontSize: '14px', backgroundColor: '#f1f5f9', padding: '8px', borderLeft: '4px solid #94a3b8' }}>
                        <em>Tipos de Pedido:</em> Acima da lista, os botões <strong>"Pagamento"</strong> e <strong>"Cotação"</strong> filtram a natureza da requisição. <br />
                        • <strong>Pagamento:</strong> Um pedido de pagamento direto, que pula a cotação e vai direto para a Aprovação de Área.<br />
                        • <strong>Cotação:</strong> Um pedido de compras padrão, que flui primeiro para o time de compras fazer a cotação no mercado antes da área aprovar.
                    </p>
                    <p style={{ margin: '8px 0 0 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                        <strong>Barra de Pesquisa:</strong> Busca resultados com base no número do pedido (ex: <span style={{ fontFamily: 'monospace', backgroundColor: '#e2e8f0', padding: '2px 4px', borderRadius: '4px' }}>REQ-13/04/2026-001</span>), no título ou no <strong>nome do solicitante</strong>.
                    </p>
                    <p style={{ margin: '8px 0 0 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                        <strong>Filtros Avançados:</strong> Ao lado da barra de pesquisa, você encontrará um ícone de botão com controles. Ao clicar nele, opções adicionais de filtro (Status, Empresa, Planta, Departamento) serão exibidas para ajudá-lo a refinar exatamente o que procura.
                    </p>
                </GuideModalSection>

                <GuideModalSection
                    icon={<Info size={24} />}
                    iconBgColor="#FFFBEB"
                    iconColor="var(--color-status-amber)"
                    title="Detalhes Adicionais (Hover)"
                >
                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                        <strong>Descubra mais sem clicar:</strong> Em vários campos da tabela (como nomes de aprovadores, descrições longas ou status complexos), experimente <strong>manter a seta do mouse parada</strong> sobre o texto.
                    </p>
                    <p style={{ margin: 0, fontSize: '14px', color: '#475569' }}>
                        Um pequeno balão de ajuda (tooltip) aparecerá revelando informações estendidas e cruciais sem precisar abrir a tela completa do pedido!
                    </p>
                </GuideModalSection>

                <GuideModalSection
                    icon={<MoreVertical size={24} />}
                    iconBgColor="#F3F4F6"
                    iconColor="#4B5563"
                    title="Menu de Ações (Kebab)"
                >
                    <p style={{ margin: '8px 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                        <strong>Oculto para limpeza visual:</strong> O ícone de três pontinhos verticais no canto direito de cada pedido é o seu centro de comando individual.
                    </p>
                    <ul style={{ margin: '0 0 16px 20px', fontSize: '14px', color: '#334155', lineHeight: 1.6 }}>
                        <li><strong>Vis. Rápida:</strong> Abre uma gaveta lateral rápida com o extrato do pedido em vez de abrir uma nova tela.</li>
                        <li><strong>Duplicar:</strong> Cria um novo rascunho de pedido contendo os mesmos itens e configurações deste pedido original.</li>
                    </ul>
                </GuideModalSection>
            </GuideModal>
        </div>
    );
}
