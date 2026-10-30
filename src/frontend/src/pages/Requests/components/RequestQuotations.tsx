import React, { useMemo, useState } from 'react';
import {
    FileText,
    CheckCircle2,
    ChevronDown,
    Hash,
    Calendar
} from 'lucide-react';
import { SavedQuotationDto } from '../../../types';
import { formatCurrencyAO, formatDate } from '../../../lib/utils';
import { describeQuotationItemAdjudication, describeQuotationLifecycle, describeQuotationSelectability, LIFECYCLE_TONE_STYLE } from '../../../lib/quotationLifecycle';
import { compareQuotationsPerLine, describeCardComparison, describeItemComparison, describeScopeNotice } from '../../../lib/quotationComparison';

interface RequestQuotationsProps {
    quotations: SavedQuotationDto[];
    isFinalApproverMode: boolean;
    isDecisionStage: boolean;
    onSelectWinner: (quotationId: string) => Promise<void>;
    isDrawerMode?: boolean;
}

const chipStyle = (bg: string, fg: string): React.CSSProperties => ({
    backgroundColor: bg, color: fg, fontSize: '0.65rem', fontWeight: 900, padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase', whiteSpace: 'nowrap'
});

export const RequestQuotations: React.FC<RequestQuotationsProps> = ({
    quotations,
    isFinalApproverMode,
    isDecisionStage,
    onSelectWinner,
    isDrawerMode
}) => {
    const [expandedQuotations, setExpandedQuotations] = useState<Record<string, boolean>>({});
    const [processingId, setProcessingId] = useState<string | null>(null);

    // v2.249.5 — comparison per REQUEST ITEM (same currency + unit, current candidates only); a card-level "Menor valor"
    // exists only when every current quotation covers the identical scope. Lifecycle context comes from the server
    // projection (lifecycleStatus / adjudicationStatus), never inferred here.
    const comparison = useMemo(() => compareQuotationsPerLine(quotations), [quotations]);
    const scopeNotice = useMemo(() => describeScopeNotice(comparison, quotations), [comparison, quotations]);

    if (quotations.length === 0) {
        return (
            <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', backgroundColor: 'var(--color-bg-page)', border: '2px dashed var(--color-border)', borderRadius: '8px' }}>
                Nenhuma cotação salva para este pedido.
            </div>
        );
    }

    const handleSelect = async (id: string) => {
        setProcessingId(id);
        try {
            await onSelectWinner(id);
        } finally {
            setProcessingId(null);
        }
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
            {scopeNotice && (
                <div data-testid="quotation-scope-notice" style={{ fontSize: '0.72rem', fontWeight: 600, color: 'var(--color-text-muted)', padding: '8px 12px', backgroundColor: 'var(--color-bg-page)', border: '1px dashed var(--color-border)', borderRadius: '6px' }}>
                    {scopeNotice}
                </div>
            )}
            {quotations.map(q => {
                const isExpanded = expandedQuotations[q.id];
                const lifecycle = describeQuotationLifecycle(q);
                const isHistorical = !!lifecycle?.isHistorical;
                // v2.249.5 §9.2 — the historical label is display; SELECTION follows the reuse eligibility (an authorised reuse is selectable).
                const selectability = describeQuotationSelectability(q);
                const cardComparison = describeCardComparison(comparison.byQuotationId.get(q.id), q.currency);
                const isLowest = cardComparison?.kind === 'LOWEST_TOTAL' && !isHistorical;
                const toneStyle = lifecycle ? LIFECYCLE_TONE_STYLE[lifecycle.tone] : null;

                return (
                    <div key={q.id} data-testid="saved-quotation-card" data-lifecycle={q.lifecycleStatus || 'OPEN'} style={{
                        backgroundColor: q.isSelected ? '#eef2ff' : isHistorical ? '#f8fafc' : '#fff',
                        border: q.isSelected ? '2px solid #4f46e5' : isHistorical ? '1px dashed #94a3b8' : isLowest ? '2px solid #10b981' : '1px solid var(--color-border-heavy)',
                        borderRadius: '8px',
                        display: 'flex',
                        flexDirection: 'column',
                        boxShadow: q.isSelected ? '0 4px 12px rgba(79, 70, 229, 0.15)' : 'var(--shadow-sm)',
                        overflow: 'hidden',
                        transition: 'all 0.2s ease',
                        opacity: isHistorical ? 0.85 : 1
                    }}>
                        {/* Summary Row */}
                        <div
                            onClick={() => setExpandedQuotations(prev => ({ ...prev, [q.id]: !prev[q.id] }))}
                            style={{
                                padding: '16px',
                                display: 'flex',
                                justifyContent: 'space-between',
                                alignItems: 'center',
                                cursor: 'pointer',
                                backgroundColor: isExpanded ? '#f8fafc' : 'transparent'
                            }}
                        >
                            <div style={{ display: 'flex', gap: '16px', alignItems: 'center', flex: 1 }}>
                                <div style={{
                                    backgroundColor: isLowest ? '#ecfdf5' : '#f1f5f9',
                                    padding: '10px',
                                    borderRadius: '8px',
                                    color: isHistorical ? '#64748b' : isLowest ? '#059669' : 'var(--color-primary)'
                                }}>
                                    <FileText size={20} />
                                </div>
                                <div style={{ flex: 1 }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                                        <div style={{ fontWeight: 800, fontSize: '1rem', color: q.isSelected ? '#3730a3' : isHistorical ? '#475569' : 'var(--color-text-main)' }}>
                                            {q.supplierNameSnapshot}
                                        </div>
                                        {q.isSelected && (
                                            <span style={chipStyle('#4f46e5', '#fff')}>
                                                Vencedora
                                            </span>
                                        )}
                                        {lifecycle && toneStyle && !(q.isSelected && lifecycle.tone === 'awarded') && (
                                            <span data-testid="quotation-lifecycle-chip" title={lifecycle.detail} style={{ ...chipStyle(toneStyle.bg, toneStyle.fg), border: `1px solid ${toneStyle.border}` }}>
                                                {lifecycle.label}
                                            </span>
                                        )}
                                        {cardComparison && !q.isSelected && !isHistorical && (
                                            <span data-testid="quotation-comparison-chip" style={chipStyle(cardComparison.kind === 'LOWEST_TOTAL' ? '#10b981' : '#ecfdf5', cardComparison.kind === 'LOWEST_TOTAL' ? '#fff' : '#047857')}>
                                                {cardComparison.label}
                                            </span>
                                        )}
                                    </div>
                                    <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 600, display: 'flex', gap: '12px', marginTop: '2px' }}>
                                        <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}><Hash size={12} /> {q.documentNumber || 'S/N'}</span>
                                        <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}><Calendar size={12} /> {q.documentDate ? formatDate(q.documentDate) : '—'}</span>
                                    </div>
                                    {lifecycle && lifecycle.tone !== 'awarded' && (
                                        <div data-testid="quotation-lifecycle-detail" style={{ fontSize: '0.7rem', color: toneStyle?.fg ?? 'var(--color-text-muted)', fontWeight: 600, marginTop: '4px' }}>
                                            {lifecycle.detail}
                                        </div>
                                    )}
                                </div>
                            </div>

                            <div style={{ display: 'flex', alignItems: 'center', gap: '24px' }}>
                                {/* Winner Selection Action (ONLY for Final Approver in Decision stage & not drawer; follows the D3 reuse eligibility — a historical quotation is selectable once its reuse is authorised) */}
                                {isFinalApproverMode && isDecisionStage && !q.isSelected && !isDrawerMode && !selectability.selectable && (
                                    <span data-testid="quotation-selection-blocked" title={selectability.hint ?? undefined} style={{ fontSize: '0.66rem', fontWeight: 700, color: 'var(--color-text-muted)', maxWidth: '220px', textAlign: 'right' }}>
                                        Seleção sujeita a autorização de reutilização
                                    </span>
                                )}
                                {isFinalApproverMode && isDecisionStage && !q.isSelected && !isDrawerMode && selectability.selectable && (
                                    <button
                                        onClick={(e) => { e.stopPropagation(); handleSelect(q.id); }}
                                        disabled={!!processingId}
                                        title={selectability.hint ?? undefined}
                                        style={{
                                            display: 'flex', alignItems: 'center', gap: '4px', padding: '6px 12px',
                                            backgroundColor: 'var(--color-bg-surface)', border: '1px solid #4f46e5', borderRadius: '4px',
                                            color: '#4f46e5', fontWeight: 800, fontSize: '0.7rem', cursor: 'pointer',
                                            textTransform: 'uppercase', transition: 'all 0.1s'
                                        }}
                                    >
                                        <CheckCircle2 size={12} /> {processingId === q.id ? '...' : 'Selecionar'}
                                    </button>
                                )}

                                <div style={{ textAlign: 'right' }}>
                                    <div style={{ fontSize: '1.15rem', fontWeight: 900, color: isHistorical ? '#64748b' : 'var(--color-primary)', textDecoration: isHistorical ? 'line-through' : 'none' }} title={isHistorical ? 'Total de uma adjudicação cancelada — histórico, não comparável com as cotações atuais' : undefined}>
                                        <span style={{ fontSize: '0.75rem', opacity: 0.7, marginRight: '4px' }}>{q.currency}</span>
                                        {formatCurrencyAO(q.totalAmount)}
                                    </div>
                                    <div style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', fontWeight: 700 }} title="Itens cotados pelo fornecedor; itens não cotados são registos de reconciliação, não compras">{q.quotedItemCount ?? q.itemCount} ITENS COTADOS{(q.notQuotedItemCount ?? 0) > 0 ? ` · ${q.notQuotedItemCount} NÃO COTADO(S)` : ''}</div>
                                </div>
                                <div style={{ color: 'var(--color-text-muted)', transform: isExpanded ? 'rotate(180deg)' : 'rotate(0deg)', transition: 'transform 0.2s' }}>
                                    <ChevronDown size={20} />
                                </div>
                            </div>
                        </div>

                        {/* Detailed Items Table (Read-only) */}
                        {isExpanded && (
                            <div style={{ padding: '0 16px 16px 16px', borderTop: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)' }}>
                                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.75rem', marginTop: '16px', border: '1px solid var(--color-border)', borderRadius: '4px', overflow: 'hidden' }}>
                                    <thead style={{ backgroundColor: 'var(--color-bg-page)' }}>
                                        <tr>
                                            <th style={{ padding: '8px', textAlign: 'left', borderBottom: '1px solid var(--color-border)' }}>DESCRIÇÃO</th>
                                            <th style={{ padding: '8px', textAlign: 'center', borderBottom: '1px solid var(--color-border)', width: '60px' }}>UNID.</th>
                                            <th style={{ padding: '8px', textAlign: 'right', borderBottom: '1px solid var(--color-border)', width: '60px' }}>QTD</th>
                                            <th style={{ padding: '8px', textAlign: 'right', borderBottom: '1px solid var(--color-border)', width: '100px' }}>UNITÁRIO</th>
                                            <th style={{ padding: '8px', textAlign: 'right', borderBottom: '1px solid var(--color-border)', width: '120px' }}>SUBTOTAL S/ IVA</th>
                                            <th style={{ padding: '8px', textAlign: 'center', borderBottom: '1px solid var(--color-border)', width: '60px' }}>% IVA</th>
                                            <th style={{ padding: '8px', textAlign: 'right', borderBottom: '1px solid var(--color-border)', width: '100px' }}>VALOR IVA</th>
                                            <th style={{ padding: '8px', textAlign: 'right', borderBottom: '1px solid var(--color-border)', width: '130px', color: 'var(--color-primary)' }}>TOTAL C/ IVA</th>
                                            <th style={{ padding: '8px', textAlign: 'left', borderBottom: '1px solid var(--color-border)', width: '220px' }}>SITUAÇÃO</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {q.items?.map((item, idx) => {
                                            const adjudication = describeQuotationItemAdjudication(item);
                                            const itemComparison = describeItemComparison(comparison.byItemId.get(item.id));
                                            const rowHistorical = !!adjudication?.isHistorical;
                                            const rowTone = adjudication ? LIFECYCLE_TONE_STYLE[adjudication.tone] : null;
                                            return (
                                                <tr key={item.id || idx} data-testid="saved-quotation-item-row" data-adjudication={item.adjudicationStatus || ''} style={{ backgroundColor: 'var(--color-bg-surface)', borderBottom: '1px solid var(--color-border-light)', opacity: rowHistorical ? 0.75 : 1 }}>
                                                    <td style={{ padding: '8px', fontWeight: 600 }}>{item.description}</td>
                                                    <td style={{ padding: '8px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>{item.unitCode || '---'}</td>
                                                    <td style={{ padding: '8px', textAlign: 'right' }}>{item.quantity}</td>
                                                    <td style={{ padding: '8px', textAlign: 'right' }}>{formatCurrencyAO(item.unitPrice)}</td>
                                                    <td style={{ padding: '8px', textAlign: 'right' }}>{formatCurrencyAO(item.grossSubtotal || (item.unitPrice * item.quantity))}</td>
                                                    <td style={{ padding: '8px', textAlign: 'center', fontWeight: 600 }}>{item.ivaRatePercent}%</td>
                                                    <td style={{ padding: '8px', textAlign: 'right', color: 'var(--color-text-muted)' }}>{formatCurrencyAO(item.ivaAmount || 0)}</td>
                                                    <td style={{ padding: '8px', textAlign: 'right', fontWeight: 800, color: 'var(--color-primary)' }}>{formatCurrencyAO(item.lineTotal)}</td>
                                                    <td style={{ padding: '8px' }}>
                                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '3px' }}>
                                                            {adjudication && rowTone && (
                                                                <span data-testid="quotation-item-adjudication" title={adjudication.detail ?? undefined} style={{ ...chipStyle(rowTone.bg, rowTone.fg), alignSelf: 'flex-start', border: `1px solid ${rowTone.border}` }}>{adjudication.label}</span>
                                                            )}
                                                            {adjudication?.detail && (
                                                                <span style={{ fontSize: '0.68rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>{adjudication.detail}</span>
                                                            )}
                                                            {itemComparison && !rowHistorical && (
                                                                <span data-testid="quotation-item-comparison" style={{ fontSize: '0.68rem', fontWeight: 800, color: itemComparison.tone === 'lowest' ? '#047857' : itemComparison.tone === 'tie' ? '#0f766e' : 'var(--color-text-muted)' }}>
                                                                    {itemComparison.label}
                                                                </span>
                                                            )}
                                                        </div>
                                                    </td>
                                                </tr>
                                            );
                                        })}
                                    </tbody>
                                    <tfoot style={{ backgroundColor: 'var(--color-bg-page)', fontWeight: 800 }}>
                                        <tr>
                                            <td colSpan={7} style={{ padding: '8px', textAlign: 'right' }}>TOTAL DA COTAÇÃO ({q.currency}):</td>
                                            <td style={{ padding: '8px', textAlign: 'right', color: 'var(--color-primary)' }}>{formatCurrencyAO(q.totalAmount)}</td>
                                            <td />
                                        </tr>
                                    </tfoot>
                                </table>
                            </div>
                        )}
                    </div>
                );
            })}
        </div>
    );
};
