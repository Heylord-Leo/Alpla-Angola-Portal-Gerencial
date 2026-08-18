import React, { useState, useEffect } from 'react';
import { ItemAssignment } from './WizardStepAllocation';
import { formatCurrencyAO } from '../../lib/utils';
import { AlertCircle, AlertTriangle, Calculator, CheckCircle2, ShieldAlert } from 'lucide-react';
import { api } from '../../lib/api';


interface BudgetAllocationItemDto {
    requestLineItemId: string;
    lineNumber: number;
    description: string;
    amount: number;
}

interface BudgetAllocationPreviewDto {
    plantId: number;
    plantName: string;
    departmentName: string;
    costCenterName: string;
    costCenterId?: number;
    currencyCode: string;
    thisRequestAmount: number;
    annualBudget: number;
    alreadyConsumed: number;
    projectedConsumed: number;
    projectedBalance: number;
    projectedUsagePercent: number;
    status: 'SAFE' | 'WARNING' | 'CRITICAL' | 'OVER_BUDGET' | 'NO_BUDGET' | 'CURRENCY_MISMATCH';
    items: BudgetAllocationItemDto[];
}

export interface AlternativeBudgetLineDto {
    companyId: number;
    companyName: string;
    plantId: number;
    plantName: string;
    departmentId: number;
    departmentName: string;
    costCenterId: number;
    costCenterCode: string;
    costCenterName: string;
    currencyCode: string;
    fiscalYear: number;
    annualBudget: number;
    alreadyConsumed: number;
    availableBefore: number;
    projectedBalanceIfApplied: number;
    projectedUsagePercentIfApplied: number;
    status: 'SAFE' | 'WARNING' | 'CRITICAL' | 'OVER_BUDGET' | 'NO_BUDGET' | 'CURRENCY_MISMATCH';
}

interface BudgetPreviewDto {
    overallStatus: 'SAFE' | 'WARNING' | 'CRITICAL' | 'OVER_BUDGET' | 'NO_BUDGET' | 'CURRENCY_MISMATCH';
    allocations: BudgetAllocationPreviewDto[];
    alternativeBudgetLines: AlternativeBudgetLineDto[];
}

export interface AllocationReassignmentDto {
    oldPlantId: number;
    oldCostCenterId: number | null;
    newPlantId: number;
    newCostCenterId: number | null;
    affectedItemIds: string[];
    reason: string;
}

interface WizardStepBudgetProps {
    requestId: string;
    /** Active ApprovalBatch id — scopes the preview to that batch's items (backend authority). */
    batchId?: string | null;
    /** Candidate model: the Area Approver's TENTATIVE winner selections. Identity only — the
     * backend values them from the frozen candidate snapshots; partial selections preview only
     * the selected subset. */
    batchSelections?: { approvalBatchItemId: string; selectedCandidateId: string }[];
    itemAwards: Record<string, string>;
    itemAssignments: Record<string, ItemAssignment>;
    itemAllocations?: Record<string, any[]>;
    onChangeItemAllocations?: (allocations: Record<string, any[]>) => void;
    budgetJustification: string;
    onChangeBudgetJustification: (val: string) => void;
    onJustificationRequiredChange: (required: boolean) => void;
    reassignments: AllocationReassignmentDto[];
    onChangeReassignments: (reassignments: AllocationReassignmentDto[]) => void;
    onChangeItemAssignments: (assignments: Record<string, ItemAssignment>) => void;
    extraItemDecisions?: Record<string, any>;
    onPreviewLoaded?: (preview: BudgetPreviewDto | null) => void;
}

export const WizardStepBudget: React.FC<WizardStepBudgetProps> = ({
    requestId,
    batchId,
    batchSelections,
    itemAwards,
    itemAssignments,
    itemAllocations,
    onChangeItemAllocations,
    budgetJustification,
    onChangeBudgetJustification,
    onJustificationRequiredChange,
    reassignments,
    onChangeReassignments,
    onChangeItemAssignments,
    extraItemDecisions,
    onPreviewLoaded
}) => {
    const [preview, setPreview] = useState<BudgetPreviewDto | null>(null);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const [isProblematicOpen, setIsProblematicOpen] = useState(true);
    const [isOkOpen, setIsOkOpen] = useState(false);

    const [reassignmentModal, setReassignmentModal] = useState<{
        isOpen: boolean;
        targetAllocation: BudgetAllocationPreviewDto | null;
        alternative: AlternativeBudgetLineDto | null;
        reason: string;
    }>({ isOpen: false, targetAllocation: null, alternative: null, reason: '' });

    const handleOpenReassignment = (targetAllocation: BudgetAllocationPreviewDto, alternative: AlternativeBudgetLineDto) => {
        setReassignmentModal({
            isOpen: true,
            targetAllocation,
            alternative,
            reason: ''
        });
    };

    const handleConfirmReassignment = () => {
        if (reassignmentModal.reason.length < 20) return;
        if (!reassignmentModal.targetAllocation || !reassignmentModal.alternative) return;

        const alloc = reassignmentModal.targetAllocation;
        const alt = reassignmentModal.alternative;
        const affectedItemIds = alloc.items.map(i => i.requestLineItemId);

        // Add to reassignments array
        const newReassignment: AllocationReassignmentDto = {
            oldPlantId: alloc.plantId,
            oldCostCenterId: alloc.costCenterId || null,
            newPlantId: alt.plantId,
            newCostCenterId: alt.costCenterId,
            affectedItemIds,
            reason: reassignmentModal.reason
        };

        const updatedReassignments = [...reassignments, newReassignment];
        onChangeReassignments(updatedReassignments);

        // Update Item Assignments
        const updatedItemAssignments = { ...itemAssignments };
        const updatedItemAllocations = itemAllocations ? { ...itemAllocations } : undefined;

        affectedItemIds.forEach(id => {
            updatedItemAssignments[id] = {
                plantId: alt.plantId,
                costCenterId: alt.costCenterId
            };
            if (updatedItemAllocations && updatedItemAllocations[id]) {
                // For simplicity in UI reassignment, if an item is moved to another cost center due to budget,
                // we assign 100% to the new cost center. This might overwrite complex multi-allocations,
                // but reassignment is a fallback.
                updatedItemAllocations[id] = [{
                    id: Math.random().toString(36).substring(7),
                    plantId: alt.plantId,
                    costCenterId: alt.costCenterId,
                    percentage: 100
                }];
            }
        });
        onChangeItemAssignments(updatedItemAssignments);
        if (updatedItemAllocations && onChangeItemAllocations) {
            onChangeItemAllocations(updatedItemAllocations);
        }

        setReassignmentModal({ isOpen: false, targetAllocation: null, alternative: null, reason: '' });
    };

    const payloadCacheKey = JSON.stringify({
        itemAwards, itemAssignments, itemAllocations, extraItemDecisions,
        batchId: batchId ?? undefined,
        selections: batchSelections && batchSelections.length > 0 ? batchSelections : undefined
    });

    useEffect(() => {
        const fetchPreview = async () => {
            setIsLoading(true);
            setError(null);
            try {
                const payload = JSON.parse(payloadCacheKey);
                const data = await api.requests.getBudgetPreview(requestId, payload);
                setPreview(data);

                if (onPreviewLoaded) {
                    onPreviewLoaded(data);
                }

                // Notify parent if justification is required
                let requiresJustification = data.requiresJustification === true;
                if (!requiresJustification) {
                    requiresJustification = data.allocations.some((a: any) => 
                        a.status === 'CRITICAL' || 
                        a.status === 'OVER_BUDGET' || 
                        a.projectedBalance < 0 || 
                        a.projectedUsagePercent >= 100
                    );
                }
                onJustificationRequiredChange(requiresJustification);

            } catch (err: any) {
                setError(err.message || 'Erro desconhecido');
                onJustificationRequiredChange(false);
                if (onPreviewLoaded) onPreviewLoaded(null);
            } finally {
                setIsLoading(false);
            }
        };

        fetchPreview();
    }, [requestId, payloadCacheKey, onJustificationRequiredChange, onPreviewLoaded]);

    if (isLoading) {
        return (
            <div style={{ padding: '40px', textAlign: 'center', color: '#6B7280' }}>
                <Calculator size={32} style={{ animation: 'spin 2s linear infinite', marginBottom: '16px' }} />
                <p>Calculando impacto orçamental...</p>
            </div>
        );
    }

    if (error) {
        return (
            <div style={{ padding: '20px', backgroundColor: '#FEF2F2', border: '1px solid #FECACA', borderRadius: '8px', color: '#991B1B' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                    <AlertCircle size={20} />
                    <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 600 }}>Erro de Cálculo</h3>
                </div>
                <p style={{ margin: 0 }}>{error}</p>
            </div>
        );
    }

    if (!preview) return null;

    let isBudgetJustificationRequired = (preview as any).requiresJustification === true;
    if (!isBudgetJustificationRequired) {
        isBudgetJustificationRequired = preview.allocations.some((a: any) => 
            a.status === 'CRITICAL' || 
            a.status === 'OVER_BUDGET' || 
            a.projectedBalance < 0 || 
            a.projectedUsagePercent >= 100
        );
    }

    const getStatusConfig = (status: string) => {
        switch (status) {
            case 'SAFE': return { color: '#10B981', bg: '#D1FAE5', icon: CheckCircle2, label: 'Disponível' };
            case 'WARNING': return { color: '#F59E0B', bg: '#FEF3C7', icon: AlertTriangle, label: 'Atenção' };
            case 'CRITICAL': return { color: '#EF4444', bg: '#FEE2E2', icon: AlertCircle, label: 'Crítico' };
            case 'OVER_BUDGET': return { color: '#991B1B', bg: '#FEF2F2', icon: ShieldAlert, label: 'Limite Excedido' };
            case 'NO_BUDGET': return { color: '#6B7280', bg: '#F3F4F6', icon: AlertCircle, label: 'Sem Orçamento Configurado' };
            case 'CURRENCY_MISMATCH': return { color: '#8B5CF6', bg: '#EDE9FE', icon: AlertCircle, label: 'Moeda Diferente' };
            default: return { color: '#6B7280', bg: '#F3F4F6', icon: CheckCircle2, label: 'Desconhecido' };
        }
    };

    const getOverallMessage = (status: string) => {
        switch (status) {
            case 'SAFE': return "Todos os centros de custo possuem orçamento disponível. Nenhuma ação adicional é necessária. Clique em Próximo para continuar.";
            case 'NO_BUDGET': return "Atenção: há centro de custo sem orçamento configurado para este ano e moeda. Você pode transferir os itens para uma alternativa disponível ou prosseguir com justificativa.";
            case 'OVER_BUDGET': return "Atenção: a aprovação deste pedido irá exceder o orçamento de um ou mais centros de custo. Você pode transferir os itens para uma alternativa disponível ou prosseguir com justificativa.";
            case 'CRITICAL': return "Atenção: um ou mais centros de custo ficarão próximos do limite orçamental. Você pode prosseguir, mas será necessária justificativa.";
            case 'WARNING': return "Atenção: um ou mais centros de custo terão uso elevado do orçamento, mas ainda dentro do limite.";
            default: return "Análise orçamental.";
        }
    };

    const overallConfig = getStatusConfig(preview.overallStatus);
    const OverallIcon = overallConfig.icon;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            {/* Contextual Guidance */}
            <div style={{
                backgroundColor: '#EFF6FF', border: '1px solid #BFDBFE',
                borderRadius: '6px', padding: '12px 16px', color: '#1E3A8A',
                fontSize: '0.875rem', display: 'flex', gap: '10px', alignItems: 'flex-start'
            }}>
                <span style={{ fontSize: '1.25rem', lineHeight: 1 }}>💡</span>
                <div>
                    Esta etapa mostra o impacto orçamental das alocações selecionadas. A análise é consultiva e não reserva orçamento automaticamente. Quando houver limite excedido, será necessário justificar.
                </div>
            </div>

            {/* Header / Intro */}
            <div>
                <h2 style={{ margin: '0 0 12px 0', fontSize: '1.5rem', fontWeight: 700, color: '#111827' }}>
                    Análise Orçamental do Pedido
                </h2>
                <p style={{ margin: '0 0 0 0', color: '#4B5563', fontSize: '0.9rem', lineHeight: 1.6 }}>
                    Revise o resumo abaixo. Se necessário, você pode transferir os itens para outro centro de custo disponível.
                </p>
            </div>

            {/* Decision Summary Banner */}
            <div style={{
                padding: '16px',
                backgroundColor: overallConfig.bg,
                border: `1px solid ${overallConfig.color}40`,
                borderRadius: '8px',
                display: 'flex',
                alignItems: 'center',
                gap: '12px'
            }}>
                <div style={{ color: overallConfig.color, display: 'flex', alignItems: 'center', flexShrink: 0 }}>
                    <OverallIcon size={24} />
                </div>
                <div style={{ color: '#111827', fontSize: '0.95rem', fontWeight: 500, lineHeight: 1.4 }}>
                    {getOverallMessage(preview.overallStatus)}
                </div>
            </div>

            {/* Resumo Orçamental */}
            {(() => {
                const problematicAllocs = preview.allocations.filter(a => ['WARNING', 'CRITICAL', 'OVER_BUDGET', 'NO_BUDGET', 'CURRENCY_MISMATCH'].includes(a.status));
                const okAllocs = preview.allocations.filter(a => a.status === 'SAFE');
                const totalRequestAmount = preview.allocations.reduce((sum, a) => sum + a.thisRequestAmount, 0);

                const renderAllocationCard = (alloc: BudgetAllocationPreviewDto, idx: number) => {
                    const cfg = getStatusConfig(alloc.status);
                    const StatusIcon = cfg.icon;
                    return (
                        <div key={idx} style={{
                            backgroundColor: '#FFFFFF',
                            border: '1px solid #E5E7EB',
                            borderRadius: '8px',
                            padding: '16px',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '16px'
                        }}>
                            {/* Allocation Header */}
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                <div>
                                    <div style={{ fontSize: '0.875rem', color: '#6B7280', fontWeight: 500, marginBottom: '2px' }}>
                                        {alloc.plantName}
                                    </div>
                                    <div style={{ fontSize: '1rem', fontWeight: 600, color: '#111827' }}>
                                        {alloc.departmentName} — {alloc.costCenterName}
                                    </div>
                                </div>
                                <div style={{
                                    display: 'flex', alignItems: 'center', gap: '6px',
                                    padding: '4px 12px', borderRadius: '9999px',
                                    backgroundColor: cfg.bg, color: cfg.color,
                                    fontSize: '0.75rem', fontWeight: 700
                                }}>
                                    <StatusIcon size={14} />
                                    {cfg.label}
                                </div>
                            </div>

                            {/* Progress Bar & Values */}
                            {alloc.status !== 'NO_BUDGET' && alloc.status !== 'CURRENCY_MISMATCH' && (
                                <div>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px', fontSize: '0.875rem' }}>
                                        <div style={{ color: '#6B7280' }}>
                                            Orçamento Anual: <span style={{ fontWeight: 600, color: '#111827' }}>{formatCurrencyAO(alloc.annualBudget, alloc.currencyCode)}</span>
                                        </div>
                                        <div style={{ fontWeight: 700, color: cfg.color }}>
                                            Uso previsto do orçamento: {alloc.projectedUsagePercent.toFixed(1)}%
                                        </div>
                                    </div>
                                    
                                    <div style={{ height: '8px', backgroundColor: '#E5E7EB', borderRadius: '4px', overflow: 'hidden', display: 'flex' }}>
                                        <div style={{ 
                                            height: '100%', 
                                            width: `${Math.min((alloc.alreadyConsumed / alloc.annualBudget) * 100, 100)}%`, 
                                            backgroundColor: '#D1D5DB' 
                                        }} />
                                        <div style={{ 
                                            height: '100%', 
                                            width: `${Math.min((alloc.thisRequestAmount / alloc.annualBudget) * 100, 100)}%`, 
                                            backgroundColor: cfg.color 
                                        }} />
                                    </div>
                                    
                                    <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: '8px', fontSize: '0.75rem', color: '#6B7280' }}>
                                        <div>Já Utilizado / Comprometido: {formatCurrencyAO(alloc.alreadyConsumed, alloc.currencyCode)}</div>
                                        <div>Valor adicionado por este pedido: <span style={{ fontWeight: 600, color: '#111827' }}>+{formatCurrencyAO(alloc.thisRequestAmount, alloc.currencyCode)}</span></div>
                                    </div>
                                    <div style={{ marginTop: '4px', fontSize: '0.875rem', fontWeight: 600, color: cfg.color }}>
                                        Saldo após este Pedido: {formatCurrencyAO(alloc.projectedBalance || (alloc.annualBudget - alloc.projectedConsumed), alloc.currencyCode)}
                                    </div>
                                </div>
                            )}

                            {/* Missing Budget Explanation */}
                            {alloc.status === 'NO_BUDGET' && (
                                <div style={{ backgroundColor: '#F9FAFB', border: '1px solid #E5E7EB', borderRadius: '6px', padding: '12px', fontSize: '0.875rem', color: '#4B5563', display: 'flex', gap: '8px' }}>
                                    <div style={{ flexShrink: 0, marginTop: '2px', color: '#6B7280' }}><AlertCircle size={16} /></div>
                                    <div>
                                        <strong>O que isso significa?</strong> Não foi encontrado um orçamento anual ativo para este centro de custo nesta moeda. Você ainda pode aprovar, mas o sistema pedirá uma justificativa.
                                    </div>
                                </div>
                            )}

                            {/* Items mapped to this allocation */}
                            <div style={{ backgroundColor: '#F9FAFB', borderRadius: '6px', padding: '12px' }}>
                                <div style={{ fontSize: '0.75rem', fontWeight: 600, color: '#6B7280', marginBottom: '8px', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                    Itens do Pedido ({alloc.items.length})
                                </div>
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                    {alloc.items.map((item, i) => (
                                        <div key={i} style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.875rem' }}>
                                            <div style={{ color: '#374151', display: 'flex', gap: '8px' }}>
                                                <span style={{ color: '#9CA3AF' }}>#{item.lineNumber}</span>
                                                {item.description}
                                            </div>
                                            <div style={{ fontWeight: 500, color: '#111827' }}>
                                                {formatCurrencyAO(item.amount, alloc.currencyCode)}
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </div>

                            {/* Alternative Budgets */}
                            {preview.overallStatus !== 'SAFE' && alloc.status !== 'SAFE' && preview.alternativeBudgetLines && preview.alternativeBudgetLines.length > 0 && (
                                <div style={{ marginTop: '8px', paddingLeft: '24px', borderLeft: '2px solid #E5E7EB' }}>
                                    <div style={{ backgroundColor: '#FFFFFF', border: '1px solid #E5E7EB', borderRadius: '8px', padding: '16px' }}>
                                        <div style={{ fontSize: '1rem', fontWeight: 600, color: '#111827', marginBottom: '8px' }}>
                                            Centros de Custo Alternativos
                                        </div>
                                        <p style={{ margin: '0 0 12px 0', fontSize: '0.875rem', color: '#4B5563', lineHeight: 1.5 }}>
                                            Encontramos centros de custo dentro do seu escopo que possuem orçamento disponível. Você pode transferir os itens desta alocação para uma das opções abaixo.<br/><br/>
                                            <span style={{ fontSize: '0.75rem', color: '#6B7280' }}>
                                                Ao transferir, apenas os itens desta alocação serão movidos. Os demais itens do pedido não serão alterados e a análise orçamental será recalculada.
                                            </span>
                                        </p>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                            {preview.alternativeBudgetLines.map((alt, altIdx) => {
                                                const projBalance = alt.availableBefore - alloc.thisRequestAmount;
                                                const projPercent = alt.annualBudget > 0 ? ((alt.alreadyConsumed + alloc.thisRequestAmount) / alt.annualBudget) * 100 : 0;
                                                const dynamicCfg = getStatusConfig(
                                                    projPercent <= 75 ? 'SAFE' : projPercent <= 90 ? 'WARNING' : projPercent <= 100 ? 'CRITICAL' : 'OVER_BUDGET'
                                                );
                                                
                                                return (
                                                    <div key={altIdx} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', backgroundColor: '#F9FAFB', padding: '12px', borderRadius: '6px', border: '1px solid #E5E7EB', gap: '16px', flexWrap: 'wrap' }}>
                                                        <div>
                                                            <div style={{ fontSize: '0.875rem', fontWeight: 600, color: '#111827' }}>{alt.plantName} / {alt.costCenterName}</div>
                                                            <div style={{ fontSize: '0.75rem', color: '#6B7280', marginTop: '4px' }}>
                                                                Saldo antes: {formatCurrencyAO(alt.availableBefore, alt.currencyCode)} &rarr; <span style={{ color: dynamicCfg.color, fontWeight: 600 }}>Saldo após este Pedido: {formatCurrencyAO(projBalance, alt.currencyCode)} ({projPercent.toFixed(1)}%)</span>
                                                            </div>
                                                        </div>
                                                        <button
                                                            onClick={() => handleOpenReassignment(alloc, alt)}
                                                            style={{ padding: '8px 16px', backgroundColor: 'transparent', color: '#2563EB', border: '1px solid #2563EB', borderRadius: '6px', fontSize: '0.8125rem', fontWeight: 500, cursor: 'pointer', flexShrink: 0, transition: 'all 0.2s ease' }}
                                                            onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#EFF6FF'; }}
                                                            onMouseOut={(e) => { e.currentTarget.style.backgroundColor = 'transparent'; }}
                                                        >
                                                            Transferir para este centro de custo
                                                        </button>
                                                    </div>
                                                );
                                            })}
                                        </div>
                                    </div>
                                </div>
                            )}
                        </div>
                    );
                };

                return (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px' }}>
                            <div style={{ backgroundColor: '#F9FAFB', padding: '16px', borderRadius: '8px', border: '1px solid #E5E7EB' }}>
                                <div style={{ fontSize: '0.875rem', color: '#6B7280', marginBottom: '4px' }}>Valor do Pedido</div>
                                <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#111827' }}>{formatCurrencyAO(totalRequestAmount, preview.allocations[0]?.currencyCode || 'AOA')}</div>
                            </div>
                            <div style={{ backgroundColor: '#F9FAFB', padding: '16px', borderRadius: '8px', border: '1px solid #E5E7EB' }}>
                                <div style={{ fontSize: '0.875rem', color: '#6B7280', marginBottom: '4px' }}>Centros de Custo Atingidos</div>
                                <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#111827' }}>{preview.allocations.length}</div>
                            </div>
                            <div style={{ backgroundColor: problematicAllocs.length > 0 ? '#FEF2F2' : '#F9FAFB', padding: '16px', borderRadius: '8px', border: `1px solid ${problematicAllocs.length > 0 ? '#FECACA' : '#E5E7EB'}` }}>
                                <div style={{ fontSize: '0.875rem', color: problematicAllocs.length > 0 ? '#991B1B' : '#6B7280', marginBottom: '4px' }}>Com Problemas / Alertas</div>
                                <div style={{ fontSize: '1.25rem', fontWeight: 700, color: problematicAllocs.length > 0 ? '#991B1B' : '#111827' }}>{problematicAllocs.length}</div>
                            </div>
                            <div style={{ backgroundColor: okAllocs.length > 0 ? '#ECFDF5' : '#F9FAFB', padding: '16px', borderRadius: '8px', border: `1px solid ${okAllocs.length > 0 ? '#A7F3D0' : '#E5E7EB'}` }}>
                                <div style={{ fontSize: '0.875rem', color: okAllocs.length > 0 ? '#065F46' : '#6B7280', marginBottom: '4px' }}>Dentro do Orçamento</div>
                                <div style={{ fontSize: '1.25rem', fontWeight: 700, color: okAllocs.length > 0 ? '#065F46' : '#111827' }}>{okAllocs.length}</div>
                            </div>
                        </div>

                        {problematicAllocs.length > 0 && (
                            <div style={{ border: '1px solid #FECACA', borderRadius: '8px', overflow: 'hidden' }}>
                                <div 
                                    onClick={() => setIsProblematicOpen(!isProblematicOpen)}
                                    style={{ padding: '16px', backgroundColor: '#FEF2F2', cursor: 'pointer', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}
                                >
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#991B1B', fontWeight: 600 }}>
                                        <AlertCircle size={20} />
                                        Centros de Custo com Problemas ({problematicAllocs.length})
                                    </div>
                                    <div style={{ color: '#991B1B', fontWeight: 600, transform: isProblematicOpen ? 'rotate(180deg)' : 'none', transition: 'transform 0.2s' }}>▼</div>
                                </div>
                                {isProblematicOpen && (
                                    <div style={{ padding: '16px', display: 'flex', flexDirection: 'column', gap: '16px', backgroundColor: '#FFFFFF' }}>
                                        {problematicAllocs.map((alloc, idx) => renderAllocationCard(alloc, idx))}
                                    </div>
                                )}
                            </div>
                        )}

                        {okAllocs.length > 0 && (
                            <div style={{ border: '1px solid #E5E7EB', borderRadius: '8px', overflow: 'hidden' }}>
                                <div 
                                    onClick={() => setIsOkOpen(!isOkOpen)}
                                    style={{ padding: '16px', backgroundColor: '#F9FAFB', cursor: 'pointer', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}
                                >
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#374151', fontWeight: 600 }}>
                                        <CheckCircle2 size={20} color="#10B981" />
                                        Centros de Custo OK ({okAllocs.length})
                                    </div>
                                    <div style={{ color: '#6B7280', fontWeight: 600, transform: isOkOpen ? 'rotate(180deg)' : 'none', transition: 'transform 0.2s' }}>▼</div>
                                </div>
                                {isOkOpen && (
                                    <div style={{ padding: '16px', display: 'flex', flexDirection: 'column', gap: '16px', backgroundColor: '#FFFFFF' }}>
                                        {okAllocs.map((alloc, idx) => renderAllocationCard(alloc, idx))}
                                    </div>
                                )}
                            </div>
                        )}
                    </div>
                );
            })()}

            {/* Justification Field (Only if required) */}
            {isBudgetJustificationRequired && (
                <div style={{
                    backgroundColor: '#FEF2F2',
                    border: '1px solid #FCA5A5',
                    borderRadius: '8px',
                    padding: '20px',
                    marginTop: '8px'
                }}>
                    <div style={{ display: 'flex', gap: '12px', marginBottom: '12px' }}>
                        <ShieldAlert size={20} color="#DC2626" style={{ flexShrink: 0, marginTop: '2px' }} />
                        <div>
                            <h4 style={{ margin: '0 0 4px 0', color: '#991B1B', fontSize: '1rem', fontWeight: 600 }}>
                                Justificativa para seguir com orçamento excedido
                            </h4>
                            <p style={{ margin: 0, color: '#B91C1C', fontSize: '0.875rem', lineHeight: 1.5 }}>
                                Este pedido excede o orçamento disponível para um ou mais centros de custo. Explique por que a aprovação deve continuar mesmo com a exceção orçamental.
                            </p>
                        </div>
                    </div>
                    
                    <textarea
                        value={budgetJustification}
                        onChange={(e) => onChangeBudgetJustification(e.target.value)}
                        placeholder="Explique por que este pedido deve continuar mesmo excedendo o orçamento disponível deste centro de custo..."
                        rows={3}
                        style={{
                            width: '100%',
                            padding: '12px',
                            borderRadius: '6px',
                            border: `1px solid ${budgetJustification.length > 0 && budgetJustification.length < 20 ? '#EF4444' : '#FCA5A5'}`,
                            fontSize: '0.875rem',
                            resize: 'vertical',
                            outline: 'none',
                            fontFamily: 'inherit'
                        }}
                    />
                    <div style={{ 
                        textAlign: 'right', 
                        fontSize: '0.75rem', 
                        marginTop: '6px',
                        color: budgetJustification.length > 0 && budgetJustification.length < 20 ? '#EF4444' : '#6B7280'
                    }}>
                        {budgetJustification.length} / 20 caracteres mínimos
                    </div>
                </div>
            )}

            {/* Reassignment Modal */}
            {reassignmentModal.isOpen && reassignmentModal.targetAllocation && reassignmentModal.alternative && (
                <div style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 9999 }}>
                    <div style={{ backgroundColor: 'white', borderRadius: '8px', padding: '24px', width: '500px', maxWidth: '90vw', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                        <h3 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 600 }}>Confirmar Alteração de Centro de Custo</h3>
                        
                        <div style={{ backgroundColor: '#F9FAFB', padding: '12px', borderRadius: '6px', fontSize: '0.875rem' }}>
                            <div><strong>Itens afetados:</strong> {reassignmentModal.targetAllocation.items.length}</div>
                            <div style={{ marginTop: '8px' }}>
                                <div style={{ color: '#6B7280' }}>De:</div>
                                <div>{reassignmentModal.targetAllocation.plantName} / {reassignmentModal.targetAllocation.costCenterName}</div>
                            </div>
                            <div style={{ marginTop: '8px' }}>
                                <div style={{ color: '#6B7280' }}>Para:</div>
                                <div>{reassignmentModal.alternative.plantName} / {reassignmentModal.alternative.costCenterName}</div>
                            </div>
                        </div>

                        <div>
                            <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 500, marginBottom: '4px' }}>Motivo da Alteração (Obrigatório)</label>
                            <textarea
                                value={reassignmentModal.reason}
                                onChange={(e) => setReassignmentModal(prev => ({ ...prev, reason: e.target.value }))}
                                placeholder="Justifique a mudança de centro de custo..."
                                rows={3}
                                style={{
                                    width: '100%', padding: '8px', borderRadius: '6px',
                                    border: `1px solid ${reassignmentModal.reason.length > 0 && reassignmentModal.reason.length < 20 ? '#EF4444' : '#D1D5DB'}`,
                                    fontSize: '0.875rem',
                                    fontFamily: 'inherit'
                                }}
                            />
                            <div style={{ fontSize: '0.75rem', color: reassignmentModal.reason.length > 0 && reassignmentModal.reason.length < 20 ? '#EF4444' : '#6B7280', marginTop: '4px', textAlign: 'right' }}>
                                {reassignmentModal.reason.length} / 20 caracteres
                            </div>
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '8px' }}>
                            <button
                                onClick={() => setReassignmentModal({ isOpen: false, targetAllocation: null, alternative: null, reason: '' })}
                                style={{ padding: '8px 16px', border: '1px solid #D1D5DB', borderRadius: '6px', backgroundColor: 'white', cursor: 'pointer', fontWeight: 500 }}
                            >
                                Cancelar
                            </button>
                            <button
                                onClick={handleConfirmReassignment}
                                disabled={reassignmentModal.reason.length < 20}
                                style={{ 
                                    padding: '8px 16px', border: 'none', borderRadius: '6px', 
                                    backgroundColor: reassignmentModal.reason.length >= 20 ? '#2563EB' : '#9CA3AF', 
                                    color: 'white', cursor: reassignmentModal.reason.length >= 20 ? 'pointer' : 'not-allowed',
                                    fontWeight: 500 
                                }}
                            >
                                Confirmar Alteração
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
