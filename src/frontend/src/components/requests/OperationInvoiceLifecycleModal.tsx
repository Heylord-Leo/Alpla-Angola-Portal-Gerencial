import { useState } from 'react';
import { ModalWrapper } from '../common/ModalWrapper';
import { operationInvoiceApi } from '../../lib/operationInvoiceApi';
import { mapOperationInvoiceError, formatMoney } from '../../lib/operationInvoiceView';
import type { OperationInvoiceDto } from '../../types/operationInvoice';

export type LifecycleAction = 'void' | 'reject';

interface OperationInvoiceLifecycleModalProps {
    requestId: string;
    invoice: OperationInvoiceDto;
    action: LifecycleAction;
    onClose: () => void;
    onDone: () => void;
}

/**
 * Void ("registada por engano") and Reject (the Finance decision on the DOCUMENT — never a
 * rejection of the request itself). Both are terminal and require a written reason.
 */
export function OperationInvoiceLifecycleModal({
    requestId, invoice, action, onClose, onDone
}: OperationInvoiceLifecycleModalProps) {
    const [reason, setReason] = useState('');
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const isVoid = action === 'void';
    const title = isVoid ? 'Anular Fatura Final' : 'Rejeitar Fatura Final';

    const submit = async () => {
        if (reason.trim().length === 0) {
            setError(isVoid ? 'Indique o motivo da anulação.' : 'Indique o motivo da rejeição.');
            return;
        }
        setSaving(true);
        setError(null);
        try {
            if (isVoid) {
                await operationInvoiceApi.void(requestId, invoice.id, reason.trim(), invoice.rowVersion);
            } else {
                await operationInvoiceApi.reject(requestId, invoice.id, reason.trim(), invoice.rowVersion);
            }
            onDone();
        } catch (err) {
            const mapped = mapOperationInvoiceError(err);
            setError(mapped.isConcurrency
                ? `${mapped.message} Feche e reabra para recarregar os dados.`
                : mapped.message);
        } finally {
            setSaving(false);
        }
    };

    return (
        <ModalWrapper title={title} onClose={onClose} width={520}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                <div style={{ fontSize: '0.88rem' }}>
                    Fatura <b>{invoice.documentNumber || 'sem número'}</b>
                    {invoice.documentSeries ? ` (série ${invoice.documentSeries})` : ''} —{' '}
                    {formatMoney(invoice.grossAmount, invoice.currency)}
                </div>

                <div style={{
                    fontSize: '0.82rem', padding: '10px 12px', borderRadius: '8px', fontWeight: 600,
                    backgroundColor: isVoid ? '#f8fafc' : '#fff7ed',
                    border: `1px solid ${isVoid ? '#e2e8f0' : '#fdba74'}`,
                    color: isVoid ? '#475569' : '#9a3412'
                }}>
                    {isVoid
                        ? 'A anulação destina-se a registos feitos por engano, antes da validação. O documento e o ficheiro permanecem consultáveis no histórico.'
                        : 'Esta decisão rejeita ESTE DOCUMENTO — não o pedido. As distribuições ficam no histórico mas deixam de contribuir para a cobertura, e a cobertura dos grupos é recalculada. O fornecedor poderá registar uma fatura corrigida.'}
                </div>

                <div>
                    <label style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', display: 'block', marginBottom: '4px' }}>
                        Motivo *
                    </label>
                    <textarea
                        value={reason}
                        onChange={e => setReason(e.target.value)}
                        style={{
                            width: '100%', minHeight: '72px', padding: '8px 10px', boxSizing: 'border-box',
                            border: '1px solid var(--color-border)', borderRadius: '8px', fontSize: '0.88rem', resize: 'vertical'
                        }}
                    />
                </div>

                {error && <div style={{ color: '#b91c1c', fontSize: '0.85rem', fontWeight: 700 }}>{error}</div>}

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                    <button onClick={onClose} disabled={saving} style={{
                        padding: '9px 16px', border: '1px solid var(--color-border)', backgroundColor: '#fff',
                        borderRadius: '8px', fontWeight: 700, cursor: 'pointer'
                    }}>
                        Cancelar
                    </button>
                    <button onClick={() => void submit()} disabled={saving} style={{
                        padding: '9px 18px', border: 'none', borderRadius: '8px', fontWeight: 800, cursor: 'pointer',
                        backgroundColor: isVoid ? '#475569' : '#dc2626', color: '#fff', opacity: saving ? 0.7 : 1
                    }}>
                        {saving ? 'A processar…' : isVoid ? 'Anular Fatura' : 'Rejeitar Fatura'}
                    </button>
                </div>
            </div>
        </ModalWrapper>
    );
}
