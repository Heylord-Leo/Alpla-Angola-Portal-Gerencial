import React, { useState } from 'react';
import { 
    FileText, 
    CheckCircle2, 
    ChevronDown, 
    Hash, 
    Calendar 
} from 'lucide-react';
import { SavedQuotationDto } from '../../../types';
import { formatCurrencyAO, formatDate } from '../../../lib/utils';

interface RequestQuotationsProps {
    quotations: SavedQuotationDto[];
    isFinalApproverMode: boolean;
    isDecisionStage: boolean;
    onSelectWinner: (quotationId: string) => Promise<void>;
    isDrawerMode?: boolean;
}

export const RequestQuotations: React.FC<RequestQuotationsProps> = ({ 
    quotations, 
    isFinalApproverMode, 
    isDecisionStage,
    onSelectWinner,
    isDrawerMode
}) => {
    const [expandedQuotations, setExpandedQuotations] = useState<Record<string, boolean>>({});
    const [processingId, setProcessingId] = useState<string | null>(null);

    if (quotations.length === 0) {
        return (
            <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', backgroundColor: 'var(--color-bg-page)', border: '2px dashed var(--color-border)', borderRadius: '8px' }}>
                Nenhuma cotação salva para este pedido.
            </div>
        );
    }

    // Find lowest amount per currency for visual highlights (same as BuyerItemsList)
    const lowestByCurrency: Record<string, number> = {};
    quotations.forEach(q => {
        if (!lowestByCurrency[q.currency] || q.totalAmount < lowestByCurrency[q.currency]) {
            lowestByCurrency[q.currency] = q.totalAmount;
        }
    });

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
            {quotations.map(q => {
                const isExpanded = expandedQuotations[q.id];
                const isLowest = quotations.length > 1 && q.totalAmount === lowestByCurrency[q.currency];

                return (
                    <div key={q.id} style={{
                        backgroundColor: q.isSelected ? '#eef2ff' : '#fff',
                        border: q.isSelected ? '2px solid #4f46e5' : isLowest ? '2px solid #10b981' : '1px solid var(--color-border-heavy)',
                        borderRadius: '8px',
                        display: 'flex',
                        flexDirection: 'column',
                        boxShadow: q.isSelected ? '0 4px 12px rgba(79, 70, 229, 0.15)' : 'var(--shadow-sm)',
                        overflow: 'hidden',
                        transition: 'all 0.2s ease'
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
                                    color: isLowest ? '#059669' : 'var(--color-primary)' 
                                }}>
                                    <FileText size={20} />
                                </div>
                                <div style={{ flex: 1 }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                        <div style={{ fontWeight: 800, fontSize: '1rem', color: q.isSelected ? '#3730a3' : 'var(--color-text-main)' }}>
                                            {q.supplierNameSnapshot}
                                        </div>
                                        {q.isSelected && (
                                            <span style={{ backgroundColor: '#4f46e5', color: '#fff', fontSize: '0.65rem', fontWeight: 900, padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                                Vencedora
                                            </span>
                                        )}
                                        {isLowest && !q.isSelected && (
                                            <span style={{ backgroundColor: '#10b981', color: '#fff', fontSize: '0.65rem', fontWeight: 900, padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                                Menor Valor ({q.currency})
                                            </span>
                                        )}
                                    </div>
                                    <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 600, display: 'flex', gap: '12px', marginTop: '2px' }}>
                                        <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}><Hash size={12} /> {q.documentNumber || 'S/N'}</span>
                                        <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}><Calendar size={12} /> {q.documentDate ? formatDate(q.documentDate) : '—'}</span>
                                    </div>
                                </div>
                            </div>

                            <div style={{ display: 'flex', alignItems: 'center', gap: '24px' }}>
                                {/* Winner Selection Action (ONLY for Final Approver in Decision stage & not drawer) */}
                                {isFinalApproverMode && isDecisionStage && !q.isSelected && !isDrawerMode && (
                                    <button 
                                        onClick={(e) => { e.stopPropagation(); handleSelect(q.id); }}
                                        disabled={!!processingId}
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
                                    <div style={{ fontSize: '1.15rem', fontWeight: 900, color: 'var(--color-primary)' }}>
                                        <span style={{ fontSize: '0.75rem', opacity: 0.7, marginRight: '4px' }}>{q.currency}</span>
                                        {formatCurrencyAO(q.totalAmount)}
                                    </div>
                                    <div style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', fontWeight: 700 }}>{q.itemCount} ITENS</div>
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
                                            <th style={{ padding: '8px', textAlign: 'right', borderBottom: '1px solid var(--color-border)', width: '120px' }}>TOTAL</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {q.items?.map((item, idx) => (
                                            <tr key={item.id || idx} style={{ backgroundColor: 'var(--color-bg-surface)', borderBottom: '1px solid var(--color-border-light)' }}>
                                                <td style={{ padding: '8px', fontWeight: 600 }}>{item.description}</td>
                                                <td style={{ padding: '8px', textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>{item.unitCode || '---'}</td>
                                                <td style={{ padding: '8px', textAlign: 'right' }}>{item.quantity}</td>
                                                <td style={{ padding: '8px', textAlign: 'right' }}>{formatCurrencyAO(item.unitPrice)}</td>
                                                <td style={{ padding: '8px', textAlign: 'right', fontWeight: 700 }}>{formatCurrencyAO(item.lineTotal)}</td>
                                            </tr>
                                        ))}
                                    </tbody>
                                    <tfoot style={{ backgroundColor: 'var(--color-bg-page)', fontWeight: 800 }}>
                                        <tr>
                                            <td colSpan={3} style={{ padding: '8px', textAlign: 'right' }}>TOTAL DA COTAÇÃO ({q.currency}):</td>
                                            <td style={{ padding: '8px', textAlign: 'right', color: 'var(--color-primary)' }}>{formatCurrencyAO(q.totalAmount)}</td>
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
