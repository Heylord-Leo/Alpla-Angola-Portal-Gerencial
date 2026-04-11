import React, { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { Search, FileText, X, Eye, Copy, DollarSign, User, Clock, AlertTriangle } from 'lucide-react';
import { api } from '../../../lib/api';
import { formatCurrencyAO, getRequestGuidance, getUrgencyStyle, formatDate } from '../../../lib/utils';
import { RequestListItemDto, DashboardSummaryDto } from '../../../types';
import {  } from '../../../components/ApprovalModal';
import { FilterDropdown, FilterGroup } from '../../../components/ui/FilterDropdown';
import { KebabMenu } from '../../../components/ui/KebabMenu';
import { Tooltip } from '../../../components/ui/Tooltip';
import { KPISummary } from '../../../components/common/dashboard/KPISummary';
import { RequestTimelineInline } from '../components/RequestTimelineInline';
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

export interface RequestsGridProps {
    title: string;
    subtitle?: string;
    emptyMessage: string;
    baseFilters?: any;
    lookups: { statuses: any[], requestTypes: any[], plants: any[], companies: any[], departments: any[] };
    showKPIs?: boolean;
    headerSlot?: React.ReactNode;
}

export function RequestsGrid({ title, subtitle, emptyMessage, baseFilters, lookups, showKPIs, headerSlot }: RequestsGridProps) {
    // Data State
    const [requests, setRequests] = useState<RequestListItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [errorMsg, setErrorMsg] = useState<string | null>(null);
    const [totalCount, setTotalCount] = useState(0);
    const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);

    // Lookups
    const { statuses, requestTypes, plants, companies, departments } = lookups;

    // Filters State
    const [searchInput, setSearchInput] = useState('');
    const [searchTerm, setSearchTerm] = useState('');
    const [statusIds, setStatusIds] = useState<string[]>([]);
    const [requestTypeIds, setRequestTypeIds] = useState<string[]>([]);
    const [plantIds, setPlantIds] = useState<string[]>([]);
    const [companyIds, setCompanyIds] = useState<string[]>([]);
    const [departmentIds, setDepartmentIds] = useState<string[]>([]);
    const [isAttention, setIsAttention] = useState<boolean | null>(null);
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(20);

    // UI State
    const navigate = useNavigate();
    const [expandedRequestId, setExpandedRequestId] = useState<string | null>(null);
    const toggleRow = (requestId: string) => {
        setExpandedRequestId(prev => prev === requestId ? null : requestId);
    };

    useEffect(() => {
        const handler = setTimeout(() => {
            setSearchTerm(searchInput);
            setPage(1);
        }, 500);
        return () => clearTimeout(handler);
    }, [searchInput]);

    const loadData = useCallback(async () => {
        try {
            setLoading(true);
            setErrorMsg(null);
            const data = await api.requests.list(
                searchTerm || undefined, 
                { 
                    statusIds: statusIds.length > 0 ? statusIds.join(',') : undefined, 
                    typeIds: requestTypeIds.length > 0 ? requestTypeIds.join(',') : undefined, 
                    plantIds: plantIds.length > 0 ? plantIds.join(',') : undefined, 
                    companyIds: companyIds.length > 0 ? companyIds.join(',') : undefined,
                    departmentIds: departmentIds.length > 0 ? departmentIds.join(',') : undefined,
                    isAttention: isAttention === true ? true : undefined,
                    ...baseFilters
                }, 
                page, 
                pageSize
            );
            
            const pagedResult = data.pagedResult || (data as any).PagedResult;
            const summaryData = data.summary || (data as any).Summary;

            if (pagedResult) {
                setRequests(pagedResult.items || (pagedResult as any).Items || []);
                setTotalCount(pagedResult.totalCount || (pagedResult as any).TotalCount || 0);
            } else {
                setRequests((data as any).items || []);
                setTotalCount((data as any).totalCount || 0);
            }

            if (summaryData) setSummary(summaryData);
        } catch (err: any) {
            setErrorMsg(err.message || 'Erro desconhecido');
        } finally {
            setLoading(false);
        }
    }, [searchTerm, statusIds, requestTypeIds, plantIds, companyIds, departmentIds, isAttention, page, pageSize, baseFilters]);

    useEffect(() => {
        loadData();
    }, [loadData]);


    const handleFilterChange = (setter: React.Dispatch<React.SetStateAction<string[]>>, newIds: string[]) => {
        setter(newIds);
        setPage(1);
    };

    const clearAllFilters = () => {
        setSearchInput('');
        setSearchTerm('');
        setStatusIds([]);
        setRequestTypeIds([]);
        setPlantIds([]);
        setCompanyIds([]);
        setDepartmentIds([]);
        setIsAttention(null);
        setPage(1);
    };

    const handleQuickChipSelect = (activeCodes: string[]) => {
        if (activeCodes.length === 0) {
            setStatusIds([]);
            setIsAttention(null);
        } else {
            const mappedIds = statuses.filter(s => activeCodes.includes(s.code)).map(s => String(s.id));
            setStatusIds(mappedIds);
            setIsAttention(null);
        }
        setPage(1);
    };

    const handleKPIFilterChange = (label: string | null) => {
        if (label === null || label === 'Todos' || label === 'Total de Pedidos') {
            handleQuickChipSelect([]);
            return;
        }
        const chip = QUICK_CHIPS.find(c => c.label === label);
        if (chip) handleQuickChipSelect(chip.activeCodes);
    };

    const statusGroups: FilterGroup[] = useMemo(() => {
        if (!statuses) return [];
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
        if (statusIds.length === 0) return 'Todos';
        const selectedCodes = statuses.filter(s => statusIds.includes(String(s.id))).map(s => s.code);
        if (selectedCodes.length === 0) return null;
        for (const chip of QUICK_CHIPS) {
            if (chip.activeCodes.length > 0 && selectedCodes.every(c => chip.activeCodes.includes(c))) return chip.label;
        }
        return null;
    };

    const activeQuickChip = determineActiveQuickChip();
    const activeFilterChips = useMemo(() => {
        const chips: { key: string, id: string, label: string, type: string }[] = [];
        
        statusIds.forEach(id => { const item = statuses.find(s => String(s.id) === id); if (item) chips.push({ key: `status-${id}`, id, label: `Status: ${item.name}`, type: 'status' }); });
        requestTypeIds.forEach(id => { const item = requestTypes.find(t => String(t.id) === id); if (item) chips.push({ key: `type-${id}`, id, label: `Tipo: ${item.name}`, type: 'type' }); });
        plantIds.forEach(id => { const item = plants.find(p => String(p.id) === id); if (item) chips.push({ key: `plant-${id}`, id, label: `Planta: ${item.name}`, type: 'plant' }); });
        companyIds.forEach(id => { const item = companies.find(c => String(c.id) === id); if (item) chips.push({ key: `company-${id}`, id, label: `Empresa: ${item.name}`, type: 'company' }); });
        departmentIds.forEach(id => { const item = departments.find(d => String(d.id) === id); if (item) chips.push({ key: `dept-${id}`, id, label: `Depto: ${item.name}`, type: 'department' }); });
        
        if (isAttention) chips.push({ key: 'attention', id: 'attention', label: 'Filtro: Em Atenção', type: 'status' });

        return chips;
    }, [statusIds, requestTypeIds, plantIds, departmentIds, companyIds, isAttention, statuses, requestTypes, plants, departments, companies]);

    const hasFilters = searchInput || statusIds.length > 0 || requestTypeIds.length > 0 || plantIds.length > 0 || departmentIds.length > 0 || companyIds.length > 0;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px', width: '100%', minWidth: 0 }}>
            {/* Header / Title */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', paddingBottom: '8px', borderBottom: '2px solid var(--color-border)', marginBottom: '8px' }}>
                <div>
                    <h2 style={{ margin: 0, fontSize: '1.5rem', color: 'var(--color-primary)' }}>{title}</h2>
                    {subtitle && <p style={{ margin: '4px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, fontSize: '0.85rem', textTransform: 'uppercase' }}>{subtitle}</p>}
                </div>
                {headerSlot}
            </div>

            {showKPIs && summary && (
                <KPISummary 
                    summary={summary} 
                    activeFilter={activeQuickChip}
                    onFilterChange={handleKPIFilterChange} 
                />
            )}

            {/* Filters Box */}
            <div style={{ backgroundColor: 'var(--color-bg-surface)', padding: '20px', borderRadius: 'var(--radius-lg)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', backgroundColor: '#fcfcfc', padding: '0 16px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)' }}>
                    <Search size={18} color="var(--color-primary)" style={{ opacity: 0.6 }} />
                    <input
                        type="text"
                        placeholder="Buscar por número, título ou fornecedor..."
                        value={searchInput}
                        onChange={(e) => setSearchInput(e.target.value)}
                        style={{ border: 'none', outline: 'none', width: '100%', fontSize: '0.9rem', padding: '12px 0', backgroundColor: 'transparent', fontWeight: 600, color: 'var(--color-text-main)' }}
                    />
                    {searchInput && (
                        <button onClick={() => setSearchInput('')} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={16} /></button>
                    )}
                </div>

                <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
                    <FilterDropdown label="Tipo de Pedido" options={requestTypes.map(t => ({ id: t.id, name: t.name }))} selectedIds={requestTypeIds} onChange={(ids) => handleFilterChange(setRequestTypeIds, ids)} />
                    <FilterDropdown label="Empresa" options={companies.map(c => ({ id: c.id, name: c.name }))} selectedIds={companyIds} onChange={(ids) => handleFilterChange(setCompanyIds, ids)} />
                    <FilterDropdown label="Planta" options={plants.map(p => ({ id: p.id, name: p.name }))} selectedIds={plantIds} onChange={(ids) => handleFilterChange(setPlantIds, ids)} />
                    <FilterDropdown label="Departamento" options={departments.map(d => ({ id: d.id, name: d.name }))} selectedIds={departmentIds} onChange={(ids) => handleFilterChange(setDepartmentIds, ids)} />
                    <FilterDropdown label="Status" groups={statusGroups} selectedIds={statusIds} onChange={(ids) => handleFilterChange(setStatusIds, ids)} />
                    
                    {hasFilters && (
                        <button onClick={clearAllFilters} style={{ background: 'none', border: 'none', color: 'var(--color-text-muted)', cursor: 'pointer', fontWeight: 700, textTransform: 'uppercase', fontSize: '0.85rem' }}>
                            Limpar
                        </button>
                    )}
                </div>

                {activeFilterChips.length > 0 && (
                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', paddingTop: '8px' }}>
                        {activeFilterChips.map((chip: any) => (
                            <span key={chip.key} style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '4px 8px', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-primary)', borderRadius: 'var(--radius-md)', color: 'var(--color-primary)', fontWeight: 600, fontSize: '0.75rem', textTransform: 'uppercase' }}>
                                {chip.label}
                                <button onClick={() => {
                                    if (chip.id === 'attention') { setIsAttention(null); return; }
                                    const setter = chip.type === 'status' ? setStatusIds : chip.type === 'type' ? setRequestTypeIds : chip.type === 'plant' ? setPlantIds : chip.type === 'company' ? setCompanyIds : setDepartmentIds;
                                    const curr = chip.type === 'status' ? statusIds : chip.type === 'type' ? requestTypeIds : chip.type === 'plant' ? plantIds : chip.type === 'company' ? companyIds : departmentIds;
                                    handleFilterChange(setter, curr.filter(id => id !== chip.id));
                                }} style={{ background: 'none', border: 'none', cursor: 'pointer', padding: 0, display: 'flex' }}><X size={14} color="var(--color-primary)" strokeWidth={2.5} /></button>
                            </span>
                        ))}
                    </div>
                )}
            </div>

            {/* Table Area */}
            <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', boxShadow: 'var(--shadow-sm)', overflow: 'hidden' }}>
                {loading ? (
                    <div style={{ padding: '40px', textAlign: 'center', color: 'var(--color-primary)', fontWeight: 700, fontSize: '1rem', textTransform: 'uppercase', letterSpacing: '0.1em' }}>Carregando...</div>
                ) : errorMsg && requests.length === 0 ? (
                    <div style={{ padding: '30px', textAlign: 'center', backgroundColor: 'var(--color-status-rejected)', color: '#fff', fontWeight: 700, textTransform: 'uppercase' }}>ERRO: {errorMsg}</div>
                ) : requests.length === 0 ? (
                    <div style={{ padding: '60px 20px', textAlign: 'center', color: 'var(--color-text-muted)' }}>
                        <FileText size={48} strokeWidth={1.5} style={{ opacity: 0.2, margin: '0 auto 16px', color: 'var(--color-primary)' }} />
                        <p style={{ fontWeight: 700, fontSize: '1rem', textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-main)' }}>{emptyMessage}</p>
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
                                <th style={{ textAlign: 'right' }}>Valor</th>
                            </tr>
                        </thead>
                        <tbody>
                            {requests.map(req => {
                                const guidance = getRequestGuidance(req.statusCode, req.requestTypeCode);
                                const urgency = getUrgencyStyle(req.needByDateUtc, req.statusCode);
                                return (
                                    <React.Fragment key={req.id}>
                                        <tr className="hoverable-row" style={{ cursor: 'pointer', borderLeft: expandedRequestId === req.id ? '6px solid var(--color-primary)' : 'none' }} onClick={() => toggleRow(req.id)}>
                                            <td onClick={e => e.stopPropagation()} style={{ textAlign: 'center' }}>
                                                <KebabMenu options={[
                                                    { label: 'Visualizar', icon: <Eye size={16} />, onClick: () => navigate(`/requests/${req.id}`) },
                                                    { label: 'Copiar', icon: <Copy size={16} />, onClick: () => navigate(`/requests/new?copyFrom=${req.id}`) }
                                                ]} />
                                            </td>
                                            <td style={{ fontWeight: 900, color: 'var(--color-primary)' }} onClick={(e) => { e.stopPropagation(); navigate(`/requests/${req.id}`); }}>
                                                <Tooltip content={<div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}><User size={14} color="var(--color-primary)" /><span style={{ fontWeight: 700, fontSize: '0.85rem' }}>{req.requesterName}</span></div>}>
                                                    <span style={{ cursor: 'pointer', borderBottom: '1px solid var(--color-primary)' }}>{req.requestNumber}</span>
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
                                                    <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>{req.companyName || 'Não Definido'}</span>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                                                        <span>{req.departmentName || '---'}</span><span style={{ opacity: 0.5 }}>•</span><span style={{ fontWeight: 700, color: 'var(--color-primary)' }}>{req.plantName || '---'}</span>
                                                    </div>
                                                </div>
                                            </td>
                                            <td>
                                                <Tooltip content={urgency ? urgency.description : 'Data limite'}>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                        {urgency ? (urgency.priority === 3 ? <AlertTriangle size={16} color={urgency.indicatorColor} /> : <Clock size={16} color={urgency.indicatorColor} />) : <div style={{ width: '16px' }} />}
                                                        <span style={{ fontWeight: 800, color: urgency ? urgency.indicatorColor : 'var(--color-text-main)', fontSize: '0.85rem' }}>{formatDate(req.needByDateUtc)}</span>
                                                    </div>
                                                </Tooltip>
                                            </td>
                                            <td>
                                                <Tooltip content={
                                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                                        <div><div style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Situação Atual</div><div style={{ fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-primary)' }}>{guidance.responsible}</div></div>
                                                        <div style={{ borderTop: '1px solid var(--color-border)', paddingTop: '8px' }}><div style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)' }}>Próxima Ação</div><div style={{ fontSize: '0.8rem', fontWeight: 600, fontStyle: 'italic', color: 'var(--color-text-main)' }}>{guidance.nextAction}</div></div>
                                                    </div>
                                                }>
                                                    <span className={`badge badge-sm badge-${req.statusBadgeColor === 'red' ? 'danger' : req.statusBadgeColor === 'yellow' ? 'warning' : req.statusBadgeColor === 'green' ? 'success' : req.statusBadgeColor || 'neutral'}`}>{req.statusName}</span>
                                                </Tooltip>
                                            </td>
                                            <td style={{ textAlign: 'right', fontWeight: 900, color: 'var(--color-text-main)', fontSize: '0.95rem' }}>
                                                <span style={{ fontSize: '0.7rem', fontWeight: 700, color: 'var(--color-text-muted)', marginRight: '4px' }}>{req.currencyCode}</span>{formatCurrencyAO(req.estimatedTotalAmount)}
                                            </td>
                                        </tr>
                                        {expandedRequestId === req.id && (
                                            <tr key={`${req.id}-details`}>
                                                <td colSpan={8} style={{ padding: '0', backgroundColor: 'var(--color-bg-page)' }}>
                                                    <div style={{ padding: '24px 32px', borderTop: '1px solid var(--color-border)', backgroundColor: 'rgba(var(--color-primary-rgb), 0.02)' }}><RequestTimelineInline requestId={req.id} /></div>
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

            {/* Pagination Controls */}
            {!loading && !errorMsg && requests.length > 0 && (
                <div style={{ marginTop: '16px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '12px 16px', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', boxShadow: 'var(--shadow-sm)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <span style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.85rem', textTransform: 'uppercase' }}>Pág:</span>
                        <select value={pageSize} onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1); }} style={{ padding: '4px 8px', border: '2px solid var(--color-primary)', backgroundColor: 'var(--color-bg-page)', outline: 'none', fontWeight: 700, color: 'var(--color-primary)', cursor: 'pointer' }}>
                            <option value={5}>5</option>
                            <option value={10}>10</option>
                            <option value={20}>20</option>
                            <option value={50}>50</option>
                        </select>
                    </div>

                    <div style={{ fontWeight: 700, color: 'var(--color-primary)', fontSize: '0.85rem' }}>MOSTRANDO {((page - 1) * pageSize) + 1} - {Math.min(page * pageSize, totalCount)} DE {totalCount}</div>

                    <div style={{ display: 'flex', gap: '8px' }}>
                        <button disabled={page === 1} onClick={() => setPage(Math.max(1, page - 1))} style={{ padding: '6px 16px', backgroundColor: page === 1 ? 'var(--color-bg-page)' : 'var(--color-bg-surface)', color: page === 1 ? 'var(--color-text-muted)' : 'var(--color-primary)', border: '2px solid', borderColor: page === 1 ? 'var(--color-border)' : 'var(--color-primary)', fontWeight: 800, cursor: page === 1 ? 'not-allowed' : 'pointer', fontSize: '0.8rem', textTransform: 'uppercase' }}>Ant</button>
                        <button disabled={page * pageSize >= totalCount} onClick={() => setPage(page + 1)} style={{ padding: '6px 16px', backgroundColor: page * pageSize >= totalCount ? 'var(--color-bg-page)' : 'var(--color-primary)', color: page * pageSize >= totalCount ? 'var(--color-text-muted)' : '#FFF', border: '2px solid', borderColor: page * pageSize >= totalCount ? 'var(--color-border)' : 'var(--color-primary)', fontWeight: 800, cursor: page * pageSize >= totalCount ? 'not-allowed' : 'pointer', fontSize: '0.8rem', textTransform: 'uppercase' }}>Próx</button>
                    </div>
                </div>
            )}
        </div>
    );
}
