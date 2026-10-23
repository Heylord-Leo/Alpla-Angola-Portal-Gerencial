import { useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { cancelBtnStyle } from '../it/EquipmentFormModal';
import { TAKEOVER_JUSTIFICATION_MAX_LENGTH, buildTakeoverConfirmation, validateTakeoverJustification } from '../../pages/Buyer/workspaceAssignActions';

interface Props {
    request: { requestNumber: string; title?: string | null; buyerName?: string | null };
    busy?: boolean;
    onConfirm: (justification: string) => void;
    onCancel: () => void;
}

/**
 * §8 — confirmation + mandatory justification before a Buyer takes over another Buyer's request. Shared by the
 * classic list and the Workspace so both state the same effects (responsibility transfers; the previous Buyer, the
 * requester and the request participants are notified by e-mail) and apply the same client-side validation; the
 * server re-validates and is the authority. Confirm is disabled while invalid or busy (no double submission).
 */
export function TakeOverRequestDialog({ request, busy = false, onConfirm, onCancel }: Props) {
    const [justification, setJustification] = useState('');
    const c = buildTakeoverConfirmation(request);
    const validation = validateTakeoverJustification(justification);
    const canConfirm = validation.ok && !busy;
    return (
        <ModalWrapper onClose={onCancel} title={c.title}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12, minWidth: 360, maxWidth: 560 }} data-tour="buyer-takeover-dialog">
                <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start', padding: 12, borderRadius: 8, background: '#FFFBEB', border: '1px solid #FDE68A', color: '#92400E', fontSize: '0.85rem' }}>
                    <AlertTriangle size={18} style={{ flexShrink: 0, marginTop: 2 }} />
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                        {c.lines.map((line, i) => <div key={i}>{line}</div>)}
                    </div>
                </div>
                <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: '0.82rem', fontWeight: 600 }}>
                    Justificação (obrigatória)
                    <textarea
                        value={justification}
                        onChange={(e) => setJustification(e.target.value)}
                        maxLength={TAKEOVER_JUSTIFICATION_MAX_LENGTH}
                        rows={4}
                        disabled={busy}
                        placeholder="Explique porque assume este pedido de outro comprador (fica registado no histórico e nas notificações)."
                        style={{ fontFamily: 'inherit', fontSize: '0.85rem', padding: 8, borderRadius: 8, border: '1px solid var(--color-border, #CBD5E1)', resize: 'vertical' }}
                    />
                    <span style={{ fontWeight: 400, color: validation.ok ? 'var(--color-text-muted, #64748B)' : '#B45309' }}>
                        {validation.ok ? `${justification.trim().length}/${TAKEOVER_JUSTIFICATION_MAX_LENGTH}` : validation.message}
                    </span>
                </label>
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                    <button type="button" onClick={onCancel} disabled={busy} style={cancelBtnStyle}>Cancelar</button>
                    <button
                        type="button"
                        onClick={() => canConfirm && onConfirm(justification.trim())}
                        disabled={!canConfirm}
                        style={{ padding: '8px 14px', borderRadius: 8, border: 'none', background: canConfirm ? '#B45309' : '#D6D3D1', color: '#fff', fontWeight: 700, cursor: canConfirm ? 'pointer' : 'not-allowed' }}
                    >
                        {busy ? 'A assumir…' : 'Assumir pedido'}
                    </button>
                </div>
            </div>
        </ModalWrapper>
    );
}
