import { AlertCircle, AlertTriangle, Info, Package, TrendingUp, TrendingDown, Minus, CheckCircle2, AlertOctagon, Eye, BarChart3 } from 'lucide-react';
import { ApprovalIntelligenceDto, ItemIntelligenceDto } from '../../../types';
import { DecisionSection } from './DecisionSection';

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
}

// --- Main Component ---

export function DecisionInsightsPanel({ intelligence, approvalStage, requestData }: DecisionInsightsPanelProps) {
    if (!intelligence) return null;

    const { overallAlerts: alerts, departmentContext: dept, items: itemInsights } = intelligence;
    const isArea = approvalStage === 'AREA';

    // --- Shared Section Renderers ---

    const alertsBlock = alerts && alerts.length > 0 ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <SectionLabel>Destaques de Atenção</SectionLabel>
            {alerts.map((alert, idx) => {
                const isCritical = alert.level === 'CRITICAL' || alert.level === 'ERROR' || alert.level === 'DANGER';
                const isWarning = alert.level === 'WARNING';
                const borderColor = isCritical ? 'var(--color-status-red)' : isWarning ? 'var(--color-status-orange)' : 'var(--color-status-blue)';
                
                return (
                    <div key={idx} style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '12px',
                        padding: '10px 16px',
                        backgroundColor: 'var(--color-bg-page)',
                        border: `1px solid var(--color-border)`,
                        borderLeft: `4px solid ${borderColor}`,
                        borderRadius: '0px'
                    }}>
                        <div style={{ color: borderColor, display: 'flex' }}>
                            {isCritical ? <AlertCircle size={16} /> : isWarning ? <AlertTriangle size={16} /> : <Info size={16} />}
                        </div>
                        <span style={{ fontSize: '0.85rem', fontWeight: 600, color: 'black' }}>
                            {alert.message}
                        </span>
                    </div>
                );
            })}
        </div>
    ) : null;

    const departmentBlock = dept ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <SectionLabel>Visão Departamental</SectionLabel>
            <div style={{ 
                display: 'grid', 
                gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', 
                gap: '12px' 
            }}>
                <KpiCard 
                    label="Acumulado Mês" 
                    value={dept.monthAccumulatedTotal.toLocaleString('pt-AO', { style: 'currency', currency: dept.currency || 'AOA' })} 
                />
                <KpiCard 
                    label="Impacto Orçame." 
                    value={`${dept.currentRequestSharePercentage.toFixed(1)}%`} 
                />
            </div>
        </div>
    ) : null;

    const itemsBlock = itemInsights && itemInsights.length > 0 ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <SectionLabel>Análise por Item</SectionLabel>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                {itemInsights.map((item, idx) => (
                    <ItemCard key={idx} item={item} />
                ))}
            </div>
        </div>
    ) : null;

    // --- Role-aware section ordering ---

    const orderedSections = isArea
        ? [alertsBlock, itemsBlock, departmentBlock]       // Area: alerts → items → dept
        : [departmentBlock, alertsBlock, itemsBlock];      // Final: dept → alerts → items

    return (
        <DecisionSection 
            title="Inteligência para Decisão" 
            icon={<Info size={18} />}
            isCollapsible={false}
        >
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                
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
        </DecisionSection>
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
            gap: '10px',
            padding: '8px 14px',
            backgroundColor: isArea ? '#eff6ff' : '#f0fdf4',
            borderLeft: `3px solid ${isArea ? '#3b82f6' : '#22c55e'}`,
        }}>
            <div style={{ display: 'flex', color: isArea ? '#3b82f6' : '#22c55e' }}>
                {isArea ? <Eye size={14} /> : <BarChart3 size={14} />}
            </div>
            <span style={{ 
                fontSize: '0.6rem', 
                fontWeight: 900, 
                textTransform: 'uppercase', 
                letterSpacing: '0.08em',
                color: isArea ? '#1e40af' : '#166534'
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

    // Quotation readiness only relevant for QUOTATION requests
    if (isQuotationType) {
        items.push({
            label: 'Cotação Formalizada',
            ok: hasQuotations,
            detail: hasQuotations ? 'Sim' : 'Nenhuma registrada'
        });
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <SectionLabel>Checklist de Legitimidade</SectionLabel>
            <div style={{
                border: '1px solid var(--color-border)',
                backgroundColor: 'white',
                overflow: 'hidden'
            }}>
                {items.map((item, idx) => (
                    <div key={idx} style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '10px',
                        padding: '10px 16px',
                        borderBottom: idx < items.length - 1 ? '1px solid var(--color-border)' : 'none',
                    }}>
                        <div style={{ display: 'flex', flexShrink: 0 }}>
                            {item.ok ? (
                                <CheckCircle2 size={15} style={{ color: '#22c55e' }} />
                            ) : (
                                <AlertOctagon size={15} style={{ color: '#f59e0b' }} />
                            )}
                        </div>
                        <span style={{ 
                            fontSize: '0.7rem', 
                            fontWeight: 800, 
                            textTransform: 'uppercase', 
                            letterSpacing: '0.02em',
                            color: 'var(--color-text-muted)',
                            minWidth: '110px'
                        }}>
                            {item.label}
                        </span>
                        <span style={{ 
                            fontSize: '0.8rem', 
                            fontWeight: 700, 
                            color: item.ok ? 'black' : '#b45309',
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

    // Consolidated weighted variation: weighted by currentUnitPrice
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
        if (variation > 5) return { color: '#DC2626', icon: <TrendingUp size={14} /> };
        if (variation < -5) return { color: '#16A34A', icon: <TrendingDown size={14} /> };
        return { color: '#6B7280', icon: <Minus size={14} /> };
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <SectionLabel>Visão Financeira Comparativa</SectionLabel>
            <div style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
                gap: '12px'
            }}>
                {/* Year accumulated total */}
                {dept && (
                    <KpiCard
                        label="Acumulado Anual — Depto."
                        value={dept.yearAccumulatedTotal.toLocaleString('pt-AO', { style: 'currency', currency: dept.currency || 'AOA' })}
                    />
                )}

                {/* Total historical purchases */}
                <KpiCard
                    label="Compras Históricas"
                    value={totalPurchaseCount > 0 ? `${totalPurchaseCount} registro${totalPurchaseCount > 1 ? 's' : ''}` : 'Sem histórico'}
                    muted={totalPurchaseCount === 0}
                />

                {/* Consolidated variation */}
                {consolidatedVariation !== null && (
                    <div style={{ 
                        padding: '16px', 
                        border: '2px solid black', 
                        backgroundColor: 'white' 
                    }}>
                        <div style={{ fontSize: '0.6rem', textTransform: 'uppercase', fontWeight: 700, letterSpacing: '0.05em', color: 'var(--color-text-muted)', marginBottom: '4px' }}>
                            Variação Consolidada
                        </div>
                        <div style={{ 
                            display: 'flex', 
                            alignItems: 'center', 
                            gap: '6px',
                            fontSize: '1.25rem', 
                            fontWeight: 900, 
                            color: getVariationStyle(consolidatedVariation).color 
                        }}>
                            {getVariationStyle(consolidatedVariation).icon}
                            {consolidatedVariation.toFixed(1)}%
                        </div>
                        {variationCoverage < 100 && (
                            <div style={{ 
                                fontSize: '0.55rem', 
                                color: 'var(--color-text-muted)', 
                                fontWeight: 600, 
                                marginTop: '4px',
                                fontStyle: 'italic'
                            }}>
                                Base: {variationCoverage.toFixed(0)}% dos itens com histórico
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
            fontSize: '0.65rem', 
            fontWeight: 700, 
            textTransform: 'uppercase', 
            letterSpacing: '0.05em', 
            color: 'var(--color-text-muted)',
            marginBottom: '4px'
        }}>
            {children}
        </div>
    );
}

function KpiCard({ label, value, muted }: { label: string; value: string; muted?: boolean }) {
    return (
        <div style={{ 
            padding: '16px', 
            border: '2px solid black', 
            backgroundColor: 'white' 
        }}>
            <div style={{ fontSize: '0.6rem', textTransform: 'uppercase', fontWeight: 700, letterSpacing: '0.05em', color: 'var(--color-text-muted)', marginBottom: '4px' }}>
                {label}
            </div>
            <div style={{ fontSize: '1.25rem', fontWeight: 900, color: muted ? 'var(--color-text-muted)' : 'black' }}>
                {value}
            </div>
        </div>
    );
}

function ItemCard({ item }: { item: ItemIntelligenceDto }) {
    const hasHistory = item.hasHistory;
    
    function getVariationStyle(variation: number) {
        if (variation > 5) return { color: '#DC2626', icon: <TrendingUp size={12} /> };
        if (variation < -5) return { color: '#16A34A', icon: <TrendingDown size={12} /> };
        return { color: '#6B7280', icon: <Minus size={12} /> };
    }

    const variationStyle = getVariationStyle(item.variationVsAvgPercentage || 0);

    return (
        <div style={{ 
            border: '1px solid var(--color-border)', 
            backgroundColor: 'white',
            padding: '16px'
        }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '12px' }}>
                <span style={{ fontSize: '0.75rem', fontWeight: 900, color: 'black', textTransform: 'uppercase', maxWidth: '70%', lineHeight: '1.2' }}>
                    {item.description}
                </span>
                <span style={{ 
                    fontSize: '0.55rem', 
                    fontWeight: 900, 
                    padding: '2px 6px', 
                    backgroundColor: hasHistory ? 'var(--color-primary)' : 'var(--color-status-gray)',
                    color: 'white',
                    textTransform: 'uppercase',
                    letterSpacing: '0.02em'
                }}>
                    {hasHistory ? `${item.totalPurchaseCount}x Comprado` : 'Novo Item'}
                </span>
            </div>

            {hasHistory ? (
                <div style={{ marginBottom: '12px' }}>
                    <div style={{ 
                        display: 'grid', 
                        gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', 
                        gap: '12px', 
                        marginBottom: '12px' 
                    }}>
                        <div>
                            <div style={{ fontSize: '0.55rem', textTransform: 'uppercase', fontWeight: 700, color: 'var(--color-text-muted)', letterSpacing: '0.03em' }}>Último Preço</div>
                            <div style={{ fontSize: '0.85rem', fontWeight: 900, color: 'black' }}>
                                {item.lastPaidPrice?.toLocaleString('pt-AO', { style: 'currency', currency: item.currency || 'AOA' })}
                            </div>
                        </div>
                        <div>
                            <div style={{ fontSize: '0.55rem', textTransform: 'uppercase', fontWeight: 700, color: 'var(--color-text-muted)', letterSpacing: '0.03em' }}>Variação vs Média</div>
                            <div style={{ 
                                fontSize: '0.85rem', 
                                fontWeight: 900, 
                                color: variationStyle.color,
                                display: 'flex',
                                alignItems: 'center',
                                gap: '4px'
                            }}>
                                {variationStyle.icon}
                                {(item.variationVsAvgPercentage || 0).toFixed(1)}%
                            </div>
                        </div>
                    </div>

                    {item.lastSupplierName && (
                        <div style={{ 
                            display: 'flex', 
                            alignItems: 'center', 
                            gap: '6px', 
                            paddingTop: '8px', 
                            borderTop: '1px dashed var(--color-border)' 
                        }}>
                            <Package size={12} style={{ color: 'var(--color-text-muted)' }} />
                            <span style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                                <span style={{ opacity: 0.6, textTransform: 'uppercase', marginRight: '4px' }}>Forn. anterior:</span>
                                <span style={{ color: 'black', fontWeight: 800 }}>{item.lastSupplierName}</span>
                            </span>
                        </div>
                    )}
                </div>
            ) : (
                <div style={{ 
                    padding: '12px', 
                    backgroundColor: 'var(--color-bg-page)', 
                    border: '1px dashed var(--color-border)',
                    textAlign: 'center'
                }}>
                    <span style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', fontWeight: 700, textTransform: 'uppercase' }}>
                        Sem histórico nesta moeda
                    </span>
                </div>
            )}
        </div>
    );
}
