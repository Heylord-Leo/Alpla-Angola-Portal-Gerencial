import { FileText, CheckCircle2, Trophy, Hash, Calendar, ChevronDown, ChevronUp } from 'lucide-react';
import { SavedQuotationDto, SavedQuotationItemDto } from '../../../types';
import { formatCurrencyAO, formatDate } from '../../../lib/utils';

interface DecisionQuotationCardProps {
    quotation: SavedQuotationDto;
    isLowest: boolean;
    canSelectWinner: boolean;
    onSelectWinner: (id: string) => void;
    isProcessing: boolean;
    isExpanded: boolean;
    onToggleExpand: (id: string) => void;
    /** Lot-aware mode (Final Approval): the quotation-item ids that belong to the current lot.
     *  When provided, expanded items are split into "considerados neste lote" and "não incluídos". */
    lotIncludedItemIds?: Set<string>;
    /** Audit reason (IGNORED justification) per quotation-item id — shown on not-included rows. */
    lotIgnoredReasonById?: Map<string, string | null | undefined>;
    /** Candidate model: this quotation's AWARDED value in the current lot, summed from the FROZEN
     *  winning candidate snapshots (never live line totals, never Quotation.TotalAmount).
     *  Null = candidate batch with no winner from this quotation decided yet. Undefined = legacy
     *  batch (no snapshots) — the footer then falls back to the included live line totals. */
    lotAwardedTotal?: number | null;
}

const MAX_VISIBLE_ITEMS = 5;

export function DecisionQuotationCard({
    quotation: q,
    isLowest,
    canSelectWinner,
    onSelectWinner,
    isProcessing,
    isExpanded,
    onToggleExpand,
    lotIncludedItemIds,
    lotIgnoredReasonById,
    lotAwardedTotal
}: DecisionQuotationCardProps) {
    const items = q.items || [];
    const hasItems = items.length > 0;
    const lotAware = !!lotIncludedItemIds;
    const includedItems = lotAware ? items.filter(it => lotIncludedItemIds!.has(it.id)) : items;
    const notIncludedItems = lotAware ? items.filter(it => !lotIncludedItemIds!.has(it.id)) : [];

    // Lot footer value: frozen winning-candidate snapshot total when provided (candidate model —
    // a number, possibly a genuine 0 for a quotation whose options all lost); null = the Area
    // decision is still pending; undefined = legacy batch, valued from the included winners'
    // line totals. NEVER the whole-document Quotation.TotalAmount — that number is the complete
    // quotation, not what this lot awards.
    const lotConsideredTotal: number | null = lotAwardedTotal !== undefined
        ? lotAwardedTotal
        : (includedItems.length > 0 ? includedItems.reduce((sum, it) => sum + (it.lineTotal || 0), 0) : null);
    const visibleItems = isExpanded ? items.slice(0, MAX_VISIBLE_ITEMS) : [];
    const hiddenCount = items.length - MAX_VISIBLE_ITEMS;
    const showScrollArea = isExpanded && items.length > MAX_VISIBLE_ITEMS;

    const handleCardBodyClick = () => {
        if (hasItems) {
            onToggleExpand(q.id);
        }
    };

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
            {/* --- Header Row (clickable to expand) --- */}
            <div 
                onClick={handleCardBodyClick}
                style={{
                    padding: '20px',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'flex-start',
                    gap: '16px',
                    backgroundColor: q.isSelected ? 'rgba(22, 163, 74, 0.04)' : 'transparent',
                    cursor: hasItems ? 'pointer' : 'default',
                    transition: 'background-color 0.15s ease'
                }}
                onMouseOver={(e) => { if (hasItems) e.currentTarget.style.backgroundColor = q.isSelected ? 'rgba(22, 163, 74, 0.06)' : 'rgba(0,0,0,0.015)'; }}
                onMouseOut={(e) => { e.currentTarget.style.backgroundColor = q.isSelected ? 'rgba(22, 163, 74, 0.04)' : 'transparent'; }}
            >
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
                             {/* Expand hint */}
                             {hasItems && (
                                <span style={{ 
                                    display: 'flex', 
                                    alignItems: 'center', 
                                    gap: '4px', 
                                    marginLeft: 'auto',
                                    color: 'var(--color-primary, #2563eb)',
                                    fontSize: '0.6rem',
                                    fontWeight: 900,
                                    opacity: 0.8
                                }}>
                                    {isExpanded ? (
                                        <>Ocultar itens <ChevronUp size={13} strokeWidth={3} /></>
                                    ) : (
                                        <>Ver itens <ChevronDown size={13} strokeWidth={3} /></>
                                    )}
                                </span>
                             )}
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
                        {lotAware
                            ? `Total da cotação · ${includedItems.length} no lote${notIncludedItems.length > 0 ? ` · ${notIncludedItems.length} fora` : ''}`
                            : `${q.itemCount} itens cotados`}
                    </div>
                </div>
            </div>

            {/* --- Expanded Items Area --- */}
            {isExpanded && (
                <div style={{
                    borderTop: '1px solid var(--color-border)',
                    backgroundColor: 'var(--color-bg-page)',
                    padding: '0',
                    overflow: 'hidden',
                    animation: 'fadeIn 0.2s ease'
                }}>
                    {!hasItems ? (
                        <div style={{
                            padding: '16px 20px',
                            fontSize: '0.8rem',
                            fontWeight: 600,
                            color: 'var(--color-text-muted)',
                            fontStyle: 'italic',
                            textAlign: 'center'
                        }}>
                            Itens não disponíveis para esta cotação.
                        </div>
                    ) : lotAware ? (
                        /* --- LOT-AWARE MODE (Final Approval): included vs. not-included-in-this-lot --- */
                        <>
                            <ItemGroupHeader
                                label={`Itens considerados neste lote (${includedItems.length})`}
                            />
                            <div style={{ padding: '0 20px' }}>
                                {includedItems.map((item, idx) => (
                                    <QuotationItemRow key={item.id || `inc-${idx}`} item={item} isLast={idx === includedItems.length - 1} />
                                ))}
                                {includedItems.length === 0 && (
                                    <div style={{ padding: '12px 0', fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                        Nenhum item desta cotação pertence a este lote.
                                    </div>
                                )}
                            </div>

                            {notIncludedItems.length > 0 && (
                                <>
                                    <ItemGroupHeader
                                        label={`Ignorados / não incluídos neste lote (${notIncludedItems.length})`}
                                        muted
                                    />
                                    <div style={{ padding: '0 20px' }}>
                                        {notIncludedItems.map((item, idx) => (
                                            <QuotationItemRow
                                                key={item.id || `exc-${idx}`}
                                                item={item}
                                                isLast={idx === notIncludedItems.length - 1}
                                                dimmed
                                                reason={lotIgnoredReasonById?.get(item.id) || item.reconciliationJustification}
                                            />
                                        ))}
                                    </div>
                                </>
                            )}
                        </>
                    ) : (
                        <>
                            {/* Items header */}
                            <div style={{
                                padding: '12px 20px 8px 20px',
                                display: 'flex',
                                justifyContent: 'space-between',
                                alignItems: 'center'
                            }}>
                                <span style={{
                                    fontSize: '0.625rem',
                                    fontWeight: 950,
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.08em',
                                    color: 'var(--color-text-muted)'
                                }}>
                                    Itens cotados
                                </span>
                                <span style={{
                                    fontSize: '0.6rem',
                                    fontWeight: 800,
                                    color: 'var(--color-text-muted)',
                                    backgroundColor: 'var(--color-bg-surface)',
                                    border: '1px solid var(--color-border)',
                                    borderRadius: 'var(--radius-sm)',
                                    padding: '2px 8px'
                                }}>
                                    {items.length} {items.length === 1 ? 'item' : 'itens'}
                                </span>
                            </div>

                            <div style={{
                                maxHeight: showScrollArea ? '280px' : 'none',
                                overflowY: showScrollArea ? 'auto' : 'visible',
                                padding: '0 20px'
                            }}>
                                {(showScrollArea ? items : visibleItems).map((item, idx) => (
                                    <QuotationItemRow
                                        key={item.id || idx}
                                        item={item}
                                        isLast={idx === (showScrollArea ? items.length : visibleItems.length) - 1}
                                    />
                                ))}
                            </div>

                            {/* Hidden items note (only for non-scrollable mode) */}
                            {!showScrollArea && hiddenCount > 0 && (
                                <div style={{
                                    padding: '8px 20px',
                                    fontSize: '0.7rem',
                                    fontWeight: 700,
                                    color: 'var(--color-text-muted)',
                                    textAlign: 'center',
                                    fontStyle: 'italic'
                                }}>
                                    +{hiddenCount} {hiddenCount === 1 ? 'item adicional' : 'itens adicionais'}
                                </div>
                            )}
                        </>
                    )}

                    {/* Footer total */}
                    {hasItems && (
                        <div style={{
                            padding: '10px 20px 14px 20px',
                            borderTop: '1px solid var(--color-border)',
                            display: 'flex',
                            justifyContent: 'space-between',
                            alignItems: 'center',
                            backgroundColor: 'var(--color-bg-surface)'
                        }}>
                            <span style={{
                                fontSize: '0.7rem',
                                fontWeight: 900,
                                textTransform: 'uppercase',
                                letterSpacing: '0.05em',
                                color: 'var(--color-text-muted)'
                            }}>
                                {lotAware ? 'Total considerado neste lote' : 'Total da cotação'}
                            </span>
                            {lotAware && lotConsideredTotal === null ? (
                                <span style={{ fontSize: '0.8125rem', fontWeight: 800, color: 'var(--color-text-muted)' }}>
                                    A definir pelo Aprovador de Área
                                </span>
                            ) : (
                                <span style={{
                                    fontSize: '1rem',
                                    fontWeight: 950,
                                    color: 'var(--color-text-main)',
                                    fontVariantNumeric: 'tabular-nums',
                                    letterSpacing: '-0.02em'
                                }}>
                                    {formatCurrencyAO(lotAware ? (lotConsideredTotal as number) : q.totalAmount)} <span style={{ fontSize: '0.7rem', opacity: 0.5, fontWeight: 800 }}>{q.currency}</span>
                                </span>
                            )}
                        </div>
                    )}
                </div>
            )}

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
                        onClick={(e) => { e.stopPropagation(); onSelectWinner(q.id); }}
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

// --- Group header used to separate lot-included from ignored/not-included lines ---
function ItemGroupHeader({ label, muted }: { label: string; muted?: boolean }) {
    return (
        <div style={{
            padding: '12px 20px 8px 20px',
            display: 'flex',
            alignItems: 'center'
        }}>
            <span style={{
                fontSize: '0.625rem',
                fontWeight: 950,
                textTransform: 'uppercase',
                letterSpacing: '0.08em',
                color: muted ? 'var(--color-text-muted)' : 'var(--color-text-main)'
            }}>
                {label}
            </span>
        </div>
    );
}

// --- One quotation line row. `dimmed` + `reason` render the audit presentation for lines
//     that are not part of the current lot (IGNORED / excluded). ---
function QuotationItemRow({ item, isLast, dimmed, reason }: {
    item: SavedQuotationItemDto;
    isLast: boolean;
    dimmed?: boolean;
    reason?: string | null;
}) {
    return (
        <div style={{
            padding: '10px 0',
            borderBottom: !isLast ? '1px solid var(--color-border)' : 'none',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'flex-start',
            gap: '12px',
            opacity: dimmed ? 0.72 : 1
        }}>
            <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                    <span style={{
                        fontSize: '0.8rem',
                        fontWeight: 700,
                        color: 'var(--color-text-main)',
                        lineHeight: 1.3,
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                    }}>
                        {item.description}
                    </span>
                    {dimmed && (
                        <span style={{
                            flexShrink: 0,
                            fontSize: '0.55rem',
                            fontWeight: 950,
                            textTransform: 'uppercase',
                            letterSpacing: '0.05em',
                            padding: '2px 6px',
                            borderRadius: 'var(--radius-sm)',
                            backgroundColor: 'var(--color-bg-page)',
                            border: '1px solid var(--color-border)',
                            color: 'var(--color-text-muted)'
                        }}>
                            Não incluído neste lote
                        </span>
                    )}
                </div>
                <div style={{
                    fontSize: '0.7rem',
                    fontWeight: 600,
                    color: 'var(--color-text-muted)',
                    marginTop: '2px'
                }}>
                    {item.quantity} {item.unitCode || item.unitName || 'UN'} × {formatCurrencyAO(item.unitPrice)}
                </div>
                {dimmed && reason && (
                    <div style={{
                        fontSize: '0.65rem',
                        fontWeight: 600,
                        color: 'var(--color-text-muted)',
                        marginTop: '4px',
                        fontStyle: 'italic'
                    }}>
                        Motivo: {reason}
                    </div>
                )}
            </div>
            <div style={{
                fontSize: '0.8rem',
                fontWeight: 900,
                color: dimmed ? 'var(--color-text-muted)' : 'var(--color-text-main)',
                whiteSpace: 'nowrap',
                fontVariantNumeric: 'tabular-nums',
                flexShrink: 0,
                textDecoration: dimmed ? 'line-through' : 'none'
            }}>
                {formatCurrencyAO(item.lineTotal)}
            </div>
        </div>
    );
}
