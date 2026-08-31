import React from 'react';
import { X } from 'lucide-react';
import type { BuyerWorkspaceBatch } from '../../types/buyerWorkspace';
import {
    reasonLabel, batchStatusLabel, cycleStateLabel, sourceStageLabel, cycleResponsibleLabel,
} from '../../lib/adjustmentReasons';

interface BatchDetailModalProps {
    batch: BuyerWorkspaceBatch | null;
    onClose: () => void;
}

const fmt = (iso?: string | null) => {
    if (!iso) return '—';
    const d = new Date(iso);
    return isNaN(d.getTime()) ? '—' : d.toLocaleString('pt-AO');
};

function Row({ label, children }: { label: string; children: React.ReactNode }) {
    return (
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, padding: '5px 0', fontSize: '0.82rem' }}>
            <span style={{ color: 'var(--color-text-muted)' }}>{label}</span>
            <span style={{ color: 'var(--color-text-main)', fontWeight: 600, textAlign: 'right' }}>{children}</span>
        </div>
    );
}

/**
 * Read-only batch details for the "Lotes & Aprovações" tab (Phase 3 quick improvement). Shows the
 * batch identity, a friendly batch-status label, and — when an OPEN structured adjustment cycle
 * exists — the cycle summary (origin, state, responsibility, reasons, approver comment, requester,
 * affected items). Purely informational: no action buttons, no editing, no later-phase surfaces.
 * Renders friendly labels only, never raw codes or GUIDs.
 */
export const BatchDetailModal: React.FC<BatchDetailModalProps> = ({ batch, onClose }) => {
    if (!batch) return null;
    const adj = batch.adjustment;

    return (
        <div
            role="dialog"
            aria-modal="true"
            onClick={onClose}
            style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.55)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1600, padding: 20 }}
        >
            <div
                onClick={(e) => e.stopPropagation()}
                style={{ background: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 14, width: '100%', maxWidth: 560, maxHeight: '85vh', overflowY: 'auto', boxShadow: 'var(--shadow-lg)' }}
            >
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 20px', borderBottom: '1px solid var(--color-border)' }}>
                    <div style={{ fontWeight: 800, fontSize: '1rem', color: 'var(--color-text-main)' }}>Lote {batch.batchNumber}</div>
                    <button onClick={onClose} aria-label="Fechar" style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)', padding: 4 }}>
                        <X size={18} />
                    </button>
                </div>

                <div style={{ padding: '14px 20px' }}>
                    <div style={{ fontSize: '0.68rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)', marginBottom: 6 }}>Situação do lote</div>
                    <div style={{ display: 'inline-block', padding: '4px 10px', borderRadius: 999, fontSize: '0.78rem', fontWeight: 700, background: 'var(--color-accent-soft, #eef2f7)', color: 'var(--color-text-main)', marginBottom: 10 }}>
                        {batchStatusLabel(batch.status)}
                    </div>
                    <Row label="Itens">{batch.itemCount}</Row>
                    <Row label="Criado em">{fmt(batch.createdAtUtc)}</Row>

                    <div style={{ height: 1, background: 'var(--color-border)', margin: '14px 0' }} />

                    {adj ? (
                        <div>
                            <div style={{ fontSize: '0.68rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-muted)', marginBottom: 8 }}>
                                Ciclo de reajuste #{adj.cycleNumber}
                            </div>
                            <Row label="Origem">{sourceStageLabel(adj.sourceStage)}</Row>
                            <Row label="Estado do ciclo">{cycleStateLabel(adj.status)}</Row>
                            <Row label="Próxima responsabilidade">{cycleResponsibleLabel(adj.status)}</Row>
                            <Row label="Solicitado por">{adj.requestedByName || '—'}</Row>
                            <Row label="Data/hora">{fmt(adj.requestedAtUtc)}</Row>

                            <div style={{ marginTop: 10 }}>
                                <div style={{ fontSize: '0.72rem', fontWeight: 800, color: 'var(--color-text-muted)', marginBottom: 6 }}>Motivos</div>
                                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                                    {adj.reasons.length === 0 && <span style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>—</span>}
                                    {adj.reasons.map((r, i) => (
                                        <span key={i} style={{ padding: '3px 9px', borderRadius: 999, fontSize: '0.74rem', fontWeight: 700, background: 'var(--requester-soft, #f5ebdc)', color: 'var(--color-text-main)' }}>
                                            {reasonLabel(r.reasonCode)}{r.lineNumber != null ? ` · item ${r.lineNumber}` : ''}
                                        </span>
                                    ))}
                                </div>
                            </div>

                            {!adj.wholeBatch && adj.reasons.some(r => r.lineNumber != null) && (
                                <Row label="Itens afetados">
                                    {Array.from(new Set(adj.reasons.filter(r => r.lineNumber != null).map(r => r.lineNumber))).sort((a, b) => (a! - b!)).map(n => `#${n}`).join(', ')}
                                </Row>
                            )}

                            <div style={{ marginTop: 10 }}>
                                <div style={{ fontSize: '0.72rem', fontWeight: 800, color: 'var(--color-text-muted)', marginBottom: 4 }}>Comentário do aprovador</div>
                                <div style={{ fontSize: '0.82rem', color: 'var(--color-text-main)', padding: '8px 10px', background: 'var(--color-bg-page)', border: '1px solid var(--color-border)', borderRadius: 8, whiteSpace: 'pre-wrap' }}>
                                    {adj.approverComment || '—'}
                                </div>
                            </div>
                        </div>
                    ) : (
                        <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>
                            Este lote não possui um ciclo de reajuste estruturado.
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
};
