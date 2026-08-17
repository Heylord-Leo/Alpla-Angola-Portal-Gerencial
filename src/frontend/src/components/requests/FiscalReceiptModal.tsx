import { useState } from 'react';
import { AlertTriangle, FileCheck2 } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { api } from '../../lib/api';
import { operationInvoiceApi } from '../../lib/operationInvoiceApi';
import { mapOperationInvoiceError, formatUtcTimestampDate } from '../../lib/operationInvoiceView';
import type { CompletionReadinessGroupDto } from '../../types/operationInvoice';

interface FiscalReceiptModalProps {
    requestId: string;
    group: CompletionReadinessGroupDto;
    onClose: () => void;
    /** Refresh readiness/coverage after a successful (or concurrency-conflicted) binding. */
    onChanged: () => void;
}

/**
 * Release 4 Phase 4D — "Registrar Recibo Fiscal" (Finance/SysAdmin only; the caller gates the
 * CTA and the backend remains authoritative).
 *
 * Two-step flow mirroring the Phase 4B contract: the file is stored as TYPE_FISCAL_RECEIPT via
 * the standard attachment upload, then bound to the group through the fiscal-receipt endpoint —
 * the binding is what stamps the dimension, writes history and lets the completion engine act.
 * No OCR, no replacement flow: an already-bound group never reaches this modal.
 */
export function FiscalReceiptModal({ requestId, group, onClose, onChanged }: FiscalReceiptModalProps) {
    const [file, setFile] = useState<File | null>(null);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [isConcurrency, setIsConcurrency] = useState(false);
    const [done, setDone] = useState<{ completed: boolean } | null>(null);

    const submit = async () => {
        if (!file) {
            setError('Selecione o documento do Recibo Fiscal.');
            return;
        }
        setSaving(true);
        setError(null);
        setIsConcurrency(false);
        try {
            const uploaded = await api.attachments.upload(requestId, [file], 'FISCAL_RECEIPT', group.groupId);
            const attachmentId = Array.isArray(uploaded) && uploaded[0]?.id ? uploaded[0].id : null;
            if (!attachmentId) {
                setError('O carregamento do anexo não devolveu um identificador válido.');
                setSaving(false);
                return;
            }

            const result = await operationInvoiceApi.bindFiscalReceipt(requestId, group.groupId, attachmentId);
            setDone({ completed: result.completed });
            onChanged();
        } catch (err) {
            const mapped = mapOperationInvoiceError(err);
            setError(mapped.message);
            setIsConcurrency(mapped.isConcurrency);
        } finally {
            setSaving(false);
        }
    };

    const rowStyle: React.CSSProperties = { fontSize: '0.85rem' };

    return (
        <ModalWrapper title="Registrar Recibo Fiscal" onClose={onClose} width={560}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                <div style={{ fontWeight: 800 }}>{group.supplierName || '—'}</div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: '6px' }}>
                    <span style={rowStyle}>P.O.: <b>{group.purchaseOrderNumber || '—'}</b></span>
                    {group.plantName && <span style={rowStyle}>Planta: <b>{group.plantName}</b></span>}
                    <span style={rowStyle}>
                        Fatura Final: <b style={{ color: '#15803d' }}>
                            {group.closedShort ? 'Encerrado com Saldo Aceite' : 'Satisfeita'}
                        </b>
                    </span>
                    <span style={rowStyle}>Recebimento operacional: <b style={{ color: '#15803d' }}>Concluído</b></span>
                </div>

                <div style={{
                    fontSize: '0.83rem', color: 'var(--color-text-muted)',
                    backgroundColor: 'var(--color-bg-muted, rgba(0,0,0,0.04))',
                    borderRadius: '8px', padding: '10px 12px'
                }}>
                    O Recibo Fiscal confirma documentalmente o encerramento fiscal deste grupo.
                </div>

                {done ? (
                    <div style={{
                        border: '1px solid #86efac', backgroundColor: '#f0fdf4', borderRadius: '8px',
                        padding: '12px', display: 'flex', alignItems: 'flex-start', gap: '8px'
                    }}>
                        <FileCheck2 size={18} color="#15803d" style={{ flexShrink: 0, marginTop: '2px' }} />
                        <div style={{ fontSize: '0.85rem', color: '#166534' }}>
                            Recibo Fiscal registado com sucesso.
                            {done.completed
                                ? ' O grupo foi concluído.'
                                : ' A dimensão fiscal está satisfeita; o encerramento do grupo seguirá o ciclo de conclusão.'}
                        </div>
                    </div>
                ) : (
                    <div>
                        <label style={{
                            fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase',
                            color: 'var(--color-text-muted)', display: 'block', marginBottom: '4px'
                        }}>
                            Documento do Recibo Fiscal *
                        </label>
                        <input
                            type="file"
                            accept=".pdf,.jpg,.jpeg,.png"
                            onChange={e => setFile(e.target.files?.[0] ?? null)}
                            disabled={saving}
                        />
                    </div>
                )}

                {error && (
                    <div style={{
                        border: '1px solid #fca5a5', backgroundColor: '#fef2f2', borderRadius: '8px',
                        padding: '10px 12px', display: 'flex', flexDirection: 'column', gap: '8px'
                    }}>
                        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                            <AlertTriangle size={16} color="#b91c1c" style={{ flexShrink: 0, marginTop: '2px' }} />
                            <span style={{ fontSize: '0.83rem', color: '#991b1b' }}>{error}</span>
                        </div>
                        {isConcurrency && (
                            <button
                                type="button"
                                onClick={() => { onChanged(); onClose(); }}
                                style={{
                                    alignSelf: 'flex-start', padding: '6px 12px', borderRadius: '8px',
                                    border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                                    cursor: 'pointer', fontSize: '0.83rem', fontWeight: 700
                                }}
                            >
                                Recarregar dados
                            </button>
                        )}
                    </div>
                )}

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                    <button
                        type="button"
                        onClick={onClose}
                        disabled={saving}
                        style={{
                            padding: '8px 14px', borderRadius: '8px', border: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-surface)', cursor: 'pointer', fontWeight: 700
                        }}
                    >
                        {done ? 'Fechar' : 'Cancelar'}
                    </button>
                    {!done && (
                        <button
                            type="button"
                            onClick={() => void submit()}
                            disabled={saving || !file || isConcurrency}
                            style={{
                                padding: '8px 14px', borderRadius: '8px', border: 'none',
                                backgroundColor: saving || !file || isConcurrency ? '#94a3b8' : '#0f766e',
                                color: '#fff', cursor: saving || !file || isConcurrency ? 'not-allowed' : 'pointer',
                                fontWeight: 800
                            }}
                        >
                            {saving ? 'A registar…' : 'Confirmar Registo'}
                        </button>
                    )}
                </div>

                {group.fiscalReceipt && (
                    <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                        Recibo atual: {group.fiscalReceipt.fileName || '—'} ·{' '}
                        {formatUtcTimestampDate(group.fiscalReceipt.uploadedAtUtc)}
                    </div>
                )}
            </div>
        </ModalWrapper>
    );
}
