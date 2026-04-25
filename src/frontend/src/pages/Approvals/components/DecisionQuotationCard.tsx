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
            backgroundColor: 'var(--color-bg-surface)',
            border: q.isSelected ? '2px solid var(--color-status-green)' : isLowest ? '1.5px solid var(--color-status-green)' : '1px solid var(--color-border)',
            borderRadius: 'var(--radius-lg)',
            marginBottom: '16px',
            display: 'flex',
            flexDirection: 'column',
            transition: 'all 0.2s ease',
            position: 'relative',
            boxShadow: q.isSelected ? 'var(--shadow-md)' : 'var(--shadow-sm)',
            overflow: 'hidden'
        }}>
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
                        backgroundColor: q.isSelected || isLowest ? 'var(--color-status-green)' : 'var(--color-bg-page)',
                        color: q.isSelected || isLowest ? 'white' : 'var(--color-text-main)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        borderRadius: 'var(--radius-md)',
                        flexShrink: 0,
                    }}>
                        {q.isSelected ? <Trophy size={22} strokeWidth={2.5} /> : <FileText size={22} strokeWidth={2.5} />}
                    </div>
                    
                    <div style={{ flex: 1 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                            <span style={{ 
                                fontWeight: 950, 
                                fontSize: '1.05rem', 
                                color: 'var(--color-text-main)',
                                textTransform: 'uppercase',
                                letterSpacing: '-0.02em',
                                lineHeight: '1.1'
                            }}>
                                {q.supplierNameSnapshot}
                            </span>
                            {q.isSelected && (
                                <span style={{
                                    backgroundColor: 'var(--color-status-green)',
                                    color: 'white',
                                    fontSize: '9px',
                                    fontWeight: 950,
                                    padding: '3px 8px',
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.1em',
                                    borderRadius: 'var(--radius-sm)',
                                    boxShadow: 'var(--shadow-sm)',
                                    whiteSpace: 'nowrap',
                                    flexShrink: 0
                                }}>
                                    VENCEDORA
                                </span>
                            )}
                            {isLowest && !q.isSelected && (
                                <span style={{
                                    backgroundColor: 'var(--color-status-green)',
                                    color: 'white',
                                    fontSize: '9px',
                                    fontWeight: 950,
                                    padding: '3px 8px',
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.1em',
                                    borderRadius: 'var(--radius-sm)',
                                    boxShadow: 'var(--shadow-sm)',
                                    whiteSpace: 'nowrap',
                                    flexShrink: 0
                                }}>
                                    MELHOR PREÇO
                                </span>
                            )}
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
                                <Hash size={13} strokeWidth={3} style={{ color: '#6b7280' }} /> {q.documentNumber || 'S/N'}
                             </span>
                             <span style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <Calendar size={13} strokeWidth={3} style={{ color: '#6b7280' }} /> {q.documentDate ? formatDate(q.documentDate) : '---'}
                             </span>
                        </div>
                    </div>
                </div>

                <div style={{ textAlign: 'right' }}>
                    <div style={{ 
                        fontSize: '1.5rem', 
                        fontWeight: 950, 
                        color: 'var(--color-text-main)',
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
                    borderTop: '1px solid var(--color-border)',
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
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-md)',
                            padding: '10px 24px',
                            fontSize: '0.75rem',
                            fontWeight: 950,
                            textTransform: 'uppercase',
                            cursor: 'pointer',
                            transition: 'all 0.2s',
                            boxShadow: 'var(--shadow-sm)',
                            color: 'var(--color-text-main)',
                            letterSpacing: '0.05em'
                        }}
                        onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#f9fafb'; e.currentTarget.style.boxShadow = 'var(--shadow-md)'; }}
                        onMouseOut={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'; e.currentTarget.style.boxShadow = 'var(--shadow-sm)'; }}
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
                    backgroundColor: 'var(--color-status-green)', 
                    fontSize: '11px',
                    fontWeight: 900,
                    color: 'white',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '10px',
                    textTransform: 'uppercase',
                    letterSpacing: '0.1em'
                }}>
                    <CheckCircle2 size={16} strokeWidth={3} style={{ color: 'white' }} /> Cotação selecionada para aprovação
                </div>
            )}
        </div>
    );
}
