import React, { useState } from 'react';
import { RequestDetailsDto, SavedQuotationDto, SavedQuotationItemDto, PurchaseHistoryInsightDto } from '../../types';
import { formatCurrencyAO } from '../../lib/utils';
import {
    CheckCircle2,
    FileText,
    AlertTriangle,
    ChevronDown,
    ChevronUp,
    Scale,
    Building2,
    Check,
    Eye
} from 'lucide-react';

interface WizardStepBatchReviewProps {
    request: RequestDetailsDto;
    quotations: SavedQuotationDto[];
    activeBatch?: any | null;
    onDownloadAttachment?: (attachmentId: string, fileName: string) => void;
}

const renderHistoryBadge = (insight?: PurchaseHistoryInsightDto | null) => {
    if (!insight || !insight.hasHistory || insight.status === 'NO_HISTORY') return null;

    let badgeText = '';
    let badgeColor = '#6B7280';
    let bgColor = 'rgba(107, 114, 128, 0.1)';
    let tooltip = '';

    if (insight.status === 'DIFFERENT_CURRENCY') {
        badgeText = 'Histórico';
        tooltip = 'Última compra encontrada em outra moeda. Comparação de preço não aplicada.';
    } else if (insight.status === 'DIFFERENT_UOM') {
        badgeText = 'Histórico';
        tooltip = 'Última compra encontrada com unidade de medida diferente. Comparação direta não aplicada.';
    } else {
        const diff = insight.differencePercent;
        if (insight.status === 'LOWER_THAN_LAST') {
            badgeText = `↓ ${diff}%`;
            badgeColor = '#059669';
            bgColor = 'rgba(22, 163, 74, 0.1)';
        } else if (insight.status === 'HIGHER_THAN_LAST') {
            badgeText = `↑ ${diff}%`;
            badgeColor = '#DC2626';
            bgColor = 'rgba(220, 38, 38, 0.1)';
        } else {
            badgeText = `= mesmo preço`;
        }

        const dateStr = insight.lastPurchaseDateUtc ? new Date(insight.lastPurchaseDateUtc).toLocaleDateString('pt-AO') : 'N/D';
        tooltip = `Última compra com este fornecedor: ${dateStr}\n` +
                  `Preço anterior: ${insight.lastCurrency} ${insight.lastUnitPrice} / ${insight.lastUom}\n` +
                  `Preço atual: ${insight.lastCurrency} ${insight.currentUnitPrice} / ${insight.lastUom}\n` +
                  `Variação: ${diff}%`;
    }

    return (
        <span
            title={tooltip}
            style={{
                fontSize: '0.625rem', fontWeight: 700, color: badgeColor,
                backgroundColor: bgColor, padding: '2px 8px', borderRadius: '4px',
                textTransform: 'uppercase', cursor: 'help', display: 'inline-block'
            }}>
            {badgeText}
        </span>
    );
};

export const WizardStepBatchReview: React.FC<WizardStepBatchReviewProps> = ({
    request,
    quotations,
    activeBatch,
    onDownloadAttachment
}) => {
    const [expandedOtherQuotes, setExpandedOtherQuotes] = useState<Record<string, boolean>>({});

    const activeItems = (request.lineItems || []).filter((i: any) => !i.isDeleted);
    const batchNumber = activeBatch?.batchNumber ?? 1;

    const toggleExpandOtherQuotes = (itemId: string) => {
        setExpandedOtherQuotes(prev => ({ ...prev, [itemId]: !prev[itemId] }));
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
            {/* Header Banner */}
            <div style={{
                backgroundColor: '#FFFFFF', border: '1px solid #E5E7EB',
                borderRadius: '12px', padding: '20px', boxShadow: '0 1px 3px rgba(0,0,0,0.05)'
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '2px' }}>
                    <div style={{
                        width: '36px', height: '36px', borderRadius: '8px',
                        backgroundColor: '#ECFDF5', border: '1px solid #A7F3D0',
                        display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#059669',
                        flexShrink: 0
                    }}>
                        <Scale size={20} />
                    </div>
                    <div>
                        <h3 style={{ fontSize: '1rem', fontWeight: 800, color: '#111827', margin: 0 }}>
                            Revisão da Cotação Selecionada pelo Comprador
                        </h3>
                        <p style={{ fontSize: '0.8125rem', color: '#6B7280', margin: '2px 0 0 0' }}>
                            Confirme os fornecedores e valores selecionados pelo comprador para os itens do Lote #{batchNumber}.
                        </p>
                    </div>
                </div>
            </div>

            {/* List of Batch Items */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {activeItems.map((item: any) => {
                    const selectedQItemId = item.selectedQuotationItemId;

                    let selectedQuotation: SavedQuotationDto | null = null;
                    let selectedQItem: SavedQuotationItemDto | null = null;

                    if (selectedQItemId) {
                        for (const q of quotations) {
                            const found = q.items?.find(qi => qi.id === selectedQItemId);
                            if (found) {
                                selectedQuotation = q;
                                selectedQItem = found;
                                break;
                            }
                        }
                    }

                    // Other non-selected quotations for read-only inspection
                    const otherQuotations = quotations.filter(q => q.id !== selectedQuotation?.id);
                    const isOtherExpanded = !!expandedOtherQuotes[item.id];

                    const isChainValid = !!selectedQItemId && !!selectedQItem && !!selectedQuotation;

                    return (
                        <div
                            key={item.id}
                            style={{
                                backgroundColor: '#FFFFFF',
                                border: isChainValid ? '1px solid #D1D5DB' : '1px solid #FCA5A5',
                                borderRadius: '12px',
                                overflow: 'hidden',
                                boxShadow: '0 1px 3px rgba(0,0,0,0.04)'
                            }}
                        >
                            {/* Requested Item Top Header Bar */}
                            <div style={{
                                padding: '14px 20px',
                                backgroundColor: '#F9FAFB',
                                borderBottom: '1px solid #E5E7EB',
                                display: 'flex',
                                justifyContent: 'space-between',
                                alignItems: 'center',
                                flexWrap: 'wrap',
                                gap: '12px'
                            }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                    <span style={{
                                        fontSize: '0.75rem', fontWeight: 800, color: '#4B5563',
                                        backgroundColor: '#E5E7EB', padding: '2px 8px', borderRadius: '6px'
                                    }}>
                                        Item #{item.lineNumber}
                                    </span>
                                    <span style={{ fontSize: '0.9375rem', fontWeight: 700, color: '#111827' }}>
                                        {item.description}
                                    </span>
                                </div>
                                <div style={{ fontSize: '0.8125rem', fontWeight: 600, color: '#4B5563' }}>
                                    Quantidade: <strong style={{ color: '#111827' }}>{item.quantity} {item.unit || 'UN'}</strong>
                                </div>
                            </div>

                            {/* Card Main Body */}
                            <div style={{ padding: '20px' }}>
                                {isChainValid ? (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                        {/* Winner Box */}
                                        <div style={{
                                            backgroundColor: 'rgba(16, 185, 129, 0.04)',
                                            border: '1.5px solid #10B981',
                                            borderRadius: '10px',
                                            padding: '16px 20px'
                                        }}>
                                            <div style={{
                                                display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start',
                                                flexWrap: 'wrap', gap: '12px', marginBottom: '14px'
                                            }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                                    <Building2 size={18} color="#059669" />
                                                    <div>
                                                        <span style={{ fontSize: '0.9375rem', fontWeight: 800, color: '#065F46' }}>
                                                            {selectedQuotation!.supplierNameSnapshot}
                                                        </span>
                                                        <span style={{ fontSize: '0.75rem', color: '#047857', display: 'block', marginTop: '2px' }}>
                                                            Cotação #{selectedQuotation!.documentNumber || 'S/N'} • {selectedQuotation!.currency}
                                                        </span>
                                                    </div>
                                                </div>

                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                                                    <span style={{
                                                        display: 'inline-flex', alignItems: 'center', gap: '4px',
                                                        fontSize: '0.6875rem', fontWeight: 700, color: '#047857',
                                                        backgroundColor: '#D1FAE5', border: '1px solid #A7F3D0',
                                                        padding: '4px 10px', borderRadius: '20px', textTransform: 'uppercase'
                                                    }}>
                                                        <Check size={13} strokeWidth={3} /> Selecionado pelo Comprador
                                                    </span>

                                                    {renderHistoryBadge(selectedQItem!.historyInsight)}

                                                    {(selectedQItem!.isReuseAuthorized || selectedQItem!.reuseConsumedFromBatchId) && selectedQItem!.sourceCancelledBatchNumber != null && (
                                                        <span style={{
                                                            fontSize: '0.625rem', fontWeight: 700, color: '#92400E',
                                                            backgroundColor: '#FEF3C7', border: '1px solid #FCD34D',
                                                            padding: '4px 8px', borderRadius: '4px', textTransform: 'uppercase'
                                                        }}>
                                                            Reutilizado do Lote #{selectedQItem!.sourceCancelledBatchNumber} (cancelado)
                                                        </span>
                                                    )}
                                                </div>
                                            </div>

                                            {/* Financial Figures */}
                                            <div style={{
                                                display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))',
                                                gap: '12px', paddingTop: '12px', borderTop: '1px solid #A7F3D0'
                                            }}>
                                                <div>
                                                    <span style={{ fontSize: '0.6875rem', fontWeight: 600, color: '#047857', textTransform: 'uppercase' }}>
                                                        Preço Unitário
                                                    </span>
                                                    <div style={{ fontSize: '0.9375rem', fontWeight: 800, color: '#065F46' }}>
                                                        {formatCurrencyAO(selectedQItem!.unitPrice)} <span style={{ fontSize: '0.75rem', fontWeight: 500 }}>/ {selectedQItem!.unitName || selectedQItem!.unitCode || 'UN'}</span>
                                                    </div>
                                                </div>

                                                <div>
                                                    <span style={{ fontSize: '0.6875rem', fontWeight: 600, color: '#047857', textTransform: 'uppercase' }}>
                                                        Total do Item
                                                    </span>
                                                    <div style={{ fontSize: '1rem', fontWeight: 800, color: '#065F46' }}>
                                                        {formatCurrencyAO(selectedQItem!.lineTotal)}
                                                    </div>
                                                </div>

                                                {selectedQItem!.discountAmount != null && selectedQItem!.discountAmount > 0 && (
                                                    <div>
                                                        <span style={{ fontSize: '0.6875rem', fontWeight: 600, color: '#047857', textTransform: 'uppercase' }}>
                                                            Desconto
                                                        </span>
                                                        <div style={{ fontSize: '0.875rem', fontWeight: 700, color: '#065F46' }}>
                                                            {formatCurrencyAO(selectedQItem!.discountAmount)} ({selectedQItem!.discountValue || 0}%)
                                                        </div>
                                                    </div>
                                                )}

                                                {selectedQItem!.ivaAmount != null && selectedQItem!.ivaAmount > 0 && (
                                                    <div>
                                                        <span style={{ fontSize: '0.6875rem', fontWeight: 600, color: '#047857', textTransform: 'uppercase' }}>
                                                            IVA ({selectedQItem!.ivaRatePercent}%)
                                                        </span>
                                                        <div style={{ fontSize: '0.875rem', fontWeight: 700, color: '#065F46' }}>
                                                            {formatCurrencyAO(selectedQItem!.ivaAmount)}
                                                        </div>
                                                    </div>
                                                )}
                                            </div>
                                        </div>

                                        {/* Action buttons & Collapsible other quotes */}
                                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '10px' }}>
                                            <div>
                                                {selectedQuotation!.proformaAttachmentId && onDownloadAttachment && (
                                                    <button
                                                        type="button"
                                                        onClick={() => onDownloadAttachment(selectedQuotation!.proformaAttachmentId!, selectedQuotation!.sourceFileName || 'Proforma.pdf')}
                                                        style={{
                                                            display: 'inline-flex', alignItems: 'center', gap: '6px',
                                                            padding: '6px 12px', backgroundColor: '#F3F4F6', color: '#374151',
                                                            border: '1px solid #D1D5DB', borderRadius: '6px', fontSize: '0.75rem',
                                                            fontWeight: 600, cursor: 'pointer', transition: 'all 0.15s'
                                                        }}
                                                    >
                                                        <FileText size={14} /> Visualizar Proforma ({selectedQuotation!.sourceFileName || 'PDF'})
                                                    </button>
                                                )}
                                            </div>

                                            {otherQuotations.length > 0 && (
                                                <button
                                                    type="button"
                                                    onClick={() => toggleExpandOtherQuotes(item.id)}
                                                    style={{
                                                        display: 'inline-flex', alignItems: 'center', gap: '6px',
                                                        padding: '6px 12px', backgroundColor: 'transparent', color: '#2563EB',
                                                        border: 'none', borderRadius: '6px', fontSize: '0.75rem',
                                                        fontWeight: 600, cursor: 'pointer'
                                                    }}
                                                >
                                                    <Eye size={14} /> {isOtherExpanded ? 'Ocultar outras cotações' : `Consultar outras cotações (${otherQuotations.length})`}
                                                    {isOtherExpanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
                                                </button>
                                            )}
                                        </div>

                                        {/* Collapsible section for other non-winning quotes */}
                                        {isOtherExpanded && otherQuotations.length > 0 && (
                                            <div style={{
                                                backgroundColor: '#F9FAFB', border: '1px solid #E5E7EB',
                                                borderRadius: '8px', padding: '14px', marginTop: '4px'
                                            }}>
                                                <span style={{ fontSize: '0.75rem', fontWeight: 700, color: '#4B5563', display: 'block', marginBottom: '8px' }}>
                                                    Outras Cotações Recebidas (Apenas Consulta):
                                                </span>
                                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                                    {otherQuotations.map(oq => {
                                                        const oqItem = oq.items?.find(qi => qi.mappedRequestLineItemId === item.id || qi.description.trim().toLowerCase() === item.description.trim().toLowerCase());
                                                        return (
                                                            <div key={oq.id} style={{
                                                                display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                                                                padding: '8px 12px', backgroundColor: '#FFFFFF', border: '1px solid #E5E7EB',
                                                                borderRadius: '6px', fontSize: '0.75rem'
                                                            }}>
                                                                <div>
                                                                    <strong style={{ color: '#111827' }}>{oq.supplierNameSnapshot}</strong>
                                                                    <span style={{ color: '#6B7280', marginLeft: '6px' }}>#{oq.documentNumber || 'S/N'}</span>
                                                                </div>
                                                                <div>
                                                                    {oqItem ? (
                                                                        <span style={{ fontWeight: 700, color: '#374151' }}>
                                                                            {formatCurrencyAO(oqItem.lineTotal)} ({formatCurrencyAO(oqItem.unitPrice)} / {oqItem.unitName || 'UN'})
                                                                        </span>
                                                                    ) : (
                                                                        <span style={{ color: '#9CA3AF', fontStyle: 'italic' }}>Não Cotado</span>
                                                                    )}
                                                                </div>
                                                            </div>
                                                        );
                                                    })}
                                                </div>
                                            </div>
                                        )}
                                    </div>
                                ) : (
                                    /* Error state card if selection chain is invalid */
                                    <div style={{
                                        backgroundColor: '#FEF2F2',
                                        border: '1px solid #FCA5A5',
                                        borderRadius: '10px',
                                        padding: '16px 20px',
                                        color: '#991B1B'
                                    }}>
                                        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px' }}>
                                            <AlertTriangle size={20} color="#DC2626" style={{ flexShrink: 0, marginTop: '2px' }} />
                                            <div>
                                                <h4 style={{ fontSize: '0.875rem', fontWeight: 800, margin: '0 0 4px 0' }}>
                                                    Cotação Vencedora Inválida ou Ausente
                                                </h4>
                                                <p style={{ fontSize: '0.8125rem', margin: 0, lineHeight: 1.4 }}>
                                                    Este lote (Lote #{batchNumber}) não possui uma cotação vencedora válida para o item <strong>#{item.lineNumber} - {item.description}</strong>. Solicite reajuste para que o comprador corrija a seleção.
                                                </p>
                                                <span style={{ fontSize: '0.75rem', fontWeight: 600, color: '#7F1D1D', display: 'block', marginTop: '6px' }}>
                                                    Motivo: {!quotations || quotations.length === 0 ? 'Dados de cotação indisponíveis no pedido.' : selectedQItemId ? `Item de cotação ${selectedQItemId} não encontrado nas cotações do pedido.` : 'SelectedQuotationItemId não informado no lote.'}
                                                </span>
                                            </div>
                                        </div>
                                    </div>
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>
        </div>
    );
};
