import React, { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { DropdownPortal } from '../ui/DropdownPortal';
import { Z_INDEX } from '../../constants/ui';
import { Feedback, FeedbackType } from '../ui/Feedback';

export type HRActionType = 'APPROVE' | 'REJECT' | 'CANCEL' | 'SYNC' | 'SYNC_DEPARTMENTS' | null;

interface HRActionModalProps {
    show: boolean;
    action: HRActionType;
    onClose: () => void;
    onConfirm: (action: HRActionType, payload: { notes?: string }) => void;
    processing: boolean;
    feedback?: { type: FeedbackType; message: string | null };
    onCloseFeedback?: () => void;
}

export function HRActionModal({
    show,
    action,
    onClose,
    onConfirm,
    processing,
    feedback,
    onCloseFeedback
}: HRActionModalProps) {
    const [notes, setNotes] = useState('');

    useEffect(() => {
        if (show) {
            setNotes('');
        }
    }, [show]);

    if (!show || !action) return null;

    const getTitle = () => {
        switch (action) {
            case 'APPROVE': return 'Confirmar Aprovação';
            case 'REJECT': return 'Motivo da Rejeição';
            case 'CANCEL': return 'Cancelar Pedido';
            case 'SYNC': return 'Sincronizar Innux';
            case 'SYNC_DEPARTMENTS': return 'Sincronizar Departamentos';
            default: return '';
        }
    };

    const getDescription = () => {
        switch (action) {
            case 'APPROVE': return 'Tem certeza que deseja aprovar este pedido de ausência?';
            case 'REJECT': return 'Por favor, informe o motivo pelo qual este pedido está a ser rejeitado.';
            case 'CANCEL': return 'Tem certeza que deseja cancelar este pedido? Esta ação não pode ser desfeita.';
            case 'SYNC': return 'Esta ação irá sincronizar a lista de funcionários do Innux para a BD do Portal. Pretende continuar?';
            case 'SYNC_DEPARTMENTS': return 'Esta ação irá importar a lista mestra de departamentos do Primavera (AlplaSOPRO e AlplaPLASTICOS) para o Portal. Alterações em departamentos inativos serão preservadas até que sejam reativados. Pretende continuar?';
            default: return '';
        }
    };

    const isCommentRequired = action === 'REJECT';
    const showCommentField = action === 'REJECT';
    
    const isConfirmDisabled = processing || (isCommentRequired && !notes.trim());

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

                        {showCommentField && (
                            <div style={{ marginBottom: '32px' }}>
                                <label style={{ display: 'block', marginBottom: '12px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                    Motivo (obrigatório)
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

                        {feedback && feedback.message && (
                            <div style={{ marginBottom: '24px' }}>
                                <Feedback
                                    type={feedback.type}
                                    message={feedback.message}
                                    onClose={onCloseFeedback || (() => {})}
                                />
                            </div>
                        )}

                        <div style={{ display: 'flex', gap: '16px', justifyContent: 'center' }}>
                            <button
                                onClick={onClose}
                                disabled={processing}
                                style={{
                                    height: '48px', padding: '0 32px', background: 'none', border: '2px solid var(--color-border-heavy)',
                                    cursor: processing ? 'not-allowed' : 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                    fontFamily: 'var(--font-family-display)', fontSize: '0.875rem'
                                }}
                            >
                                CANCELAR
                            </button>
                            <button
                                disabled={isConfirmDisabled}
                                onClick={() => onConfirm(action, { notes })}
                                style={{
                                    height: '48px',
                                    padding: '0 40px',
                                    backgroundColor: (action === 'REJECT' || action === 'CANCEL') ? 'var(--color-status-red)' : 'var(--color-primary)',
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
