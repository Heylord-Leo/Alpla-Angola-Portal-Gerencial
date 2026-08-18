import { useState } from 'react';
import { AlertTriangle, CheckCircle2, FileCheck2, Upload, X } from 'lucide-react';
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
 *
 * v2.229.9: presentation aligned with the Finance modal family (OperationInvoiceRegisterModal)
 * — Portal dashed upload area instead of the native "Choose File" control, shared label
 * typography, primary-token CTA, explicit ✓ evidence rows. Business semantics unchanged.
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

    // The Finance modal family conventions (OperationInvoiceRegisterModal).
    const labelStyle: React.CSSProperties = {
        fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)',
        textTransform: 'uppercase', marginBottom: '4px', display: 'block'
    };
    const rowStyle: React.CSSProperties = { fontSize: '0.85rem' };
    const evidenceStyle: React.CSSProperties = {
        display: 'flex', alignItems: 'center', gap: '6px',
        fontSize: '0.83rem', fontWeight: 700, color: '#15803d'
    };

    return (
        <ModalWrapper title="Registrar Recibo Fiscal" onClose={onClose} width={560}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                {/* ── Summary: supplier / P.O. ── */}
                <div style={{ fontWeight: 800, fontSize: '0.95rem' }}>{group.supplierName || '—'}</div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: '6px' }}>
                    <span style={rowStyle}>P.O.: <b>{group.purchaseOrderNumber || '—'}</b></span>
                    {group.plantName && <span style={rowStyle}>Planta: <b>{group.plantName}</b></span>}
                </div>

                {/* ── Status evidence (the two prerequisites the deriver already proved) ── */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    <span style={evidenceStyle}>
                        <CheckCircle2 size={15} />
                        {group.closedShort ? 'Fatura Final — Encerrado com Saldo Aceite' : 'Fatura Final satisfeita'}
                    </span>
                    <span style={evidenceStyle}>
                        <CheckCircle2 size={15} /> Recebimento operacional concluído
                    </span>
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
                        <label style={labelStyle}>Documento do Recibo Fiscal *</label>
                        {/* Portal upload area (the OperationInvoiceRegisterModal pattern) — the
                            native control never shows; the hidden input keeps accessibility. */}
                        <label style={{
                            display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 12px',
                            border: '1px dashed var(--color-border)', borderRadius: '8px',
                            cursor: saving ? 'not-allowed' : 'pointer',
                            fontSize: '0.85rem', fontWeight: 600,
                            color: file ? '#15803d' : 'var(--color-text-muted)'
                        }}>
                            {file ? <FileCheck2 size={16} /> : <Upload size={16} />}
                            <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                {file ? file.name : 'Selecionar o PDF/imagem do Recibo Fiscal'}
                            </span>
                            <input
                                type="file"
                                accept=".pdf,.jpg,.jpeg,.png"
                                style={{ display: 'none' }}
                                onChange={e => setFile(e.target.files?.[0] ?? null)}
                                disabled={saving}
                            />
                        </label>
                        {file && !saving && (
                            <button
                                type="button"
                                onClick={() => setFile(null)}
                                style={{
                                    marginTop: '6px', display: 'inline-flex', alignItems: 'center', gap: '4px',
                                    padding: '4px 8px', borderRadius: '6px',
                                    border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                                    cursor: 'pointer', fontSize: '0.75rem', fontWeight: 700,
                                    color: 'var(--color-text-muted)'
                                }}
                            >
                                <X size={12} /> Remover ficheiro
                            </button>
                        )}
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

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '4px' }}>
                    <button
                        type="button"
                        onClick={onClose}
                        disabled={saving}
                        style={{
                            padding: '9px 16px', border: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-surface)', borderRadius: '8px',
                            fontWeight: 700, cursor: 'pointer'
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
                                padding: '9px 18px', border: 'none',
                                backgroundColor: 'var(--color-primary)', color: '#fff',
                                borderRadius: '8px', fontWeight: 800,
                                cursor: saving || !file || isConcurrency ? 'not-allowed' : 'pointer',
                                opacity: saving || !file || isConcurrency ? 0.6 : 1
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
