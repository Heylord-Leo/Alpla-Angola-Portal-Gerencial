import { useState, useEffect } from 'react';
import { motion } from 'framer-motion';
import { DropdownPortal } from './DropdownPortal';

import { Feedback, FeedbackType } from './Feedback';
import { ShieldCheck } from 'lucide-react';
import { api } from '../../lib/api';

const Input = ({ label, type = 'text', value, onChange, placeholder, required, disabled }: any) => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
        <label style={{ fontSize: '0.875rem', fontWeight: 600, color: '#334155' }}>
            {label} {required && <span style={{ color: '#EF4444' }}>*</span>}
        </label>
        <input
            type={type}
            value={value}
            onChange={onChange}
            placeholder={placeholder}
            required={required}
            disabled={disabled}
            style={{
                width: '100%', padding: '10px 12px', borderRadius: 'var(--radius-md)',
                border: '1px solid #CBD5E1', fontSize: '0.875rem', fontFamily: 'inherit'
            }}
        />
    </div>
);

const Select = ({ label, value, onChange, disabled, options }: any) => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
        <label style={{ fontSize: '0.875rem', fontWeight: 600, color: '#334155' }}>{label}</label>
        <select
            value={value}
            onChange={onChange}
            disabled={disabled}
            style={{
                width: '100%', padding: '10px 12px', borderRadius: 'var(--radius-md)',
                border: '1px solid #CBD5E1', fontSize: '0.875rem', fontFamily: 'inherit',
                backgroundColor: '#fff'
            }}
        >
            {options.map((opt: any) => (
                <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
        </select>
    </div>
);

interface ReconciliationModalProps {
    show: boolean;
    requestId: string;
    onClose: () => void;
    onSuccess: (message: string) => void;
    totalPaid?: number; // Passed from parent if needed to show difference
}

export function ReconciliationModal({ show, requestId, onClose, onSuccess }: ReconciliationModalProps) {
    const [processing, setProcessing] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    
    // Form state
    const [finalInvoiceAmount, setFinalInvoiceAmount] = useState('');
    const [finalAcceptedAmount, setFinalAcceptedAmount] = useState('');
    const [deliveredAcceptedAmount, setDeliveredAcceptedAmount] = useState('');
    const [reconciliationDecision, setReconciliationDecision] = useState('NO_DIFFERENCE');
    const [reconciliationNotes, setReconciliationNotes] = useState('');
    
    // Additional notes
    const [creditNoteRequired, setCreditNoteRequired] = useState(false);
    const [creditNoteNumber, setCreditNoteNumber] = useState('');
    
    const [debitNoteRequired, setDebitNoteRequired] = useState(false);
    const [debitNoteNumber, setDebitNoteNumber] = useState('');
    
    const [refundRequired, setRefundRequired] = useState(false);
    const [refundAmount, setRefundAmount] = useState('');
    
    const [compensationFuturePayment, setCompensationFuturePayment] = useState(false);
    const [compensationNotes, setCompensationNotes] = useState('');

    useEffect(() => {
        if (show) {
            setFeedback({ type: 'error', message: null });
            setProcessing(false);
            setFinalInvoiceAmount('');
            setFinalAcceptedAmount('');
            setDeliveredAcceptedAmount('');
            setReconciliationDecision('NO_DIFFERENCE');
            setReconciliationNotes('');
            setCreditNoteRequired(false);
            setCreditNoteNumber('');
            setDebitNoteRequired(false);
            setDebitNoteNumber('');
            setRefundRequired(false);
            setRefundAmount('');
            setCompensationFuturePayment(false);
            setCompensationNotes('');
        }
    }, [show]);

    if (!show) return null;

    const handleSubmit = async () => {
        setFeedback({ type: 'error', message: null });
        
        if (!finalInvoiceAmount || !finalAcceptedAmount || !deliveredAcceptedAmount) {
            setFeedback({ type: 'error', message: 'Preencha todos os valores obrigatórios.' });
            return;
        }

        if (reconciliationDecision !== 'NO_DIFFERENCE' && !reconciliationNotes) {
            setFeedback({ type: 'error', message: 'Justifique a divergência nas notas.' });
            return;
        }

        setProcessing(true);
        try {
            const dto = {
                finalInvoiceAmount: parseFloat(finalInvoiceAmount),
                finalAcceptedAmount: parseFloat(finalAcceptedAmount),
                deliveredAcceptedAmount: parseFloat(deliveredAcceptedAmount),
                reconciliationDecision,
                reconciliationNotes: reconciliationNotes || undefined,
                creditNoteRequired,
                creditNoteNumber: creditNoteNumber || undefined,
                debitNoteRequired,
                debitNoteNumber: debitNoteNumber || undefined,
                refundRequired,
                refundAmount: refundAmount ? parseFloat(refundAmount) : undefined,
                compensationFuturePayment,
                compensationNotes: compensationNotes || undefined
            };

            const res = await api.requests.reconcile(requestId, dto);
            onSuccess(res.message);
            onClose();
        } catch (error: any) {
            setFeedback({
                type: 'error',
                message: error.message || 'Falha ao registrar reconciliação.'
            });
            setProcessing(false);
        }
    };

    return (
        <DropdownPortal>
            <div
                style={{
                    position: 'fixed',
                    top: 0,
                    left: 0,
                    right: 0,
                    bottom: 0,
                    backgroundColor: 'rgba(15, 23, 42, 0.4)',
                    backdropFilter: 'blur(4px)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    padding: '24px'
                }}
            >
                <motion.div
                    initial={{ opacity: 0, scale: 0.95, y: 10 }}
                    animate={{ opacity: 1, scale: 1, y: 0 }}
                    exit={{ opacity: 0, scale: 0.95, y: 10 }}
                    transition={{ duration: 0.2 }}
                    style={{
                        backgroundColor: '#FFFFFF',
                        borderRadius: 'var(--radius-xl)',
                        width: '100%',
                        maxWidth: '700px',
                        maxHeight: '90vh',
                        overflowY: 'auto',
                        boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 8px 10px -6px rgba(0, 0, 0, 0.1)'
                    }}
                    role="dialog"
                    aria-modal="true"
                    aria-labelledby="modal-title"
                >
                    <div style={{
                        padding: '24px 32px',
                        borderBottom: '1px solid #E2E8F0',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '12px',
                        backgroundColor: '#F8FAFC',
                        borderRadius: 'var(--radius-xl) var(--radius-xl) 0 0'
                    }}>
                        <div style={{
                            width: '40px',
                            height: '40px',
                            borderRadius: '10px',
                            backgroundColor: '#E0E7FF',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            color: '#4F46E5'
                        }}>
                            <ShieldCheck size={22} strokeWidth={2.5} />
                        </div>
                        <div>
                            <h2 id="modal-title" style={{ margin: 0, fontSize: '1.25rem', color: '#0F172A', fontWeight: 700, letterSpacing: '-0.01em' }}>
                                Reconciliação Financeira
                            </h2>
                            <p style={{ margin: '4px 0 0 0', fontSize: '0.875rem', color: '#64748B' }}>
                                Conferência entre valores pagos e valores da fatura/entrega.
                            </p>
                        </div>
                    </div>

                    <div style={{ padding: '32px' }}>
                        {feedback.message && (
                            <div style={{ marginBottom: '24px' }}>
                                <Feedback
                                    type={feedback.type}
                                    message={feedback.message}
                                    onClose={() => setFeedback({ type: 'error', message: null })}
                                />
                            </div>
                        )}

                        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '16px' }}>
                                <Input
                                    label="Valor Final da Fatura"
                                    type="number"
                                    value={finalInvoiceAmount}
                                    onChange={(e: any) => setFinalInvoiceAmount(e.target.value)}
                                    placeholder="Ex: 5000.00"
                                    required
                                    disabled={processing}
                                />
                                <Input
                                    label="Valor Final Aceito"
                                    type="number"
                                    value={finalAcceptedAmount}
                                    onChange={(e: any) => setFinalAcceptedAmount(e.target.value)}
                                    placeholder="Ex: 5000.00"
                                    required
                                    disabled={processing}
                                />
                                <Input
                                    label="Valor Entregue Aceito"
                                    type="number"
                                    value={deliveredAcceptedAmount}
                                    onChange={(e: any) => setDeliveredAcceptedAmount(e.target.value)}
                                    placeholder="Ex: 5000.00"
                                    required
                                    disabled={processing}
                                />
                            </div>

                            <Select
                                label="Decisão de Reconciliação"
                                value={reconciliationDecision}
                                onChange={(e: any) => setReconciliationDecision(e.target.value)}
                                disabled={processing}
                                options={[
                                    { value: 'NO_DIFFERENCE', label: 'Sem Divergência (Finalizar)' },
                                    { value: 'BALANCE_DUE', label: 'Saldo a Pagar' },
                                    { value: 'PARTIAL_DELIVERY', label: 'Entrega Parcial' },
                                    { value: 'INVOICE_HIGHER', label: 'Fatura Maior que Pedido' },
                                    { value: 'INVOICE_LOWER', label: 'Fatura Menor que Pedido' },
                                    { value: 'SUPPLIER_ISSUE', label: 'Problema com Fornecedor' }
                                ]}
                            />

                            {reconciliationDecision !== 'NO_DIFFERENCE' && (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                    <label style={{ fontSize: '0.875rem', fontWeight: 600, color: '#334155' }}>
                                        Notas de Reconciliação <span style={{ color: '#EF4444' }}>*</span>
                                    </label>
                                    <textarea
                                        value={reconciliationNotes}
                                        onChange={(e: any) => setReconciliationNotes(e.target.value)}
                                        placeholder="Descreva o motivo da divergência e ações a serem tomadas..."
                                        disabled={processing}
                                        style={{
                                            width: '100%',
                                            minHeight: '80px',
                                            padding: '12px',
                                            borderRadius: 'var(--radius-md)',
                                            border: '1px solid #CBD5E1',
                                            fontSize: '0.875rem',
                                            resize: 'vertical',
                                            fontFamily: 'inherit',
                                            transition: 'border-color 0.2s',
                                        }}
                                        onFocus={(e) => e.target.style.borderColor = '#3B82F6'}
                                        onBlur={(e) => e.target.style.borderColor = '#CBD5E1'}
                                    />
                                </div>
                            )}

                            <div style={{ borderTop: '1px solid #E2E8F0', paddingTop: '16px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                <h3 style={{ margin: 0, fontSize: '1rem', color: '#0F172A', fontWeight: 600 }}>Ações Adicionais (Opcional)</h3>
                                
                                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                                    <div>
                                        <label style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem', fontWeight: 500, color: '#334155', marginBottom: '8px' }}>
                                            <input type="checkbox" checked={creditNoteRequired} onChange={(e: any) => setCreditNoteRequired(e.target.checked)} disabled={processing} />
                                            Requer Nota de Crédito
                                        </label>
                                        {creditNoteRequired && (
                                            <Input
                                                label="Número da Nota"
                                                value={creditNoteNumber}
                                                onChange={(e: any) => setCreditNoteNumber(e.target.value)}
                                                placeholder="Nº da nota"
                                                disabled={processing}
                                            />
                                        )}
                                    </div>
                                    <div>
                                        <label style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem', fontWeight: 500, color: '#334155', marginBottom: '8px' }}>
                                            <input type="checkbox" checked={debitNoteRequired} onChange={(e: any) => setDebitNoteRequired(e.target.checked)} disabled={processing} />
                                            Requer Nota de Débito
                                        </label>
                                        {debitNoteRequired && (
                                            <Input
                                                label="Número da Nota"
                                                value={debitNoteNumber}
                                                onChange={(e: any) => setDebitNoteNumber(e.target.value)}
                                                placeholder="Nº da nota"
                                                disabled={processing}
                                            />
                                        )}
                                    </div>
                                </div>

                                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                                    <div>
                                        <label style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem', fontWeight: 500, color: '#334155', marginBottom: '8px' }}>
                                            <input type="checkbox" checked={refundRequired} onChange={(e: any) => setRefundRequired(e.target.checked)} disabled={processing} />
                                            Requer Reembolso
                                        </label>
                                        {refundRequired && (
                                            <Input
                                                label="Valor a Reembolsar"
                                                type="number"
                                                value={refundAmount}
                                                onChange={(e: any) => setRefundAmount(e.target.value)}
                                                placeholder="0.00"
                                                disabled={processing}
                                            />
                                        )}
                                    </div>
                                    <div>
                                        <label style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem', fontWeight: 500, color: '#334155', marginBottom: '8px' }}>
                                            <input type="checkbox" checked={compensationFuturePayment} onChange={(e: any) => setCompensationFuturePayment(e.target.checked)} disabled={processing} />
                                            Compensar Futuro
                                        </label>
                                        {compensationFuturePayment && (
                                            <Input
                                                label="Notas de Compensação"
                                                value={compensationNotes}
                                                onChange={(e: any) => setCompensationNotes(e.target.value)}
                                                placeholder="Detalhes..."
                                                disabled={processing}
                                            />
                                        )}
                                    </div>
                                </div>
                            </div>

                        </div>
                    </div>

                    <div style={{
                        padding: '20px 32px',
                        borderTop: '1px solid #E2E8F0',
                        display: 'flex',
                        justifyContent: 'flex-end',
                        gap: '12px',
                        backgroundColor: '#F8FAFC',
                        borderRadius: '0 0 var(--radius-xl) var(--radius-xl)'
                    }}>
                        <button
                            type="button"
                            onClick={onClose}
                            disabled={processing}
                            style={{
                                padding: '10px 18px',
                                fontSize: '0.875rem',
                                fontWeight: 600,
                                color: '#475569',
                                backgroundColor: 'transparent',
                                border: '1px solid #CBD5E1',
                                borderRadius: 'var(--radius-md)',
                                cursor: processing ? 'not-allowed' : 'pointer',
                                transition: 'all 0.2s',
                            }}
                            onMouseOver={(e) => {
                                if (!processing) {
                                    e.currentTarget.style.backgroundColor = '#F1F5F9';
                                    e.currentTarget.style.borderColor = '#94A3B8';
                                }
                            }}
                            onMouseOut={(e) => {
                                if (!processing) {
                                    e.currentTarget.style.backgroundColor = 'transparent';
                                    e.currentTarget.style.borderColor = '#CBD5E1';
                                }
                            }}
                        >
                            Cancelar
                        </button>

                        <button
                            type="button"
                            onClick={handleSubmit}
                            disabled={processing}
                            style={{
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '8px',
                                padding: '10px 24px',
                                fontSize: '0.875rem',
                                fontWeight: 600,
                                color: '#FFFFFF',
                                backgroundColor: '#4F46E5',
                                border: 'none',
                                borderRadius: 'var(--radius-md)',
                                cursor: processing ? 'not-allowed' : 'pointer',
                                transition: 'background-color 0.2s',
                                opacity: processing ? 0.7 : 1
                            }}
                            onMouseOver={(e) => {
                                if (!processing) e.currentTarget.style.backgroundColor = '#4338CA';
                            }}
                            onMouseOut={(e) => {
                                if (!processing) e.currentTarget.style.backgroundColor = '#4F46E5';
                            }}
                        >
                            {processing ? (
                                <>
                                    <svg className="animate-spin" style={{ width: '16px', height: '16px', color: '#FFFFFF' }} xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                    </svg>
                                    Salvando...
                                </>
                            ) : (
                                'Registrar Reconciliação'
                            )}
                        </button>
                    </div>
                </motion.div>
            </div>
        </DropdownPortal>
    );
}
