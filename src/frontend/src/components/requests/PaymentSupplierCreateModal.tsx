import React, { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { AlertTriangle, Building2, Info, Loader2, Save, X } from 'lucide-react';
import { api } from '../../lib/api';
import { Z_INDEX } from '../../constants/ui';

/**
 * Supplier registration for the PAYMENT source-document editor.
 *
 * <p><b>Deliberately self-contained.</b> It offers the same capability as the quotation wizard's
 * supplier step — identity plus the optional Ficha fields — but shares no runtime code with it, so
 * nothing here can change how Quotation Management behaves. The duplication is intentional and
 * approved: the alternative was a shared component whose every future edit would have to be
 * re-validated against two flows.</p>
 *
 * <p><b>One view, one Save.</b> The Portal stores a supplier in two calls — create, then update the
 * ficha — but that is a persistence detail, not a workflow the user should have to walk through.
 * Both calls happen behind a single action, and the supplier exists even if the second one fails.</p>
 *
 * <p>The backend stays authoritative for duplicates: this never decides that a supplier is new. It
 * asks, and renders whatever the server decided.</p>
 */

export interface CreatedSupplier {
    id: number;
    name: string;
    portalCode?: string;
}

/** Everything the extraction read about the supplier. Absent fields stay absent. */
export interface SupplierPrefill {
    name?: string | null;
    taxId?: string | null;
    address?: string | null;
    paymentTerms?: string | null;
    contactName?: string | null;
    email?: string | null;
    phone?: string | null;
    bankIban?: string | null;
    bankAccountNumber?: string | null;
    bankSwift?: string | null;
}

interface Props {
    isOpen: boolean;
    onClose: () => void;
    onCreated: (supplier: CreatedSupplier) => void;
    prefill: SupplierPrefill;
}

interface DuplicateState {
    existing: any | null;
    message: string;
    /** A hard conflict cannot be overridden; a soft one may be confirmed explicitly. */
    hard: boolean;
}

export function PaymentSupplierCreateModal({ isOpen, onClose, onCreated, prefill }: Props) {
    const [name, setName] = useState('');
    const [taxId, setTaxId] = useState('');
    const [extra, setExtra] = useState({
        Address: '', PaymentTerms: '', ContactName1: '', ContactEmail1: '',
        ContactPhone1: '', BankIban: '', BankAccountNumber: '', BankSwift: ''
    });

    const [isSaving, setIsSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [fichaWarning, setFichaWarning] = useState<string | null>(null);
    const [duplicate, setDuplicate] = useState<DuplicateState | null>(null);
    const [internalNif, setInternalNif] = useState<{ name?: string; taxId?: string } | null>(null);

    const dialogRef = useRef<HTMLDivElement>(null);

    // Seeded from the reading each time the modal opens. Only what the document actually carried.
    useEffect(() => {
        if (!isOpen) return;

        setName(prefill.name ?? '');
        setTaxId(prefill.taxId ?? '');
        setExtra({
            Address: prefill.address ?? '',
            PaymentTerms: prefill.paymentTerms ?? '',
            ContactName1: prefill.contactName ?? '',
            ContactEmail1: prefill.email ?? '',
            ContactPhone1: prefill.phone ?? '',
            BankIban: prefill.bankIban ?? '',
            BankAccountNumber: prefill.bankAccountNumber ?? '',
            BankSwift: prefill.bankSwift ?? ''
        });
        setError(null);
        setFichaWarning(null);
        setDuplicate(null);
        setInternalNif(null);
        setIsSaving(false);
    }, [isOpen]);

    useEffect(() => {
        if (!isOpen) return;
        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'Escape') { e.stopPropagation(); onClose(); }
        };
        window.document.addEventListener('keydown', onKey, true);
        return () => window.document.removeEventListener('keydown', onKey, true);
    }, [isOpen, onClose]);

    if (!isOpen) return null;

    const hasExtra = Object.values(extra).some(v => v.trim().length > 0);

    /** Uses a supplier the server says already exists, instead of creating a second one. */
    const useExisting = (s: any) => {
        if (!s?.id) return;
        onCreated({ id: s.id, name: s.name, portalCode: s.portalCode });
        onClose();
    };

    const save = async (confirmDespiteDuplicate = false) => {
        if (!name.trim()) { setError('O nome do fornecedor é obrigatório.'); return; }

        setIsSaving(true);
        setError(null);
        setFichaWarning(null);

        try {
            const res = await api.lookups.createSupplierFromPaymentOcr({
                name: name.trim(),
                taxId: taxId.trim() || undefined,
                confirmCreateDespiteDuplicate: confirmDespiteDuplicate,
                extractedName: prefill.name ?? undefined,
                extractedTaxId: prefill.taxId ?? undefined,
                internalCompanyTaxIdExtracted: internalNif?.taxId || undefined
            });

            // ── The extracted NIF belongs to an internal ALPLA company ──
            // Drop it and let the user proceed by name. The internal NIF is never re-sent.
            if (res && (res.status === 'InternalCompanyTaxId' || res.code === 'INTERNAL_COMPANY_TAX_ID')) {
                setInternalNif(res.internalCompany ?? { taxId: taxId.trim() });
                setTaxId('');
                setDuplicate(null);
                setIsSaving(false);
                return;
            }

            // ── Same NIF, or exact same name: not overridable ──
            if (res && res.status === 'Conflict') {
                const s = res.supplier;
                setDuplicate({
                    existing: s ?? null,
                    hard: true,
                    message: (res.message || 'Já existe um fornecedor com estes dados.') +
                             (s ? ` Fornecedor existente: ${s.name}${s.isActive === false ? ' (atualmente inativo)' : ''}.` : '')
                });
                setIsSaving(false);
                return;
            }

            // ── Same name, different NIF: possible but must be confirmed ──
            if (res && res.status === 'DuplicateSuspected') {
                setDuplicate({
                    existing: (res.candidates && res.candidates[0]) || null,
                    hard: false,
                    message: 'Já existe um fornecedor com este mesmo nome, mas com NIF diferente. ' +
                             'Confirme se são realmente empresas distintas antes de continuar.'
                });
                setIsSaving(false);
                return;
            }

            if (!res || res.status !== 'Created' || !res.supplier) {
                setError((res && res.message) || 'Não foi possível criar o fornecedor.');
                setIsSaving(false);
                return;
            }

            const created: CreatedSupplier = {
                id: res.supplier.id, name: res.supplier.name, portalCode: res.supplier.portalCode
            };

            // ── Second call, same Save ──
            // The supplier already exists at this point. If the ficha update fails, that is worth
            // saying, but it must not read as "the supplier was not created" — it was.
            if (hasExtra) {
                try {
                    await api.lookups.updateSupplierFicha(created.id, {
                        Name: created.name,
                        TaxId: taxId.trim(),
                        PrimaveraCode: '',
                        ...extra
                    });
                } catch (e: any) {
                    onCreated(created);
                    setFichaWarning(
                        `O fornecedor ${created.name} foi criado e já está selecionado, mas as ` +
                        'informações adicionais não foram guardadas. Pode completá-las em ' +
                        'Contratos → Fichas de Fornecedor.');
                    setIsSaving(false);
                    return;
                }
            }

            onCreated(created);
            onClose();
        } catch (e: any) {
            setError(e?.message ?? 'Não foi possível criar o fornecedor.');
            setIsSaving(false);
        }
    };

    const label: React.CSSProperties = {
        display: 'block', fontSize: '0.72rem', fontWeight: 700,
        color: 'var(--color-text-muted)', marginBottom: '4px'
    };
    const input: React.CSSProperties = {
        width: '100%', boxSizing: 'border-box', padding: '9px 10px', fontSize: '0.85rem',
        borderRadius: 'var(--radius-sm, 6px)', border: '1px solid var(--color-border)',
        backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)'
    };

    const field = (
        text: string, key: keyof typeof extra, type = 'text'
    ) => (
        <div>
            <label style={label}>{text}</label>
            <input
                type={type}
                value={extra[key]}
                disabled={isSaving}
                onChange={e => setExtra({ ...extra, [key]: e.target.value })}
                style={input}
            />
        </div>
    );

    return createPortal(
        <div
            role="dialog"
            aria-modal="true"
            aria-label="Registar fornecedor"
            onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}
            style={{
                position: 'fixed', inset: 0, zIndex: Z_INDEX.MODAL as unknown as number,
                display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '20px',
                backgroundColor: 'rgba(0,0,0,0.8)'
            }}
        >
            <div
                ref={dialogRef}
                style={{
                    width: '100%', maxWidth: '760px', maxHeight: '90vh', overflowY: 'auto',
                    padding: '28px', borderRadius: 'var(--radius-md)',
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-md)'
                }}
            >
                {/* No <form>: every action is an explicit button, so nothing can be mistaken for a
                    submission of the request the user is composing behind this modal. */}
                <div style={{
                    display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between',
                    gap: '12px', marginBottom: '4px'
                }}>
                    <h3 style={{
                        margin: 0, display: 'flex', alignItems: 'center', gap: '8px',
                        fontSize: '1rem', fontWeight: 800, color: 'var(--color-text-main)'
                    }}>
                        <Building2 size={18} /> Registar fornecedor
                    </h3>
                    <button
                        type="button" onClick={onClose} aria-label="Fechar"
                        style={{
                            background: 'none', border: 'none', cursor: 'pointer', padding: '2px',
                            color: 'var(--color-text-muted)', lineHeight: 0
                        }}
                    >
                        <X size={18} />
                    </button>
                </div>

                <p style={{ margin: '0 0 18px', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                    Os dados abaixo foram lidos do documento. Reveja-os antes de guardar.
                </p>

                {error && (
                    <div role="alert" style={{
                        marginBottom: '14px', padding: '10px 12px', borderRadius: '6px',
                        border: '1px solid #fca5a5', backgroundColor: 'rgba(185,28,28,0.08)',
                        color: '#b91c1c', fontSize: '0.8rem', fontWeight: 600
                    }}>
                        {error}
                    </div>
                )}

                {fichaWarning && (
                    <div role="alert" style={{
                        marginBottom: '14px', padding: '10px 12px', borderRadius: '6px',
                        border: '1px solid #fcd34d', backgroundColor: 'rgba(180,83,9,0.08)',
                        color: '#b45309', fontSize: '0.8rem', fontWeight: 600
                    }}>
                        {fichaWarning}
                        <div style={{ marginTop: '10px' }}>
                            <button type="button" onClick={onClose} style={primaryBtn}>Fechar</button>
                        </div>
                    </div>
                )}

                {internalNif && (
                    <div style={{
                        display: 'flex', gap: '8px', marginBottom: '14px', padding: '10px 12px',
                        borderRadius: '6px', border: '1px solid #fcd34d',
                        backgroundColor: 'rgba(180,83,9,0.08)', color: '#b45309',
                        fontSize: '0.78rem', fontWeight: 600
                    }}>
                        <Info size={15} style={{ flexShrink: 0, marginTop: '1px' }} />
                        <span>
                            O NIF identificado no documento pertence à empresa interna
                            {internalNif.name ? ` ${internalNif.name}` : ''}
                            {internalNif.taxId ? ` (NIF ${internalNif.taxId})` : ''} e foi
                            desconsiderado. Confirme o nome do fornecedor e guarde — o NIF poderá ser
                            completado depois na ficha.
                        </span>
                    </div>
                )}

                {duplicate && (
                    <div role="alert" style={{
                        marginBottom: '14px', padding: '12px', borderRadius: '6px',
                        border: '1px solid #fcd34d', backgroundColor: 'rgba(180,83,9,0.08)'
                    }}>
                        <div style={{
                            display: 'flex', gap: '8px', color: '#b45309',
                            fontSize: '0.8rem', fontWeight: 700
                        }}>
                            <AlertTriangle size={15} style={{ flexShrink: 0, marginTop: '1px' }} />
                            <span>{duplicate.message}</span>
                        </div>

                        <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap', marginTop: '12px' }}>
                            {duplicate.existing && (
                                <button
                                    type="button"
                                    onClick={() => useExisting(duplicate.existing)}
                                    style={primaryBtn}
                                >
                                    Usar “{duplicate.existing.name}”
                                </button>
                            )}
                            {!duplicate.hard && (
                                <button
                                    type="button"
                                    disabled={isSaving}
                                    onClick={() => void save(true)}
                                    style={secondaryBtn}
                                >
                                    São empresas distintas — criar mesmo assim
                                </button>
                            )}
                        </div>
                    </div>
                )}

                {/* ── Identity ── */}
                <div style={{
                    display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
                    gap: '12px'
                }}>
                    <div>
                        <label style={label}>
                            Nome do fornecedor <span style={{ color: 'var(--color-status-red, #dc2626)' }}>*</span>
                        </label>
                        <input
                            type="text"
                            value={name}
                            disabled={isSaving}
                            onChange={e => { setName(e.target.value); setDuplicate(null); }}
                            style={input}
                        />
                    </div>
                    <div>
                        <label style={label}>NIF</label>
                        <input
                            type="text"
                            value={taxId}
                            disabled={isSaving}
                            onChange={e => { setTaxId(e.target.value); setDuplicate(null); }}
                            style={input}
                        />
                    </div>
                </div>

                {/* ── Optional, in the same view: no second step, no second save ── */}
                <div style={{
                    marginTop: '20px', paddingTop: '16px', borderTop: '1px solid var(--color-border)'
                }}>
                    <div style={{
                        display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px'
                    }}>
                        <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                            Informações adicionais do fornecedor
                        </span>
                        <span style={{
                            fontSize: '0.68rem', fontWeight: 700, color: 'var(--color-text-muted)',
                            backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)',
                            padding: '2px 7px', borderRadius: '4px'
                        }}>
                            Opcional
                        </span>
                    </div>
                    <p style={{ margin: '0 0 14px', fontSize: '0.76rem', color: 'var(--color-text-muted)' }}>
                        Guardadas junto com o fornecedor. Pode deixá-las em branco e completá-las depois
                        em <strong>Contratos → Fichas de Fornecedor</strong>.
                    </p>

                    <div style={{
                        display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
                        gap: '12px'
                    }}>
                        {field('Morada', 'Address')}
                        {field('Condições de Pagamento', 'PaymentTerms')}
                        {field('Nome do contacto', 'ContactName1')}
                        {field('Email', 'ContactEmail1', 'email')}
                        {field('Telemóvel', 'ContactPhone1')}
                        {field('IBAN', 'BankIban')}
                        {field('Conta', 'BankAccountNumber')}
                        {field('SWIFT', 'BankSwift')}
                    </div>
                </div>

                <div style={{
                    display: 'flex', alignItems: 'flex-start', gap: '8px', marginTop: '18px',
                    padding: '12px', borderRadius: 'var(--radius-sm)',
                    border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)'
                }}>
                    <Info size={15} style={{ color: 'var(--color-primary)', flexShrink: 0, marginTop: '1px' }} />
                    <p style={{ margin: 0, fontSize: '0.76rem', fontWeight: 600, lineHeight: 1.5, color: 'var(--color-text-main)' }}>
                        O fornecedor será criado como <strong>rascunho</strong> e deve ser completado
                        em <strong>Contratos → Fichas de Fornecedor</strong> antes da emissão de uma
                        ordem de compra.
                    </p>
                </div>

                <div style={{ display: 'flex', gap: '14px', marginTop: '20px' }}>
                    <button type="button" onClick={onClose} disabled={isSaving} style={{ ...secondaryBtn, flex: 1 }}>
                        CANCELAR
                    </button>
                    <button
                        type="button"
                        onClick={() => void save(false)}
                        disabled={isSaving || !name.trim() || (duplicate?.hard ?? false)}
                        style={{
                            ...primaryBtn, flex: 1,
                            opacity: (isSaving || !name.trim() || duplicate?.hard) ? 0.6 : 1,
                            cursor: (isSaving || !name.trim() || duplicate?.hard) ? 'not-allowed' : 'pointer'
                        }}
                    >
                        {isSaving ? <Loader2 size={16} className="spin-icon" /> : <Save size={16} />}
                        {isSaving ? 'A GUARDAR…' : 'GUARDAR FORNECEDOR'}
                    </button>
                </div>
            </div>
        </div>,
        document.body
    );
}

const primaryBtn: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
    height: '44px', padding: '0 20px', borderRadius: 'var(--radius-sm)', border: 'none',
    cursor: 'pointer', backgroundColor: 'var(--color-primary)', color: '#fff',
    fontWeight: 800, fontSize: '0.82rem'
};

const secondaryBtn: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
    height: '44px', padding: '0 20px', borderRadius: 'var(--radius-sm)', cursor: 'pointer',
    border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-page)',
    color: 'var(--color-text-main)', fontWeight: 800, fontSize: '0.82rem'
};
