import { FileText, CheckCircle2, Trophy, Hash, Calendar } from 'lucide-react';
import { SavedQuotationDto } from '../../../types';
import { formatCurrencyAO, formatDate } from '../../../lib/utils';

interface DecisionQuotationCardProps {
    quotation: SavedQuotationDto;
    isLowest: boolean;
    canSelectWinner: boolean;
    onSelectWinner: (id: string) => void;
    isProcessing: boolean;
}

export function DecisionQuotationCard({
    quotation: q,
    isLowest,
    canSelectWinner,
    onSelectWinner,
    isProcessing
}: DecisionQuotationCardProps) {
    return (
        <div style={{
            backgroundColor: q.isSelected ? '#eff6ff' : '#fff',
            border: q.isSelected ? '3px solid #3b82f6' : isLowest ? '2px solid #10b981' : '2px solid black',
            marginBottom: '12px',
            display: 'flex',
            flexDirection: 'column',
            transition: 'all 0.2s ease',
            position: 'relative'
        }}>
            {/* --- Header Row --- */}
            <div style={{
                padding: '16px',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'flex-start',
                gap: '12px'
            }}>
                <div style={{ display: 'flex', gap: '12px', flex: 1 }}>
                    <div style={{ 
                        width: '40px',
                        height: '40px',
                        backgroundColor: q.isSelected ? '#3b82f6' : isLowest ? '#10b981' : 'black',
                        color: 'white',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        borderRadius: '0',
                        flexShrink: 0
                    }}>
                        {q.isSelected ? <Trophy size={20} /> : <FileText size={20} />}
                    </div>
                    
                    <div style={{ flex: 1 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                            <span style={{ 
                                fontWeight: 900, 
                                fontSize: '0.95rem', 
                                color: q.isSelected ? '#1e40af' : 'black' 
                            }}>
                                {q.supplierNameSnapshot}
                            </span>
                            {q.isSelected && (
                                <span style={{ 
                                    backgroundColor: '#3b82f6', 
                                    color: 'white', 
                                    fontSize: '10px', 
                                    fontWeight: 900, 
                                    padding: '2px 8px', 
                                    textTransform: 'uppercase',
                                    borderRadius: '2px'
                                }}>
                                    VENCEDORA
                                </span>
                            )}
                            {isLowest && !q.isSelected && (
                                <span style={{ 
                                    backgroundColor: '#10b981', 
                                    color: 'white', 
                                    fontSize: '10px', 
                                    fontWeight: 900, 
                                    padding: '2px 8px', 
                                    textTransform: 'uppercase',
                                    borderRadius: '2px'
                                }}>
                                    MELHOR PREÇO
                                </span>
                            )}
                        </div>
                        
                        <div style={{ 
                            display: 'flex', 
                            gap: '12px', 
                            marginTop: '4px',
                            color: 'var(--color-text-muted)',
                            fontSize: '11px',
                            fontWeight: 700
                        }}>
                             <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                <Hash size={12} /> {q.documentNumber || 'S/N'}
                             </span>
                             <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                <Calendar size={12} /> {q.documentDate ? formatDate(q.documentDate) : '---'}
                             </span>
                        </div>
                    </div>
                </div>

                <div style={{ textAlign: 'right' }}>
                    <div style={{ 
                        fontSize: '1.25rem', 
                        fontWeight: 900, 
                        color: q.isSelected ? '#1e40af' : 'black',
                        letterSpacing: '-0.02em'
                    }}>
                        {formatCurrencyAO(q.totalAmount)}
                        <span style={{ fontSize: '0.75rem', marginLeft: '4px', opacity: 0.6 }}>{q.currency}</span>
                    </div>
                    <div style={{ fontSize: '10px', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                        {q.itemCount} itens cotados
                    </div>
                </div>
            </div>

            {/* --- Action Row (if processing or selectable) --- */}
            {canSelectWinner && !q.isSelected && (
                <div style={{ 
                    padding: '12px 16px', 
                    borderTop: '1px solid black',
                    backgroundColor: '#fafafa',
                    display: 'flex',
                    justifyContent: 'flex-end'
                }}>
                    <button 
                        onClick={() => onSelectWinner(q.id)}
                        disabled={isProcessing}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '6px',
                            backgroundColor: 'white',
                            border: '2px solid black',
                            borderRadius: '0',
                            padding: '6px 16px',
                            fontSize: '0.7rem',
                            fontWeight: 900,
                            textTransform: 'uppercase',
                            cursor: 'pointer',
                            transition: 'all 0.1s'
                        }}
                        className="hover:bg-black hover:text-white"
                    >
                         <CheckCircle2 size={14} />
                         {isProcessing ? 'Selecionando...' : 'Selecionar Vencedora'}
                    </button>
                </div>
            )}
            
            {/* If winner selected, show confirmation of choice */}
            {q.isSelected && (
                <div style={{ 
                    padding: '8px 16px', 
                    backgroundColor: '#dbeafe', 
                    borderTop: '1px solid #3b82f6',
                    fontSize: '11px',
                    fontWeight: 800,
                    color: '#1e40af',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px'
                }}>
                    <CheckCircle2 size={12} /> Cotação selecionada para aprovação
                </div>
            )}
        </div>
    );
}
