import { useState, useEffect } from 'react';
import {
    User, Building2, Factory,
    Calendar, Landmark, Download,
    Paperclip, AlertCircle, List,
    MessageSquare, Users, History as HistoryIcon, DollarSign,
    Target, TrendingUp, ArrowRightLeft, AlertTriangle, ShieldCheck
} from 'lucide-react';
import { RequestDetailsDto, ApprovalIntelligenceDto } from '../../types';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { FeedbackType } from '../../components/ui/Feedback';
import { Tooltip } from '../../components/ui/Tooltip';
import { api } from '../../lib/api';
import { formatDate, formatCurrencyAO } from '../../lib/utils';
import { motion } from 'framer-motion';

// Decision specific components
import { DecisionHeader } from './components/DecisionHeader';
import { DecisionSummaryGrid } from './components/DecisionSummaryGrid';
import { DecisionSection } from './components/DecisionSection';
import { DecisionQuotationCard } from './components/DecisionQuotationCard';
import { DecisionTimeline } from './components/DecisionTimeline';
import { DecisionInsightsPanel } from './components/DecisionInsightsPanel';

// --- Constants ---

const ATTACHMENT_TYPE_LABELS: Record<string, string> = {
    'PROFORMA': 'Proforma',
    'PO': 'P.O (Ordem de Compra)',
    'PAYMENT_SCHEDULE': 'Cronograma de Pagamento',
    'PAYMENT_PROOF': 'Comprovante de Pagamento',
    'GENERAL': 'Documento Geral',
    'INVOICE': 'Fatura',
    'RECEIPT': 'Recibo',
};

// --- Interfaces ---

export interface ApprovalDetailPanelProps {
    data: RequestDetailsDto;
    approvalStage: 'AREA' | 'FINAL';
    isAreaApprover: boolean;
    isFinalApprover: boolean;
    onActionCompleted: (successMessage: string) => void;
    onClose: () => void;
    onDataRefresh: () => Promise<void>;
    isDrawerContext?: boolean;
    onDrillDown?: (item: any) => void;
    // Navigation props
    onNext?: () => void;
    onPrev?: () => void;
    currentIndex?: number;
    totalCount?: number;
}

// --- Component ---

export function ApprovalDetailPanel({
    data,
    approvalStage,
    onActionCompleted,
    onClose,
    onDataRefresh,
    onDrillDown,
    onNext,
    onPrev,
    currentIndex,
    totalCount
}: ApprovalDetailPanelProps) {

    // Approval modal state
    const [showApprovalModal, setShowApprovalModal] = useState<{
        show: boolean;
        type: ApprovalActionType;
    }>({ show: false, type: null });
    const [approvalComment, setApprovalComment] = useState('');
    const [approvalProcessing, setApprovalProcessing] = useState(false);
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    
    // Internal processing state for winner selection
    const [quotationProcessingId, setQuotationProcessingId] = useState<string | null>(null);

    // UX Tracking
    const [highlightSection, setHighlightSection] = useState(false);
    const [highlightFields, setHighlightFields] = useState(false);

    // Phase 3A: Intelligence
    const [intelligence, setIntelligence] = useState<ApprovalIntelligenceDto | null>(null);
    const [loadingIntelligence, setLoadingIntelligence] = useState(false);

    // Business Control: Cost Center & Plant Selection (DEC-085/DEC-099/DEC-103)
    const [costCenters, setCostCenters] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);
    const [itemAssignments, setItemAssignments] = useState<Record<string, { plantId: number | null, costCenterId: number | null }>>({});
    const [activeInsightItemId, setActiveInsightItemId] = useState<string | null>(null);
    const [activeAssignmentItemId, setActiveAssignmentItemId] = useState<string | null>(null);
    const [viewMode, setViewMode] = useState<'CARDS' | 'LIST'>(
        (data.lineItems && data.lineItems.length > 5) ? 'LIST' : 'CARDS'
    );
    const [insightSearchQ, setInsightSearchQ] = useState('');

    const isAreaApprovalStage = data.statusCode === 'WAITING_AREA_APPROVAL';
    const isFinalApprovalStage = data.statusCode === 'WAITING_FINAL_APPROVAL';

    useEffect(() => {
        if (data.id) {
            fetchIntelligence();
        }
    }, [data.id]);

    useEffect(() => {
        if (approvalStage === 'AREA' && data.companyId) {
            // Fetch all CCs and Plants for the company to support item-level re-assignment
            fetchCostCenters(data.companyId);
            fetchPlants(data.companyId);
        }
    }, [approvalStage, data.companyId]);

    // Initialize item assignments from existing data
    useEffect(() => {
        if (data.lineItems) {
            const initialMap: Record<string, { plantId: number | null, costCenterId: number | null }> = {};
            data.lineItems.forEach(item => {
                initialMap[item.id] = {
                    plantId: item.plantId || null,
                    costCenterId: item.costCenterId || null
                };
            });
            setItemAssignments(initialMap);
            if (data.lineItems.length > 0) {
                if (!activeInsightItemId) setActiveInsightItemId(data.lineItems[0].id);
                if (!activeAssignmentItemId) {
                    const firstPending = data.lineItems.find(i => !initialMap[i.id].plantId || !initialMap[i.id].costCenterId);
                    setActiveAssignmentItemId(firstPending ? firstPending.id : data.lineItems[0].id);
                }
            }
        }
    }, [data.id, data.lineItems]);

    const fetchIntelligence = async () => {
        setLoadingIntelligence(true);
        try {
            const intel = await api.approvals.getIntelligence(data.id);
            setIntelligence(intel);
        } catch (err) {
            console.error('Failed to fetch approval intelligence:', err);
        } finally {
            setLoadingIntelligence(false);
        }
    };

    const fetchCostCenters = async (companyId: number) => {
        try {
            const list = await api.lookups.getCostCenters(false, undefined, companyId);
            setCostCenters(list);
        } catch (err) {
            console.error('Failed to fetch cost centers:', err);
        }
    };

    const fetchPlants = async (companyId: number) => {
        try {
            const list = await api.lookups.getPlants(companyId, false);
            setPlants(list);
        } catch (err) {
            console.error('Failed to fetch plants:', err);
        }
    };

    // --- Computed ---
    const isQuotation = data.requestTypeCode === 'QUOTATION';
    const isPayment = data.requestTypeCode === 'PAYMENT';
    const selectedQuotation = data.quotations?.find(q => q.isSelected);
    const hasWinnerSelected = !!selectedQuotation;

    const activeItems = data.lineItems || [];

    // DEC-099/DEC-103: Cost Center & Plant Validation Logic
    const itemsMissingPlant = activeItems.filter(item => !itemAssignments[item.id]?.plantId);
    const itemsMissingCC = activeItems.filter(item => !itemAssignments[item.id]?.costCenterId);
    
    const countMissingPlant = itemsMissingPlant.length;
    const countMissingCC = itemsMissingCC.length;
    
    const allAssigned = activeItems.length > 0 && countMissingPlant === 0 && countMissingCC === 0;
    
    // A simplified helper to find if all items currently share the same assignment (for summary display)
    const currentPairs = Object.values(itemAssignments).filter(v => v.plantId && v.costCenterId);
    const uniquePlants = Array.from(new Set(currentPairs.map(p => p.plantId)));
    const uniqueCCs = Array.from(new Set(currentPairs.map(p => p.costCenterId)));
    
    const isUnifiedAssignment = activeItems.length > 0 && allAssigned && uniquePlants.length === 1 && uniqueCCs.length === 1;
    const unifiedCC = isUnifiedAssignment ? costCenters.find(cc => cc.id === uniqueCCs[0]) : null;

    const handleBulkFill = (itemId: string) => {
        const source = itemAssignments[itemId];
        if (!source || !source.plantId || !source.costCenterId) return;

        const newMap = { ...itemAssignments };
        activeItems.forEach(item => {
            // Safer Helper: Apply only to items that are still pending
            if (!newMap[item.id]?.plantId || !newMap[item.id]?.costCenterId) {
                newMap[item.id] = {
                    plantId: source.plantId,
                    costCenterId: source.costCenterId
                };
            }
        });
        setItemAssignments(newMap);
    };

    const canBulkFill = (itemId: string): boolean => {
        const source = itemAssignments[itemId];
        if (!source || !source.plantId || !source.costCenterId) return false;

        // True if there is at least one OTHER pending item
        return activeItems.some(item => item.id !== itemId && (!itemAssignments[item.id]?.plantId || !itemAssignments[item.id]?.costCenterId));
    };

    // Winner selection is enabled for QUOTATION requests when the approver is in their decision stage
    const canSelectWinner = isQuotation && (
        (approvalStage === 'AREA' && isAreaApprovalStage) ||
        (approvalStage === 'FINAL' && isFinalApprovalStage)
    );

    // Approve is blocked if winner not selected (Quotation) OR any Item Assignment is missing (Area Approval)
    const isApproveBlocked = (isQuotation && !hasWinnerSelected) || (approvalStage === 'AREA' && isAreaApprovalStage && !allAssigned);

    // REQUEST_ADJUSTMENT is hidden for PAYMENT requests
    const showAdjustmentAction = !isPayment;

    // Find lowest amount for visual highlights
    const lowestByCurrency: Record<string, number> = {};
    if (isQuotation && data.quotations) {
        data.quotations.forEach(q => {
            if (!lowestByCurrency[q.currency] || q.totalAmount < lowestByCurrency[q.currency]) {
                lowestByCurrency[q.currency] = q.totalAmount;
            }
        });
    }

    // Intelligence summary flags
    const intelItemsWithHistory = intelligence?.items?.filter(i => i.hasHistory) || [];
    const hasHistoricalItems = intelItemsWithHistory.length > 0;
    // Considered above if unit price is greater than historical average
    const hasItemAboveAvg = intelItemsWithHistory.some(i => i.currentUnitPrice > (i.averageHistoricalPrice || 0));

    // --- Handlers ---

    const handleAllocationWarningClick = () => {
        document.getElementById('itens-do-pedido-section')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        
        setHighlightSection(true);
        setHighlightFields(true);
        
        setTimeout(() => {
            setHighlightSection(false);
        }, 5000);
    };

    const handleSelectWinner = async (quotationId: string) => {
        setQuotationProcessingId(quotationId);
        try {
            await api.requests.selectQuotation(data.id, quotationId);
            await onDataRefresh();
        } catch (err: any) {
            console.error('Failed to select winner:', err);
        } finally {
            setQuotationProcessingId(null);
        }
    };

    const handleDownloadAttachment = async (attachmentId: string, fileName: string) => {
        try {
            await api.attachments.download(attachmentId, fileName);
        } catch (err: any) {
            console.error('Failed to download attachment:', err);
        }
    };

    const handleApprovalAction = async (action: ApprovalActionType) => {
        if (!action) return;

        if (action === 'APPROVE') {
            // DEC-099/DEC-103: item-level assignment validation
            if (approvalStage === 'AREA' && !allAssigned) {
                let msg = 'Todos os itens devem ter Planta e Centro de Custo atribuídos.';
                if (countMissingPlant > 0 && countMissingCC > 0) msg = `Faltam ${countMissingPlant} plantas e ${countMissingCC} centros de custo.`;
                else if (countMissingPlant > 0) msg = `Faltam ${countMissingPlant} plantas a serem definidas.`;
                else msg = `Faltam ${countMissingCC} centros de custo a serem definidos.`;

                setModalFeedback({ 
                    type: 'error', 
                    message: msg
                });
                return;
            }

            if (isQuotation) {
                const selected = data.quotations?.find(q => q.isSelected);
                if (!selected) {
                    setModalFeedback({ type: 'error', message: 'É necessário selecionar uma cotação vencedora antes de aprovar.' });
                    return;
                }
            }
        }

        // Validate comment for reject / adjustment
        if ((action === 'REJECT' || action === 'REQUEST_ADJUSTMENT') && !approvalComment.trim()) {
            setModalFeedback({
                type: 'error',
                message: action === 'REJECT' ? 'Informe o motivo da rejeição.' : 'Informe o motivo do reajuste.'
            });
            return;
        }

        setApprovalProcessing(true);
        setModalFeedback({ type: 'error', message: null });

        try {
            let result;
            const isArea = isAreaApprovalStage;

            if (action === 'APPROVE') {
                result = isArea
                    ? await api.requests.approveArea(data.id, approvalComment, data.quotations?.find(q => q.isSelected)?.id, itemAssignments)
                    : await api.requests.approveFinal(data.id, approvalComment);
            } else if (action === 'REJECT') {
                result = isArea
                    ? await api.requests.rejectArea(data.id, approvalComment, itemAssignments)
                    : await api.requests.rejectFinal(data.id, approvalComment);
            } else if (action === 'REQUEST_ADJUSTMENT') {
                result = isArea
                    ? await api.requests.requestAdjustmentArea(data.id, approvalComment, itemAssignments)
                    : await api.requests.requestAdjustmentFinal(data.id, approvalComment);
            } else {
                throw new Error('Ação inválida.');
            }

            setShowApprovalModal({ show: false, type: null });
            setApprovalComment('');
            setModalFeedback({ type: 'error', message: null });
            onActionCompleted(result.message || 'Ação concluída com sucesso.');
        } catch (err: any) {
            setModalFeedback({ type: 'error', message: err.message || 'Não foi possível concluir a ação. Tente novamente.' });
        } finally {
            setApprovalProcessing(false);
        }
    };

    // --- Render Helpers ---

    const summaryItems = [
        { label: 'Solicitante', value: data.requesterName, icon: <User size={12} /> },
        { label: 'Departamento', value: data.departmentName, icon: <Building2 size={12} /> },
        { label: 'Empresa', value: data.companyName },
        { label: 'Planta', value: data.plantName, icon: <Factory size={12} /> },
        { label: 'Necessário Até', value: data.needByDateUtc ? formatDate(data.needByDateUtc) : '---', icon: <Calendar size={12} /> },
        { label: 'Fornecedor Atual', value: data.supplierName },
        { 
            label: 'Atribuição Financeira', 
            value: (approvalStage === 'AREA' && isAreaApprovalStage) 
                ? (isUnifiedAssignment && unifiedCC ? `[${unifiedCC.code}] ${unifiedCC.name}` : 'Múltiplos / Pendente')
                : data.costCenterCode || (data.lineItems?.[0]?.costCenterCode ? `[${data.lineItems[0].costCenterCode}]` : '---'), 
            icon: <Landmark size={12} /> 
        },
        { 
            label: 'Grau Necessidade', 
            value: <span className="text-[#0a2540]">{data.needLevelName}</span> 
        }
    ];

    return (
        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.3 }}
            style={{ 
                backgroundColor: 'var(--color-bg-page)', 
                minHeight: '100%', display: 'flex', flexDirection: 'column', position: 'relative'
            }}
        >
            {/* 1. DECISION HEADER (Top Navigation & Hero) */}
            <DecisionHeader 
                requestNumber={data.requestNumber || ''}
                requestTypeCode={data.requestTypeCode || ''}
                statusCode={data.statusCode || ''}
                statusName={data.statusName || ''}
                statusBadgeColor={data.statusBadgeColor || ''}
                totalAmount={data.estimatedTotalAmount}
                currencyCode={data.currencyCode || ''}
                approvalStage={approvalStage}
                onClose={onClose}
                onOpenRequest={() => window.open(`/requests/${data.id}`, '_blank')}
                onNext={onNext}
                onPrev={onPrev}
                currentIndex={currentIndex}
                totalCount={totalCount}
            />

            {/* Content Container */}
            <div style={{ maxWidth: '80rem', margin: '0 auto', width: '100%', padding: '16px 24px 96px 24px' }}>

                {/* --- INTELLIGENCE ALERTS --- */}
                {hasHistoricalItems && (
                    <div style={{
                        marginBottom: '24px', width: '100%', 
                        backgroundColor: hasItemAboveAvg ? '#FEF9C3' : '#F0FDF4', 
                        border: `1px solid ${hasItemAboveAvg ? '#FEF08A' : '#BBF7D0'}`, 
                        borderRadius: 'var(--radius-lg)', padding: '16px',
                        display: 'flex', gap: '16px', boxShadow: 'var(--shadow-sm)', alignItems: 'flex-start'
                    }}>
                        {hasItemAboveAvg ? (
                            <AlertTriangle color="#A16207" style={{ marginTop: '2px', flexShrink: 0 }} size={20} />
                        ) : (
                            <ShieldCheck color="#166534" style={{ marginTop: '2px', flexShrink: 0 }} size={20} />
                        )}
                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                            <span style={{ color: hasItemAboveAvg ? '#854D0E' : '#14532D', fontWeight: 700, marginBottom: '4px' }}>
                                {hasItemAboveAvg ? 'Atenção ao Histórico de Preços' : 'Preços Favoráveis'}
                            </span>
                            <span style={{ color: hasItemAboveAvg ? '#A16207' : '#166534', fontSize: '0.875rem', lineHeight: 1.2 }}>
                                {hasItemAboveAvg 
                                    ? 'Um ou mais itens deste pedido estão com preço acima da média histórica.' 
                                    : 'Nenhum item com histórico no pedido encontra-se com preços acima da média.'}
                            </span>
                        </div>
                    </div>
                )}

                {/* --- BLOCKING ALERTS --- */}
                {isQuotation && !hasWinnerSelected && (
                    <div style={{
                        marginBottom: '24px', width: '100%', backgroundColor: '#FEF2F2',
                        border: '1px solid #FECACA', borderRadius: 'var(--radius-lg)', padding: '16px',
                        display: 'flex', gap: '16px', boxShadow: 'var(--shadow-sm)', alignItems: 'flex-start'
                    }}>
                        <AlertCircle color="var(--color-status-red)" style={{ marginTop: '2px', flexShrink: 0 }} size={20} />
                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                            <span style={{ color: '#991B1B', fontWeight: 700, marginBottom: '4px' }}>Cotação Vencedora Obrigatória</span>
                            <span style={{ color: '#B91C1C', fontSize: '0.875rem', lineHeight: 1.2 }}>
                                Uma cotação vencedora deve ser selecionada para prosseguir com a aprovação.
                            </span>
                        </div>
                    </div>
                )}
                
                {isAreaApprovalStage && !allAssigned && (
                    <div 
                        onClick={handleAllocationWarningClick}
                        style={{
                            marginBottom: '24px', width: '100%', backgroundColor: '#FEF2F2',
                            border: '1px solid #FECACA', borderRadius: 'var(--radius-lg)', padding: '16px',
                            display: 'flex', gap: '16px', boxShadow: 'var(--shadow-sm)', alignItems: 'flex-start',
                            cursor: 'pointer'
                        }}
                    >
                        <AlertTriangle color="var(--color-status-red)" style={{ marginTop: '2px', flexShrink: 0 }} size={20} />
                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                            <span style={{ color: '#991B1B', fontWeight: 700, marginBottom: '4px' }}>Pendência de Alocação</span>
                            <span style={{ color: '#B91C1C', fontSize: '0.875rem', lineHeight: 1.2 }}>
                                Existem itens sem centro de custo ou planta definida. A aprovação requer a regularização destes campos.
                            </span>
                        </div>
                    </div>
                )}

                {/* 2. RESUMO PARA DECISÃO (Always Open Grid Context) */}
                <div className="mb-8">
                    <DecisionSummaryGrid items={summaryItems} />
                </div>

                {/* 3. INTELIGÊNCIA PARA DECISÃO (Phase 4 - Horizontal Navigation) */}
                <DecisionSection 
                    title="Inteligência para Decisão" 
                    icon={<TrendingUp size={16} className="text-black" />}
                    isCollapsible={false}
                >
                    {loadingIntelligence ? (
                        <div style={{ padding: '40px 24px', textAlign: 'center', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)' }}>
                            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: '16px' }}>
                                <div style={{ width: '40px', height: '40px', border: '4px solid var(--color-text-main)', borderTopColor: 'transparent', borderRadius: '50%', animation: 'spin 1s linear infinite' }} />
                                <span style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.2em', color: 'var(--color-text-main)' }}>Analizando Histórico...</span>
                            </div>
                        </div>
                    ) : intelligence ? (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                            {/* Horizontal Tab Strip for Item Selection */}
                            {activeItems.length > 1 && (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', paddingBottom: '0', marginBottom: '16px', borderBottom: '1px solid var(--color-border)', width: '100%' }}>
                                    {activeItems.length > 5 && (
                                        <div style={{ padding: '0 4px', display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between' }}>
                                             <div style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>
                                                Analisar Item:
                                             </div>
                                             <div style={{ position: 'relative', width: '250px' }}>
                                                 <Target size={14} style={{ position: 'absolute', left: '12px', top: '50%', transform: 'translateY(-50%)', color: 'gray' }} />
                                                 <input 
                                                    type="text" 
                                                    placeholder="Buscar item..."
                                                    value={insightSearchQ}
                                                    onChange={(e) => setInsightSearchQ(e.target.value)}
                                                    style={{ width: '100%', fontSize: '0.75rem', padding: '6px 12px 6px 36px', border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-sm)', outline: 'none', fontWeight: 700, color: 'var(--color-text-main)' }}
                                                 />
                                             </div>
                                        </div>
                                    )}
                                    {activeItems.length <= 5 && (
                                        <div style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)', padding: '0 4px' }}>
                                            Analisar Item:
                                        </div>
                                    )}
                                    <div style={{ display: 'flex', alignItems: 'flex-end', gap: '8px', overflowX: 'auto', scrollBehavior: 'smooth' }}>
                                        {activeItems.filter(item => item.description.toLowerCase().includes(insightSearchQ.toLowerCase())).map((item) => {
                                            const isActive = activeInsightItemId === item.id;
                                            
                                            // Status cue logic
                                            const assignment = itemAssignments[item.id];
                                            const isAssigned = assignment?.plantId && assignment?.costCenterId;
                                            
                                            return (
                                                <button
                                                    key={item.id}
                                                    onClick={() => setActiveInsightItemId(item.id)}
                                                    style={{
                                                        flexShrink: 0, display: 'flex', flexDirection: 'column', alignItems: 'flex-start', padding: '0 16px 12px 16px', transition: 'all 0.2s', outline: 'none', cursor: 'pointer', background: 'none', border: 'none', borderBottom: isActive ? '2px solid var(--color-primary)' : '2px solid transparent',
                                                        color: isActive ? 'var(--color-primary)' : 'var(--color-text-muted)', minWidth: '160px', maxWidth: '220px'
                                                    }}
                                                >
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', width: '100%', marginBottom: '4px' }}>
                                                        <span style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                                            Item #{item.lineNumber}
                                                        </span>
                                                        <div style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: isAssigned ? 'var(--color-status-green)' : 'var(--color-status-red)' }} />
                                                    </div>
                                                    <div style={{ fontSize: '0.75rem', width: '100%', textAlign: 'left', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', transition: 'opacity 0.2s', fontWeight: isActive ? 900 : 700, opacity: isActive ? 1 : 0.7 }}>
                                                        {item.description}
                                                    </div>
                                                </button>
                                            );
                                        })}
                                    </div>
                                </div>
                            )}

                            <DecisionInsightsPanel 
                                intelligence={{
                                    ...intelligence,
                                    // Only pass the intelligence for the selected item to focus the UI
                                    items: intelligence.items?.filter(i => {
                                        const originalItem = activeItems.find(ai => ai.id === activeInsightItemId);
                                        return i.description === originalItem?.description;
                                    }) || []
                                }} 
                                approvalStage={approvalStage}
                                onDrillDown={onDrillDown}
                                requestData={{
                                    description: data.description,
                                    supplierName: data.supplierName,
                                    costCenterCode: data.costCenterCode,
                                    requestTypeCode: data.requestTypeCode,
                                    hasQuotations: (data.quotations?.length || 0) > 0
                                }}
                                isSingleItemFocus={activeItems.length > 1}
                            />
                        </div>
                    ) : (
                        <div style={{ padding: '32px', textAlign: 'center', backgroundColor: 'var(--color-bg-page)', border: '2px dashed var(--color-border)' }}>
                             <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '12px' }}>
                                <Target style={{ color: 'var(--color-border-heavy)' }} size={32} />
                                <span style={{ fontSize: '0.625rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Dados de inteligência não disponíveis</span>
                             </div>
                        </div>
                    )}
                </DecisionSection>

                {/* 4. ITENS DO PEDIDO (Adaptive Navigation) */}
                <motion.div
                    id="itens-do-pedido-section"
                    animate={
                        highlightSection
                            ? {
                                  boxShadow: ['0 0 0px 0px transparent', '0 0 15px 5px rgba(239, 68, 68, 0.4)', '0 0 0px 0px transparent'],
                                  transition: { duration: 1, repeat: 4 }
                              }
                            : { boxShadow: '0 0 0px 0px transparent' }
                    }
                    style={{ borderRadius: 'var(--radius-lg)' }}
                >
                    <DecisionSection 
                        title="Itens do pedido" 
                        icon={<List size={16} />}
                        count={activeItems.length}
                        isCollapsible={true}
                        defaultOpen={true}
                        noPadding={viewMode === 'LIST'}
                        headerRight={
                        <div style={{ display: 'flex', backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-md)', padding: '4px', marginLeft: 'auto', flexShrink: 0 }}>
                            <button
                                onClick={(e) => { e.stopPropagation(); setViewMode('CARDS'); }}
                                style={{
                                    padding: '6px 12px', borderRadius: '4px', fontSize: '0.625rem', textTransform: 'uppercase', fontWeight: 700, letterSpacing: '0.05em', transition: 'all 0.2s', border: 'none',
                                    ...(viewMode === 'CARDS' ? { backgroundColor: 'white', boxShadow: 'var(--shadow-sm)', color: 'var(--color-text-main)' } : { backgroundColor: 'transparent', color: 'var(--color-text-muted)', cursor: 'pointer' })
                                }}
                            >
                                Cards
                            </button>
                            <button
                                onClick={(e) => { e.stopPropagation(); setViewMode('LIST'); }}
                                style={{
                                    padding: '6px 12px', borderRadius: '4px', fontSize: '0.625rem', textTransform: 'uppercase', fontWeight: 700, letterSpacing: '0.05em', transition: 'all 0.2s', border: 'none',
                                    ...(viewMode === 'LIST' ? { backgroundColor: 'white', boxShadow: 'var(--shadow-sm)', color: 'var(--color-text-main)' } : { backgroundColor: 'transparent', color: 'var(--color-text-muted)', cursor: 'pointer' })
                                }}
                            >
                                Lista
                            </button>
                        </div>
                    }
                >
                    {activeItems.length === 0 ? (
                        <div style={{ padding: '48px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                            Nenhum item encontrado.
                        </div>
                    ) : viewMode === 'CARDS' ? (
                        /* --- CARDS MODE --- */
                        <div style={{ display: 'flex', gap: '16px', overflowX: 'auto', paddingBottom: '16px', paddingTop: '4px', scrollBehavior: 'smooth' }}>
                            {activeItems.map((item) => {
                                const assignment = itemAssignments[item.id] || { plantId: null, costCenterId: null };
                                const isResolved = assignment.plantId && assignment.costCenterId;

                                return (
                                    <div 
                                        key={item.id}
                                        style={{
                                            flexShrink: 0, width: '340px', display: 'flex', flexDirection: 'column', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', overflow: 'hidden', boxShadow: 'var(--shadow-sm)', transition: 'box-shadow 0.2s'
                                        }}
                                    >
                                        <div style={{ padding: '20px', display: 'flex', flexDirection: 'column', flexGrow: 1 }}>
                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '16px' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                    <span style={{ fontSize: '0.625rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)', marginBottom: '4px' }}>
                                                        Item #{item.lineNumber}
                                                    </span>
                                                    <span style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)', lineHeight: 1.2 }}>
                                                        {item.description}
                                                    </span>
                                                </div>
                                                <div style={{ width: '12px', height: '12px', borderRadius: '50%', marginTop: '4px', flexShrink: 0, backgroundColor: isResolved ? 'var(--color-status-green)' : 'var(--color-status-red)' }} />
                                            </div>

                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', marginBottom: '24px' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                    <span style={{ fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', fontSize: '0.625rem' }}>Quantidade</span>
                                                    <span style={{ fontWeight: 900, color: 'var(--color-text-main)', fontSize: '0.875rem' }}>{item.quantity} {item.unit || 'UN'}</span>
                                                </div>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', alignItems: 'flex-end' }}>
                                                    <span style={{ fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', fontSize: '0.625rem' }}>Total</span>
                                                    <span style={{ fontWeight: 900, color: 'var(--color-text-main)', fontSize: '0.875rem' }}>{formatCurrencyAO(item.totalAmount)}</span>
                                                </div>
                                            </div>

                                            {approvalStage === 'AREA' && isAreaApprovalStage ? (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', marginTop: 'auto', paddingTop: '16px', borderTop: '1px solid var(--color-border)' }}>
                                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                                        <label style={{ fontSize: '0.5625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Planta <span style={{ color: 'var(--color-status-red)' }}>*</span></label>
                                                        <select
                                                            value={assignment.plantId || ''}
                                                            onChange={(e) => setItemAssignments(prev => ({ 
                                                                ...prev, [item.id]: { plantId: parseInt(e.target.value) || null, costCenterId: null } 
                                                            }))}
                                                            style={{ 
                                                                width: '100%', fontSize: '0.75rem', fontWeight: 700, backgroundColor: 'var(--color-bg-page)', 
                                                                border: (highlightFields && !assignment.plantId) ? '2px solid var(--color-status-red)' : '1px solid var(--color-border)', 
                                                                borderRadius: 'var(--radius-md)', padding: '10px', outline: 'none' 
                                                            }}
                                                        >
                                                            <option value="">Selecionar...</option>
                                                            {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                                                        </select>
                                                    </div>
                                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                                        <label style={{ fontSize: '0.5625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Centro de Custo <span style={{ color: 'var(--color-status-red)' }}>*</span></label>
                                                        <select
                                                            value={assignment.costCenterId || ''}
                                                            disabled={!assignment.plantId}
                                                            onChange={(e) => setItemAssignments(prev => ({ 
                                                                ...prev, [item.id]: { ...prev[item.id], costCenterId: parseInt(e.target.value) || null } 
                                                            }))}
                                                            style={{ 
                                                                width: '100%', fontSize: '0.75rem', fontWeight: 700, backgroundColor: 'var(--color-bg-page)', 
                                                                border: (highlightFields && !assignment.costCenterId) ? '2px solid var(--color-status-red)' : '1px solid var(--color-border)', 
                                                                borderRadius: 'var(--radius-md)', padding: '10px', outline: 'none', opacity: assignment.plantId ? 1 : 0.5 
                                                            }}
                                                        >
                                                            <option value="">{assignment.plantId ? 'Selecionar...' : 'Exige Planta'}</option>
                                                            {costCenters.filter(cc => cc.plantId === assignment.plantId).map(cc => (
                                                                <option key={cc.id} value={cc.id}>[{cc.code}] {cc.name}</option>
                                                            ))}
                                                        </select>
                                                    </div>
                                                    {canBulkFill(item.id) && (() => {
                                                        const pendingCount = activeItems.filter(i => i.id !== item.id && (!itemAssignments[i.id]?.plantId || !itemAssignments[i.id]?.costCenterId)).length;
                                                        return (
                                                            <Tooltip content="Aplica a mesma Planta e Centro de Custo aos demais itens desta lista com status pendente" variant="dark" side="top">
                                                                <button 
                                                                    onClick={() => handleBulkFill(item.id)}
                                                                    style={{ marginTop: '8px', width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px', padding: '8px 12px', backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-muted)', borderRadius: 'var(--radius-md)', fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', border: 'none', cursor: 'pointer' }}
                                                                >
                                                                    Aplicar a {pendingCount} pendentes
                                                                </button>
                                                            </Tooltip>
                                                        );
                                                    })()}
                                                </div>
                                            ) : (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: 'auto', paddingTop: '16px', borderTop: '1px solid var(--color-border)' }}>
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.75rem' }}>
                                                        <span style={{ fontWeight: 700, color: 'var(--color-text-muted)' }}>Planta</span>
                                                        <span style={{ fontWeight: 900, color: 'var(--color-text-main)' }}>{item.plantName || '---'}</span>
                                                    </div>
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.75rem' }}>
                                                        <span style={{ fontWeight: 700, color: 'var(--color-text-muted)' }}>Centro de Custo</span>
                                                        <span style={{ fontWeight: 900, color: 'var(--color-text-main)' }}>{item.costCenterCode ? `[${item.costCenterCode}]` : '---'}</span>
                                                    </div>
                                                </div>
                                            )}
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    ) : (
                        /* --- LIST MODE (Master-Detail Replacement) --- */
                        <div style={{ display: 'flex', flexDirection: 'column', borderTop: '1px solid var(--color-border)' }}>
                            {activeItems.map((item) => {
                                const assignment = itemAssignments[item.id] || { plantId: null, costCenterId: null };
                                const isResolved = assignment.plantId && assignment.costCenterId;

                                return (
                                    <div key={item.id} style={{ padding: '24px', display: 'flex', gap: '24px', borderBottom: '1px solid var(--color-border)', flexWrap: 'wrap' }}>
                                        <div style={{ flex: '1 1 0%', display: 'flex', flexDirection: 'column', minWidth: 0 }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '8px' }}>
                                                <div style={{ width: '10px', height: '10px', borderRadius: '50%', flexShrink: 0, backgroundColor: isResolved ? 'var(--color-status-green)' : 'var(--color-status-red)' }} />
                                                <span style={{ fontSize: '0.625rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Item #{item.lineNumber}</span>
                                            </div>
                                            <h4 style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)', marginBottom: '12px', paddingRight: '16px' }}>{item.description}</h4>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '24px', marginTop: 'auto' }}>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                    <span style={{ fontSize: '0.5625rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Quantidade</span>
                                                    <span style={{ fontSize: '0.75rem', fontWeight: 900, color: 'var(--color-text-main)' }}>{item.quantity} {item.unit || 'UN'}</span>
                                                </div>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                                    <span style={{ fontSize: '0.5625rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Total Estimado</span>
                                                    <span style={{ fontSize: '0.75rem', fontWeight: 900, color: 'var(--color-text-main)' }}>{formatCurrencyAO(item.totalAmount)}</span>
                                                </div>
                                            </div>
                                        </div>

                                        <div style={{ width: '100%', maxWidth: '400px', flexShrink: 0, backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', padding: '16px', boxShadow: 'var(--shadow-sm)', position: 'relative' }}>
                                            {approvalStage === 'AREA' && isAreaApprovalStage ? (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                                            <label style={{ fontSize: '0.5625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Planta</label>
                                                            <select
                                                                value={assignment.plantId || ''}
                                                                onChange={(e) => setItemAssignments(prev => ({ 
                                                                    ...prev, [item.id]: { plantId: parseInt(e.target.value) || null, costCenterId: null } 
                                                                }))}
                                                                style={{ 
                                                                    width: '100%', fontSize: '0.75rem', fontWeight: 700, backgroundColor: 'var(--color-bg-page)', 
                                                                    border: (highlightFields && !assignment.plantId) ? '2px solid var(--color-status-red)' : '1px solid var(--color-border)', 
                                                                    borderRadius: 'var(--radius-md)', padding: '8px', outline: 'none' 
                                                                }}
                                                            >
                                                                <option value="">Selecionar...</option>
                                                                {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                                                            </select>
                                                        </div>
                                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                                            <label style={{ fontSize: '0.5625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>C. Custo</label>
                                                            <select
                                                                value={assignment.costCenterId || ''}
                                                                disabled={!assignment.plantId}
                                                                onChange={(e) => setItemAssignments(prev => ({ 
                                                                    ...prev, [item.id]: { ...prev[item.id], costCenterId: parseInt(e.target.value) || null } 
                                                                }))}
                                                                style={{ 
                                                                    width: '100%', fontSize: '0.75rem', fontWeight: 700, backgroundColor: 'var(--color-bg-page)', 
                                                                    border: (highlightFields && !assignment.costCenterId) ? '2px solid var(--color-status-red)' : '1px solid var(--color-border)', 
                                                                    borderRadius: 'var(--radius-md)', padding: '8px', outline: 'none', opacity: assignment.plantId ? 1 : 0.5 
                                                                }}
                                                            >
                                                                <option value="">{assignment.plantId ? 'Selecionar...' : '---'}</option>
                                                                {costCenters.filter(cc => cc.plantId === assignment.plantId).map(cc => (
                                                                    <option key={cc.id} value={cc.id}>[{cc.code}] {cc.name}</option>
                                                                ))}
                                                            </select>
                                                        </div>
                                                    </div>
                                                    {canBulkFill(item.id) && (() => {
                                                        const pendingCount = activeItems.filter(i => i.id !== item.id && (!itemAssignments[i.id]?.plantId || !itemAssignments[i.id]?.costCenterId)).length;
                                                        return (
                                                            <Tooltip content="Aplica a mesma Planta e Centro de Custo aos demais itens desta lista com status pendente" variant="dark" side="top">
                                                                <button 
                                                                    onClick={() => handleBulkFill(item.id)}
                                                                    style={{ width: '100%', marginTop: '4px', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px', padding: '6px 12px', backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-muted)', borderRadius: 'var(--radius-sm)', fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', transition: 'all 0.2s', border: 'none', cursor: 'pointer' }}
                                                                >
                                                                    Aplicar aos {pendingCount} compatíveis
                                                                </button>
                                                            </Tooltip>
                                                        );
                                                    })()}
                                                </div>
                                            ) : (
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', justifyContent: 'center', height: '100%' }}>
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem' }}>
                                                        <span style={{ fontWeight: 700, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>Planta</span>
                                                        <span style={{ fontWeight: 900, color: 'var(--color-text-main)' }}>{item.plantName || '---'}</span>
                                                    </div>
                                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.875rem' }}>
                                                        <span style={{ fontWeight: 700, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>C. Custo</span>
                                                        <span style={{ fontWeight: 900, color: 'var(--color-text-main)' }}>{item.costCenterCode ? `[${item.costCenterCode}] ${item.costCenterName}` : '---'}</span>
                                                    </div>
                                                </div>
                                            )}
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    )}
                </DecisionSection>
                </motion.div>

                {/* 5. JUSTIFICATIVA / OBSERVAÇÕES (Always Open if present) */}
                {data.description && (
                    <DecisionSection 
                        title="Justificativa / Observações" 
                        icon={<MessageSquare size={16} />}
                        isCollapsible={false}
                    >
                        <p style={{ margin: 0, fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', lineHeight: 1.6, padding: '20px', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', whiteSpace: 'pre-wrap' }}>
                            {data.description}
                        </p>
                    </DecisionSection>
                )}

                {/* 6. COTAÇÕES SALVAS (Always Open for Quotation types) */}
                {isQuotation && (
                    <DecisionSection 
                        title="Cotações Salvas" 
                        icon={<DollarSign size={16} />}
                        count={data.quotations?.length || 0}
                        isCollapsible={false}
                    >
                        {(data.quotations?.length || 0) === 0 ? (
                            <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                                Nenhuma cotação registrada.
                            </div>
                        ) : (
                            <div>
                                {data.quotations?.map(q => (
                                    <DecisionQuotationCard 
                                        key={q.id}
                                        quotation={q}
                                        isLowest={data.quotations.length > 1 && q.totalAmount === lowestByCurrency[q.currency]}
                                        canSelectWinner={canSelectWinner}
                                        onSelectWinner={handleSelectWinner}
                                        isProcessing={quotationProcessingId === q.id}
                                    />
                                ))}
                            </div>
                        )}
                    </DecisionSection>
                )}

                {/* 7. RESUMO FINANCEIRO (Collapsible) */}
                <DecisionSection 
                    title="Resumo financeiro" 
                    icon={<DollarSign size={16} />}
                    isCollapsible={true}
                    defaultOpen={false}
                >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', padding: '24px', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Custo Estimado Total</span>
                            <span style={{ fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-text-main)', letterSpacing: '-0.02em' }}>
                                {formatCurrencyAO(data.estimatedTotalAmount)} <span style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', fontWeight: 700, marginLeft: '4px' }}>{data.currencyCode}</span>
                            </span>
                        </div>
                        {data.supplierPortalCode && (
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderTop: '1px solid var(--color-border)', paddingTop: '16px' }}>
                                <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Código do Fornecedor</span>
                                <span style={{ fontSize: '0.875rem', fontWeight: 900, fontFamily: 'monospace', backgroundColor: 'var(--color-bg-page)', padding: '4px 10px', borderRadius: 'var(--radius-sm)', color: 'var(--color-text-main)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}>{data.supplierPortalCode}</span>
                            </div>
                        )}
                    </div>
                </DecisionSection>

                {/* 8. ANEXOS (Collapsible) */}
                <DecisionSection 
                    title="Anexos" 
                    icon={<Paperclip size={16} />}
                    count={data.attachments?.length || 0}
                    isCollapsible={true}
                    defaultOpen={false}
                    noPadding={true}
                >
                    {(data.attachments?.length || 0) === 0 ? (
                        <div style={{ padding: '32px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                            Nenhum anexo registrado.
                        </div>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                            {data.attachments.map((att) => (
                                <div key={att.id} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '20px', borderBottom: '1px solid var(--color-border)' }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px', flex: 1, minWidth: 0 }}>
                                        <div style={{ width: '40px', height: '40px', backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-sm)', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, border: '1px solid var(--color-border)' }}>
                                            <Paperclip size={18} color="var(--color-text-muted)" />
                                        </div>
                                        <div style={{ minWidth: 0 }}>
                                            <div style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', marginBottom: '4px' }}>
                                                {att.fileName}
                                            </div>
                                            <div style={{ fontSize: '0.625rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                                {ATTACHMENT_TYPE_LABELS[att.attachmentTypeCode] || att.attachmentTypeCode} • Por {att.uploadedByName}
                                            </div>
                                        </div>
                                    </div>
                                    <button
                                        onClick={() => handleDownloadAttachment(att.id, att.fileName)}
                                        style={{ marginLeft: '16px', padding: '8px', backgroundColor: 'white', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', color: 'var(--color-text-muted)', boxShadow: 'var(--shadow-sm)', cursor: 'pointer', flexShrink: 0 }}
                                    >
                                        <Download size={16} />
                                    </button>
                                </div>
                            ))}
                        </div>
                    )}
                </DecisionSection>

                {/* 9. PARTICIPANTES DO FLUXO (Collapsible) */}
                <DecisionSection 
                    title="Participantes do fluxo" 
                    icon={<Users size={16} />}
                    isCollapsible={true}
                    defaultOpen={false}
                >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        {[
                            { label: 'Solicitante', name: data.requesterName, role: 'Buyer/User' },
                            { label: 'Comprador Atribuído', name: data.buyerName, role: 'Procurement' },
                            { label: 'Aprovador da Área', name: data.areaApproverName, role: 'Area Manager' },
                            { label: 'Aprovador Final', name: data.finalApproverName, role: 'C-Level / Admin' }
                        ].map((p, idx) => (
                            <div key={idx} style={{ display: 'flex', alignItems: 'center', gap: '16px', padding: '16px', backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-lg)', border: '1px solid var(--color-border)' }}>
                                <div style={{ width: '40px', height: '40px', borderRadius: '50%', backgroundColor: 'var(--color-text-main)', color: 'white', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.875rem', fontWeight: 900, flexShrink: 0 }}>
                                    {p.name?.substring(0, 2).toUpperCase() || '??'}
                                </div>
                                <div style={{ minWidth: 0 }}>
                                    <div style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{p.name || 'Pendente'}</div>
                                    <div style={{ fontSize: '0.625rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', marginTop: '4px' }}>{p.label}</div>
                                </div>
                            </div>
                        ))}
                    </div>
                </DecisionSection>

                {/* 10. HISTÓRICO DO PEDIDO (Collapsible) */}
                <DecisionSection 
                    title="Histórico do pedido" 
                    icon={<HistoryIcon size={16} />}
                    count={data.statusHistory?.length || 0}
                    isCollapsible={true}
                    defaultOpen={false}
                >
                    <DecisionTimeline entries={data.statusHistory} />
                </DecisionSection>
            </div>

            {/* Sticky Action Footer */}
            <div style={{ position: 'sticky', bottom: 0, zIndex: 50, backgroundColor: 'var(--color-bg-page)', borderTop: '1px solid var(--color-border)', padding: '16px 24px', display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: '12px', boxShadow: '0 -4px 6px -1px rgba(0,0,0,0.05)', width: '100%' }}>
                {showAdjustmentAction && (
                    <button
                        onClick={() => setShowApprovalModal({ show: true, type: 'REQUEST_ADJUSTMENT' })}
                        style={{ padding: '12px 24px', backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-main)', fontWeight: 700, border: '1px solid var(--color-border)', borderRadius: 'var(--radius-lg)', display: 'flex', alignItems: 'center', gap: '8px', textTransform: 'uppercase', letterSpacing: '0.05em', fontSize: '0.75rem', cursor: 'pointer', transition: 'background-color 0.2s' }}
                        onMouseEnter={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'}
                        onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'}
                    >
                        <ArrowRightLeft size={16} /> Reajuste
                    </button>
                )}
                <button
                    onClick={() => setShowApprovalModal({ show: true, type: 'REJECT' })}
                    style={{ padding: '12px 24px', backgroundColor: 'var(--color-bg-page)', color: 'var(--color-status-red)', fontWeight: 700, border: '1px solid rgba(239, 68, 68, 0.2)', borderRadius: 'var(--radius-lg)', display: 'flex', alignItems: 'center', gap: '8px', textTransform: 'uppercase', letterSpacing: '0.05em', fontSize: '0.75rem', cursor: 'pointer', transition: 'background-color 0.2s' }}
                    onMouseEnter={(e) => e.currentTarget.style.backgroundColor = 'rgba(239, 68, 68, 0.05)'}
                    onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'}
                >
                    <AlertTriangle size={16} /> Rejeitar
                </button>
                <button
                    onClick={() => setShowApprovalModal({ show: true, type: 'APPROVE' })}
                    disabled={isApproveBlocked}
                    style={{
                        padding: '12px 32px', borderRadius: 'var(--radius-lg)', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', textTransform: 'uppercase', letterSpacing: '0.05em', fontSize: '0.75rem', transition: 'colors 0.2s', border: 'none',
                        ...(isApproveBlocked 
                            ? { backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-muted)', cursor: 'not-allowed' } 
                            : { backgroundColor: 'var(--color-text-main)', color: 'white', cursor: 'pointer' })
                    }}
                >
                    <ShieldCheck size={16} /> Aprovar
                </button>
            </div>

            {/* APPROVAL MODAL */}
            <ApprovalModal
                show={showApprovalModal.show}
                type={showApprovalModal.type}
                status={data.statusCode || ''}
                onClose={() => {
                    setShowApprovalModal({ show: false, type: null });
                    setApprovalComment('');
                    setModalFeedback({ type: 'error', message: null });
                }}
                onConfirm={handleApprovalAction}
                comment={approvalComment}
                setComment={setApprovalComment}
                processing={approvalProcessing}
                feedback={modalFeedback}
                onCloseFeedback={() => setModalFeedback({ type: 'error', message: null })}
                selectedQuotationName={selectedQuotation?.supplierNameSnapshot || null}
            />
        </motion.div>
    );
}
