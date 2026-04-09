import React, { useEffect, useState, useMemo } from 'react';
import { Link, useLocation, useSearchParams, useNavigate } from 'react-router-dom';
import { Plus, Search, FileText, X, Eye, Copy, DollarSign, User, Clock, AlertTriangle } from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { formatCurrencyAO, getRequestGuidance, getUrgencyStyle, formatDate } from '../../lib/utils';
import { RequestListItemDto, DashboardSummaryDto } from '../../types';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { FilterDropdown, FilterGroup } from '../../components/ui/FilterDropdown';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { Tooltip } from '../../components/ui/Tooltip';
import { KPISummary } from '../../components/common/dashboard/KPISummary';
import { RequestTimelineInline } from './components/RequestTimelineInline';

// Quick Chip Definitions
const QUICK_CHIPS = [
    { label: 'Todos', activeCodes: [] },
    { label: 'Em Cotação', activeCodes: ['WAITING_QUOTATION'] },
    { label: 'Em Aprovação', activeCodes: ['WAITING_AREA_APPROVAL', 'WAITING_FINAL_APPROVAL'] },
    { label: 'Aguardando P.O', activeCodes: ['APPROVED'] },
    { label: 'Aguardando Pagamento', activeCodes: ['PO_ISSUED', 'PAYMENT_REQUEST_SENT', 'PAYMENT_SCHEDULED'] },
    { label: 'Recebimento', activeCodes: ['WAITING_RECEIPT', 'PARTIALLY_RECEIVED'] },
    { label: 'Finalizados', activeCodes: ['COMPLETED', 'QUOTATION_COMPLETED'] },
    { label: 'Cancelados', activeCodes: ['CANCELLED', 'REJECTED'] }
];



export function RequestsList() {
    const [searchParams, setSearchParams] = useSearchParams();

    // Data State
    const [requests, setRequests] = useState<RequestListItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [totalCount, setTotalCount] = useState(0);
    const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);

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
    const [approvalProcessing] = useState(false);
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
            const newState = { ...locationState };
            delete newState.successMessage;
            
            navigate({ pathname: location.pathname, search: location.search }, { replace: true, state: newState });
        }
    }, [locationState, navigate, location.pathname, location.search]);


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
                
                // Defensive check to handle potential casing differences from backend (System.Text.Json default vs custom)
                const pagedResult = data.pagedResult || (data as any).PagedResult;
                const summaryData = data.summary || (data as any).Summary;

                if (pagedResult) {
                    setRequests(pagedResult.items || (pagedResult as any).Items || []);
                    setTotalCount(pagedResult.totalCount || (pagedResult as any).TotalCount || 0);
                } else {
                    // Fallback for legacy structure if any
                    setRequests((data as any).items || []);
                    setTotalCount((data as any).totalCount || 0);
                }

                if (summaryData) {
                    setSummary(summaryData);
                }
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
            updateParams({ statusIds: null, isAttention: null });
        } else {
            const mappedIds = statuses
                .filter(s => activeCodes.includes(s.code))
                .map(s => String(s.id));
            updateParams({ statusIds: mappedIds.join(','), isAttention: null });
        }
    };

    const handleKPIFilterChange = (label: string | null) => {
        if (label === null || label === 'Todos' || label === 'Total de Pedidos') {
            handleQuickChipSelect([]);
            return;
        }

        // The labels from KPISummary now match QUICK_CHIPS labels directly for high-level filters
        const chipLabel = label; 
        const chip = QUICK_CHIPS.find(c => c.label === chipLabel);
        if (chip) {
            handleQuickChipSelect(chip.activeCodes);
        }
    };

    // Filter Mappings
    const statusGroups: FilterGroup[] = useMemo(() => {
        return [
            { name: 'Inicial', codes: ['DRAFT', 'AREA_ADJUSTMENT', 'FINAL_ADJUSTMENT'] },
            { name: 'Aprovação & Cotação', codes: ['WAITING_QUOTATION', 'WAITING_AREA_APPROVAL', 'WAITING_FINAL_APPROVAL', 'APPROVED'] },
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
        
        // Get codes for the currently selected IDs
        const selectedCodes = statuses.filter(s => statusIds.includes(String(s.id))).map(s => s.code);
        if (selectedCodes.length === 0) return null;
        
        for (const chip of QUICK_CHIPS) {
            // Match if ALL selected codes belong to this chip's group
            // This handles cases where only some statuses of a group are present/selected
            if (chip.activeCodes.length > 0 && selectedCodes.every(c => chip.activeCodes.includes(c))) {
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
            setShowApprovalModal({ show: false, type: null, requestId: null });
            navigate(`/requests/new?copyFrom=${showApprovalModal.requestId}`);
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
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '1px solid var(--color-border)', paddingBottom: '16px', width: '100%', minWidth: 0 }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: '2.5rem', color: 'var(--color-primary)' }}>Pedidos de Compras e Pagamentos</h1>
                    <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase' }}>Gerencie e acompanhe todos os pedidos corporativos.</p>
                </div>
                <Link to="/requests/new" className="btn-primary" style={{ display: 'flex', alignItems: 'center', gap: '12px', textDecoration: 'none' }}>
                    <Plus size={20} strokeWidth={3} />
                    Novo Pedido
                </Link>
            </div>

            {summary && (
                <KPISummary 
                    summary={summary} 
                    activeFilter={activeQuickChip}
                    onFilterChange={handleKPIFilterChange} 
                />
            )}

            {/* Filter Hub */}
            <div style={{ 
                backgroundColor: 'var(--color-bg-surface)', 
                padding: '24px', 
                borderRadius: 'var(--radius-lg)', 
                border: '1px solid var(--color-border)',
                boxShadow: 'var(--shadow-sm)',
                display: 'flex',
                flexDirection: 'column',
                gap: '20px'
            }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <h2 style={{ 
                        margin: 0, 
                        fontSize: '1rem', 
                        fontWeight: 900, 
                        textTransform: 'uppercase', 
                        letterSpacing: '0.05em',
                        color: 'var(--color-primary)'
                    }}>
                        Filtros e Busca
                    </h2>
                    <div style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>
                        {totalCount} PEDIDOS ENCONTRADOS
                    </div>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', backgroundColor: '#fcfcfc', padding: '0 16px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)', transition: 'all 0.2s' }}>
                    <Search size={20} color="var(--color-primary)" strokeWidth={2.5} style={{ opacity: 0.6 }} />
                    <input
                        type="text"
                        placeholder="Buscar por número, título ou fornecedor..."
                        value={searchInput}
                        onChange={(e) => setSearchInput(e.target.value)}
                        style={{ border: 'none', outline: 'none', width: '100%', fontSize: '0.9rem', padding: '16px 0', backgroundColor: 'transparent', fontWeight: 600, color: 'var(--color-text-main)' }}
                    />
                    {searchInput && (
                        <button onClick={() => setSearchInput('')} style={{ background: 'none', border: 'none', cursor: 'pointer' }}>
                            <X size={18} />
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
                    {hasFilters && (
                        <button onClick={clearAllFilters} style={{ 
                            background: 'none', border: 'none', color: 'var(--color-text-muted)', cursor: 'pointer', 
                            fontWeight: 700, textTransform: 'uppercase', fontSize: '0.85rem', whiteSpace: 'nowrap'
                        }}>
                            Limpar Filtros
                        </button>
                    )}
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
                                        border: `1px solid ${isActive ? 'var(--color-primary)' : 'var(--color-border)'}`,
                                        borderRadius: 'var(--radius-md)',
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
                        {activeFilterChips.map((chip: any) => (
                            <span key={chip.key} style={{
                                display: 'inline-flex', alignItems: 'center', gap: '6px',
                                padding: '4px 8px', backgroundColor: 'var(--color-bg-surface)',
                                border: '1px solid var(--color-primary)', borderRadius: 'var(--radius-md)', color: 'var(--color-primary)',
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

                <div style={{ 
                        backgroundColor: 'var(--color-bg-surface)', 
                        border: '1px solid var(--color-border)', 
                        borderRadius: 'var(--radius-lg)', 
                        boxShadow: 'var(--shadow-sm)',
                        overflow: 'hidden' 
                    }}>
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
                    <table style={{ minWidth: '1200px', margin: 0, borderCollapse: 'collapse' }}>
                        <thead>
                            <tr>
                                <th style={{ width: '80px', textAlign: 'center' }}>Ações</th>
                                <th style={{ width: '180px' }}>Número</th>
                                <th>Título do Pedido</th>
                                <th>Tipo</th>
                                <th>Empresa/Planta</th>
                                <th style={{ width: '150px' }}>Data Limite</th>
                                <th>Status</th>
                                <th style={{ textAlign: 'right' }}>Valor Estimado</th>
                            </tr>
                        </thead>
                        <tbody>
                            {requests.map(req => {
                                const guidance = getRequestGuidance(req.statusCode, req.requestTypeCode);
                                const urgency = getUrgencyStyle(req.needByDateUtc, req.statusCode);

                                return (
                                    <React.Fragment key={req.id}>
                                        <tr
                                            key={req.id}
                                            className="hoverable-row"
                                            style={{ 
                                                cursor: 'pointer',
                                                borderLeft: expandedRequestId === req.id ? '6px solid var(--color-primary)' : 'none'
                                            }}
                                            onClick={() => toggleRow(req.id)}
                                        >
                                            <td style={{ textAlign: 'center' }}>
                                                 <KebabMenu
                                                    options={[
                                                        {
                                                            label: 'Visualizar',
                                                            icon: <Eye size={16} />,
                                                            onClick: () => navigate(`/requests/${req.id}`)
                                                        },
                                                        {
                                                            label: 'Copiar',
                                                            icon: <Copy size={16} />,
                                                            onClick: () => setShowApprovalModal({ show: true, type: 'DUPLICATE_REQUEST', requestId: req.id })
                                                        }
                                                    ]}
                                                />
                                            </td>
                                            <td 
                                                style={{ fontWeight: 900, color: 'var(--color-primary)', letterSpacing: '0.02em' }}
                                                onClick={(e) => { e.stopPropagation(); navigate(`/requests/${req.id}`); }}
                                            >
                                                <Tooltip content={
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                        <User size={14} color="var(--color-primary)" />
                                                        <span style={{ fontWeight: 700, fontSize: '0.85rem' }}>{req.requesterName}</span>
                                                    </div>
                                                }>
                                                    <span style={{ cursor: 'pointer', borderBottom: '1px solid var(--color-primary)' }}>
                                                        {req.requestNumber}
                                                    </span>
                                                </Tooltip>
                                            </td>
                                            <td>{req.title}</td>
                                            <td style={{ textAlign: 'center' }}>
                                                <Tooltip content={<span style={{ fontWeight: 700, textTransform: 'uppercase', fontSize: '0.75rem' }}>{req.requestTypeName}</span>}>
                                                    {req.requestTypeCode === 'PAYMENT' ? <DollarSign size={20} color="#10b981" /> : <FileText size={20} color="var(--color-primary)" />}
                                                </Tooltip>
                                            </td>
                                            <td>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                    <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>
                                                        {req.companyName || 'Não Definido'}
                                                    </span>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                                                        <span>{req.departmentName || '---'}</span>
                                                        <span style={{ opacity: 0.5 }}>•</span>
                                                        <span style={{ fontWeight: 700, color: 'var(--color-primary)' }}>{req.plantName || '---'}</span>
                                                    </div>
                                                </div>
                                            </td>
                                            <td>
                                                <Tooltip content={urgency ? urgency.description : 'Data de necessidade solicitada'}>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                        {urgency ? (
                                                            urgency.priority === 3 ? (
                                                                <AlertTriangle size={16} color={urgency.indicatorColor} />
                                                            ) : (
                                                                <Clock size={16} color={urgency.indicatorColor} />
                                                            )
                                                        ) : (
                                                            <div style={{ width: '16px' }} />
                                                        )}
                                                        <span style={{ 
                                                            fontWeight: 800, 
                                                            color: urgency ? urgency.indicatorColor : 'var(--color-text-main)',
                                                            fontSize: '0.85rem'
                                                        }}>
                                                            {formatDate(req.needByDateUtc)}
                                                        </span>
                                                    </div>
                                                </Tooltip>
                                            </td>
                                            <td>
                                                <Tooltip content={
                                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                                        <div>
                                                            <div style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Situação Atual</div>
                                                            <div style={{ fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-primary)', textTransform: 'uppercase' }}>{guidance.responsible}</div>
                                                        </div>
                                                        <div style={{ borderTop: '1px solid var(--color-border)', paddingTop: '8px' }}>
                                                            <div style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Próxima Ação</div>
                                                            <div style={{ fontSize: '0.8rem', fontWeight: 600, fontStyle: 'italic', color: 'var(--color-text-main)' }}>{guidance.nextAction}</div>
                                                        </div>
                                                    </div>
                                                }>
                                                    <span className={`badge badge-sm badge-${
                                                        req.statusBadgeColor === 'red' ? 'danger' :
                                                        req.statusBadgeColor === 'yellow' ? 'warning' :
                                                        req.statusBadgeColor === 'green' ? 'success' :
                                                        req.statusBadgeColor || 'neutral'
                                                    }`} title={`Código: ${req.statusCode}`}>
                                                        {req.statusName}
                                                    </span>
                                                </Tooltip>
                                            </td>
                                            <td style={{ textAlign: 'right', fontWeight: 900, color: 'var(--color-text-main)', fontSize: '0.95rem' }}>
                                                <span style={{ fontSize: '0.7rem', fontWeight: 700, color: 'var(--color-text-muted)', marginRight: '4px' }}>
                                                    {req.currencyCode}
                                                </span>
                                                {formatCurrencyAO(req.estimatedTotalAmount)}
                                            </td>
                                        </tr>
                                        {expandedRequestId === req.id && (
                                            <tr key={`${req.id}-details`}>
                                                <td colSpan={8} style={{ padding: '0', backgroundColor: 'var(--color-bg-page)' }}>
                                                    <div style={{ padding: '24px 32px', borderTop: '1px solid var(--color-border)', backgroundColor: 'rgba(var(--color-primary-rgb), 0.02)' }}>
                                                        <RequestTimelineInline requestId={req.id} />
                                                    </div>
                                                </td>
                                            </tr>
                                        )}
                                    </React.Fragment>
                                );
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
                        border: '1px solid var(--color-border)',
                        borderRadius: 'var(--radius-lg)',
                        width: '100%',
                        minWidth: 0,
                        boxShadow: 'var(--shadow-sm)'
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
