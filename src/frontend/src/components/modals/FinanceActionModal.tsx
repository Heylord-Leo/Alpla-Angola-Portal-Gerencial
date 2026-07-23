import { useState, useEffect, useRef } from 'react';
import { Upload, FileText, X, Calendar } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { DropdownPortal } from '../ui/DropdownPortal';
import { Z_INDEX } from '../../constants/ui';
import { Feedback, FeedbackType } from '../ui/Feedback';
import { CurrencyInput } from '../CurrencyInput';
import { formatBusinessDateOnly } from '../../lib/financePaymentsView';

export type FinanceActionType = 'SCHEDULE' | 'PAY' | 'RETURN' | 'NOTE' | 'CANCEL_SCHEDULE' | null;

export interface CancelScheduleContext {
    supplierName: string;
    loteNumber?: number | null;
    amount: number;
    currencyCode: string;
    scheduledDateUtc: string | null;
}

interface FinanceActionModalProps {
    show: boolean;
    action: FinanceActionType;
    isAdvance?: boolean;
    expectedAmount?: number;
    cancelScheduleContext?: CancelScheduleContext;
    onClose: () => void;
    onConfirm: (action: FinanceActionType, payload: { date?: string; notes?: string; file?: File | null; amount?: string }) => void;
    processing: boolean;
    feedback: { type: FeedbackType; message: string | null };
    onCloseFeedback: () => void;
}

const CANCEL_SCHEDULE_MIN_REASON_LENGTH = 20;

export function FinanceActionModal({
    show,
    action,
    isAdvance = false,
    expectedAmount,
    cancelScheduleContext,
    onClose,
    onConfirm,
    processing,
    feedback,
    onCloseFeedback
}: FinanceActionModalProps) {
    const [notes, setNotes] = useState('');
    const [date, setDate] = useState('');
    const [file, setFile] = useState<File | null>(null);
    const [amount, setAmount] = useState('');
    const fileInputRef = useRef<HTMLInputElement>(null);

    const handleClearFile = () => {
        setFile(null);
        if (fileInputRef.current) fileInputRef.current.value = '';
    };

    useEffect(() => {
        if (show) {
            setNotes('');
            setDate('');
            setFile(null);
            // Pre-fill amount from expectedAmount when opening PAY modal
            if (action === 'PAY' && expectedAmount && expectedAmount > 0) {
                setAmount(expectedAmount.toFixed(2));
            } else {
                setAmount('');
            }
        }
    }, [show, action, expectedAmount]);

    if (!show || !action) return null;

    const getTitle = () => {
        switch (action) {
            case 'SCHEDULE': return isAdvance ? 'Agendar Adiantamento' : 'Agendar Pagamento';
            case 'PAY': return isAdvance ? 'Confirmar Adiantamento' : 'Confirmar Liquidação';
            case 'RETURN': return 'Devolver Pedido para Ajuste';
            case 'NOTE': return 'Adicionar Observação';
            case 'CANCEL_SCHEDULE': return isAdvance ? 'Cancelar Agendamento de Adiantamento' : 'Cancelar Agendamento';
            default: return '';
        }
    };

    const getDescription = () => {
        switch (action) {
            case 'SCHEDULE': return 'Informe a data prevista para a saída do pagamento.';
            case 'PAY': return 'Informe o montante efetivamente pago e anexe o comprovante de pagamento.';
            case 'RETURN': return 'Descreva o motivo da devolução para que o setor de Compras possa realizar os ajustes necessários na P.O.';
            case 'NOTE': return 'Registre uma observação financeira sobre este pedido. Esta nota ficará visível no histórico de auditoria.';
            case 'CANCEL_SCHEDULE': return 'Esta ação cancela o agendamento atual deste grupo. O documento de cronograma anexado será marcado como sem efeito.';
            default: return '';
        }
    };

    const isCommentRequired = action === 'RETURN' || action === 'NOTE' || action === 'CANCEL_SCHEDULE';
    const showCommentField = action === 'RETURN' || action === 'PAY' || action === 'SCHEDULE' || action === 'NOTE' || action === 'CANCEL_SCHEDULE';

    // Determine button disabled state
    const isConfirmDisabled = processing ||
        (action === 'SCHEDULE' && !date) ||
        (action === 'PAY' && (!file || !amount || parseFloat(amount) <= 0 || !date)) ||
        (action === 'CANCEL_SCHEDULE' && notes.trim().length < CANCEL_SCHEDULE_MIN_REASON_LENGTH) ||
        (isCommentRequired && !notes.trim());

    const inputStyle = {
        width: '100%',
        padding: '12px 14px',
        backgroundColor: 'var(--color-bg-page)',
        border: '2px solid var(--color-border)',
        borderRadius: 'var(--radius-sm)',
        fontSize: '0.875rem',
        fontWeight: 600,
        color: 'var(--color-text-main)',
        transition: 'all 0.2s ease',
        fontFamily: 'inherit'
    };

    const formatDate = (d: string) => {
        if (!d) return '';
        const [year, month, day] = d.split('-');
        if (!year || !month || !day) return d;
        return `${day}/${month}/${year}`;
    };

    return (
        <DropdownPortal>
            <AnimatePresence>
                <motion.div
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    exit={{ opacity: 0 }}
                    style={{
                        position: 'fixed',
                        top: 0,
                        left: 0,
                        right: 0,
                        bottom: 0,
                        backgroundColor: 'rgba(0,0,0,0.8)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        zIndex: Z_INDEX.MODAL as any,
                        padding: '20px'
                    }}
                >
                    <motion.div
                        initial={{ scale: 0.9, y: 20 }}
                        animate={{ scale: 1, y: 0 }}
                        className="modal-content"
                        style={{
                            backgroundColor: 'var(--color-bg-surface)',
                            padding: '40px',
                            borderRadius: 'var(--radius-md)',
                            maxWidth: '500px',
                            width: '100%',
                            border: '1px solid var(--color-border)',
                            boxShadow: 'var(--shadow-md)'
                        }}
                    >
                        <h2 style={{ fontSize: '1.5rem', fontWeight: 900, marginBottom: '24px', color: 'var(--color-text-main)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            {getTitle()}
                        </h2>

                        <p style={{ marginBottom: '24px', fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.95rem' }}>
                            {getDescription()}
                        </p>

                        {action === 'SCHEDULE' && (
                            <div style={{ marginBottom: '24px' }}>
                                <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    Data de Agendamento (obrigatório)
                                </label>
                                <div style={{ position: 'relative' }}>
                                    <input
                                        type="date"
                                        value={date}
                                        onChange={(e) => setDate(e.target.value)}
                                        onClick={(e) => {
                                            if ('showPicker' in HTMLInputElement.prototype) {
                                                try { e.currentTarget.showPicker(); } catch (err) {}
                                            }
                                        }}
                                        style={{ ...inputStyle, opacity: 0, position: 'absolute', top: 0, left: 0, width: '100%', height: '100%', zIndex: 2, cursor: 'pointer', padding: 0 }}
                                    />
                                    <div style={{ ...inputStyle, position: 'relative', zIndex: 1, display: 'flex', alignItems: 'center', justifyContent: 'space-between', color: date ? 'var(--color-text-main)' : '#94a3b8' }}>
                                        <span>{date ? formatDate(date) : 'DD/MM/YYYY'}</span>
                                        <Calendar size={18} color="#94a3b8" />
                                    </div>
                                </div>
                                <label style={{ display: 'block', marginTop: '24px', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    Comprovante de Agendamento (opcional)
                                </label>
                                {!file ? (
                                    <div
                                        style={{
                                            border: '2px dashed var(--color-primary)', borderRadius: 'var(--radius-md)', padding: '24px', textAlign: 'center', backgroundColor: 'rgba(59, 130, 246, 0.02)', cursor: 'pointer', transition: 'all 0.2s ease'
                                        }}
                                        onClick={() => fileInputRef.current?.click()}
                                    >
                                        <input type="file" onChange={(e) => setFile(e.target.files && e.target.files.length > 0 ? e.target.files[0] : null)} style={{ display: 'none' }} ref={fileInputRef} />
                                        <Upload size={28} color="var(--color-primary)" style={{ margin: '0 auto 12px', opacity: 0.8 }} />
                                        <div style={{ fontWeight: 800, color: 'var(--color-primary)', fontSize: '0.9rem', marginBottom: '4px' }}>Clique para anexar o comprovante</div>
                                        <div style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>Max 10MB</div>
                                    </div>
                                ) : (
                                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 20px', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-text-main)', borderRadius: 'var(--radius-md)' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0 }}>
                                            <FileText size={20} color="var(--color-text-main)" />
                                            <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
                                                <span style={{ 
                                                    fontWeight: 800, 
                                                    color: 'var(--color-text-main)', 
                                                    fontSize: '0.85rem',
                                                    whiteSpace: 'nowrap',
                                                    overflow: 'hidden',
                                                    textOverflow: 'ellipsis'
                                                }}>
                                                    {file.name}
                                                </span>
                                                <span style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>{(file.size / 1024 / 1024).toFixed(2)} MB</span>
                                            </div>
                                        </div>
                                        <button onClick={handleClearFile} style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '4px', display: 'flex', color: 'var(--color-status-red)' }}>
                                            <X size={18} />
                                        </button>
                                    </div>
                                )}
                            </div>
                        )}

                        {action === 'PAY' && (
                            <div style={{ marginBottom: '24px' }}>
                                <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    Data do Pagamento (obrigatório)
                                </label>
                                <div style={{ position: 'relative' }}>
                                    <input
                                        type="date"
                                        value={date}
                                        onChange={(e) => setDate(e.target.value)}
                                        onClick={(e) => {
                                            if ('showPicker' in HTMLInputElement.prototype) {
                                                try { e.currentTarget.showPicker(); } catch (err) {}
                                            }
                                        }}
                                        style={{ ...inputStyle, opacity: 0, position: 'absolute', top: 0, left: 0, width: '100%', height: '100%', zIndex: 2, cursor: 'pointer', padding: 0 }}
                                    />
                                    <div style={{ ...inputStyle, position: 'relative', zIndex: 1, display: 'flex', alignItems: 'center', justifyContent: 'space-between', color: date ? 'var(--color-text-main)' : '#94a3b8' }}>
                                        <span>{date ? formatDate(date) : 'DD/MM/YYYY'}</span>
                                        <Calendar size={18} color="#94a3b8" />
                                    </div>
                                </div>
                                <div style={{ marginTop: '6px', fontSize: '0.7rem', fontWeight: 600, color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                    Informe a data constante no comprovativo de pagamento.
                                </div>
                            </div>
                        )}

                        {action === 'PAY' && (
                            <div style={{ marginBottom: '24px' }}>
                                <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    Montante Efetivamente Pago (obrigatório)
                                </label>
                                <CurrencyInput
                                    value={amount}
                                    onChange={(val) => setAmount(val)}
                                    placeholder="0,00"
                                    style={inputStyle}
                                />
                                {expectedAmount && expectedAmount > 0 && (
                                    <div style={{ marginTop: '8px', fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                        Valor esperado/agendado: {expectedAmount.toLocaleString('pt-AO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} AOA
                                    </div>
                                )}
                            </div>
                        )}

                        {action === 'PAY' && (
                            <div style={{ marginBottom: '24px' }}>
                                <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    Comprovante de Pagamento (obrigatório)
                                </label>
                                {!file ? (
                                    <div
                                        style={{
                                            border: '2px dashed var(--color-primary)', borderRadius: 'var(--radius-md)', padding: '24px', textAlign: 'center', backgroundColor: 'rgba(59, 130, 246, 0.02)', cursor: 'pointer', transition: 'all 0.2s ease'
                                        }}
                                        onClick={() => fileInputRef.current?.click()}
                                    >
                                        <input type="file" onChange={(e) => setFile(e.target.files && e.target.files.length > 0 ? e.target.files[0] : null)} style={{ display: 'none' }} ref={fileInputRef} />
                                        <Upload size={28} color="var(--color-primary)" style={{ margin: '0 auto 12px', opacity: 0.8 }} />
                                        <div style={{ fontWeight: 800, color: 'var(--color-primary)', fontSize: '0.9rem', marginBottom: '4px' }}>Clique para anexar o comprovante</div>
                                        <div style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>Max 10MB</div>
                                    </div>
                                ) : (
                                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 20px', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-text-main)', borderRadius: 'var(--radius-md)' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0 }}>
                                            <FileText size={20} color="var(--color-text-main)" />
                                            <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
                                                <span style={{ 
                                                    fontWeight: 800, 
                                                    color: 'var(--color-text-main)', 
                                                    fontSize: '0.85rem',
                                                    whiteSpace: 'nowrap',
                                                    overflow: 'hidden',
                                                    textOverflow: 'ellipsis'
                                                }}>
                                                    {file.name}
                                                </span>
                                                <span style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>{(file.size / 1024 / 1024).toFixed(2)} MB</span>
                                            </div>
                                        </div>
                                        <button onClick={handleClearFile} style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '4px', display: 'flex', color: 'var(--color-status-red)' }}>
                                            <X size={18} />
                                        </button>
                                    </div>
                                )}
                            </div>
                        )}

                        {action === 'CANCEL_SCHEDULE' && cancelScheduleContext && (
                            <div style={{ marginBottom: '24px', padding: '16px 20px', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)', borderRadius: 'var(--radius-md)', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', fontWeight: 700 }}>
                                    <span style={{ color: 'var(--color-text-muted)' }}>Fornecedor</span>
                                    <span>{cancelScheduleContext.supplierName}</span>
                                </div>
                                {cancelScheduleContext.loteNumber != null && (
                                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', fontWeight: 700 }}>
                                        <span style={{ color: 'var(--color-text-muted)' }}>Lote</span>
                                        <span>#{cancelScheduleContext.loteNumber}</span>
                                    </div>
                                )}
                                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', fontWeight: 700 }}>
                                    <span style={{ color: 'var(--color-text-muted)' }}>Montante</span>
                                    <span>{new Intl.NumberFormat('pt-AO', { style: 'currency', currency: cancelScheduleContext.currencyCode || 'AOA' }).format(cancelScheduleContext.amount)}</span>
                                </div>
                                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', fontWeight: 700 }}>
                                    <span style={{ color: 'var(--color-text-muted)' }}>Data agendada</span>
                                    <span>{formatBusinessDateOnly(cancelScheduleContext.scheduledDateUtc)}</span>
                                </div>
                                <div style={{ marginTop: '8px', display: 'flex', alignItems: 'flex-start', gap: '8px', fontSize: '0.75rem', fontWeight: 700, color: '#c2410c' }}>
                                    <span>⚠</span>
                                    <span>O documento de cronograma de pagamento anexado a este agendamento será marcado como "Sem efeito" e deixará de ser considerado ativo.</span>
                                </div>
                            </div>
                        )}

                        {showCommentField && (
                            <div style={{ marginBottom: '32px' }}>
                                <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    {action === 'NOTE' ? 'Observação (obrigatório)'
                                        : action === 'CANCEL_SCHEDULE' ? `Motivo do Cancelamento (obrigatório, mínimo ${CANCEL_SCHEDULE_MIN_REASON_LENGTH} caracteres)`
                                        : isCommentRequired ? 'Motivo da Devolução (obrigatório)' : 'Observações (opcional)'}
                                </label>
                                <textarea
                                    value={notes}
                                    onChange={(e) => setNotes(e.target.value)}
                                    placeholder="Digite aqui..."
                                    rows={4}
                                    style={{
                                        ...inputStyle,
                                        height: 'auto',
                                        padding: '16px'
                                    }}
                                />
                                {action === 'CANCEL_SCHEDULE' && (
                                    <div style={{ marginTop: '6px', fontSize: '0.7rem', fontWeight: 600, color: notes.trim().length < CANCEL_SCHEDULE_MIN_REASON_LENGTH ? '#dc2626' : 'var(--color-text-muted)' }}>
                                        {notes.trim().length}/{CANCEL_SCHEDULE_MIN_REASON_LENGTH} caracteres mínimos
                                    </div>
                                )}
                            </div>
                        )}

                        <Feedback
                            type={feedback.type}
                            message={feedback.message}
                            onClose={onCloseFeedback}
                        />

                        <div style={{ display: 'flex', gap: '16px', justifyContent: 'center' }}>
                            <button
                                onClick={onClose}
                                style={{
                                    height: '48px', padding: '0 32px', background: 'none', border: '1px solid var(--color-border)',
                                    cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                    fontFamily: 'var(--font-family-display)', fontSize: '0.875rem'
                                }}
                            >
                                CANCELAR
                            </button>
                            <button
                                disabled={isConfirmDisabled}
                                onClick={() => onConfirm(action, { date, notes, file, amount })}
                                style={{
                                    height: '48px',
                                    padding: '0 40px',
                                    backgroundColor: (action === 'RETURN' || action === 'CANCEL_SCHEDULE') ? 'var(--color-status-red)' : 'var(--color-primary)',
                                    color: '#fff',
                                    border: 'none',
                                    cursor: isConfirmDisabled ? 'not-allowed' : 'pointer',
                                    fontWeight: 800,
                                    borderRadius: 'var(--radius-sm)',
                                    boxShadow: isConfirmDisabled ? 'none' : 'var(--shadow-md)',
                                    fontFamily: 'var(--font-family-display)',
                                    fontSize: '0.875rem',
                                    opacity: isConfirmDisabled ? 0.6 : 1
                                }}
                            >
                                {processing ? 'PROCESSANDO...' : 'CONFIRMAR'}
                            </button>
                        </div>
                    </motion.div>
                </motion.div>
            </AnimatePresence>
        </DropdownPortal>
    );
}
