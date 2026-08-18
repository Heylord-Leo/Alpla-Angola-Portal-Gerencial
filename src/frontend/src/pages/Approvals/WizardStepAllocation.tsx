import React, { useState } from 'react';
import { RequestDetailsDto } from '../../types';
import { AlertCircle, CheckCircle2, Copy, Plus, Trash2, ChevronDown, ChevronUp, Search, Filter, X } from 'lucide-react';
import { useBudgetPreview } from './hooks/useBudgetPreview';
import { useWizardValidation } from './hooks/useWizardValidation';

export interface ItemAssignment {
    plantId: number | null;
    costCenterId: number | null;
}

export interface ItemAllocation {
    id: string; // frontend-only unique ID for key tracking
    plantId: number | null;
    costCenterId: number | null;
    percentage: number;
}

interface WizardStepAllocationProps {
    request: RequestDetailsDto;
    itemAllocations: Record<string, ItemAllocation[]>;
    onChangeAllocations: (itemId: string, newAllocations: ItemAllocation[]) => void;
    onBulkFill: (sourceItemId: string) => void;
    plants: { id: number, name: string }[];
    costCenters: { id: number, code: string, name: string, plantId?: number }[];
    assignedCount: number;
    totalCount: number;
    itemAwards?: Record<string, string>;
    itemAssignments?: Record<string, ItemAssignment>;
    extraItems?: any[];
}

export const WizardStepAllocation: React.FC<WizardStepAllocationProps> = ({
    request,
    itemAllocations,
    onChangeAllocations,
    onBulkFill,
    plants,
    costCenters,
    assignedCount,
    totalCount,
    itemAwards = {},
    itemAssignments = {},
    extraItems = []
}) => {
    const [collapsedItems, setCollapsedItems] = useState<Record<string, boolean>>({});
    const [bulkApplyFeedback, setBulkApplyFeedback] = useState<string | null>(null);

    const [searchQuery, setSearchQuery] = useState('');
    const [filterPending, setFilterPending] = useState(false);
    const [filterAlerts, setFilterAlerts] = useState(false);

    const { getBudgetStatusForLine } = useBudgetPreview(
        request.id,
        itemAwards,
        itemAssignments,
        itemAllocations,
        assignedCount,
        totalCount
    );
    const baseItems = request.lineItems?.filter((i: any) => !i.isDeleted) || [];
    const activeItems = [...baseItems, ...extraItems];
    const allAssigned = assignedCount === totalCount && totalCount > 0;

    // A bound allocation value must never silently render as "Selecione..." — when a bound
    // plant/cost-center id is absent from the company-scoped lookup lists (failed fetch, or a
    // value outside the request-company scope), the selects below inject a render-only fallback
    // option and this flag raises a visible warning instead of a console-only error.
    const hasMissingLookupValues = activeItems.some((item: any) =>
        (itemAllocations[item.id] || []).some(a =>
            (a.plantId != null && !plants.some(p => p.id === a.plantId)) ||
            (a.costCenterId != null && !costCenters.some(cc => cc.id === a.costCenterId))));

    const { isAssigned } = useWizardValidation(
        activeItems,
        itemAllocations,
        itemAwards,
        '',
        false,
        false
    );

    const canBulkFill = (itemId: string): boolean => {
        const sourceAllocations = itemAllocations[itemId];
        if (!sourceAllocations || sourceAllocations.length === 0) return false;
        const totalPercentage = sourceAllocations.reduce((sum, a) => sum + (a.percentage || 0), 0);
        const allFilled = sourceAllocations.every(a => a.plantId && a.costCenterId && a.percentage > 0);
        
        if (totalPercentage !== 100 || !allFilled) return false;
        
        return activeItems.some(item => item.id !== itemId && !isAssigned(item.id));
    };

    const handleBulkFillClick = (itemId: string) => {
        const unassignedCount = activeItems.filter(i => i.id !== itemId && !isAssigned(i.id)).length;
        if (unassignedCount > 0) {
            onBulkFill(itemId);
            setBulkApplyFeedback(`Alocação aplicada a ${unassignedCount} itens pendentes.`);
            setTimeout(() => setBulkApplyFeedback(null), 4000);
        } else {
            setBulkApplyFeedback("Nenhum item pendente para aplicar.");
            setTimeout(() => setBulkApplyFeedback(null), 4000);
        }
    };

    const handleAddAllocation = (itemId: string) => {
        const current = itemAllocations[itemId] || [];
        
        // Handle special 50/50 split for the very first split
        if (current.length === 1 && current[0].percentage === 100) {
            const newAllocations = [
                { ...current[0], percentage: 50 },
                {
                    id: Math.random().toString(36).substring(7),
                    plantId: null,
                    costCenterId: null,
                    percentage: 50
                }
            ];
            onChangeAllocations(itemId, newAllocations);
            return;
        }

        const totalPercent = current.reduce((sum, a) => sum + (a.percentage || 0), 0);
        const remaining = Math.max(0, 100 - totalPercent);
        
        const newAllocations = [...current, {
            id: Math.random().toString(36).substring(7),
            plantId: null,
            costCenterId: null,
            percentage: current.length === 0 ? 100 : remaining
        }];
        onChangeAllocations(itemId, newAllocations);
    };

    const handleRemoveAllocation = (itemId: string, allocationId: string) => {
        const current = itemAllocations[itemId] || [];
        const newAllocations = current.filter(a => a.id !== allocationId);
        
        // If one remains, auto-fill it to 100%
        if (newAllocations.length === 1) {
            newAllocations[0].percentage = 100;
        }
        onChangeAllocations(itemId, newAllocations);
    };

    const handleChangeAllocation = (itemId: string, allocationId: string, field: keyof ItemAllocation, value: any) => {
        const current = itemAllocations[itemId] || [];
        const newAllocations = current.map(a => {
            if (a.id === allocationId) {
                if (field === 'plantId' && value !== a.plantId) {
                    return { ...a, plantId: value, costCenterId: null };
                }
                return { ...a, [field]: value };
            }
            return a;
        });
        onChangeAllocations(itemId, newAllocations);
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
            {/* Contextual Guidance */}
            <div style={{
                backgroundColor: '#EFF6FF', border: '1px solid #BFDBFE',
                borderRadius: '6px', padding: '12px 16px', color: '#1E3A8A',
                fontSize: '0.875rem', display: 'flex', gap: '10px', alignItems: 'flex-start'
            }}>
                <span style={{ fontSize: '1.25rem', lineHeight: 1 }}>💡</span>
                <div>
                    Defina para cada item a Planta, o Centro de Custo e o percentual de alocação. Cada item deve totalizar exatamente 100%.
                </div>
            </div>

            {hasMissingLookupValues && (
                <div style={{
                    backgroundColor: '#FFFBEB', border: '1px solid #FCD34D',
                    borderRadius: '8px', padding: '12px 16px', color: '#92400E',
                    fontSize: '0.8125rem', display: 'flex', gap: '10px', alignItems: 'flex-start'
                }}>
                    <AlertCircle size={18} style={{ flexShrink: 0, marginTop: '1px' }} />
                    <div>
                        <strong>Atenção:</strong> uma ou mais atribuições possuem Planta/Centro de Custo que não constam
                        na lista da empresa deste pedido (lista indisponível ou valor fora do escopo da empresa).
                        Os valores atribuídos permanecem válidos e são exibidos abaixo — nada foi descartado.
                    </div>
                </div>
            )}

            {/* Header */}
            <div>
                <h3 style={{ fontSize: '1.125rem', fontWeight: 700, color: '#1F2937', margin: 0 }}>Atribuição Financeira dos Itens</h3>
                <p style={{ color: '#6B7280', fontSize: '0.8125rem', marginTop: '4px', margin: 0 }}>
                    A soma das alocações de cada item deve ser 100%. Use 'Dividir Custo' quando o mesmo item precisar ser distribuído entre mais de uma Planta ou Centro de Custo.
                </p>
            </div>

            {/* Pending Issues Summary */}
            <div style={{
                display: 'flex', flexDirection: 'column', gap: '12px',
                padding: '16px', backgroundColor: '#F8FAFC',
                border: '1px solid #E2E8F0', borderRadius: '8px'
            }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ fontSize: '0.875rem', fontWeight: 600, color: '#334155' }}>
                        Resumo de Atribuições
                    </span>
                    <span style={{ fontSize: '0.8125rem', color: '#64748B' }}>
                        {activeItems.length} itens no total
                    </span>
                </div>
                
                <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                    {(() => {
                        const fullyAllocatedCount = activeItems.filter(i => isAssigned(i.id)).length;
                        const incompleteCount = activeItems.length - fullyAllocatedCount;
                        const warningCount = activeItems.filter(i => {
                            const allocations = itemAllocations[i.id] || [];
                            return allocations.some(alloc => {
                                const status = getBudgetStatusForLine(alloc.plantId, alloc.costCenterId);
                                return status && status.annualBudget > 0 && (status.alreadyConsumed / status.annualBudget >= 0.9);
                            });
                        }).length;

                        const scrollToFirst = (condition: 'incomplete' | 'warning') => {
                            const target = activeItems.find(i => {
                                if (condition === 'incomplete') return !isAssigned(i.id);
                                if (condition === 'warning') {
                                    const allocations = itemAllocations[i.id] || [];
                                    return allocations.some(alloc => {
                                        const status = getBudgetStatusForLine(alloc.plantId, alloc.costCenterId);
                                        return status && status.annualBudget > 0 && (status.alreadyConsumed / status.annualBudget >= 0.9);
                                    });
                                }
                                return false;
                            });
                            if (target) {
                                document.getElementById(`allocation-item-${target.id}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                                setCollapsedItems(prev => ({ ...prev, [target.id]: false }));
                            }
                        };

                        return (
                            <>
                                <div style={{ padding: '4px 10px', borderRadius: '999px', backgroundColor: '#ECFDF5', color: '#065F46', fontSize: '0.75rem', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '4px' }}>
                                    <CheckCircle2 size={14} /> {fullyAllocatedCount} Atribuídos
                                </div>
                                {incompleteCount > 0 && (
                                    <button 
                                        onClick={() => scrollToFirst('incomplete')}
                                        style={{ padding: '4px 10px', borderRadius: '999px', backgroundColor: '#FEF2F2', color: '#991B1B', border: '1px solid #FECACA', cursor: 'pointer', fontSize: '0.75rem', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '4px', transition: 'all 0.15s' }}
                                        onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#FEE2E2'; }}
                                        onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#FEF2F2'; }}
                                    >
                                        <AlertCircle size={14} /> {incompleteCount} Incompletos
                                    </button>
                                )}
                                {warningCount > 0 && (
                                    <button 
                                        onClick={() => scrollToFirst('warning')}
                                        style={{ padding: '4px 10px', borderRadius: '999px', backgroundColor: '#FFFBEB', color: '#92400E', border: '1px solid #FDE68A', cursor: 'pointer', fontSize: '0.75rem', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '4px', transition: 'all 0.15s' }}
                                        onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#FEF3C7'; }}
                                        onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#FFFBEB'; }}
                                    >
                                        <AlertCircle size={14} /> {warningCount} {warningCount === 1 ? 'com alerta' : 'com alertas'}
                                    </button>
                                )}
                            </>
                        );
                    })()}
                </div>
            </div>

            {/* List Controls */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                {/* Filter and Search Bar */}
                <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap', alignItems: 'center', backgroundColor: '#F8FAFC', padding: '12px', borderRadius: '8px', border: '1px solid #E2E8F0' }}>
                    <div style={{ position: 'relative', flexGrow: 1, minWidth: '200px' }}>
                        <div style={{ position: 'absolute', top: '50%', left: '12px', transform: 'translateY(-50%)', color: '#94A3B8' }}>
                            <Search size={16} />
                        </div>
                        <input
                            type="text"
                            placeholder="Pesquisar por item ou descrição..."
                            value={searchQuery}
                            onChange={(e) => setSearchQuery(e.target.value)}
                            style={{
                                width: '100%',
                                padding: '8px 32px 8px 36px',
                                border: '1px solid #CBD5E1',
                                borderRadius: '6px',
                                fontSize: '0.875rem',
                                outline: 'none'
                            }}
                        />
                        {searchQuery && (
                            <button
                                onClick={() => setSearchQuery('')}
                                style={{ position: 'absolute', top: '50%', right: '12px', transform: 'translateY(-50%)', background: 'none', border: 'none', color: '#94A3B8', cursor: 'pointer', padding: 0 }}
                            >
                                <X size={16} />
                            </button>
                        )}
                    </div>
                    <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                        <div style={{ color: '#64748B', display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.875rem', fontWeight: 500, marginRight: '4px' }}>
                            <Filter size={16} /> Filtros:
                        </div>
                        <button
                            onClick={() => setFilterPending(!filterPending)}
                            style={{
                                padding: '6px 12px',
                                fontSize: '0.8125rem',
                                fontWeight: 500,
                                borderRadius: '999px',
                                cursor: 'pointer',
                                border: `1px solid ${filterPending ? '#F59E0B' : '#E2E8F0'}`,
                                backgroundColor: filterPending ? '#FEF3C7' : '#FFFFFF',
                                color: filterPending ? '#92400E' : '#475569',
                                transition: 'all 0.2s'
                            }}
                        >
                            Apenas pendentes
                        </button>
                        <button
                            onClick={() => setFilterAlerts(!filterAlerts)}
                            style={{
                                padding: '6px 12px',
                                fontSize: '0.8125rem',
                                fontWeight: 500,
                                borderRadius: '999px',
                                cursor: 'pointer',
                                border: `1px solid ${filterAlerts ? '#EF4444' : '#E2E8F0'}`,
                                backgroundColor: filterAlerts ? '#FEE2E2' : '#FFFFFF',
                                color: filterAlerts ? '#991B1B' : '#475569',
                                transition: 'all 0.2s'
                            }}
                        >
                            Apenas com alertas
                        </button>
                        {(searchQuery || filterPending || filterAlerts) && (
                            <button
                                onClick={() => {
                                    setSearchQuery('');
                                    setFilterPending(false);
                                    setFilterAlerts(false);
                                }}
                                style={{ background: 'none', border: 'none', color: '#2563EB', fontSize: '0.75rem', fontWeight: 600, cursor: 'pointer', marginLeft: '4px' }}
                            >
                                Limpar filtros
                            </button>
                        )}
                    </div>
                </div>

                {/* Expand/Collapse and Bulk Apply Feedback */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div style={{ display: 'flex', gap: '12px' }}>
                        <button
                            onClick={() => {
                                const newCollapsed: Record<string, boolean> = {};
                                activeItems.forEach(i => newCollapsed[i.id] = false);
                                setCollapsedItems(newCollapsed);
                            }}
                            style={{ background: 'none', border: 'none', color: '#2563EB', fontSize: '0.75rem', fontWeight: 600, cursor: 'pointer', padding: 0 }}
                        >
                            Expandir todos
                        </button>
                        <button
                            onClick={() => {
                                const newCollapsed: Record<string, boolean> = {};
                                activeItems.forEach(i => newCollapsed[i.id] = true);
                                setCollapsedItems(newCollapsed);
                            }}
                            style={{ background: 'none', border: 'none', color: '#64748B', fontSize: '0.75rem', fontWeight: 600, cursor: 'pointer', padding: 0 }}
                        >
                            Recolher todos
                        </button>
                    </div>
                    {bulkApplyFeedback && (
                        <div style={{ fontSize: '0.75rem', fontWeight: 600, color: '#059669', backgroundColor: '#D1FAE5', padding: '4px 10px', borderRadius: '4px', animation: 'fadeInOut 4s forwards' }}>
                            {bulkApplyFeedback}
                        </div>
                    )}
                </div>
            </div>

            {/* List */}
            {(() => {
                const filteredItems = activeItems.filter(item => {
                    if (searchQuery.trim()) {
                        const query = searchQuery.toLowerCase();
                        const descMatch = item.description?.toLowerCase().includes(query);
                        const numMatch = String(item.lineNumber || '').includes(query);
                        if (!descMatch && !numMatch) return false;
                    }
                    if (filterPending && isAssigned(item.id)) return false;
                    if (filterAlerts) {
                        const allocations = itemAllocations[item.id] || [];
                        const hasWarning = allocations.some(alloc => {
                            const status = getBudgetStatusForLine(alloc.plantId, alloc.costCenterId);
                            return status && status.annualBudget > 0 && (status.alreadyConsumed / status.annualBudget >= 0.9);
                        });
                        if (!hasWarning) return false;
                    }
                    return true;
                });

                if (filteredItems.length === 0) {
                    return (
                        <div style={{ padding: '32px', textAlign: 'center', backgroundColor: '#F8FAFC', borderRadius: '8px', border: '1px dashed #CBD5E1', color: '#64748B' }}>
                            <Filter size={32} style={{ marginBottom: '12px', opacity: 0.5 }} />
                            <p style={{ margin: 0, fontWeight: 500 }}>Nenhum item corresponde aos filtros selecionados.</p>
                            <button
                                onClick={() => {
                                    setSearchQuery('');
                                    setFilterPending(false);
                                    setFilterAlerts(false);
                                }}
                                style={{ marginTop: '12px', padding: '6px 12px', backgroundColor: 'transparent', border: '1px solid #CBD5E1', borderRadius: '6px', cursor: 'pointer', color: '#475569', fontSize: '0.875rem' }}
                            >
                                Limpar todos os filtros
                            </button>
                        </div>
                    );
                }

                return (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                        {filteredItems.map((item: any) => {
                    const assigned = isAssigned(item.id);
                    const allocations = itemAllocations[item.id] || [];
                    const showBulkFill = canBulkFill(item.id);
                    const totalPercentage = allocations.reduce((sum, a) => sum + (a.percentage || 0), 0);
                    const hasError = allocations.length > 0 && totalPercentage !== 100;

                    const hasWarning = allocations.some(alloc => {
                        const status = getBudgetStatusForLine(alloc.plantId, alloc.costCenterId);
                        return status && status.annualBudget > 0 && (status.alreadyConsumed / status.annualBudget >= 0.9);
                    });

                    // Determine collapse state
                    let isCollapsed = collapsedItems[item.id];
                    if (isCollapsed === undefined) {
                        isCollapsed = assigned && !hasWarning;
                    }

                    return (
                        <div id={`allocation-item-${item.id}`} key={item.id} style={{
                            border: `1px solid ${assigned ? '#D1D5DB' : '#FECACA'}`,
                            borderRadius: '8px', overflow: 'hidden',
                            backgroundColor: '#FFFFFF',
                            boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)'
                        }}>
                            {/* Item Header */}
                            <div 
                                onClick={() => setCollapsedItems(prev => ({ ...prev, [item.id]: !isCollapsed }))}
                                aria-expanded={!isCollapsed}
                                style={{
                                    padding: '12px 16px',
                                    backgroundColor: assigned ? '#F9FAFB' : '#FEF2F2',
                                    borderBottom: isCollapsed ? 'none' : '1px solid #E5E7EB',
                                    display: 'flex',
                                    justifyContent: 'space-between',
                                    alignItems: 'center',
                                    cursor: 'pointer'
                                }}
                            >
                                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                    {assigned ? (
                                        <CheckCircle2 size={20} color="#10B981" />
                                    ) : (
                                        <AlertCircle size={20} color="#EF4444" />
                                    )}
                                    <div>
                                        <div style={{ fontSize: '0.75rem', fontWeight: 700, color: '#6B7280' }}>#{item.lineNumber}</div>
                                        <div style={{ fontSize: '0.875rem', color: '#111827', fontWeight: 600 }}>
                                            {item.description}
                                        </div>
                                    </div>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                                    <div style={{ fontSize: '0.8125rem', color: '#6B7280', fontWeight: 500 }}>
                                        <span style={{ marginRight: '4px' }}>Qtd:</span> 
                                        <strong style={{ color: '#374151' }}>{item.quantity} {item.unit || 'UN'}</strong>
                                    </div>
                                    
                                    {isCollapsed && allocations.length > 0 && assigned && (
                                        <div style={{ fontSize: '0.75rem', color: '#64748B', backgroundColor: '#F1F5F9', padding: '2px 8px', borderRadius: '4px' }}>
                                            {allocations.length === 1 ? '1 linha de alocação' : `${allocations.length} linhas de alocação`}
                                        </div>
                                    )}

                                    {showBulkFill && !isCollapsed && (
                                        <button
                                            onClick={(e) => { e.stopPropagation(); handleBulkFillClick(item.id); }}
                                            title="Aplicar esta alocação aos itens pendentes"
                                            style={{
                                                background: '#FFFFFF', border: '1px solid #D1D5DB',
                                                borderRadius: '6px', padding: '6px 10px',
                                                cursor: 'pointer', color: '#374151',
                                                display: 'flex', alignItems: 'center', gap: '6px',
                                                fontSize: '0.75rem', fontWeight: 600,
                                                transition: 'all 0.15s'
                                            }}
                                            onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#F3F4F6'; }}
                                            onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#FFFFFF'; }}
                                        >
                                            <Copy size={14} /> Aplicar aos pendentes
                                        </button>
                                    )}

                                    <div style={{ color: '#9CA3AF' }}>
                                        {isCollapsed ? <ChevronDown size={20} /> : <ChevronUp size={20} />}
                                    </div>
                                </div>
                            </div>
                            
                            {/* Allocations Lines */}
                            {!isCollapsed && (
                            <div style={{ padding: '16px' }}>
                                {allocations.length === 0 ? (
                                    <button 
                                        onClick={() => handleAddAllocation(item.id)}
                                        style={{
                                            padding: '8px 16px', background: 'none',
                                            border: '1px dashed #9CA3AF', borderRadius: '6px',
                                            color: '#4B5563', cursor: 'pointer',
                                            fontSize: '0.8125rem', fontWeight: 600,
                                            display: 'flex', alignItems: 'center', gap: '8px'
                                        }}
                                    >
                                        <Plus size={16} /> Adicionar Atribuição
                                    </button>
                                ) : (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                        {allocations.map((alloc) => {
                                            const budgetStatus = getBudgetStatusForLine(alloc.plantId, alloc.costCenterId);
                                            const isBudgetWarning = budgetStatus && budgetStatus.annualBudget > 0 && (budgetStatus.alreadyConsumed / budgetStatus.annualBudget >= 0.9);
                                            // Step 2 asserts ALLOCATION COMPLETENESS only — the budget
                                            // verdict (incl. "Sem Orçamento Configurado") belongs to Step 4.
                                            const allocationLineComplete = !!(alloc.plantId && alloc.costCenterId && (alloc.percentage || 0) > 0);

                                            // Render-only fallbacks: a bound id absent from the company-scoped
                                            // lookup list stays visible/auditable and can never silently read
                                            // as "Selecione...". Disabled = it is not newly selectable.
                                            const plantOptionMissing = alloc.plantId != null && !plants.some(p => p.id === alloc.plantId);
                                            const plantFallbackLabel = (request as any).plantId === alloc.plantId && (request as any).plantName
                                                ? `${(request as any).plantName} (fora da lista da empresa)`
                                                : `Planta #${alloc.plantId} (fora da lista da empresa)`;
                                            const ccVisibleOptions = costCenters.filter(cc => cc.plantId === alloc.plantId);
                                            const ccOptionMissing = alloc.costCenterId != null && !ccVisibleOptions.some(cc => cc.id === alloc.costCenterId);
                                            const ccFallbackLabel = (item as any).costCenterId === alloc.costCenterId && ((item as any).costCenterCode || (item as any).costCenterName)
                                                ? `${(item as any).costCenterCode ? `[${(item as any).costCenterCode}] ` : ''}${(item as any).costCenterName || ''} (fora da lista da empresa)`.trim()
                                                : `Centro de custo #${alloc.costCenterId} (fora da lista da empresa)`;

                                            return (
                                                <div key={alloc.id} style={{ display: 'flex', flexDirection: 'column', gap: '8px', borderBottom: allocations.length > 1 ? '1px solid #F3F4F6' : 'none', paddingBottom: allocations.length > 1 ? '12px' : '0' }}>
                                                    <div style={{ display: 'flex', gap: '12px', alignItems: 'center' }}>
                                                        <div style={{ flex: '1' }}>
                                                            <label style={{ display: 'block', fontSize: '0.6875rem', fontWeight: 700, color: '#6B7280', marginBottom: '4px' }}>Planta</label>
                                                            <select
                                                                value={alloc.plantId || ''}
                                                                onChange={(e) => handleChangeAllocation(item.id, alloc.id, 'plantId', e.target.value ? parseInt(e.target.value) : null)}
                                                                style={{
                                                                    width: '100%', padding: '8px 10px',
                                                                    border: `1px solid ${alloc.plantId ? '#D1D5DB' : '#FCA5A5'}`,
                                                                    borderRadius: '6px', fontSize: '0.8125rem',
                                                                    backgroundColor: alloc.plantId ? '#FFFFFF' : '#FEF2F2',
                                                                    cursor: 'pointer', outline: 'none'
                                                                }}
                                                            >
                                                                <option value="">Selecione...</option>
                                                                {plantOptionMissing && (
                                                                    <option value={alloc.plantId!} disabled>{plantFallbackLabel}</option>
                                                                )}
                                                                {plants.map(p => (
                                                                    <option key={p.id} value={p.id}>{p.name}</option>
                                                                ))}
                                                            </select>
                                                        </div>
                                                        <div style={{ flex: '1.5' }}>
                                                            <label style={{ display: 'block', fontSize: '0.6875rem', fontWeight: 700, color: '#6B7280', marginBottom: '4px' }}>Centro de Custo</label>
                                                            <select
                                                                value={alloc.costCenterId || ''}
                                                                onChange={(e) => handleChangeAllocation(item.id, alloc.id, 'costCenterId', e.target.value ? parseInt(e.target.value) : null)}
                                                                disabled={!alloc.plantId}
                                                                style={{
                                                                    width: '100%', padding: '8px 10px',
                                                                    border: `1px solid ${!alloc.plantId ? '#E5E7EB' : alloc.costCenterId ? '#D1D5DB' : '#FCA5A5'}`,
                                                                    borderRadius: '6px', fontSize: '0.8125rem',
                                                                    backgroundColor: !alloc.plantId ? '#F3F4F6' : alloc.costCenterId ? '#FFFFFF' : '#FEF2F2',
                                                                    cursor: !alloc.plantId ? 'not-allowed' : 'pointer', outline: 'none'
                                                                }}
                                                            >
                                                                <option value="">{!alloc.plantId ? 'Selecione a Planta primeiro' : 'Selecione...'}</option>
                                                                {ccOptionMissing && (
                                                                    <option value={alloc.costCenterId!} disabled>{ccFallbackLabel}</option>
                                                                )}
                                                                {ccVisibleOptions.map(cc => (
                                                                    <option key={cc.id} value={cc.id}>{cc.code} - {cc.name}</option>
                                                                ))}
                                                            </select>
                                                        </div>
                                                        <div style={{ width: '100px' }}>
                                                            <label style={{ display: 'block', fontSize: '0.6875rem', fontWeight: 700, color: '#6B7280', marginBottom: '4px' }}>%</label>
                                                            <div style={{ position: 'relative' }}>
                                                                <input 
                                                                    type="number" 
                                                                    min="0" max="100" step="1"
                                                                    value={alloc.percentage || ''}
                                                                    onChange={(e) => handleChangeAllocation(item.id, alloc.id, 'percentage', parseFloat(e.target.value) || 0)}
                                                                    style={{
                                                                        width: '100%', padding: '8px 10px',
                                                                        border: '1px solid #D1D5DB', borderRadius: '6px',
                                                                        fontSize: '0.8125rem', backgroundColor: '#FFFFFF',
                                                                        outline: 'none'
                                                                    }}
                                                                />
                                                            </div>
                                                        </div>
                                                        <div style={{ paddingTop: '18px' }}>
                                                            <button
                                                                onClick={() => handleRemoveAllocation(item.id, alloc.id)}
                                                                disabled={allocations.length === 1}
                                                                style={{
                                                                    background: 'none', border: 'none',
                                                                    color: allocations.length === 1 ? '#D1D5DB' : '#EF4444',
                                                                    cursor: allocations.length === 1 ? 'not-allowed' : 'pointer',
                                                                    padding: '6px', borderRadius: '4px'
                                                                }}
                                                                title="Remover"
                                                            >
                                                                <Trash2 size={18} />
                                                            </button>
                                                        </div>
                                                    </div>
                                                    
                                                    {/* Allocation completeness only — never a budget verdict.
                                                        NO_BUDGET/critical states are Step 4's responsibility and
                                                        must never surface here as a positive indicator. */}
                                                    {allocationLineComplete ? (
                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.75rem', color: '#10B981', fontWeight: 600, paddingLeft: '2px', marginTop: '4px' }}>
                                                            <CheckCircle2 size={14} /> Alocação completa
                                                        </div>
                                                    ) : (
                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.75rem', color: '#B45309', fontWeight: 600, paddingLeft: '2px', marginTop: '4px' }}>
                                                            <AlertCircle size={14} /> Alocação incompleta
                                                        </div>
                                                    )}
                                                    {isBudgetWarning && (
                                                        <div style={{ 
                                                            display: 'flex', gap: '8px', alignItems: 'center',
                                                            fontSize: '0.75rem', color: '#92400E', backgroundColor: '#FEF3C7', 
                                                            padding: '8px 12px', borderRadius: '6px', border: '1px solid #FCD34D',
                                                            marginTop: '4px', fontWeight: 600
                                                        }}>
                                                            <AlertCircle size={16} color="#D97706" style={{ flexShrink: 0 }} />
                                                            <span>⚠ Centro de custo próximo ou acima do limite orçamental. A análise completa será feita na etapa de Disponibilidade Orçamental.</span>
                                                        </div>
                                                    )}
                                                </div>
                                            );
                                        })}
                                        
                                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '4px', borderTop: '1px solid #F3F4F6', paddingTop: '12px' }}>
                                            <button
                                                onClick={() => handleAddAllocation(item.id)}
                                                disabled={allocations.length >= 10 || (allocations.length > 1 && totalPercentage >= 100)}
                                                style={{
                                                    background: 'none', border: '1px dashed #D1D5DB',
                                                    color: (allocations.length >= 10 || (allocations.length > 1 && totalPercentage >= 100)) ? '#9CA3AF' : '#2563EB',
                                                    cursor: (allocations.length >= 10 || (allocations.length > 1 && totalPercentage >= 100)) ? 'not-allowed' : 'pointer',
                                                    padding: '6px 12px', borderRadius: '6px', fontSize: '0.75rem', fontWeight: 600,
                                                    display: 'flex', alignItems: 'center', gap: '6px'
                                                }}
                                            >
                                                <Plus size={14} /> Dividir Custo
                                            </button>
                                            
                                            <div style={{ fontSize: '0.8125rem', fontWeight: 600, color: hasError ? '#EF4444' : '#10B981' }}>
                                                Total Alocado: {totalPercentage}% {hasError && '(deve ser 100%)'}
                                            </div>
                                        </div>
                                    </div>
                                )}
                            </div>
                            )}
                        </div>
                    );
                })}
            </div>
            );
        })()}

        {/* Validation warning */}
            {!allAssigned && (
                <div style={{
                    display: 'flex', alignItems: 'flex-start', gap: '12px',
                    padding: '14px 16px', backgroundColor: '#FEF2F2',
                    border: '1px solid #FECACA', borderRadius: '8px'
                }}>
                    <AlertCircle color="#DC2626" size={18} style={{ flexShrink: 0, marginTop: '1px' }} />
                    <div>
                        <span style={{ color: '#991B1B', fontWeight: 600, fontSize: '0.8125rem' }}>Atribuição Pendente</span>
                        <span style={{ color: '#B91C1C', fontSize: '0.8125rem', display: 'block', marginTop: '2px' }}>
                            {totalCount - assignedCount} {totalCount - assignedCount === 1 ? 'item necessita' : 'itens necessitam'} de alocação total de 100% para prosseguir.
                        </span>
                    </div>
                </div>
            )}
        </div>
    );
};
