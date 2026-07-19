import React, { useEffect, useMemo, useState } from 'react';
import { X, RefreshCcw, History, AlertCircle, CheckCircle2 } from 'lucide-react';
import { api } from '../../lib/api';
import { Z_INDEX } from '../../constants/ui';
import { DropdownPortal } from '../ui/DropdownPortal';
import { SavedQuotationDto, SavedQuotationItemDto } from '../../types';

interface QuotationReuseModalProps {
    isOpen: boolean;
    requestId: string;
    quotation: SavedQuotationDto | null;
    onClose: () => void;
    /** Called after a successful authorization so the caller refreshes its data. */
    onAuthorized: () => void;
}

/**
 * Option C — explicit Buyer confirmation to reuse quotation items previously used in a
 * CANCELLED approval batch. Creates one per-item authorization on the backend; nothing is
 * selected or batched automatically, and the cancelled batch is never modified.
 */
export function QuotationReuseModal({ isOpen, requestId, quotation, onClose, onAuthorized }: QuotationReuseModalProps) {
    const blockedItems = useMemo(
        () => (quotation?.items || []).filter((qi: SavedQuotationItemDto) => qi.isReuseBlocked),
        [quotation]
    );

    const [selected, setSelected] = useState<Record<string, boolean>>({});
    const [reason, setReason] = useState('');
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (isOpen) {
            // Pre-check all blocked items ("reuse entire quotation" default; user can uncheck for partial reuse)
            const all: Record<string, boolean> = {};
            blockedItems.forEach(qi => { all[qi.id] = true; });
            setSelected(all);
            setReason('');
            setError(null);
            setSaving(false);
        }
    }, [isOpen, quotation?.id]); // eslint-disable-line react-hooks/exhaustive-deps

    if (!isOpen || !quotation) return null;

    const selectedIds = Object.keys(selected).filter(id => selected[id]);
    const sourceBatches = Array.from(new Set(blockedItems.map(qi => qi.sourceCancelledBatchNumber).filter(n => n != null)));

    const handleConfirm = async () => {
        if (selectedIds.length === 0) { setError('Selecione pelo menos um item.'); return; }
        if (!reason.trim()) { setError('Informe o motivo da autorização.'); return; }
        setSaving(true);
        setError(null);
        try {
            await api.requests.authorizeQuotationReuse(requestId, quotation.id, selectedIds, reason.trim());
            onAuthorized();
            onClose();
        } catch (err: any) {
            setError(err.message || 'Falha ao autorizar o reuso.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <DropdownPortal>
            <div style={{
                position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.75)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                zIndex: Z_INDEX.MODAL as any, padding: '20px'
            }}>
                <div style={{
                    backgroundColor: 'var(--color-bg-surface)', padding: '32px', borderRadius: 'var(--radius-md)',
                    maxWidth: '560px', width: '100%', border: '1px solid var(--color-border)',
                    boxShadow: 'var(--shadow-md)', position: 'relative', maxHeight: '90vh', overflowY: 'auto'
                }}>
                    <button onClick={onClose} style={{ position: 'absolute', top: '16px', right: '16px', background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)' }}>
                        <X size={22} />
                    </button>

                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '16px' }}>
                        <History size={26} style={{ color: '#D97706' }} />
                        <h2 style={{ fontSize: '1.15rem', fontWeight: 900, margin: 0, textTransform: 'uppercase', color: 'var(--color-text-main)' }}>
                            Reutilizar cotação em novo lote
                        </h2>
                    </div>

                    <div style={{ fontSize: '0.8rem', color: 'var(--color-text-main)', marginBottom: '12px', lineHeight: 1.5 }}>
                        Cotação <strong>{quotation.documentNumber || 'S/N'}</strong> — <strong>{quotation.supplierNameSnapshot}</strong>.
                        Os itens abaixo foram utilizados no(s) <strong>Lote(s) #{sourceBatches.join(', #')}</strong> (cancelado) e, por isso,
                        não são elegíveis automaticamente para um novo lote.
                    </div>

                    <div style={{
                        backgroundColor: '#FFFBEB', border: '1px solid #F59E0B', borderRadius: 'var(--radius-sm)',
                        padding: '10px 12px', marginBottom: '16px', fontSize: '0.72rem', color: '#92400E', fontWeight: 600, lineHeight: 1.5
                    }}>
                        O lote cancelado e todo o histórico permanecem inalterados. Apenas os itens selecionados voltam a ser
                        elegíveis, e a autorização é consumida quando o item for usado num novo lote. Nenhum lote é criado nem
                        vencedor selecionado automaticamente.
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginBottom: '16px' }}>
                        {blockedItems.map(qi => (
                            <label key={qi.id} style={{
                                display: 'flex', alignItems: 'flex-start', gap: '10px', padding: '10px 12px',
                                border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', cursor: 'pointer',
                                backgroundColor: selected[qi.id] ? 'rgba(217, 119, 6, 0.06)' : 'transparent'
                            }}>
                                <input
                                    type="checkbox"
                                    checked={!!selected[qi.id]}
                                    onChange={e => setSelected(prev => ({ ...prev, [qi.id]: e.target.checked }))}
                                    style={{ marginTop: '2px' }}
                                />
                                <div style={{ flex: 1 }}>
                                    <div style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-main)' }}>{qi.description}</div>
                                    <div style={{ fontSize: '0.68rem', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                        Linha {qi.lineNumber} · Total {qi.lineTotal?.toLocaleString('pt-AO', { minimumFractionDigits: 2 })} ·
                                        Usado no Lote #{qi.sourceCancelledBatchNumber} (cancelado) · Estado atual: reuso não autorizado
                                    </div>
                                </div>
                            </label>
                        ))}
                    </div>

                    <div style={{ marginBottom: '16px' }}>
                        <label style={{ display: 'block', fontSize: '0.72rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '6px' }}>
                            Motivo da autorização <span style={{ color: 'var(--color-status-red)' }}>*</span>
                        </label>
                        <textarea
                            value={reason}
                            onChange={e => setReason(e.target.value)}
                            rows={3}
                            placeholder="Explique por que estes itens devem voltar a ser elegíveis..."
                            style={{
                                width: '100%', padding: '10px 12px', fontSize: '0.8rem',
                                border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)',
                                fontFamily: 'inherit', resize: 'vertical'
                            }}
                        />
                    </div>

                    {error && (
                        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', backgroundColor: '#FEF2F2', border: '1px solid #FCA5A5', borderRadius: 'var(--radius-sm)', padding: '10px 12px', marginBottom: '14px' }}>
                            <AlertCircle size={16} style={{ color: '#DC2626', flexShrink: 0, marginTop: '1px' }} />
                            <span style={{ fontSize: '0.75rem', fontWeight: 600, color: '#991B1B' }}>{error}</span>
                        </div>
                    )}

                    <div style={{ display: 'flex', gap: '12px' }}>
                        <button onClick={onClose} style={{
                            flex: 1, height: '44px', background: 'none', border: '1px solid var(--color-border)',
                            cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)', fontSize: '0.8rem'
                        }}>
                            CANCELAR
                        </button>
                        <button onClick={handleConfirm} disabled={saving || selectedIds.length === 0 || !reason.trim()} style={{
                            flex: 1, height: '44px', backgroundColor: '#D97706', color: '#fff', border: 'none',
                            cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)', fontSize: '0.8rem',
                            opacity: (saving || selectedIds.length === 0 || !reason.trim()) ? 0.6 : 1,
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px'
                        }}>
                            {saving ? <RefreshCcw size={15} style={{ animation: 'spin 1s linear infinite' }} /> : <CheckCircle2 size={15} />}
                            AUTORIZAR REUSO ({selectedIds.length})
                        </button>
                    </div>
                </div>
            </div>
        </DropdownPortal>
    );
}
