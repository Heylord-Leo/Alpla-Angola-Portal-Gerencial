import { useState } from 'react';
import { AlertCircle, AlertTriangle, Info, Package, TrendingUp, TrendingDown, Minus, CheckCircle2, AlertOctagon, Eye, BarChart3, HelpCircle, Wallet, ChevronDown, ChevronRight } from 'lucide-react';
import { ApprovalIntelligenceDto, ItemIntelligenceDto, BudgetAvailabilityDto, DepartmentCostCenterBudgetDto } from '../../../types';
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
    /** Area Approver's budget justification (batch approved with a critical/
     *  over-budget cost center). Rendered inside "Disponibilidade Orçamental".
     *  The caller decides when to pass it (e.g. FINAL stage only). */
    budgetJustification?: string | null;
    /** Display name of who recorded the justification (Area Approver). */
    budgetJustificationAuthor?: string | null;
    /** Pre-formatted display date of when the justification was recorded. */
    budgetJustificationDate?: string | null;
    /** Batch-specific checklist for the FINAL stage — replaces the area-oriented
     *  "Checklist de Legitimidade" concepts (request-level supplier, CC pending)
     *  that don't apply once a batch reached final approval. */
    batchChecklist?: {
        batchNumber: number;
        itemCount: number;
        areaApproved: boolean;
        winnersDefined: boolean;
        allocationDefined: boolean;
        budgetJustificationRegistered: boolean;
    };
}

// --- Main Component ---

export function DecisionInsightsPanel({
    intelligence,
    approvalStage,
    requestData,
    onDrillDown,
    isSingleItemFocus,
    budgetJustification,
    budgetJustificationAuthor,
    budgetJustificationDate,
    batchChecklist
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

    // The Area Approver's budget justification belongs with the budget analysis
    // itself — rendered right below the availability KPIs. When no budget data
    // is available but a justification exists, the section still renders so the
    // note is never lost.
    const justificationNote = budgetJustification ? (
        <BudgetJustificationNote
            text={budgetJustification}
            author={budgetJustificationAuthor}
            date={budgetJustificationDate}
        />
    ) : null;

    const budgetBlock = (intelligence.budgetAvailability || justificationNote) ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            {intelligence.budgetAvailability
                ? <BudgetAvailabilityBlock budget={intelligence.budgetAvailability} isBatchScoped={intelligence.scope === 'BATCH'} />
                : <SectionLabel>Disponibilidade Orçamental</SectionLabel>}
            {justificationNote}
        </div>
    ) : null;

    // --- Role-aware section ordering ---
    // Budget availability placed after dept context and before alerts

    const orderedSections = isArea
        ? [alertsBlock, budgetBlock, itemsBlock, departmentBlock]       // Area: alerts → budget → items → dept
        : [departmentBlock, budgetBlock, alertsBlock, itemsBlock];      // Final: dept → budget → alerts → items

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
                <>
                    {batchChecklist && <BatchFinalChecklistBlock checklist={batchChecklist} />}
                    <FinalEmphasisBlock intelligence={intelligence} />
                </>
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

// --- Final Emphasis: Checklist da Aprovação Final (batch model) ---

// Batch-oriented replacement for the area "Checklist de Legitimidade": by the
// time a batch reaches final approval, allocation and winners were enforced at
// area approval — the Final Approver validates the DECISION, not the request's
// legitimacy. Rendered only when the caller provides batch data (FINAL + batch).
function BatchFinalChecklistBlock({ checklist }: {
    checklist: NonNullable<DecisionInsightsPanelProps['batchChecklist']>;
}) {
    const rows: { label: string; ok: boolean; detail: string }[] = [
        {
            label: 'Cotação aprovada pela área',
            ok: checklist.areaApproved,
            detail: checklist.areaApproved ? 'Sim' : 'Pendente'
        },
        {
            label: 'Fornecedor vencedor',
            ok: checklist.winnersDefined,
            detail: checklist.winnersDefined ? 'Definido' : 'Pendente'
        },
        {
            label: 'Atribuição financeira',
            ok: checklist.allocationDefined,
            detail: checklist.allocationDefined ? 'Definida' : 'Pendente'
        },
        {
            label: 'Justificativa orçamental',
            ok: true,
            detail: checklist.budgetJustificationRegistered
                ? 'Registrada — ver Disponibilidade Orçamental'
                : 'Não necessária'
        },
        {
            label: 'Itens do lote',
            ok: checklist.itemCount > 0,
            detail: `Lote #${checklist.batchNumber} — ${checklist.itemCount} ${checklist.itemCount === 1 ? 'item' : 'itens'}`
        },
    ];

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <SectionLabel>Checklist da Aprovação Final</SectionLabel>
            <div style={{
                border: '1px solid var(--color-border)',
                backgroundColor: 'var(--color-bg-surface)',
                borderRadius: 'var(--radius-lg)',
                overflow: 'hidden',
                boxShadow: 'var(--shadow-sm)'
            }}>
                {rows.map((row, idx) => (
                    <div key={idx} style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '12px',
                        padding: '12px 20px',
                        borderBottom: idx < rows.length - 1 ? '1px solid var(--color-border)' : 'none',
                    }}>
                        <div style={{ display: 'flex', flexShrink: 0 }}>
                            {row.ok ? (
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
                            minWidth: '200px'
                        }}>
                            {row.label}
                        </span>
                        <span style={{
                            fontSize: '0.85rem',
                            fontWeight: 700,
                            color: row.ok ? 'black' : 'var(--color-status-orange)',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap'
                        }}>
                            {row.detail}
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
// Budget Justification Note
// ====================================

// Discreet amber note shown inside "Disponibilidade Orçamental": the Area
// Approver's mandatory justification when the batch was approved with a
// critical/over-budget cost center.
function BudgetJustificationNote({ text, author, date }: { text: string; author?: string | null; date?: string | null }) {
    return (
        <div style={{
            display: 'flex',
            alignItems: 'flex-start',
            gap: '10px',
            padding: '12px 14px',
            backgroundColor: '#fffbeb',
            border: '1px solid #fde68a',
            borderLeft: '4px solid #f59e0b',
            borderRadius: 'var(--radius-md)'
        }}>
            <AlertTriangle size={14} style={{ color: '#b45309', flexShrink: 0, marginTop: '2px' }} />
            <div style={{ minWidth: 0 }}>
                <div style={{ fontSize: '0.65rem', fontWeight: 950, textTransform: 'uppercase', letterSpacing: '0.08em', color: '#92400e' }}>
                    Justificativa Orçamental
                </div>
                <div style={{ fontSize: '0.65rem', fontWeight: 700, color: '#a16207', marginTop: '2px' }}>
                    Registrada durante a aprovação de área.
                </div>
                <div style={{ fontSize: '0.8rem', fontWeight: 600, color: '#78350f', lineHeight: 1.5, marginTop: '8px', whiteSpace: 'pre-wrap' }}>
                    {text}
                </div>
                {(author || date) && (
                    <div style={{ fontSize: '0.65rem', fontWeight: 700, color: '#a16207', marginTop: '8px' }}>
                        Registrada por {author || 'Aprovador de Área'}{date ? ` em ${date}` : ''}.
                    </div>
                )}
            </div>
        </div>
    );
}

// ====================================
// Budget Availability Block
// ====================================

function BudgetAvailabilityBlock({ budget, isBatchScoped }: { budget: BudgetAvailabilityDto; isBatchScoped?: boolean }) {
    const [showCCDetail, setShowCCDetail] = useState(false);

    const statusColors: Record<string, string> = {
        OK: 'var(--color-success, #22c55e)',
        WARNING: 'var(--color-status-orange, #f59e0b)',
        CRITICAL: 'var(--color-status-red, #ef4444)',
        EXCEEDED: 'var(--color-status-red, #dc2626)'
    };

    const statusColor = statusColors[budget.status] || statusColors.OK;

    const fmtCurrency = (v: number) => v.toLocaleString('pt-AO', { style: 'currency', currency: budget.currencyCode || 'AOA' });

    // Fallback: no budget configured or info-only message
    if (!budget.hasBudgetConfig || budget.matchLevel === 'NONE') {
        return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                <SectionLabel>Disponibilidade Orçamental</SectionLabel>
                <div style={{
                    display: 'flex', alignItems: 'center', gap: '14px',
                    padding: '14px 18px',
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px dashed var(--color-border)',
                    borderRadius: 'var(--radius-md)',
                    borderLeft: budget.status === 'WARNING' ? '4px solid var(--color-status-orange)' : '4px solid var(--color-border)'
                }}>
                    <Info size={16} style={{ color: budget.status === 'WARNING' ? 'var(--color-status-orange)' : 'var(--color-text-muted)', flexShrink: 0 }} />
                    <span style={{ fontSize: '0.8rem', fontWeight: 800, color: 'var(--color-text-main)' }}>
                        {budget.infoMessage || 'Orçamento não configurado para esta combinação.'}
                    </span>
                </div>
                {budget.departmentCostCenters && budget.departmentCostCenters.length > 0 && (
                    <DepartmentCostCentersBlock
                        costCenters={budget.departmentCostCenters}
                        currencyCode={budget.currencyCode}
                        isBatchScoped={isBatchScoped}
                    />
                )}
            </div>
        );
    }

    const utilBarWidth = Math.min(budget.utilizationPercent, 100);

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <SectionLabel>Disponibilidade Orçamental</SectionLabel>

            {/* Utilization Bar */}
            <div style={{
                padding: '16px 18px',
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-lg)',
                backgroundColor: 'var(--color-bg-surface)',
                boxShadow: 'var(--shadow-sm)'
            }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <Wallet size={14} style={{ color: statusColor }} />
                        <span style={{ fontSize: '0.65rem', fontWeight: 950, textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--color-text-muted)' }}>
                            {budget.costCenterName || 'Orçamento Geral'}
                        </span>
                    </div>
                    <span style={{
                        fontSize: '0.7rem', fontWeight: 950,
                        color: statusColor,
                        textTransform: 'uppercase', letterSpacing: '0.05em'
                    }}>
                        {budget.utilizationPercent.toFixed(1)}% utilizado
                    </span>
                </div>

                {/* Progress bar */}
                <div style={{
                    width: '100%', height: '8px',
                    backgroundColor: 'var(--color-bg-page)',
                    borderRadius: '4px', overflow: 'hidden'
                }}>
                    <div style={{
                        width: `${utilBarWidth}%`, height: '100%',
                        backgroundColor: statusColor,
                        borderRadius: '4px',
                        transition: 'width 0.6s ease',
                        animation: budget.status === 'EXCEEDED' ? 'budgetPulse 1.5s ease-in-out infinite' : undefined
                    }} />
                </div>

                {/* Context line */}
                <div style={{
                    display: 'flex', flexWrap: 'wrap', gap: '8px', marginTop: '10px',
                    fontSize: '0.6rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.03em'
                }}>
                    {budget.departmentName && <span>Depto: {budget.departmentName}</span>}
                    {budget.departmentName && budget.plantName && <span>·</span>}
                    {budget.plantName && <span>Planta: {budget.plantName}</span>}
                    <span>·</span>
                    <span>Ano: {budget.fiscalYear}</span>
                </div>
            </div>

            {/* KPI Grid: 3x2 */}
            <div style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(3, 1fr)',
                gap: '10px'
            }}>
                <BudgetKpi label="Orçamento Anual" value={fmtCurrency(budget.annualBudget)} />
                <BudgetKpi label="Comprometido" value={fmtCurrency(budget.committedAmount)} />
                <BudgetKpi label="Disponível Antes" value={fmtCurrency(budget.availableBefore)} />
                <BudgetKpi label={isBatchScoped ? 'Valor do Lote' : 'Valor do Pedido'} value={fmtCurrency(budget.currentRequestAmount)} highlight />
                <BudgetKpi
                    label="Disponível Após"
                    value={fmtCurrency(budget.availableAfter)}
                    status={budget.status}
                />
                <BudgetKpi
                    label="Status"
                    value={budget.status === 'OK' ? 'Dentro do Orçamento'
                         : budget.status === 'WARNING' ? 'Atenção'
                         : budget.status === 'CRITICAL' ? 'Crítico'
                         : 'Excedido'}
                    status={budget.status}
                />
            </div>

            {/* Multi-CC breakdown toggle */}
            {budget.matchLevel === 'MULTI_CC' && budget.costCenterBreakdown && budget.costCenterBreakdown.length > 0 && (
                <div>
                    <button
                        onClick={() => setShowCCDetail(!showCCDetail)}
                        style={{
                            display: 'flex', alignItems: 'center', gap: '6px',
                            background: 'none', border: 'none', cursor: 'pointer',
                            fontSize: '0.65rem', fontWeight: 950, textTransform: 'uppercase',
                            color: 'var(--color-primary)', letterSpacing: '0.05em', padding: '4px 0'
                        }}
                    >
                        {showCCDetail ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                        {showCCDetail ? 'Ocultar Detalhe por CC' : 'Ver Detalhe por CC'}
                    </button>
                    {showCCDetail && (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '6px' }}>
                            {budget.costCenterBreakdown.map((cc, idx) => (
                                <div key={idx} style={{
                                    padding: '10px 14px',
                                    border: '1px solid var(--color-border)',
                                    borderRadius: 'var(--radius-md)',
                                    backgroundColor: 'var(--color-bg-surface)',
                                    borderLeft: `3px solid ${statusColors[cc.status] || statusColors.OK}`
                                }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
                                        <span style={{ fontSize: '0.7rem', fontWeight: 950, color: 'var(--color-text-main)' }}>
                                            {cc.costCenterName}
                                        </span>
                                        <span style={{
                                            fontSize: '0.6rem', fontWeight: 950, textTransform: 'uppercase',
                                            color: statusColors[cc.status] || statusColors.OK
                                        }}>
                                            {cc.hasBudgetLine ? `${cc.utilizationPercent.toFixed(1)}%` : 'Sem Orçamento'}
                                        </span>
                                    </div>
                                    {cc.hasBudgetLine && (
                                        <div style={{ display: 'flex', gap: '16px', fontSize: '0.65rem', color: 'var(--color-text-muted)', fontWeight: 800 }}>
                                            <span>Orçamento: {fmtCurrency(cc.annualBudget)}</span>
                                            <span>Pedido: {fmtCurrency(cc.requestAmountInCC)}</span>
                                            <span style={{ color: statusColors[cc.status] || statusColors.OK, fontWeight: 950 }}>Após: {fmtCurrency(cc.availableAfter)}</span>
                                        </div>
                                    )}
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            )}

            {/* Department cost centers — read-only overview */}
            {budget.departmentCostCenters && budget.departmentCostCenters.length > 0 && (
                <DepartmentCostCentersBlock
                    costCenters={budget.departmentCostCenters}
                    currencyCode={budget.currencyCode}
                    isBatchScoped={isBatchScoped}
                />
            )}

            {/* Pulse animation for EXCEEDED */}
            {budget.status === 'EXCEEDED' && (
                <style>{`
                    @keyframes budgetPulse {
                        0%, 100% { opacity: 1; }
                        50% { opacity: 0.6; }
                    }
                `}</style>
            )}
        </div>
    );
}

/**
 * Read-only overview of the department's cost centers inside "Disponibilidade
 * Orçamental": every budgeted CC plus scope-used CCs without budget, with the
 * ones used by the active batch highlighted. Informational only — no editing,
 * no selection, no CC switching.
 */
function DepartmentCostCentersBlock({ costCenters, currencyCode, isBatchScoped }: {
    costCenters: DepartmentCostCenterBudgetDto[];
    currencyCode: string;
    isBatchScoped?: boolean;
}) {
    const [expanded, setExpanded] = useState(false);

    const statusColors: Record<string, string> = {
        OK: 'var(--color-success, #22c55e)',
        WARNING: 'var(--color-status-orange, #f59e0b)',
        CRITICAL: 'var(--color-status-red, #ef4444)',
        EXCEEDED: 'var(--color-status-red, #dc2626)'
    };
    const statusLabels: Record<string, string> = {
        OK: 'Disponível',
        WARNING: 'Atenção',
        CRITICAL: 'Crítico',
        EXCEEDED: 'Excedido'
    };

    const fmtCurrency = (v: number) => v.toLocaleString('pt-AO', { style: 'currency', currency: currencyCode || 'AOA' });
    const usedBadgeLabel = isBatchScoped ? 'Selecionado neste lote' : 'Utilizado neste pedido';

    return (
        <div>
            <button
                onClick={() => setExpanded(!expanded)}
                style={{
                    display: 'flex', alignItems: 'center', gap: '6px',
                    background: 'none', border: 'none', cursor: 'pointer',
                    fontSize: '0.65rem', fontWeight: 950, textTransform: 'uppercase',
                    color: 'var(--color-primary)', letterSpacing: '0.05em', padding: '4px 0'
                }}
            >
                {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                Centros de custo do departamento ({costCenters.length})
            </button>
            {expanded && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '6px' }}>
                    {costCenters.map((cc, idx) => {
                        const color = cc.hasBudgetConfigured
                            ? (statusColors[cc.status] || statusColors.OK)
                            : 'var(--color-text-muted)';
                        return (
                            <div key={cc.costCenterId ?? `general-${idx}`} style={{
                                padding: '10px 14px',
                                border: cc.isUsedInScope
                                    ? '1px solid var(--color-primary)'
                                    : '1px solid var(--color-border)',
                                borderRadius: 'var(--radius-md)',
                                backgroundColor: cc.isUsedInScope
                                    ? 'var(--color-primary-soft, rgba(59,130,246,0.06))'
                                    : 'var(--color-bg-surface)',
                                borderLeft: `3px solid ${color}`
                            }}>
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '8px', flexWrap: 'wrap', marginBottom: '6px' }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                                        {cc.isUsedInScope && (
                                            <span style={{
                                                fontSize: '0.55rem', fontWeight: 950, textTransform: 'uppercase',
                                                letterSpacing: '0.05em', padding: '2px 8px', borderRadius: '999px',
                                                backgroundColor: 'var(--color-primary)', color: '#fff'
                                            }}>
                                                {usedBadgeLabel}
                                            </span>
                                        )}
                                        <span style={{ fontSize: '0.7rem', fontWeight: 950, color: 'var(--color-text-main)' }}>
                                            {cc.costCenterName}{cc.plantName ? ` / ${cc.plantName}` : ''}
                                        </span>
                                    </div>
                                    <span style={{
                                        fontSize: '0.6rem', fontWeight: 950, textTransform: 'uppercase',
                                        color
                                    }}>
                                        {cc.hasBudgetConfigured
                                            ? `${statusLabels[cc.status] || cc.status} · ${cc.utilizationPercent.toFixed(1)}%`
                                            : 'Sem orçamento configurado'}
                                    </span>
                                </div>
                                {cc.hasBudgetConfigured && (
                                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '16px', fontSize: '0.65rem', color: 'var(--color-text-muted)', fontWeight: 800 }}>
                                        <span>Orçamento anual: {fmtCurrency(cc.annualBudget)}</span>
                                        <span>Comprometido: {fmtCurrency(cc.committedAmount)}</span>
                                        <span style={{ color, fontWeight: 950 }}>Disponível: {fmtCurrency(cc.availableAmount)}</span>
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
            )}
        </div>
    );
}

function BudgetKpi({ label, value, status, highlight }: {
    label: string; value: string; status?: string; highlight?: boolean;
}) {
    const statusColors: Record<string, string> = {
        WARNING: 'var(--color-status-orange)',
        CRITICAL: 'var(--color-status-red)',
        EXCEEDED: 'var(--color-status-red)'
    };
    const valueColor = status && statusColors[status]
        ? statusColors[status]
        : highlight ? 'var(--color-primary)' : 'var(--color-text-main)';

    return (
        <div style={{
            padding: '12px 14px',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-md)',
            backgroundColor: 'var(--color-bg-surface)',
            boxShadow: 'var(--shadow-sm)'
        }}>
            <div style={{ fontSize: '0.55rem', textTransform: 'uppercase', fontWeight: 900, letterSpacing: '0.08em', color: 'var(--color-text-muted)', marginBottom: '4px' }}>
                {label}
            </div>
            <div style={{ fontSize: '0.85rem', fontWeight: 950, color: valueColor, fontVariantNumeric: 'tabular-nums', lineHeight: 1.2 }}>
                {value}
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
