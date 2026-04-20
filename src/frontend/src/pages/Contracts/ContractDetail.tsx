import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
    ArrowLeft, Play, Pause, XCircle, Send, FileText, Plus, Download,
    DollarSign, History, Bell, AlertTriangle, Edit, Trash2,
    ChevronDown, ChevronUp, Info, Calendar, Shield
} from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { PageContainer } from '../../components/ui/PageContainer';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { DateInput } from '../../components/DateInput';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { api } from '../../lib/api';
import {
    fetchContractDetail, transitionContractStatus, createObligation, generatePaymentRequest,
    updateObligation, cancelObligation,
    ContractDetail as ContractDetailType, Obligation, CONTRACT_STATUS_MAP, OBLIGATION_STATUS_MAP,
    CreateObligationPayload, getDocumentDownloadUrl
} from '../../lib/contractsApi';

// ─── StatusBadge ───

function StatusBadge({ statusCode, map }: { statusCode: string; map: Record<string, { label: string; color: string; bg: string }> }) {
    const cfg = map[statusCode] || { label: statusCode, color: '#6b7280', bg: '#f3f4f6' };
    return (
        <span style={{
            display: 'inline-flex', alignItems: 'center', gap: '4px',
            padding: '5px 14px', borderRadius: 'var(--radius-full)',
            fontSize: '0.8rem', fontWeight: 700, color: cfg.color, backgroundColor: cfg.bg
        }}>
            {cfg.label}
        </span>
    );
}

// ─── DueDate Source Badge ───

function DueDateSourceBadge({ source }: { source?: string }) {
    if (!source) return null;
    const isAuto = source === 'AUTO_FROM_CONTRACT';
    return (
        <span style={{
            display: 'inline-flex', alignItems: 'center', gap: '4px',
            padding: '2px 8px', borderRadius: 'var(--radius-full)',
            fontSize: '0.68rem', fontWeight: 700,
            backgroundColor: isAuto ? 'rgba(5,150,105,0.1)' : 'rgba(217,119,6,0.1)',
            color: isAuto ? '#059669' : '#d97706',
            border: `1px solid ${isAuto ? 'rgba(5,150,105,0.3)' : 'rgba(217,119,6,0.3)'}`
        }}>
            {isAuto ? '🔄 Auto' : '✏️ Manual'}
        </span>
    );
}

// ─── ObligationDeadlinePanel ───

interface ObligationDeadlinePanelProps {
    obl: Obligation;
    formatDate: (iso?: string) => string;
}

function ObligationDeadlinePanel({ obl, formatDate }: ObligationDeadlinePanelProps) {
    const [open, setOpen] = useState(false);

    const hasDeadlineInfo = obl.dueDateSourceCode || obl.calculatedDueDateUtc ||
        obl.graceDateUtc || obl.penaltyStartDateUtc || obl.referenceDateUtc;

    if (!hasDeadlineInfo) return null;

    const isOverridden = obl.dueDateSourceCode === 'MANUAL_OVERRIDE';
    const isAuto = obl.dueDateSourceCode === 'AUTO_FROM_CONTRACT';

    return (
        <tr style={{ backgroundColor: 'rgba(var(--color-primary-rgb), 0.02)' }}>
            <td colSpan={7} style={{ padding: '0 14px 0 28px', borderBottom: '1px solid var(--color-border)' }}>
                <button
                    type="button"
                    onClick={() => setOpen(o => !o)}
                    style={{
                        display: 'flex', alignItems: 'center', gap: '6px',
                        padding: '6px 0', background: 'none', border: 'none',
                        cursor: 'pointer', fontSize: '0.72rem', fontWeight: 700,
                        color: 'var(--color-text-muted)', letterSpacing: '0.04em'
                    }}
                >
                    <Calendar size={12} />
                    Detalhes do Prazo
                    {open ? <ChevronUp size={12} /> : <ChevronDown size={12} />}
                    <DueDateSourceBadge source={obl.dueDateSourceCode} />
                </button>
                <AnimatePresence>
                    {open && (
                        <motion.div
                            initial={{ height: 0, opacity: 0 }}
                            animate={{ height: 'auto', opacity: 1 }}
                            exit={{ height: 0, opacity: 0 }}
                            style={{ overflow: 'hidden' }}
                        >
                            <div style={{
                                display: 'grid',
                                gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))',
                                gap: '0.75rem',
                                padding: '10px 0 14px'
                            }}>
                                {obl.referenceDateUtc && (
                                    <DeadlineMetaItem
                                        label="Data de Referência"
                                        value={formatDate(obl.referenceDateUtc)}
                                        icon={<Info size={11} />}
                                    />
                                )}
                                {isAuto && obl.calculatedDueDateUtc && (
                                    <DeadlineMetaItem
                                        label="Vencimento Calculado"
                                        value={formatDate(obl.calculatedDueDateUtc)}
                                        icon={<Calendar size={11} />}
                                        highlight="auto"
                                    />
                                )}
                                {isOverridden && obl.calculatedDueDateUtc && (
                                    <DeadlineMetaItem
                                        label="Calculado (sobreposto)"
                                        value={formatDate(obl.calculatedDueDateUtc)}
                                        icon={<Calendar size={11} />}
                                        muted
                                    />
                                )}
                                {isOverridden && (
                                    <DeadlineMetaItem
                                        label="Vencimento Final (Manual)"
                                        value={formatDate(obl.dueDateUtc)}
                                        icon={<Edit size={11} />}
                                        highlight="manual"
                                    />
                                )}
                                {obl.graceDateUtc && (
                                    <DeadlineMetaItem
                                        label="Fim do Período de Carência"
                                        value={formatDate(obl.graceDateUtc)}
                                        icon={<Shield size={11} />}
                                    />
                                )}
                                {obl.penaltyStartDateUtc && (
                                    <DeadlineMetaItem
                                        label="Início de Multa / Juros"
                                        value={formatDate(obl.penaltyStartDateUtc)}
                                        icon={<AlertTriangle size={11} />}
                                        highlight="penalty"
                                    />
                                )}
                            </div>
                        </motion.div>
                    )}
                </AnimatePresence>
            </td>
        </tr>
    );
}

function DeadlineMetaItem({
    label, value, icon, highlight, muted
}: {
    label: string; value: string; icon: React.ReactNode;
    highlight?: 'auto' | 'manual' | 'penalty';
    muted?: boolean;
}) {
    const colorMap = {
        auto: { text: '#059669', bg: 'rgba(5,150,105,0.08)', border: 'rgba(5,150,105,0.2)' },
        manual: { text: '#d97706', bg: 'rgba(217,119,6,0.08)', border: 'rgba(217,119,6,0.2)' },
        penalty: { text: '#dc2626', bg: 'rgba(220,38,38,0.08)', border: 'rgba(220,38,38,0.2)' },
    };
    const style = highlight ? colorMap[highlight] : null;
    return (
        <div style={{
            padding: '8px 12px', borderRadius: 'var(--radius-md)',
            backgroundColor: style?.bg ?? (muted ? 'transparent' : 'var(--color-bg-surface)'),
            border: `1px solid ${style?.border ?? 'var(--color-border)'}`,
        }}>
            <div style={{
                display: 'flex', alignItems: 'center', gap: '4px',
                fontSize: '0.68rem', fontWeight: 700, color: style?.text ?? (muted ? 'var(--color-text-muted)' : 'var(--color-text-muted)'),
                textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '4px', opacity: muted ? 0.6 : 1
            }}>
                {icon} {label}
            </div>
            <div style={{
                fontSize: '0.85rem', fontWeight: 700, fontFamily: 'var(--font-family-display)',
                color: style?.text ?? (muted ? 'var(--color-text-muted)' : 'var(--color-text-main)'),
                textDecoration: muted ? 'line-through' : 'none', opacity: muted ? 0.5 : 1
            }}>
                {value}
            </div>
        </div>
    );
}

// ─── ObligationContextNote ───
// Displayed inside the Add/Edit form to tell the user what context data
// the selected contract rule requires.

function ObligationContextNote({ contract }: { contract: ContractDetailType }) {
    const { referenceEventTypeCode, paymentTermTypeCode } = contract;

    if (!paymentTermTypeCode) return null;

    const isManual = paymentTermTypeCode === 'MANUAL' || paymentTermTypeCode === 'ADVANCE_PAYMENT';

    if (isManual) {
        return (
            <div style={{
                display: 'flex', gap: '8px', alignItems: 'flex-start',
                padding: '10px 14px', borderRadius: 'var(--radius-md)',
                backgroundColor: 'rgba(217,119,6,0.08)', border: '1px solid rgba(217,119,6,0.25)',
                fontSize: '0.8rem', color: '#92400e', gridColumn: '1 / -1'
            }}>
                <Info size={14} style={{ flexShrink: 0, marginTop: '1px', color: '#d97706' }} />
                <span>
                    Este contrato utiliza prazo <strong>{paymentTermTypeCode === 'ADVANCE_PAYMENT' ? 'Pagamento Antecipado' : 'Manual'}</strong>.
                    O campo <strong>Vencimento</strong> deve ser preenchido manualmente para esta parcela.
                </span>
            </div>
        );
    }

    if (referenceEventTypeCode === 'INVOICE_RECEIVED_DATE') {
        return (
            <div style={{
                display: 'flex', gap: '8px', alignItems: 'flex-start',
                padding: '10px 14px', borderRadius: 'var(--radius-md)',
                backgroundColor: 'rgba(var(--color-primary-rgb), 0.06)', border: '1px solid rgba(var(--color-primary-rgb), 0.2)',
                fontSize: '0.8rem', color: 'var(--color-text-muted)', gridColumn: '1 / -1'
            }}>
                <Info size={14} style={{ flexShrink: 0, marginTop: '1px', color: 'var(--color-primary)' }} />
                <span>
                    A regra deste contrato calcula o vencimento a partir da <strong>Data de Recepção da Fatura</strong>.
                    Preencha esse campo para que o sistema calcule automaticamente o vencimento.
                </span>
            </div>
        );
    }

    if (referenceEventTypeCode === 'MANUAL_REFERENCE_DATE') {
        return (
            <div style={{
                display: 'flex', gap: '8px', alignItems: 'flex-start',
                padding: '10px 14px', borderRadius: 'var(--radius-md)',
                backgroundColor: 'rgba(var(--color-primary-rgb), 0.06)', border: '1px solid rgba(var(--color-primary-rgb), 0.2)',
                fontSize: '0.8rem', color: 'var(--color-text-muted)', gridColumn: '1 / -1'
            }}>
                <Info size={14} style={{ flexShrink: 0, marginTop: '1px', color: 'var(--color-primary)' }} />
                <span>
                    A regra deste contrato requer uma <strong>Data de Referência Manual</strong> para calcular o vencimento automaticamente.
                </span>
            </div>
        );
    }

    if (contract.paymentRuleSummary) {
        return (
            <div style={{
                display: 'flex', gap: '8px', alignItems: 'flex-start',
                padding: '10px 14px', borderRadius: 'var(--radius-md)',
                backgroundColor: 'rgba(5,150,105,0.06)', border: '1px solid rgba(5,150,105,0.2)',
                fontSize: '0.8rem', color: '#065f46', gridColumn: '1 / -1'
            }}>
                <Info size={14} style={{ flexShrink: 0, marginTop: '1px', color: '#059669' }} />
                <span>
                    Vencimento será calculado automaticamente: <strong>{contract.paymentRuleSummary}</strong>
                </span>
            </div>
        );
    }

    return null;
}

// ─── Main Component ───

export default function ContractDetailPage() {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const [contract, setContract] = useState<ContractDetailType | null>(null);
    const [loading, setLoading] = useState(true);

    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [modalShow, setModalShow] = useState(false);
    const [modalType, setModalType] = useState<ApprovalActionType>(null);
    const [modalTargetId, setModalTargetId] = useState<string | null>(null);
    const [modalAction, setModalAction] = useState<string | null>(null);
    const [error, setError] = useState('');
    const [activeTab, setActiveTab] = useState<'obligations' | 'documents' | 'history' | 'alerts'>('obligations');
    const [actionLoading, setActionLoading] = useState(false);
    const [requestToDelete, setRequestToDelete] = useState<string | null>(null);

    // ─── Obligation add form state ───
    const [showAddObligation, setShowAddObligation] = useState(false);
    const [oblDescription, setOblDescription] = useState('');
    const [oblAmount, setOblAmount] = useState('');
    const [oblDueDate, setOblDueDate] = useState('');
    const [oblInvoiceDate, setOblInvoiceDate] = useState('');
    const [oblManualRefDate, setOblManualRefDate] = useState('');
    const [oblBillingRef, setOblBillingRef] = useState('');
    const [oblNotes, setOblNotes] = useState('');

    // ─── Obligation edit form state ───
    const [editingObligationId, setEditingObligationId] = useState<string | null>(null);
    const [editOblDescription, setEditOblDescription] = useState('');
    const [editOblAmount, setEditOblAmount] = useState('');
    const [editOblDueDate, setEditOblDueDate] = useState('');
    const [editOblInvoiceDate, setEditOblInvoiceDate] = useState('');
    const [editOblManualRefDate, setEditOblManualRefDate] = useState('');
    const [editOblBillingRef, setEditOblBillingRef] = useState('');
    const [editOblNotes, setEditOblNotes] = useState('');

    const loadContract = useCallback(async () => {
        if (!id) return;
        setLoading(true);
        try {
            const data = await fetchContractDetail(id);
            setContract(data);
        } catch (err: any) {
            setError(err.message || 'Erro ao carregar contrato.');
        } finally {
            setLoading(false);
        }
    }, [id]);

    useEffect(() => { loadContract(); }, [loadContract]);

    // ─── Derived: contract rule context ───

    const ruleRequiresInvoiceDate = contract?.referenceEventTypeCode === 'INVOICE_RECEIVED_DATE';
    const ruleRequiresManualRefDate = contract?.referenceEventTypeCode === 'MANUAL_REFERENCE_DATE';
    const ruleIsManual = contract?.paymentTermTypeCode === 'MANUAL' || contract?.paymentTermTypeCode === 'ADVANCE_PAYMENT';
    const ruleIsAutoCalc = contract?.paymentTermTypeCode && !ruleIsManual;
    const allowsOverride = contract?.allowsManualDueDateOverride ?? false;

    // Is manual due date field required?
    const dueDateRequired = !contract?.paymentTermTypeCode || ruleIsManual || allowsOverride;

    const handleConfirmModal = async () => {
        if (!modalType) return;
        setModalShow(false);
        if (modalType === 'CHANGE_CONTRACT_STATUS' && modalAction) {
            handleStatusAction(modalAction, true);
        } else if (modalType === 'DELETE_OBLIGATION' && modalTargetId) {
            handleDeleteObligation(modalTargetId, true);
        } else if (modalType === 'GENERATE_PAYMENT' && modalTargetId) {
            handleGenerateRequest(modalTargetId, true);
        }
        setModalType(null);
        setModalTargetId(null);
        setModalAction(null);
    };

    const handleConfirmDeleteDraftRequest = async () => {
        if (!requestToDelete) return;
        try {
            setActionLoading(true);
            await api.requests.remove(requestToDelete);
            setFeedback({ type: 'success', message: 'Rascunho de pedido excluído com sucesso.' });
            setRequestToDelete(null);
            loadContract();
        } catch (error: any) {
            console.error('Failed to delete draft request:', error);
        } finally {
            setActionLoading(false);
        }
    };

    const handleStatusAction = async (action: string, confirmed: boolean = false) => {
        if (!id) return;
        if (!confirmed) {
            setModalType('CHANGE_CONTRACT_STATUS');
            setModalAction(action);
            setModalShow(true);
            return;
        }
        setActionLoading(true);
        try {
            await transitionContractStatus(id, action);
            setFeedback({ type: 'success', message: 'Status atualizado com sucesso.' });
            await loadContract();
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro na transição de status.' });
        } finally {
            setActionLoading(false);
        }
    };

    const buildObligationPayload = (
        description: string, amount: string, dueDate: string,
        invoiceDate: string, manualRefDate: string, billingRef: string, notes: string
    ): CreateObligationPayload => ({
        description: description || undefined,
        expectedAmount: parseFloat(amount),
        dueDateUtc: dueDate ? new Date(dueDate).toISOString() : undefined,
        invoiceReceivedDateUtc: invoiceDate ? new Date(invoiceDate).toISOString() : undefined,
        manualReferenceDateUtc: manualRefDate ? new Date(manualRefDate).toISOString() : undefined,
        billingReference: billingRef || undefined,
        obligationNotes: notes || undefined,
    });

    const handleAddObligation = async () => {
        if (!id || !oblAmount) return;
        // Require due date only when rule doesn't auto-calculate
        if (dueDateRequired && !oblDueDate) {
            setFeedback({ type: 'error', message: 'O campo Vencimento é obrigatório para este tipo de contrato.' });
            return;
        }
        setActionLoading(true);
        try {
            const payload = buildObligationPayload(oblDescription, oblAmount, oblDueDate, oblInvoiceDate, oblManualRefDate, oblBillingRef, oblNotes);
            await createObligation(id, payload);
            setShowAddObligation(false);
            setOblDescription(''); setOblAmount(''); setOblDueDate('');
            setOblInvoiceDate(''); setOblManualRefDate(''); setOblBillingRef(''); setOblNotes('');
            setFeedback({ type: 'success', message: 'Obrigação adicionada com sucesso.' });
            await loadContract();
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao adicionar obrigação.' });
        } finally {
            setActionLoading(false);
        }
    };

    const handleEditObligationStart = (obl: Obligation) => {
        setEditingObligationId(obl.id);
        setEditOblDescription(obl.description || '');
        setEditOblAmount(obl.expectedAmount.toString());
        setEditOblDueDate(obl.dueDateUtc ? obl.dueDateUtc.split('T')[0] : '');
        setEditOblInvoiceDate(obl.invoiceReceivedDateUtc ? obl.invoiceReceivedDateUtc.split('T')[0] : '');
        setEditOblManualRefDate('');
        setEditOblBillingRef(obl.billingReference || '');
        setEditOblNotes(obl.obligationNotes || '');
    };

    const handleEditObligationSave = async (oblId: string) => {
        if (!id || !editOblAmount) return;
        setActionLoading(true);
        try {
            const payload = buildObligationPayload(editOblDescription, editOblAmount, editOblDueDate, editOblInvoiceDate, editOblManualRefDate, editOblBillingRef, editOblNotes);
            await updateObligation(id, oblId, payload);
            setEditingObligationId(null);
            setFeedback({ type: 'success', message: 'Obrigação atualizada com sucesso.' });
            await loadContract();
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao atualizar obrigação.' });
        } finally {
            setActionLoading(false);
        }
    };

    const handleDeleteObligation = async (oblId: string, confirmed: boolean = false) => {
        if (!id) return;
        if (!confirmed) {
            setModalType('DELETE_OBLIGATION');
            setModalTargetId(oblId);
            setModalShow(true);
            return;
        }
        setActionLoading(true);
        try {
            await cancelObligation(id, oblId);
            setFeedback({ type: 'success', message: 'Obrigação apagada com sucesso.' });
            await loadContract();
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao apagar obrigação.' });
        } finally {
            setActionLoading(false);
        }
    };

    const handleGenerateRequest = async (obligationId: string, confirmed: boolean = false) => {
        if (!id) return;
        if (!confirmed) {
            setModalType('GENERATE_PAYMENT');
            setModalTargetId(obligationId);
            setModalShow(true);
            return;
        }
        setActionLoading(true);
        try {
            const result = await generatePaymentRequest(id, obligationId);
            setFeedback({ type: 'success', message: `${result.message}\nNúmero: ${result.requestNumber}` });
            await loadContract();
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao gerar pedido de pagamento.' });
        } finally {
            setActionLoading(false);
        }
    };

    const formatDate = (iso?: string) => {
        if (!iso) return '—';
        return new Date(iso).toLocaleDateString('pt-PT', { day: '2-digit', month: '2-digit', year: 'numeric' });
    };

    const formatCurrency = (val?: number, code?: string) => {
        if (val === null || val === undefined) return '—';
        return `${val.toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${code || ''}`.trim();
    };

    const fieldStyle: React.CSSProperties = {
        width: '100%', padding: '10px 14px', borderRadius: 'var(--radius-md)',
        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
        fontSize: '0.9rem', fontFamily: 'var(--font-family-body)', color: 'var(--color-text-main)'
    };
    const smallFieldStyle: React.CSSProperties = { ...fieldStyle, padding: '6px 10px' };
    const smallLabelStyle: React.CSSProperties = {
        display: 'block', fontSize: '0.75rem', fontWeight: 700,
        color: 'var(--color-text-muted)', marginBottom: '4px'
    };

    if (loading && !contract) return <PageContainer><div style={{ padding: '3rem', textAlign: 'center', color: 'var(--color-text-muted)' }}>Carregando...</div></PageContainer>;
    if (error || !contract) return <PageContainer><div style={{ padding: '3rem', textAlign: 'center', color: '#dc2626' }}>{error || 'Contrato não encontrado.'}</div></PageContainer>;

    // Available actions based on status (DEC-118 aware)
    const actions: { label: string; action: string; icon: React.ReactNode; color: string; isNavigation?: boolean; target?: string }[] = [];
    if (contract.statusCode === 'DRAFT') {
        actions.push({ label: 'Editar', action: 'edit', icon: <Edit size={14} />, color: 'var(--color-primary)', isNavigation: true, target: `/contracts/${contract.id}/edit` });
        actions.push({ label: 'Submeter para Revisão', action: 'submit-review', icon: <Send size={14} />, color: '#d97706' });
        actions.push({ label: 'Ativar Diretamente', action: 'activate', icon: <Play size={14} />, color: '#059669' });
    }
    // Legacy pre-DEC-118 state
    if (contract.statusCode === 'UNDER_REVIEW') {
        actions.push({ label: 'Editar', action: 'edit', icon: <Edit size={14} />, color: 'var(--color-primary)', isNavigation: true, target: `/contracts/${contract.id}/edit` });
        actions.push({ label: 'Ativar', action: 'activate', icon: <Play size={14} />, color: '#059669' });
    }
    // DEC-118 two-step approval statuses — contract is locked, admin force-activate only
    if (contract.statusCode === 'UNDER_TECHNICAL_REVIEW' || contract.statusCode === 'UNDER_FINAL_REVIEW') {
        actions.push({ label: 'Ativar (Admin)', action: 'activate', icon: <Play size={14} />, color: '#dc2626' });
    }
    if (contract.statusCode === 'ACTIVE') {
        actions.push({ label: 'Suspender', action: 'suspend', icon: <Pause size={14} />, color: '#d97706' });
        actions.push({ label: 'Terminar', action: 'terminate', icon: <XCircle size={14} />, color: '#dc2626' });
    }
    if (contract.statusCode === 'SUSPENDED') {
        actions.push({ label: 'Reativar', action: 'reactivate', icon: <Play size={14} />, color: '#059669' });
        actions.push({ label: 'Terminar', action: 'terminate', icon: <XCircle size={14} />, color: '#dc2626' });
    }

    return (
        <PageContainer>
            <Feedback
                type={feedback.type}
                message={feedback.message}
                onClose={() => setFeedback({ ...feedback, message: null })}
                isFixed
            />
            <ApprovalModal
                show={modalShow}
                type={modalType}
                onClose={() => { setModalShow(false); setModalType(null); setModalTargetId(null); setModalAction(null); }}
                onConfirm={handleConfirmModal}
                comment=""
                setComment={() => {}}
                processing={actionLoading}
                feedback={{ type: 'success', message: null }}
                onCloseFeedback={() => {}}
            />

            {/* Breadcrumb */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', marginBottom: '1.5rem' }}>
                <button
                    onClick={() => navigate('/contracts/list')}
                    style={{
                        display: 'flex', alignItems: 'center', gap: '6px',
                        padding: '8px 16px', borderRadius: 'var(--radius-md)',
                        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                        cursor: 'pointer', fontSize: '0.85rem', fontWeight: 600,
                        color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)'
                    }}
                >
                    <ArrowLeft size={16} /> Contratos
                </button>
            </div>

            {/* Header */}
            <div style={{
                display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '1rem',
                marginBottom: '1.5rem', flexWrap: 'wrap'
            }}>
                <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '4px' }}>
                        <h1 style={{
                            fontSize: '1.5rem', fontWeight: 800, fontFamily: 'var(--font-family-display)',
                            color: 'var(--color-primary)', margin: 0
                        }}>
                            {contract.contractNumber}
                        </h1>
                        <StatusBadge statusCode={contract.statusCode} map={CONTRACT_STATUS_MAP} />
                    </div>
                    <h2 style={{ fontSize: '1.1rem', fontWeight: 600, color: 'var(--color-text-main)', margin: '4px 0 0 0' }}>
                        {contract.title}
                    </h2>
                    <p style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', margin: '4px 0 0 0' }}>
                        {contract.contractTypeName} • {contract.companyName}{contract.plantName ? ` • ${contract.plantName}` : ''} • {contract.departmentName}
                    </p>
                </div>
                <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                    {actions.map(a => (
                        <button
                            key={a.action}
                            disabled={actionLoading}
                            onClick={() => a.isNavigation ? navigate(a.target!) : handleStatusAction(a.action)}
                            style={{
                                display: 'flex', alignItems: 'center', gap: '6px',
                                padding: '8px 16px', borderRadius: 'var(--radius-md)',
                                border: `1px solid ${a.color}`, backgroundColor: 'transparent',
                                color: a.color, cursor: actionLoading ? 'default' : 'pointer',
                                fontWeight: 700, fontSize: '0.8rem', fontFamily: 'var(--font-family-body)',
                                opacity: actionLoading ? 0.5 : 1, transition: 'all 0.2s'
                            }}
                        >
                            {a.icon} {a.label}
                        </button>
                    ))}
                </div>
            </div>

            {/* DEC-118 Approval Chain Banner */}
            {(contract.statusCode === 'UNDER_TECHNICAL_REVIEW' || contract.statusCode === 'UNDER_FINAL_REVIEW') && (
                <div style={{
                    marginBottom: '1.5rem', padding: '1rem 1.5rem',
                    borderRadius: 'var(--radius-lg)',
                    border: `1px solid ${contract.statusCode === 'UNDER_TECHNICAL_REVIEW' ? '#bfdbfe' : '#ddd6fe'}`,
                    backgroundColor: contract.statusCode === 'UNDER_TECHNICAL_REVIEW' ? '#eff6ff' : '#f5f3ff',
                    display: 'flex', alignItems: 'center', gap: '1rem', flexWrap: 'wrap'
                }}>
                    <Shield size={18} color={contract.statusCode === 'UNDER_TECHNICAL_REVIEW' ? '#0369a1' : '#7c3aed'} style={{ flexShrink: 0 }} />
                    <div style={{ flex: 1, minWidth: 240 }}>
                        <div style={{ fontSize: '0.78rem', fontWeight: 700, color: contract.statusCode === 'UNDER_TECHNICAL_REVIEW' ? '#0369a1' : '#7c3aed', marginBottom: 4 }}>
                            {contract.statusCode === 'UNDER_TECHNICAL_REVIEW' ? 'Em Revisão Técnica' : 'Aguardando Aprovação Final'}
                        </div>
                        <div style={{ display: 'flex', gap: '1.5rem', flexWrap: 'wrap', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                            {contract.technicalApproverName && (
                                <span>
                                    <strong>Aprovador Técnico:</strong>{' '}
                                    <span style={{ color: contract.statusCode === 'UNDER_TECHNICAL_REVIEW' ? '#0369a1' : 'inherit' }}>
                                        {contract.technicalApproverName}
                                    </span>
                                    {contract.statusCode === 'UNDER_FINAL_REVIEW' && (
                                        <span style={{ marginLeft: 4, fontSize: '0.7rem', color: '#059669', fontWeight: 700 }}>✓ aprovado</span>
                                    )}
                                </span>
                            )}
                            {contract.finalApproverName && (
                                <span>
                                    <strong>Aprovador Final:</strong>{' '}
                                    <span style={{ color: contract.statusCode === 'UNDER_FINAL_REVIEW' ? '#7c3aed' : 'inherit' }}>
                                        {contract.finalApproverName}
                                    </span>
                                </span>
                            )}
                        </div>
                    </div>
                    <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                        Contrato bloqueado para edição durante a revisão.
                    </span>
                </div>
            )}


            <div style={{
                display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                gap: '1rem', marginBottom: '1.5rem', padding: '1.5rem',
                backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-lg)',
                border: '1px solid var(--color-border)'
            }}>
                <InfoField label="Contraparte" value={contract.supplierName || contract.counterpartyName} />
                <InfoField label="Data Início" value={formatDate(contract.effectiveDateUtc)} />
                <InfoField label="Data Expiração" value={formatDate(contract.expirationDateUtc)} />
                <InfoField label="Data Assinatura" value={formatDate(contract.signedAtUtc)} />
                <InfoField label="Valor Total" value={formatCurrency(contract.totalContractValue, contract.currencyCode)} highlight />
                <InfoField label="Renovação Auto." value={contract.autoRenew ? `Sim (${contract.renewalNoticeDays || 0} dias aviso)` : 'Não'} />
                <InfoField label="Condições Pagamento" value={contract.paymentTerms} />
                <InfoField label="Lei Aplicável" value={contract.governingLaw} />
                <InfoField label="Criado por" value={contract.createdByUserName} />
                <InfoField label="Data Criação" value={formatDate(contract.createdAtUtc)} />
            </div>

            {/* Payment Rule Summary Panel */}
            {(contract.paymentTermTypeCode || contract.paymentRuleSummary) && (
                <div style={{
                    marginBottom: '1.5rem', padding: '1.25rem',
                    backgroundColor: 'rgba(5,150,105,0.04)',
                    borderRadius: 'var(--radius-lg)',
                    border: '1px solid rgba(5,150,105,0.25)'
                }}>
                    <div style={{
                        display: 'flex', alignItems: 'center', gap: '8px',
                        marginBottom: '0.75rem'
                    }}>
                        <Calendar size={16} color="#059669" />
                        <span style={{
                            fontSize: '0.8rem', fontWeight: 800, color: '#059669',
                            textTransform: 'uppercase', letterSpacing: '0.07em'
                        }}>
                            Regra de Pagamento Ativa
                        </span>
                    </div>
                    <div style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                        gap: '0.75rem'
                    }}>
                        {contract.paymentRuleSummary && (
                            <div style={{ gridColumn: '1 / -1' }}>
                                <div style={{ fontSize: '0.72rem', fontWeight: 700, color: '#065f46', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '4px' }}>Resumo</div>
                                <div style={{ fontSize: '0.9rem', fontWeight: 600, color: 'var(--color-text-main)' }}>{contract.paymentRuleSummary}</div>
                            </div>
                        )}
                        {contract.gracePeriodDays != null && contract.gracePeriodDays > 0 && (
                            <div>
                                <div style={{ fontSize: '0.72rem', fontWeight: 700, color: '#065f46', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '4px' }}>Carência</div>
                                <div style={{ fontSize: '0.85rem', fontWeight: 600 }}>{contract.gracePeriodDays} dias</div>
                            </div>
                        )}
                        {contract.hasLatePenalty && (
                            <div>
                                <div style={{ fontSize: '0.72rem', fontWeight: 700, color: '#dc2626', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '4px' }}>Multa</div>
                                <div style={{ fontSize: '0.85rem', fontWeight: 600, color: '#dc2626' }}>
                                    {contract.latePenaltyValue}{contract.latePenaltyTypeCode === 'PERCENTAGE' ? '%' : ' (fixo)'}
                                </div>
                            </div>
                        )}
                        {contract.hasLateInterest && (
                            <div>
                                <div style={{ fontSize: '0.72rem', fontWeight: 700, color: '#d97706', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '4px' }}>Juros</div>
                                <div style={{ fontSize: '0.85rem', fontWeight: 600, color: '#d97706' }}>
                                    {contract.lateInterestValue}{contract.lateInterestTypeCode === 'PERCENTAGE' ? '%' : ' (fixo)'}
                                </div>
                            </div>
                        )}
                        {contract.allowsManualDueDateOverride && (
                            <div>
                                <div style={{ fontSize: '0.72rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '4px' }}>Ajuste Manual</div>
                                <div style={{ fontSize: '0.85rem', fontWeight: 600, color: '#d97706' }}>Permitido</div>
                            </div>
                        )}
                    </div>
                    {contract.financialNotes && (
                        <p style={{ marginTop: '0.75rem', fontSize: '0.8rem', color: 'var(--color-text-muted)', borderTop: '1px solid rgba(5,150,105,0.2)', paddingTop: '0.75rem' }}>
                            📝 {contract.financialNotes}
                        </p>
                    )}
                </div>
            )}

            {/* Tabs */}
            <div style={{ display: 'flex', gap: '0', borderBottom: '2px solid var(--color-border)', marginBottom: '1.5rem' }}>
                {[
                    { key: 'obligations' as const, label: 'Obrigações', icon: <DollarSign size={16} />, count: contract.obligations.length },
                    { key: 'documents' as const, label: 'Documentos', icon: <FileText size={16} />, count: contract.documents.length },
                    { key: 'history' as const, label: 'Histórico', icon: <History size={16} />, count: contract.histories.length },
                    { key: 'alerts' as const, label: 'Alertas', icon: <Bell size={16} />, count: contract.alerts.length }
                ].map(tab => (
                    <button
                        key={tab.key}
                        onClick={() => setActiveTab(tab.key)}
                        style={{
                            display: 'flex', alignItems: 'center', gap: '6px',
                            padding: '12px 20px', border: 'none', cursor: 'pointer',
                            backgroundColor: activeTab === tab.key ? 'rgba(var(--color-primary-rgb), 0.08)' : 'transparent',
                            color: activeTab === tab.key ? 'var(--color-primary)' : 'var(--color-text-muted)',
                            borderBottom: activeTab === tab.key ? '2px solid var(--color-primary)' : '2px solid transparent',
                            fontWeight: 700, fontSize: '0.85rem', fontFamily: 'var(--font-family-body)',
                            transition: 'all 0.2s', marginBottom: '-2px'
                        }}
                    >
                        {tab.icon} {tab.label}
                        {tab.count > 0 && (
                            <span style={{
                                padding: '2px 8px', borderRadius: 'var(--radius-full)',
                                backgroundColor: activeTab === tab.key ? 'var(--color-primary)' : 'var(--color-border)',
                                color: activeTab === tab.key ? 'white' : 'var(--color-text-muted)',
                                fontSize: '0.7rem', fontWeight: 800
                            }}>
                                {tab.count}
                            </span>
                        )}
                    </button>
                ))}
            </div>

            {/* Tab Content */}
            <AnimatePresence mode="wait">
                {activeTab === 'obligations' && (
                    <motion.div key="obligations" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
                            <h3 style={{ fontSize: '1rem', fontWeight: 700, margin: 0 }}>Obrigações de Pagamento</h3>
                            {(contract.statusCode === 'ACTIVE' || contract.statusCode === 'DRAFT') && (
                                <button
                                    onClick={() => setShowAddObligation(!showAddObligation)}
                                    style={{
                                        display: 'flex', alignItems: 'center', gap: '6px',
                                        padding: '8px 16px', borderRadius: 'var(--radius-md)',
                                        backgroundColor: 'var(--color-primary)', color: 'white',
                                        border: 'none', cursor: 'pointer', fontWeight: 700, fontSize: '0.8rem',
                                        fontFamily: 'var(--font-family-body)'
                                    }}
                                >
                                    <Plus size={14} /> Adicionar
                                </button>
                            )}
                        </div>

                        {/* Add Obligation Form */}
                        <AnimatePresence>
                            {showAddObligation && (
                                <motion.div
                                    initial={{ height: 0, opacity: 0 }}
                                    animate={{ height: 'auto', opacity: 1 }}
                                    exit={{ height: 0, opacity: 0 }}
                                    style={{ overflow: 'hidden', marginBottom: '1rem' }}
                                >
                                    <div style={{
                                        padding: '1.25rem',
                                        backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-lg)',
                                        border: '1px solid var(--color-border)',
                                        display: 'grid', gap: '1rem',
                                        gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))',
                                        alignItems: 'end'
                                    }}>
                                        {/* Context note for the user */}
                                        {contract && <ObligationContextNote contract={contract} />}

                                        <div style={{ flex: 2, minWidth: '150px' }}>
                                            <label style={smallLabelStyle}>Descrição</label>
                                            <input style={fieldStyle} value={oblDescription} onChange={e => setOblDescription(e.target.value)} placeholder="Ex: Parcela 1" />
                                        </div>
                                        <div>
                                            <label style={smallLabelStyle}>Valor *</label>
                                            <input type="number" step="0.01" style={fieldStyle} value={oblAmount} onChange={e => setOblAmount(e.target.value)} placeholder="0.00" required />
                                        </div>

                                        {/* Invoice date — when rule requires it */}
                                        {ruleRequiresInvoiceDate && (
                                            <div>
                                                <label style={smallLabelStyle}>Data Recepção Fatura *</label>
                                                <DateInput style={fieldStyle} value={oblInvoiceDate} onChange={setOblInvoiceDate} />
                                            </div>
                                        )}

                                        {/* Manual reference date */}
                                        {ruleRequiresManualRefDate && (
                                            <div>
                                                <label style={smallLabelStyle}>Data de Referência *</label>
                                                <DateInput style={fieldStyle} value={oblManualRefDate} onChange={setOblManualRefDate} />
                                            </div>
                                        )}

                                        {/* Due date: required when manual/no-rule, optional for auto+override */}
                                        {(ruleIsManual || !contract.paymentTermTypeCode || allowsOverride) && (
                                            <div>
                                                <label style={smallLabelStyle}>
                                                    Vencimento {ruleIsManual || !contract.paymentTermTypeCode ? '*' : '(ajuste manual)'}
                                                </label>
                                                <DateInput style={fieldStyle} value={oblDueDate} onChange={setOblDueDate} />
                                                {ruleIsAutoCalc && allowsOverride && (
                                                    <p style={{ marginTop: '2px', fontSize: '0.65rem', color: '#d97706' }}>
                                                        Deixe em branco para usar o vencimento calculado automaticamente.
                                                    </p>
                                                )}
                                            </div>
                                        )}

                                        <div>
                                            <label style={smallLabelStyle}>Referência/Fatura</label>
                                            <input style={fieldStyle} value={oblBillingRef} onChange={e => setOblBillingRef(e.target.value)} placeholder="Nº da fatura" />
                                        </div>
                                        <div style={{ gridColumn: '1 / -1' }}>
                                            <label style={smallLabelStyle}>Observações</label>
                                            <input style={fieldStyle} value={oblNotes} onChange={e => setOblNotes(e.target.value)} placeholder="Notas internas desta parcela" />
                                        </div>
                                        <div style={{ gridColumn: '1 / -1', display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
                                            <button
                                                onClick={() => { setShowAddObligation(false); setOblDescription(''); setOblAmount(''); setOblDueDate(''); setOblInvoiceDate(''); setOblManualRefDate(''); setOblBillingRef(''); setOblNotes(''); }}
                                                style={{ padding: '8px 16px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)', backgroundColor: 'transparent', cursor: 'pointer', fontWeight: 700, fontSize: '0.8rem', color: 'var(--color-text-muted)' }}
                                            >
                                                Cancelar
                                            </button>
                                            <button
                                                disabled={actionLoading || !oblAmount}
                                                onClick={handleAddObligation}
                                                style={{
                                                    padding: '8px 20px', borderRadius: 'var(--radius-md)',
                                                    backgroundColor: 'var(--color-primary)', color: 'white',
                                                    border: 'none', cursor: actionLoading ? 'default' : 'pointer',
                                                    fontWeight: 700, fontSize: '0.85rem', fontFamily: 'var(--font-family-body)',
                                                    opacity: actionLoading ? 0.6 : 1
                                                }}
                                            >
                                                Adicionar
                                            </button>
                                        </div>
                                    </div>
                                </motion.div>
                            )}
                        </AnimatePresence>

                        {/* Obligations Table */}
                        {contract.obligations.length === 0 ? (
                            <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
                                Nenhuma obrigação de pagamento registada.
                            </div>
                        ) : (
                            <div style={{ overflowX: 'auto', borderRadius: 'var(--radius-lg)', border: '1px solid var(--color-border)' }}>
                                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }}>
                                    <thead>
                                        <tr style={{ backgroundColor: 'var(--color-bg-page)', borderBottom: '2px solid var(--color-border)' }}>
                                            {['#', 'Descrição', 'Valor', 'Vencimento', 'Status', 'Pedido Vinculado', 'Ação'].map(h => (
                                                <th key={h} style={{
                                                    padding: '10px 14px', textAlign: 'left', fontWeight: 700,
                                                    color: 'var(--color-text-muted)', fontSize: '0.75rem',
                                                    textTransform: 'uppercase', letterSpacing: '0.05em'
                                                }}>
                                                    {h}
                                                </th>
                                            ))}
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {contract.obligations.map(obl => {
                                            const isEditing = editingObligationId === obl.id;
                                            return (
                                                <>
                                                    <tr key={obl.id} style={{ borderBottom: obl.dueDateSourceCode ? 'none' : '1px solid var(--color-border)', backgroundColor: isEditing ? 'rgba(var(--color-primary-rgb), 0.05)' : 'transparent' }}>
                                                        <td style={{ padding: '12px 14px', fontWeight: 700, fontFamily: 'var(--font-family-display)' }}>
                                                            {obl.sequenceNumber}
                                                        </td>
                                                        <td style={{ padding: '12px 14px' }}>
                                                            {isEditing ? (
                                                                <input style={smallFieldStyle} value={editOblDescription} onChange={e => setEditOblDescription(e.target.value)} placeholder="Descrição" />
                                                            ) : (obl.description || '—')}
                                                        </td>
                                                        <td style={{ padding: '12px 14px', fontWeight: 700, fontFamily: 'var(--font-family-display)', whiteSpace: 'nowrap' }}>
                                                            {isEditing ? (
                                                                <input type="number" step="0.01" style={{ ...smallFieldStyle, width: '110px' }} value={editOblAmount} onChange={e => setEditOblAmount(e.target.value)} required />
                                                            ) : formatCurrency(obl.expectedAmount, obl.currencyCode || contract.currencyCode)}
                                                        </td>
                                                        <td style={{ padding: '12px 14px', whiteSpace: 'nowrap' }}>
                                                            {isEditing ? (
                                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                                    <DateInput style={smallFieldStyle} value={editOblDueDate} onChange={setEditOblDueDate} />
                                                                    {ruleRequiresInvoiceDate && (
                                                                        <>
                                                                            <label style={{ ...smallLabelStyle, marginBottom: 0 }}>Fatura</label>
                                                                            <DateInput style={smallFieldStyle} value={editOblInvoiceDate} onChange={setEditOblInvoiceDate} />
                                                                        </>
                                                                    )}
                                                                </div>
                                                            ) : (
                                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                                    <span>{formatDate(obl.dueDateUtc)}</span>
                                                                    {obl.dueDateSourceCode && <DueDateSourceBadge source={obl.dueDateSourceCode} />}
                                                                </div>
                                                            )}
                                                        </td>
                                                        <td style={{ padding: '12px 14px' }}>
                                                            <StatusBadge statusCode={obl.statusCode} map={OBLIGATION_STATUS_MAP} />
                                                        </td>
                                                        <td style={{ padding: '12px 14px' }}>
                                                            {obl.linkedRequestNumber ? (
                                                                <a
                                                                    onClick={() => navigate(`/requests/${obl.linkedRequestId}`)}
                                                                    style={{ color: 'var(--color-primary)', cursor: 'pointer', fontWeight: 600, textDecoration: 'underline' }}
                                                                >
                                                                    {obl.linkedRequestNumber}
                                                                </a>
                                                            ) : '—'}
                                                        </td>
                                                        <td style={{ padding: '12px 14px' }}>
                                                            {isEditing ? (
                                                                <div style={{ display: 'flex', gap: '6px' }}>
                                                                    <button
                                                                        disabled={actionLoading}
                                                                        onClick={() => handleEditObligationSave(obl.id)}
                                                                        style={{ padding: '6px 10px', borderRadius: 'var(--radius-md)', backgroundColor: 'var(--color-primary)', color: 'white', border: 'none', cursor: actionLoading ? 'default' : 'pointer', fontWeight: 700, fontSize: '0.75rem' }}
                                                                    >
                                                                        Salvar
                                                                    </button>
                                                                    <button
                                                                        disabled={actionLoading}
                                                                        onClick={() => setEditingObligationId(null)}
                                                                        style={{ padding: '6px 10px', borderRadius: 'var(--radius-md)', backgroundColor: 'transparent', border: '1px solid var(--color-border)', color: 'var(--color-text-muted)', cursor: actionLoading ? 'default' : 'pointer', fontWeight: 700, fontSize: '0.75rem' }}
                                                                    >
                                                                        Cancelar
                                                                    </button>
                                                                </div>
                                                            ) : (
                                                                <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                                                                    {obl.statusCode === 'PENDING' && (contract.statusCode === 'DRAFT' || contract.statusCode === 'UNDER_REVIEW') && (
                                                                        <>
                                                                            <button
                                                                                disabled={actionLoading}
                                                                                onClick={() => handleEditObligationStart(obl)}
                                                                                title="Editar Obrigação"
                                                                                style={{ padding: '6px', borderRadius: 'var(--radius-md)', backgroundColor: 'transparent', color: 'var(--color-primary)', border: '1px solid var(--color-primary)', cursor: actionLoading ? 'default' : 'pointer', opacity: actionLoading ? 0.5 : 1 }}
                                                                            >
                                                                                <Edit size={14} />
                                                                            </button>
                                                                            <button
                                                                                disabled={actionLoading}
                                                                                onClick={() => handleDeleteObligation(obl.id)}
                                                                                title="Apagar Obrigação"
                                                                                style={{ padding: '6px', borderRadius: 'var(--radius-md)', backgroundColor: 'transparent', color: '#dc2626', border: '1px solid #dc2626', cursor: actionLoading ? 'default' : 'pointer', opacity: actionLoading ? 0.5 : 1 }}
                                                                            >
                                                                                <Trash2 size={14} />
                                                                            </button>
                                                                        </>
                                                                    )}
                                                                    {obl.statusCode === 'PENDING' && contract.statusCode === 'ACTIVE' && (
                                                                        <button
                                                                            disabled={actionLoading}
                                                                            onClick={() => handleGenerateRequest(obl.id)}
                                                                            title="Gerar Pedido de Pagamento"
                                                                            style={{
                                                                                display: 'flex', alignItems: 'center', gap: '4px',
                                                                                padding: '6px 12px', borderRadius: 'var(--radius-md)',
                                                                                backgroundColor: '#059669', color: 'white',
                                                                                border: 'none', cursor: actionLoading ? 'default' : 'pointer',
                                                                                fontWeight: 700, fontSize: '0.75rem', fontFamily: 'var(--font-family-body)',
                                                                                opacity: actionLoading ? 0.5 : 1, whiteSpace: 'nowrap'
                                                                            }}
                                                                        >
                                                                            <DollarSign size={12} /> Gerar Pedido
                                                                        </button>
                                                                    )}
                                                                    {obl.linkedRequestStatusCode === 'DRAFT' && (
                                                                        <KebabMenu
                                                                            options={[
                                                                                {
                                                                                    label: 'Editar Pedido',
                                                                                    icon: <Edit size={14} />,
                                                                                    onClick: () => navigate(`/requests/${obl.linkedRequestId}/edit`),
                                                                                },
                                                                                {
                                                                                    label: 'Excluir Rascunho',
                                                                                    icon: <Trash2 size={14} color="#dc2626" />,
                                                                                    onClick: () => setRequestToDelete(obl.linkedRequestId!)
                                                                                }
                                                                            ]}
                                                                        />
                                                                    )}
                                                                </div>
                                                            )}
                                                        </td>
                                                    </tr>
                                                    {/* Deadline metadata expandable row */}
                                                    <ObligationDeadlinePanel obl={obl} formatDate={formatDate} />
                                                </>
                                            );
                                        })}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </motion.div>
                )}

                {activeTab === 'documents' && (
                    <motion.div key="documents" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
                        {contract.documents.length === 0 ? (
                            <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
                                Nenhum documento anexado.
                            </div>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                                {contract.documents.map(doc => (
                                    <div key={doc.id} style={{
                                        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                                        padding: '1rem', borderRadius: 'var(--radius-md)',
                                        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)'
                                    }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                            <FileText size={20} style={{ color: 'var(--color-primary)' }} />
                                            <div>
                                                <div style={{ fontWeight: 700, fontSize: '0.9rem' }}>{doc.fileName}</div>
                                                <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                                    {doc.documentType} • v{doc.versionNumber} • {(doc.fileSizeBytes / 1024).toFixed(0)} KB • {doc.uploadedByUserName} • {formatDate(doc.uploadedAtUtc)}
                                                </div>
                                            </div>
                                        </div>
                                        <a
                                            href={getDocumentDownloadUrl(contract.id, doc.id)}
                                            target="_blank"
                                            rel="noreferrer"
                                            style={{
                                                display: 'flex', alignItems: 'center', gap: '4px',
                                                padding: '6px 12px', borderRadius: 'var(--radius-md)',
                                                border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                                                color: 'var(--color-primary)', textDecoration: 'none', fontWeight: 600, fontSize: '0.8rem'
                                            }}
                                        >
                                            <Download size={14} /> Download
                                        </a>
                                    </div>
                                ))}
                            </div>
                        )}
                    </motion.div>
                )}

                {activeTab === 'history' && (
                    <motion.div key="history" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
                        {contract.histories.length === 0 ? (
                            <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
                                Nenhum registro de histórico.
                            </div>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                                {contract.histories.map(h => (
                                    <div key={h.id} style={{
                                        display: 'flex', gap: '1rem', padding: '1rem',
                                        borderLeft: '3px solid var(--color-primary)', backgroundColor: 'var(--color-bg-page)',
                                        borderRadius: '0 var(--radius-md) var(--radius-md) 0'
                                    }}>
                                        <div style={{ flex: 1 }}>
                                            <div style={{ fontWeight: 700, fontSize: '0.85rem' }}>{h.comment}</div>
                                            <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: '4px' }}>
                                                {h.actorUserName} • {formatDate(h.occurredAtUtc)} • {h.eventType}
                                                {h.fromStatusCode && h.toStatusCode && (
                                                    <span> • {h.fromStatusCode} → {h.toStatusCode}</span>
                                                )}
                                            </div>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </motion.div>
                )}

                {activeTab === 'alerts' && (
                    <motion.div key="alerts" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
                        {contract.alerts.length === 0 ? (
                            <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
                                Nenhum alerta ativo.
                            </div>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                                {contract.alerts.map(a => (
                                    <div key={a.id} style={{
                                        display: 'flex', alignItems: 'center', gap: '12px', padding: '1rem',
                                        borderRadius: 'var(--radius-md)', border: '1px solid #fbbf24',
                                        backgroundColor: '#fffbeb'
                                    }}>
                                        <AlertTriangle size={20} style={{ color: '#d97706', flexShrink: 0 }} />
                                        <div style={{ flex: 1 }}>
                                            <div style={{ fontWeight: 700, fontSize: '0.85rem', color: '#92400e' }}>{a.alertType}</div>
                                            <div style={{ fontSize: '0.8rem', color: '#a16207' }}>{a.message || `Vencimento: ${formatDate(a.triggerDateUtc)}`}</div>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </motion.div>
                )}
            </AnimatePresence>

            {/* Confirm Delete Draft Request Modal */}
            {requestToDelete && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 9999 }}>
                    <div style={{ backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-lg)', padding: '2rem', maxWidth: '420px', width: '90%', boxShadow: '0 20px 60px rgba(0,0,0,0.3)' }}>
                        <h3 style={{ margin: '0 0 0.75rem', fontSize: '1.1rem', fontWeight: 800, fontFamily: 'var(--font-family-display)', color: 'var(--color-text-main)' }}>
                            Excluir Rascunho do Pedido
                        </h3>
                        <p style={{ margin: '0 0 1.5rem', fontSize: '0.9rem', color: 'var(--color-text-muted)', lineHeight: 1.5 }}>
                            Tem a certeza que deseja excluir o rascunho de pedido vinculado? A obrigação de pagamento retornará ao status Pendente para geração de um novo pedido.
                        </p>
                        <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
                            <button
                                disabled={actionLoading}
                                onClick={() => setRequestToDelete(null)}
                                style={{ padding: '8px 16px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)', backgroundColor: 'transparent', cursor: 'pointer', fontWeight: 700, fontSize: '0.85rem' }}
                            >
                                Cancelar
                            </button>
                            <button
                                disabled={actionLoading}
                                onClick={handleConfirmDeleteDraftRequest}
                                style={{ padding: '8px 16px', borderRadius: 'var(--radius-md)', border: 'none', backgroundColor: '#dc2626', color: 'white', cursor: actionLoading ? 'default' : 'pointer', fontWeight: 700, fontSize: '0.85rem', opacity: actionLoading ? 0.6 : 1 }}
                            >
                                {actionLoading ? 'Excluindo...' : 'Excluir Rascunho'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </PageContainer>
    );
}

function InfoField({ label, value, highlight }: { label: string; value?: string | null; highlight?: boolean }) {
    return (
        <div>
            <div style={{ fontSize: '0.7rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '4px' }}>
                {label}
            </div>
            <div style={{
                fontSize: highlight ? '1rem' : '0.9rem',
                fontWeight: highlight ? 800 : 600,
                fontFamily: highlight ? 'var(--font-family-display)' : 'var(--font-family-body)',
                color: highlight ? 'var(--color-primary)' : 'var(--color-text-main)'
            }}>
                {value || '—'}
            </div>
        </div>
    );
}
