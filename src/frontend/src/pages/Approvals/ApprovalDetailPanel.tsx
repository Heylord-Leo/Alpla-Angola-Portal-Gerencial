import { useState, useEffect } from 'react';
import {
    User, Building2, Factory,
    Calendar, Landmark, Download,
    Paperclip, AlertCircle, List, FileText, 
    MessageSquare, Users, History as HistoryIcon, DollarSign,
    Target, TrendingUp, CheckCircle
} from 'lucide-react';
import { RequestDetailsDto, ApprovalIntelligenceDto } from '../../types';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { FeedbackType } from '../../components/ui/Feedback';
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

    // Phase 3A: Intelligence
    const [intelligence, setIntelligence] = useState<ApprovalIntelligenceDto | null>(null);
    const [loadingIntelligence, setLoadingIntelligence] = useState(false);

    // Business Control: Cost Center Selection (DEC-085/DEC-099)
    const [costCenters, setCostCenters] = useState<any[]>([]);
    const [itemCostCenters, setItemCostCenters] = useState<Record<string, number | null>>({});

    useEffect(() => {
        if (data.id) {
            fetchIntelligence();
        }
    }, [data.id]);

    useEffect(() => {
        if (approvalStage === 'AREA' && (data.plantId || data.companyId)) {
            // Fetch all CCs for the company to support cross-plant items if needed
            fetchCostCenters(data.companyId);
        }
    }, [approvalStage, data.companyId, data.plantId]);

    // Initialize item cost centers from existing data
    useEffect(() => {
        if (data.lineItems) {
            const initialMap: Record<string, number | null> = {};
            data.lineItems.forEach(item => {
                initialMap[item.id] = item.costCenterId || null;
            });
            setItemCostCenters(initialMap);
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
            // Fetching by companyId allows the UI to filter per-item based on plantId
            const list = await api.lookups.getCostCenters(false, undefined, companyId);
            setCostCenters(list);
        } catch (err) {
            console.error('Failed to fetch cost centers:', err);
        }
    };

    // --- Computed ---
    const isQuotation = data.requestTypeCode === 'QUOTATION';
    const isPayment = data.requestTypeCode === 'PAYMENT';
    const selectedQuotation = data.quotations?.find(q => q.isSelected);
    const hasWinnerSelected = !!selectedQuotation;

    const isAreaApprovalStage = data.statusCode === 'WAITING_AREA_APPROVAL';
    const isFinalApprovalStage = data.statusCode === 'WAITING_FINAL_APPROVAL';

    // DEC-099: Cost Center Validation Logic
    const activeItems = data.lineItems || [];
    const pendingItems = activeItems.filter(item => !itemCostCenters[item.id]);
    const pendingCount = pendingItems.length;
    const allCostCentersAssigned = activeItems.length > 0 && pendingCount === 0;
    
    // A simplified helper to find if all items currently share the same CC (for summary display)
    const currentValues = Object.values(itemCostCenters).filter((v): v is number => v !== null);
    const currentUniqueCCIds = Array.from(new Set(currentValues));
    const isUnifiedCC = activeItems.length > 0 && currentUniqueCCIds.length === 1 && allCostCentersAssigned;
    const unifiedCC = isUnifiedCC ? costCenters.find(cc => cc.id === currentUniqueCCIds[0]) : null;

    const handleBulkFillCostCenter = (itemId: string) => {
        const sourceCCId = itemCostCenters[itemId];
        if (!sourceCCId) return;
        
        const sourceItem = activeItems.find(i => i.id === itemId);
        if (!sourceItem) return;

        const newMap = { ...itemCostCenters };
        activeItems.forEach(item => {
            // Apply only to unassigned items of the exact same plant
            if (item.plantId === sourceItem.plantId && !newMap[item.id]) {
                newMap[item.id] = sourceCCId;
            }
        });
        setItemCostCenters(newMap);
    };

    const canBulkFillForItem = (itemId: string): boolean => {
        const sourceCCId = itemCostCenters[itemId];
        if (!sourceCCId) return false;
        
        const sourceItem = activeItems.find(i => i.id === itemId);
        if (!sourceItem) return false;

        // True if there is at least one OTHER unassigned item for the SAME plant
        return pendingItems.some(item => item.id !== itemId && item.plantId === sourceItem.plantId);
    };

    useEffect(() => {
        if (approvalStage === 'AREA' && data.companyId) {
            fetchCostCenters(data.companyId);
        }
    }, [approvalStage, data.companyId]);

    // Winner selection is enabled for QUOTATION requests when the approver is in their decision stage
    const canSelectWinner = isQuotation && (
        (approvalStage === 'AREA' && isAreaApprovalStage) ||
        (approvalStage === 'FINAL' && isFinalApprovalStage)
    );

    // Approve is blocked if winner not selected (Quotation) OR any Cost Center is missing (Area Approval)
    const isApproveBlocked = (isQuotation && !hasWinnerSelected) || (approvalStage === 'AREA' && isAreaApprovalStage && !allCostCentersAssigned);

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

    // --- Handlers ---

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
            // DEC-099: item-level Cost Center alignment
            if (approvalStage === 'AREA' && !allCostCentersAssigned) {
                setModalFeedback({ 
                    type: 'error', 
                    message: activeItems.length > 1 
                        ? 'Todos os itens devem ter um Centro de Custo atribuído antes de aprovar.' 
                        : 'A seleção do Centro de Custo é obrigatória para prosseguir.' 
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
                    ? await api.requests.approveArea(data.id, approvalComment, data.quotations?.find(q => q.isSelected)?.id, itemCostCenters)
                    : await api.requests.approveFinal(data.id, approvalComment);
            } else if (action === 'REJECT') {
                result = isArea
                    ? await api.requests.rejectArea(data.id, approvalComment)
                    : await api.requests.rejectFinal(data.id, approvalComment);
            } else if (action === 'REQUEST_ADJUSTMENT') {
                result = isArea
                    ? await api.requests.requestAdjustmentArea(data.id, approvalComment)
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
            label: 'Centro de Custo', 
            value: (approvalStage === 'AREA' && isAreaApprovalStage) ? (
                activeItems.length === 1 ? (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        <select
                            value={itemCostCenters[activeItems[0].id] || ''}
                            onChange={(e) => setItemCostCenters(prev => ({ ...prev, [activeItems[0].id]: parseInt(e.target.value) || null }))}
                            className="w-full text-xs font-black uppercase text-black"
                            style={{
                                padding: '6px 10px',
                                border: itemCostCenters[activeItems[0].id] ? '2px solid black' : '2px solid var(--color-status-red)',
                                borderRadius: '0',
                                backgroundColor: 'white',
                                outline: 'none',
                                cursor: 'pointer',
                                appearance: 'auto',
                                boxShadow: itemCostCenters[activeItems[0].id] ? 'none' : '2px 2px 0px var(--color-status-red-bg)'
                            }}
                        >
                            <option value="">Selecione...</option>
                            {costCenters.filter(cc => cc.plantId === activeItems[0].plantId).map(cc => (
                                <option key={cc.id} value={cc.id}>
                                    [{cc.code}] {cc.name}
                                </option>
                            ))}
                        </select>
                        {!itemCostCenters[activeItems[0].id] ? (
                            <div className="flex items-center gap-1 text-[9px] font-black tracking-wider text-red-600 uppercase">
                                <AlertCircle size={10} /> Obrigatório
                            </div>
                        ) : (
                            <div className="flex items-center gap-1 text-[9px] font-black tracking-wider text-green-600 uppercase">
                                <CheckCircle size={10} /> Validado
                            </div>
                        )}
                    </div>
                ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                        <div style={{ 
                            padding: '10px', fontSize: '0.75rem', fontWeight: 800, 
                            backgroundColor: !allCostCentersAssigned ? 'var(--color-status-red-bg)' : (isUnifiedCC ? 'var(--color-status-green-bg)' : 'var(--color-bg-page)'), 
                            border: !allCostCentersAssigned ? '2px solid var(--color-status-red)' : '1.5px solid black', 
                            color: !allCostCentersAssigned ? 'var(--color-status-red)' : 'black'
                        }}>
                            {!allCostCentersAssigned 
                                ? `Pendente (${pendingCount} item${pendingCount > 1 ? 's' : ''} sem centro de custo)` 
                                : (isUnifiedCC && unifiedCC ? `[${unifiedCC.code}] ${unifiedCC.name}` : 'Mapeados individualmente (Ver Itens)')}
                        </div>
                        <div style={{ fontSize: '9px', fontWeight: 900, textTransform: 'uppercase', opacity: 0.6 }}>
                            {allCostCentersAssigned ? '✓ Todos itens atribuídos' : '⚠ Verifique Detalhes nos Itens'}
                        </div>
                    </div>
                )
            ) : data.costCenterCode || (data.lineItems?.[0]?.costCenterCode ? `[${data.lineItems[0].costCenterCode}]` : '---'), 
            icon: <Landmark size={12} /> 
        },
        { label: 'Grau Necessidade', value: data.needLevelName }
    ];

    return (
        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.3 }}
            style={{ backgroundColor: 'var(--color-bg-page)', minHeight: '100%' }}
        >
            {/* 1. DECISION HEADER (Sticky) */}
            <DecisionHeader 
                requestNumber={data.requestNumber || ''}
                requestTypeCode={data.requestTypeCode || ''}
                statusCode={data.statusCode || ''}
                statusName={data.statusName || ''}
                statusBadgeColor={data.statusBadgeColor || ''}
                totalAmount={data.estimatedTotalAmount}
                currencyCode={data.currencyCode || ''}
                approvalStage={approvalStage}
                onAction={(type) => setShowApprovalModal({ show: true, type })}
                onClose={onClose}
                onOpenRequest={() => window.open(`/requests/${data.id}`, '_blank')}
                isApproveBlocked={isApproveBlocked}
                showAdjustmentAction={showAdjustmentAction}
                onNext={onNext}
                onPrev={onPrev}
                currentIndex={currentIndex}
                totalCount={totalCount}
            />

            {/* Content Container */}
            <div style={{ padding: '24px 32px' }}>
                
                {/* 2. RESUMO PARA DECISÃO (Always Open) */}
                <DecisionSection 
                    title="Resumo para decisão" 
                    icon={<FileText size={16} />}
                    isCollapsible={false}
                >
                    <DecisionSummaryGrid items={summaryItems} />
                </DecisionSection>

                {/* 3. INTELIGÊNCIA PARA DECISÃO (Phase 3A - Always Open) */}
                <DecisionSection 
                    title="Inteligência para Decisão" 
                    icon={<TrendingUp size={16} className="text-black" />}
                    isCollapsible={false}
                >
                    {loadingIntelligence ? (
                        <div style={{ 
                            padding: '40px 24px', 
                            textAlign: 'center', 
                            backgroundColor: 'white', 
                            border: '1.5px solid black' 
                        }}>
                            <div className="flex flex-col items-center justify-center gap-4">
                                <div className="w-10 h-10 rounded-none border-4 border-black border-t-transparent animate-spin" />
                                <span className="text-[10px] font-black uppercase tracking-[0.2em] text-black">Analizando Histórico...</span>
                            </div>
                        </div>
                    ) : intelligence ? (
                        <DecisionInsightsPanel 
                            intelligence={intelligence} 
                            approvalStage={approvalStage}
                            onDrillDown={onDrillDown}
                            requestData={{
                                description: data.description,
                                supplierName: data.supplierName,
                                costCenterCode: data.costCenterCode,
                                requestTypeCode: data.requestTypeCode,
                                hasQuotations: (data.quotations?.length || 0) > 0
                            }}
                        />
                    ) : (
                        <div className="p-8 text-center bg-gray-50 border-2 border-dashed border-gray-300">
                             <div className="flex flex-col items-center gap-3">
                                <Target className="w-8 h-8 text-gray-300" />
                                <span className="text-[10px] font-bold uppercase tracking-widest text-gray-400">Dados de inteligência não disponíveis</span>
                             </div>
                        </div>
                    )}
                </DecisionSection>

                {/* 4. ALERTAS / PENDÊNCIAS (Always Open if present) */}
                {isQuotation && !hasWinnerSelected && (
                    <DecisionSection 
                        title="Alertas / Pendências" 
                        icon={<AlertCircle size={16} className="text-red-600" />}
                        isCollapsible={false}
                    >
                         <div style={{
                            display: 'flex', alignItems: 'center', gap: '12px',
                            padding: '16px',
                            backgroundColor: 'var(--color-status-red-bg)', 
                            border: '2px solid var(--color-status-red)',
                            fontSize: '0.8rem', fontWeight: 800,
                            color: 'var(--color-status-red)'
                        }}>
                            <AlertCircle size={20} style={{ flexShrink: 0 }} />
                            <span className="uppercase tracking-tight">
                                Aprovação bloqueada: Uma cotação vencedora deve ser selecionada para prosseguir.
                            </span>
                        </div>
                    </DecisionSection>
                )}

                {/* 4. JUSTIFICATIVA / OBSERVAÇÕES (Always Open if present) */}
                {data.description && (
                    <DecisionSection 
                        title="Justificativa / Observações" 
                        icon={<MessageSquare size={16} />}
                        isCollapsible={false}
                    >
                        <p style={{
                            margin: 0, fontSize: '0.9rem', fontWeight: 600,
                            color: 'var(--color-text-main)', lineHeight: '1.6',
                            padding: '20px', backgroundColor: 'white',
                            border: '1.5px solid black',
                            whiteSpace: 'pre-wrap'
                        }}>
                            {data.description}
                        </p>
                    </DecisionSection>
                )}

                {/* 5. COTAÇÕES SALVAS (Always Open for Quotation types) */}
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

                {/* 6. ITENS DO PEDIDO (Collapsible) */}
                <DecisionSection 
                    title="Itens do pedido" 
                    icon={<List size={16} />}
                    count={data.lineItems?.length || 0}
                    isCollapsible={true}
                    defaultOpen={false}
                    noPadding={true}
                >
                    {(data.lineItems?.length || 0) === 0 ? (
                        <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                            Nenhum item encontrado.
                        </div>
                    ) : (
                        <div style={{ overflowX: 'auto' }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem' }}>
                                <thead style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '2px solid black' }}>
                                    <tr>
                                        <th style={{ padding: '14px 16px', textAlign: 'left', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.65rem', letterSpacing: '0.05em' }}>#</th>
                                        <th style={{ padding: '14px 16px', textAlign: 'left', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.65rem', letterSpacing: '0.05em' }}>Descrição</th>
                                        <th style={{ padding: '14px 16px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.65rem', letterSpacing: '0.05em' }}>Qtd</th>
                                        <th style={{ padding: '14px 16px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.65rem', letterSpacing: '0.05em' }}>Total</th>
                                        {approvalStage === 'AREA' && isAreaApprovalStage && activeItems.length > 1 && (
                                            <th style={{ padding: '14px 16px', textAlign: 'left', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.65rem', letterSpacing: '0.05em', width: '220px' }}>Centro de Custo</th>
                                        )}
                                    </tr>
                                </thead>
                                <tbody>
                                    {data.lineItems.map((item) => (
                                        <tr key={item.id} style={{ borderBottom: '1.5px solid black', backgroundColor: 'white' }}>
                                            <td style={{ padding: '14px 16px', fontWeight: 800, color: 'var(--color-text-muted)' }}>{item.lineNumber}</td>
                                            <td style={{ padding: '14px 16px', fontWeight: 700 }}>
                                                <div className="font-black text-gray-900">{item.description}</div>
                                                <div style={{ fontSize: '10px', color: 'var(--color-text-muted)', marginTop: '4px', fontWeight: 800, textTransform: 'uppercase' }}>
                                                    {item.unit || 'UN'} x {formatCurrencyAO(item.unitPrice)}
                                                </div>
                                            </td>
                                            <td style={{ padding: '14px 16px', textAlign: 'right', fontWeight: 700 }}>{item.quantity}</td>
                                            <td style={{ padding: '14px 16px', textAlign: 'right', fontWeight: 900 }}>{formatCurrencyAO(item.totalAmount)}</td>
                                            {approvalStage === 'AREA' && isAreaApprovalStage && activeItems.length > 1 && (
                                                <td style={{ padding: '14px 16px', borderLeft: '1.5px solid black' }}>
                                                    <div className="flex flex-col gap-2">
                                                        <div className="text-[10px] font-black text-gray-500 uppercase tracking-widest">
                                                            Planta: {item.plantName || '---'}
                                                        </div>
                                                        <select
                                                            value={itemCostCenters[item.id] || ''}
                                                            onChange={(e) => setItemCostCenters(prev => ({ ...prev, [item.id]: parseInt(e.target.value) || null }))}
                                                            className={`w-full text-[10px] font-bold uppercase p-1.5 border-2 focus:outline-none ${!itemCostCenters[item.id] ? 'border-red-600 bg-red-50 text-red-900' : 'border-black bg-white text-black'}`}
                                                        >
                                                            <option value="">Selecione o C.C...</option>
                                                            {costCenters.filter(cc => cc.plantId === item.plantId).map(cc => (
                                                                <option key={cc.id} value={cc.id}>
                                                                    [{cc.code}] {cc.name}
                                                                </option>
                                                            ))}
                                                        </select>
                                                        {canBulkFillForItem(item.id) && (
                                                            <button 
                                                                onClick={() => handleBulkFillCostCenter(item.id)}
                                                                className="text-[9px] font-black uppercase text-blue-700 hover:underline text-left leading-tight py-1"
                                                            >
                                                                Aplicar este centro de custo aos itens pendentes de {item.plantName}
                                                            </button>
                                                        )}
                                                    </div>
                                                </td>
                                            )}
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                </DecisionSection>

                {/* 7. RESUMO FINANCEIRO (Collapsible) */}
                <DecisionSection 
                    title="Resumo financeiro" 
                    icon={<DollarSign size={16} />}
                    isCollapsible={true}
                    defaultOpen={false}
                >
                    <div style={{
                        display: 'flex', flexDirection: 'column', gap: '20px',
                        padding: '24px', backgroundColor: 'white', border: '2px solid black'
                    }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <span style={{ fontSize: '11px', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Custo Estimado Total</span>
                            <span style={{ fontSize: '1.6rem', fontWeight: 900, letterSpacing: '-0.02em', color: 'black' }}>
                                {formatCurrencyAO(data.estimatedTotalAmount)} <span style={{ fontSize: '0.9rem', opacity: 0.5 }}>{data.currencyCode}</span>
                            </span>
                        </div>
                        {data.supplierPortalCode && (
                            <div style={{ display: 'flex', justifyContent: 'space-between', borderTop: '2px solid black', paddingTop: '16px' }}>
                                <span style={{ fontSize: '11px', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>Código do Fornecedor</span>
                                <span style={{ fontSize: '0.9rem', fontWeight: 900, fontFamily: 'monospace', backgroundColor: 'var(--color-bg-page)', padding: '2px 8px', border: '1px solid black' }}>{data.supplierPortalCode}</span>
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
                        <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                            Nenhum anexo registrado.
                        </div>
                    ) : (
                        <div style={{ backgroundColor: 'white' }}>
                            {data.attachments.map((att) => (
                                <div key={att.id} style={{
                                    display: 'flex', alignItems: 'center', justifySelf: 'space-between',
                                    padding: '16px 20px', borderBottom: '1.5px solid black'
                                }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '14px', flex: 1, minWidth: 0 }}>
                                        <div style={{ 
                                            width: '36px', height: '36px', backgroundColor: 'var(--color-bg-page)', 
                                            border: '1.5px solid black',
                                            display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 
                                        }}>
                                            <Paperclip size={18} style={{ color: 'black' }} />
                                        </div>
                                        <div style={{ minWidth: 0 }}>
                                            <div style={{ fontWeight: 900, fontSize: '0.9rem', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', color: 'black' }}>
                                                {att.fileName}
                                            </div>
                                            <div style={{ fontSize: '10px', color: 'var(--color-text-muted)', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.02em' }}>
                                                {ATTACHMENT_TYPE_LABELS[att.attachmentTypeCode] || att.attachmentTypeCode} • Por {att.uploadedByName}
                                            </div>
                                        </div>
                                    </div>
                                    <button
                                        onClick={() => handleDownloadAttachment(att.id, att.fileName)}
                                        style={{
                                            background: 'white', border: '2px solid black',
                                            cursor: 'pointer', padding: '8px', marginLeft: '12px', display: 'flex',
                                            boxShadow: '2px 2px 0px rgba(0,0,0,1)'
                                        }}
                                        className="hover:translate-x-[1px] hover:translate-y-[1px] hover:shadow-none transition-all"
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
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                        {[
                            { label: 'Solicitante', name: data.requesterName, role: 'Buyer/User' },
                            { label: 'Comprador Atribuído', name: data.buyerName, role: 'Procurement' },
                            { label: 'Aprovador da Área', name: data.areaApproverName, role: 'Area Manager' },
                            { label: 'Aprovador Final', name: data.finalApproverName, role: 'C-Level / Admin' }
                        ].map((p, idx) => (
                            <div key={idx} style={{ 
                                display: 'flex', alignItems: 'center', gap: '14px',
                                padding: '14px', backgroundColor: 'white', border: '2px solid black'
                            }}>
                                <div style={{ 
                                    width: '36px', height: '36px', backgroundColor: 'black', color: 'white',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.8rem', fontWeight: 900
                                }}>
                                    {p.name?.substring(0, 2).toUpperCase() || '??'}
                                </div>
                                <div>
                                    <div style={{ fontSize: '0.9rem', fontWeight: 950, color: 'black' }}>{p.name || 'Pendente'}</div>
                                    <div style={{ fontSize: '10px', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>{p.label}</div>
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
