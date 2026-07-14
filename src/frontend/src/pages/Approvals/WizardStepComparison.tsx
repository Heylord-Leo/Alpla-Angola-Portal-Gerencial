import React, { useMemo } from 'react';
import { RequestDetailsDto } from '../../types';
import { formatCurrencyAO } from '../../lib/utils';

interface WizardStepComparisonProps {
    request: RequestDetailsDto;
    quotations: any[];
}

export const WizardStepComparison: React.FC<WizardStepComparisonProps> = ({ request, quotations }) => {
    const activeItems = request.lineItems?.filter((i: any) => !i.isDeleted) || [];

    // Calculate lowest price per item
    const lowestPrices = useMemo(() => {
        const prices: Record<string, number> = {};
        activeItems.forEach((item: any) => {
            let lowest = Infinity;
            quotations.forEach(q => {
                const qItem = q.items?.find((qi: any) => qi.mappedRequestLineItemId === item.id);
                if (qItem && qItem.unitPrice < lowest) {
                    lowest = qItem.unitPrice;
                }
            });
            if (lowest !== Infinity) prices[item.id] = lowest;
        });
        return prices;
    }, [activeItems, quotations]);

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <div>
                <h3 style={{ fontSize: '1.25rem', fontWeight: 600, color: '#1F2937', margin: 0 }}>Comparação de Cotações</h3>
                <p style={{ color: '#6B7280', fontSize: '0.875rem', marginTop: '4px' }}>
                    Revise as opções de fornecimento para cada item solicitado.
                </p>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                {activeItems.map((item: any) => (
                    <div key={item.id} style={{ 
                        border: '1px solid #E5E7EB',
                        borderRadius: '8px',
                        overflow: 'hidden',
                        backgroundColor: '#FFFFFF',
                        boxShadow: '0 1px 2px 0 rgba(0, 0, 0, 0.05)'
                    }}>
                        {/* Item Header */}
                        <div style={{ backgroundColor: '#F9FAFB', padding: '16px', borderBottom: '1px solid #E5E7EB' }}>
                            <div style={{ fontSize: '0.875rem', fontWeight: 700, color: '#374151' }}>Item #{item.lineNumber}</div>
                            <div style={{ fontSize: '1rem', fontWeight: 600, color: '#111827' }}>{item.description}</div>
                            <div style={{ fontSize: '0.875rem', color: '#6B7280' }}>Quantidade: {item.quantity} {item.unitName || ''}</div>
                        </div>

                        {/* Quotation Options */}
                        <div style={{ padding: '16px', display: 'flex', gap: '16px', overflowX: 'auto' }}>
                            {quotations.map(q => {
                                const qItem = q.items?.find((qi: any) => qi.mappedRequestLineItemId === item.id);
                                const isLowest = qItem && qItem.unitPrice === lowestPrices[item.id];

                                return (
                                    <div key={q.id} style={{ 
                                        minWidth: '280px',
                                        flex: '1',
                                        padding: '16px', 
                                        borderRadius: '8px', 
                                        border: isLowest ? '2px solid #10B981' : '1px solid #E5E7EB',
                                        backgroundColor: isLowest ? '#ECFDF5' : '#FFFFFF',
                                        position: 'relative',
                                        display: 'flex',
                                        flexDirection: 'column',
                                        gap: '8px'
                                    }}>
                                        {isLowest && (
                                            <div style={{ 
                                                position: 'absolute', top: '-10px', right: '16px', 
                                                backgroundColor: '#10B981', color: 'white', 
                                                padding: '2px 8px', borderRadius: '12px', 
                                                fontSize: '0.75rem', fontWeight: 700
                                            }}>
                                                Menor Preço
                                            </div>
                                        )}
                                        
                                        <div style={{ fontWeight: 700, color: '#1F2937', fontSize: '0.9rem' }}>{q.supplierNameSnapshot}</div>
                                        <div style={{ fontSize: '0.75rem', color: '#6B7280' }}>Cot. #{q.documentNumber || 'S/N'}</div>
                                        
                                        {qItem ? (
                                            <div style={{ marginTop: '8px', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.875rem' }}>
                                                    <span style={{ color: '#6B7280' }}>Preço Unit.:</span>
                                                    <span style={{ fontWeight: 600, color: '#374151' }}>{formatCurrencyAO(qItem.unitPrice, q.currency)}</span>
                                                </div>
                                                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.875rem' }}>
                                                    <span style={{ color: '#6B7280' }}>Preço Total:</span>
                                                    <span style={{ fontWeight: 700, color: '#111827' }}>{formatCurrencyAO(qItem.totalPrice, q.currency)}</span>
                                                </div>
                                            </div>
                                        ) : (
                                            <div style={{ marginTop: '16px', textAlign: 'center', color: '#9CA3AF', fontSize: '0.875rem', fontWeight: 500 }}>
                                                Não Cotado
                                            </div>
                                        )}
                                    </div>
                                );
                            })}
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
};
