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
            backgroundColor: 'white',
            border: q.isSelected ? '3px solid #16a34a' : isLowest ? '2.5px solid #16a34a' : '2.5px solid black',
            marginBottom: '16px',
            display: 'flex',
            flexDirection: 'column',
            transition: 'all 0.2s ease',
            position: 'relative',
            boxShadow: q.isSelected ? 'var(--shadow-brutal)' : 'none'
        }}>
            {/* Winner/Best Price Badge Overlay */}
            {(q.isSelected || isLowest) && (
                <div style={{
                    position: 'absolute',
                    top: '-12px',
                    right: '16px',
                    display: 'flex',
                    gap: '8px',
                    zIndex: 10
                }}>
                    {q.isSelected && (
                        <div style={{
                            backgroundColor: '#16a34a',
                            color: 'white',
                            fontSize: '9px',
                            fontWeight: 950,
                            padding: '4px 10px',
                            textTransform: 'uppercase',
                            letterSpacing: '0.1em',
                            border: '1.5px solid black',
                            boxShadow: '2px 2px 0px rgba(0,0,0,0.2)'
                        }}>
                            VENCEDORA
                        </div>
                    )}
                    {isLowest && !q.isSelected && (
                        <div style={{
                            backgroundColor: '#16a34a',
                            color: 'white',
                            fontSize: '9px',
                            fontWeight: 950,
                            padding: '4px 10px',
                            textTransform: 'uppercase',
                            letterSpacing: '0.1em',
                            border: '1.5px solid black',
                            boxShadow: '2px 2px 0px rgba(0,0,0,0.2)'
                        }}>
                            MELHOR PREÇO
                        </div>
                    )}
                </div>
            )}

            {/* --- Header Row --- */}
            <div style={{
                padding: '20px',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'flex-start',
                gap: '16px',
                backgroundColor: q.isSelected ? 'rgba(22, 163, 74, 0.04)' : 'transparent'
            }}>
                <div style={{ display: 'flex', gap: '16px', flex: 1 }}>
                    <div style={{ 
                        width: '44px',
                        height: '44px',
                        backgroundColor: q.isSelected ? '#16a34a' : isLowest ? '#16a34a' : 'white',
                        color: q.isSelected ? 'white' : isLowest ? 'white' : 'black',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        borderRadius: '0',
                        flexShrink: 0,
                        border: '2px solid black',
                        boxShadow: '2px 2px 0px rgba(0,0,0,1)'
                    }}>
                        {q.isSelected ? <Trophy size={22} strokeWidth={2.5} /> : <FileText size={22} strokeWidth={2.5} />}
                    </div>
                    
                    <div style={{ flex: 1 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                            <span style={{ 
                                fontWeight: 950, 
                                fontSize: '1.05rem', 
                                color: 'black',
                                textTransform: 'uppercase',
                                letterSpacing: '-0.02em',
                                lineHeight: '1.1'
                            }}>
                                {q.supplierNameSnapshot}
                            </span>
                        </div>
                        
                        <div style={{ 
                            display: 'flex', 
                            gap: '14px', 
                            marginTop: '8px',
                            color: 'var(--color-text-muted)',
                            fontSize: '0.65rem',
                            fontWeight: 850,
                            textTransform: 'uppercase',
                            letterSpacing: '0.05em'
                        }}>
                             <span style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <Hash size={13} strokeWidth={3} className="text-black" /> {q.documentNumber || 'S/N'}
                             </span>
                             <span style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <Calendar size={13} strokeWidth={3} className="text-black" /> {q.documentDate ? formatDate(q.documentDate) : '---'}
                             </span>
                        </div>
                    </div>
                </div>

                <div style={{ textAlign: 'right' }}>
                    <div style={{ 
                        fontSize: '1.5rem', 
                        fontWeight: 950, 
                        color: 'black',
                        letterSpacing: '-0.03em',
                        fontVariantNumeric: 'tabular-nums'
                    }}>
                        {formatCurrencyAO(q.totalAmount)}
                        <span style={{ fontSize: '0.75rem', marginLeft: '6px', opacity: 0.5, fontWeight: 800 }}>{q.currency}</span>
                    </div>
                    <div style={{ fontSize: '10px', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                        {q.itemCount} itens cotados
                    </div>
                </div>
            </div>

            {/* --- Action Row (if processing or selectable) --- */}
            {canSelectWinner && !q.isSelected && (
                <div style={{ 
                    padding: '16px 20px', 
                    borderTop: '2px solid black',
                    backgroundColor: 'var(--color-bg-page)',
                    display: 'flex',
                    justifyContent: 'flex-end'
                }}>
                    <button 
                        onClick={() => onSelectWinner(q.id)}
                        disabled={isProcessing}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            backgroundColor: 'white',
                            border: '2px solid black',
                            borderRadius: '0',
                            padding: '10px 24px',
                            fontSize: '0.75rem',
                            fontWeight: 950,
                            textTransform: 'uppercase',
                            cursor: 'pointer',
                            transition: 'all 0.1s',
                            boxShadow: '3px 3px 0px rgba(0,0,0,1)',
                            letterSpacing: '0.05em'
                        }}
                        className="hover:translate-x-[1px] hover:translate-y-[1px] hover:shadow-none bg-white hover:bg-black hover:text-white"
                    >
                         <CheckCircle2 size={16} strokeWidth={3} />
                         {isProcessing ? 'Selecionando...' : 'Selecionar Vencedora'}
                    </button>
                </div>
            )}
            
            {/* Choice highlight footer */}
            {q.isSelected && (
                <div style={{ 
                    padding: '12px 20px', 
                    backgroundColor: '#16a34a', 
                    borderTop: '2px solid black',
                    fontSize: '11px',
                    fontWeight: 900,
                    color: 'white',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '10px',
                    textTransform: 'uppercase',
                    letterSpacing: '0.1em'
                }}>
                    <CheckCircle2 size={16} strokeWidth={3} className="text-white" /> Cotação selecionada para aprovação
                </div>
            )}
        </div>
    );
}
