import React, { useMemo, useState } from 'react';
import { AlertTriangle, Info } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { cancelBtnStyle } from '../it/EquipmentFormModal';
import { api } from '../../lib/api';
import type { PoGroupCancellationDto, PoGroupCancellationResultDto } from '../../types';
import type { BuyerWorkspacePoGroup } from '../../types/buyerWorkspace';
import {
    EMPTY_CANCEL_FORM, MIN_JUSTIFICATION_LENGTH, MAX_JUSTIFICATION_LENGTH, MAX_ECF_REFERENCE_LENGTH,
    CANCELLATION_KIND_LABELS, CANCELLATION_KIND_EXPLANATIONS, CANCELLATION_REASON_CODES, CANCELLATION_REASON_LABELS,
    ECF_STATUS_OPTION_LABELS, validateCancelPoGroupForm, validateEcfDeclaration, describeCancellationConsequences,
    type CancelPoGroupForm, type CancelFormError,
} from '../../lib/poGroupCancellation';

/**
 * Phase 1 P.O. cancellation (docs/PO_GROUP_CANCELLATION.md).
 *  • mode 'cancel' — "Cancelar P.O." on one active group: kind (RESTART_QUOTATION | CANCEL_ITEMS), reason code,
 *    explanation (≥ 20), the Buyer's ECF DECLARATION when the group ever had a P.O., and the consequences list.
 *    POST requests/{id}/po-groups/{gid}/cancel with expectedGroupRowVersion = group.rowVersion.
 *  • mode 'declaration' — "Atualizar declaração ECF" on a cancelled group while Finance confirmation is pending or
 *    rejected. PUT …/cancellation/ecf-declaration with expectedRowVersion = cancellation.rowVersion.
 * The ECF part is ALWAYS worded as the Buyer's declaration: the Portal never cancels nor verifies an ECF in Primavera;
 * Finance confirms separately. Server errors (ProblemDetails detail) are shown verbatim; the server is the authority.
 */
type Mode = 'cancel' | 'declaration';

interface Props {
    mode: Mode;
    requestId: string;
    requestNumber: string;
    group: BuyerWorkspacePoGroup;
    onCancel: () => void;
    onSuccess: (result: { mode: 'cancel'; result: PoGroupCancellationResultDto } | { mode: 'declaration'; cancellation: PoGroupCancellationDto }) => void;
}

export function CancelPoGroupDialog({ mode, requestId, requestNumber, group, onCancel, onSuccess }: Props) {
    const cancellation = group.cancellation ?? null;
    const ecfRequired = mode === 'cancel' ? group.financeConfirmationWouldBeRequired : true;
    const [form, setForm] = useState<CancelPoGroupForm>(() => mode === 'declaration' && cancellation
        ? { ...EMPTY_CANCEL_FORM, ecfStatus: cancellation.ecfStatus === 'NOT_APPLICABLE' ? '' : cancellation.ecfStatus, ecfReference: cancellation.ecfReference ?? '', ecfDeclarationNote: cancellation.ecfDeclarationNote ?? '' }
        : EMPTY_CANCEL_FORM);
    const [busy, setBusy] = useState(false);
    const [serverError, setServerError] = useState<string | null>(null);
    const [touched, setTouched] = useState(false);
    const set = (patch: Partial<CancelPoGroupForm>) => setForm(f => ({ ...f, ...patch }));

    const errors: CancelFormError[] = useMemo(() => mode === 'cancel'
        ? validateCancelPoGroupForm(form, { financeConfirmationWouldBeRequired: ecfRequired })
        : validateEcfDeclaration({ ecfStatus: form.ecfStatus, ecfReference: form.ecfReference }),
        [form, mode, ecfRequired]);
    const errorFor = (field: keyof CancelPoGroupForm) => touched ? errors.find(e => e.field === field)?.message : undefined;
    const consequences = useMemo(() => mode === 'cancel' ? describeCancellationConsequences(form, {
        purchaseOrderNumber: group.purchaseOrderNumber, paymentConditionCode: group.paymentConditionCode,
        financeConfirmationWouldBeRequired: ecfRequired, lineNumbers: group.lineNumbers,
    }) : [], [form, group, mode, ecfRequired]);
    const canConfirm = errors.length === 0 && !busy;

    const submit = async () => {
        setTouched(true);
        if (errors.length > 0 || busy) return;
        setBusy(true);
        setServerError(null);
        try {
            if (mode === 'cancel') {
                const result = await api.requests.cancelPoGroup(requestId, group.id, {
                    kind: form.kind,
                    reasonCode: form.reasonCode,
                    reason: form.reason.trim(),
                    ecfStatus: ecfRequired ? form.ecfStatus : undefined,
                    ecfReference: ecfRequired && form.ecfStatus === 'DECLARED_CANCELLED' ? form.ecfReference.trim() : undefined,
                    ecfDeclarationNote: ecfRequired && form.ecfDeclarationNote.trim() ? form.ecfDeclarationNote.trim() : undefined,
                    expectedGroupRowVersion: group.rowVersion ?? undefined,
                });
                onSuccess({ mode: 'cancel', result });
            } else {
                const updated = await api.requests.updatePoGroupEcfDeclaration(requestId, group.id, {
                    ecfStatus: form.ecfStatus,
                    ecfReference: form.ecfStatus === 'DECLARED_CANCELLED' ? form.ecfReference.trim() : undefined,
                    note: form.ecfDeclarationNote.trim() || undefined,
                    expectedRowVersion: cancellation?.rowVersion ?? undefined,
                });
                onSuccess({ mode: 'declaration', cancellation: updated });
            }
        } catch (err: any) {
            // ProblemDetails: ApiError.message = detail || title; the `code` extension is in errorCode.
            const code = err?.errorCode || err?.details?.code;
            const detail = err?.details?.detail || err?.message || 'Não foi possível concluir a operação.';
            setServerError(code ? `${detail} (${code})` : detail);
            setBusy(false);
        }
    };

    const title = mode === 'cancel' ? `Cancelar P.O. — ${requestNumber}` : `Atualizar declaração sobre a ECF — ${requestNumber}`;
    const groupLine = [group.supplierName || 'Fornecedor não definido', group.purchaseOrderNumber ? `P.O. ${group.purchaseOrderNumber}` : 'sem P.O. registada', group.approvalBatchNumber != null ? `Lote ${group.approvalBatchNumber}` : null].filter(Boolean).join(' · ');

    return (
        <ModalWrapper onClose={busy ? () => undefined : onCancel} title={title} width={640}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 360 }} data-tour="buyer-cancel-po-dialog">
                <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>{groupLine}</div>

                {mode === 'cancel' && (
                    <>
                        <fieldset style={fieldset}>
                            <legend style={legend}>O que acontece aos itens? (obrigatório)</legend>
                            {(['RESTART_QUOTATION', 'CANCEL_ITEMS'] as const).map(k => (
                                <label key={k} style={radioRow(form.kind === k)}>
                                    <input type="radio" name="cancel-kind" value={k} checked={form.kind === k} disabled={busy} onChange={() => set({ kind: k })} style={{ marginTop: 3 }} />
                                    <span>
                                        <strong>{CANCELLATION_KIND_LABELS[k]}</strong>
                                        <span style={{ display: 'block', fontSize: '0.78rem', color: 'var(--color-text-muted)', marginTop: 2 }}>{CANCELLATION_KIND_EXPLANATIONS[k]}</span>
                                    </span>
                                </label>
                            ))}
                            {errorFor('kind') && <FieldError>{errorFor('kind')}</FieldError>}
                        </fieldset>

                        <label style={labelCol}>
                            Motivo (obrigatório)
                            <select value={form.reasonCode} disabled={busy} onChange={e => set({ reasonCode: e.target.value })} style={input}>
                                <option value="">— Escolha o motivo —</option>
                                {CANCELLATION_REASON_CODES.map(c => <option key={c} value={c}>{CANCELLATION_REASON_LABELS[c]}</option>)}
                            </select>
                            {errorFor('reasonCode') && <FieldError>{errorFor('reasonCode')}</FieldError>}
                        </label>

                        <label style={labelCol}>
                            Explicação (obrigatória, {MIN_JUSTIFICATION_LENGTH}–{MAX_JUSTIFICATION_LENGTH} caracteres)
                            <textarea value={form.reason} disabled={busy} rows={4} maxLength={MAX_JUSTIFICATION_LENGTH} onChange={e => set({ reason: e.target.value })}
                                placeholder="Descreva o que aconteceu e porque a P.O. deixa de ter efeito (fica registado no histórico e nas notificações)."
                                style={{ ...input, resize: 'vertical' }} />
                            <span style={{ fontWeight: 400, color: form.reason.trim().length < MIN_JUSTIFICATION_LENGTH ? '#B45309' : 'var(--color-text-muted)' }}>
                                {form.reason.trim().length}/{MIN_JUSTIFICATION_LENGTH} caracteres mínimos
                            </span>
                            {errorFor('reason') && <FieldError>{errorFor('reason')}</FieldError>}
                        </label>
                    </>
                )}

                {ecfRequired && (
                    <fieldset style={fieldset} data-tour="buyer-cancel-po-ecf">
                        <legend style={legend}>Declaração do comprador sobre a ECF (obrigatória)</legend>
                        <div style={{ display: 'flex', gap: 8, alignItems: 'flex-start', padding: 10, borderRadius: 8, background: '#EFF6FF', border: '1px solid #BFDBFE', color: '#1E3A8A', fontSize: '0.78rem' }}>
                            <Info size={15} style={{ flexShrink: 0, marginTop: 1 }} />
                            <span>
                                Esta é a <strong>sua declaração</strong> sobre o documento externo (ECF/P.O. no Primavera). O Portal <strong>não verifica</strong> nem anula a ECF;
                                Finanças confirma separadamente a anulação. Até essa confirmação, o registo de uma P.O. de substituição para estes itens fica bloqueado.
                            </span>
                        </div>
                        {(['PENDING_EXTERNAL', 'DECLARED_CANCELLED', 'UNKNOWN'] as const).map(s => (
                            <label key={s} style={radioRow(form.ecfStatus === s)}>
                                <input type="radio" name="ecf-status" value={s} checked={form.ecfStatus === s} disabled={busy} onChange={() => set({ ecfStatus: s })} style={{ marginTop: 3 }} />
                                <span><strong>{ECF_STATUS_OPTION_LABELS[s]}</strong></span>
                            </label>
                        ))}
                        {errorFor('ecfStatus') && <FieldError>{errorFor('ecfStatus')}</FieldError>}
                        {form.ecfStatus === 'DECLARED_CANCELLED' && (
                            <label style={labelCol}>
                                Referência da ECF anulada (obrigatória)
                                <input type="text" value={form.ecfReference} disabled={busy} maxLength={MAX_ECF_REFERENCE_LENGTH} onChange={e => set({ ecfReference: e.target.value })} placeholder="Ex.: ECF10 2026/364" style={input} />
                                {errorFor('ecfReference') && <FieldError>{errorFor('ecfReference')}</FieldError>}
                            </label>
                        )}
                        <label style={labelCol}>
                            Nota da declaração (opcional)
                            <textarea value={form.ecfDeclarationNote} disabled={busy} rows={2} maxLength={MAX_JUSTIFICATION_LENGTH} onChange={e => set({ ecfDeclarationNote: e.target.value })}
                                placeholder="Contexto adicional para Finanças (ex.: quem anulou, quando)." style={{ ...input, resize: 'vertical' }} />
                        </label>
                    </fieldset>
                )}

                {mode === 'cancel' && (
                    <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start', padding: 12, borderRadius: 8, background: '#FFFBEB', border: '1px solid #FDE68A', color: '#92400E', fontSize: '0.82rem' }} data-tour="buyer-cancel-po-consequences">
                        <AlertTriangle size={18} style={{ flexShrink: 0, marginTop: 2 }} />
                        <div>
                            <div style={{ fontWeight: 700, marginBottom: 4 }}>Consequências desta ação</div>
                            <ul style={{ margin: 0, paddingLeft: 18, display: 'flex', flexDirection: 'column', gap: 4 }}>
                                {consequences.map((line, i) => <li key={i}>{line}</li>)}
                            </ul>
                        </div>
                    </div>
                )}

                {serverError && (
                    <div role="alert" style={{ padding: '10px 12px', borderRadius: 8, background: '#FEF2F2', border: '1px solid #FECACA', color: '#991B1B', fontSize: '0.82rem', fontWeight: 600 }}>
                        {serverError}
                    </div>
                )}

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                    <button type="button" onClick={onCancel} disabled={busy} style={cancelBtnStyle}>Cancelar</button>
                    <button
                        type="button"
                        onClick={() => void submit()}
                        disabled={!canConfirm && touched}
                        style={{ padding: '8px 14px', borderRadius: 8, border: 'none', background: (canConfirm || !touched) ? (mode === 'cancel' ? '#B91C1C' : '#B45309') : '#D6D3D1', color: '#fff', fontWeight: 700, cursor: (canConfirm || !touched) ? 'pointer' : 'not-allowed' }}
                    >
                        {busy ? 'A processar…' : mode === 'cancel' ? 'Confirmar cancelamento da P.O.' : 'Guardar declaração'}
                    </button>
                </div>
            </div>
        </ModalWrapper>
    );
}

function FieldError({ children }: { children: React.ReactNode }) {
    return <span style={{ fontWeight: 600, fontSize: '0.76rem', color: '#B91C1C' }}>{children}</span>;
}

const fieldset: React.CSSProperties = { border: '1px solid var(--color-border, #CBD5E1)', borderRadius: 10, padding: '10px 12px 12px', display: 'flex', flexDirection: 'column', gap: 8, margin: 0 };
const legend: React.CSSProperties = { fontSize: '0.8rem', fontWeight: 700, padding: '0 6px' };
const labelCol: React.CSSProperties = { display: 'flex', flexDirection: 'column', gap: 4, fontSize: '0.82rem', fontWeight: 600 };
const input: React.CSSProperties = { fontFamily: 'inherit', fontSize: '0.85rem', padding: 8, borderRadius: 8, border: '1px solid var(--color-border, #CBD5E1)', background: 'var(--color-bg-surface, #fff)', color: 'var(--color-text-main, #0F172A)' };
const radioRow = (active: boolean): React.CSSProperties => ({ display: 'flex', gap: 10, alignItems: 'flex-start', padding: '8px 10px', borderRadius: 8, border: `1px solid ${active ? 'var(--color-primary, #1D4ED8)' : 'var(--color-border, #CBD5E1)'}`, background: active ? 'color-mix(in srgb, var(--color-primary, #1D4ED8) 6%, transparent)' : 'transparent', fontSize: '0.84rem', cursor: 'pointer' });
