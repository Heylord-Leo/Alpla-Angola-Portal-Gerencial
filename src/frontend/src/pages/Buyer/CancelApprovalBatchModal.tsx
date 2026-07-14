import { useState } from 'react';
import { AlertCircle, Loader2, X } from 'lucide-react';
import { api } from '../../lib/api';

interface CancelApprovalBatchModalProps {
    isOpen: boolean;
    onClose: () => void;
    requestId: string;
    batchId: string;
    batchNumber: number;
    onSuccess: () => void;
}

export function CancelApprovalBatchModal({
    isOpen,
    onClose,
    requestId,
    batchId,
    batchNumber,
    onSuccess
}: CancelApprovalBatchModalProps) {
    const [justification, setJustification] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);

    if (!isOpen) return null;

    const handleConfirm = async () => {
        if (!justification.trim()) {
            setError('A justificativa é obrigatória.');
            return;
        }

        setIsSubmitting(true);
        setError(null);

        try {
            await api.requests.cancelApprovalBatch(requestId, batchId, justification.trim());
            onSuccess();
            onClose();
        } catch (err: any) {
            setError(err.message || 'Ocorreu um erro ao cancelar o lote.');
        } finally {
            setIsSubmitting(false);
        }
    };

    const handleClose = () => {
        if (!isSubmitting) {
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
                        <div style={{ padding: '8px', backgroundColor: '#fee2e2', borderRadius: '8px' }}>
                            <AlertCircle size={24} color="#dc2626" />
                        </div>
                        <div>
                            <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, color: '#dc2626' }}>
                                Anular Lote de Aprovação
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
                    <p style={{ margin: '0 0 16px 0', fontSize: '0.9rem', color: 'var(--color-text-main)', lineHeight: '1.5' }}>
                        Tem certeza que deseja cancelar o envio do <strong>Lote {batchNumber}</strong> para aprovação?
                        Os itens retornarão para a lista de cotações pendentes e a cotação original será preservada para auditoria.
                    </p>

                    <div style={{ marginBottom: '16px' }}>
                        <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)', marginBottom: '8px' }}>
                            Informe o motivo do cancelamento deste envio <span style={{ color: '#ef4444' }}>*</span>
                        </label>
                        <textarea
                            value={justification}
                            onChange={(e) => setJustification(e.target.value)}
                            placeholder="Ex: Cotação errada selecionada, valores incorretos..."
                            disabled={isSubmitting}
                            style={{
                                width: '100%',
                                minHeight: '100px',
                                padding: '12px',
                                border: '1px solid var(--color-border)',
                                borderRadius: '6px',
                                fontSize: '0.9rem',
                                resize: 'vertical'
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
                        Voltar
                    </button>
                    <button
                        onClick={handleConfirm}
                        disabled={isSubmitting || !justification.trim()}
                        style={{
                            padding: '8px 16px',
                            backgroundColor: '#dc2626',
                            border: 'none',
                            borderRadius: '6px',
                            color: '#fff',
                            fontWeight: 600,
                            cursor: (isSubmitting || !justification.trim()) ? 'not-allowed' : 'pointer',
                            opacity: (isSubmitting || !justification.trim()) ? 0.7 : 1,
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px'
                        }}
                    >
                        {isSubmitting ? (
                            <>
                                <Loader2 size={16} className="animate-spin" />
                                Anulando...
                            </>
                        ) : (
                            'Confirmar Anulação'
                        )}
                    </button>
                </div>
            </div>
        </div>
    );
}
