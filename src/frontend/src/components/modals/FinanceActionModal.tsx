import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { DropdownPortal } from '../ui/DropdownPortal';
import { Z_INDEX } from '../../constants/ui';
import { Feedback, FeedbackType } from '../ui/Feedback';

export type FinanceActionType = 'SCHEDULE' | 'PAY' | 'RETURN' | null;

interface FinanceActionModalProps {
    show: boolean;
    action: FinanceActionType;
    onClose: () => void;
    onConfirm: (action: FinanceActionType, payload: { date?: string; notes?: string; file?: File | null }) => void;
    processing: boolean;
    feedback: { type: FeedbackType; message: string | null };
    onCloseFeedback: () => void;
}

export function FinanceActionModal({
    show,
    action,
    onClose,
    onConfirm,
    processing,
    feedback,
    onCloseFeedback
}: FinanceActionModalProps) {
    const [notes, setNotes] = useState('');
    const [date, setDate] = useState('');
    const [file, setFile] = useState<File | null>(null);

    useEffect(() => {
        if (show) {
            setNotes('');
            setDate('');
            setFile(null);
        }
    }, [show]);

    if (!show || !action) return null;

    const getTitle = () => {
        switch (action) {
            case 'SCHEDULE': return 'Agendar Pagamento';
            case 'PAY': return 'Confirmar Liquidação';
            case 'RETURN': return 'Devolver Pedido para Ajuste';
            default: return '';
        }
    };

    const getDescription = () => {
        switch (action) {
            case 'SCHEDULE': return 'Informe a data prevista para a saída do pagamento.';
            case 'PAY': return 'Deseja confirmar que o pagamento deste pedido foi liquidado?';
            case 'RETURN': return 'Descreva o motivo da devolução para que o setor de Compras possa realizar os ajustes necessários na P.O.';
            default: return '';
        }
    };

    const isCommentRequired = action === 'RETURN';
    const showCommentField = action === 'RETURN' || action === 'PAY' || action === 'SCHEDULE';
    
    // Determine button disabled state
    const isConfirmDisabled = processing || 
        (action === 'SCHEDULE' && !date) || 
        (action === 'PAY' && !file) ||
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
                            border: '4px solid var(--color-border-heavy)',
                            boxShadow: 'var(--shadow-brutal)'
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
                                <input
                                    type="date"
                                    value={date}
                                    onChange={(e) => setDate(e.target.value)}
                                    style={inputStyle}
                                />
                                <label style={{ display: 'block', marginTop: '16px', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    Comprovante de Agendamento (opcional)
                                </label>
                                <input
                                    type="file"
                                    onChange={(e) => setFile(e.target.files && e.target.files.length > 0 ? e.target.files[0] : null)}
                                    style={{...inputStyle, padding: '8px 10px'}}
                                />
                            </div>
                        )}

                        {action === 'PAY' && (
                            <div style={{ marginBottom: '24px' }}>
                                <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    Comprovante de Pagamento (obrigatório)
                                </label>
                                <input
                                    type="file"
                                    onChange={(e) => setFile(e.target.files && e.target.files.length > 0 ? e.target.files[0] : null)}
                                    style={{...inputStyle, padding: '8px 10px'}}
                                />
                            </div>
                        )}

                        {showCommentField && (
                            <div style={{ marginBottom: '32px' }}>
                                <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    {isCommentRequired ? 'Motivo da Devolução (obrigatório)' : 'Observações (opcional)'}
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
                                    height: '48px', padding: '0 32px', background: 'none', border: '2px solid var(--color-border-heavy)',
                                    cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                    fontFamily: 'var(--font-family-display)', fontSize: '0.875rem'
                                }}
                            >
                                CANCELAR
                            </button>
                            <button
                                disabled={isConfirmDisabled}
                                onClick={() => onConfirm(action, { date, notes, file })}
                                style={{
                                    height: '48px',
                                    padding: '0 40px',
                                    backgroundColor: action === 'RETURN' ? 'var(--color-status-red)' : 'var(--color-primary)',
                                    color: '#fff',
                                    border: 'none',
                                    cursor: isConfirmDisabled ? 'not-allowed' : 'pointer',
                                    fontWeight: 800,
                                    borderRadius: 'var(--radius-sm)',
                                    boxShadow: isConfirmDisabled ? 'none' : '4px 4px 0 var(--color-accent)',
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
