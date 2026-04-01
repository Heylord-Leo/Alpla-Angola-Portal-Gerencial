import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import {
    User, Building2, Factory,
    Calendar, Landmark, Download,
    Paperclip, AlertCircle, List, FileText, 
    MessageSquare, Users, History as HistoryIcon, DollarSign,
    Target, TrendingUp
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
}

// --- Component ---

export function ApprovalDetailPanel({
    data,
    approvalStage,
    onActionCompleted,
    onClose,
    onDataRefresh
}: ApprovalDetailPanelProps) {
    const navigate = useNavigate();

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

    useEffect(() => {
        if (data.id) {
            fetchIntelligence();
        }
    }, [data.id]);

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

    // --- Computed ---
    const isQuotation = data.requestTypeCode === 'QUOTATION';
    const isPayment = data.requestTypeCode === 'PAYMENT';
    const selectedQuotation = data.quotations?.find(q => q.isSelected);
    const hasWinnerSelected = !!selectedQuotation;

    const isAreaApprovalStage = data.statusCode === 'WAITING_AREA_APPROVAL';
    const isFinalApprovalStage = data.statusCode === 'WAITING_FINAL_APPROVAL';

    // Winner selection is enabled for QUOTATION requests when the approver is in their decision stage
    const canSelectWinner = isQuotation && (
        (approvalStage === 'AREA' && isAreaApprovalStage) ||
        (approvalStage === 'FINAL' && isFinalApprovalStage)
    );

    // Approve is blocked for QUOTATION requests until a winner is selected
    const isApproveBlocked = isQuotation && !hasWinnerSelected;

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

        // Validate winner selection for QUOTATION approve
        if (action === 'APPROVE' && isQuotation) {
            const selected = data.quotations?.find(q => q.isSelected);
            if (!selected) {
                setModalFeedback({ type: 'error', message: 'É necessário selecionar uma cotação vencedora antes de aprovar.' });
                return;
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
                    ? await api.requests.approveArea(data.id, approvalComment, data.quotations?.find(q => q.isSelected)?.id)
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
        { label: 'Centro de Custo', value: data.costCenterCode, icon: <Landmark size={12} /> },
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
                requestNumber={data.requestNumber || data.id.substring(0, 8)}
                requestTypeCode={data.requestTypeCode || ''}
                statusCode={data.statusCode || ''}
                statusName={data.statusName || ''}
                statusBadgeColor={data.statusBadgeColor || ''}
                totalAmount={data.estimatedTotalAmount}
                currencyCode={data.currencyCode || ''}
                approvalStage={approvalStage}
                onAction={(type) => setShowApprovalModal({ show: true, type })}
                onClose={onClose}
                onOpenRequest={() => navigate(`/requests/${data.id}`)}
                isApproveBlocked={isApproveBlocked}
                showAdjustmentAction={showAdjustmentAction}
            />

            {/* Content Container */}
            <div style={{ padding: '24px' }}>
                
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
                    icon={<TrendingUp size={16} className="text-primary" />}
                    isCollapsible={false}
                >
                    {loadingIntelligence ? (
                        <div style={{ padding: '32px', textAlign: 'center', backgroundColor: '#f8fafc', border: '1px solid #e2e8f0' }}>
                            <div className="flex flex-col items-center justify-center gap-3">
                                <div className="w-8 h-8 rounded-full border-2 border-primary border-t-transparent animate-spin" />
                                <span className="text-[10px] font-black uppercase tracking-[0.2em] text-slate-400">Processando histórico...</span>
                            </div>
                        </div>
                    ) : intelligence ? (
                        <DecisionInsightsPanel 
                            intelligence={intelligence} 
                            approvalStage={approvalStage}
                            requestData={{
                                description: data.description,
                                supplierName: data.supplierName,
                                costCenterCode: data.costCenterCode,
                                requestTypeCode: data.requestTypeCode,
                                hasQuotations: (data.quotations?.length || 0) > 0
                            }}
                        />
                    ) : (
                        <div className="p-6 text-center bg-slate-50 border border-slate-100">
                             <div className="flex flex-col items-center gap-2">
                                <Target className="w-8 h-8 text-slate-200" />
                                <span className="text-xs text-slate-400 font-medium">Dados de inteligência não disponíveis</span>
                             </div>
                        </div>
                    )}
                </DecisionSection>

                {/* 4. ALERTAS / PENDÊNCIAS (Always Open if present) */}
                {isQuotation && !hasWinnerSelected && (
                    <DecisionSection 
                        title="Alertas / Pendências" 
                        icon={<AlertCircle size={16} />}
                        isCollapsible={false}
                    >
                         <div style={{
                            display: 'flex', alignItems: 'center', gap: '10px',
                            padding: '16px',
                            backgroundColor: '#fef2f2', border: '1px solid #ef4444',
                            borderRadius: '0', fontSize: '0.85rem', fontWeight: 800,
                            color: '#b91c1c'
                        }}>
                            <AlertCircle size={18} style={{ flexShrink: 0 }} />
                            <span>
                                Aprovação bloqueada: Uma cotação vencedora deve ser selecionada abaixo para prosseguir.
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
                            padding: '16px', backgroundColor: '#fff',
                            border: '1px solid var(--color-border)', borderRadius: '0',
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
                                <thead style={{ backgroundColor: '#f9fafb', borderBottom: '2px solid black' }}>
                                    <tr>
                                        <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.65rem' }}>#</th>
                                        <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.65rem' }}>Descrição</th>
                                        <th style={{ padding: '12px 16px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.65rem' }}>Qtd</th>
                                        <th style={{ padding: '12px 16px', textAlign: 'right', fontWeight: 900, textTransform: 'uppercase', fontSize: '0.65rem' }}>Total</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {data.lineItems.map((item) => (
                                        <tr key={item.id} style={{ borderBottom: '1px solid var(--color-border-light)', backgroundColor: 'white' }}>
                                            <td style={{ padding: '12px 16px', fontWeight: 800, color: 'var(--color-text-muted)' }}>{item.lineNumber}</td>
                                            <td style={{ padding: '12px 16px', fontWeight: 700 }}>
                                                <div>{item.description}</div>
                                                <div style={{ fontSize: '10px', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                                    {item.unit || 'UN'} x {formatCurrencyAO(item.unitPrice)}
                                                </div>
                                            </td>
                                            <td style={{ padding: '12px 16px', textAlign: 'right', fontWeight: 700 }}>{item.quantity}</td>
                                            <td style={{ padding: '12px 16px', textAlign: 'right', fontWeight: 900 }}>{formatCurrencyAO(item.totalAmount)}</td>
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
                        display: 'flex', flexDirection: 'column', gap: '16px',
                        padding: '20px', backgroundColor: '#f8fafc', border: '1px solid black'
                    }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <span style={{ fontSize: '11px', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Custo Estimado Total</span>
                            <span style={{ fontSize: '1.4rem', fontWeight: 900, letterSpacing: '-0.02em' }}>
                                {formatCurrencyAO(data.estimatedTotalAmount)} <span style={{ fontSize: '0.8rem', opacity: 0.6 }}>{data.currencyCode}</span>
                            </span>
                        </div>
                        {data.supplierPortalCode && (
                            <div style={{ display: 'flex', justifyContent: 'space-between', borderTop: '1px solid var(--color-border)', paddingTop: '12px' }}>
                                <span style={{ fontSize: '11px', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Código do Fornecedor</span>
                                <span style={{ fontSize: '0.9rem', fontWeight: 900, fontFamily: 'monospace' }}>{data.supplierPortalCode}</span>
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
                                    padding: '16px 20px', borderBottom: '1px solid var(--color-border-light)'
                                }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flex: 1, minWidth: 0 }}>
                                        <div style={{ 
                                            width: '32px', height: '32px', backgroundColor: '#f1f5f9', 
                                            display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 
                                        }}>
                                            <Paperclip size={16} style={{ color: 'var(--color-text-muted)' }} />
                                        </div>
                                        <div style={{ minWidth: 0 }}>
                                            <div style={{ fontWeight: 800, fontSize: '0.85rem', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                                {att.fileName}
                                            </div>
                                            <div style={{ fontSize: '10px', color: 'var(--color-text-muted)', fontWeight: 700, textTransform: 'uppercase' }}>
                                                {ATTACHMENT_TYPE_LABELS[att.attachmentTypeCode] || att.attachmentTypeCode} • Por {att.uploadedByName}
                                            </div>
                                        </div>
                                    </div>
                                    <button
                                        onClick={() => handleDownloadAttachment(att.id, att.fileName)}
                                        style={{
                                            background: 'none', border: '1.5px solid black',
                                            cursor: 'pointer', padding: '6px', marginLeft: '12px', display: 'flex'
                                        }}
                                        className="hover:bg-black hover:text-white"
                                    >
                                        <Download size={14} />
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
                            <div key={idx} style={{ 
                                display: 'flex', alignItems: 'center', gap: '12px',
                                padding: '12px', backgroundColor: '#fff', border: '1.5px solid var(--color-border)'
                            }}>
                                <div style={{ 
                                    width: '32px', height: '32px', backgroundColor: 'black', color: 'white',
                                    display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.7rem', fontWeight: 900
                                }}>
                                    {p.name?.substring(0, 2).toUpperCase() || '??'}
                                </div>
                                <div>
                                    <div style={{ fontSize: '0.85rem', fontWeight: 900 }}>{p.name || 'Pendente'}</div>
                                    <div style={{ fontSize: '10px', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>{p.label}</div>
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
