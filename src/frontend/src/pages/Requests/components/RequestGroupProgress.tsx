import React from 'react';
import { CheckCircle2, Circle, CircleDot, AlertTriangle } from 'lucide-react';
import type { RequestWorkflowProjection, WorkflowUnit } from '../../../types';

/**
 * v2.230.0 — "Progresso por Grupo": one card per active operational unit
 * (in-approval ApprovalBatch before PO-group creation, RequestPoGroup after).
 * Read-only presentation of the workflow projection; superseded batches never
 * appear here (they surface via the warnings banner + history only).
 * Visual language mirrors the Finance module cards (compact, corporate, PT).
 */

interface RequestGroupProgressProps {
    projection: RequestWorkflowProjection;
    /** Admin-only diagnostics: superseded-batch warnings are shown when true. */
    showWarnings?: boolean;
}

type StageState = 'done' | 'current' | 'pending' | 'na';

interface Stage {
    label: string;
    state: StageState;
}

function buildStages(unit: WorkflowUnit): Stage[] {
    if (unit.unitType === 'BATCH') {
        return [
            { label: 'Aprovação', state: unit.approvalState === 'ADJUSTMENT' ? 'current' : 'current' },
            { label: 'P.O.', state: 'pending' },
            { label: 'Pagamento', state: 'pending' },
            { label: 'Recebimento', state: 'pending' },
            { label: 'Conclusão', state: 'pending' },
        ];
    }

    const po: StageState = unit.poState === 'ISSUED' ? 'done'
        : unit.poState === 'CORRECTION' ? 'current'
        : unit.paymentState === 'ADVANCE_IN_PROGRESS' ? 'current'
        : 'current';
    const payment: StageState = unit.paymentState === 'COMPLETE' ? 'done'
        : unit.paymentState === 'SCHEDULED' || unit.paymentState === 'ADVANCE_IN_PROGRESS' ? 'current'
        : unit.paymentState === 'PENDING' && po === 'done' ? 'current'
        : 'pending';
    const receiving: StageState = unit.receivingState === 'COMPLETE' ? 'done'
        : unit.receivingState === 'IN_PROGRESS' ? 'current'
        : unit.receivingState === 'PENDING' && payment === 'done' ? 'current'
        : 'pending';
    const completion: StageState = unit.completionState === 'COMPLETE' ? 'done'
        : unit.completionState === 'WAITING_FISCAL_RECEIPT' ? 'current'
        : 'pending';

    return [
        { label: 'Aprovação', state: 'done' },
        { label: 'P.O.', state: po },
        { label: 'Pagamento', state: payment },
        { label: 'Recebimento', state: receiving },
        { label: 'Conclusão', state: completion },
    ];
}

const StageIcon: React.FC<{ state: StageState }> = ({ state }) => {
    if (state === 'done') return <CheckCircle2 size={14} style={{ color: 'var(--color-status-green, #16a34a)' }} />;
    if (state === 'current') return <CircleDot size={14} style={{ color: 'var(--color-primary)' }} />;
    return <Circle size={14} style={{ color: 'var(--color-text-muted)', opacity: 0.5 }} />;
};

export const RequestGroupProgress: React.FC<RequestGroupProgressProps> = ({ projection, showWarnings }) => {
    if (projection.units.length === 0 && (!showWarnings || projection.warnings.length === 0)) return null;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            <h3 style={{
                fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase',
                color: 'var(--color-text-muted)', margin: 0, letterSpacing: '0.05em'
            }}>
                Progresso por Grupo
            </h3>

            {showWarnings && projection.warnings.map((warning, i) => (
                <div key={`w-${i}`} style={{
                    display: 'flex', alignItems: 'center', gap: '8px',
                    padding: '8px 12px', borderRadius: '8px',
                    backgroundColor: 'rgba(245, 158, 11, 0.08)',
                    border: '1px solid rgba(245, 158, 11, 0.35)',
                    fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-main)'
                }}>
                    <AlertTriangle size={14} style={{ color: 'var(--color-status-amber, #d97706)', flexShrink: 0 }} />
                    <span>{warning} <span style={{ color: 'var(--color-text-muted)' }}>(diagnóstico administrativo — o histórico completo permanece na linha do tempo)</span></span>
                </div>
            ))}

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', gap: '12px' }}>
                {projection.units.map(unit => {
                    const stages = buildStages(unit);
                    return (
                        <div key={unit.unitId} style={{
                            border: '1px solid var(--color-border)',
                            borderRadius: '10px',
                            padding: '12px 14px',
                            backgroundColor: 'var(--color-bg-surface)',
                            display: 'flex', flexDirection: 'column', gap: '10px'
                        }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '8px' }}>
                                <div style={{ minWidth: 0 }}>
                                    <p style={{ fontSize: '0.8rem', fontWeight: 800, color: 'var(--color-text-main)', margin: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                        {unit.label}
                                    </p>
                                    <p style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', margin: '2px 0 0', fontWeight: 600 }}>
                                        {unit.itemCount > 0 && `${unit.itemCount} ite${unit.itemCount > 1 ? 'ns' : 'm'}`}
                                        {unit.itemCount > 0 && unit.totalAmount > 0 && ' · '}
                                        {unit.totalAmount > 0 && `${unit.currencyCode || 'AOA'} ${unit.totalAmount.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}`}
                                        {unit.purchaseOrderNumber && ` · P.O. ${unit.purchaseOrderNumber}`}
                                    </p>
                                </div>
                                <span style={{
                                    fontSize: '0.6rem', fontWeight: 800, textTransform: 'uppercase',
                                    padding: '3px 8px', borderRadius: '999px', whiteSpace: 'nowrap',
                                    backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)',
                                    color: 'var(--color-primary)'
                                }}>
                                    {unit.statusLabel}
                                </span>
                            </div>

                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                                {stages.map((stage, i) => (
                                    <React.Fragment key={stage.label}>
                                        {i > 0 && <div style={{ width: '10px', height: '1px', backgroundColor: 'var(--color-border)' }} />}
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                            <StageIcon state={stage.state} />
                                            <span style={{
                                                fontSize: '0.65rem',
                                                fontWeight: stage.state === 'current' ? 800 : 600,
                                                color: stage.state === 'pending' ? 'var(--color-text-muted)' : 'var(--color-text-main)'
                                            }}>
                                                {stage.label}
                                            </span>
                                        </div>
                                    </React.Fragment>
                                ))}
                            </div>

                            {unit.nextAction && (
                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px', borderTop: '1px dashed var(--color-border)', paddingTop: '8px' }}>
                                    <span style={{ fontSize: '0.6rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Próxima ação:</span>
                                    <span style={{ fontSize: '0.7rem', fontWeight: 700, fontStyle: 'italic', color: 'var(--color-text-main)' }}>
                                        {unit.nextAction.label}
                                    </span>
                                    <span style={{ fontSize: '0.6rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-primary)' }}>
                                        ({unit.responsibleRole})
                                    </span>
                                </div>
                            )}
                        </div>
                    );
                })}
            </div>
        </div>
    );
};
