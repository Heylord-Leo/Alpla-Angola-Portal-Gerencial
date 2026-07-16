import { useMemo } from 'react';
import { RequestLineItemDto, SavedQuotationDto } from '../../../types';
import { formatCurrencyAO } from '../../../lib/utils';
import { CircleDashed, CheckCircle2, DollarSign } from 'lucide-react';

interface ItemAwardMatrixProps {
    items: RequestLineItemDto[];
    quotations: SavedQuotationDto[];
    itemAwards: Record<string, string>; // requestLineItemId -> quotationItemId
    onAwardChange: (requestLineItemId: string, quotationItemId: string) => void;
    onSelectAll?: (quotationId: string) => void;
}

export function ItemAwardMatrix({
    items,
    quotations,
    itemAwards,
    onAwardChange,
    onSelectAll
}: ItemAwardMatrixProps) {
    
    // Find the lowest price for each request item (across all quotations)
    const lowestPrices = useMemo(() => {
        const prices: Record<string, number> = {};
        
        items.forEach(item => {
            let minPrice: number | null = null;
            
            quotations.forEach(q => {
                const qItem = q.items.find(qi => qi.mappedRequestLineItemId === item.id);
                if (qItem && 
                    (qItem.reconciliationStatus === 'MAPPED' || qItem.reconciliationStatus === 'SUBSTITUTE') && 
                    qItem.unitPrice > 0) {
                    if (minPrice === null || qItem.unitPrice < minPrice) {
                        minPrice = qItem.unitPrice;
                    }
                }
            });
            
            if (minPrice !== null) {
                prices[item.id] = minPrice;
            }
        });
        
        return prices;
    }, [items, quotations]);

    // Compute the summary of assignments
    const groupSummary = useMemo(() => {
        const summary: Record<string, {
            supplierName: string;
            currency: string;
            itemCount: number;
            totalAmount: number;
        }> = {};

        items.forEach(item => {
            const awardedQuotationItemId = itemAwards[item.id];
            if (!awardedQuotationItemId) return;

            // Find the quotation that has this item
            const quotation = quotations.find(q => q.items.some(qi => qi.id === awardedQuotationItemId));
            if (!quotation) return;

            const qItem = quotation.items.find(qi => qi.id === awardedQuotationItemId);
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
    }, [items, quotations, itemAwards]);

    const allItemsAssigned = items.length > 0 && Object.keys(itemAwards).length === items.length;

    // Detect if quotation items exist but are not mapped to request line items
    const hasQuotationItems = quotations.some(q => q.items && q.items.length > 0);
    const anyItemMapped = quotations.some(q =>
        q.items && q.items.some(qi => qi.mappedRequestLineItemId)
    );
    const hasUnmappedWarning = hasQuotationItems && !anyItemMapped;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
            {/* Unmapped quotation items warning for Area Approver */}
            {hasUnmappedWarning && (
                <div style={{
                    padding: '16px 20px',
                    backgroundColor: '#FFFBEB',
                    border: '2px solid #F59E0B',
                    borderRadius: '8px',
                    display: 'flex',
                    alignItems: 'flex-start',
                    gap: '12px',
                    color: '#92400E',
                    fontSize: '0.85rem',
                    fontWeight: 600
                }}>
                    <span style={{ fontSize: '1.2rem', flexShrink: 0 }}>⚠️</span>
                    <span>Esta cotação possui itens, mas eles não estão vinculados aos itens do pedido. Devolva o pedido para correção para que o comprador mapeie os itens.</span>
                </div>
            )}
            <div style={{
                backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-lg)',
                overflow: 'hidden',
                boxShadow: 'var(--shadow-sm)'
            }}>
                <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
                        <thead>
                            <tr>
                                <th style={{ 
                                    padding: '16px', 
                                    borderBottom: '2px solid var(--color-border)', 
                                    backgroundColor: 'var(--color-bg-page)',
                                    position: 'sticky',
                                    left: 0,
                                    zIndex: 10,
                                    minWidth: '250px'
                                }}>
                                    <span style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>
                                        Item Requerido
                                    </span>
                                </th>
                                {quotations.map(q => {
                                    // Check if this quotation covers all items
                                    const coversAll = items.every(item => q.items.some(qi => qi.mappedRequestLineItemId === item.id));
                                    
                                    return (
                                        <th key={q.id} style={{ 
                                            padding: '16px', 
                                            borderBottom: '2px solid var(--color-border)',
                                            borderLeft: '1px solid var(--color-border)',
                                            backgroundColor: 'var(--color-bg-page)',
                                            minWidth: '220px',
                                            verticalAlign: 'top'
                                        }}>
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                <span style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)' }}>
                                                    {q.supplierNameSnapshot}
                                                </span>
                                                <span style={{ fontSize: '0.625rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                                    Cotação #{q.documentNumber || 'S/N'} • {q.currency}
                                                </span>
                                                
                                                {coversAll && onSelectAll && (
                                                    <button
                                                        onClick={() => onSelectAll(q.id)}
                                                        style={{
                                                            marginTop: '8px',
                                                            padding: '4px 8px',
                                                            backgroundColor: 'rgba(22, 163, 74, 0.1)',
                                                            color: 'var(--color-status-green)',
                                                            border: '1px solid rgba(22, 163, 74, 0.3)',
                                                            borderRadius: '4px',
                                                            fontSize: '0.625rem',
                                                            fontWeight: 800,
                                                            cursor: 'pointer',
                                                            transition: 'all 0.2s ease',
                                                            textTransform: 'uppercase'
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
                                    padding: '16px', 
                                    borderBottom: '2px solid var(--color-border)',
                                    borderLeft: '1px solid var(--color-border)',
                                    backgroundColor: 'var(--color-bg-page)',
                                    minWidth: '120px'
                                }}>
                                    <span style={{ fontSize: '0.625rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>
                                        Estado
                                    </span>
                                </th>
                            </tr>
                        </thead>
                        <tbody>
                            {items.map((item, index) => {
                                const isItemAssigned = !!itemAwards[item.id];
                                
                                return (
                                    <tr key={item.id} style={{ borderBottom: index === items.length - 1 ? 'none' : '1px solid var(--color-border)' }}>
                                        <td style={{ 
                                            padding: '16px', 
                                            position: 'sticky', 
                                            left: 0, 
                                            backgroundColor: 'var(--color-bg-surface)',
                                            zIndex: 5,
                                            borderRight: '1px solid var(--color-border)'
                                        }}>
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                <span style={{ fontSize: '0.625rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>
                                                    #{item.lineNumber}
                                                </span>
                                                <span style={{ fontSize: '0.875rem', fontWeight: 800, color: 'var(--color-text-main)' }}>
                                                    {item.description}
                                                </span>
                                                <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>
                                                    {item.quantity} {item.unit || 'UN'}
                                                </span>
                                            </div>
                                        </td>
                                        
                                        {quotations.map(q => {
                                            const qItem = q.items.find(qi => qi.mappedRequestLineItemId === item.id);
                                            const isBestPrice = qItem && qItem.unitPrice === lowestPrices[item.id];
                                            const isSelected = qItem && itemAwards[item.id] === qItem.id;
                                            const isNotQuoted = qItem?.reconciliationStatus === 'NOT_QUOTED';
                                            const isIgnored = qItem?.reconciliationStatus === 'IGNORED';
                                            const isValid = qItem && !isNotQuoted && !isIgnored;
                                            
                                            return (
                                                <td key={q.id} style={{ 
                                                    padding: '16px', 
                                                    borderLeft: '1px solid var(--color-border)',
                                                    backgroundColor: isSelected ? 'rgba(22, 163, 74, 0.05)' : 'transparent',
                                                    verticalAlign: 'top',
                                                    transition: 'background-color 0.2s ease'
                                                }}>
                                                    {isValid ? (
                                                        <div 
                                                            onClick={() => onAwardChange(item.id, qItem.id)}
                                                            style={{ 
                                                                display: 'flex', 
                                                                flexDirection: 'column', 
                                                                gap: '8px',
                                                                cursor: 'pointer',
                                                                height: '100%',
                                                                padding: '8px',
                                                                borderRadius: '8px',
                                                                border: isSelected ? '2px solid var(--color-status-green)' : '2px solid transparent',
                                                                backgroundColor: isBestPrice && !isSelected ? 'rgba(22, 163, 74, 0.03)' : 'transparent'
                                                            }}
                                                        >
                                                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                                {isSelected ? (
                                                                    <div style={{ width: '18px', height: '18px', borderRadius: '50%', backgroundColor: 'var(--color-status-green)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                                                        <div style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: 'white' }} />
                                                                    </div>
                                                                ) : (
                                                                    <div style={{ width: '18px', height: '18px', borderRadius: '50%', border: '2px solid var(--color-border)', display: 'flex', alignItems: 'center', justifyContent: 'center' }} />
                                                                )}
                                                                <span style={{ fontSize: '0.875rem', fontWeight: 800, color: isSelected ? 'var(--color-status-green)' : 'var(--color-text-main)' }}>
                                                                    {formatCurrencyAO(qItem.unitPrice)}
                                                                </span>
                                                                <span style={{ fontSize: '0.625rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                                                                    / {qItem.unitName || qItem.unitCode || 'UN'}
                                                                </span>
                                                            </div>
                                                            
                                                            <div style={{ paddingLeft: '26px' }}>
                                                                <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>
                                                                    Total: {formatCurrencyAO(qItem.lineTotal)}
                                                                </span>
                                                            </div>

                                                            {isBestPrice && !isSelected && (
                                                                <div style={{ paddingLeft: '26px', marginTop: '4px' }}>
                                                                    <span style={{ fontSize: '0.625rem', fontWeight: 800, color: 'var(--color-status-green)', backgroundColor: 'rgba(22, 163, 74, 0.1)', padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                                                        Melhor Preço
                                                                    </span>
                                                                </div>
                                                            )}
                                                        </div>
                                                    ) : (
                                                        <div style={{ 
                                                            display: 'flex', 
                                                            alignItems: 'center', 
                                                            justifyContent: 'center',
                                                            height: '100%',
                                                            color: 'var(--color-text-muted)',
                                                            fontSize: '0.75rem',
                                                            fontWeight: 600,
                                                            opacity: 0.5,
                                                            backgroundColor: isNotQuoted ? 'var(--color-bg-page)' : 'transparent',
                                                            borderRadius: '8px'
                                                        }}>
                                                            {isNotQuoted ? '— não cotado —' : (isIgnored ? '' : '—')}
                                                        </div>
                                                    )}
                                                </td>
                                            );
                                        })}
                                        
                                        <td style={{ 
                                            padding: '16px', 
                                            borderLeft: '1px solid var(--color-border)',
                                            verticalAlign: 'middle',
                                            textAlign: 'center'
                                        }}>
                                            {isItemAssigned ? (
                                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px', color: 'var(--color-status-green)' }}>
                                                    <CheckCircle2 size={18} />
                                                    <span style={{ fontSize: '0.75rem', fontWeight: 800 }}>Atribuído</span>
                                                </div>
                                            ) : (
                                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px', color: 'var(--color-status-red)' }}>
                                                    <CircleDashed size={18} />
                                                    <span style={{ fontSize: '0.75rem', fontWeight: 800 }}>Pendente</span>
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

            {/* Assignment Summary */}
            <div style={{
                backgroundColor: 'var(--color-bg-page)',
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-lg)',
                padding: '20px'
            }}>
                <h4 style={{ fontSize: '0.875rem', fontWeight: 900, color: 'var(--color-text-main)', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <DollarSign size={16} /> Resumo da Atribuição
                </h4>
                
                {groupSummary.length === 0 ? (
                    <div style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                        Nenhuma cotação selecionada ainda.
                    </div>
                ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        {groupSummary.map((group, idx) => (
                            <div key={idx} style={{ 
                                display: 'flex', 
                                justifyContent: 'space-between', 
                                alignItems: 'center',
                                padding: '12px 16px',
                                backgroundColor: 'white',
                                border: '1px solid var(--color-border)',
                                borderRadius: 'var(--radius-md)'
                            }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                    <div style={{ width: '32px', height: '32px', borderRadius: '50%', backgroundColor: 'var(--color-bg-page)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 900, color: 'var(--color-text-main)', fontSize: '0.75rem' }}>
                                        {idx + 1}
                                    </div>
                                    <div style={{ display: 'flex', flexDirection: 'column' }}>
                                        <span style={{ fontSize: '0.875rem', fontWeight: 800, color: 'var(--color-text-main)' }}>
                                            Grupo {idx + 1}: {group.supplierName}
                                        </span>
                                        <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                            {group.itemCount} {group.itemCount === 1 ? 'item' : 'itens'}
                                        </span>
                                    </div>
                                </div>
                                <div style={{ fontSize: '1rem', fontWeight: 900, color: 'var(--color-text-main)' }}>
                                    {formatCurrencyAO(group.totalAmount)} <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>{group.currency}</span>
                                </div>
                            </div>
                        ))}
                    </div>
                )}

                {!allItemsAssigned && (
                    <div style={{ marginTop: '16px', padding: '12px', backgroundColor: '#FEF2F2', border: '1px solid #FECACA', borderRadius: 'var(--radius-md)', display: 'flex', alignItems: 'center', gap: '8px', color: '#991B1B', fontSize: '0.75rem', fontWeight: 600 }}>
                        ⚠ Se algum item ficar sem atribuição de cotação vencedora, a aprovação será bloqueada.
                    </div>
                )}
            </div>
        </div>
    );
}
