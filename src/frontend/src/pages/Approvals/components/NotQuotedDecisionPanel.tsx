import { useState } from 'react';
import { AlertTriangle, CheckCircle2, XCircle, Clock, User } from 'lucide-react';
import { RequestLineItemDto } from '../../../types';
import { formatDate } from '../../../lib/utils';
import { NotQuotedDecisionModal, NotQuotedDecisionAction } from './NotQuotedDecisionModal';

interface NotQuotedDecisionPanelProps {
    requestId: string;
    lineItems: RequestLineItemDto[];
    canDecide: boolean;
    onDecided: () => void;
}

// Not-quoted decisions (accept/reject a buyer's "no quote available" declaration)
// are deliberately kept separate from the Approval Batch Wizard: an item here
// never belongs to any ApprovalBatch, is never a quotation winner/loser, and
// carries no budget/PO/financial implication — it's purely a decision about
// whether the request's original line item will simply go unquoted.
export function NotQuotedDecisionPanel({ requestId, lineItems, canDecide, onDecided }: NotQuotedDecisionPanelProps) {
    const [activeDecision, setActiveDecision] = useState<{ action: NotQuotedDecisionAction; lineItem: RequestLineItemDto } | null>(null);

    const proposedItems = (lineItems || []).filter(li => li.quotationLifecycleStatus === 'NOT_QUOTED_PROPOSED');

    if (proposedItems.length === 0) return null;

    return (
        <div style={{
            backgroundColor: '#fffbeb',
            border: '1px solid #fcd34d',
            borderRadius: '12px',
            padding: '20px',
            display: 'flex',
            flexDirection: 'column',
            gap: '16px'
        }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <AlertTriangle size={18} color="#b45309" />
                <h3 style={{ margin: 0, fontSize: '0.95rem', fontWeight: 800, color: '#92400e' }}>
                    Itens Propostos como Não Cotado
                </h3>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                {proposedItems.map(item => (
                    <div key={item.id} style={{
                        backgroundColor: '#fff',
                        border: '1px solid var(--color-border)',
                        borderRadius: '8px',
                        padding: '16px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '10px'
                    }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '12px' }}>
                            <div>
                                <div style={{ fontWeight: 700, fontSize: '0.9rem', color: 'var(--color-text-main)' }}>
                                    Linha {item.lineNumber} — {item.description}
                                </div>
                                <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                    Qtd: {item.quantity} {item.unit || ''}
                                </div>
                            </div>
                            <span style={{
                                fontSize: '0.7rem', fontWeight: 700, padding: '4px 8px', borderRadius: '4px',
                                backgroundColor: '#fef3c7', color: '#92400e', whiteSpace: 'nowrap'
                            }}>
                                Aguardando decisão
                            </span>
                        </div>

                        {item.notQuotedJustification && (
                            <div style={{ backgroundColor: '#f9fafb', borderRadius: '6px', padding: '10px 12px', fontSize: '0.85rem', color: 'var(--color-text-main)' }}>
                                <div style={{ fontSize: '0.7rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '4px' }}>
                                    Justificativa do comprador
                                </div>
                                {item.notQuotedJustification}
                            </div>
                        )}

                        {(item.notQuotedProposedByName || item.notQuotedProposedAtUtc) && (
                            <div style={{ display: 'flex', gap: '16px', fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                {item.notQuotedProposedByName && (
                                    <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                        <User size={12} /> {item.notQuotedProposedByName}
                                    </span>
                                )}
                                {item.notQuotedProposedAtUtc && (
                                    <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                        <Clock size={12} /> {formatDate(item.notQuotedProposedAtUtc)}
                                    </span>
                                )}
                            </div>
                        )}

                        {canDecide && (
                            <div style={{ display: 'flex', gap: '8px', marginTop: '4px' }}>
                                <button
                                    onClick={() => setActiveDecision({ action: 'ACCEPT', lineItem: item })}
                                    style={{
                                        display: 'flex', alignItems: 'center', gap: '6px',
                                        padding: '8px 14px', fontSize: '0.8rem', fontWeight: 700,
                                        backgroundColor: '#059669', color: '#fff', border: 'none',
                                        borderRadius: '6px', cursor: 'pointer'
                                    }}
                                >
                                    <CheckCircle2 size={14} /> Aceitar
                                </button>
                                <button
                                    onClick={() => setActiveDecision({ action: 'REJECT', lineItem: item })}
                                    style={{
                                        display: 'flex', alignItems: 'center', gap: '6px',
                                        padding: '8px 14px', fontSize: '0.8rem', fontWeight: 700,
                                        backgroundColor: '#fff', color: '#dc2626', border: '1px solid #fecaca',
                                        borderRadius: '6px', cursor: 'pointer'
                                    }}
                                >
                                    <XCircle size={14} /> Rejeitar / Retornar
                                </button>
                            </div>
                        )}
                    </div>
                ))}
            </div>

            <NotQuotedDecisionModal
                isOpen={activeDecision !== null}
                onClose={() => setActiveDecision(null)}
                action={activeDecision?.action || 'ACCEPT'}
                requestId={requestId}
                lineItemId={activeDecision?.lineItem.id || ''}
                itemDescription={activeDecision ? `Linha ${activeDecision.lineItem.lineNumber} — ${activeDecision.lineItem.description}` : ''}
                onSuccess={() => {
                    setActiveDecision(null);
                    onDecided();
                }}
            />
        </div>
    );
}
