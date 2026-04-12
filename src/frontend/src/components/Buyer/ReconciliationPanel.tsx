import React, { useState } from 'react';
import { CheckCircle2, AlertTriangle, HelpCircle, XCircle, ArrowRight, GitCompareArrows, ChevronDown, ChevronUp, Send, Package } from 'lucide-react';
import { ReconciliationBatchDto, ReconciliationRecordDto, ReconciliationReviewItemDto } from '../../types';
import { formatCurrencyAO } from '../../lib/utils';
import { api } from '../../lib/api';

interface ReconciliationPanelProps {
    requestId: string;
    batch: ReconciliationBatchDto;
    onBatchUpdated: (updatedBatch: ReconciliationBatchDto) => void;
}

const MATCH_STATUS_CONFIG: Record<string, { label: string; icon: React.ReactNode; color: string; bg: string }> = {
    'EXACT_MATCH': { label: 'Correspondência Exata', icon: <CheckCircle2 size={14} />, color: '#059669', bg: '#ecfdf5' },
    'PROBABLE_MATCH': { label: 'Correspondência Provável', icon: <HelpCircle size={14} />, color: '#d97706', bg: '#fffbeb' },
    'REVIEW_REQUIRED': { label: 'Revisão Necessária', icon: <AlertTriangle size={14} />, color: '#dc2626', bg: '#fef2f2' },
    'EXTRA_SUPPLIER_ITEM': { label: 'Item Extra (Fornecedor)', icon: <ArrowRight size={14} />, color: '#6366f1', bg: '#eef2ff' },
    'MISSING_REQUESTED_ITEM': { label: 'Item Ausente (Pedido)', icon: <XCircle size={14} />, color: '#dc2626', bg: '#fef2f2' },
};

const REVIEW_STATUS_CONFIG: Record<string, { label: string; color: string; bg: string }> = {
    'PENDING': { label: 'Pendente', color: '#6b7280', bg: '#f3f4f6' },
    'CONFIRMED': { label: 'Confirmado', color: '#059669', bg: '#ecfdf5' },
    'REJECTED': { label: 'Rejeitado', color: '#dc2626', bg: '#fef2f2' },
    'ADJUSTED': { label: 'Ajustado', color: '#d97706', bg: '#fffbeb' },
};

export const ReconciliationPanel: React.FC<ReconciliationPanelProps> = ({ requestId, batch, onBatchUpdated }) => {
    const [expanded, setExpanded] = useState(true);
    const [pendingReviews, setPendingReviews] = useState<Record<string, ReconciliationReviewItemDto>>({});
    const [submitting, setSubmitting] = useState(false);
    const [justifications, setJustifications] = useState<Record<string, string>>({});

    const { summary, records } = batch;

    if (!records || records.length === 0) return null;

    const handleSetReview = (recordId: string, status: string) => {
        setPendingReviews(prev => ({
            ...prev,
            [recordId]: {
                recordId,
                reviewStatus: status,
                justification: justifications[recordId] || undefined,
            }
        }));
    };

    const handleSetJustification = (recordId: string, text: string) => {
        setJustifications(prev => ({ ...prev, [recordId]: text }));
        // Update pending review if already set
        if (pendingReviews[recordId]) {
            setPendingReviews(prev => ({
                ...prev,
                [recordId]: { ...prev[recordId], justification: text || undefined }
            }));
        }
    };

    const handleSubmitReviews = async () => {
        const reviews = Object.values(pendingReviews);
        if (reviews.length === 0) return;

        setSubmitting(true);
        try {
            await api.requests.submitReconciliationReview(requestId, reviews);
            // Reload reconciliation data
            const updated = await api.requests.getReconciliation(requestId);
            onBatchUpdated(updated);
            setPendingReviews({});
        } catch (err) {
            console.error('Failed to submit reconciliation review:', err);
        } finally {
            setSubmitting(false);
        }
    };

    const pendingCount = Object.keys(pendingReviews).length;

    const getConfidenceBar = (confidence: number | null) => {
        if (confidence === null || confidence === undefined) return null;
        const pct = Math.round(confidence * 100);
        const color = pct >= 80 ? '#059669' : pct >= 50 ? '#d97706' : '#dc2626';
        return (
            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <div style={{ width: '50px', height: '6px', backgroundColor: '#e5e7eb', borderRadius: '3px', overflow: 'hidden' }}>
                    <div style={{ width: `${pct}%`, height: '100%', backgroundColor: color, borderRadius: '3px' }} />
                </div>
                <span style={{ fontSize: '0.65rem', fontWeight: 700, color }}>{pct}%</span>
            </div>
        );
    };

    return (
        <div style={{
            border: '1px solid var(--color-border)',
            borderRadius: '8px',
            backgroundColor: 'var(--color-bg-surface)',
            overflow: 'hidden',
            marginBottom: '16px'
        }}>
            {/* Header */}
            <div
                onClick={() => setExpanded(!expanded)}
                style={{
                    padding: '12px 16px',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    cursor: 'pointer',
                    backgroundColor: '#f8fafc',
                    borderBottom: expanded ? '1px solid var(--color-border)' : 'none'
                }}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <div style={{ backgroundColor: '#eef2ff', padding: '6px', borderRadius: '6px', color: '#4f46e5' }}>
                        <GitCompareArrows size={18} />
                    </div>
                    <div>
                        <div style={{ fontWeight: 800, fontSize: '0.85rem', color: 'var(--color-text-main)' }}>
                            Reconciliação OCR
                        </div>
                        <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                            {summary.totalRecords} registros
                            {summary.exactMatches > 0 && <span> · {summary.exactMatches} exatos</span>}
                            {summary.probableMatches > 0 && <span> · {summary.probableMatches} prováveis</span>}
                            {summary.reviewRequired > 0 && <span> · <strong style={{ color: '#dc2626' }}>{summary.reviewRequired} p/ revisão</strong></span>}
                            {summary.missingRequestedItems > 0 && <span> · <strong style={{ color: '#dc2626' }}>{summary.missingRequestedItems} ausentes</strong></span>}
                            {summary.extraSupplierItems > 0 && <span> · {summary.extraSupplierItems} extras</span>}
                        </div>
                    </div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    {/* Summary Badges */}
                    <div style={{ display: 'flex', gap: '4px' }}>
                        {summary.buyerConfirmed > 0 && (
                            <span style={{ fontSize: '0.6rem', fontWeight: 800, padding: '2px 6px', borderRadius: '4px', backgroundColor: '#ecfdf5', color: '#059669' }}>
                                ✓ {summary.buyerConfirmed}
                            </span>
                        )}
                        {summary.buyerPending > 0 && (
                            <span style={{ fontSize: '0.6rem', fontWeight: 800, padding: '2px 6px', borderRadius: '4px', backgroundColor: '#f3f4f6', color: '#6b7280' }}>
                                ⏳ {summary.buyerPending}
                            </span>
                        )}
                    </div>
                    <div style={{ color: 'var(--color-text-muted)', transition: 'transform 0.2s' }}>
                        {expanded ? <ChevronUp size={18} /> : <ChevronDown size={18} />}
                    </div>
                </div>
            </div>

            {/* Body */}
            {expanded && (
                <div style={{ padding: '16px' }}>
                    {/* Info banner */}
                    <div style={{
                        padding: '10px 14px',
                        backgroundColor: '#eff6ff',
                        border: '1px solid #bfdbfe',
                        borderRadius: '6px',
                        fontSize: '0.72rem',
                        color: '#1e40af',
                        fontWeight: 600,
                        marginBottom: '16px'
                    }}>
                        📋 Esta reconciliação compara os <strong>itens solicitados pelo requisitante</strong> com os <strong>itens extraídos do documento</strong> por OCR. Revise as correspondências e confirme, rejeite ou ajuste conforme necessário. Esta etapa é <strong>informativa</strong> e não bloqueia o salvamento da cotação.
                    </div>

                    {/* Records Table */}
                    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.75rem', border: '1px solid var(--color-border)', borderRadius: '6px', overflow: 'hidden' }}>
                        <thead>
                            <tr style={{ backgroundColor: 'var(--color-bg-page)' }}>
                                <th style={{ ...thStyle, width: '30%' }}>ITEM SOLICITADO</th>
                                <th style={{ ...thStyle, width: '30%' }}>ITEM OCR (DOCUMENTO)</th>
                                <th style={{ ...thStyle, width: '15%', textAlign: 'center' }}>STATUS</th>
                                <th style={{ ...thStyle, width: '25%', textAlign: 'center' }}>AÇÃO</th>
                            </tr>
                        </thead>
                        <tbody>
                            {records.map((record) => {
                                const matchConfig = MATCH_STATUS_CONFIG[record.matchStatus] || MATCH_STATUS_CONFIG['REVIEW_REQUIRED'];
                                const currentReview = pendingReviews[record.id];
                                const effectiveStatus = currentReview?.reviewStatus || record.buyerReviewStatus;
                                const reviewConfig = REVIEW_STATUS_CONFIG[effectiveStatus] || REVIEW_STATUS_CONFIG['PENDING'];

                                return (
                                    <tr key={record.id} style={{ borderBottom: '1px solid var(--color-border-light)', backgroundColor: 'var(--color-bg-surface)' }}>
                                        {/* Requester Item */}
                                        <td style={tdStyle}>
                                            {record.requesterItemId ? (
                                                <div>
                                                    <div style={{ fontWeight: 700, color: 'var(--color-text-main)', marginBottom: '2px' }}>
                                                        {record.requesterDescription || '—'}
                                                    </div>
                                                    <div style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', display: 'flex', gap: '8px' }}>
                                                        <span>Qtd: {record.requesterQuantity ?? '—'}</span>
                                                        <span>Unid: {record.requesterUnitCode || '—'}</span>
                                                        {record.requesterCatalogCode && (
                                                            <span style={{ display: 'flex', alignItems: 'center', gap: '2px' }}>
                                                                <Package size={10} /> {record.requesterCatalogCode}
                                                            </span>
                                                        )}
                                                    </div>
                                                </div>
                                            ) : (
                                                <span style={{ color: '#9ca3af', fontStyle: 'italic' }}>— sem correspondência —</span>
                                            )}
                                        </td>

                                        {/* OCR Item */}
                                        <td style={tdStyle}>
                                            {record.ocrExtractedItemId ? (
                                                <div>
                                                    <div style={{ fontWeight: 700, color: 'var(--color-text-main)', marginBottom: '2px' }}>
                                                        {record.ocrDescription || '—'}
                                                    </div>
                                                    <div style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', display: 'flex', gap: '8px' }}>
                                                        <span>Qtd: {record.ocrQuantity ?? '—'}</span>
                                                        <span>Unid: {record.ocrRawUnit || '—'}</span>
                                                        {record.ocrUnitPrice != null && <span>P.Unit: {formatCurrencyAO(record.ocrUnitPrice)}</span>}
                                                    </div>
                                                    {record.ocrLineTotal != null && (
                                                        <div style={{ fontSize: '0.65rem', color: 'var(--color-primary)', fontWeight: 700 }}>
                                                            Total: {formatCurrencyAO(record.ocrLineTotal)}
                                                        </div>
                                                    )}
                                                </div>
                                            ) : (
                                                <span style={{ color: '#9ca3af', fontStyle: 'italic' }}>— não encontrado no documento —</span>
                                            )}
                                        </td>

                                        {/* Match Status */}
                                        <td style={{ ...tdStyle, textAlign: 'center' }}>
                                            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '4px' }}>
                                                <span style={{
                                                    display: 'inline-flex', alignItems: 'center', gap: '4px',
                                                    fontSize: '0.6rem', fontWeight: 800, padding: '3px 8px',
                                                    borderRadius: '4px', backgroundColor: matchConfig.bg, color: matchConfig.color,
                                                    textTransform: 'uppercase', whiteSpace: 'nowrap'
                                                }}>
                                                    {matchConfig.icon} {matchConfig.label}
                                                </span>
                                                {getConfidenceBar(record.matchConfidence)}
                                                {record.quantityDivergence != null && record.quantityDivergence !== 0 && (
                                                    <span style={{ fontSize: '0.6rem', color: record.quantityDivergence > 0 ? '#059669' : '#dc2626', fontWeight: 700 }}>
                                                        Qtd: {record.quantityDivergence > 0 ? '+' : ''}{record.quantityDivergence}
                                                    </span>
                                                )}
                                                {record.unitDivergence && (
                                                    <span style={{ fontSize: '0.6rem', color: '#d97706', fontWeight: 700 }}>
                                                        ⚠ Unid. diferente
                                                    </span>
                                                )}
                                            </div>
                                        </td>

                                        {/* Action */}
                                        <td style={{ ...tdStyle, textAlign: 'center' }}>
                                            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '6px' }}>
                                                {/* Current/pending review badge */}
                                                <span style={{
                                                    fontSize: '0.6rem', fontWeight: 800, padding: '2px 6px',
                                                    borderRadius: '4px', backgroundColor: reviewConfig.bg, color: reviewConfig.color,
                                                    textTransform: 'uppercase'
                                                }}>
                                                    {reviewConfig.label}
                                                </span>

                                                {/* Action buttons */}
                                                <div style={{ display: 'flex', gap: '4px' }}>
                                                    <button
                                                        onClick={() => handleSetReview(record.id, 'CONFIRMED')}
                                                        style={{
                                                            ...actionBtnStyle,
                                                            backgroundColor: effectiveStatus === 'CONFIRMED' ? '#059669' : 'transparent',
                                                            color: effectiveStatus === 'CONFIRMED' ? '#fff' : '#059669',
                                                            border: '1px solid #059669'
                                                        }}
                                                        title="Confirmar"
                                                    >✓</button>
                                                    <button
                                                        onClick={() => handleSetReview(record.id, 'REJECTED')}
                                                        style={{
                                                            ...actionBtnStyle,
                                                            backgroundColor: effectiveStatus === 'REJECTED' ? '#dc2626' : 'transparent',
                                                            color: effectiveStatus === 'REJECTED' ? '#fff' : '#dc2626',
                                                            border: '1px solid #dc2626'
                                                        }}
                                                        title="Rejeitar"
                                                    >✗</button>
                                                    <button
                                                        onClick={() => handleSetReview(record.id, 'ADJUSTED')}
                                                        style={{
                                                            ...actionBtnStyle,
                                                            backgroundColor: effectiveStatus === 'ADJUSTED' ? '#d97706' : 'transparent',
                                                            color: effectiveStatus === 'ADJUSTED' ? '#fff' : '#d97706',
                                                            border: '1px solid #d97706'
                                                        }}
                                                        title="Ajustar"
                                                    >~</button>
                                                </div>

                                                {/* Justification */}
                                                {(currentReview || record.buyerReviewStatus !== 'PENDING') && (
                                                    <input
                                                        type="text"
                                                        placeholder="Justificativa (opcional)"
                                                        value={justifications[record.id] ?? record.buyerJustification ?? ''}
                                                        onChange={(e) => handleSetJustification(record.id, e.target.value)}
                                                        style={{
                                                            width: '100%', fontSize: '0.65rem', padding: '4px 6px',
                                                            border: '1px solid var(--color-border)',
                                                            borderRadius: '4px', backgroundColor: 'var(--color-bg-page)'
                                                        }}
                                                    />
                                                )}
                                            </div>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>

                    {/* Submit Bar */}
                    {pendingCount > 0 && (
                        <div style={{
                            marginTop: '12px',
                            display: 'flex',
                            justifyContent: 'flex-end',
                            alignItems: 'center',
                            gap: '12px'
                        }}>
                            <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                {pendingCount} revisão(ões) pendente(s)
                            </span>
                            <button
                                onClick={handleSubmitReviews}
                                disabled={submitting}
                                style={{
                                    display: 'flex', alignItems: 'center', gap: '6px',
                                    padding: '8px 16px', backgroundColor: '#4f46e5', color: '#fff',
                                    border: 'none', borderRadius: '6px', fontWeight: 800,
                                    fontSize: '0.75rem', cursor: submitting ? 'wait' : 'pointer',
                                    opacity: submitting ? 0.7 : 1, transition: 'opacity 0.2s'
                                }}
                            >
                                <Send size={14} />
                                {submitting ? 'Enviando...' : 'Salvar Revisão'}
                            </button>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
};

const thStyle: React.CSSProperties = {
    padding: '10px 12px',
    textAlign: 'left',
    fontWeight: 800,
    fontSize: '0.65rem',
    textTransform: 'uppercase',
    color: 'var(--color-text-muted)',
    borderBottom: '2px solid var(--color-border)',
    letterSpacing: '0.5px'
};

const tdStyle: React.CSSProperties = {
    padding: '10px 12px',
    verticalAlign: 'top'
};

const actionBtnStyle: React.CSSProperties = {
    width: '24px',
    height: '24px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: '4px',
    fontWeight: 900,
    fontSize: '0.7rem',
    cursor: 'pointer',
    transition: 'all 0.15s'
};
