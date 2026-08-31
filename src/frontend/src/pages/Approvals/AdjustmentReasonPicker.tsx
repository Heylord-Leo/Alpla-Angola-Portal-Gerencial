import React from 'react';
import { ADJUSTMENT_REASONS, isItemRequired, type AdjustmentReasonOwner } from '../../lib/adjustmentReasons';

interface AdjustmentReasonPickerProps {
    /** Batch line items available for item-scoped reasons. */
    items: { id: string; label: string }[];
    selectedCodes: string[];
    onChangeCodes: (codes: string[]) => void;
    selectedItemIds: string[];
    onChangeItemIds: (ids: string[]) => void;
    disabled?: boolean;
}

/**
 * Adjustment V2 (Phase 3) — the approver's structured "Motivos do Reajuste" selector. Multi-select
 * reasons grouped by owner, with an affected-items list shown only when an item-required reason is
 * chosen. Renders friendly Portuguese labels exclusively — never the raw reason codes. Pure
 * presentation: mapping to the request payload lives in lib/adjustmentReasons.
 */
export const AdjustmentReasonPicker: React.FC<AdjustmentReasonPickerProps> = ({
    items, selectedCodes, onChangeCodes, selectedItemIds, onChangeItemIds, disabled = false,
}) => {
    const toggleCode = (code: string) => {
        onChangeCodes(selectedCodes.includes(code)
            ? selectedCodes.filter(c => c !== code)
            : [...selectedCodes, code]);
    };
    const toggleItem = (id: string) => {
        onChangeItemIds(selectedItemIds.includes(id)
            ? selectedItemIds.filter(i => i !== id)
            : [...selectedItemIds, id]);
    };

    const needsItems = selectedCodes.some(isItemRequired);

    const group = (owner: AdjustmentReasonOwner, title: string) => (
        <div style={{ flex: 1, minWidth: 220 }}>
            <div style={{ fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted, #6b7280)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: 6 }}>
                {title}
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                {ADJUSTMENT_REASONS.filter(r => r.owner === owner).map(r => (
                    <label key={r.code} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: '0.8rem', cursor: disabled ? 'not-allowed' : 'pointer', color: 'var(--color-text-main, #111827)' }}>
                        <input
                            type="checkbox"
                            checked={selectedCodes.includes(r.code)}
                            onChange={() => toggleCode(r.code)}
                            disabled={disabled}
                        />
                        {r.label}
                    </label>
                ))}
            </div>
        </div>
    );

    return (
        <div data-testid="adjustment-reason-picker" style={{ border: '1px solid #FCD34D', backgroundColor: '#FFFBEB', borderRadius: 8, padding: 14 }}>
            <div style={{ fontWeight: 800, fontSize: '0.8rem', color: '#B45309', marginBottom: 10 }}>
                Motivos do Reajuste (selecione um ou mais)
            </div>
            <div style={{ display: 'flex', gap: 24, flexWrap: 'wrap' }}>
                {group('BUYER', 'Comercial (Comprador)')}
                {group('REQUESTER', 'Pedido (Solicitante)')}
            </div>

            {needsItems && (
                <div style={{ marginTop: 12, borderTop: '1px dashed #FCD34D', paddingTop: 10 }}>
                    <div style={{ fontSize: '0.72rem', fontWeight: 800, color: '#92400E', marginBottom: 6 }}>
                        Itens afetados (obrigatório para os motivos por item)
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                        {items.length === 0 && (
                            <span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted, #6b7280)' }}>Nenhum item disponível no lote.</span>
                        )}
                        {items.map(it => (
                            <label key={it.id} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: '0.8rem', cursor: disabled ? 'not-allowed' : 'pointer', color: 'var(--color-text-main, #111827)' }}>
                                <input
                                    type="checkbox"
                                    checked={selectedItemIds.includes(it.id)}
                                    onChange={() => toggleItem(it.id)}
                                    disabled={disabled}
                                />
                                {it.label}
                            </label>
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
};
