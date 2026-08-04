import React from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { CatalogItemAutocomplete } from '../CatalogItemAutocomplete';
import { IvaRate, Unit } from '../../types';
import {
    TemporaryPaymentItem,
    computeItemTotal,
    createTemporaryItem,
    itemsTotalOf
} from '../../lib/paymentRequestCreation';
import { itemsTolerance } from '../../lib/paymentDocumentComposition';
import { formatCurrencyAO } from '../../lib/utils';

interface Props {
    items: TemporaryPaymentItem[];
    onChange: (next: TemporaryPaymentItem[]) => void;
    units: Unit[];
    ivaRates: IvaRate[];
    currency: string | null;
    /** The document's own stated total, which the lines must add up to. */
    documentTotal: number | null;
    readOnly?: boolean;
}

/**
 * The lines of <b>one</b> document.
 *
 * <p>Placement is the whole point. A single global item grid sitting above a list of document cards
 * cannot answer the only question that matters — which document is this line being paid against —
 * and was one of the main reasons the previous screen read as two competing workflows. Here the
 * grid is inside the document being edited, so a line has an owner by construction.</p>
 *
 * <p>The running comparison against the document's own total is shown as the user types rather than
 * at submission: a mismatch between what the invoice says and what its lines add up to is the check
 * that keeps every downstream group total honest.</p>
 */
export function PaymentDocumentItemsEditor({
    items, onChange, units, ivaRates, currency, documentTotal, readOnly = false
}: Props) {
    const ivaPercentOf = (ivaRateId: number | null) =>
        ivaRates.find(r => r.id === ivaRateId)?.ratePercent ?? 0;

    const patch = (tempId: string, changes: Partial<TemporaryPaymentItem>) =>
        onChange(items.map(i => {
            if (i.tempId !== tempId) return i;
            const next = { ...i, ...changes };
            return { ...next, totalAmount: computeItemTotal(next, ivaPercentOf(next.ivaRateId)) };
        }));

    const total = itemsTotalOf(items);
    const stated = documentTotal ?? 0;
    const mismatch = stated > 0 && items.length > 0 &&
                     Math.abs(total - stated) > itemsTolerance(stated);

    const cell: React.CSSProperties = { padding: '4px 6px', verticalAlign: 'top' };
    const input: React.CSSProperties = {
        width: '100%', boxSizing: 'border-box', padding: '6px 8px', fontSize: '0.78rem',
        borderRadius: 'var(--radius-sm, 6px)', border: '1px solid var(--color-border)',
        backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)'
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <div style={{
                display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '10px'
            }}>
                <h4 style={{
                    margin: 0, fontSize: '0.75rem', fontWeight: 900, letterSpacing: '0.04em',
                    textTransform: 'uppercase', color: 'var(--color-text-muted)'
                }}>
                    Itens deste documento
                </h4>
                {!readOnly && (
                    <button
                        type="button"
                        onClick={() => onChange([...items, createTemporaryItem()])}
                        style={{
                            display: 'inline-flex', alignItems: 'center', gap: '5px',
                            background: 'none', border: 'none', cursor: 'pointer',
                            color: 'var(--color-primary)', fontWeight: 800, fontSize: '0.75rem'
                        }}
                    >
                        <Plus size={14} /> Adicionar item
                    </button>
                )}
            </div>

            {/* Wide content scrolls inside its own box; the page never scrolls sideways. */}
            <div style={{
                overflowX: 'auto', border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-sm, 8px)'
            }}>
                <table style={{
                    width: '100%', minWidth: '720px', borderCollapse: 'collapse', fontSize: '0.75rem'
                }}>
                    <thead>
                        <tr style={{
                            backgroundColor: 'var(--color-bg-page)',
                            borderBottom: '1px solid var(--color-border)'
                        }}>
                            <th style={{ ...cell, textAlign: 'left', fontWeight: 800 }}>DESCRIÇÃO</th>
                            <th style={{ ...cell, textAlign: 'center', width: '90px', fontWeight: 800 }}>UNID.</th>
                            <th style={{ ...cell, textAlign: 'center', width: '80px', fontWeight: 800 }}>QTD</th>
                            <th style={{ ...cell, textAlign: 'right', width: '100px', fontWeight: 800 }}>P. UNIT</th>
                            <th style={{ ...cell, textAlign: 'center', width: '90px', fontWeight: 800 }}>IVA</th>
                            <th style={{ ...cell, textAlign: 'right', width: '100px', fontWeight: 800 }}>DESC.</th>
                            <th style={{ ...cell, textAlign: 'right', width: '110px', fontWeight: 800 }}>TOTAL</th>
                            {!readOnly && <th style={{ ...cell, width: '36px' }} />}
                        </tr>
                    </thead>

                    <tbody>
                        {items.length === 0 ? (
                            <tr>
                                <td
                                    colSpan={readOnly ? 7 : 8}
                                    style={{
                                        padding: '24px 16px', textAlign: 'center',
                                        color: 'var(--color-text-muted)'
                                    }}
                                >
                                    <span style={{ display: 'block', fontWeight: 700, marginBottom: '8px' }}>
                                        Este documento ainda não tem itens.
                                    </span>
                                    {!readOnly && (
                                        <button
                                            type="button"
                                            onClick={() => onChange([createTemporaryItem()])}
                                            style={{
                                                display: 'inline-flex', alignItems: 'center', gap: '6px',
                                                padding: '8px 16px', borderRadius: '6px', border: 'none',
                                                cursor: 'pointer', backgroundColor: 'var(--color-primary)',
                                                color: '#fff', fontWeight: 800, fontSize: '0.75rem'
                                            }}
                                        >
                                            <Plus size={14} /> Adicionar linha
                                        </button>
                                    )}
                                </td>
                            </tr>
                        ) : items.map(item => (
                            <tr key={item.tempId} style={{ borderBottom: '1px solid var(--color-border)' }}>
                                <td style={cell}>
                                    <CatalogItemAutocomplete
                                        value={item.itemCatalogCode
                                            ? `[${item.itemCatalogCode}] ${item.description}`
                                            : item.description}
                                        itemCatalogId={item.itemCatalogId}
                                        disabled={readOnly}
                                        onChange={(desc, catId, catCode, defaultUnitId) => patch(item.tempId, {
                                            description: desc,
                                            itemCatalogId: catId,
                                            itemCatalogCode: catCode,
                                            // A catalog item brings its unit, but never overrides one
                                            // the user (or the reading) already settled on.
                                            unitId: item.unitId ?? defaultUnitId,
                                            unitCode: item.unitCode ??
                                                (units.find(u => u.id === defaultUnitId)?.code ?? null)
                                        })}
                                        style={{ padding: '6px 8px', marginTop: 0, fontSize: '0.78rem' }}
                                    />
                                </td>

                                <td style={cell}>
                                    <select
                                        value={item.unitId ?? ''}
                                        disabled={readOnly}
                                        onChange={e => {
                                            const id = e.target.value ? Number(e.target.value) : null;
                                            patch(item.tempId, {
                                                unitId: id,
                                                unitCode: units.find(u => u.id === id)?.code ?? null
                                            });
                                        }}
                                        style={input}
                                    >
                                        <option value="">--</option>
                                        {units.filter(u => u.isActive !== false).map(u => (
                                            <option key={u.id} value={u.id}>{u.code}</option>
                                        ))}
                                    </select>
                                </td>

                                <td style={cell}>
                                    <input
                                        type="number" step="0.01" min="0"
                                        value={item.quantity}
                                        disabled={readOnly}
                                        onChange={e => patch(item.tempId, {
                                            quantity: Number(e.target.value) || 0
                                        })}
                                        style={{ ...input, textAlign: 'right' }}
                                    />
                                </td>

                                <td style={cell}>
                                    <input
                                        type="number" step="0.01" min="0"
                                        value={item.unitPrice}
                                        disabled={readOnly}
                                        onChange={e => patch(item.tempId, {
                                            unitPrice: Number(e.target.value) || 0
                                        })}
                                        style={{ ...input, textAlign: 'right' }}
                                    />
                                </td>

                                <td style={cell}>
                                    <select
                                        value={item.ivaRateId ?? ''}
                                        disabled={readOnly}
                                        onChange={e => patch(item.tempId, {
                                            ivaRateId: e.target.value ? Number(e.target.value) : null
                                        })}
                                        style={input}
                                    >
                                        <option value="">--</option>
                                        {ivaRates.map(r => (
                                            <option key={r.id} value={r.id}>{r.ratePercent}%</option>
                                        ))}
                                    </select>
                                </td>

                                <td style={cell}>
                                    <input
                                        type="number" step="0.01" min="0"
                                        value={item.discountAmount ?? ''}
                                        disabled={readOnly}
                                        onChange={e => patch(item.tempId, {
                                            discountAmount: e.target.value === '' ? null : Number(e.target.value)
                                        })}
                                        style={{ ...input, textAlign: 'right' }}
                                    />
                                </td>

                                <td style={{
                                    ...cell, textAlign: 'right', fontWeight: 800,
                                    color: 'var(--color-text-main)', paddingTop: '12px'
                                }}>
                                    {formatCurrencyAO(item.totalAmount)}
                                </td>

                                {!readOnly && (
                                    <td style={{ ...cell, textAlign: 'center', paddingTop: '10px' }}>
                                        <button
                                            type="button"
                                            onClick={() => onChange(items.filter(i => i.tempId !== item.tempId))}
                                            aria-label="Remover item"
                                            style={{
                                                background: 'none', border: 'none', cursor: 'pointer',
                                                color: '#b91c1c', padding: '2px', lineHeight: 0
                                            }}
                                        >
                                            <Trash2 size={14} />
                                        </button>
                                    </td>
                                )}
                            </tr>
                        ))}
                    </tbody>

                    <tfoot>
                        <tr style={{ backgroundColor: 'var(--color-bg-page)', fontWeight: 800 }}>
                            <td colSpan={6} style={{ ...cell, textAlign: 'right', padding: '10px 8px' }}>
                                SOMA DOS ITENS ({currency ?? ''}):
                            </td>
                            <td style={{
                                ...cell, textAlign: 'right', padding: '10px 8px',
                                color: mismatch ? '#b45309' : 'var(--color-primary)'
                            }}>
                                {formatCurrencyAO(total)}
                            </td>
                            {!readOnly && <td />}
                        </tr>
                    </tfoot>
                </table>
            </div>

            {mismatch && (
                <p role="alert" style={{
                    margin: 0, padding: '8px 10px', borderRadius: '6px', fontSize: '0.75rem',
                    fontWeight: 600, color: '#b45309',
                    border: '1px solid #fcd34d', backgroundColor: 'rgba(180,83,9,0.06)'
                }}>
                    A soma dos itens ({formatCurrencyAO(total)}) não corresponde ao total do documento
                    ({formatCurrencyAO(stated)}). Corrija os itens ou o total antes de confirmar.
                </p>
            )}
        </div>
    );
}
