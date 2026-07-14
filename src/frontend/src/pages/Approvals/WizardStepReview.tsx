import React, { useMemo } from 'react';
import { RequestDetailsDto, SavedQuotationDto } from '../../types';
import { ItemAssignment } from './WizardStepAllocation';
import { formatCurrencyAO } from '../../lib/utils';
import { AlertCircle, CheckCircle2, Package, Landmark, Factory, MessageSquare, ShieldCheck, XCircle, ShieldAlert } from 'lucide-react';

interface WizardStepReviewProps {
    request: RequestDetailsDto;
    quotations: SavedQuotationDto[];
    itemAwards: Record<string, string>;
    itemAssignments: Record<string, ItemAssignment>;
    itemAllocations?: Record<string, any[]>;
    comment: string;
    onChangeComment: (comment: string) => void;
    plants: { id: number, name: string }[];
    costCenters: { id: number, code: string, name: string, plantId?: number }[];
    confirmReview: boolean;
    onChangeConfirmReview: (value: boolean) => void;
    isStep2Valid: boolean;
    isStep3Valid: boolean;
    isStep4Valid: boolean;
    reassignments?: import('./WizardStepBudget').AllocationReassignmentDto[];
    extraItems?: any[];
    budgetJustification?: string;
    budgetPreview?: any;
}

interface SupplierGroup {
    supplierName: string;
    currency: string;
    quotationDocNumber: string;
    items: {
        lineNumber: number;
        description: string;
        quantity: number;
        unit: string;
        unitPrice: number;
        lineTotal: number;
        isExtra?: boolean;
    }[];
    totalAmount: number;
}

export const WizardStepReview: React.FC<WizardStepReviewProps> = ({
    request,
    quotations,
    itemAwards,
    itemAssignments,
    itemAllocations,
    comment,
    onChangeComment,
    plants,
    costCenters,
    confirmReview,
    onChangeConfirmReview,
    isStep2Valid,
    isStep3Valid,
    isStep4Valid,
    reassignments,
    extraItems = [],
    budgetJustification,
    budgetPreview
}) => {
    const activeItems = request.lineItems?.filter((i: any) => !i.isDeleted) || [];

    // Helper to resolve item amount
    const resolveItemAmount = (item: any) => {
        let itemAmount = Number(
            item.lineTotal ??
            item.totalAmount ??
            item.total ??
            item.quotationTotal ??
            0
        );
        const awardedQItemId = itemAwards[item.id];
        if (awardedQItemId) {
            const quotation = quotations.find(q => q.items?.some(qi => qi.id === awardedQItemId));
            const qItem = quotation?.items.find(qi => qi.id === awardedQItemId);
            if (qItem) itemAmount = qItem.lineTotal;
        } else if (request.selectedQuotationId) {
            const selectedQ = quotations.find(q => q.id === request.selectedQuotationId);
            const qItem = selectedQ?.items.find(qi => qi?.lineNumber === item.lineNumber);
            if (qItem) itemAmount = qItem.lineTotal;
        }
        return itemAmount;
    };

    // Build supplier groups
    const supplierGroups = useMemo((): SupplierGroup[] => {
        const groups: Record<string, SupplierGroup> = {};

        activeItems.forEach((item: any) => {
            const awardedQItemId = itemAwards[item.id];
            if (!awardedQItemId) return;

            const quotation = quotations.find(q => q.items?.some(qi => qi.id === awardedQItemId));
            if (!quotation) return;

            const qItem = quotation.items.find(qi => qi.id === awardedQItemId);
            if (!qItem) return;

            const groupKey = `${quotation.supplierId || quotation.supplierNameSnapshot}_${quotation.currency}`;

            if (!groups[groupKey]) {
                groups[groupKey] = {
                    supplierName: quotation.supplierNameSnapshot,
                    currency: quotation.currency,
                    quotationDocNumber: quotation.documentNumber || 'S/N',
                    items: [],
                    totalAmount: 0
                };
            }

            groups[groupKey].items.push({
                lineNumber: item.lineNumber,
                description: item.description,
                quantity: qItem.quantity,
                unit: qItem.unitName || qItem.unitCode || item.unit || 'UN',
                unitPrice: qItem.unitPrice,
                lineTotal: qItem.lineTotal
            });
            groups[groupKey].totalAmount += qItem.lineTotal;
        });

        extraItems.forEach((extraItem: any) => {
            const quotation = quotations.find(q => q.items?.some(qi => qi.id === (extraItem.selectedQuotationItemId || extraItem.id)));
            if (!quotation) return;

            const groupKey = `${quotation.supplierId || quotation.supplierNameSnapshot}_${quotation.currency}`;

            if (!groups[groupKey]) {
                groups[groupKey] = {
                    supplierName: quotation.supplierNameSnapshot,
                    currency: quotation.currency,
                    quotationDocNumber: quotation.documentNumber || 'S/N',
                    items: [],
                    totalAmount: 0
                };
            }

            const extraItemAmount = resolveItemAmount(extraItem);

            groups[groupKey].items.push({
                lineNumber: 999,
                description: extraItem.description,
                quantity: 1,
                unit: 'UN',
                unitPrice: extraItemAmount,
                lineTotal: extraItemAmount,
                isExtra: true
            });
            groups[groupKey].totalAmount += extraItemAmount;
        });

        let result = Object.values(groups);

        // Fallback for Payment requests or single-supplier requests without explicit itemAwards
        if (result.length === 0 && (request.supplierName || request.selectedQuotationId)) {
            const fallbackGroup: SupplierGroup = {
                supplierName: request.supplierName || 'Fornecedor Selecionado',
                currency: request.currencyCode || 'AOA',
                quotationDocNumber: request.selectedQuotationId ? 'S/N' : 'N/A',
                items: [],
                totalAmount: 0
            };

            activeItems.forEach((item: any) => {
                const itemAmount = resolveItemAmount(item);
                fallbackGroup.items.push({
                    lineNumber: item.lineNumber,
                    description: item.description,
                    quantity: item.quantity,
                    unit: item.unit || 'UN',
                    unitPrice: item.unitPrice || 0,
                    lineTotal: itemAmount
                });
                fallbackGroup.totalAmount += itemAmount;
            });
            result.push(fallbackGroup);
        }

        return result;
    }, [activeItems, quotations, itemAwards, request]);

    // Total approved value
    const totalApprovedValue = useMemo(() => {
        return supplierGroups.reduce((sum, g) => sum + g.totalAmount, 0);
    }, [supplierGroups]);

    // Allocation summary (Cost Center)
    const allocationSummary = useMemo(() => {
        const ccMap: Record<number, { name: string, code: string, count: number, amount: number }> = {};

        [...activeItems, ...extraItems].forEach((item: any) => {
            const itemAmount = resolveItemAmount(item);

            const allocations = itemAllocations?.[item.id];
            if (allocations && allocations.length > 0) {
                allocations.forEach(alloc => {
                    if (alloc.costCenterId) {
                        const cc = costCenters.find(c => c.id === alloc.costCenterId);
                        if (cc) {
                            if (!ccMap[alloc.costCenterId]) {
                                ccMap[alloc.costCenterId] = { name: cc.name, code: cc.code, count: 0, amount: 0 };
                            }
                            ccMap[alloc.costCenterId].count += 1;
                            ccMap[alloc.costCenterId].amount += itemAmount * (alloc.percentage / 100);
                        }
                    }
                });
            } else {
                // Fallback to itemAssignments
                const ccId = itemAssignments[item.id]?.costCenterId;
                if (ccId) {
                    const cc = costCenters.find(c => c.id === ccId);
                    if (cc) {
                        if (!ccMap[ccId]) {
                            ccMap[ccId] = { name: cc.name, code: cc.code, count: 0, amount: 0 };
                        }
                        ccMap[ccId].count += 1;
                        ccMap[ccId].amount += itemAmount;
                    }
                }
            }
        });

        const entries = Object.entries(ccMap).map(([, data]) => ({
            label: `${data.code} - ${data.name}`,
            count: data.count,
            amount: data.amount
        }));

        const grandTotal = entries.reduce((sum, e) => sum + e.amount, 0);

        return entries.map(e => ({
            label: e.label,
            count: e.count,
            amount: e.amount,
            percent: grandTotal > 0 ? Math.round((e.amount / grandTotal) * 100) : 0
        }));
    }, [activeItems, itemAssignments, itemAllocations, costCenters, itemAwards, quotations, request]);

    // Plant distribution — based on total approved monetary amount
    const plantDistribution = useMemo(() => {
        const plantMap: Record<number, { name: string, amount: number }> = {};

        [...activeItems, ...extraItems].forEach((item: any) => {
            const itemAmount = resolveItemAmount(item);

            const allocations = itemAllocations?.[item.id];
            if (allocations && allocations.length > 0) {
                allocations.forEach(alloc => {
                    if (alloc.plantId) {
                        const plant = plants.find(p => p.id === alloc.plantId);
                        if (plant) {
                            if (!plantMap[alloc.plantId]) {
                                plantMap[alloc.plantId] = { name: plant.name, amount: 0 };
                            }
                            plantMap[alloc.plantId].amount += itemAmount * (alloc.percentage / 100);
                        }
                    }
                });
            } else {
                // Fallback to itemAssignments
                const plantId = itemAssignments[item.id]?.plantId;
                if (!plantId) return;

                const plant = plants.find(p => p.id === plantId);
                if (!plant) return;

                if (!plantMap[plantId]) {
                    plantMap[plantId] = { name: plant.name, amount: 0 };
                }
                plantMap[plantId].amount += itemAmount;
            }
        });

        const entries = Object.entries(plantMap).map(([, data]) => ({
            name: data.name,
            amount: data.amount
        }));
        const grandTotal = entries.reduce((sum, e) => sum + e.amount, 0);

        return entries.map(e => ({
            name: e.name,
            amount: e.amount,
            percent: grandTotal > 0 ? Math.round((e.amount / grandTotal) * 100) : 0
        }));
    }, [activeItems, itemAssignments, itemAllocations, plants, itemAwards, quotations, request]);

    const hasValidationErrors = !isStep2Valid || !isStep3Valid || !isStep4Valid;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
            {/* Contextual Guidance */}
            <div style={{
                backgroundColor: '#EFF6FF', border: '1px solid #BFDBFE',
                borderRadius: '6px', padding: '12px 16px', color: '#1E3A8A',
                fontSize: '0.875rem', display: 'flex', gap: '10px', alignItems: 'flex-start'
            }}>
                <span style={{ fontSize: '1.25rem', lineHeight: 1 }}>💡</span>
                <div>
                    Revise fornecedor/pagamento, alocação financeira, impacto por Planta e Centro de Custo antes de confirmar sua decisão.
                </div>
            </div>

            {/* Header */}
            <div>
                <h3 style={{ fontSize: '1.125rem', fontWeight: 700, color: '#1F2937', margin: 0 }}>Revisão Final</h3>
            </div>

            {/* Checklist de Aprovação */}
            <div style={{
                backgroundColor: (hasValidationErrors || !isStep2Valid || !isStep3Valid || !isStep4Valid) ? '#FEF2F2' : '#F0FDF4', 
                border: `1px solid ${(hasValidationErrors || !isStep2Valid || !isStep3Valid || !isStep4Valid) ? '#FECACA' : '#BBF7D0'}`,
                borderRadius: '8px', padding: '16px', display: 'flex', flexDirection: 'column', gap: '12px'
            }}>
                <h4 style={{ margin: '0 0 4px 0', fontSize: '0.875rem', fontWeight: 700, color: (hasValidationErrors || !isStep2Valid || !isStep3Valid || !isStep4Valid) ? '#991B1B' : '#166534', display: 'flex', alignItems: 'center', gap: '6px' }}>
                    {(hasValidationErrors || !isStep2Valid || !isStep3Valid || !isStep4Valid) ? <XCircle size={18} /> : <ShieldCheck size={18} />} 
                    Checklist de Aprovação
                </h4>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <CheckCircle2 size={16} color="#10B981" />
                        <span style={{ fontSize: '0.8125rem', color: '#166534', fontWeight: 500 }}>Itens revisados</span>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        {isStep3Valid ? <CheckCircle2 size={16} color="#10B981" /> : <AlertCircle size={16} color="#EF4444" />}
                        <span style={{ fontSize: '0.8125rem', color: isStep3Valid ? '#166534' : '#991B1B', fontWeight: 500 }}>
                            {isStep3Valid ? 'Fornecedor/pagamento revisado' : 'Fornecedor/pagamento pendente'}
                        </span>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        {isStep2Valid ? <CheckCircle2 size={16} color="#10B981" /> : <AlertCircle size={16} color="#EF4444" />}
                        <span style={{ fontSize: '0.8125rem', color: isStep2Valid ? '#166534' : '#991B1B', fontWeight: 500 }}>
                            {isStep2Valid ? 'Alocação financeira completa' : 'Alocação financeira incompleta'}
                        </span>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <CheckCircle2 size={16} color="#10B981" />
                        <span style={{ fontSize: '0.8125rem', color: '#166534', fontWeight: 500 }}>Distribuição por Planta e CC revisada</span>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', gridColumn: '1 / -1' }}>
                        {isStep4Valid ? <CheckCircle2 size={16} color="#10B981" /> : <AlertCircle size={16} color="#EF4444" />}
                        <span style={{ fontSize: '0.8125rem', color: isStep4Valid ? '#166534' : '#991B1B', fontWeight: 500 }}>
                            {isStep4Valid ? 'Análise orçamental validada' : 'Justificativa orçamental pendente'}
                        </span>
                    </div>
                </div>
            </div>

            {/* Approval overview card */}
            <div style={{
                display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px'
            }}>
                {/* Supplier groups */}
                <div style={{
                    backgroundColor: '#FFFFFF', border: '1px solid #E5E7EB',
                    borderRadius: '8px', overflow: 'hidden'
                }}>
                    <div style={{
                        padding: '12px 16px', backgroundColor: '#F9FAFB',
                        borderBottom: '1px solid #E5E7EB',
                        display: 'flex', alignItems: 'center', gap: '8px',
                        fontSize: '0.75rem', fontWeight: 700, color: '#374151',
                        textTransform: 'uppercase', letterSpacing: '0.05em'
                    }}>
                        <Package size={14} /> Grupos de Fornecedores ({supplierGroups.length})
                    </div>
                    <div style={{ padding: '12px', display: 'flex', flexDirection: 'column', gap: '10px' }}>
                        {supplierGroups.length === 0 ? (
                            <div style={{ color: '#9CA3AF', fontSize: '0.8125rem', fontStyle: 'italic', padding: '8px' }}>
                                Nenhum fornecedor selecionado.
                            </div>
                        ) : (
                            supplierGroups.map((group, idx) => (
                                <div key={idx} style={{
                                    padding: '12px', backgroundColor: '#F9FAFB',
                                    border: '1px solid #E5E7EB', borderRadius: '6px'
                                }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                        <div>
                                            <div style={{ fontWeight: 700, fontSize: '0.875rem', color: '#111827' }}>
                                                {group.supplierName}
                                            </div>
                                            <div style={{ fontSize: '0.6875rem', color: '#6B7280', marginTop: '2px' }}>
                                                Cot. #{group.quotationDocNumber} • {group.items.length} {group.items.length === 1 ? 'item' : 'itens'}
                                            </div>
                                        </div>
                                        <div style={{ textAlign: 'right' }}>
                                            <div style={{ fontSize: '1rem', fontWeight: 700, color: '#059669' }}>
                                                {formatCurrencyAO(group.totalAmount)}
                                            </div>
                                            <div style={{ fontSize: '0.6875rem', color: '#6B7280' }}>{group.currency}</div>
                                        </div>
                                    </div>
                                    {/* Items under this supplier */}
                                    <div style={{ marginTop: '10px', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                        {group.items.map((gItem, gIdx) => (
                                            <div key={gIdx} style={{
                                                display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                                                fontSize: '0.75rem', color: '#6B7280', padding: '4px 0',
                                                borderTop: gIdx > 0 ? '1px solid #E5E7EB' : 'none'
                                            }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                    <span>{gItem.lineNumber !== 999 ? `#${gItem.lineNumber} ` : ''}{gItem.description}</span>
                                                    {(gItem as any).isExtra && (
                                                        <span style={{
                                                            fontSize: '0.625rem', fontWeight: 600, color: '#047857', backgroundColor: '#D1FAE5',
                                                            padding: '2px 6px', borderRadius: '4px', textTransform: 'uppercase'
                                                        }}>
                                                            Item Adicional Aprovado
                                                        </span>
                                                    )}
                                                </div>
                                                <span style={{ fontWeight: 600, color: '#374151' }}>{formatCurrencyAO(gItem.lineTotal)}</span>
                                            </div>
                                        ))}
                                    </div>
                                </div>
                            ))
                        )}

                        {/* Total */}
                        {supplierGroups.length > 0 && (
                            <div style={{
                                display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                                padding: '12px', borderTop: '2px solid #E5E7EB',
                                marginTop: '4px'
                            }}>
                                <span style={{ fontWeight: 700, fontSize: '0.875rem', color: '#111827' }}>Total Aprovado</span>
                                <span style={{ fontWeight: 800, fontSize: '1.125rem', color: '#059669' }}>
                                    {formatCurrencyAO(totalApprovedValue)}
                                </span>
                            </div>
                        )}
                    </div>
                </div>

                {/* Right column: allocation summary */}
                <div style={{
                    backgroundColor: '#FFFFFF', border: '1px solid #E5E7EB',
                    borderRadius: '8px', overflow: 'hidden'
                }}>
                    <div style={{
                        padding: '12px 16px', backgroundColor: '#F9FAFB',
                        borderBottom: '1px solid #E5E7EB',
                        display: 'flex', alignItems: 'center', gap: '8px',
                        fontSize: '0.75rem', fontWeight: 700, color: '#374151',
                        textTransform: 'uppercase', letterSpacing: '0.05em'
                    }}>
                        <Landmark size={14} /> Alocação Financeira
                    </div>
                    <div style={{ padding: '12px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {/* Checklist removed - moved to top */}

                        {/* Cost center distribution */}
                        {allocationSummary.length > 0 && (
                            <div style={{ marginTop: '8px' }}>
                                <div style={{ fontSize: '0.6875rem', fontWeight: 700, color: '#6B7280', textTransform: 'uppercase', marginBottom: '8px' }}>
                                    Distribuição por Centro de Custo
                                </div>
                                {allocationSummary.map((entry, idx) => (
                                    <div key={idx} style={{
                                        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                                        padding: '6px 0', fontSize: '0.8125rem',
                                        borderBottom: idx < allocationSummary.length - 1 ? '1px solid #F3F4F6' : 'none'
                                    }}>
                                        <div>
                                            <div style={{ color: '#374151', fontWeight: 500 }}>{entry.label}</div>
                                            <div style={{ fontSize: '0.6875rem', color: '#6B7280', marginTop: '2px' }}>
                                                {entry.count} {entry.count === 1 ? 'item' : 'itens'} ({entry.percent}%)
                                            </div>
                                        </div>
                                        <span style={{ fontWeight: 600, color: '#111827' }}>
                                            {formatCurrencyAO(entry.amount)}
                                        </span>
                                    </div>
                                ))}
                            </div>
                        )}

                        {/* Plant distribution — monetary bar chart */}
                        {plantDistribution.length > 0 && (
                            <div style={{ marginTop: '12px', borderTop: '1px solid #E5E7EB', paddingTop: '12px' }}>
                                <div style={{ fontSize: '0.6875rem', fontWeight: 700, color: '#6B7280', textTransform: 'uppercase', marginBottom: '10px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <Factory size={12} /> Distribuição por Planta
                                </div>
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                                    {plantDistribution.map((entry, idx) => (
                                        <div key={idx}>
                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
                                                <span style={{ fontSize: '0.8125rem', fontWeight: 600, color: '#111827' }}>{entry.name}</span>
                                                <span style={{ fontSize: '0.75rem', fontWeight: 700, color: '#374151' }}>
                                                    {formatCurrencyAO(entry.amount)} — {entry.percent}%
                                                </span>
                                            </div>
                                            <div style={{ width: '100%', height: '8px', backgroundColor: '#E5E7EB', borderRadius: '4px', overflow: 'hidden' }}>
                                                <div style={{
                                                    height: '100%', borderRadius: '4px',
                                                    backgroundColor: idx === 0 ? '#2563EB' : idx === 1 ? '#7C3AED' : idx === 2 ? '#059669' : '#D97706',
                                                    width: `${entry.percent}%`,
                                                    transition: 'width 0.3s ease',
                                                    minWidth: entry.percent > 0 ? '4px' : '0'
                                                }} />
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </div>
                        )}

                        {/* Reassignments Audit */}
                        {reassignments && reassignments.length > 0 && (
                            <div style={{ marginTop: '12px', borderTop: '1px solid #E5E7EB', paddingTop: '12px' }}>
                                <div style={{ fontSize: '0.6875rem', fontWeight: 700, color: '#6B7280', textTransform: 'uppercase', marginBottom: '10px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <ShieldCheck size={12} /> Realocações Orçamentais
                                </div>
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                    {reassignments.map((r, idx) => {
                                        const oldPlant = plants.find(p => p.id === r.oldPlantId)?.name;
                                        const oldCc = costCenters.find(c => c.id === r.oldCostCenterId)?.name || 'Orçamento Geral';
                                        const newPlant = plants.find(p => p.id === r.newPlantId)?.name;
                                        const newCc = costCenters.find(c => c.id === r.newCostCenterId)?.name;
                                        
                                        return (
                                            <div key={idx} style={{ backgroundColor: '#F9FAFB', padding: '10px', borderRadius: '6px', border: '1px solid #E5E7EB' }}>
                                                <div style={{ fontSize: '0.8125rem', color: '#374151', display: 'flex', gap: '8px', marginBottom: '4px' }}>
                                                    <span style={{ fontWeight: 600 }}>De:</span> {oldPlant} / {oldCc}
                                                </div>
                                                <div style={{ fontSize: '0.8125rem', color: '#111827', display: 'flex', gap: '8px', marginBottom: '6px' }}>
                                                    <span style={{ fontWeight: 600 }}>Para:</span> {newPlant} / {newCc}
                                                </div>
                                                <div style={{ fontSize: '0.75rem', color: '#6B7280', display: 'flex', alignItems: 'flex-start', gap: '4px' }}>
                                                    <span style={{ fontWeight: 600 }}>Motivo:</span> 
                                                    <span style={{ fontStyle: 'italic' }}>"{r.reason}"</span>
                                                </div>
                                                <div style={{ fontSize: '0.6875rem', color: '#9CA3AF', marginTop: '4px' }}>
                                                    {r.affectedItemIds.length} {r.affectedItemIds.length === 1 ? 'item afetado' : 'itens afetados'}
                                                </div>
                                            </div>
                                        );
                                    })}
                                </div>
                            </div>
                        )}
                    </div>
                </div>
            </div>

            {/* Budget Justification field */}
            {budgetJustification && (
                <div style={{
                    backgroundColor: '#FEF2F2', border: '1px solid #FCA5A5',
                    borderRadius: '8px', padding: '16px', marginBottom: '16px'
                }}>
                    <div style={{
                        display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '10px'
                    }}>
                        <ShieldAlert size={16} color="#991B1B" />
                        <span style={{ fontSize: '0.8125rem', fontWeight: 700, color: '#991B1B' }}>
                            Exceção Orçamental
                        </span>
                    </div>

                    {budgetPreview && budgetPreview.allocations && (
                        <div style={{ marginBottom: '12px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            {budgetPreview.allocations
                                .filter((a: any) => a.status === 'CRITICAL' || a.status === 'OVER_BUDGET' || a.projectedBalance < 0 || a.projectedUsagePercent >= 100)
                                .map((a: any, idx: number) => {
                                    
                                    const formatCurrency = (val: number) => {
                                        return new Intl.NumberFormat('pt-AO', { style: 'currency', currency: a.currencyCode }).format(val);
                                    };

                                    return (
                                        <div key={idx} style={{ 
                                            display: 'grid', gridTemplateColumns: 'max-content 1fr', gap: '2px 8px', 
                                            fontSize: '0.75rem', color: '#991B1B', backgroundColor: '#FEE2E2', padding: '8px', borderRadius: '4px' 
                                        }}>
                                            <span>Centro de Custo:</span> 
                                            <span style={{ fontWeight: 600 }}>{a.plantName} — {a.costCenterName}</span>
                                            
                                            <span>Orçamento Anual:</span> 
                                            <span>{formatCurrency(a.annualBudget)}</span>
                                            
                                            <span>Valor do Pedido:</span> 
                                            <span>{formatCurrency(a.thisRequestAmount)}</span>

                                            <span>Saldo Projetado:</span> 
                                            <span style={{ fontWeight: 700, color: a.projectedBalance < 0 ? '#B91C1C' : '#991B1B' }}>
                                                {formatCurrency(a.projectedBalance)}
                                            </span>
                                        </div>
                                    );
                                })
                            }
                        </div>
                    )}

                    <div style={{
                        width: '100%', padding: '10px 12px',
                        border: '1px solid #F87171', borderRadius: '6px', backgroundColor: '#FEF2F2',
                        fontSize: '0.875rem', color: '#7F1D1D',
                        fontFamily: 'inherit', lineHeight: 1.5, whiteSpace: 'pre-wrap'
                    }}>
                        {budgetJustification}
                    </div>
                </div>
            )}

            {/* Comment field */}
            <div style={{
                backgroundColor: '#FFFFFF', border: '1px solid #E5E7EB',
                borderRadius: '8px', padding: '16px'
            }}>
                <div style={{
                    display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '10px'
                }}>
                    <MessageSquare size={14} color="#6B7280" />
                    <span style={{ fontSize: '0.8125rem', fontWeight: 700, color: '#374151' }}>
                        Comentário
                    </span>
                    <span style={{ fontSize: '0.6875rem', color: '#9CA3AF', fontWeight: 500 }}>(obrigatório para Rejeição ou Reajuste)</span>
                </div>
                <textarea
                    value={comment}
                    onChange={(e) => onChangeComment(e.target.value)}
                    placeholder="Adicione observações sobre esta aprovação..."
                    rows={3}
                    style={{
                        width: '100%', padding: '10px 12px',
                        border: '1px solid #D1D5DB', borderRadius: '6px',
                        fontSize: '0.875rem', resize: 'vertical', outline: 'none',
                        fontFamily: 'inherit', lineHeight: 1.5
                    }}
                />
            </div>

            {/* Confirmation checkbox */}
            <div
                style={{
                    padding: '16px', borderRadius: '8px',
                    backgroundColor: confirmReview ? '#ECFDF5' : '#F9FAFB',
                    border: `1px solid ${confirmReview ? '#A7F3D0' : '#E5E7EB'}`,
                    transition: 'all 0.2s'
                }}
            >
                <label style={{
                    display: 'flex', alignItems: 'flex-start', gap: '12px', cursor: 'pointer',
                    userSelect: 'none'
                }}>
                    <input
                        type="checkbox"
                        checked={confirmReview}
                        onChange={(e) => onChangeConfirmReview(e.target.checked)}
                        style={{
                            width: '20px', height: '20px', marginTop: '2px',
                            accentColor: '#10B981', cursor: 'pointer', flexShrink: 0
                        }}
                    />
                    <div>
                        <div style={{ fontWeight: 700, fontSize: '0.875rem', color: confirmReview ? '#065F46' : '#374151', display: 'flex', alignItems: 'center', gap: '6px' }}>
                            <ShieldCheck size={16} color={confirmReview ? '#059669' : '#6B7280'} />
                            Confirmação de Revisão
                        </div>
                        <div style={{ fontSize: '0.8125rem', color: '#6B7280', marginTop: '4px' }}>
                            Confirmo que revisei os valores, atribuições e fornecedores selecionados.
                        </div>
                    </div>
                </label>
            </div>
        </div>
    );
};
