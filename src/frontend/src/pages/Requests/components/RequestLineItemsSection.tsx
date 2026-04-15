// @ts-nocheck
/**
 * RequestLineItemsSection
 *
 * Phase 3 extraction from RequestEdit.tsx (lines 454-588).
 * Renders the "Itens do Pedido" collapsible section containing:
 *   - Section header with title and "Adicionar Item" button
 *   - Quotation-linked note banner (when quotation is selected)
 *   - Item table with dual rendering paths:
 *       a) Selected quotation items (read-only)
 *       b) Normal line items with edit/delete actions
 *   - Inline item form (RequestLineItemForm) with animation
 *
 * All state, handlers, permissions, and data are received via props.
 * No business logic is contained here.
 */
import React from 'react';
import { Plus, Edit2, Trash2 } from 'lucide-react';
import { motion } from 'framer-motion';
import { CollapsibleSection } from '../../../components/ui/CollapsibleSection';
import { RequestLineItemForm } from '../../../components/RequestLineItemForm';
import { RequestLineItemDto, SavedQuotationDto, LookupDto } from '../../../types';

export interface RequestLineItemsSectionProps {
    // Line items data
    lineItems: RequestLineItemDto[];
    itemForm: any;
    setItemForm: (val: any) => void;
    itemSaving: boolean;

    // Quotation data
    selectedQuotationId: string | null;
    quotations: SavedQuotationDto[];

    // Lookup data
    units: LookupDto[];
    plants: LookupDto[];
    costCenters: LookupDto[];
    ivaRates: LookupDto[];

    // Form context
    companyId: string | number;
    requestTypeCode: string | undefined;
    supplierId: number | null;
    fieldErrors: Record<string, string[]>;
    clearFieldError: (fieldName: string) => void;

    // Permissions
    canEditItems: boolean;

    // Handlers
    handleSaveItem: (item: any) => void;
    handleDeleteItem: (itemId: string | number) => void;

    // Collapsible state
    isOpen: boolean;
    onToggle: () => void;

    // UI state
    isItemsHighlighted: boolean;
    itemsSectionRef: React.RefObject<HTMLDivElement>;

    // Formatting
    formatCurrencyAO: (value: number) => string;

    // Style helpers (Phase 4A: CSS Module class names)
    sectionTitleClassName: string;
}

export function RequestLineItemsSection({
    lineItems, itemForm, setItemForm, itemSaving,
    selectedQuotationId, quotations,
    units, plants, costCenters, ivaRates,
    companyId, requestTypeCode, supplierId, fieldErrors, clearFieldError,
    canEditItems,
    handleSaveItem, handleDeleteItem,
    isOpen, onToggle,
    isItemsHighlighted, itemsSectionRef,
    formatCurrencyAO,
    sectionTitleClassName
}: RequestLineItemsSectionProps) {
    return (
        <CollapsibleSection
            title={selectedQuotationId ? 'Itens da Cotação Vencedora' : 'Itens do Pedido'}
            count={selectedQuotationId ? (quotations.find(q => q.id === selectedQuotationId)?.items.length || 0) : lineItems.length}
            isOpen={isOpen}
            onToggle={onToggle}
        >
            <div
                id="items-section-container"
                ref={itemsSectionRef}
                style={{
                    padding: '32px',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '24px',
                    transition: 'box-shadow 0.5s ease',
                    boxShadow: isItemsHighlighted ? 'inset 0 0 0 4px #EF4444' : 'none'
                }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
                    <h2 id="items-section" className={sectionTitleClassName} style={{ borderBottom: 'none', marginBottom: 0, marginTop: 0 }}>
                        {selectedQuotationId ? 'Detalhamento da Proposta' : 'Itens da Requisição'}
                    </h2>
                    {selectedQuotationId && (
                        <span style={{ backgroundColor: '#4f46e5', color: '#fff', fontSize: '0.75rem', fontWeight: 900, padding: '4px 12px', borderRadius: '4px', textTransform: 'uppercase' }}>
                            Autoridade: Cotação Selecionada
                        </span>
                    )}
                    {canEditItems && !itemForm && !selectedQuotationId && (
                        <button
                            type="button"
                            onClick={() => setItemForm({ itemPriority: 'MEDIUM', quantity: 1, unit: '' })}
                            className="btn-primary"
                            style={{ display: 'flex', alignItems: 'center', gap: '8px' }}
                        >
                            <Plus size={18} />
                            Adicionar Item
                        </button>
                    )}
                </div>

                {selectedQuotationId && (
                    <div style={{ backgroundColor: '#f0f4ff', padding: '16px', borderRadius: '8px', borderLeft: '4px solid #4f46e5', marginBottom: '16px' }}>
                        <p style={{ fontSize: '0.875rem', color: '#1e1b4b', margin: 0 }}>
                            <strong>Nota:</strong> Este pedido está vinculado à cotação vencedora. Os itens abaixo refletem a proposta comercial do fornecedor selecionado.
                        </p>
                    </div>
                )}

                <div style={{ overflowX: 'auto' }}>
                    <table className="alpla-table">
                        <thead>
                            <tr>
                                <th>#</th>
                                <th>Descrição</th>
                                <th style={{ textAlign: 'center' }}>Unid.</th>
                                <th style={{ textAlign: 'right' }}>Qtd</th>
                                {!selectedQuotationId && <th>Planta</th>}
                                {!selectedQuotationId && <th>IVA</th>}
                                <th style={{ textAlign: 'right' }}>Preço Unit.</th>
                                <th style={{ textAlign: 'right' }}>Total</th>
                                {!selectedQuotationId && <th style={{ textAlign: 'center' }}>Ações</th>}
                            </tr>
                        </thead>
                        <tbody>
                            {selectedQuotationId ? (
                                // PIVOT: Show items from the selected quotation
                                (quotations.find(q => q.id === selectedQuotationId)?.items || []).map((item, idx) => (
                                    <tr key={item.id || idx}>
                                        <td>{idx + 1}</td>
                                        <td style={{ fontWeight: 600 }}>{item.description}</td>
                                        <td style={{ textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 700 }}>{item.unitCode || '---'}</td>
                                        <td style={{ textAlign: 'right' }}>{item.quantity}</td>
                                        <td style={{ textAlign: 'right' }}>{formatCurrencyAO(item.unitPrice)}</td>
                                        <td style={{ textAlign: 'right', fontWeight: 700 }}>{formatCurrencyAO(item.lineTotal)}</td>
                                    </tr>
                                ))
                            ) : lineItems.length === 0 ? (
                                <tr>
                                    <td colSpan={9} style={{ textAlign: 'center', padding: '24px', color: 'var(--color-text-muted)' }}>Nenhum item adicionado a este pedido.</td>
                                </tr>
                            ) : (
                                lineItems.map(item => (
                                    <tr key={item.id}>
                                        <td>{item.lineNumber}</td>
                                        <td>{item.description}</td>
                                        <td style={{ textAlign: 'center' }}>{item.unit || '---'}</td>
                                        <td style={{ textAlign: 'right' }}>{item.quantity}</td>
                                        <td>{item.plantName || '---'}</td>
                                        <td>
                                            <span style={{ fontSize: '0.75rem' }}>{item.ivaRatePercent != null ? `${item.ivaRatePercent}%` : '---'}</span>
                                        </td>
                                        <td style={{ textAlign: 'right' }}>{formatCurrencyAO(item.unitPrice)}</td>
                                        <td style={{ textAlign: 'right', fontWeight: 600 }}>{formatCurrencyAO(item.totalAmount)}</td>
                                        <td style={{ padding: '8px', textAlign: 'center' }}>
                                            <div style={{ display: 'flex', gap: '8px', justifyContent: 'center' }}>
                                                {canEditItems ? (
                                                    <>
                                                        <button onClick={() => setItemForm(item)} style={{ background: 'none', border: 'none', color: 'var(--color-primary)', cursor: 'pointer' }} title="Editar"><Edit2 size={16} /></button>
                                                        <button onClick={() => handleDeleteItem(item.id)} style={{ background: 'none', border: 'none', color: '#EF4444', cursor: 'pointer' }} title="Excluir"><Trash2 size={16} /></button>
                                                    </>
                                                ) : (
                                                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>Somente leitura</span>
                                                )}
                                            </div>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {itemForm && (
                    <motion.div
                        initial={{ opacity: 0, y: 10 }}
                        animate={{ opacity: 1, y: 0 }}
                    >
                        <RequestLineItemForm
                            itemForm={itemForm}
                            setItemForm={setItemForm}
                            onSaveItem={handleSaveItem}
                            itemSaving={itemSaving}
                            units={units as any}
                            fieldErrors={fieldErrors}
                            clearFieldError={clearFieldError}
                            companyId={companyId ? Number(companyId) : null}
                            plants={(plants || []).filter(p => !companyId || Number(p.companyId) === Number(companyId))}
                            requestTypeCode={requestTypeCode || undefined}
                            costCenters={costCenters}
                            ivaRates={ivaRates}
                            supplierId={supplierId}
                        />
                    </motion.div>
                )}
            </div>
        </CollapsibleSection>
    );
}
