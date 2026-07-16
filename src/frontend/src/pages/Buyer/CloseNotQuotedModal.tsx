import { useState } from 'react';
import { AlertCircle, Loader2, X, Ban } from 'lucide-react';
import { api } from '../../lib/api';

// Final Buyer decision — the item will no longer be considered in this
// quotation process. Distinct from the Wizard's per-document "Não cotado
// nesta cotação" mark, which keeps the item pending on the request.
export const CLOSE_NOT_QUOTED_REASONS = [
    'Fornecedor não possui o item',
    'Item não encontrado no mercado',
    'Item substituído por outro item',
    'Item não é mais necessário',
    'Especificação insuficiente para cotação',
    'Solicitante confirmou cancelamento',
    'Outro'
];

const MIN_JUSTIFICATION_LENGTH = 20;

interface CloseNotQuotedModalProps {
    isOpen: boolean;
    onClose: () => void;
    requestId: string;
    lineItemId: string;
    itemDescription: string;
    /**
     * Copy context: when this is the LAST pending item of the request, closing
     * it ends the quotation stage ("Encerrar sem cotação"); otherwise the
     * action only affects this one item ("Desconsiderar item"). Backend
     * endpoint and status are identical in both cases.
     */
    isLastPendingItem?: boolean;
    onSuccess: () => void;
}

export function CloseNotQuotedModal({
    isOpen,
    onClose,
    requestId,
    lineItemId,
    itemDescription,
    isLastPendingItem = false,
    onSuccess
}: CloseNotQuotedModalProps) {
    const [reason, setReason] = useState('');
    const [justification, setJustification] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);

    if (!isOpen) return null;

    const isValid = !!reason && justification.trim().length >= MIN_JUSTIFICATION_LENGTH;

    const copy = isLastPendingItem
        ? {
            title: 'Encerrar Item sem Cotação',
            intro: 'Este é o último item pendente do pedido. Ao confirmar, ele deixará de ser considerado neste processo de cotação e a etapa de cotação poderá avançar conforme as regras do sistema. Esta decisão ficará registrada no histórico do pedido.',
            confirmText: 'Encerrar sem Cotação',
            confirmingText: 'Encerrando...'
        }
        : {
            title: 'Desconsiderar Item',
            intro: 'Este item deixará de ser considerado neste processo de cotação e não aparecerá em novas cotações. Esta decisão ficará registrada no histórico do pedido.',
            confirmText: 'Desconsiderar Item',
            confirmingText: 'Desconsiderando...'
        };

    const handleConfirm = async () => {
        if (!isValid) {
            setError(`Selecione o motivo e informe uma justificativa com pelo menos ${MIN_JUSTIFICATION_LENGTH} caracteres.`);
            return;
        }

        setIsSubmitting(true);
        setError(null);

        try {
            await api.requests.closeNotQuoted(requestId, lineItemId, reason, justification.trim());
            setReason('');
            setJustification('');
            onSuccess();
            onClose();
        } catch (err: any) {
            setError(err.message || 'Ocorreu um erro ao encerrar o item sem cotação.');
        } finally {
            setIsSubmitting(false);
        }
    };

    const handleClose = () => {
        if (!isSubmitting) {
            setReason('');
            setJustification('');
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
                    maxWidth: '520px',
                    boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
                    display: 'flex',
                    flexDirection: 'column',
                    maxHeight: 'calc(100vh - 80px)'
                }}
            >
                {/* Header */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '20px 24px', borderBottom: '1px solid var(--color-border)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <div style={{ padding: '8px', backgroundColor: '#f3f4f6', borderRadius: '8px' }}>
                            <Ban size={24} color="#4b5563" />
                        </div>
                        <div>
                            <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, color: 'var(--color-text-main)' }}>
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
                    <p style={{ margin: '0 0 16px 0', fontSize: '0.85rem', color: 'var(--color-text-muted)', lineHeight: '1.5' }}>
                        {copy.intro}
                    </p>

                    <div style={{ marginBottom: '16px' }}>
                        <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)', marginBottom: '8px' }}>
                            Motivo <span style={{ color: '#ef4444' }}>*</span>
                        </label>
                        <select
                            value={reason}
                            onChange={(e) => setReason(e.target.value)}
                            disabled={isSubmitting}
                            style={{
                                width: '100%',
                                padding: '10px 12px',
                                border: '1px solid var(--color-border)',
                                borderRadius: '6px',
                                fontSize: '0.9rem',
                                backgroundColor: '#fff',
                                color: 'var(--color-text-main)'
                            }}
                        >
                            <option value="">-- Selecione o motivo --</option>
                            {CLOSE_NOT_QUOTED_REASONS.map(r => (
                                <option key={r} value={r}>{r}</option>
                            ))}
                        </select>
                    </div>

                    <div style={{ marginBottom: '8px' }}>
                        <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)', marginBottom: '8px' }}>
                            Justificativa (mínimo {MIN_JUSTIFICATION_LENGTH} caracteres) <span style={{ color: '#ef4444' }}>*</span>
                        </label>
                        <textarea
                            value={justification}
                            onChange={(e) => setJustification(e.target.value)}
                            placeholder="Ex: Após consulta aos fornecedores disponíveis, o item não foi encontrado para cotação..."
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
                        {justification.trim().length > 0 && justification.trim().length < MIN_JUSTIFICATION_LENGTH && (
                            <div style={{ marginTop: '6px', fontSize: '0.75rem', color: '#b45309', fontWeight: 600 }}>
                                {MIN_JUSTIFICATION_LENGTH - justification.trim().length} caractere(s) restante(s) para o mínimo.
                            </div>
                        )}
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
                        disabled={isSubmitting || !isValid}
                        style={{
                            padding: '8px 16px',
                            backgroundColor: '#4b5563',
                            border: 'none',
                            borderRadius: '6px',
                            color: '#fff',
                            fontWeight: 600,
                            cursor: (isSubmitting || !isValid) ? 'not-allowed' : 'pointer',
                            opacity: (isSubmitting || !isValid) ? 0.7 : 1,
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
