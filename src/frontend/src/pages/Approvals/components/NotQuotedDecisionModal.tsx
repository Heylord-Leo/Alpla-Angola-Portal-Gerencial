import { useState } from 'react';
import { AlertCircle, CheckCircle2, Loader2, X } from 'lucide-react';
import { api } from '../../../lib/api';

export type NotQuotedDecisionAction = 'ACCEPT' | 'REJECT';

interface NotQuotedDecisionModalProps {
    isOpen: boolean;
    onClose: () => void;
    action: NotQuotedDecisionAction;
    requestId: string;
    lineItemId: string;
    itemDescription: string;
    onSuccess: () => void;
}

const COPY: Record<NotQuotedDecisionAction, {
    title: string;
    intro: string;
    label: string;
    placeholder: string;
    confirmText: string;
    confirmingText: string;
    accentColor: string;
    accentBg: string;
}> = {
    ACCEPT: {
        title: 'Aceitar Item Não Cotado',
        intro: 'Confirmar que o item não pôde ser cotado, aceitando a justificativa do comprador?',
        label: 'Comentário (obrigatório)',
        placeholder: 'Ex: Justificativa procede, item confirmado como não cotado...',
        confirmText: 'Confirmar Aceite',
        confirmingText: 'Aceitando...',
        accentColor: '#059669',
        accentBg: '#d1fae5'
    },
    REJECT: {
        title: 'Rejeitar / Retornar ao Comprador',
        intro: 'O item voltará para a fila do comprador para ser cotado normalmente ou proposto novamente como não cotado com nova justificativa.',
        label: 'Motivo da rejeição (obrigatório)',
        placeholder: 'Ex: Justificativa insuficiente, existe fornecedor alternativo disponível...',
        confirmText: 'Retornar ao Comprador',
        confirmingText: 'Retornando...',
        accentColor: '#dc2626',
        accentBg: '#fee2e2'
    }
};

export function NotQuotedDecisionModal({
    isOpen,
    onClose,
    action,
    requestId,
    lineItemId,
    itemDescription,
    onSuccess
}: NotQuotedDecisionModalProps) {
    const [comment, setComment] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);

    if (!isOpen) return null;

    const copy = COPY[action];

    const handleConfirm = async () => {
        if (!comment.trim()) {
            setError('O comentário é obrigatório.');
            return;
        }

        setIsSubmitting(true);
        setError(null);

        try {
            if (action === 'ACCEPT') {
                await api.requests.acceptNotQuoted(requestId, lineItemId, comment.trim());
            } else {
                await api.requests.rejectNotQuoted(requestId, lineItemId, comment.trim());
            }
            onSuccess();
            onClose();
        } catch (err: any) {
            setError(err.message || 'Ocorreu um erro ao processar a decisão.');
        } finally {
            setIsSubmitting(false);
        }
    };

    const handleClose = () => {
        if (!isSubmitting) {
            setComment('');
            setError(null);
            onClose();
        }
    };

    return (
        <div
            style={{
                position: 'fixed',
                top: 0, left: 0, right: 0, bottom: 0,
                backgroundColor: 'rgba(17, 24, 39, 0.7)',
                backdropFilter: 'blur(4px)',
                zIndex: 10000,
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'flex-start',
                padding: '40px 24px',
                overflowY: 'auto'
            }}
            onClick={handleClose}
        >
            <div
                onClick={(e) => e.stopPropagation()}
                style={{
                    backgroundColor: '#FFFFFF',
                    borderRadius: '12px',
                    width: '100%',
                    maxWidth: '500px',
                    boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
                    display: 'flex',
                    flexDirection: 'column',
                    maxHeight: 'calc(100vh - 80px)'
                }}
            >
                {/* Header */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '20px 24px', borderBottom: '1px solid var(--color-border)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <div style={{ padding: '8px', backgroundColor: copy.accentBg, borderRadius: '8px' }}>
                            {action === 'ACCEPT' ? <CheckCircle2 size={24} color={copy.accentColor} /> : <AlertCircle size={24} color={copy.accentColor} />}
                        </div>
                        <div>
                            <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, color: copy.accentColor }}>
                                {copy.title}
                            </h2>
                        </div>
                    </div>
                    <button
                        onClick={handleClose}
                        style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)', padding: '4px' }}
                        disabled={isSubmitting}
                    >
                        <X size={20} />
                    </button>
                </div>

                {/* Body */}
                <div style={{ padding: '24px', overflowY: 'auto', flex: 1 }}>
                    <p style={{ margin: '0 0 8px 0', fontSize: '0.9rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                        {itemDescription}
                    </p>
                    <p style={{ margin: '0 0 16px 0', fontSize: '0.9rem', color: 'var(--color-text-muted)', lineHeight: '1.5' }}>
                        {copy.intro}
                    </p>

                    <div style={{ marginBottom: '16px' }}>
                        <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)', marginBottom: '8px' }}>
                            {copy.label} <span style={{ color: '#ef4444' }}>*</span>
                        </label>
                        <textarea
                            value={comment}
                            onChange={(e) => setComment(e.target.value)}
                            placeholder={copy.placeholder}
                            disabled={isSubmitting}
                            style={{
                                width: '100%',
                                minHeight: '100px',
                                padding: '12px',
                                border: '1px solid var(--color-border)',
                                borderRadius: '6px',
                                fontSize: '0.9rem',
                                resize: 'vertical',
                                fontFamily: 'inherit',
                                boxSizing: 'border-box'
                            }}
                        />
                        {error && (
                            <div style={{ marginTop: '8px', color: '#dc2626', fontSize: '0.8rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '4px' }}>
                                <AlertCircle size={14} />
                                <span>{error}</span>
                            </div>
                        )}
                    </div>
                </div>

                {/* Footer */}
                <div style={{ padding: '16px 24px', borderTop: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)', display: 'flex', justifyContent: 'flex-end', gap: '12px', borderBottomLeftRadius: '12px', borderBottomRightRadius: '12px' }}>
                    <button
                        onClick={handleClose}
                        disabled={isSubmitting}
                        style={{
                            padding: '8px 16px',
                            backgroundColor: '#fff',
                            border: '1px solid var(--color-border)',
                            borderRadius: '6px',
                            color: 'var(--color-text-main)',
                            fontWeight: 600,
                            cursor: isSubmitting ? 'not-allowed' : 'pointer',
                            opacity: isSubmitting ? 0.7 : 1
                        }}
                    >
                        Cancelar
                    </button>
                    <button
                        onClick={handleConfirm}
                        disabled={isSubmitting || !comment.trim()}
                        style={{
                            padding: '8px 16px',
                            backgroundColor: copy.accentColor,
                            border: 'none',
                            borderRadius: '6px',
                            color: '#fff',
                            fontWeight: 600,
                            cursor: (isSubmitting || !comment.trim()) ? 'not-allowed' : 'pointer',
                            opacity: (isSubmitting || !comment.trim()) ? 0.7 : 1,
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px'
                        }}
                    >
                        {isSubmitting ? (
                            <>
                                <Loader2 size={16} className="animate-spin" />
                                {copy.confirmingText}
                            </>
                        ) : (
                            copy.confirmText
                        )}
                    </button>
                </div>
            </div>
        </div>
    );
}
