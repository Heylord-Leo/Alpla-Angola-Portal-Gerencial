// @ts-nocheck
import { useRequestDetail } from './hooks/useRequestDetail';
import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { useNavigate, useParams, useLocation, useSearchParams } from 'react-router-dom';
import { 
    Save, 
    X, 
    ShieldCheck, 
    ShieldAlert, 
    Edit2, 
    Trash2, 
    Send, 
    ArrowLeft, 
    Plus,
    FileText,
    Clock,
    ArrowRight,
    CheckCircle,
    Check,
    AlertCircle,
    AlertTriangle,
    UserPlus
} from 'lucide-react';
import { QuickSupplierModal } from '../../components/Buyer/QuickSupplierModal';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../features/auth/AuthContext';
import { ROLES } from '../../constants/roles';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { SupplierAutocomplete } from '../../components/SupplierAutocomplete';
import { formatCurrencyAO, getRequestGuidance, formatDateTime } from '../../lib/utils';
import { CurrencyDto, LookupDto, RequestStatusHistoryDto, RequestAttachmentDto, RequestLineItemDto, SavedQuotationDto } from '../../types';
import { DateInput } from '../../components/DateInput';
import { RequestAttachments } from '../../components/RequestAttachments';
import { motion, AnimatePresence } from 'framer-motion';
import { completeQuotationAction } from '../../lib/workflow';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { RegisterPoModal } from '../../components/RegisterPoModal';
import { CorrectPoModal } from '../../components/CorrectPoModal';
import { RequestLineItemForm } from '../../components/RequestLineItemForm';
import { RequestActionHeader, BreadcrumbItem, OperationalGuidance } from './components/RequestActionHeader';
import { RequestQuotations } from './components/RequestQuotations';
import { scrollToFirstError } from '../../lib/validation';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';

export interface RequestEditProps { requestId?: string | null; onClose?: () => void; }
export function RequestEdit({ requestId: inputRequestId, onClose: onDrawerClose }: RequestEditProps = {}) {
    
    const {
        id,
        isCopyMode,
        copyFromId,
        loading,
        saving,
        submitting,
        feedback,
        setFeedback,
        fieldErrors,
        status,
        setStatus,
        statusFullName,
        statusBadgeColor,
        requestTypeCode,
        requestNumber,
        formData,
        setFormData,
        initialFormData,
        supplierName,
        setSupplierName,
        supplierPortalCode,
        lineItems,
        setLineItems,
        itemForm,
        setItemForm,
        itemSaving,
        statusHistory,
        setStatusHistory,
        attachments,
        setAttachments,
        quotations,
        selectedQuotationId,
        units,
        currencies,
        needLevels,
        departments,
        companies,
        plants,
        costCenters,
        ivaRates,
        sectionsOpen,
        toggleSection,
        isItemsHighlighted,
        isAttachmentsHighlighted,
        isQuotationsHighlighted,
        itemsSectionRef,
        quotationsSectionRef,
        showApprovalModal,
        setShowApprovalModal,
        showRegisterPoModal,
        setShowRegisterPoModal,
        showCorrectPoModal,
        setShowCorrectPoModal,
        approvalComment,
        setApprovalComment,
        approvalProcessing,
        modalFeedback,
        setModalFeedback,
        quickSupplierModal,
        setQuickSupplierModal,
        isBuyer,
        isAreaApprover,
        isFinalApprover,
        isFinance,
        isReceiving,
        isReworkStatus,
        isQuotationStage,
        isDraftEditable,
        isQuotationPartiallyEditable,
        isFullyReadOnly,
        hasSavedQuotations,
        canEditHeader,
        canEditSupplier,
        canEditItems,
        canManageAttachments,
        canExecuteOperationalAction,
        canEdit,
        canCancelRequest,
        handleChange,
        clearFieldError,
        handleSubmit,
        handleRequestAction,
        handleSubmitRequest,
        handleSaveItem,
        handleDeleteItem,
        handleDeleteRequest,
        handleAttachmentRefresh,
        loadData,
        navigate,
        location
    } = useRequestDetail({ id: inputRequestId || undefined, onClose: onDrawerClose });

    const isDrawerMode = !!onDrawerClose;

    const inputStyle = {
        width: '100%',
        padding: '12px 14px',
        borderRadius: 'var(--radius-md)',
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-sm)',
        transition: 'all 0.2s',
        backgroundColor: '#fcfcfc'
    };

    const sectionTitleStyle: React.CSSProperties = {
        fontSize: '0.85rem',
        fontWeight: 900,
        textTransform: 'uppercase',
        letterSpacing: '0.08em',
        color: 'var(--color-primary)',
        paddingBottom: '12px',
        borderBottom: '1px solid var(--color-border)',
        marginBottom: '24px',
        display: 'flex',
        alignItems: 'center',
        gap: '12px'
    };

    const labelStyle: React.CSSProperties = {
        display: 'flex',
        flexDirection: 'column',
        gap: '8px',
        fontSize: '0.7rem',
        fontWeight: 800,
        textTransform: 'uppercase',
        color: 'var(--color-text-muted)',
        letterSpacing: '0.02em'
    };

    const getFieldErrors = (fieldName: string) => {
        if (!fieldErrors) return null;
        const normalizedField = fieldName.toLowerCase();
        const key = Object.keys(fieldErrors).find(k => {
            const normalizedKey = k.toLowerCase().replace(/^\$\./, '');
            return normalizedKey === normalizedField || normalizedKey.endsWith('.' + normalizedField);
        });
        return key ? fieldErrors[key] : null;
    };

    const renderFieldError = (fieldName: string) => {
        const errors = getFieldErrors(fieldName);
        if (!errors) return null;
        return (
            <div style={{ color: '#EF4444', fontSize: '0.75rem', marginTop: '4px', position: 'absolute' }}>
                {errors[0]}
            </div>
        );
    };

    const getInputStyle = (fieldName: string) => ({
        ...inputStyle,
        ...(getFieldErrors(fieldName) ? { borderColor: '#EF4444', backgroundColor: '#FEF2F2', boxShadow: '0 0 0 4px rgba(239, 68, 68, 0.1)' } : {})
    });


    if (loading) {
        return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1400px', margin: '0 auto' }}>
                <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                <div style={{ padding: '20px', textAlign: 'center', backgroundColor: 'var(--color-bg-surface)', borderRadius: '8px', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}>
                    <div style={{ width: '40px', height: '40px', border: '3px solid var(--color-border)', borderTopColor: 'var(--color-primary)', borderRadius: '50%', animation: 'spin 1s linear infinite', margin: '0 auto 16px auto' }}></div>
                    <div style={{ fontWeight: 600, color: 'var(--color-text-main)' }}>Carregando detalhes do pedido...</div>
                </div>
            </div>
        );
    }

    // Defensive Guard: If data failed to load and we're not in copy mode, avoid white-screen crash
    // We check initialFormData as it's only populated after a successful loadData cycle.
    if (!loading && !initialFormData && !isCopyMode) {
        return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1400px', margin: '100px auto', textAlign: 'center' }}>
                <Feedback type={feedback.type} message={feedback.message || 'Erro ao carregar o pedido.'} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                <div style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '1.25rem' }}>O pedido solicitado não pôde ser encontrado ou carregado.</div>
                <div style={{ color: 'var(--color-text-muted)' }}>Isso pode ocorrer se o pedido foi excluído ou se há um problema de conexão.</div>
                <div style={{ display: 'flex', gap: '12px', justifyContent: 'center', marginTop: '24px' }}>
                    <button onClick={() => window.location.reload()} className="btn-primary" style={{ padding: '10px 24px' }}>Tentar Novamente</button>
                    <button onClick={() => navigate('/requests')} className="btn-secondary" style={{ padding: '10px 24px' }}>Voltar para a Lista</button>
                </div>
            </div>
        );
    }

    const headerProps = {
        breadcrumbs: [
            { label: 'Dashboard', to: '/' },
            { label: 'Pedidos', to: `/requests${location.state?.fromList || ''}` },
            { label: status === 'DRAFT' ? 'Editar' : isReworkStatus ? 'Reajustar' : 'Visualizar' }
        ] as BreadcrumbItem[],
        title: status === 'DRAFT' ? 'Editar Pedido' : isReworkStatus ? 'Reajustar Pedido' : 'Pedido',
        requestNumber,
        statusBadge: statusFullName && (
            <span className={`badge badge-sm badge-${
                statusBadgeColor === 'red' ? 'danger' :
                statusBadgeColor === 'yellow' ? 'warning' :
                statusBadgeColor === 'green' ? 'success' :
                statusBadgeColor || 'neutral'
            }`} style={{ marginLeft: '8px' }}>
                {statusFullName}
            </span>
        ),
        contextBadges: (
            <>
                {isFullyReadOnly && (
                    <span style={{
                        padding: '2px 8px', backgroundColor: 'var(--color-bg-page)', color: '#64748b',
                        borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800,
                        textTransform: 'uppercase', border: '1px solid #e2e8f0',
                        display: 'flex', alignItems: 'center', gap: '4px'
                    }}>
                        <ShieldAlert size={12} /> MODO DE CONSULTA
                    </span>
                )}
                {isQuotationPartiallyEditable && (
                    <span style={{
                        padding: '2px 8px', backgroundColor: '#e0f2fe', color: '#0369a1',
                        borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800,
                        textTransform: 'uppercase', border: '1px solid #bae6fd',
                        display: 'flex', alignItems: 'center', gap: '4px'
                    }}>
                        <ShieldCheck size={12} /> COTAÇÃO ATIVA
                    </span>
                )}
                {isReworkStatus && (
                    <span style={{
                        padding: '2px 8px', backgroundColor: '#fff7ed', color: '#ea580c',
                        borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800,
                        textTransform: 'uppercase', border: '1px solid #ffedd5',
                        display: 'flex', alignItems: 'center', gap: '4px'
                    }}>
                        <ShieldAlert size={12} /> REAJUSTE NECESSÁRIO
                    </span>
                )}
            </>
        ),
        secondaryActions: (
            <>
                <button
                    type="button"
                    onClick={() => navigate(`/requests`)}
                    style={{
                        height: '36px', padding: '0 16px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)',
                        backgroundColor: 'var(--color-bg-surface)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                        fontWeight: 800, fontFamily: 'var(--font-family-display)', fontSize: '0.7rem', color: 'var(--color-text-main)',
                        boxShadow: 'var(--shadow-sm)', transition: 'all 0.2s'
                    }}
                >
                    {isCopyMode ? <><X size={14} /> DESCARTAR CÓPIA</> : (status === 'DRAFT' ? <><X size={14} /> CANCELAR</> : <><ArrowLeft size={14} /> VOLTAR</>)}
                </button>
            </>
        ),
        primaryActions: (
            <>
                {status === 'DRAFT' && !isCopyMode && (
                    <button
                        type="button"
                        onClick={() => handleDeleteRequest()}
                        disabled={saving}
                        style={{
                            height: '36px', padding: '0 12px', borderRadius: 'var(--radius-sm)', border: '2px solid #EF4444',
                            backgroundColor: 'var(--color-bg-page)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                            fontWeight: 800, fontFamily: 'var(--font-family-display)', color: '#EF4444', fontSize: '0.75rem'
                        }}
                    >
                        <Trash2 size={14} /> EXCLUIR
                    </button>
                )}
                {canCancelRequest && status !== 'DRAFT' && (
                    <button
                        type="button"
                        onClick={() => setShowApprovalModal({ show: true, type: 'CANCEL_REQUEST' })}
                        disabled={saving || submitting}
                        style={{
                            height: '36px', padding: '0 16px', borderRadius: 'var(--radius-md)', border: '1px solid #EF4444',
                            backgroundColor: 'white', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                            fontWeight: 800, fontFamily: 'var(--font-family-display)', color: '#EF4444', fontSize: '0.7rem',
                            boxShadow: 'var(--shadow-sm)', transition: 'all 0.2s'
                        }}
                    >
                        <X size={14} /> CANCELAR PEDIDO
                    </button>
                )}
                {(isDraftEditable || isQuotationPartiallyEditable) && !isCopyMode && (
                    <button
                        onClick={handleSubmit}
                        disabled={saving}
                        className="btn-primary"
                        style={{ height: '36px', padding: '0 16px', display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.75rem', borderRadius: 'var(--radius-sm)' }}
                    >
                        <Save size={14} /> {saving ? 'SALVANDO...' : 'SALVAR'}
                    </button>
                )}
                {isDraftEditable && (
                    <button
                        onClick={handleSubmitRequest}
                        disabled={submitting || saving}
                        className="btn-primary"
                        style={{
                            height: '36px', padding: '0 16px', display: 'flex', alignItems: 'center', gap: '6px',
                            fontSize: '0.75rem', backgroundColor: 'var(--color-primary)', borderRadius: 'var(--radius-sm)'
                        }}
                    >
                        <Send size={14} /> {submitting ? 'PROCESSANDO...' : (isReworkStatus ? 'REENVIAR' : 'SUBMETER')}
                    </button>
                )}
            </>
        ),
        operationalGuidance: (status ? getRequestGuidance(status, requestTypeCode || '') : null) as OperationalGuidance | null,
        feedback,
        onCloseFeedback: () => setFeedback(prev => ({ ...prev, message: null })),
        isDrawerMode: !!onDrawerClose
    };

    return (

        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.4 }}
            style={{ display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', maxWidth: '1440px', margin: '0 auto', minWidth: 0 }}
        >

            {/* Sticky Header Unit - Feedback, Banners, and Main Action Header */}
            <RequestActionHeader {...headerProps}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    {/* Unified Approval Presentation (replaces legacy direct action buttons) */}
                    {(status === 'WAITING_AREA_APPROVAL' || status === 'WAITING_FINAL_APPROVAL') && (
                        <div style={{
                            backgroundColor: '#e0e7ff',
                            padding: '16px',
                            borderRadius: '4px',
                            border: '1px solid #c7d2fe',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            gap: '16px',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                <ShieldAlert size={20} color="#4338ca" />
                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                    <span style={{ fontWeight: 800, fontSize: '0.8rem', color: '#312e81', textTransform: 'uppercase' }}>EM PROCESSO DE APROVAÇÃO</span>
                                    <span style={{ fontSize: '0.75rem', color: '#3730a3', fontWeight: 500 }}>Este pedido encontra-se atualmente em análise no fluxo de aprovação.</span>
                                </div>
                            </div>
                            {((status === 'WAITING_AREA_APPROVAL' && isAreaApprover) || (status === 'WAITING_FINAL_APPROVAL' && isFinalApprover)) && (
                                <button
                                    type="button"
                                    onClick={() => {
                                        if (onDrawerClose) onDrawerClose();
                                        navigate('/approvals', { state: { flashRequestId: id } });
                                    }}
                                    style={{
                                        height: '36px', padding: '0 16px', borderRadius: 'var(--radius-md)', border: 'none',
                                        backgroundColor: '#4338ca', color: '#ffffff', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                                        fontWeight: 800, fontFamily: 'var(--font-family-display)', fontSize: '0.75rem',
                                        boxShadow: '0 2px 4px rgba(67, 56, 202, 0.2)', transition: 'all 0.2s', textTransform: 'uppercase', whiteSpace: 'nowrap'
                                    }}
                                >
                                    IR PARA CENTRO DE APROVAÇÕES
                                </button>
                            )}
                        </div>
                    )}


                    {/* Procurement/Buyer Status Panel (Former Action Bar) */}
                    {canExecuteOperationalAction && ['APPROVED', 'PO_ISSUED', 'WAITING_PO_CORRECTION', 'PAYMENT_SCHEDULED', 'PAYMENT_COMPLETED', 'WAITING_RECEIPT'].includes(status || '') && (
                        <div style={{
                            backgroundColor: 'white',
                            padding: '12px 24px',
                            borderRadius: '4px',
                            border: '1px solid var(--color-primary)',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '4px',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                             <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                                <span style={{ fontWeight: 800, fontSize: '0.75rem', color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                    Status do Fluxo
                                </span>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end' }}>
                                <div style={{ display: 'flex', gap: '24px' }}>
                                    <div style={{ display: 'flex', flexDirection: 'column' }}>
                                        <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Responsável atual</span>
                                        <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                            {getRequestGuidance(status || '', requestTypeCode).responsible}
                                        </span>
                                    </div>
                                    <div style={{ display: 'flex', flexDirection: 'column' }}>
                                        <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Próxima ação / Situação</span>
                                        <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                                            {getRequestGuidance(status || '', requestTypeCode).nextAction}
                                        </span>
                                    </div>
                                </div>

                                {/* ADDED: Operational Actions Buttons (Only visible to BUYER mode) */}
                                {/* Buyer actions */}
                                {isBuyer && (
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        {status === 'APPROVED' && (
                                            <button 
                                                onClick={() => setShowRegisterPoModal(true)}
                                                className="btn-primary"
                                                style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                            >
                                                <FileText size={14} /> REGISTRAR P.O
                                            </button>
                                        )}
                                        {status === 'WAITING_PO_CORRECTION' && (
                                            <button 
                                                onClick={() => setShowCorrectPoModal(true)}
                                                className="btn-primary"
                                                style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px', backgroundColor: '#ea580c' }}
                                            >
                                                <Edit2 size={14} /> CORRIGIR P.O
                                            </button>
                                        )}
                                        {/* TRANSITIONAL FALLBACK: Removed direct access from Buyer to enforce Receiving ownership */}
                                    </div>
                                )}

                                {/* Receiving actions */}
                                {isReceiving && (
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        {(status === 'PAYMENT_COMPLETED' || status === 'WAITING_RECEIPT') && (
                                            <div style={{ display: 'flex', gap: '8px' }}>
                                                <button 
                                                    onClick={() => setShowApprovalModal({ show: true, type: 'MOVE_TO_RECEIPT' })}
                                                    className="btn-primary"
                                                    style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                                >
                                                    <ArrowRight size={14} /> MOVER PARA RECEBIMENTO
                                                </button>
                                                <button 
                                                    onClick={() => setShowApprovalModal({ show: true, type: 'FINALIZE' })}
                                                    className="btn-success"
                                                    style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                                >
                                                    <CheckCircle size={14} /> FINALIZAR PEDIDO
                                                </button>
                                            </div>
                                        )}
                                    </div>
                                )}

                                {/* Finance actions */}
                                {isFinance && (
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        {status === 'PO_ISSUED' && (
                                            <button 
                                                onClick={() => setShowApprovalModal({ show: true, type: 'SCHEDULE_PAYMENT' })}
                                                className="btn-primary"
                                                style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                            >
                                                <Clock size={14} /> AGENDAR PAGAMENTO
                                            </button>
                                        )}
                                        {(status === 'PO_ISSUED' || status === 'PAYMENT_SCHEDULED') && (
                                            <button 
                                                onClick={() => setShowApprovalModal({ show: true, type: 'COMPLETE_PAYMENT' })}
                                                className="btn-primary"
                                                style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                            >
                                                <Check size={14} /> CONFIRMAR PAGAMENTO
                                            </button>
                                        )}
                                    </div>
                                )}
                            </div>
                        </div>
                    )}

                    {/* Quotation Status Panel (Former Action Bar) - Isolated for QUOTATION workflow */}
                    {canExecuteOperationalAction && requestTypeCode === 'QUOTATION' && isQuotationPartiallyEditable && (
                        <div style={{
                            backgroundColor: 'white',
                            padding: '12px 24px',
                            borderRadius: '4px',
                            border: '1px solid var(--color-status-blue)',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '4px',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                             <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                                <span style={{ fontWeight: 800, fontSize: '0.75rem', color: 'var(--color-status-blue)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                    Andamento do Pedido
                                </span>
                            </div>
                            <div style={{ display: 'flex', gap: '24px' }}>
                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                    <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Responsável atual</span>
                                    <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>Comprador</span>
                                </div>
                                <div style={{ display: 'flex', flexDirection: 'column' }}>
                                    <span style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>Próxima ação</span>
                                    <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>Pedido em tratamento do comprador</span>
                                </div>
                            </div>
                        </div>
                    )}
                </div>
            </RequestActionHeader>

            {/* Form Card */}
            <form onSubmit={handleSubmit} style={{
                display: 'flex',
                flexDirection: 'column',
                gap: '32px'
            }}>
                {isQuotationPartiallyEditable && (
                    <div style={{ 
                        margin: '0 0 24px', padding: '16px', borderRadius: 'var(--radius-lg)', 
                        backgroundColor: '#f0f9ff', border: '1px solid #0ea5e9',
                        display: 'flex', alignItems: 'center', gap: '16px'
                    }}>
                        <ShieldCheck size={24} color="#0ea5e9" />
                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                            <span style={{ fontWeight: 800, fontSize: '0.85rem', color: '#0369a1', textTransform: 'uppercase' }}>
                                Modo de Edição Parcial Ativo
                            </span>
                            <span style={{ fontSize: '0.8rem', color: '#0c4a6e' }}>
                                O pedido está em fase de cotação. Você pode ajustar o título, descrição e justificativa. 
                                {hasSavedQuotations ? ' Os itens e fornecedor estão bloqueados por existirem cotações salvas.' : ' Itens e fornecedor permanecem editáveis até que a primeira cotação seja registrada.'}
                            </span>
                        </div>
                    </div>
                )}

                <section style={{ backgroundColor: 'var(--color-bg-surface)', padding: '32px', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-brutal)', border: '2px solid var(--color-border-heavy)' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '24px', borderBottom: '3px solid var(--color-primary)', paddingBottom: '12px' }}>
                        <h2 style={{ ...sectionTitleStyle, borderBottom: 'none', marginBottom: 0, paddingBottom: 0, marginTop: 0, fontSize: '1.1rem', fontWeight: 900 }}>Dados Gerais do Pedido</h2>
                        {requestNumber && (
                            <div style={{
                                display: 'flex',
                                flexDirection: 'column',
                                alignItems: 'flex-end',
                                gap: '2px',
                                borderLeft: '2px solid var(--color-border)',
                                paddingLeft: '16px'
                            }}>
                                <span style={{ fontSize: '0.65rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>
                                    Código de Rastreio
                                </span>
                                <span style={{
                                    fontSize: '1.4rem',
                                    fontWeight: 900,
                                    color: 'var(--color-primary)',
                                    fontFamily: 'var(--font-family-display)',
                                    lineHeight: 1
                                }}>
                                    {requestNumber}
                                </span>
                            </div>
                        )}
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                        <label style={labelStyle}>
                            Título do Pedido <span style={{ color: 'red' }}>*</span>
                            <input
                                required
                                type="text"
                                name="title"
                                value={formData.title}
                                onChange={handleChange}
                                placeholder="Ex: Aquisição de Laptops para TI"
                                style={getInputStyle('Title')}
                                disabled={!canEditHeader}
                            />
                            {renderFieldError('Title')}
                        </label>

                        <label style={labelStyle}>
                            Descrição ou Justificativa <span style={{ color: 'red' }}>*</span>
                            <textarea
                                required
                                name="description"
                                value={formData.description}
                                onChange={handleChange}
                                rows={4}
                                placeholder="Explique o motivo e os detalhes primários..."
                                style={{ ...getInputStyle('Description'), resize: 'vertical' }}
                                disabled={!canEditHeader}
                            />
                            {renderFieldError('Description')}
                        </label>

                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '24px' }}>
                            <label style={labelStyle}>
                                Tipo de Pedido <span style={{ color: 'red' }}>*</span>
                                <select
                                    name="requestTypeId"
                                    value={formData.requestTypeId}
                                    onChange={handleChange}
                                    style={getInputStyle('RequestTypeId')}
                                    disabled={!canEditHeader || isQuotationPartiallyEditable || status !== 'DRAFT'}
                                >
                                    <option value={1}>COTAÇÃO (COM)</option>
                                    <option value={2}>PAGAMENTO (PAG)</option>
                                </select>
                                {renderFieldError('RequestTypeId')}
                            </label>

                            <label style={labelStyle}>
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                    <span>
                                        Fornecedor {Number(formData.requestTypeId) === 1 && <span style={{ color: 'red' }}>*</span>}
                                    </span>
                                    {canEditSupplier && (
                                        <button
                                            type="button"
                                            onClick={() => setQuickSupplierModal({
                                                show: true,
                                                initialName: supplierName,
                                                initialTaxId: '' // Persist if available in backend DTO later
                                            })}
                                            style={{ fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-primary)', background: 'none', border: 'none', cursor: 'pointer', padding: 0, textDecoration: 'underline', display: 'flex', alignItems: 'center', gap: '4px' }}
                                        >
                                            <UserPlus size={12} />
                                            + NOVO FORNECEDOR
                                        </button>
                                    )}
                                </div>
                                <SupplierAutocomplete
                                    initialName={supplierName}
                                    initialPortalCode={supplierPortalCode}
                                    onChange={(id, name) => {
                                        setFormData(prev => ({ ...prev, supplierId: id ? String(id) : '' }));
                                        setSupplierName(name);
                                        // Once selected, we don't strictly need to update supplierPortalCode state 
                                        // for the autocomplete itself as handles its own query, 
                                        // but it's good practice.
                                        setSupplierPortalCode('');
                                        clearFieldError('SupplierId');
                                    }}
                                    hasError={!!getFieldErrors('SupplierId')}
                                    className="mt-1"
                                    disabled={!canEditSupplier}
                                />
                                {canEditSupplier && !formData.supplierId && supplierName && (
                                    <div style={{ marginTop: '8px', padding: '10px 12px', backgroundColor: '#fff7ed', border: '1px solid #ffedd5', borderRadius: '4px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                            <AlertCircle size={16} color="#c2410c" />
                                            <span style={{ fontSize: '0.75rem', fontWeight: 600, color: '#9a3412' }}>
                                                Fornecedor sugerido <strong>"{supplierName}"</strong> não encontrado.
                                            </span>
                                        </div>
                                        <button
                                            type="button"
                                            onClick={() => setQuickSupplierModal({
                                                show: true,
                                                initialName: supplierName,
                                                initialTaxId: ''
                                            })}
                                            style={{ 
                                                backgroundColor: '#f97316', color: '#fff', border: 'none', padding: '4px 10px', 
                                                borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800, cursor: 'pointer',
                                                textTransform: 'uppercase'
                                            }}
                                        >
                                            CRIAR AGORA
                                        </button>
                                    </div>
                                )}
                                {isQuotationStage && (
                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                        Em tratamento pelo comprador.
                                    </span>
                                )}
                                {renderFieldError('SupplierId')}
                            </label>
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '24px' }}>
                            <label style={labelStyle}>
                                Grau de Necessidade <span style={{ color: 'red' }}>*</span>
                                <select name="needLevelId" value={formData.needLevelId} onChange={handleChange} style={getInputStyle('NeedLevelId')} disabled={!canEditHeader}>
                                    <option value="">-- Selecione --</option>
                                    {needLevels.filter(nl => nl.isActive || Number(formData.needLevelId) === nl.id).map(nl => (
                                        <option key={nl.id} value={nl.id}>{nl.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('NeedLevelId')}
                            </label>

                            <AnimatePresence>
                                {(requestTypeCode === 'QUOTATION' || Number(formData.requestTypeId) === 1 || requestTypeCode === 'PAYMENT' || Number(formData.requestTypeId) === 2) && (
                                    <motion.div
                                        initial={{ opacity: 0, height: 0 }}
                                        animate={{ opacity: 1, height: 'auto' }}
                                        exit={{ opacity: 0, height: 0 }}
                                        style={{ overflow: 'hidden' }}
                                    >
                                        <label style={labelStyle}>
                                            {(requestTypeCode === 'PAYMENT' || Number(formData.requestTypeId) === 2) ? 'Data de vencimento' : 'Necessário até (Data limite)'} <span style={{ color: 'red' }}>*</span>
                                            <DateInput
                                                required
                                                name="needByDateUtc"
                                                value={formData.needByDateUtc}
                                                onChange={(val) => {
                                                    setFormData(prev => ({ ...prev, needByDateUtc: val }));
                                                    clearFieldError('NeedByDateUtc');
                                                }}
                                                hasError={!!getFieldErrors('NeedByDateUtc')}
                                                style={getInputStyle('NeedByDateUtc')}
                                                disabled={!canEditHeader}
                                            />
                                            {renderFieldError('NeedByDateUtc')}
                                            {!getFieldErrors('NeedByDateUtc') && formData.needByDateUtc && new Date(formData.needByDateUtc).getTime() < new Date().setHours(0, 0, 0, 0) && (
                                                <div style={{ color: '#D97706', fontSize: '0.75rem', marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px', fontWeight: 600 }}>
                                                    <AlertTriangle size={12} />
                                                    {(requestTypeCode === 'PAYMENT' || Number(formData.requestTypeId) === 2) ? 'O documento está vencido.' : 'A data selecionada está no passado.'}
                                                </div>
                                            )}
                                        </label>
                                    </motion.div>
                                )}
                            </AnimatePresence>

                            <label style={labelStyle}>
                                Departamento <span style={{ color: 'red' }}>*</span>
                                <select name="departmentId" value={formData.departmentId} onChange={handleChange} style={getInputStyle('DepartmentId')} disabled={!canEditHeader}>
                                    <option value="">-- Selecione --</option>
                                    {departments.filter(d => d.isActive || Number(formData.departmentId) === d.id).map(d => (
                                        <option key={d.id} value={d.id}>{d.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('DepartmentId')}
                            </label>

                            <label style={labelStyle}>
                                Empresa <span style={{ color: 'red' }}>*</span>
                                <select 
                                    name="companyId" 
                                    value={formData.companyId} 
                                    onChange={handleChange} 
                                    style={{
                                        ...getInputStyle('CompanyId'),
                                        ...(lineItems.length > 0 || isQuotationPartiallyEditable || !canEditHeader ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                    }}
                                    disabled={!canEditHeader || isQuotationPartiallyEditable || lineItems.length > 0}
                                >
                                    <option value="">-- Selecione --</option>
                                    {companies.filter(c => c.isActive || Number(formData.companyId) === c.id).map(c => (
                                        <option key={c.id} value={c.id}>{c.name}</option>
                                    ))}
                                </select>
                                {lineItems.length > 0 && (
                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                        Empresa bloqueada pois o pedido já possui itens.
                                    </span>
                                )}
                                {renderFieldError('CompanyId')}
                            </label>

                            <label style={labelStyle}>
                                Planta <span style={{ color: 'red' }}>*</span>
                                <select 
                                    name="plantId" 
                                    value={formData.plantId} 
                                    onChange={handleChange} 
                                    style={{
                                        ...getInputStyle('PlantId'),
                                        ...(!formData.companyId || isQuotationPartiallyEditable || !canEditHeader ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                    }}
                                    disabled={!canEditHeader || isQuotationPartiallyEditable || !formData.companyId}
                                >
                                    <option value="">-- Selecione --</option>
                                    {plants.filter(p => (p.isActive && (!formData.companyId || p.companyId === Number(formData.companyId))) || formData.plantId === p.id.toString()).map(p => (
                                        <option key={p.id} value={p.id}>{p.name}</option>
                                    ))}
                                </select>
                                {!formData.companyId && (
                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                        Selecione uma empresa primeiro.
                                    </span>
                                )}
                                {renderFieldError('PlantId')}
                            </label>
                        </div>

                    </div>
                </section>



                {/* Section C: Resumo Financeiro do Pedido */}
                <CollapsibleSection
                    title="Resumo Financeiro do Pedido"
                    count={0}
                    isOpen={sectionsOpen.finance}
                    onToggle={() => toggleSection('finance')}
                >
                    <div style={{ padding: '32px' }}>
                        <div style={{ display: 'grid', gridTemplateColumns: '1.5fr 1.5fr 1fr', gap: '32px', alignItems: 'start' }}>
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', padding: '16px', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)', borderRadius: 'var(--radius-sm)' }}>
                                <span style={{ fontSize: '0.75rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>
                                    Valor Total Estimado
                                </span>
                                <span style={{ fontSize: '2rem', fontWeight: 800, color: 'var(--color-text-main)', fontFamily: 'var(--font-family-display)' }}>
                                    {formatCurrencyAO(Number(formData.estimatedTotalAmount) || 0)}
                                </span>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                    Este valor é recalculado automaticamente a partir dos itens inseridos ao recarregar a tela.
                                </span>
                            </div>

                            <label style={labelStyle}>
                                Desconto Global ({currencies.find(c => c.id === Number(formData.currencyId))?.code || ''})
                                <input
                                    type="number"
                                    min="0"
                                    step="0.01"
                                    value={formData.discountAmount}
                                    onChange={(e) => {
                                        setFormData(prev => ({ ...prev, discountAmount: e.target.value }));
                                        clearFieldError('DiscountAmount');
                                    }}
                                    disabled={!canEditHeader}
                                    placeholder="0.00"
                                    style={getInputStyle('DiscountAmount')}
                                />
                                <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                    Abatimento que reduz a base tributável deste pedido.
                                </span>
                                {renderFieldError('DiscountAmount')}
                            </label>

                            <label style={labelStyle}>
                                Moeda Principal do Pedido
                                <select
                                    name="currencyId"
                                    value={formData.currencyId}
                                    onChange={handleChange}
                                    style={{
                                        ...getInputStyle('CurrencyId'),
                                        ...(lineItems.length > 0 || !canEdit ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                                    }}
                                    disabled={lineItems.length > 0 || !canEditHeader}
                                >
                                    {currencies.map(c => (
                                        <option key={c.id} value={c.id}>{c.code} ({c.symbol})</option>
                                    ))}
                                </select>
                                {lineItems.length > 0 && (
                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                        Moeda bloqueada pois o pedido já possui itens.
                                    </span>
                                )}
                                {renderFieldError('CurrencyId')}
                            </label>
                        </div>
                    </div>
                </CollapsibleSection>

            </form>

            {/* Line Items Section */}
            <CollapsibleSection
                title={selectedQuotationId ? 'Itens da Cotação Vencedora' : 'Itens do Pedido'}
                count={selectedQuotationId ? (quotations.find(q => q.id === selectedQuotationId)?.items.length || 0) : lineItems.length}
                isOpen={sectionsOpen.items}
                onToggle={() => toggleSection('items')}
            >
                <div
                    id="items-section-container"
                    ref={itemsSectionRef}
                    style={{
                        padding: '32px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '24px',
                        transition: 'box-shadow 0.5s ease',
                        boxShadow: isItemsHighlighted ? 'inset 0 0 0 4px #EF4444' : 'none'
                    }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
                        <h2 id="items-section" style={{ ...sectionTitleStyle, borderBottom: 'none', marginBottom: 0, marginTop: 0 }}>
                            {selectedQuotationId ? 'Detalhamento da Proposta' : 'Itens da Requisição'}
                        </h2>
                        {selectedQuotationId && (
                            <span style={{ backgroundColor: '#4f46e5', color: '#fff', fontSize: '0.75rem', fontWeight: 900, padding: '4px 12px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                Autoridade: Cotação Selecionada
                            </span>
                        )}
                        {canEditItems && !itemForm && !selectedQuotationId && (
                            <button
                                type="button"
                                onClick={() => setItemForm({ itemPriority: 'MEDIUM', quantity: 1, unit: '' })}
                                className="btn-primary"
                                style={{ display: 'flex', alignItems: 'center', gap: '8px' }}
                            >
                                <Plus size={18} />
                                Adicionar Item
                            </button>
                        )}
                    </div>

                    {selectedQuotationId && (
                        <div style={{ backgroundColor: '#f0f4ff', padding: '16px', borderRadius: '8px', borderLeft: '4px solid #4f46e5', marginBottom: '16px' }}>
                            <p style={{ fontSize: '0.875rem', color: '#1e1b4b', margin: 0 }}>
                                <strong>Nota:</strong> Este pedido está vinculado à cotação vencedora. Os itens abaixo refletem a proposta comercial do fornecedor selecionado.
                            </p>
                        </div>
                    )}

                    <div style={{ overflowX: 'auto' }}>
                        <table className="alpla-table">
                            <thead>
                                <tr>
                                    <th>#</th>
                                    <th>Descrição</th>
                                    <th style={{ textAlign: 'center' }}>Unid.</th>
                                    <th style={{ textAlign: 'right' }}>Qtd</th>
                                    {!selectedQuotationId && <th>Planta</th>}
                                    {!selectedQuotationId && <th>IVA</th>}
                                    <th style={{ textAlign: 'right' }}>Preço Unit.</th>
                                    <th style={{ textAlign: 'right' }}>Total</th>
                                    {!selectedQuotationId && <th style={{ textAlign: 'center' }}>Ações</th>}
                                </tr>
                            </thead>
                            <tbody>
                                {selectedQuotationId ? (
                                    // PIVOT: Show items from the selected quotation
                                    (quotations.find(q => q.id === selectedQuotationId)?.items || []).map((item, idx) => (
                                        <tr key={item.id || idx}>
                                            <td>{idx + 1}</td>
                                            <td style={{ fontWeight: 600 }}>{item.description}</td>
                                            <td style={{ textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>{item.unitCode || '---'}</td>
                                            <td style={{ textAlign: 'right' }}>{item.quantity}</td>
                                            <td style={{ textAlign: 'right' }}>{formatCurrencyAO(item.unitPrice)}</td>
                                            <td style={{ textAlign: 'right', fontWeight: 700 }}>{formatCurrencyAO(item.lineTotal)}</td>
                                        </tr>
                                    ))
                                ) : lineItems.length === 0 ? (
                                    <tr>
                                        <td colSpan={9} style={{ textAlign: 'center', padding: '24px', color: 'var(--color-text-muted)' }}>Nenhum item adicionado a este pedido.</td>
                                    </tr>
                                ) : (
                                    lineItems.map(item => (
                                        <tr key={item.id}>
                                            <td>{item.lineNumber}</td>
                                            <td>{item.description}</td>
                                            <td style={{ textAlign: 'center' }}>{item.unit || '---'}</td>
                                            <td style={{ textAlign: 'right' }}>{item.quantity}</td>
                                            <td>{item.plantName || '---'}</td>
                                            <td>
                                                <span style={{ fontSize: '0.75rem' }}>{item.ivaRatePercent != null ? `${item.ivaRatePercent}%` : '---'}</span>
                                            </td>
                                            <td style={{ textAlign: 'right' }}>{formatCurrencyAO(item.unitPrice)}</td>
                                            <td style={{ textAlign: 'right', fontWeight: 600 }}>{formatCurrencyAO(item.totalAmount)}</td>
                                            <td style={{ padding: '8px', textAlign: 'center' }}>
                                                <div style={{ display: 'flex', gap: '8px', justifyContent: 'center' }}>
                                                    {canEditItems ? (
                                                        <>
                                                            <button onClick={() => setItemForm(item)} style={{ background: 'none', border: 'none', color: 'var(--color-primary)', cursor: 'pointer' }} title="Editar"><Edit2 size={16} /></button>
                                                            <button onClick={() => handleDeleteItem(item.id)} style={{ background: 'none', border: 'none', color: '#EF4444', cursor: 'pointer' }} title="Excluir"><Trash2 size={16} /></button>
                                                        </>
                                                    ) : (
                                                        <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Somente leitura</span>
                                                    )}
                                                </div>
                                            </td>
                                        </tr>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>

                    {itemForm && (
                        <motion.div
                            initial={{ opacity: 0, y: 10 }}
                            animate={{ opacity: 1, y: 0 }}
                        >
                            <RequestLineItemForm
                                itemForm={itemForm}
                                setItemForm={setItemForm}
                                onSaveItem={handleSaveItem}
                                itemSaving={itemSaving}
                                units={units as any}
                                fieldErrors={fieldErrors}
                                clearFieldError={clearFieldError}
                                companyId={formData.companyId ? Number(formData.companyId) : null}
                                plants={(plants || []).filter(p => !formData.companyId || Number(p.companyId) === Number(formData.companyId))}
                                requestTypeCode={requestTypeCode || undefined}
                                costCenters={costCenters}
                                ivaRates={ivaRates}
                            />
                        </motion.div>
                    )}
                </div>
            </CollapsibleSection>

            {/* Section: Cotações Salvas */}
            {requestTypeCode === 'QUOTATION' && status !== 'CANCELLED' && status !== 'REJECTED' && (
                <CollapsibleSection
                    title="Cotações Salvas"
                    count={quotations.length}
                    isOpen={sectionsOpen.quotations}
                    onToggle={() => toggleSection('quotations')}
                >
                    <div 
                        ref={quotationsSectionRef}
                        style={{ 
                            padding: '32px',
                            transition: 'box-shadow 0.6s ease-in-out, border-color 0.6s ease-in-out',
                            boxShadow: isQuotationsHighlighted ? 'inset 0 0 0 4px rgba(239, 68, 68, 0.2), 0 0 20px rgba(239, 68, 68, 0.15)' : 'none',
                            border: isQuotationsHighlighted ? '2px solid #EF4444' : 'none',
                            borderRadius: 'inherit'
                        }}
                    >
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                            <h2 style={{ ...sectionTitleStyle, margin: 0 }}>
                                Cotações Recebidas
                            </h2>
                            {isFinalApprover && status === 'WAITING_FINAL_APPROVAL' && requestTypeCode === 'QUOTATION' && (
                                <span style={{ backgroundColor: '#4f46e5', color: '#fff', fontSize: '0.7rem', fontWeight: 900, padding: '4px 10px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                    AÇÃO: SELECIONAR VENCEDORA
                                </span>
                            )}
                        </div>
                        
                        <RequestQuotations 
                            quotations={quotations}
                            isDrawerMode={isDrawerMode}
                            isFinalApproverMode={!!(isFinalApprover || isAreaApprover)}
                            isDecisionStage={status === 'WAITING_FINAL_APPROVAL' || status === 'WAITING_AREA_APPROVAL'}
                            onSelectWinner={async (qid) => {
                                try {
                                    await api.requests.selectQuotation(id!, qid);
                                    setFeedback({ type: 'success', message: 'Cotação vencedora selecionada!' });
                                    loadData();
                                } catch (err: any) {
                                    setFeedback({ type: 'error', message: err.message });
                                }
                            }}
                        />
                    </div>
                </CollapsibleSection>
            )}

            {/* Section: Pedido Anexos */}
            <CollapsibleSection
                title="Anexos do Pedido"
                count={attachments.length}
                isOpen={sectionsOpen.attachments}
                onToggle={() => toggleSection('attachments')}
            >
                <div style={{ padding: '32px' }}>
                    <RequestAttachments
                        id="attachments-section"
                        highlight={isAttachmentsHighlighted}
                        requestId={id || ''}
                        attachments={attachments}
                        canEdit={canManageAttachments}
                        onRefresh={handleAttachmentRefresh}
                        requestType={requestTypeCode || undefined}
                        status={status || undefined}
                    />
                </div>
            </CollapsibleSection>

            {/* Section D: Histórico do Pedido */}
            <CollapsibleSection
                title="Histórico do Pedido"
                count={statusHistory.length}
                isOpen={sectionsOpen.history}
                onToggle={() => toggleSection('history')}
            >
                <div style={{
                    padding: '32px'
                }}>
                    {statusHistory.length === 0 ? (
                        <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', backgroundColor: 'var(--color-bg-page)', border: '2px dashed var(--color-border)' }}>
                            Nenhum registro de histórico disponível para este pedido.
                        </div>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                            {statusHistory.map((entry, idx) => (
                                <div key={entry.id} style={{
                                    display: 'grid',
                                    gridTemplateColumns: 'minmax(150px, 1fr) 2fr 3fr',
                                    gap: '24px',
                                    padding: '20px',
                                    backgroundColor: idx % 2 === 0 ? 'var(--color-bg-surface)' : 'var(--color-bg-page)',
                                    borderLeft: '4px solid var(--color-primary)',
                                    position: 'relative'
                                }}>
                                    <div>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Data / Hora</div>
                                        <div style={{ fontSize: '0.85rem', fontWeight: 600 }}>
                                            {formatDateTime(entry.createdAtUtc)}
                                        </div>
                                    </div>
                                    <div>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Ação / Novo Status</div>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                            <span style={{ fontSize: '0.9rem', fontWeight: 800, color: 'var(--color-primary)' }}>
                                                {entry.actionTaken === 'CREATE' ? 'CRIAÇÃO' :
                                                    entry.actionTaken === 'UPDATE' ? 'ATUALIZAÇÃO' :
                                                        entry.actionTaken === 'SUBMIT' ? 'SUBMISSÃO' : 
                                                            entry.actionTaken === 'DOCUMENTO ADICIONADO' ? 'DOC. ANEXADO' :
                                                                entry.actionTaken === 'DOCUMENTO REMOVIDO' ? 'DOC. REMOVIDO' :
                                                                    entry.actionTaken === 'ITEM_ADICIONADO' ? 'ITEM ADICIONADO' :
                                                                        entry.actionTaken === 'ITEM_ALTERADO' ? 'ITEM ALTERADO' :
                                                                            entry.actionTaken === 'ITEM_REMOVIDO' ? 'ITEM REMOVIDO' :
                                                                                entry.actionTaken}
                                            </span>
                                            <span style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--color-text-main)' }}>
                                                ➡ {entry.newStatusName}
                                            </span>
                                        </div>
                                    </div>
                                    <div>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Responsável / Comentário</div>
                                        <div style={{ fontSize: '0.85rem' }}>
                                            <span style={{ fontWeight: 700, display: 'block', marginBottom: '4px' }}>{entry.actorName}</span>
                                            {entry.comment && (
                                                <span style={{ color: 'var(--color-text-muted)', fontStyle: 'italic', fontSize: '0.8rem' }}>"{entry.comment}"</span>
                                            )}
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            </CollapsibleSection>

            {/* Approval Modal */}
            <ApprovalModal
                selectedQuotationName={quotations.find(q => q.isSelected)?.supplierNameSnapshot}
                show={showApprovalModal.show}
                type={showApprovalModal.type}
                status={status}
                isReworkStatus={isReworkStatus}
                onClose={() => {
                    setShowApprovalModal({ show: false, type: null });
                    setApprovalComment('');
                    setModalFeedback({ type: 'error', message: null });
                }}
                onConfirm={(action) => handleRequestAction(action!)}
                comment={approvalComment}
                setComment={setApprovalComment}
                processing={approvalProcessing || saving || submitting}
                feedback={modalFeedback}
                onCloseFeedback={() => setModalFeedback(prev => ({ ...prev, message: null }))}
            />

            {id && (
                (() => {
                    let activeTotalAmount = Number(formData.estimatedTotalAmount) || 0;
                    let activeSupplierName = supplierName;
                    let activeCurrencyCode = currencies.find(c => c.id === Number(formData.currencyId))?.code || '';

                    if (requestTypeCode === 'QUOTATION' && quotations.some(q => q.isSelected)) {
                       const winner = quotations.find(q => q.isSelected)!;
                       activeTotalAmount = winner.totalAmount;
                       activeSupplierName = winner.supplierNameSnapshot;
                       activeCurrencyCode = winner.currency || activeCurrencyCode;
                    }

                    return (
                        <RegisterPoModal
                            show={showRegisterPoModal}
                            requestId={id}
                            requestData={{
                                totalAmount: activeTotalAmount,
                                supplierName: activeSupplierName,
                                currencyCode: activeCurrencyCode
                            }}
                            onClose={() => setShowRegisterPoModal(false)}
                            onSuccess={async (msg) => {
                                setShowRegisterPoModal(false);
                                setFeedback({ type: 'success', message: msg });
                                // Reload state
                                const data = await api.requests.get(id);
                                setStatus(data.statusCode);
                                setStatusFullName(data.statusName);
                                setStatusBadgeColor(data.statusBadgeColor);
                                setStatusHistory(data.statusHistory || []);
                                setAttachments(data.attachments || []);
                            }}
                        />
                    );
                })()
            )}

            {/* Correct PO Modal — only for WAITING_PO_CORRECTION correction flow (isolated from initial registration) */}
            <CorrectPoModal
                show={showCorrectPoModal}
                requestId={id}
                onClose={() => setShowCorrectPoModal(false)}
                onSuccess={async (msg) => {
                    setShowCorrectPoModal(false);
                    setFeedback({ type: 'success', message: msg });
                    const data = await api.requests.get(id);
                    setStatus(data.statusCode);
                    setStatusFullName(data.statusName);
                    setStatusBadgeColor(data.statusBadgeColor);
                    setStatusHistory(data.statusHistory || []);
                    setAttachments(data.attachments || []);
                }}
            />

            <QuickSupplierModal 
                isOpen={quickSupplierModal.show}
                onClose={() => setQuickSupplierModal({ show: false, initialName: '', initialTaxId: '' })}
                onSuccess={(supplier: { id: number; name: string; taxId?: string }) => {
                    setFormData(prev => ({ ...prev, supplierId: String(supplier.id) }));
                    setSupplierName(supplier.name);
                    setSupplierPortalCode('');
                    clearFieldError('SupplierId');
                }}
                initialName={quickSupplierModal.initialName}
                initialTaxId={quickSupplierModal.initialTaxId}
            />

        </motion.div >
    );
}
