import React, { useEffect, useState } from 'react';
import { CheckCircle2, ChevronDown, ChevronUp, Loader2, Save } from 'lucide-react';
import { api } from '../../lib/api';

/**
 * The optional supplier details a DRAFT supplier can be enriched with, exactly as the Quotation
 * wizard offers them.
 *
 * <p>Extracted verbatim from <c>WizardStepSupplierValidation</c> so both flows use one
 * implementation: same labels, same optional-by-default rule, and the same persistence —
 * <c>PUT /lookups/suppliers/{id}/ficha</c>, a <b>second call after creation</b> rather than a fatter
 * create payload. Quick-create deliberately asks only for name and NIF; everything here is
 * enrichment of a record that already exists.</p>
 *
 * <p>Nothing is mandatory. A DRAFT supplier is legitimate on its own and is completed later in
 * <b>Contratos → Fichas de Fornecedor</b>; this panel only lets the user save a trip.</p>
 */

export interface SupplierAdditionalInfo {
    Name: string;
    TaxId: string;
    PrimaveraCode: string;
    Address: string;
    ContactName1: string;
    ContactEmail1: string;
    ContactPhone1: string;
    BankIban: string;
    BankAccountNumber: string;
    BankSwift: string;
    PaymentTerms: string;
}

export const EMPTY_SUPPLIER_ADDITIONAL_INFO: SupplierAdditionalInfo = {
    Name: '', TaxId: '', PrimaveraCode: '', Address: '', ContactName1: '', ContactEmail1: '',
    ContactPhone1: '', BankIban: '', BankAccountNumber: '', BankSwift: '', PaymentTerms: ''
};

interface Props {
    supplierId: number;
    /** Seed values — from the document extraction, or from what is already on the record. */
    initial: SupplierAdditionalInfo;
    /** `panel` is the wizard's collapsible box; `inline` is already inside a modal step. */
    variant?: 'panel' | 'inline';
    defaultExpanded?: boolean;
    onSaved?: () => void;
}

export function SupplierAdditionalInfoPanel({
    supplierId, initial, variant = 'panel', defaultExpanded = false, onSaved
}: Props) {
    const [expanded, setExpanded] = useState(defaultExpanded);
    const [data, setData] = useState<SupplierAdditionalInfo>(initial);
    const [isSaving, setIsSaving] = useState(false);
    const [isSaved, setIsSaved] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => { setData(initial); }, [
        initial.Name, initial.TaxId, initial.PrimaveraCode, initial.Address, initial.ContactName1,
        initial.ContactEmail1, initial.ContactPhone1, initial.BankIban, initial.BankAccountNumber,
        initial.BankSwift, initial.PaymentTerms
    ]);

    const save = async () => {
        if (!supplierId) return;
        setIsSaving(true);
        setError(null);

        try {
            await api.lookups.updateSupplierFicha(supplierId, data);
            setIsSaved(true);
            if (variant === 'panel') setExpanded(false);
            onSaved?.();
        } catch (e: any) {
            // Reported in place, never as a toast that disappears before it can be acted on.
            setError(e?.message ?? 'Não foi possível guardar as informações adicionais.');
        } finally {
            setIsSaving(false);
        }
    };

    const field = (label: string, key: keyof SupplierAdditionalInfo, type = 'text') => (
        <div>
            <label style={{
                display: 'block', fontSize: '0.75rem', fontWeight: 500,
                color: 'var(--color-text-muted)', marginBottom: '4px'
            }}>
                {label}
            </label>
            <input
                type={type}
                value={data[key]}
                onChange={e => setData({ ...data, [key]: e.target.value })}
                style={{
                    width: '100%', boxSizing: 'border-box', padding: '8px',
                    border: '1px solid var(--color-border)', borderRadius: '4px', fontSize: '0.875rem',
                    backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)'
                }}
            />
        </div>
    );

    const body = (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
            <p style={{ margin: 0, fontSize: '0.8125rem', color: 'var(--color-text-muted)' }}>
                Pode completar estas informações agora para acelerar a validação posterior do
                fornecedor. Estes campos são opcionais e também podem ser preenchidos depois em
                <strong> Contratos → Fichas de Fornecedor</strong>.
            </p>

            <div style={{
                display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px'
            }}>
                {field('Morada', 'Address')}
                {field('Condições de Pagamento', 'PaymentTerms')}
                {field('Nome Contato', 'ContactName1')}
                {field('Email', 'ContactEmail1', 'email')}
                {field('Telemóvel', 'ContactPhone1')}
                {field('IBAN', 'BankIban')}
                {field('Conta', 'BankAccountNumber')}
                {field('SWIFT', 'BankSwift')}
            </div>

            {error && (
                <div role="alert" style={{
                    padding: '8px 10px', borderRadius: '6px', fontSize: '0.78rem', fontWeight: 600,
                    border: '1px solid #fca5a5', backgroundColor: 'rgba(185,28,28,0.08)', color: '#b91c1c'
                }}>
                    {error}
                </div>
            )}

            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '8px' }}>
                <button
                    type="button"
                    onClick={save}
                    disabled={isSaving}
                    style={{
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px',
                        backgroundColor: 'var(--color-primary)', color: '#fff', border: 'none',
                        borderRadius: '4px', fontSize: '0.875rem', fontWeight: 500,
                        cursor: isSaving ? 'not-allowed' : 'pointer', opacity: isSaving ? 0.7 : 1
                    }}
                >
                    {isSaving ? <Loader2 size={16} className="spin-icon" /> : <Save size={16} />}
                    Salvar Informações Adicionais
                </button>
            </div>
        </div>
    );

    if (variant === 'inline') return body;

    return (
        <div style={{
            marginTop: '16px', border: '1px solid var(--color-border)', borderRadius: '6px',
            backgroundColor: 'var(--color-bg-surface)', overflow: 'hidden'
        }}>
            <button
                type="button"
                onClick={() => setExpanded(!expanded)}
                style={{
                    width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                    padding: '12px 16px', border: 'none', cursor: 'pointer',
                    backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-main)'
                }}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <span style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                        Informações adicionais do fornecedor
                    </span>
                    <span style={{
                        fontSize: '0.75rem', color: 'var(--color-text-muted)',
                        backgroundColor: 'var(--color-border)', padding: '2px 6px', borderRadius: '4px'
                    }}>
                        Opcional
                    </span>
                    {isSaved && <CheckCircle2 size={14} color="var(--color-status-success-text, #15803d)" />}
                </div>
                {expanded ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
            </button>

            <div style={{
                display: expanded ? 'block' : 'none', padding: '16px',
                borderTop: '1px solid var(--color-border)'
            }}>
                {body}
            </div>
        </div>
    );
}
