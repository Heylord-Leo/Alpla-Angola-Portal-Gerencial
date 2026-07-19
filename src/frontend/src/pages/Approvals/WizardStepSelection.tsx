import React, { useMemo } from 'react';
import { RequestDetailsDto, SavedQuotationDto, SavedQuotationItemDto } from '../../types';
import { formatCurrencyAO } from '../../lib/utils';
import { AlertCircle, CheckCircle2, CircleDashed, DollarSign, Zap, Plus, X, MessageSquare, AlertTriangle } from 'lucide-react';

import { PurchaseHistoryInsightDto } from '../../types';

export interface ExtraItemDecision {
    decision: 'APPROVE' | 'REJECT' | 'ADJUST' | null;
    comment: string;
}

interface WizardStepSelectionProps {
    request: RequestDetailsDto;
    quotations: SavedQuotationDto[];
    itemAwards: Record<string, string>;
    onChangeAward: (requestLineItemId: string, quotationItemId: string) => void;
    onSelectAll?: (quotationId: string) => void;
    onSelectLowestPrices?: () => void;
    awardedCount: number;
    totalCount: number;
    isSingleQuotation?: boolean;
    extraItemDecisions?: Record<string, ExtraItemDecision>;
    onChangeExtraItemDecision?: (quotationItemId: string, decision: ExtraItemDecision) => void;
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
                fontSize: '0.5625rem', fontWeight: 700, color: badgeColor,
                backgroundColor: bgColor, padding: '2px 6px', borderRadius: '4px', 
                textTransform: 'uppercase', cursor: 'help', display: 'inline-block',
                marginTop: '4px'
            }}>
            {badgeText}
        </span>
    );
};

export const WizardStepSelection: React.FC<WizardStepSelectionProps> = ({
    request,
    quotations,
    itemAwards,
    onChangeAward,
    onSelectAll,
    onSelectLowestPrices,
    awardedCount,
    totalCount,
    isSingleQuotation = false,
    extraItemDecisions = {},
    onChangeExtraItemDecision
}) => {
    const activeItems = request.lineItems?.filter((i: any) => !i.isDeleted) || [];
    const allAwarded = awardedCount === totalCount && totalCount > 0;
    const progressPercent = totalCount > 0 ? Math.round((awardedCount / totalCount) * 100) : 0;

    // Collect extra items
    const extraItems = useMemo(() => {
        const extras: { quotationId: string; supplierName: string; item: SavedQuotationItemDto }[] = [];
        quotations.forEach(q => {
            q.items?.forEach(qi => {
                if (qi.reconciliationStatus === 'EXTRA_ITEM') {
                    extras.push({ quotationId: q.id, supplierName: q.supplierNameSnapshot, item: qi });
                }
            });
        });
        return extras;
    }, [quotations]);

    // Option C: a cancelled-batch item without an active reuse authorization is not a candidate
    // anywhere in this step (radio, best price, select-all). Backend annotation is authoritative.
    const isSelectableCandidate = (qi: any): boolean =>
        !!qi &&
        (qi.reconciliationStatus === 'MAPPED' || qi.reconciliationStatus === 'SUBSTITUTE') &&
        !qi.isReuseBlocked;

    // Compute lowest price per item
    const lowestPrices = useMemo(() => {
        const prices: Record<string, number> = {};
        activeItems.forEach((item: any) => {
            let minPrice: number | null = null;
            quotations.forEach(q => {
                const qItem = q.items?.find(qi => qi.mappedRequestLineItemId === item.id);
                if (isSelectableCandidate(qItem) && qItem!.unitPrice > 0) {
                    if (minPrice === null || qItem!.unitPrice < minPrice) {
                        minPrice = qItem!.unitPrice;
                    }
                }
            });
            if (minPrice !== null) prices[item.id] = minPrice;
        });
        return prices;
    }, [activeItems, quotations]);

    // Compute summary groups
    const groupSummary = useMemo(() => {
        const summary: Record<string, {
            supplierName: string;
            currency: string;
            itemCount: number;
            totalAmount: number;
        }> = {};

        activeItems.forEach((item: any) => {
            const awardedQItemId = itemAwards[item.id];
            if (!awardedQItemId) return;

            const quotation = quotations.find(q => q.items?.some(qi => qi.id === awardedQItemId));
            if (!quotation) return;

            const qItem = quotation.items.find(qi => qi.id === awardedQItemId);
            if (!qItem) return;

            const groupId = `${quotation.supplierId || quotation.supplierNameSnapshot}_${quotation.currency}`;
            if (!summary[groupId]) {
                summary[groupId] = {
                    supplierName: quotation.supplierNameSnapshot,
                    currency: quotation.currency,
                    itemCount: 0,
                    totalAmount: 0
                };
            }
            summary[groupId].itemCount += 1;
            summary[groupId].totalAmount += qItem.lineTotal;
        });
        return Object.values(summary);
    }, [activeItems, quotations, itemAwards]);

    // Check unmapped quotation items
    const hasQuotationItems = quotations.some(q => q.items && q.items.length > 0);
    const anyItemMapped = quotations.some(q => q.items?.some(qi => qi.mappedRequestLineItemId));
    const hasUnmappedWarning = hasQuotationItems && !anyItemMapped;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                <div>
                    <h3 style={{ fontSize: '1.125rem', fontWeight: 700, color: '#1F2937', margin: 0 }}>
                        {isSingleQuotation ? 'Cotação Única / Revisão da Cotação' : 'Comparação e Seleção de Vencedores'}
                    </h3>
                    <p style={{ color: '#6B7280', fontSize: '0.8125rem', marginTop: '4px', margin: 0 }}>
                        {isSingleQuotation
                            ? 'Revise os valores da cotação mapeados para este pedido.'
                            : 'Compare os preços por item e selecione o fornecedor vencedor para cada linha.'}
                    </p>
                </div>
                {/* Quick select lowest prices */}
                {onSelectLowestPrices && quotations.length > 0 && !allAwarded && !isSingleQuotation && (
                    <button
                        onClick={onSelectLowestPrices}
                        style={{
                            padding: '8px 14px', borderRadius: '8px',
                            backgroundColor: '#ECFDF5', border: '1px solid #A7F3D0',
                            color: '#065F46', fontSize: '0.75rem', fontWeight: 700,
                            cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                            transition: 'all 0.15s', textTransform: 'uppercase', letterSpacing: '0.03em'
                        }}
                        onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#D1FAE5'; }}
                        onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#ECFDF5'; }}
                        title="Selecionar automaticamente o menor preço para cada item"
                    >
                        <Zap size={14} /> Menor Preço
                    </button>
                )}
            </div>

            {/* Progress */}
            <div style={{
                display: 'flex', alignItems: 'center', gap: '16px',
                padding: '12px 16px', backgroundColor: allAwarded ? '#ECFDF5' : '#FFFFFF',
                border: `1px solid ${allAwarded ? '#A7F3D0' : '#E5E7EB'}`,
                borderRadius: '8px', transition: 'all 0.3s'
            }}>
                <div style={{ flex: 1 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
                        <span style={{ fontSize: '0.8125rem', fontWeight: 600, color: allAwarded ? '#065F46' : '#374151' }}>
                            {allAwarded ? '✓ Todos os itens com vencedor selecionado' : 'Progresso da seleção'}
                        </span>
                        <span style={{ fontSize: '0.8125rem', fontWeight: 700, color: allAwarded ? '#059669' : '#374151' }}>
                            {awardedCount}/{totalCount}
                        </span>
                    </div>
                    <div style={{ width: '100%', height: '6px', backgroundColor: '#E5E7EB', borderRadius: '3px', overflow: 'hidden' }}>
                        <div style={{
                            height: '100%', borderRadius: '3px',
                            backgroundColor: allAwarded ? '#10B981' : '#2563EB',
                            width: `${progressPercent}%`,
                            transition: 'width 0.3s ease'
                        }} />
                    </div>
                </div>
            </div>

            {/* Unmapped warning */}
            {hasUnmappedWarning && (
                <div style={{
                    padding: '14px 16px', backgroundColor: '#FFFBEB',
                    border: '2px solid #F59E0B', borderRadius: '8px',
                    display: 'flex', alignItems: 'flex-start', gap: '12px',
                    color: '#92400E', fontSize: '0.8125rem', fontWeight: 600
                }}>
                    <AlertCircle size={18} style={{ flexShrink: 0, marginTop: '1px' }} />
                    <span>Os itens da cotação não estão vinculados aos itens do pedido. Devolva para que o comprador mapeie os itens.</span>
                </div>
            )}

            {/* Auto-selection Banner */}
            {isSingleQuotation && !hasUnmappedWarning && allAwarded && (
                <div style={{
                    padding: '14px 16px', backgroundColor: '#EFF6FF',
                    border: '2px solid #60A5FA', borderRadius: '8px',
                    display: 'flex', alignItems: 'flex-start', gap: '12px',
                    color: '#1E40AF', fontSize: '0.8125rem', fontWeight: 600
                }}>
                    <CheckCircle2 size={18} style={{ flexShrink: 0, marginTop: '1px', color: '#2563EB' }} />
                    <span>Este pedido possui apenas uma cotação disponível. A seleção foi preenchida automaticamente para sua revisão.</span>
                </div>
            )}

            {isSingleQuotation && !hasUnmappedWarning && !allAwarded && (
                <div style={{
                    padding: '14px 16px', backgroundColor: '#FFFBEB',
                    border: '2px solid #F59E0B', borderRadius: '8px',
                    display: 'flex', alignItems: 'flex-start', gap: '12px',
                    color: '#92400E', fontSize: '0.8125rem', fontWeight: 600
                }}>
                    <AlertCircle size={18} style={{ flexShrink: 0, marginTop: '1px' }} />
                    <span>Não foi possível associar automaticamente todos os itens da cotação aos itens do pedido. Revise o mapeamento antes de continuar.</span>
                </div>
            )}

            {/* Item Award Matrix */}
            <div style={{
                backgroundColor: '#FFFFFF', border: '1px solid #E5E7EB',
                borderRadius: '8px', overflow: 'hidden'
            }}>
                <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
                        <thead>
                            <tr>
                                <th style={{
                                    padding: '14px 16px', borderBottom: '2px solid #E5E7EB',
                                    backgroundColor: '#F9FAFB', position: 'sticky', left: 0, zIndex: 10,
                                    minWidth: '220px'
                                }}>
                                    <span style={{ fontSize: '0.6875rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6B7280' }}>
                                        Item Requerido
                                    </span>
                                </th>
                                {quotations.map(q => {
                                    // "Select all" only when every item has a SELECTABLE candidate in this
                                    // quotation — reuse-blocked items (Option C) disable bulk selection.
                                    const coversAll = activeItems.every((item: any) =>
                                        isSelectableCandidate(q.items?.find(qi => qi.mappedRequestLineItemId === item.id)));
                                    return (
                                        <th key={q.id} style={{
                                            padding: '14px 16px', borderBottom: '2px solid #E5E7EB',
                                            borderLeft: '1px solid #E5E7EB', backgroundColor: '#F9FAFB',
                                            minWidth: '200px', verticalAlign: 'top'
                                        }}>
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                <span style={{ fontSize: '0.8125rem', fontWeight: 800, color: '#111827' }}>{q.supplierNameSnapshot}</span>
                                                <span style={{ fontSize: '0.625rem', fontWeight: 600, color: '#6B7280', textTransform: 'uppercase' }}>
                                                    Cot. #{q.documentNumber || 'S/N'} • {q.currency}
                                                </span>
                                                {coversAll && onSelectAll && !isSingleQuotation && (
                                                    <button
                                                        onClick={() => onSelectAll(q.id)}
                                                        style={{
                                                            marginTop: '6px', padding: '4px 8px',
                                                            backgroundColor: 'rgba(22, 163, 74, 0.08)',
                                                            color: '#059669', border: '1px solid rgba(22, 163, 74, 0.25)',
                                                            borderRadius: '4px', fontSize: '0.625rem', fontWeight: 700,
                                                            cursor: 'pointer', textTransform: 'uppercase',
                                                            transition: 'all 0.15s'
                                                        }}
                                                    >
                                                        Selecionar Todos
                                                    </button>
                                                )}
                                            </div>
                                        </th>
                                    );
                                })}
                                <th style={{
                                    padding: '14px 16px', borderBottom: '2px solid #E5E7EB',
                                    borderLeft: '1px solid #E5E7EB', backgroundColor: '#F9FAFB',
                                    minWidth: '100px'
                                }}>
                                    <span style={{ fontSize: '0.6875rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em', color: '#6B7280' }}>
                                        Estado
                                    </span>
                                </th>
                            </tr>
                        </thead>
                        <tbody>
                            {activeItems.map((item: any, index: number) => {
                                const isItemAwarded = !!itemAwards[item.id];

                                return (
                                    <tr key={item.id} style={{ borderBottom: index === activeItems.length - 1 ? 'none' : '1px solid #E5E7EB' }}>
                                        {/* Item description column (sticky) */}
                                        <td style={{
                                            padding: '14px 16px', position: 'sticky', left: 0,
                                            backgroundColor: '#FFFFFF', zIndex: 5,
                                            borderRight: '1px solid #E5E7EB'
                                        }}>
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '3px' }}>
                                                <span style={{ fontSize: '0.625rem', fontWeight: 700, color: '#6B7280' }}>#{item.lineNumber}</span>
                                                <span style={{ fontSize: '0.8125rem', fontWeight: 700, color: '#111827' }}>{item.description}</span>
                                                <span style={{ fontSize: '0.75rem', fontWeight: 500, color: '#6B7280' }}>{item.quantity} {item.unit || 'UN'}</span>
                                            </div>
                                        </td>

                                        {/* Quotation columns */}
                                        {quotations.map(q => {
                                            const qItem = q.items?.find(qi => qi.mappedRequestLineItemId === item.id);
                                            const isBestPrice = qItem && qItem.unitPrice === lowestPrices[item.id];
                                            const isNotQuoted = qItem?.reconciliationStatus === 'NOT_QUOTED';
                                            const isIgnored = qItem?.reconciliationStatus === 'IGNORED';
                                            // Option C: cancelled-batch item without authorization → no radio, not selectable
                                            const isReuseBlocked = !!qItem?.isReuseBlocked;
                                            const isValid = qItem && !isNotQuoted && !isIgnored && !isReuseBlocked;
                                            const isSelected = itemAwards[item.id] === qItem?.id || (isSingleQuotation && !!qItem && !isReuseBlocked);

                                            return (
                                                <td key={q.id} style={{
                                                    padding: '12px 16px', borderLeft: '1px solid #E5E7EB',
                                                    backgroundColor: isSelected ? 'rgba(22, 163, 74, 0.04)' : 'transparent',
                                                    verticalAlign: 'top', transition: 'background-color 0.2s'
                                                }}>
                                                    {isValid ? (
                                                        <div
                                                            onClick={() => !isSingleQuotation && onChangeAward(item.id, qItem.id)}
                                                            style={{
                                                                display: 'flex', flexDirection: 'column', gap: '6px',
                                                                cursor: isSingleQuotation ? 'default' : 'pointer', padding: '8px', borderRadius: '8px',
                                                                border: isSelected && !isSingleQuotation ? '2px solid #10B981' : isSelected && isSingleQuotation ? '2px solid transparent' : '2px solid transparent',
                                                                backgroundColor: isBestPrice && !isSelected && !isSingleQuotation ? 'rgba(22, 163, 74, 0.03)' : 'transparent',
                                                                transition: 'all 0.15s'
                                                            }}
                                                        >
                                                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                                {!isSingleQuotation && (
                                                                    isSelected ? (
                                                                        <div style={{ width: '18px', height: '18px', borderRadius: '50%', backgroundColor: '#10B981', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
                                                                            <div style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: 'white' }} />
                                                                        </div>
                                                                    ) : (
                                                                        <div style={{ width: '18px', height: '18px', borderRadius: '50%', border: '2px solid #D1D5DB', flexShrink: 0 }} />
                                                                    )
                                                                )}
                                                                <span style={{ fontSize: '0.875rem', fontWeight: 700, color: isSelected && !isSingleQuotation ? '#059669' : '#111827' }}>
                                                                    {formatCurrencyAO(qItem.unitPrice)}
                                                                </span>
                                                                <span style={{ fontSize: '0.625rem', fontWeight: 600, color: '#6B7280', textTransform: 'uppercase' }}>
                                                                    / {qItem.unitName || qItem.unitCode || 'UN'}
                                                                </span>
                                                            </div>
                                                            <div style={{ paddingLeft: isSingleQuotation ? '0' : '26px' }}>
                                                                <span style={{ fontSize: '0.75rem', fontWeight: 500, color: '#6B7280' }}>
                                                                    Total: {formatCurrencyAO(qItem.lineTotal)}
                                                                </span>
                                                                <div style={{ marginTop: '2px' }}>
                                                                    {renderHistoryBadge(qItem.historyInsight)}
                                                                </div>
                                                            </div>
                                                            {isBestPrice && !isSelected && !isSingleQuotation && (
                                                                <div style={{ paddingLeft: '26px', marginTop: '2px' }}>
                                                                    <span style={{
                                                                        fontSize: '0.5625rem', fontWeight: 700, color: '#059669',
                                                                        backgroundColor: 'rgba(22, 163, 74, 0.1)',
                                                                        padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase'
                                                                    }}>
                                                                        Melhor Preço
                                                                    </span>
                                                                </div>
                                                            )}
                                                            {(qItem.isReuseAuthorized || qItem.reuseConsumedFromBatchId) && qItem.sourceCancelledBatchNumber != null && (
                                                                <div style={{ paddingLeft: isSingleQuotation ? '0' : '26px', marginTop: '2px' }}>
                                                                    <span style={{
                                                                        fontSize: '0.5625rem', fontWeight: 700, color: '#92400E',
                                                                        backgroundColor: '#FEF3C7',
                                                                        padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase'
                                                                    }}>
                                                                        Reutilizado do Lote #{qItem.sourceCancelledBatchNumber} (cancelado)
                                                                    </span>
                                                                </div>
                                                            )}
                                                        </div>
                                                    ) : (
                                                        <div style={{
                                                            display: 'flex', alignItems: 'center', justifyContent: 'center', textAlign: 'center',
                                                            height: '100%', color: isReuseBlocked ? '#B45309' : '#9CA3AF', fontSize: '0.7rem',
                                                            fontWeight: isReuseBlocked ? 700 : 500, fontStyle: 'italic', padding: '16px 6px',
                                                            backgroundColor: isReuseBlocked ? '#FFFBEB' : (isNotQuoted ? '#F9FAFB' : 'transparent'), borderRadius: '8px'
                                                        }}>
                                                            {isReuseBlocked
                                                                ? `Lote #${qItem?.sourceCancelledBatchNumber ?? '?'} cancelado — reuso não autorizado`
                                                                : isNotQuoted ? '— não cotado —' : (isIgnored ? '' : '—')}
                                                        </div>
                                                    )}
                                                </td>
                                            );
                                        })}

                                        {/* Status column */}
                                        <td style={{
                                            padding: '14px 16px', borderLeft: '1px solid #E5E7EB',
                                            verticalAlign: 'middle', textAlign: 'center'
                                        }}>
                                            {isItemAwarded ? (
                                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '4px', color: '#059669' }}>
                                                    <CheckCircle2 size={16} />
                                                    <span style={{ fontSize: '0.6875rem', fontWeight: 700 }}>Atribuído</span>
                                                </div>
                                            ) : (
                                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '4px', color: '#EF4444' }}>
                                                    <CircleDashed size={16} />
                                                    <span style={{ fontSize: '0.6875rem', fontWeight: 700 }}>Pendente</span>
                                                </div>
                                            )}
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            </div>

            {/* Live supplier group summary */}
            {groupSummary.length > 0 && (
                <div style={{
                    backgroundColor: '#F9FAFB', border: '1px solid #E5E7EB',
                    borderRadius: '8px', padding: '16px'
                }}>
                    <h4 style={{ fontSize: '0.8125rem', fontWeight: 700, color: '#374151', marginBottom: '12px', display: 'flex', alignItems: 'center', gap: '8px', margin: '0 0 12px 0' }}>
                        <DollarSign size={14} /> Resumo de Seleção
                    </h4>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {groupSummary.map((group, idx) => (
                            <div key={idx} style={{
                                display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                                padding: '10px 14px', backgroundColor: '#FFFFFF',
                                border: '1px solid #E5E7EB', borderRadius: '6px'
                            }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                    <div style={{
                                        width: '28px', height: '28px', borderRadius: '50%',
                                        backgroundColor: '#EFF6FF', display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        fontWeight: 700, color: '#2563EB', fontSize: '0.75rem'
                                    }}>
                                        {idx + 1}
                                    </div>
                                    <div>
                                        <span style={{ fontSize: '0.8125rem', fontWeight: 700, color: '#111827' }}>{group.supplierName}</span>
                                        <span style={{ fontSize: '0.6875rem', color: '#6B7280', display: 'block' }}>
                                            {group.itemCount} {group.itemCount === 1 ? 'item' : 'itens'}
                                        </span>
                                    </div>
                                </div>
                                <div style={{ fontSize: '0.875rem', fontWeight: 700, color: '#111827' }}>
                                    {formatCurrencyAO(group.totalAmount)} <span style={{ fontSize: '0.6875rem', color: '#6B7280' }}>{group.currency}</span>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {/* Extra Items Section */}
            {extraItems.length > 0 && (
                <div style={{
                    backgroundColor: '#FFFFFF', border: '1px solid #E5E7EB',
                    borderRadius: '8px', overflow: 'hidden', marginTop: '10px'
                }}>
                    <div style={{ padding: '16px', backgroundColor: '#F9FAFB', borderBottom: '1px solid #E5E7EB' }}>
                        <h4 style={{ fontSize: '1rem', fontWeight: 700, color: '#111827', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <Plus size={18} color="#4F46E5" /> Itens Adicionais das Cotações
                        </h4>
                        <p style={{ fontSize: '0.8125rem', color: '#6B7280', margin: '4px 0 0 0' }}>
                            Avalie os itens extra adicionados pelos fornecedores e decida se devem ser incluídos na aprovação final.
                        </p>
                    </div>
                    
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '0' }}>
                        {extraItems.map(({ supplierName, item }, idx) => {
                            const decision = extraItemDecisions[item.id];
                            const currentDecision = decision?.decision;
                            
                            return (
                                <div key={item.id} style={{ 
                                    padding: '16px', 
                                    borderBottom: idx < extraItems.length - 1 ? '1px solid #E5E7EB' : 'none',
                                    display: 'flex', flexDirection: 'column', gap: '16px',
                                    backgroundColor: currentDecision === 'APPROVE' ? 'rgba(16, 185, 129, 0.03)' : currentDecision === 'REJECT' ? 'rgba(239, 68, 68, 0.03)' : 'transparent'
                                }}>
                                    {/* Item Details */}
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                        <div>
                                            <span style={{ fontSize: '0.75rem', fontWeight: 700, color: '#4F46E5', textTransform: 'uppercase' }}>
                                                {supplierName}
                                            </span>
                                            <h5 style={{ fontSize: '0.9375rem', fontWeight: 700, color: '#111827', margin: '4px 0' }}>
                                                {item.description}
                                            </h5>
                                            <div style={{ fontSize: '0.8125rem', color: '#6B7280', display: 'flex', gap: '12px' }}>
                                                <span>Qtd: <b>{item.quantity} {item.unitName || item.unitCode || 'UN'}</b></span>
                                                <span>P. Unit: <b>{formatCurrencyAO(item.unitPrice)}</b></span>
                                                <span style={{ color: '#111827', fontWeight: 700 }}>Total: {formatCurrencyAO(item.lineTotal)}</span>
                                            </div>
                                        </div>
                                        
                                        {/* Decision Buttons */}
                                        <div style={{ display: 'flex', gap: '8px' }}>
                                            <button
                                                onClick={() => onChangeExtraItemDecision?.(item.id, { decision: 'APPROVE', comment: decision?.comment || item.buyerJustification || '' })}
                                                style={{
                                                    padding: '6px 12px', borderRadius: '6px',
                                                    border: currentDecision === 'APPROVE' ? '2px solid #10B981' : '1px solid #D1D5DB',
                                                    backgroundColor: currentDecision === 'APPROVE' ? '#10B981' : '#FFFFFF',
                                                    color: currentDecision === 'APPROVE' ? '#FFFFFF' : '#374151',
                                                    fontSize: '0.75rem', fontWeight: 600, cursor: 'pointer',
                                                    display: 'flex', alignItems: 'center', gap: '4px',
                                                    transition: 'all 0.15s'
                                                }}
                                            >
                                                <CheckCircle2 size={14} /> Aprovar
                                            </button>
                                            
                                            <button
                                                onClick={() => onChangeExtraItemDecision?.(item.id, { decision: 'REJECT', comment: decision?.comment || item.buyerJustification || '' })}
                                                style={{
                                                    padding: '6px 12px', borderRadius: '6px',
                                                    border: currentDecision === 'REJECT' ? '2px solid #EF4444' : '1px solid #D1D5DB',
                                                    backgroundColor: currentDecision === 'REJECT' ? '#EF4444' : '#FFFFFF',
                                                    color: currentDecision === 'REJECT' ? '#FFFFFF' : '#374151',
                                                    fontSize: '0.75rem', fontWeight: 600, cursor: 'pointer',
                                                    display: 'flex', alignItems: 'center', gap: '4px',
                                                    transition: 'all 0.15s'
                                                }}
                                            >
                                                <X size={14} /> Rejeitar
                                            </button>
                                            
                                            <button
                                                onClick={() => onChangeExtraItemDecision?.(item.id, { decision: 'ADJUST', comment: decision?.comment || item.buyerJustification || '' })}
                                                style={{
                                                    padding: '6px 12px', borderRadius: '6px',
                                                    border: currentDecision === 'ADJUST' ? '2px solid #F59E0B' : '1px solid #D1D5DB',
                                                    backgroundColor: currentDecision === 'ADJUST' ? '#F59E0B' : '#FFFFFF',
                                                    color: currentDecision === 'ADJUST' ? '#FFFFFF' : '#374151',
                                                    fontSize: '0.75rem', fontWeight: 600, cursor: 'pointer',
                                                    display: 'flex', alignItems: 'center', gap: '4px',
                                                    transition: 'all 0.15s'
                                                }}
                                            >
                                                <AlertTriangle size={14} /> Ajustar
                                            </button>
                                        </div>
                                    </div>
                                    
                                    {/* Justifications */}
                                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                                        {/* Buyer Justification */}
                                        <div style={{ backgroundColor: '#F3F4F6', padding: '12px', borderRadius: '6px', border: '1px solid #E5E7EB' }}>
                                            <span style={{ fontSize: '0.6875rem', fontWeight: 700, color: '#6B7280', textTransform: 'uppercase', display: 'flex', alignItems: 'center', gap: '4px', marginBottom: '6px' }}>
                                                <MessageSquare size={12} /> Justificação do Comprador
                                            </span>
                                            <p style={{ margin: 0, fontSize: '0.8125rem', color: '#374151', whiteSpace: 'pre-wrap' }}>
                                                {item.buyerJustification || <span style={{ fontStyle: 'italic', color: '#9CA3AF' }}>Sem justificação fornecida.</span>}
                                            </p>
                                        </div>
                                        
                                        {/* Approver Comment */}
                                        {currentDecision && (
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                                <label style={{ fontSize: '0.6875rem', fontWeight: 700, color: currentDecision === 'REJECT' ? '#B91C1C' : '#374151', textTransform: 'uppercase', display: 'flex', justifyContent: 'space-between' }}>
                                                    <span>Comentário do Aprovador {currentDecision === 'REJECT' && '*'}</span>
                                                </label>
                                                <textarea
                                                    value={decision?.comment || ''}
                                                    onChange={(e) => onChangeExtraItemDecision?.(item.id, { ...decision, comment: e.target.value })}
                                                    placeholder="Adicione um comentário à sua decisão..."
                                                    style={{
                                                        width: '100%', minHeight: '60px', padding: '8px 12px',
                                                        fontSize: '0.8125rem', borderRadius: '6px',
                                                        border: currentDecision === 'REJECT' && !decision?.comment?.trim() ? '1px solid #FCA5A5' : '1px solid #D1D5DB',
                                                        backgroundColor: '#FFFFFF', resize: 'vertical'
                                                    }}
                                                />
                                                {currentDecision === 'REJECT' && !decision?.comment?.trim() && (
                                                    <span style={{ fontSize: '0.6875rem', color: '#DC2626', fontWeight: 500 }}>
                                                        A justificação é obrigatória ao rejeitar.
                                                    </span>
                                                )}
                                            </div>
                                        )}
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                </div>
            )}

            {/* Validation warning */}
            {!allAwarded && (
                <div style={{
                    display: 'flex', alignItems: 'flex-start', gap: '12px',
                    padding: '14px 16px', backgroundColor: '#FEF2F2',
                    border: '1px solid #FECACA', borderRadius: '8px'
                }}>
                    <AlertCircle color="#DC2626" size={18} style={{ flexShrink: 0, marginTop: '1px' }} />
                    <div>
                        <span style={{ color: '#991B1B', fontWeight: 600, fontSize: '0.8125rem' }}>Seleção Incompleta</span>
                        <span style={{ color: '#B91C1C', fontSize: '0.8125rem', display: 'block', marginTop: '2px' }}>
                            {totalCount - awardedCount} {totalCount - awardedCount === 1 ? 'item necessita' : 'itens necessitam'} de um fornecedor vencedor para prosseguir.
                        </span>
                    </div>
                </div>
            )}
        </div>
    );
};
