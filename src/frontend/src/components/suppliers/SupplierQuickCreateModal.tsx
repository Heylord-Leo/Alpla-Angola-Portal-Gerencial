import React, { useState } from 'react';
import { createPortal } from 'react-dom';
import { Building2, CheckCircle2, X } from 'lucide-react';
import { QuickSupplierModal } from '../Buyer/QuickSupplierModal';
import {
    EMPTY_SUPPLIER_ADDITIONAL_INFO,
    SupplierAdditionalInfo,
    SupplierAdditionalInfoPanel
} from './SupplierAdditionalInfoPanel';
import { Z_INDEX } from '../../constants/ui';

export interface CreatedSupplier {
    id: number;
    name: string;
    portalCode?: string;
}

interface Props {
    isOpen: boolean;
    onClose: () => void;
    /** Fired once the supplier exists — before the optional enrichment step. */
    onCreated: (supplier: CreatedSupplier) => void;

    initialName: string;
    initialTaxId: string;
    /** Everything the extraction read about this supplier, for pre-filling the optional step. */
    extraction?: Partial<SupplierAdditionalInfo>;
}

/**
 * Supplier creation for the PAYMENT source-document editor, with the same reach the Quotation
 * wizard has.
 *
 * <p>Two steps, because that is how the Portal actually stores a supplier — not because a modal
 * wanted two pages:</p>
 *
 * <ol>
 *   <li><b>Identity</b> — {@link QuickSupplierModal}, reused whole. Name and NIF, the authoritative
 *   duplicate check, the internal-NIF fallback and the create-without-NIF path all live there and
 *   are not reimplemented.</li>
 *   <li><b>Optional details</b> — {@link SupplierAdditionalInfoPanel}, the same component the
 *   quotation wizard shows after creating a supplier, persisted through the same
 *   <c>PUT /lookups/suppliers/{'{'}id{'}'}/ficha</c>.</li>
 * </ol>
 *
 * <p>The supplier is usable after step 1. Step 2 is genuinely optional — the record is a DRAFT
 * either way, completed later in <b>Contratos → Fichas de Fornecedor</b>, and the payment document
 * may proceed with it under the existing rules.</p>
 */
export function SupplierQuickCreateModal({
    isOpen, onClose, onCreated, initialName, initialTaxId, extraction
}: Props) {
    const [created, setCreated] = useState<CreatedSupplier | null>(null);

    const close = () => { setCreated(null); onClose(); };

    // ── Step 1: identity, duplicates, the record itself ──
    if (!created) {
        return (
            <QuickSupplierModal
                isOpen={isOpen}
                onClose={close}
                mode="PAYMENT_OCR"
                initialName={initialName}
                initialTaxId={initialTaxId}
                extractedName={initialName}
                extractedTaxId={initialTaxId}
                onSuccess={supplier => {
                    // Selected immediately: the document must not wait on an optional step.
                    onCreated(supplier);
                    setCreated(supplier);
                }}
            />
        );
    }

    if (!isOpen) return null;

    // ── Step 2: optional enrichment ──
    const seed: SupplierAdditionalInfo = {
        ...EMPTY_SUPPLIER_ADDITIONAL_INFO,
        ...extraction,
        Name: created.name,
        TaxId: extraction?.TaxId ?? initialTaxId
    };

    return createPortal(
        <div
            role="dialog"
            aria-modal="true"
            aria-label="Informações adicionais do fornecedor"
            onMouseDown={e => { if (e.target === e.currentTarget) close(); }}
            style={{
                position: 'fixed', inset: 0, zIndex: Z_INDEX.MODAL as unknown as number,
                display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '20px',
                backgroundColor: 'rgba(0,0,0,0.8)'
            }}
        >
            <div
                onMouseDown={e => e.stopPropagation()}
                style={{
                    width: '100%', maxWidth: '720px', maxHeight: '90vh', overflowY: 'auto',
                    padding: '28px', borderRadius: 'var(--radius-md)',
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-md)'
                }}
            >
                <div style={{
                    display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between',
                    gap: '12px', marginBottom: '6px'
                }}>
                    <h3 style={{
                        margin: 0, display: 'flex', alignItems: 'center', gap: '8px',
                        fontSize: '1rem', fontWeight: 800, color: 'var(--color-text-main)'
                    }}>
                        <Building2 size={18} /> Informações adicionais do fornecedor
                    </h3>
                    <button
                        type="button"
                        onClick={close}
                        aria-label="Fechar"
                        style={{
                            background: 'none', border: 'none', cursor: 'pointer', padding: '2px',
                            color: 'var(--color-text-muted)', lineHeight: 0
                        }}
                    >
                        <X size={18} />
                    </button>
                </div>

                <div style={{
                    display: 'flex', alignItems: 'flex-start', gap: '10px', margin: '12px 0 18px',
                    padding: '12px', borderRadius: 'var(--radius-sm)',
                    border: '1px solid var(--color-status-success-border, #bbf7d0)',
                    backgroundColor: 'var(--color-status-success-light, rgba(21,128,61,0.08))'
                }}>
                    <CheckCircle2 size={16} style={{ color: '#15803d', flexShrink: 0, marginTop: '1px' }} />
                    <p style={{ margin: 0, fontSize: '0.8rem', fontWeight: 600, lineHeight: 1.5, color: 'var(--color-text-main)' }}>
                        <strong>{created.name}</strong> foi criado e já está selecionado neste documento.
                        Continua a ser um <strong>rascunho</strong> e deve ser completado em
                        <strong> Contratos → Fichas de Fornecedor</strong> antes da emissão de uma
                        ordem de compra. Pode adiantar parte dessa informação aqui, se quiser.
                    </p>
                </div>

                <SupplierAdditionalInfoPanel
                    supplierId={created.id}
                    initial={seed}
                    variant="inline"
                    onSaved={close}
                />

                <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '18px' }}>
                    <button
                        type="button"
                        onClick={close}
                        style={{
                            height: '42px', padding: '0 22px', cursor: 'pointer', fontWeight: 800,
                            borderRadius: 'var(--radius-sm)', fontSize: '0.85rem',
                            border: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-main)'
                        }}
                    >
                        CONTINUAR SEM PREENCHER
                    </button>
                </div>
            </div>
        </div>,
        document.body
    );
}
