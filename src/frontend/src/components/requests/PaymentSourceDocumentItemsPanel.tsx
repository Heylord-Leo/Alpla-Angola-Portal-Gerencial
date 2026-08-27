import React, { useMemo, useState } from 'react';
import { AlertTriangle, Plus } from 'lucide-react';
import { api } from '../../lib/api';
import { IvaRate, Unit } from '../../types';
import { PaymentSourceDocumentDto } from '../../types/paymentSourceDocument';
import { TemporaryPaymentItem } from '../../lib/paymentRequestCreation';
import {
    buildRecoveryLineItemPayload,
    documentItemEntryBlockers,
    recoveryItemValidationError
} from '../../lib/paymentSourceDocumentRecovery';
import { PaymentDocumentItemsEditor } from './PaymentDocumentItemsEditor';
import { formatCurrencyAO } from '../../lib/utils';

interface Props {
    requestId: string;
    document: PaymentSourceDocumentDto;
    units: Unit[];
    ivaRates: IvaRate[];
    readOnly: boolean;
    /** Refreshes the collection after items are persisted, so they appear as real, linked lines. */
    onItemsPersisted: () => Promise<void>;
}

/**
 * The items of ONE persisted payment source document, with the recovery path for a document that
 * has none.
 *
 * <p>The review screen used to render only a "N item(ns)" count for a persisted document — never an
 * item editor — so a draft whose document ended up with zero linked items (REQ-276) could not have
 * its first item added and was permanently unsubmittable. This panel fills that gap: it lists the
 * items the document already has and, while the request is editable and the document's own metadata
 * is complete, lets the user add more. Each new line is persisted through the SAME line-item
 * endpoint the creation flow uses, carrying the document's id so it is linked, not orphaned.</p>
 */
export function PaymentSourceDocumentItemsPanel({
    requestId, document, units, ivaRates, readOnly, onItemsPersisted
}: Props) {
    const [draftItems, setDraftItems] = useState<TemporaryPaymentItem[]>([]);
    const [isSaving, setIsSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const blockers = useMemo(() => documentItemEntryBlockers(document), [document]);
    const canAdd = blockers.length === 0;

    // The buffer's lines validate against what the document still needs covered, not the whole gross
    // — a document that already has items must not flag the new lines as a mismatch on their own.
    const remainingTotal = Math.max(0, (document.grossAmount ?? 0) - (document.itemsTotal ?? 0));

    const persist = async () => {
        setError(null);

        const invalid = draftItems.map(recoveryItemValidationError).find(e => e != null);
        if (draftItems.length === 0) { setError('Adicione pelo menos uma linha antes de guardar.'); return; }
        if (invalid) { setError(invalid); return; }

        setIsSaving(true);
        try {
            for (const item of draftItems) {
                await api.requests.createLineItem(
                    requestId, buildRecoveryLineItemPayload(document, item));
            }
            setDraftItems([]);
            await onItemsPersisted();
        } catch (e: any) {
            setError(e?.message ?? 'Não foi possível guardar os itens.');
        } finally {
            setIsSaving(false);
        }
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', marginTop: '4px' }}>
            {/* What the document already has — the number the gross must eventually agree with. */}
            {document.items.length > 0 ? (
                <div style={{
                    overflowX: 'auto', border: '1px solid var(--color-border)',
                    borderRadius: 'var(--radius-sm, 8px)'
                }}>
                    <table style={{
                        width: '100%', minWidth: '520px', borderCollapse: 'collapse', fontSize: '0.75rem'
                    }}>
                        <thead>
                            <tr style={{
                                backgroundColor: 'var(--color-bg-page)',
                                borderBottom: '1px solid var(--color-border)'
                            }}>
                                <th style={{ ...th, textAlign: 'left' }}>DESCRIÇÃO</th>
                                <th style={{ ...th, textAlign: 'center', width: '70px' }}>QTD</th>
                                <th style={{ ...th, textAlign: 'center', width: '80px' }}>UNID.</th>
                                <th style={{ ...th, textAlign: 'right', width: '120px' }}>TOTAL c/ IVA</th>
                            </tr>
                        </thead>
                        <tbody>
                            {document.items.map(it => (
                                <tr key={it.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
                                    <td style={td}>{it.description}</td>
                                    <td style={{ ...td, textAlign: 'center' }}>{it.quantity}</td>
                                    <td style={{ ...td, textAlign: 'center' }}>{it.unitCode ?? '—'}</td>
                                    <td style={{ ...td, textAlign: 'right', fontWeight: 700 }}>
                                        {formatCurrencyAO(it.totalAmount)}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            ) : (
                <p style={{
                    margin: 0, fontSize: '0.78rem', fontWeight: 700, color: '#b45309',
                    display: 'inline-flex', alignItems: 'center', gap: '6px'
                }}>
                    <AlertTriangle size={14} /> Este documento ainda não tem itens. Adicione pelo menos
                    um item para que o pedido possa ser submetido.
                </p>
            )}

            {readOnly ? null : !canAdd ? (
                // §7 — the affordance is disabled, but never silently: the reason is stated, and it
                // disappears on its own the moment the document's required fields are supplied.
                <div style={{
                    padding: '10px 12px', borderRadius: '8px', fontSize: '0.76rem',
                    border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)',
                    color: 'var(--color-text-muted)'
                }}>
                    <p style={{ margin: '0 0 6px', fontWeight: 800, color: 'var(--color-text-main)' }}>
                        Complete os campos obrigatórios do documento antes de adicionar itens.
                    </p>
                    <ul style={{ margin: 0, paddingLeft: '18px', display: 'flex', flexDirection: 'column', gap: '3px' }}>
                        {blockers.map((b, i) => <li key={i}>{b}</li>)}
                    </ul>
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <PaymentDocumentItemsEditor
                        items={draftItems}
                        onChange={setDraftItems}
                        units={units}
                        ivaRates={ivaRates}
                        currency={document.currency ?? null}
                        documentTotal={remainingTotal > 0 ? remainingTotal : null}
                        readOnly={false}
                    />

                    {error && (
                        <p role="alert" style={{
                            margin: 0, padding: '8px 10px', borderRadius: '6px', fontSize: '0.75rem',
                            fontWeight: 600, color: '#b91c1c',
                            border: '1px solid #fca5a5', backgroundColor: 'rgba(185,28,28,0.08)'
                        }}>
                            {error}
                        </p>
                    )}

                    <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
                        <button
                            type="button"
                            onClick={() => void persist()}
                            disabled={isSaving || draftItems.length === 0}
                            style={{
                                display: 'inline-flex', alignItems: 'center', gap: '6px',
                                padding: '8px 14px', borderRadius: '8px', border: 'none',
                                cursor: isSaving || draftItems.length === 0 ? 'default' : 'pointer',
                                opacity: isSaving || draftItems.length === 0 ? 0.6 : 1,
                                backgroundColor: 'var(--color-primary)', color: '#fff',
                                fontWeight: 700, fontSize: '0.8rem'
                            }}
                        >
                            <Plus size={14} /> {isSaving ? 'A guardar…' : 'Guardar itens'}
                        </button>
                    </div>
                </div>
            )}
        </div>
    );
}

const th: React.CSSProperties = { padding: '6px 8px', fontWeight: 800, color: 'var(--color-text-muted)' };
const td: React.CSSProperties = { padding: '6px 8px', color: 'var(--color-text-main)' };
