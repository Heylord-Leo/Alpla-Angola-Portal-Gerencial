import React, { useMemo } from 'react';
import { RequestDetailsDto } from '../../types';
import { formatCurrencyAO } from '../../lib/utils';
import { AlertCircle, CheckCircle2 } from 'lucide-react';

interface WizardStepAwardProps {
    request: RequestDetailsDto;
    quotations: any[];
    itemAwards: Record<string, string>;
    onChangeAward: (requestLineItemId: string, quotationItemId: string) => void;
}

export const WizardStepAward: React.FC<WizardStepAwardProps> = ({ request, quotations, itemAwards, onChangeAward }) => {
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

    const isAwarded = (itemId: string) => {
        return !!itemAwards[itemId] || !!activeItems.find((i: any) => i.id === itemId)?.selectedQuotationItemId;
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <div>
                <h3 style={{ fontSize: '1.25rem', fontWeight: 600, color: '#1F2937', margin: 0 }}>Atribuição de Cotação (Vencedor)</h3>
                <p style={{ color: '#6B7280', fontSize: '0.875rem', marginTop: '4px' }}>
                    Selecione qual cotação vencerá cada item solicitado. A aprovação da área exige que todos os itens possuam uma cotação vencedora.
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
                        <div style={{ backgroundColor: '#F9FAFB', padding: '16px', borderBottom: '1px solid #E5E7EB', display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                            <div>
                                <div style={{ fontSize: '0.875rem', fontWeight: 700, color: '#374151' }}>Item #{item.lineNumber}</div>
                                <div style={{ fontSize: '1rem', fontWeight: 600, color: '#111827' }}>{item.description}</div>
                                <div style={{ fontSize: '0.875rem', color: '#6B7280' }}>Quantidade: {item.quantity} {item.unitName || ''}</div>
                            </div>
                            <div>
                                {isAwarded(item.id) ? (
                                    <span style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.75rem', fontWeight: 700, color: '#10B981' }}>
                                        <CheckCircle2 size={16} /> Atribuído
                                    </span>
                                ) : (
                                    <span style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.75rem', fontWeight: 700, color: '#EF4444' }}>
                                        <AlertCircle size={16} /> Pendente
                                    </span>
                                )}
                            </div>
                        </div>

                        {/* Quotation Options */}
                        <div style={{ padding: '16px', display: 'flex', gap: '16px', overflowX: 'auto' }}>
                            {quotations.map(q => {
                                const qItem = q.items?.find((qi: any) => qi.mappedRequestLineItemId === item.id);
                                const isLowest = qItem && qItem.unitPrice === lowestPrices[item.id];
                                // Check local awards first, then fall back to persisted assignment if unedited
                                const currentSelected = itemAwards[item.id] || item.selectedQuotationItemId;
                                const isSelected = qItem && currentSelected === qItem.id;

                                return (
                                    <div 
                                        key={q.id} 
                                        onClick={() => qItem && onChangeAward(item.id, qItem.id)}
                                        style={{ 
                                            minWidth: '280px',
                                            flex: '1',
                                            padding: '16px', 
                                            borderRadius: '8px', 
                                            border: isSelected ? '2px solid #2563EB' : '1px solid #E5E7EB',
                                            backgroundColor: isSelected ? '#EFF6FF' : '#FFFFFF',
                                            cursor: qItem ? 'pointer' : 'not-allowed',
                                            opacity: qItem ? 1 : 0.6,
                                            position: 'relative',
                                            display: 'flex',
                                            flexDirection: 'column',
                                            gap: '8px',
                                            transition: 'all 0.2s'
                                        }}
                                    >
                                        {isLowest && !isSelected && (
                                            <div style={{ 
                                                position: 'absolute', top: '-10px', right: '16px', 
                                                backgroundColor: '#10B981', color: 'white', 
                                                padding: '2px 8px', borderRadius: '12px', 
                                                fontSize: '0.75rem', fontWeight: 700
                                            }}>
                                                Menor Preço
                                            </div>
                                        )}
                                        {isSelected && (
                                            <div style={{ 
                                                position: 'absolute', top: '-10px', right: '16px', 
                                                backgroundColor: '#2563EB', color: 'white', 
                                                padding: '2px 8px', borderRadius: '12px', 
                                                fontSize: '0.75rem', fontWeight: 700
                                            }}>
                                                Selecionado
                                            </div>
                                        )}
                                        
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                            <input 
                                                type="radio" 
                                                checked={isSelected} 
                                                readOnly
                                                disabled={!qItem}
                                                style={{ cursor: qItem ? 'pointer' : 'not-allowed' }}
                                            />
                                            <div>
                                                <div style={{ fontWeight: 700, color: '#1F2937', fontSize: '0.9rem' }}>{q.supplierNameSnapshot}</div>
                                                <div style={{ fontSize: '0.75rem', color: '#6B7280' }}>Cot. #{q.documentNumber || 'S/N'}</div>
                                            </div>
                                        </div>
                                        
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

            {!activeItems.every((i: any) => isAwarded(i.id)) && (
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px', padding: '16px', backgroundColor: '#FEF2F2', border: '1px solid #F87171', borderRadius: '8px' }}>
                    <AlertCircle color="#DC2626" size={20} style={{ flexShrink: 0, marginTop: '2px' }} />
                    <div style={{ display: 'flex', flexDirection: 'column' }}>
                        <span style={{ color: '#991B1B', fontWeight: 600, fontSize: '0.875rem' }}>Seleção Incompleta</span>
                        <span style={{ color: '#B91C1C', fontSize: '0.875rem' }}>Todos os itens devem ter um fornecedor vencedor selecionado para prosseguir.</span>
                    </div>
                </div>
            )}
        </div>
    );
};
