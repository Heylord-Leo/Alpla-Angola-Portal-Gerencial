// @ts-nocheck
/**
 * RequestStatusActionPanels
 *
 * Phase 2B extraction from RequestEdit.tsx (lines 376-557).
 * Purely presentational — renders the status/action panels that appear
 * as children of RequestActionHeader.
 *
 * Contains three distinct panel sections:
 * 1. Approval Banner — shown when status is WAITING_AREA_APPROVAL or WAITING_FINAL_APPROVAL
 * 2. Procurement/Buyer Status Panel — shown for post-approval statuses with role-based action buttons
 * 3. Quotation Status Panel — shown for QUOTATION-type requests in buyer treatment
 *
 * All state, handlers, permissions, and navigation are received via props.
 * No business logic is contained here.
 */
import React from 'react';
import {
    ShieldAlert,
    FileText,
    Edit2,
    ArrowRight,
    CheckCircle,
    Check,
    Clock
} from 'lucide-react';

export interface RequestStatusActionPanelsProps {
    // Identity
    requestId: string | null | undefined;

    // Workflow state
    status: string | undefined;
    requestTypeCode: string | undefined;

    // Role flags
    isBuyer: boolean;
    isAreaApprover: boolean;
    isFinalApprover: boolean;
    isFinance: boolean;
    isReceiving: boolean;

    // Permission flags
    canExecuteOperationalAction: boolean;
    isQuotationPartiallyEditable: boolean;

    // Modal setters
    setShowRegisterPoModal: (show: boolean) => void;
    setShowCorrectPoModal: (show: boolean) => void;
    setShowApprovalModal: (val: { show: boolean; type: string }) => void;

    // Navigation
    navigate: (to: string, options?: any) => void;
    onDrawerClose?: () => void;

    // Utility
    getRequestGuidance: (status: string, requestTypeCode: string) => { responsible: string; nextAction: string };
}

export function RequestStatusActionPanels({
    requestId,
    status,
    requestTypeCode,
    isBuyer, isAreaApprover, isFinalApprover, isFinance, isReceiving,
    canExecuteOperationalAction, isQuotationPartiallyEditable,
    setShowRegisterPoModal, setShowCorrectPoModal, setShowApprovalModal,
    navigate, onDrawerClose,
    getRequestGuidance
}: RequestStatusActionPanelsProps) {
    return (
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
                                navigate('/approvals', { state: { flashRequestId: requestId } });
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
                                {status === 'PAYMENT_COMPLETED' && (
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        <button 
                                            onClick={() => setShowApprovalModal({ show: true, type: 'MOVE_TO_RECEIPT' })}
                                            className="btn-primary"
                                            style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                        >
                                            <ArrowRight size={14} /> MOVER PARA RECEBIMENTO
                                        </button>
                                    </div>
                                )}
                            </div>
                        )}

                        {/* Finance actions */}
                        {status === 'PO_ISSUED' ? (
                            isFinance ? (
                                <div style={{ display: 'flex', gap: '8px' }}>
                                    <button 
                                        onClick={() => {
                                            if (onDrawerClose) onDrawerClose();
                                            navigate('/finance/payments', { state: { flashRequestId: requestId } });
                                        }}
                                        className="btn-primary"
                                        style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                    >
                                        <ArrowRight size={14} /> IR PARA PAGAMENTOS
                                    </button>
                                </div>
                            ) : (
                                <span style={{ fontSize: '0.75rem', color: '#64748b', fontStyle: 'italic', display: 'flex', alignItems: 'center', height: '32px' }}>
                                    A próxima ação deve ser realizada pelo Financeiro na tela de Pagamentos.
                                </span>
                            )
                        ) : (
                            isFinance && status === 'PAYMENT_SCHEDULED' ? (
                                <div style={{ display: 'flex', gap: '8px' }}>
                                    <button 
                                        onClick={() => setShowApprovalModal({ show: true, type: 'COMPLETE_PAYMENT' })}
                                        className="btn-primary"
                                        style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                    >
                                        <Check size={14} /> CONFIRMAR PAGAMENTO
                                    </button>
                                </div>
                            ) : (
                                isFinance && status === 'WAITING_RECEIPT' && (
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        <button 
                                            onClick={() => setShowApprovalModal({ show: true, type: 'FINALIZE' })}
                                            className="btn-success"
                                            style={{ height: '32px', padding: '0 12px', fontSize: '0.7rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                                        >
                                            <CheckCircle size={14} /> FINALIZAR PEDIDO
                                        </button>
                                    </div>
                                )
                            )
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
    );
}
