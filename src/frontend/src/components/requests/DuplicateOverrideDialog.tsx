import { useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { cancelBtnStyle } from '../it/EquipmentFormModal';

/** Mirrors PaymentSourceDocumentDuplicateHierarchy.MinimumOverrideReasonLength on the backend. */
export const DUPLICATE_OVERRIDE_REASON_MIN_LENGTH = 20;

interface Props {
    /** The backend's explanation of which document shares the reference and why it cannot decide. */
    detail: string;
    /** Backend classification (e.g. STRONG_BUSINESS_DUPLICATE) — drives the severity wording. */
    classification?: string | null;
    onConfirm: (reason: string) => void;
    onCancel: () => void;
}

/**
 * Duplicate hierarchy LEVEL 4 — the same supplier reference exists on another document and the
 * content evidence cannot prove duplicate or distinct.
 *
 * <p>Deliberately NOT a plain confirm: proceeding needs a written reason (≥ 20 chars), which the
 * backend re-checks and records in the request timeline with user and timestamp. The user is never
 * asked to falsify the supplier's real reference to get past validation — this dialog is the
 * legitimate path through.</p>
 */
export function DuplicateOverrideDialog({ detail, classification = null, onConfirm, onCancel }: Props) {
    const [reason, setReason] = useState('');
    const written = reason.trim().length;
    const missing = DUPLICATE_OVERRIDE_REASON_MIN_LENGTH - written;
    const canConfirm = missing <= 0;
    const strong = classification === 'STRONG_BUSINESS_DUPLICATE';

    return (
        <ModalWrapper
            title={strong ? 'Provável documento duplicado' : 'Possível documento duplicado'}
            onClose={onCancel}
            width={480}
        >
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                    <div style={{ color: strong ? '#dc2626' : '#ea580c', flexShrink: 0, marginTop: '2px' }}>
                        <AlertTriangle size={20} />
                    </div>
                    <p style={{
                        margin: 0, fontSize: '0.85rem', color: 'var(--color-text-main)',
                        lineHeight: 1.55, overflowWrap: 'anywhere'
                    }}>
                        {detail}
                    </p>
                </div>

                <p style={{ margin: 0, fontSize: '0.78rem', color: 'var(--color-text-muted)', lineHeight: 1.5 }}>
                    {strong
                        ? 'Fornecedor, referência, data, moeda e total coincidem com um documento já ' +
                          'registado — um PDF diferente não torna o documento novo. Prossiga apenas em ' +
                          'casos excecionais legítimos (reemissão, cópia digitalizada, exportação ' +
                          'corrigida) e explique porquê — a confirmação fica registada no histórico.'
                        : 'Fornecedores podem reutilizar a mesma referência em propostas diferentes. Se tem a ' +
                          'certeza de que este é um documento distinto, explique porquê — a confirmação fica ' +
                          'registada no histórico do pedido.'}
                </p>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    <label style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>
                        Justificativa (obrigatória)
                    </label>
                    <textarea
                        value={reason}
                        onChange={e => setReason(e.target.value)}
                        rows={3}
                        placeholder="Ex.: Proposta para um projeto diferente do Documento 1 (CCTV Viana02 vs CCTV Viana01), com escopo e valores próprios."
                        style={{
                            width: '100%', boxSizing: 'border-box', resize: 'vertical',
                            padding: '8px 10px', borderRadius: '8px', fontSize: '0.82rem',
                            border: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-main)'
                        }}
                    />
                    {!canConfirm && (
                        <span style={{ fontSize: '0.72rem', color: '#b45309' }}>
                            Faltam {missing} caracteres (mínimo {DUPLICATE_OVERRIDE_REASON_MIN_LENGTH}).
                        </span>
                    )}
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px' }}>
                    <button type="button" onClick={onCancel} style={cancelBtnStyle}>
                        Cancelar
                    </button>
                    <button
                        type="button"
                        disabled={!canConfirm}
                        onClick={() => canConfirm && onConfirm(reason.trim())}
                        style={{
                            padding: '8px 16px', border: 'none', borderRadius: '8px',
                            fontWeight: 600, fontSize: '0.85rem',
                            cursor: canConfirm ? 'pointer' : 'not-allowed',
                            backgroundColor: '#ea580c', color: 'white',
                            opacity: canConfirm ? 1 : 0.5
                        }}
                    >
                        Confirmar documento distinto
                    </button>
                </div>
            </div>
        </ModalWrapper>
    );
}
