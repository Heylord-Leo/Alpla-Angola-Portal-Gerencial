import React, { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, ChevronDown, ChevronRight, ShieldCheck, ShieldX, FileText, RefreshCw } from 'lucide-react';
import { api } from '../../lib/api';
import type { FinancePoCancellationListDto, FinancePoCancellationListItemDto } from '../../types';
import { ModalWrapper } from '../common/ModalWrapper';
import { cancelBtnStyle } from '../it/EquipmentFormModal';
import {
    cancellationKindLabel, ecfStatusLabel, financeStatusLabel, validateFinanceRejectionNote,
    MIN_JUSTIFICATION_LENGTH, NO_FINANCE_RECIPIENTS_WARNING, describeFinanceEmailPolicy,
} from '../../lib/poGroupCancellation';

/**
 * Phase 1 P.O. cancellation (D12) — Finance's ECF confirmation queue, rendered above the obligations list.
 * GET finance/po-cancellations?status=OPEN (pending + rejected) on mount and after every decision.
 * Each row separates the Buyer's DECLARATION ("Declaração do comprador") from Finance's own decision:
 * "Confirmar anulação" (optional note) / "Rejeitar declaração" (mandatory note ≥ 20). Both are human attestations
 * — the Portal never verifies the ECF in Primavera. `canReview` / `reviewBlockReason` are server decisions.
 */
interface Props {
    plantId?: number;
    onDecision?: (message: string) => void;
}

const formatMoney = (amount: number, currency?: string | null) =>
    `${(currency || '—')} ${amount.toLocaleString('pt-AO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
const fmtDateTime = (iso?: string | null) => { if (!iso) return '—'; try { return new Date(iso).toLocaleString('pt-PT', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }); } catch { return '—'; } };

export function PoCancellationReviewPanel({ plantId, onDecision }: Props) {
    const [data, setData] = useState<FinancePoCancellationListDto | null>(null);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [open, setOpen] = useState(true);
    const [review, setReview] = useState<{ item: FinancePoCancellationListItemDto; decision: 'CONFIRM' | 'REJECT' } | null>(null);

    const load = useCallback(async () => {
        setLoadError(null);
        try { setData(await api.finance.listPoCancellations({ status: 'OPEN', plantId })); }
        catch (e: any) {
            // 403 (not Finance / SysAdmin) → hide the section silently; anything else is reported.
            if (e?.status === 403) { setData(null); return; }
            setLoadError(e?.message || 'Falha ao carregar os cancelamentos a confirmar.');
        }
    }, [plantId]);
    useEffect(() => { void load(); }, [load]);

    const count = data ? data.pendingCount + data.rejectedCount : 0;
    if (!data && !loadError) return null;
    if (data && data.items.length === 0 && !loadError) return null;

    return (
        <section data-tour="finance-po-cancellations" style={{ marginBottom: '16px', border: '1px solid #FCD34D', borderRadius: 'var(--radius-lg)', backgroundColor: 'var(--color-bg-surface)', boxShadow: 'var(--shadow-sm)', overflow: 'hidden' }}>
            <button type="button" onClick={() => setOpen(v => !v)} aria-expanded={open}
                style={{ width: '100%', display: 'flex', alignItems: 'center', gap: '10px', padding: '10px 16px', background: 'rgba(245, 158, 11, 0.08)', border: 'none', cursor: 'pointer', textAlign: 'left' }}>
                {open ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                <span style={{ fontWeight: 800, fontSize: '0.9rem', color: 'var(--color-text-main)' }}>Cancelamentos a confirmar</span>
                <span style={{ fontSize: '0.72rem', fontWeight: 800, color: '#92400E', backgroundColor: '#FEF3C7', border: '1px solid #FCD34D', padding: '2px 9px', borderRadius: 'var(--radius-full)' }}>{count}</span>
                <span style={{ fontSize: '0.74rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>P.O. canceladas pelo comprador cuja anulação da ECF aguarda a confirmação de Finanças</span>
                <span style={{ marginLeft: 'auto' }} onClick={(e) => { e.stopPropagation(); void load(); }} title="Atualizar"><RefreshCw size={14} style={{ opacity: 0.6 }} /></span>
            </button>
            {open && (
                <div style={{ display: 'flex', flexDirection: 'column' }}>
                    {loadError && <div style={{ padding: '10px 16px', color: '#b91c1c', fontSize: '0.82rem', fontWeight: 600 }}>{loadError}</div>}
                    {data?.items.map(item => (
                        <ReviewRow key={item.cancellationId} item={item} onReview={(decision) => setReview({ item, decision })} />
                    ))}
                </div>
            )}
            {review && (
                <ReviewDialog
                    item={review.item}
                    decision={review.decision}
                    onCancel={() => setReview(null)}
                    onDone={(msg) => { setReview(null); onDecision?.(msg); void load(); }}
                />
            )}
        </section>
    );
}

function ReviewRow({ item, onReview }: { item: FinancePoCancellationListItemDto; onReview: (d: 'CONFIRM' | 'REJECT') => void }) {
    const rejected = item.financeConfirmationStatus === 'REJECTED';
    return (
        <div data-tour="finance-po-cancellation-row" style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1.6fr) minmax(0, 1.4fr) minmax(0, 1.8fr) auto', gap: '12px', alignItems: 'start', padding: '12px 16px', borderTop: '1px solid var(--color-border)', fontSize: '0.8rem' }}>
            {/* 1 · Request / supplier / plant / amount / previous P.O. */}
            <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: '2px' }}>
                <div style={{ fontWeight: 700, color: 'var(--color-primary)' }}>{item.requestNumber}</div>
                <div style={{ fontWeight: 600, color: 'var(--color-text-main)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={item.requestTitle ?? undefined}>{item.requestTitle || '—'}</div>
                <div style={{ color: 'var(--color-text-muted)' }}>{item.supplierName || 'Fornecedor não definido'}</div>
                <div style={{ color: 'var(--color-text-muted)' }}>{[item.companyName, item.plantName].filter(Boolean).join(' · ') || '—'}</div>
                <div style={{ fontWeight: 700 }}>{formatMoney(item.totalAmountSnapshot, item.currencyCodeSnapshot)}</div>
                <div><FileText size={12} style={{ opacity: 0.5, marginRight: 4, verticalAlign: '-1px' }} />P.O. anterior: <strong>{item.purchaseOrderNumberSnapshot || '—'}</strong></div>
            </div>
            {/* 2 · Cancellation (kind / reason / who / when) */}
            <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: '2px' }}>
                <div style={{ fontSize: '0.66rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Cancelamento</div>
                <div style={{ fontWeight: 700 }}>{cancellationKindLabel(item.kind)}</div>
                <div>{item.reasonLabel || item.reasonCode}</div>
                <div style={{ color: 'var(--color-text-muted)', whiteSpace: 'pre-wrap' }}>{item.reason}</div>
                <div style={{ color: 'var(--color-text-muted)' }}>Por {item.cancelledByName || '—'} · {fmtDateTime(item.cancelledAtUtc)}</div>
            </div>
            {/* 3 · Buyer declaration — clearly labelled; never a Portal verification */}
            <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: '2px', padding: '8px 10px', borderRadius: 'var(--radius-md)', background: 'rgba(0,0,0,0.03)', border: '1px dashed var(--color-border)' }}>
                <div style={{ fontSize: '0.66rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Declaração do comprador</div>
                <div style={{ fontWeight: 700 }}>{ecfStatusLabel(item.ecfStatus)}</div>
                {item.ecfReference && <div>Referência: <strong>{item.ecfReference}</strong></div>}
                <div style={{ color: 'var(--color-text-muted)' }}>Declarado por {item.ecfDeclaredByName || '—'} · {fmtDateTime(item.ecfDeclaredAtUtc)}</div>
                <div style={{ marginTop: '4px', fontWeight: 700, color: rejected ? 'var(--color-status-red)' : 'var(--color-status-orange)' }}>{financeStatusLabel(item.financeConfirmationStatus)}</div>
                {rejected && item.financeConfirmationNote && <div style={{ color: 'var(--color-text-muted)', whiteSpace: 'pre-wrap' }}>Nota de Finanças: {item.financeConfirmationNote}</div>}
                {item.financeRecipientsResolvedCount === 0 && (
                    <div style={{ marginTop: '4px', color: '#B45309', fontWeight: 700 }}><AlertTriangle size={12} style={{ verticalAlign: '-2px', marginRight: 4 }} />{NO_FINANCE_RECIPIENTS_WARNING}</div>
                )}
                {/* D13: Finance e-mail policy outcome (what was queued) — plant Finance users always get the e-mail; AP mailbox is additional. */}
                {item.financeEmailPolicyOutcome && (() => {
                    const policy = describeFinanceEmailPolicy(item);
                    return (
                        <div data-tour="finance-po-cancellation-email-policy" style={{ marginTop: '4px', fontSize: '0.72rem', fontWeight: 600, color: policy.severity === 'ok' ? '#166534' : policy.severity === 'warning' ? '#92400E' : '#1E3A8A' }}>
                            E-mail para Finanças: {policy.text}
                        </div>
                    );
                })()}
            </div>
            {/* 4 · Finance decision */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', alignItems: 'stretch', minWidth: 170 }}>
                {item.canReview ? (
                    <>
                        <button type="button" onClick={() => onReview('CONFIRM')} data-tour="finance-po-cancellation-confirm"
                            style={{ padding: '7px 12px', borderRadius: 'var(--radius-md)', border: 'none', backgroundColor: 'var(--color-status-green, #16a34a)', color: '#fff', fontWeight: 700, fontSize: '0.76rem', display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer', whiteSpace: 'nowrap', justifyContent: 'center' }}>
                            <ShieldCheck size={14} /> Confirmar anulação
                        </button>
                        <button type="button" onClick={() => onReview('REJECT')} data-tour="finance-po-cancellation-reject"
                            style={{ padding: '7px 12px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-status-red, #dc2626)', backgroundColor: 'transparent', color: 'var(--color-status-red, #dc2626)', fontWeight: 700, fontSize: '0.76rem', display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer', whiteSpace: 'nowrap', justifyContent: 'center' }}>
                            <ShieldX size={14} /> Rejeitar declaração
                        </button>
                    </>
                ) : (
                    <div style={{ fontSize: '0.74rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>{item.reviewBlockReason || 'Sem permissão para decidir este cancelamento.'}</div>
                )}
            </div>
        </div>
    );
}

function ReviewDialog({ item, decision, onCancel, onDone }: { item: FinancePoCancellationListItemDto; decision: 'CONFIRM' | 'REJECT'; onCancel: () => void; onDone: (message: string) => void }) {
    const [note, setNote] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const confirm = decision === 'CONFIRM';
    const validation = confirm ? null : validateFinanceRejectionNote(note);
    const canSubmit = !busy && validation === null;

    const submit = async () => {
        if (!canSubmit) return;
        setBusy(true);
        setError(null);
        try {
            if (confirm) await api.finance.confirmPoCancellation(item.cancellationId, { note: note.trim() || undefined, expectedRowVersion: item.rowVersion ?? undefined });
            else await api.finance.rejectPoCancellation(item.cancellationId, { note: note.trim(), expectedRowVersion: item.rowVersion ?? undefined });
            onDone(confirm
                ? `Anulação da ECF ${item.purchaseOrderNumberSnapshot || ''} confirmada por Finanças (${item.requestNumber}).`
                : `Declaração do comprador sobre a ECF ${item.purchaseOrderNumberSnapshot || ''} rejeitada (${item.requestNumber}). O comprador foi notificado.`);
        } catch (e: any) {
            const code = e?.errorCode || e?.details?.code;
            const detail = e?.details?.detail || e?.message || 'Não foi possível registar a decisão.';
            setError(code ? `${detail} (${code})` : detail);
            setBusy(false);
        }
    };

    return (
        <ModalWrapper onClose={busy ? () => undefined : onCancel} title={confirm ? 'Confirmar anulação da ECF' : 'Rejeitar declaração do comprador'} width={560}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }} data-tour="finance-po-cancellation-dialog">
                <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>
                    {item.requestNumber} · {item.supplierName || 'Fornecedor não definido'} · P.O. anterior {item.purchaseOrderNumberSnapshot || '—'} · {formatMoney(item.totalAmountSnapshot, item.currencyCodeSnapshot)}
                </div>
                <div style={{ padding: '10px 12px', borderRadius: 8, background: 'rgba(0,0,0,0.03)', border: '1px dashed var(--color-border)', fontSize: '0.8rem' }}>
                    <div style={{ fontSize: '0.66rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Declaração do comprador</div>
                    <div style={{ fontWeight: 700 }}>{ecfStatusLabel(item.ecfStatus)}</div>
                    {item.ecfReference && <div>Referência: <strong>{item.ecfReference}</strong></div>}
                </div>
                <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start', padding: 12, borderRadius: 8, background: confirm ? '#ECFDF5' : '#FEF2F2', border: `1px solid ${confirm ? '#A7F3D0' : '#FECACA'}`, color: confirm ? '#065F46' : '#991B1B', fontSize: '0.82rem' }}>
                    <AlertTriangle size={18} style={{ flexShrink: 0, marginTop: 2 }} />
                    <div>
                        {confirm
                            ? 'Ao confirmar, Finanças atesta que a ECF anterior foi anulada no sistema externo. O Portal não verifica o Primavera: esta confirmação é a sua declaração e desbloqueia o registo de uma nova P.O. para estes itens.'
                            : 'A rejeição mantém a P.O. cancelada e o registo de uma nova P.O. bloqueado; o comprador é notificado e pode atualizar a declaração. Indique o motivo (obrigatório).'}
                    </div>
                </div>
                <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: '0.82rem', fontWeight: 600 }}>
                    {confirm ? 'Nota (opcional)' : `Motivo da rejeição (obrigatório, mínimo ${MIN_JUSTIFICATION_LENGTH} caracteres)`}
                    <textarea value={note} onChange={(e) => setNote(e.target.value)} rows={4} disabled={busy} maxLength={1000}
                        placeholder={confirm ? 'Ex.: anulação verificada no Primavera em DD/MM.' : 'Ex.: a referência indicada não corresponde a uma ECF anulada.'}
                        style={{ fontFamily: 'inherit', fontSize: '0.85rem', padding: 8, borderRadius: 8, border: '1px solid var(--color-border, #CBD5E1)', resize: 'vertical' }} />
                    {!confirm && <span style={{ fontWeight: 400, color: validation ? '#B45309' : 'var(--color-text-muted)' }}>{validation ?? `${note.trim().length} caracteres`}</span>}
                </label>
                {error && <div role="alert" style={{ padding: '10px 12px', borderRadius: 8, background: '#FEF2F2', border: '1px solid #FECACA', color: '#991B1B', fontSize: '0.82rem', fontWeight: 600 }}>{error}</div>}
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                    <button type="button" onClick={onCancel} disabled={busy} style={cancelBtnStyle}>Cancelar</button>
                    <button type="button" onClick={() => void submit()} disabled={!canSubmit}
                        style={{ padding: '8px 14px', borderRadius: 8, border: 'none', background: canSubmit ? (confirm ? '#047857' : '#B91C1C') : '#D6D3D1', color: '#fff', fontWeight: 700, cursor: canSubmit ? 'pointer' : 'not-allowed' }}>
                        {busy ? 'A registar…' : confirm ? 'Confirmar anulação' : 'Rejeitar declaração'}
                    </button>
                </div>
            </div>
        </ModalWrapper>
    );
}

export default PoCancellationReviewPanel;
