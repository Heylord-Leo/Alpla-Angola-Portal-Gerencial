import { AlertCircle, AlertTriangle, Info, Package, TrendingUp, TrendingDown, Minus, CheckCircle2, AlertOctagon, Eye, BarChart3, HelpCircle } from 'lucide-react';
import { ApprovalIntelligenceDto, ItemIntelligenceDto } from '../../../types';
import { Tooltip } from '../../../components/ui/Tooltip';

// --- Interfaces ---

interface RequestContextData {
    description?: string;
    supplierName?: string | null;
    costCenterCode?: string | null;
    requestTypeCode?: string;
    hasQuotations: boolean;
}

interface DecisionInsightsPanelProps {
    intelligence: ApprovalIntelligenceDto;
    approvalStage: 'AREA' | 'FINAL';
    requestData?: RequestContextData;
    onDrillDown?: (item: ItemIntelligenceDto) => void;
    isSingleItemFocus?: boolean;
}

// --- Main Component ---

export function DecisionInsightsPanel({ 
    intelligence, 
    approvalStage, 
    requestData, 
    onDrillDown,
    isSingleItemFocus 
}: DecisionInsightsPanelProps) {
    if (!intelligence) return null;

    const { overallAlerts: alerts, departmentContext: dept, items: itemInsights } = intelligence;
    const isArea = approvalStage === 'AREA';

    // --- Shared Section Renderers ---

    const alertsBlock = alerts && alerts.length > 0 ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <SectionLabel>Destaques de Atenção</SectionLabel>
            {alerts.map((alert, idx) => {
                const isCritical = alert.level === 'CRITICAL' || alert.level === 'ERROR' || alert.level === 'DANGER';
                const isWarning = alert.level === 'WARNING';
                const borderColor = isCritical ? 'var(--color-status-red)' : isWarning ? 'var(--color-status-orange)' : 'var(--color-status-blue)';
                
                return (
                    <div key={idx} style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '14px',
                        padding: '12px 16px',
                        backgroundColor: 'var(--color-bg-surface)',
                        border: '1px solid var(--color-border)',
                        borderRadius: 'var(--radius-md)',
                        borderLeft: `4px solid ${borderColor}`,
                        boxShadow: 'var(--shadow-sm)'
                    }}>
                        <div style={{ color: borderColor, display: 'flex' }}>
                            {isCritical ? <AlertCircle size={18} /> : isWarning ? <AlertTriangle size={18} /> : <Info size={18} />}
                        </div>
                        <span style={{ fontSize: '0.85rem', fontWeight: 900, color: 'black', textTransform: 'uppercase', letterSpacing: '-0.01em' }}>
                            {alert.message}
                        </span>
                    </div>
                );
            })}
        </div>
    ) : null;

    const departmentBlock = dept ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <SectionLabel>Visão Departamental</SectionLabel>
            <div style={{ 
                display: 'grid', 
                gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', 
                gap: '16px' 
            }}>
                <KpiCard 
                    label="Acumulado Mês" 
                    value={dept.monthAccumulatedTotal.toLocaleString('pt-AO', { style: 'currency', currency: dept.currency || 'AOA' })} 
                    tooltip="Total de gastos do departamento no mês atual (considerando pedidos já aprovados)."
                />
                <KpiCard 
                    label="Impacto Orçame." 
                    value={`${dept.currentRequestSharePercentage.toFixed(1)}%`} 
                    tooltip="Percentual que este pedido consome do volume total de gastos do mês para o seu departamento."
                />
            </div>
        </div>
    ) : null;

    const itemsBlock = itemInsights && itemInsights.length > 0 ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            {!isSingleItemFocus && <SectionLabel>Análise por Item</SectionLabel>}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {itemInsights.map((item, idx) => (
                    <ItemCard 
                        key={idx} 
                        item={item} 
                        onDrillDown={onDrillDown}
                        isArea={isArea}
                        isFocused={isSingleItemFocus}
                    />
                ))}
            </div>
        </div>
    ) : null;

    // --- Role-aware section ordering ---

    const orderedSections = isArea
        ? [alertsBlock, itemsBlock, departmentBlock]       // Area: alerts → items → dept
        : [departmentBlock, alertsBlock, itemsBlock];      // Final: dept → alerts → items

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '28px' }}>
            
            {/* --- CONTEXT BANNER --- */}
            <ContextBanner approvalStage={approvalStage} />

            {/* --- ROLE-SPECIFIC EMPHASIS BLOCK --- */}
            {isArea ? (
                <AreaEmphasisBlock 
                    intelligence={intelligence} 
                    requestData={requestData} 
                />
            ) : (
                <FinalEmphasisBlock intelligence={intelligence} />
            )}

            {/* --- SHARED SECTIONS (role-ordered) --- */}
            {orderedSections.map((section, idx) => section ? (
                <div key={idx}>{section}</div>
            ) : null)}
        </div>
    );
}

// ====================================
// Sub-Components
// ====================================

// --- Context Banner ---

function ContextBanner({ approvalStage }: { approvalStage: 'AREA' | 'FINAL' }) {
    const isArea = approvalStage === 'AREA';

    return (
        <div style={{
            display: 'flex',
            alignItems: 'center',
            gap: '12px',
            padding: '12px 18px',
            backgroundColor: isArea ? 'var(--color-bg-page)' : 'var(--color-success-muted)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-md)',
            borderLeft: `6px solid ${isArea ? 'var(--color-primary)' : 'var(--color-success)'}`,
            boxShadow: 'var(--shadow-sm)'
        }}>
            <div style={{ display: 'flex', color: 'var(--color-text-main)' }}>
                {isArea ? <Eye size={18} strokeWidth={2.5} /> : <BarChart3 size={18} strokeWidth={2.5} />}
            </div>
            <span style={{ 
                fontSize: '0.7rem', 
                fontWeight: 950, 
                textTransform: 'uppercase', 
                letterSpacing: '0.1em',
                color: 'var(--color-text-main)'
            }}>
                {isArea ? 'Foco: Legitimidade e Necessidade' : 'Foco: Racionalidade Financeira'}
            </span>
        </div>
    );
}

// --- Area Emphasis: Checklist de Legitimidade ---

function AreaEmphasisBlock({ intelligence, requestData }: { 
    intelligence: ApprovalIntelligenceDto; 
    requestData?: RequestContextData;
}) {
    const hasCCMissing = intelligence.overallAlerts?.some(a => a.type === 'CC_MISSING');
    const hasJustification = !!(requestData?.description && requestData.description.trim().length > 0);
    const hasSupplier = !!(requestData?.supplierName);
    const isQuotationType = requestData?.requestTypeCode === 'QUOTATION';
    const hasQuotations = requestData?.hasQuotations ?? false;

    const items: { label: string; ok: boolean; detail: string }[] = [
        {
            label: 'Centro de Custo',
            ok: !hasCCMissing,
            detail: hasCCMissing ? 'Pendente em itens' : 'Atribuído'
        },
        {
            label: 'Justificativa',
            ok: hasJustification,
            detail: hasJustification ? 'Registrada' : 'Não informada'
        },
        {
            label: 'Fornecedor',
            ok: hasSupplier,
            detail: hasSupplier ? requestData!.supplierName! : 'Não informado'
        },
    ];

    if (isQuotationType) {
        items.push({
            label: 'Cotação Formalizada',
            ok: hasQuotations,
            detail: hasQuotations ? 'Sim' : 'Nenhuma registrada'
        });
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <SectionLabel>Checklist de Legitimidade</SectionLabel>
            <div style={{
                border: '1px solid var(--color-border)',
                backgroundColor: 'var(--color-bg-surface)',
                borderRadius: 'var(--radius-lg)',
                overflow: 'hidden',
                boxShadow: 'var(--shadow-sm)'
            }}>
                {items.map((item, idx) => (
                    <div key={idx} style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '12px',
                        padding: '12px 20px',
                        borderBottom: idx < items.length - 1 ? '1px solid var(--color-border)' : 'none',
                    }}>
                        <div style={{ display: 'flex', flexShrink: 0 }}>
                            {item.ok ? (
                                <CheckCircle2 size={16} strokeWidth={3} style={{ color: '#16a34a' }} />
                            ) : (
                                <AlertOctagon size={16} strokeWidth={3} style={{ color: '#f97316' }} />
                            )}
                        </div>
                        <span style={{ 
                            fontSize: '0.75rem', 
                            fontWeight: 900, 
                            textTransform: 'uppercase', 
                            letterSpacing: '0.04em',
                            color: 'black',
                            minWidth: '120px'
                        }}>
                            {item.label}
                        </span>
                        <span style={{ 
                            fontSize: '0.85rem', 
                            fontWeight: 700, 
                            color: item.ok ? 'black' : 'var(--color-status-orange)',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap'
                        }}>
                            {item.detail}
                        </span>
                    </div>
                ))}
            </div>
        </div>
    );
}

// --- Final Emphasis: Visão Financeira Comparativa ---

function FinalEmphasisBlock({ intelligence }: { intelligence: ApprovalIntelligenceDto }) {
    const dept = intelligence.departmentContext;
    const itemsWithHistory = intelligence.items?.filter(i => i.hasHistory) || [];
    const totalItems = intelligence.items?.length || 0;

    let consolidatedVariation: number | null = null;
    let variationCoverage = 0;

    if (itemsWithHistory.length > 0) {
        const totalWeight = itemsWithHistory.reduce((sum, i) => sum + i.currentUnitPrice, 0);
        if (totalWeight > 0) {
            consolidatedVariation = itemsWithHistory.reduce(
                (sum, i) => sum + (i.variationVsAvgPercentage || 0) * i.currentUnitPrice, 0
            ) / totalWeight;
        }
        variationCoverage = totalItems > 0 ? (itemsWithHistory.length / totalItems) * 100 : 0;
    }

    const totalPurchaseCount = itemsWithHistory.reduce((sum, i) => sum + i.totalPurchaseCount, 0);

    function getVariationStyle(variation: number) {
        if (variation > 5) return { color: 'var(--color-status-red)', icon: <TrendingUp size={16} /> };
        if (variation < -5) return { color: 'var(--color-status-green)', icon: <TrendingDown size={16} /> };
        return { color: 'var(--color-text-muted)', icon: <Minus size={16} /> };
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <SectionLabel>Visão Financeira Comparativa</SectionLabel>
            <div style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
                gap: '16px'
            }}>
                {dept && (
                    <KpiCard
                        label="Acumulado Anual — Depto."
                        value={dept.yearAccumulatedTotal.toLocaleString('pt-AO', { style: 'currency', currency: dept.currency || 'AOA' })}
                        tooltip="Soma de todos os gastos aprovados para este departamento no ano civil corrente."
                    />
                )}

                <KpiCard
                    label="Compras Históricas"
                    value={totalPurchaseCount > 0 ? `${totalPurchaseCount} registro${totalPurchaseCount > 1 ? 's' : ''}` : 'Sem histórico'}
                    muted={totalPurchaseCount === 0}
                    tooltip="Quantidade de vezes que itens com a mesma descrição foram comprados anteriormente no portal."
                />

                {consolidatedVariation !== null && (
                    <div style={{ 
                        padding: '20px', 
                        border: '1px solid var(--color-border)', 
                        borderRadius: 'var(--radius-lg)',
                        backgroundColor: 'var(--color-bg-surface)',
                        boxShadow: 'var(--shadow-sm)'
                    }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px', marginBottom: '8px' }}>
                            <div style={{ fontSize: '0.65rem', textTransform: 'uppercase', fontWeight: 900, letterSpacing: '0.1em', color: 'var(--color-text-muted)' }}>
                                Variação Consolidada
                            </div>
                            <Tooltip content="Média ponderada da variação de preço dos itens deste pedido em relação aos preços médios praticados no histórico.">
                                <HelpCircle size={12} style={{ color: '#9ca3af', cursor: 'help' }} />
                            </Tooltip>
                        </div>
                        <div style={{ 
                            display: 'flex', 
                            alignItems: 'center', 
                            gap: '8px',
                            fontSize: '1.5rem', 
                            fontWeight: 950, 
                            color: getVariationStyle(consolidatedVariation).color 
                        }}>
                            {getVariationStyle(consolidatedVariation).icon}
                            {consolidatedVariation.toFixed(1)}%
                        </div>
                        {variationCoverage < 100 && (
                            <div style={{ 
                                fontSize: '0.65rem', 
                                color: 'var(--color-text-muted)', 
                                fontWeight: 800, 
                                marginTop: '6px',
                                textTransform: 'uppercase',
                                letterSpacing: '0.02em'
                            }}>
                                Base: {variationCoverage.toFixed(0)}% dos itens
                            </div>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}

// ====================================
// Reusable Primitives
// ====================================

function SectionLabel({ children }: { children: React.ReactNode }) {
    return (
        <div style={{ 
            fontSize: '0.7rem', 
            fontWeight: 950, 
            textTransform: 'uppercase', 
            letterSpacing: '0.15em', 
            color: 'var(--color-text-main)',
            marginBottom: '6px',
            display: 'flex',
            alignItems: 'center',
            gap: '8px'
        }}>
            <div style={{ width: '4px', height: '12px', backgroundColor: 'var(--color-primary)' }} />
            {children}
        </div>
    );
}

function KpiCard({ label, value, muted, tooltip }: { label: string; value: string; muted?: boolean; tooltip?: string }) {
    const cardContent = (
        <div style={{ 
            padding: '20px', 
            border: '1px solid var(--color-border)', 
            borderRadius: 'var(--radius-lg)',
            backgroundColor: 'var(--color-bg-surface)',
            boxShadow: 'var(--shadow-sm)',
            height: '100%'
        }}>
            <div style={{ 
                display: 'flex', 
                alignItems: 'center', 
                gap: '6px', 
                marginBottom: '8px' 
            }}>
                <div style={{ fontSize: '0.65rem', textTransform: 'uppercase', fontWeight: 900, letterSpacing: '0.1em', color: 'var(--color-text-muted)' }}>
                    {label}
                </div>
                {tooltip && <HelpCircle size={12} style={{ color: '#9ca3af' }} />}
            </div>
            <div style={{ fontSize: '1.5rem', fontWeight: 950, color: muted ? 'var(--color-text-muted)' : 'var(--color-text-main)', fontVariantNumeric: 'tabular-nums' }}>
                {value}
            </div>
        </div>
    );

    if (tooltip) {
        return (
            <Tooltip content={tooltip}>
                {cardContent}
            </Tooltip>
        );
    }

    return cardContent;
}

function ItemCard({ 
    item, 
    onDrillDown, 
    isArea,
    isFocused 
}: { 
    item: ItemIntelligenceDto; 
    onDrillDown?: (item: ItemIntelligenceDto) => void, 
    isArea: boolean;
    isFocused?: boolean;
}) {
    const hasHistory = item.hasHistory;
    
    function getVariationStyle(variation: number) {
        if (variation > 5) return { color: 'var(--color-status-red)', icon: <TrendingUp size={14} /> };
        if (variation < -5) return { color: 'var(--color-status-green)', icon: <TrendingDown size={14} /> };
        return { color: 'var(--color-text-muted)', icon: <Minus size={14} /> };
    }

    const variationStyle = getVariationStyle(item.variationVsAvgPercentage || 0);

    return (
        <div style={{ 
            border: isFocused ? '2px solid var(--color-primary)' : '1px solid var(--color-border)', 
            borderRadius: 'var(--radius-lg)',
            backgroundColor: 'var(--color-bg-surface)',
            padding: isFocused ? '24px' : '20px',
            position: 'relative',
            boxShadow: isFocused ? 'var(--shadow-md)' : 'var(--shadow-sm)',
        }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '16px' }}>
                <span style={{ fontSize: '0.9rem', fontWeight: 950, color: 'var(--color-text-main)', textTransform: 'uppercase', maxWidth: '70%', lineHeight: '1.1', letterSpacing: '-0.02em' }}>
                    {item.description}
                </span>
                <span style={{ 
                    fontSize: '0.6rem', 
                    fontWeight: 950, 
                    padding: '3px 8px', 
                    backgroundColor: hasHistory ? 'var(--color-text-main)' : 'var(--color-bg-page)',
                    border: '1px solid var(--color-border)',
                    borderRadius: 'var(--radius-sm)',
                    color: hasHistory ? 'white' : 'var(--color-text-main)',
                    textTransform: 'uppercase',
                    letterSpacing: '0.05em'
                }}>
                    {hasHistory ? `${item.totalPurchaseCount}x Comprado` : 'Novo Item'}
                </span>
            </div>

            {hasHistory ? (
                <div style={{ marginBottom: '4px' }}>
                    <div style={{ 
                        display: 'grid', 
                        gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', 
                        gap: '16px', 
                        marginBottom: '16px' 
                    }}>
                        <div>
                            <div style={{ fontSize: '0.65rem', textTransform: 'uppercase', fontWeight: 900, color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>Último Preço</div>
                            <div style={{ fontSize: '1rem', fontWeight: 950, color: 'var(--color-text-main)', fontVariantNumeric: 'tabular-nums' }}>
                                {item.lastPaidPrice?.toLocaleString('pt-AO', { style: 'currency', currency: item.currency || 'AOA' })}
                            </div>
                        </div>
                        <div>
                            <div style={{ fontSize: '0.65rem', textTransform: 'uppercase', fontWeight: 900, color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>Variação vs Média</div>
                            <div style={{ 
                                fontSize: '1rem', 
                                fontWeight: 950, 
                                color: variationStyle.color,
                                display: 'flex',
                                alignItems: 'center',
                                gap: '6px'
                            }}>
                                {variationStyle.icon}
                                {(item.variationVsAvgPercentage || 0).toFixed(1)}%
                            </div>
                        </div>
                    </div>

                    <div style={{ 
                        display: 'flex', 
                        alignItems: 'center', 
                        justifyContent: 'space-between',
                        paddingTop: '12px', 
                        borderTop: '1px solid var(--color-border)' 
                    }}>
                        {item.lastSupplierName && (
                            <div style={{ 
                                display: 'flex', 
                                alignItems: 'center', 
                                gap: '8px' 
                            }}>
                                <Package size={14} style={{ color: 'var(--color-text-main)' }} />
                                <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 800 }}>
                                    <span style={{ opacity: 0.6, textTransform: 'uppercase', marginRight: '6px' }}>Forn. anterior:</span>
                                    <span style={{ color: 'var(--color-text-main)', fontWeight: 950 }}>{item.lastSupplierName}</span>
                                </span>
                            </div>
                        )}

                        {onDrillDown && (
                            <button 
                                onClick={(e) => {
                                    e.stopPropagation();
                                    onDrillDown(item);
                                }}
                                style={{
                                    fontSize: '0.65rem',
                                    fontWeight: 950,
                                    textTransform: 'uppercase',
                                    color: 'white',
                                    backgroundColor: 'var(--color-text-main)',
                                    border: 'none',
                                    borderRadius: 'var(--radius-sm)',
                                    padding: '6px 12px',
                                    cursor: 'pointer',
                                    boxShadow: 'var(--shadow-sm)',
                                    letterSpacing: '0.05em'
                                }}
                                onMouseOver={(e) => { e.currentTarget.style.transform = 'translateY(-2px)'; e.currentTarget.style.boxShadow = 'var(--shadow-md)'; }}
                                onMouseOut={(e) => { e.currentTarget.style.transform = ''; e.currentTarget.style.boxShadow = 'var(--shadow-sm)'; }}
                            >
                                {isArea ? 'Analisar Histórico' : 'Ver Detalhes'}
                            </button>
                        )}
                    </div>
                </div>
            ) : (
                <div style={{ 
                    padding: '14px', 
                    backgroundColor: 'var(--color-bg-page)', 
                    border: '1px dashed var(--color-border)',
                    borderRadius: 'var(--radius-md)',
                    textAlign: 'center',
                    boxShadow: 'none'
                }}>
                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                        Sem histórico nesta moeda
                    </span>
                </div>
            )}
        </div>
    );
}
