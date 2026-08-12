import { useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import { ModalWrapper } from '../common/ModalWrapper';
import { operationInvoiceApi } from '../../lib/operationInvoiceApi';
import { formatMoney, coverageView, mapOperationInvoiceError } from '../../lib/operationInvoiceView';
import type {
    OperationInvoiceObligationDto,
    OperationInvoiceShortCloseDto
} from '../../types/operationInvoice';

interface ShortCloseModalProps {
    requestId: string;
    obligation: OperationInvoiceObligationDto;
    shortCloses: OperationInvoiceShortCloseDto[];
    canWrite: boolean;
    canDecide: boolean;
    currentUserId: string | null;
    onClose: () => void;
    onChanged: () => void;
}

/**
 * Release 4 Phase 3B — short-close ("Encerramento com Saldo") of one group's obligation.
 *
 * Two-person decision, structurally: the proposer never sees Approve on their own proposal —
 * their path out is "Retirar Proposta" (the backend's self-rejection). Deciders see
 * Approve/Reject with a mandatory reason on rejection.
 */
export function OperationInvoiceShortCloseModal({
    requestId, obligation, shortCloses, canWrite, canDecide, currentUserId, onClose, onChanged
}: ShortCloseModalProps) {
    const [justification, setJustification] = useState('');
    const [decisionReason, setDecisionReason] = useState('');
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const view = coverageView(obligation);
    const pending = shortCloses.find(c => c.status === 'PROPOSED');
    const isProposer = !!pending && pending.proposedByUserId === currentUserId;
    const decided = shortCloses.filter(c => c.status !== 'PROPOSED');

    const run = async (fn: () => Promise<unknown>) => {
        setSaving(true);
        setError(null);
        try {
            await fn();
            onChanged();
        } catch (err) {
            setError(mapOperationInvoiceError(err).message);
        } finally {
            setSaving(false);
        }
    };

    const propose = () => run(() =>
        operationInvoiceApi.proposeShortClose(requestId, obligation.groupId, {
            justification: justification.trim()
        }));

    const approve = () => run(() =>
        operationInvoiceApi.approveShortClose(requestId, obligation.groupId, pending!.id, {
            decisionReason: decisionReason.trim() || null,
            rowVersion: pending!.rowVersion ?? null
        }));

    const reject = (isWithdrawal: boolean) => {
        if (decisionReason.trim().length === 0) {
            setError(isWithdrawal ? 'Indique o motivo da retirada.' : 'Indique o motivo da rejeição.');
            return;
        }
        void run(() =>
            operationInvoiceApi.rejectShortClose(requestId, obligation.groupId, pending!.id, {
                decisionReason: decisionReason.trim(),
                rowVersion: pending!.rowVersion ?? null
            }));
    };

    const labelStyle: React.CSSProperties = {
        fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase',
        color: 'var(--color-text-muted)', display: 'block', marginBottom: '4px'
    };
    const textareaStyle: React.CSSProperties = {
        width: '100%', minHeight: '64px', padding: '8px 10px', boxSizing: 'border-box',
        border: '1px solid var(--color-border)', borderRadius: '8px', fontSize: '0.88rem', resize: 'vertical'
    };

    return (
        <ModalWrapper title="Encerramento com Saldo" onClose={onClose} width={620}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                <div style={{ fontWeight: 800 }}>{obligation.supplierName || '—'}</div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))', gap: '8px', fontSize: '0.83rem' }}>
                    <span>Esperado: <b>{view.expectedLabel}</b></span>
                    <span>Validado: <b style={{ color: '#15803d' }}>{view.validatedLabel}</b></span>
                    <span>Restante: <b>{view.remainingLabel}</b></span>
                </div>

                {/* ── Pending proposal: review / decide / withdraw ── */}
                {pending && (
                    <div style={{ border: '1px solid #fdba74', backgroundColor: '#fff7ed', borderRadius: '8px', padding: '12px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        <div style={{ fontWeight: 800, color: '#9a3412' }}>Proposta pendente de decisão</div>
                        <div style={{ fontSize: '0.83rem' }}>
                            <b>Saldo a encerrar (congelado na proposta):</b>{' '}
                            {formatMoney(pending.remainingAmountAtProposal, obligation.currency)}
                        </div>
                        <div style={{ fontSize: '0.83rem' }}>
                            <b>Proposta por:</b> {pending.proposedByName || '—'} em{' '}
                            {new Date(pending.proposedAtUtc).toLocaleDateString('pt-BR')}
                        </div>
                        <div style={{ fontSize: '0.83rem' }}>
                            <b>Justificativa:</b> {pending.proposalJustification}
                        </div>

                        {(canDecide || isProposer) && (
                            <>
                                <div>
                                    <label style={labelStyle}>
                                        {isProposer && !canDecide ? 'Motivo da retirada *'
                                            : 'Motivo da decisão (obrigatório para rejeitar)'}
                                    </label>
                                    <textarea style={textareaStyle} value={decisionReason}
                                              onChange={e => setDecisionReason(e.target.value)} />
                                </div>
                                <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                                    {isProposer ? (
                                        <button onClick={() => reject(true)} disabled={saving} style={{
                                            padding: '8px 16px', border: '1px solid #9a3412', backgroundColor: '#fff',
                                            color: '#9a3412', borderRadius: '8px', fontWeight: 800, cursor: 'pointer'
                                        }}>
                                            Retirar Proposta
                                        </button>
                                    ) : canDecide && (
                                        <>
                                            <button onClick={() => reject(false)} disabled={saving} style={{
                                                padding: '8px 16px', border: '1px solid #dc2626', backgroundColor: '#fff',
                                                color: '#dc2626', borderRadius: '8px', fontWeight: 800, cursor: 'pointer'
                                            }}>
                                                Rejeitar
                                            </button>
                                            <button onClick={() => void approve()} disabled={saving} style={{
                                                padding: '8px 16px', border: 'none', backgroundColor: '#15803d',
                                                color: '#fff', borderRadius: '8px', fontWeight: 800, cursor: 'pointer'
                                            }}>
                                                Aprovar Encerramento
                                            </button>
                                        </>
                                    )}
                                </div>
                                {isProposer && canDecide && (
                                    <div style={{ fontSize: '0.76rem', color: '#9a3412', fontWeight: 600 }}>
                                        A aprovação exige uma segunda pessoa — quem propôs não pode aprovar.
                                    </div>
                                )}
                            </>
                        )}
                    </div>
                )}

                {/* ── New proposal ── */}
                {!pending && !obligation.closedShort && canWrite && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                        <div style={{ fontSize: '0.83rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                            Propõe encerrar o saldo restante de <b>{view.remainingLabel}</b> — o grupo passa a
                            contar como concluído com saldo aceite APÓS a aprovação de uma segunda pessoa do
                            Financeiro ou Administração.
                        </div>
                        <div>
                            <label style={labelStyle}>Justificativa *</label>
                            <textarea style={textareaStyle} value={justification}
                                      onChange={e => setJustification(e.target.value)}
                                      placeholder="Por que o valor restante não será faturado? (mínimo 20 caracteres significativos)" />
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                            <button onClick={() => void propose()} disabled={saving || justification.trim().length < 20} style={{
                                padding: '9px 18px', border: 'none', backgroundColor: 'var(--color-primary)',
                                color: '#fff', borderRadius: '8px', fontWeight: 800, cursor: 'pointer',
                                opacity: saving || justification.trim().length < 20 ? 0.6 : 1
                            }}>
                                {saving ? 'A enviar…' : 'Propor Encerramento com Saldo'}
                            </button>
                        </div>
                    </div>
                )}

                {/* ── History ── */}
                {decided.length > 0 && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        <span style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Histórico</span>
                        {decided.map(c => (
                            <div key={c.id} style={{ fontSize: '0.8rem', border: '1px solid var(--color-border)', borderRadius: '8px', padding: '8px 10px' }}>
                                <b>{c.status === 'APPROVED' ? 'Aprovado' : 'Rejeitado/Retirado'}</b>
                                {' — '}{formatMoney(c.remainingAmountAtProposal, obligation.currency)}
                                {' · proposta de '}{c.proposedByName || '—'}
                                {c.decidedByName ? ` · decidido por ${c.decidedByName}` : ''}
                                {c.decisionReason ? ` · ${c.decisionReason}` : ''}
                            </div>
                        ))}
                    </div>
                )}

                {error && (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#b91c1c', fontSize: '0.85rem', fontWeight: 700 }}>
                        <AlertTriangle size={15} /> {error}
                    </div>
                )}
            </div>
        </ModalWrapper>
    );
}
