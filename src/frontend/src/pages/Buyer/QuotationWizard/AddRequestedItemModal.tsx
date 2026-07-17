import React, { useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { api, ApiError } from '../../../lib/api';
import { X, Save, FilePlus, AlertCircle, AlertTriangle, Loader2 } from 'lucide-react';

interface AddRequestedItemModalProps {
    requestId: string;
    sourceProformaAttachmentId: string | null;
    units: any[];
    initial: { description: string; quantity: number; unitId: number | null; itemCatalogId: number | null };
    onClose: () => void;
    /** Called with the created / idempotent / chosen-existing line item (shape from the backend projection). */
    onResolved: (item: any) => void;
}

/**
 * "Adicionar como item solicitado" — creates a REAL RequestLineItem from a proforma line to cover an
 * omitted requested item. Distinct from EXTRA_ITEM. Never sends a price (backend forces UnitPrice = 0).
 *
 * Idempotency: a single UUID is generated when the form opens and reused for every attempt of THIS
 * operation (retries + "create anyway"), so double-click / retry / confirm never duplicate.
 */
export const AddRequestedItemModal: React.FC<AddRequestedItemModalProps> = ({
    requestId,
    sourceProformaAttachmentId,
    units,
    initial,
    onClose,
    onResolved,
}) => {
    const [description, setDescription] = useState(initial.description || '');
    const [quantity, setQuantity] = useState<string>(initial.quantity ? String(initial.quantity) : '');
    const [unitId, setUnitId] = useState<string>(initial.unitId != null ? String(initial.unitId) : '');
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [mustRefresh, setMustRefresh] = useState(false);
    const [duplicate, setDuplicate] = useState<any | null>(null);

    // One stable idempotency key for the whole lifetime of this form (survives retries and "create anyway").
    const idempotencyKeyRef = useRef<string>(
        (typeof crypto !== 'undefined' && (crypto as any).randomUUID)
            ? (crypto as any).randomUUID()
            : `${Date.now()}-${Math.random().toString(36).slice(2)}`
    );

    const validate = (): string | null => {
        if (!description.trim()) return 'A descrição é obrigatória.';
        if (!(Number(quantity) > 0)) return 'A quantidade deve ser maior que zero.';
        if (!unitId) return 'A unidade é obrigatória.';
        return null;
    };

    const submit = async (confirmCreateDespiteDuplicate: boolean) => {
        if (submitting) return; // guard against double submit
        const v = validate();
        if (v) { setError(v); return; }
        setError(null);
        setSubmitting(true);
        try {
            const res = await api.requests.createLineItemFromProforma(requestId, {
                description: description.trim(),
                quantity: Number(quantity),
                unitId: Number(unitId),
                itemCatalogId: initial.itemCatalogId ?? null,
                sourceProformaAttachmentId: sourceProformaAttachmentId ?? null,
                idempotencyKey: idempotencyKeyRef.current,
                confirmCreateDespiteDuplicate,
            });

            if (res && res.duplicateSuspected) {
                setDuplicate(res);
                setSubmitting(false);
                return;
            }

            const item = res?.item;
            if (!item || !item.id) {
                setError('Resposta inesperada do servidor. Atualize o pedido e verifique se o item foi criado.');
                setSubmitting(false);
                return;
            }
            onResolved(item); // parent upserts + auto-maps + closes
        } catch (err: any) {
            const status = err instanceof ApiError ? err.status : undefined;
            if (status === 401 || status === 403) {
                setError('Você não tem permissão para adicionar itens solicitados neste pedido.');
            } else if (status === 400) {
                setError(err.message || 'Dados inválidos. Verifique os campos.');
            } else if (status === 409) {
                setError('O pedido mudou de estado (por exemplo, um lote de aprovação foi criado). Feche e atualize o pedido antes de tentar novamente.');
                setMustRefresh(true);
            } else {
                setError((err && err.message) || 'Falha de comunicação. O item pode ter sido criado — ao tentar novamente, o sistema evita duplicar.');
            }
            setSubmitting(false);
        }
    };

    const inputStyle: React.CSSProperties = {
        width: '100%', padding: '10px', border: '1px solid #E2E8F0', borderRadius: '6px', fontSize: '0.875rem', fontFamily: 'inherit',
    };
    const labelStyle: React.CSSProperties = { display: 'block', fontSize: '0.75rem', fontWeight: 600, color: '#64748b', marginBottom: '4px', textTransform: 'uppercase' };

    return createPortal(
        <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.6)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 10000, padding: '20px' }}>
            <div style={{ backgroundColor: '#fff', borderRadius: '12px', width: '100%', maxWidth: '520px', boxShadow: '0 10px 25px rgba(0,0,0,0.2)', overflow: 'hidden' }}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 20px', borderBottom: '1px solid #E2E8F0' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <FilePlus size={20} style={{ color: 'var(--color-primary)' }} />
                        <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: '#1e293b' }}>Adicionar como item solicitado</h3>
                    </div>
                    <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}><X size={20} /></button>
                </div>

                <div style={{ padding: '20px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    <div style={{ fontSize: '0.8125rem', color: '#64748b', backgroundColor: '#f8fafc', border: '1px solid #E2E8F0', borderRadius: '6px', padding: '10px 12px' }}>
                        O item será criado no pedido <strong>sem preço</strong> (o valor virá da cotação/aprovação). Revise os dados antes de confirmar.
                    </div>

                    {error && (
                        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '10px 12px', color: '#b91c1c', fontSize: '0.8125rem' }}>
                            <AlertCircle size={16} style={{ flexShrink: 0, marginTop: '1px' }} /> <span>{error}</span>
                        </div>
                    )}

                    {duplicate ? (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                            <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', backgroundColor: '#fffbeb', border: '1px solid #fcd34d', borderRadius: '6px', padding: '12px', color: '#92400e', fontSize: '0.8125rem' }}>
                                <AlertTriangle size={18} style={{ flexShrink: 0, marginTop: '1px' }} />
                                <span>
                                    Já existe um item semelhante neste pedido{duplicate.existingLineNumber ? ` (linha ${duplicate.existingLineNumber})` : ''}: <strong>{duplicate.existingDescription}</strong>. Deseja utilizar o item existente ou criar outro? Dois itens semelhantes poderão coexistir.
                                </span>
                            </div>
                            <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap', justifyContent: 'flex-end' }}>
                                <button onClick={onClose} style={{ padding: '10px 16px', border: '1px solid #E2E8F0', borderRadius: '6px', background: '#fff', cursor: 'pointer', fontWeight: 600 }}>Cancelar</button>
                                <button onClick={() => { const it = duplicate.existingItem; if (it && it.id) onResolved(it); else onClose(); }} style={{ padding: '10px 16px', border: '1px solid var(--color-primary)', borderRadius: '6px', background: '#fff', color: 'var(--color-primary)', cursor: 'pointer', fontWeight: 700 }}>Usar item existente</button>
                                <button onClick={() => { setDuplicate(null); submit(true); }} style={{ padding: '10px 16px', border: 'none', borderRadius: '6px', background: 'var(--color-primary)', color: '#fff', cursor: 'pointer', fontWeight: 700 }}>Criar mesmo assim</button>
                            </div>
                        </div>
                    ) : (
                        <>
                            <div>
                                <label style={labelStyle}>Descrição *</label>
                                <input type="text" value={description} onChange={(e) => setDescription(e.target.value)} style={inputStyle} disabled={submitting || mustRefresh} />
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                                <div>
                                    <label style={labelStyle}>Quantidade *</label>
                                    <input type="number" min="0.01" step="0.01" value={quantity} onChange={(e) => setQuantity(e.target.value)} style={inputStyle} disabled={submitting || mustRefresh} />
                                </div>
                                <div>
                                    <label style={labelStyle}>Unidade *</label>
                                    <select value={unitId} onChange={(e) => setUnitId(e.target.value)} style={inputStyle} disabled={submitting || mustRefresh}>
                                        <option value="">-- Selecione --</option>
                                        {units.map((u: any) => (
                                            <option key={u.id} value={u.id}>{u.code || u.name}</option>
                                        ))}
                                    </select>
                                </div>
                            </div>

                            <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end', marginTop: '4px' }}>
                                <button onClick={onClose} style={{ padding: '10px 16px', border: '1px solid #E2E8F0', borderRadius: '6px', background: '#fff', cursor: 'pointer', fontWeight: 600 }}>
                                    {mustRefresh ? 'Fechar' : 'Cancelar'}
                                </button>
                                {!mustRefresh && (
                                    <button onClick={() => submit(false)} disabled={submitting} style={{ padding: '10px 16px', border: 'none', borderRadius: '6px', background: 'var(--color-primary)', color: '#fff', cursor: submitting ? 'not-allowed' : 'pointer', fontWeight: 700, opacity: submitting ? 0.7 : 1, display: 'flex', alignItems: 'center', gap: '8px' }}>
                                        {submitting ? <Loader2 size={16} className="animate-spin" /> : <Save size={16} />}
                                        Adicionar ao pedido
                                    </button>
                                )}
                            </div>
                        </>
                    )}
                </div>
            </div>
        </div>,
        document.body
    );
};
