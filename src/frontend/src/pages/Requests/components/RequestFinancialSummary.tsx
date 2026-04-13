// @ts-nocheck
/**
 * RequestFinancialSummary
 *
 * Phase 2A extraction from RequestEdit.tsx (lines 597-664).
 * Purely presentational — renders the "Resumo Financeiro do Pedido"
 * collapsible section containing: estimated total, global discount, and currency.
 *
 * All state, handlers, and style helpers are received via props.
 */
import React from 'react';
import { CollapsibleSection } from '../../../components/ui/CollapsibleSection';
import { CurrencyDto } from '../../../types';

export interface RequestFinancialSummaryProps {
    // Form state & handlers
    formData: Record<string, any>;
    setFormData: (updater: (prev: any) => any) => void;
    handleChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => void;
    clearFieldError: (fieldName: string) => void;

    // Lookups
    currencies: CurrencyDto[];

    // Permissions
    canEditHeader: boolean;
    canEdit: boolean;

    // Context
    lineItemsCount: number;

    // Collapsible state
    isOpen: boolean;
    onToggle: () => void;

    // Formatting
    formatCurrencyAO: (value: number) => string;

    // Style helpers (Phase 4A: CSS Module class names)
    labelClassName: string;
    getInputClassName: (fieldName: string) => string;
    renderFieldError: (fieldName: string) => React.ReactNode;
}

export function RequestFinancialSummary({
    formData, setFormData, handleChange, clearFieldError,
    currencies,
    canEditHeader, canEdit,
    lineItemsCount,
    isOpen, onToggle,
    formatCurrencyAO,
    labelClassName, getInputClassName, renderFieldError
}: RequestFinancialSummaryProps) {
    return (
        <CollapsibleSection
            title="Resumo Financeiro do Pedido"
            count={0}
            isOpen={isOpen}
            onToggle={onToggle}
        >
            <div style={{ padding: '32px' }}>
                <div style={{ display: 'grid', gridTemplateColumns: '1.5fr 1.5fr 1fr', gap: '32px', alignItems: 'start' }}>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', padding: '16px', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)', borderRadius: 'var(--radius-sm)' }}>
                        <span style={{ fontSize: '0.75rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.05em' }}>
                            Valor Total Estimado
                        </span>
                        <span style={{ fontSize: '2rem', fontWeight: 800, color: 'var(--color-text-main)', fontFamily: 'var(--font-family-display)' }}>
                            {formatCurrencyAO(Number(formData.estimatedTotalAmount) || 0)}
                        </span>
                        <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                            Este valor é recalculado automaticamente a partir dos itens inseridos ao recarregar a tela.
                        </span>
                    </div>

                    <label className={labelClassName}>
                        Desconto Global ({currencies.find(c => c.id === Number(formData.currencyId))?.code || ''})
                        <input
                            type="number"
                            min="0"
                            step="0.01"
                            value={formData.discountAmount}
                            onChange={(e) => {
                                setFormData(prev => ({ ...prev, discountAmount: e.target.value }));
                                clearFieldError('DiscountAmount');
                            }}
                            disabled={!canEditHeader}
                            placeholder="0.00"
                            className={getInputClassName('DiscountAmount')}
                        />
                        <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                            Abatimento que reduz a base tributável deste pedido.
                        </span>
                        {renderFieldError('DiscountAmount')}
                    </label>

                    <label className={labelClassName}>
                        Moeda Principal do Pedido
                        <select
                            name="currencyId"
                            value={formData.currencyId}
                            onChange={handleChange}
                            className={getInputClassName('CurrencyId')}
                            style={{
                                ...(lineItemsCount > 0 || !canEdit ? { backgroundColor: 'var(--color-bg-page)', cursor: 'not-allowed', opacity: 0.8 } : {})
                            }}
                            disabled={lineItemsCount > 0 || !canEditHeader}
                        >
                            {currencies.map(c => (
                                <option key={c.id} value={c.id}>{c.code} ({c.symbol})</option>
                            ))}
                        </select>
                        {lineItemsCount > 0 && (
                            <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: '4px', display: 'block' }}>
                                Moeda bloqueada pois o pedido já possui itens.
                            </span>
                        )}
                        {renderFieldError('CurrencyId')}
                    </label>
                </div>
            </div>
        </CollapsibleSection>
    );
}
