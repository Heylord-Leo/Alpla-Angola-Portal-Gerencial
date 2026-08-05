import React, { useState, useEffect } from 'react';
import { X, Save, Building2, AlertCircle, RefreshCcw, FileText, Info, CheckCircle2, Search, ArrowLeft } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { Feedback, FeedbackType } from '../ui/Feedback';
import { Z_INDEX } from '../../constants/ui';
import { DropdownPortal } from '../ui/DropdownPortal';
import { SupplierAutocomplete } from '../SupplierAutocomplete';
import { motion, AnimatePresence } from 'framer-motion';

interface QuickSupplierModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: (supplier: { id: number; name: string; portalCode?: string }) => void;
    initialName?: string;
    initialTaxId?: string;
    /** 'PAYMENT_OCR' uses the contextual endpoint (DRAFT + backend-authoritative match/conflict handling). */
    mode?: 'GENERAL' | 'PAYMENT_OCR';
    extractedName?: string;
    extractedTaxId?: string;
}

export function QuickSupplierModal({ isOpen, onClose, onSuccess, initialName = '', initialTaxId = '', mode = 'GENERAL', extractedName, extractedTaxId }: QuickSupplierModalProps) {
    const [name, setName] = useState(initialName);
    const [taxId, setTaxId] = useState(initialTaxId);
    const [isSaving, setIsSaving] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
    const [fichaToast, setFichaToast] = useState(false);
    // Persistent conflict object (NOT the transient feedback): stays visible until the user edits
    // name/NIF, cancels, closes, or creation succeeds. `hard` = NIF/exact-name conflict (blocks even
    // with confirmation); soft = suspected duplicate (same name, different NIF) → "create anyway".
    const [duplicate, setDuplicate] = useState<{ existing: any | null; message: string; hard: boolean } | null>(null);
    // Internal-NIF fallback: when the extracted NIF belongs to an internal ALPLA company, we discard the
    // NIF, keep the extracted name, and re-match by NAME only. The user then picks an existing supplier or
    // (only when none matches) creates one without a NIF.
    const [internalCompany, setInternalCompany] = useState<{ id?: number; name?: string; taxId?: string } | null>(null);
    const [nameCandidates, setNameCandidates] = useState<any[]>([]);
    const [matchingByName, setMatchingByName] = useState(false);
    // Fallback decision flow (internal-NIF path):
    //  'suggestion'   — an existing supplier was found by name → decision screen (Use / Not this one)
    //  'alternatives' — user declined the suggestion → search other / create without NIF / back / cancel
    //  'create'       — user consciously creates a supplier WITHOUT a NIF
    const [fbStage, setFbStage] = useState<'suggestion' | 'alternatives' | 'create'>('suggestion');
    const [droppedInternalNif, setDroppedInternalNif] = useState<string | null>(null);
    const [rejectedSupplierId, setRejectedSupplierId] = useState<number | null>(null);
    const [confirmNoNif, setConfirmNoNif] = useState(false);
    const [showManualSearch, setShowManualSearch] = useState(false);

    // Sync name and taxId with initial props when modal opens or props change
    useEffect(() => {
        if (isOpen) {
            setName(initialName);
            setTaxId(initialTaxId);
            setFeedback({ type: 'error', message: null });
            setFieldErrors({});
            setFichaToast(false);
            setDuplicate(null);
            setInternalCompany(null);
            setNameCandidates([]);
            setMatchingByName(false);
            setFbStage('suggestion');
            setDroppedInternalNif(null);
            setRejectedSupplierId(null);
            setConfirmNoNif(false);
            setShowManualSearch(false);
        }
    }, [isOpen, initialName, initialTaxId]);

    if (!isOpen) return null;

    // Contextual creation from the Payment OCR flow — backend is authoritative for match/conflict.
    // `audit` carries provenance for the no-NIF path (internal NIF dropped, suggestion declined).
    const createViaPaymentOcr = async (confirmCreateDespiteDuplicate: boolean, audit?: { internalNif?: string | null; rejectedId?: number | null }) => {
        const res = await api.lookups.createSupplierFromPaymentOcr({
            name: name.trim(),
            taxId: taxId.trim() || undefined,
            confirmCreateDespiteDuplicate,
            extractedName: extractedName ?? initialName,
            extractedTaxId: extractedTaxId ?? initialTaxId,
            internalCompanyTaxIdExtracted: audit?.internalNif || undefined,
            rejectedSuggestedSupplierId: audit?.rejectedId ?? undefined,
        });
        if (res && res.status === 'Created' && res.supplier) {
            setDuplicate(null);
            setFichaToast(true);
            setTimeout(() => { onSuccess({ id: res.supplier.id, name: res.supplier.name, portalCode: res.supplier.portalCode }); onClose(); }, 300);
            return;
        }
        // Internal-company NIF: the extracted NIF belongs to an internal ALPLA company. Discard the NIF,
        // keep the name, and re-match by name only (never re-send the internal NIF).
        if (res && (res.status === 'InternalCompanyTaxId' || res.code === 'INTERNAL_COMPANY_TAX_ID')) {
            setDroppedInternalNif(res.internalCompany?.taxId || taxId.trim() || null);
            setTaxId('');
            setDuplicate(null);
            setInternalCompany(res.internalCompany || { name: undefined });
            await runNameOnlyMatch();
            setIsSaving(false);
            return;
        }
        // Hard conflict: same NIF (active or inactive) or exact name → block even with confirmation.
        if (res && res.status === 'Conflict') {
            const s = res.supplier;
            const inactiveNote = s && s.isActive === false ? ' (atualmente inativo)' : '';
            setDuplicate({
                existing: s,
                hard: true,
                message: (res.message || 'Já existe um fornecedor com estes dados.') + (s ? ` Fornecedor existente: ${s.name}${inactiveNote}.` : '')
            });
            setIsSaving(false);
            return;
        }
        // Soft: same normalized name, different NIF → keep the alert and allow explicit confirmation.
        if (res && res.status === 'DuplicateSuspected') {
            const c = (res.candidates && res.candidates[0]) || null;
            setDuplicate({
                existing: c,
                hard: false,
                message: 'Já existe um fornecedor com este mesmo nome, mas com NIF diferente. Confirme se são realmente empresas distintas antes de continuar.'
            });
            setIsSaving(false);
            return;
        }
        setFeedback({ type: 'error', message: (res && res.message) || 'Não foi possível criar o fornecedor.' });
        setIsSaving(false);
    };

    // Re-match by NAME only (used by the internal-NIF fallback). Never sends the internal NIF.
    // Routes to the decision screen when an existing supplier is found, or straight to the no-NIF
    // creation form when nothing matches.
    const runNameOnlyMatch = async () => {
        setConfirmNoNif(false);
        setShowManualSearch(false);
        if (!name.trim()) { setNameCandidates([]); setFbStage('create'); return; }
        setMatchingByName(true);
        try {
            const match = await api.lookups.matchSupplier(name.trim(), undefined);
            const cands = (match?.candidates && match.candidates.length > 0)
                ? match.candidates
                : (match?.supplier ? [match.supplier] : []);
            setNameCandidates(cands);
            setFbStage(cands.length > 0 ? 'suggestion' : 'create'); // no match → create-without-NIF form
        } catch {
            setNameCandidates([]);
            setFbStage('create');
        } finally {
            setMatchingByName(false);
        }
    };

    // Select an existing supplier found by name (association was made by name only — NIF unconfirmed).
    const handleUseExisting = (s: any) => {
        onSuccess({ id: s.id, name: s.name, portalCode: s.portalCode });
        onClose();
    };

    // User declined the suggested supplier(s) → present the alternatives.
    const handleRejectSuggestion = () => {
        setRejectedSupplierId(nameCandidates[0]?.id ?? null);
        setFbStage('alternatives');
        setShowManualSearch(false);
        setConfirmNoNif(false);
    };

    // Manual search selection (SupplierAutocomplete) — use an existing supplier picked by hand.
    const handleManualSelect = (id: number | null, sName: string, portalCode?: string) => {
        if (!id) return;
        onSuccess({ id, name: sName, portalCode });
        onClose();
    };

    // Create a DRAFT supplier WITHOUT a NIF. When a similar name exists (the user reached this by
    // declining a suggestion) a final confirmation is required first. The internal NIF is never sent.
    const similarNameExists = nameCandidates.length > 0;
    const handleCreateWithoutNif = async () => {
        if (!name.trim()) { setFieldErrors({ Name: ['O nome do fornecedor é obrigatório.'] }); return; }
        if (similarNameExists && !confirmNoNif) { setConfirmNoNif(true); return; } // require explicit confirmation
        setIsSaving(true);
        setFeedback({ type: 'error', message: null });
        try {
            // When a same-name supplier exists, the explicit confirmation above IS the duplicate decision.
            await createViaPaymentOcr(similarNameExists, { internalNif: droppedInternalNif, rejectedId: rejectedSupplierId });
        } finally {
            setIsSaving(false);
        }
    };

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        // The modal is portalled to document.body, but a React portal still propagates events
        // through the REACT tree — so a caller that renders this modal inside its own <form> would
        // otherwise receive this submit and run its own validation. Saving a supplier must never
        // submit the request the user happens to be composing.
        e.stopPropagation();
        setIsSaving(true);
        setFeedback({ type: 'error', message: null }); // transient feedback only — do NOT clear `duplicate`
        setFieldErrors({});

        try {
            // Minimal requirement: Name
            if (!name.trim()) {
                setFieldErrors({ Name: ['O nome do fornecedor é obrigatório.'] });
                setIsSaving(false);
                return;
            }

            if (mode === 'PAYMENT_OCR') {
                // Confirm only when a SOFT duplicate is already shown; the alert stays visible during loading.
                await createViaPaymentOcr(!!duplicate && !duplicate.hard);
                return;
            }

            const newSupplier = await api.lookups.createSupplier({
                name: name.trim(),
                taxId: taxId.trim() || undefined
            });

            // Show ficha toast notification before closing
            setFichaToast(true);

            // Allow the toast to be seen briefly, then proceed
            setTimeout(() => {
                onSuccess({ id: newSupplier.id, name: newSupplier.name, portalCode: newSupplier.portalCode });
                onClose();
            }, 300);
        } catch (err: any) {
            if (err instanceof ApiError) {
                // Handle 409 Conflict — distinguish Name vs NIF duplicate
                if (err.status === 409) {
                    const detail = err.message || '';
                    const isNameDuplicate = detail.includes('nome') || detail.includes('Nome');
                    const isNifDuplicate = detail.includes('NIF') || detail.includes('nif');

                    if (isNameDuplicate) {
                        setFieldErrors({ Name: [detail || 'Já existe um fornecedor com este nome.'] });
                        setFeedback({ type: 'warning', message: detail || 'Já existe um fornecedor com este nome. Utilize o fornecedor existente ou altere o nome.' });
                    } else if (isNifDuplicate) {
                        setFieldErrors({ TaxId: [detail || 'NIF já registado no sistema.'] });
                        setFeedback({ type: 'warning', message: detail || 'Já existe um fornecedor com este NIF. Utilize o fornecedor existente.' });
                    } else {
                        // Generic 409 — show as warning
                        setFeedback({ type: 'warning', message: detail || 'Já existe um fornecedor com estes dados. Utilize o fornecedor existente.' });
                    }
                    setIsSaving(false);
                    return;
                }
                if (err.fieldErrors) {
                    setFieldErrors(err.fieldErrors);
                }
            }
            setFeedback({ type: 'error', message: err.message || 'Erro ao criar fornecedor.' });
        } finally {
            setIsSaving(false);
        }
    };

    const inputStyle: React.CSSProperties = {
        width: '100%',
        padding: '12px 14px',
        backgroundColor: 'var(--color-bg-page)',
        border: '2px solid var(--color-border)',
        borderRadius: 'var(--radius-sm)',
        fontSize: '0.875rem',
        fontWeight: 600,
        color: 'var(--color-text-main)',
        transition: 'all 0.2s ease',
        fontFamily: 'inherit',
        outline: 'none'
    };

    const singleActiveStrongMatch = internalCompany && nameCandidates.length === 1 && nameCandidates[0]?.isActive !== false;
    const modalTitle = !internalCompany ? 'Novo Fornecedor'
        : fbStage === 'suggestion' ? (nameCandidates.length > 1 ? 'Fornecedores encontrados pelo nome' : 'Fornecedor encontrado pelo nome')
        : fbStage === 'alternatives' ? 'Selecionar ou criar fornecedor'
        : 'Criar fornecedor sem NIF';

    const primaryBtnStyle: React.CSSProperties = {
        flex: 1, height: '48px', padding: '0 20px', backgroundColor: 'var(--color-primary)', color: '#fff',
        border: 'none', cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
        boxShadow: 'var(--shadow-md)', fontFamily: 'var(--font-family-display)', fontSize: '0.85rem',
        display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px'
    };
    const secondaryBtnStyle: React.CSSProperties = {
        flex: 1, height: '48px', padding: '0 20px', background: 'none', border: '1px solid var(--color-border)',
        cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)', color: 'var(--color-text-main)',
        fontFamily: 'var(--font-family-display)', fontSize: '0.85rem'
    };

    // A read-only supplier card used in the decision/selection screens.
    const SupplierCard = ({ c, recommended }: { c: any; recommended?: boolean }) => (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)',
            border: `2px solid ${recommended ? 'var(--color-primary)' : 'var(--color-border)'}`,
            borderRadius: 'var(--radius-sm)', padding: '14px'
        }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                <strong style={{ fontSize: '0.9rem', color: 'var(--color-text-main)' }}>{c.name}</strong>
                {recommended && (
                    <span style={{ fontSize: '0.6rem', fontWeight: 900, color: '#fff', backgroundColor: 'var(--color-primary)', padding: '2px 8px', borderRadius: '4px', textTransform: 'uppercase', letterSpacing: '0.03em' }}>Recomendado</span>
                )}
                {c.isActive === false
                    ? <span style={{ fontSize: '0.6rem', fontWeight: 800, color: '#B91C1C', backgroundColor: '#FEE2E2', padding: '2px 6px', borderRadius: '4px' }}>INATIVO</span>
                    : <span style={{ fontSize: '0.6rem', fontWeight: 800, color: '#15803D', backgroundColor: '#DCFCE7', padding: '2px 6px', borderRadius: '4px' }}>ATIVO</span>}
            </div>
            <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', marginTop: '6px', lineHeight: 1.6 }}>
                NIF: <strong>{c.taxId || '—'}</strong><br />
                Portal: {c.portalCode || '—'} · Primavera: {c.primaveraCode || '—'}
            </div>
        </div>
    );

    return (
        <DropdownPortal>
            <AnimatePresence>
                <motion.div
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    exit={{ opacity: 0 }}
                    style={{
                        position: 'fixed',
                        top: 0,
                        left: 0,
                        right: 0,
                        bottom: 0,
                        backgroundColor: 'rgba(0,0,0,0.8)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        zIndex: Z_INDEX.MODAL as any,
                        padding: '20px'
                    }}
                >
                    <motion.div
                        initial={{ scale: 0.9, y: 20 }}
                        animate={{ scale: 1, y: 0 }}
                        style={{
                            backgroundColor: 'var(--color-bg-surface)',
                            padding: '40px',
                            borderRadius: 'var(--radius-md)',
                            maxWidth: '500px',
                            width: '100%',
                            border: '1px solid var(--color-border)',
                            boxShadow: 'var(--shadow-md)',
                            position: 'relative'
                        }}
                    >
                    <button 
                        onClick={onClose}
                        style={{
                            position: 'absolute',
                            top: '20px',
                            right: '20px',
                            background: 'none',
                            border: 'none',
                            cursor: 'pointer',
                            color: 'var(--color-text-muted)'
                        }}
                    >
                        <X size={24} />
                    </button>

                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '24px' }}>
                        <Building2 style={{ width: '32px', height: '32px', color: 'var(--color-primary)' }} />
                        <h2 style={{ fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-text-main)', textTransform: 'uppercase', margin: 0, letterSpacing: '-0.02em' }}>
                            {modalTitle}
                        </h2>
                    </div>

                    <form onSubmit={handleSave} style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                        {feedback.message && (
                            <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback({ ...feedback, message: null })} />
                        )}

                        {duplicate && (
                            <div style={{
                                padding: '12px 14px', borderRadius: 'var(--radius-sm)',
                                backgroundColor: duplicate.hard ? '#FEF2F2' : '#FFFBEB',
                                border: `1px solid ${duplicate.hard ? '#FCA5A5' : '#F59E0B'}`,
                                display: 'flex', flexDirection: 'column', gap: '6px'
                            }}>
                                <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                                    <AlertCircle size={16} style={{ color: duplicate.hard ? '#DC2626' : '#D97706', flexShrink: 0, marginTop: '1px' }} />
                                    <span style={{ fontSize: '0.8rem', fontWeight: 600, color: duplicate.hard ? '#991B1B' : '#92400E', lineHeight: 1.4 }}>
                                        {duplicate.message}
                                    </span>
                                </div>
                                {duplicate.existing && (
                                    <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', paddingLeft: '24px', lineHeight: 1.5 }}>
                                        Cadastrado: <strong>{duplicate.existing.name}</strong>{duplicate.existing.taxId ? `, NIF ${duplicate.existing.taxId}` : ''}{duplicate.existing.isActive === false ? ' (inativo)' : ''}<br />
                                        Informado: <strong>{name || '—'}</strong>{taxId ? `, NIF ${taxId}` : ''}
                                    </div>
                                )}
                            </div>
                        )}

                        {internalCompany && (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                {/* Internal company identified — the extracted NIF was dropped (shown in every stage). */}
                                <div style={{
                                    padding: '12px 14px', borderRadius: 'var(--radius-sm)',
                                    backgroundColor: '#FFFBEB', border: '1px solid #F59E0B',
                                    display: 'flex', alignItems: 'flex-start', gap: '8px'
                                }}>
                                    <AlertCircle size={16} style={{ color: '#D97706', flexShrink: 0, marginTop: '1px' }} />
                                    <span style={{ fontSize: '0.78rem', fontWeight: 600, color: '#92400E', lineHeight: 1.45 }}>
                                        O NIF identificado no documento pertence à empresa interna{internalCompany.name ? ` ${internalCompany.name}` : ''}{internalCompany.taxId ? ` (NIF ${internalCompany.taxId})` : ''} e foi desconsiderado.
                                    </span>
                                </div>

                                {matchingByName && (
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--color-text-muted)', fontSize: '0.8rem', fontWeight: 600, padding: '8px 0' }}>
                                        <RefreshCcw size={16} style={{ animation: 'spin 1s linear infinite' }} /> A procurar fornecedores pelo nome…
                                    </div>
                                )}

                                {/* ── Stage 1: existing supplier(s) found → decision screen ── */}
                                {!matchingByName && fbStage === 'suggestion' && nameCandidates.length > 0 && (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                        <p style={{ fontSize: '0.82rem', color: 'var(--color-text-main)', margin: 0, lineHeight: 1.5 }}>
                                            {nameCandidates.length === 1
                                                ? 'Encontramos um fornecedor cadastrado com o mesmo nome. Recomendamos usar o cadastro existente.'
                                                : 'Encontramos fornecedores cadastrados com o mesmo nome. Selecione o correto.'}
                                        </p>

                                        {nameCandidates.length === 1 ? (
                                            <>
                                                <SupplierCard c={nameCandidates[0]} recommended={!!singleActiveStrongMatch} />
                                                <p style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', margin: 0, fontStyle: 'italic' }}>
                                                    O NIF do fornecedor não foi confirmado pelo OCR — a associação seria feita apenas pelo nome.
                                                </p>
                                                <div style={{ display: 'flex', gap: '12px' }}>
                                                    <button type="button" onClick={() => handleUseExisting(nameCandidates[0])} style={primaryBtnStyle}>
                                                        <CheckCircle2 size={16} /> USAR FORNECEDOR CADASTRADO
                                                    </button>
                                                </div>
                                                <div style={{ display: 'flex', gap: '12px' }}>
                                                    <button type="button" onClick={handleRejectSuggestion} style={secondaryBtnStyle}>NÃO É ESTE FORNECEDOR</button>
                                                    <button type="button" onClick={onClose} style={secondaryBtnStyle}>CANCELAR</button>
                                                </div>
                                            </>
                                        ) : (
                                            <>
                                                {nameCandidates.map((c) => (
                                                    <div key={c.id} style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                                        <SupplierCard c={c} />
                                                        <button type="button" onClick={() => handleUseExisting(c)} style={{ ...primaryBtnStyle, height: '40px', fontSize: '0.75rem' }}>
                                                            <CheckCircle2 size={15} /> USAR ESTE FORNECEDOR
                                                        </button>
                                                    </div>
                                                ))}
                                                <div style={{ display: 'flex', gap: '12px' }}>
                                                    <button type="button" onClick={handleRejectSuggestion} style={secondaryBtnStyle}>NENHUM DESTES</button>
                                                    <button type="button" onClick={onClose} style={secondaryBtnStyle}>CANCELAR</button>
                                                </div>
                                            </>
                                        )}
                                    </div>
                                )}

                                {/* ── Stage 2: user declined the suggestion → alternatives ── */}
                                {!matchingByName && fbStage === 'alternatives' && (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                        <p style={{ fontSize: '0.82rem', color: 'var(--color-text-main)', margin: 0, lineHeight: 1.5 }}>O que deseja fazer?</p>

                                        <button type="button" onClick={() => setShowManualSearch(s => !s)} style={{ ...secondaryBtnStyle, height: '44px', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px' }}>
                                            <Search size={16} /> Buscar outro fornecedor
                                        </button>
                                        {showManualSearch && (
                                            <SupplierAutocomplete onChange={handleManualSelect} placeholder="Pesquisar fornecedor por nome…" />
                                        )}

                                        <div style={{
                                            backgroundColor: '#FFFBEB', border: '1px solid #F59E0B', borderRadius: 'var(--radius-sm)', padding: '10px 12px',
                                            fontSize: '0.72rem', color: '#92400E', fontWeight: 600, lineHeight: 1.45
                                        }}>
                                            Crie um novo fornecedor sem NIF somente se tiver certeza de que o cadastro encontrado não corresponde ao fornecedor deste documento.
                                        </div>
                                        <button type="button" onClick={() => { setConfirmNoNif(false); setFbStage('create'); }} style={{ ...secondaryBtnStyle, height: '44px' }}>
                                            Criar novo fornecedor sem NIF
                                        </button>

                                        <div style={{ display: 'flex', gap: '12px' }}>
                                            <button type="button" onClick={() => setFbStage('suggestion')} style={{ ...secondaryBtnStyle, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px' }}>
                                                <ArrowLeft size={15} /> VOLTAR
                                            </button>
                                            <button type="button" onClick={onClose} style={secondaryBtnStyle}>CANCELAR</button>
                                        </div>
                                    </div>
                                )}

                                {/* ── Stage 3: conscious creation WITHOUT a NIF ── */}
                                {!matchingByName && fbStage === 'create' && (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                        <p style={{ fontSize: '0.8rem', color: 'var(--color-text-main)', margin: 0, lineHeight: 1.5 }}>
                                            {similarNameExists
                                                ? 'Será criado um novo fornecedor, sem NIF, pois recusou o cadastro sugerido.'
                                                : 'Nenhum fornecedor foi encontrado pelo nome. O NIF do fornecedor não foi identificado; será criado sem NIF e deverá ser completado posteriormente.'}
                                        </p>

                                        <div>
                                            <label style={{ display: 'block', marginBottom: '6px', fontWeight: 800, fontSize: '0.72rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                                Nome do Fornecedor <span style={{ color: 'var(--color-status-red)' }}>*</span>
                                            </label>
                                            <input
                                                type="text"
                                                value={name}
                                                onChange={(e) => { setName(e.target.value); setConfirmNoNif(false); if (fieldErrors.Name) setFieldErrors({}); }}
                                                placeholder="Ex: Zeepack Angola, Lda"
                                                style={{ ...inputStyle, borderColor: fieldErrors.Name ? '#F59E0B' : 'var(--color-border)' }}
                                                autoFocus
                                            />
                                            {fieldErrors.Name && (
                                                <p style={{ color: '#92400E', fontSize: '0.72rem', fontWeight: 600, margin: '6px 0 0' }}>{fieldErrors.Name[0]}</p>
                                            )}
                                        </div>

                                        <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                            Sem NIF · Rascunho (DRAFT) · Origem: OCR de pagamento. O NIF poderá ser completado depois na ficha.
                                        </div>

                                        {!similarNameExists && (
                                            <>
                                                <button type="button" onClick={() => setShowManualSearch(s => !s)} style={{ ...secondaryBtnStyle, height: '40px', fontSize: '0.75rem', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px' }}>
                                                    <Search size={15} /> Buscar fornecedor manualmente
                                                </button>
                                                {showManualSearch && (
                                                    <SupplierAutocomplete onChange={handleManualSelect} placeholder="Pesquisar fornecedor por nome…" />
                                                )}
                                            </>
                                        )}

                                        {confirmNoNif && (
                                            <div style={{ backgroundColor: '#FEF2F2', border: '1px solid #FCA5A5', borderRadius: 'var(--radius-sm)', padding: '10px 12px', display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                                                <AlertCircle size={16} style={{ color: '#DC2626', flexShrink: 0, marginTop: '1px' }} />
                                                <span style={{ fontSize: '0.74rem', fontWeight: 700, color: '#991B1B', lineHeight: 1.45 }}>
                                                    Já existe um fornecedor com nome semelhante. Confirme que se trata de uma empresa diferente.
                                                </span>
                                            </div>
                                        )}

                                        <div style={{ display: 'flex', gap: '12px' }}>
                                            <button type="button" onClick={() => { setConfirmNoNif(false); setFbStage(similarNameExists ? 'alternatives' : 'create'); if (!similarNameExists) onClose(); }} style={{ ...secondaryBtnStyle, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '6px' }}>
                                                <ArrowLeft size={15} /> {similarNameExists ? 'VOLTAR' : 'CANCELAR'}
                                            </button>
                                            <button type="button" onClick={handleCreateWithoutNif} disabled={isSaving || !name.trim()} style={{ ...primaryBtnStyle, opacity: (isSaving || !name.trim()) ? 0.7 : 1 }}>
                                                {isSaving ? <RefreshCcw size={16} style={{ animation: 'spin 1s linear infinite' }} /> : <Save size={16} />}
                                                {confirmNoNif ? 'CONFIRMAR CRIAÇÃO SEM NIF' : 'CRIAR FORNECEDOR SEM NIF'}
                                            </button>
                                        </div>
                                    </div>
                                )}
                            </div>
                        )}

                        {!internalCompany && (<>
                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                Nome do Fornecedor <span style={{ color: 'var(--color-status-red)' }}>*</span>
                            </label>
                            <input
                                type="text"
                                value={name}
                                onChange={(e) => {
                                    setName(e.target.value);
                                    setDuplicate(null); // editing name → the previous match is stale; re-check on next submit
                                    // Clear Name duplicate error when user types
                                    if (fieldErrors.Name) {
                                        setFieldErrors(prev => {
                                            const next = { ...prev };
                                            delete next.Name;
                                            return next;
                                        });
                                        setFeedback({ type: 'error', message: null });
                                    }
                                }}
                                placeholder="Ex: Alpla Portugal, Lda"
                                style={{
                                    ...inputStyle,
                                    borderColor: fieldErrors.Name ? '#F59E0B' : 'var(--color-border)'
                                }}
                                autoFocus
                            />
                            {fieldErrors.Name && (
                                <motion.div
                                    initial={{ opacity: 0, height: 0 }}
                                    animate={{ opacity: 1, height: 'auto' }}
                                    style={{
                                        marginTop: '8px',
                                        padding: '10px 12px',
                                        backgroundColor: '#FFFBEB',
                                        border: '1px solid #F59E0B',
                                        borderRadius: 'var(--radius-sm)',
                                        display: 'flex',
                                        alignItems: 'flex-start',
                                        gap: '8px'
                                    }}
                                >
                                    <AlertCircle size={16} style={{ color: '#D97706', flexShrink: 0, marginTop: '1px' }} />
                                    <p style={{ color: '#92400E', fontSize: '0.75rem', fontWeight: 600, margin: 0, lineHeight: '1.4' }}>
                                        {fieldErrors.Name[0]}
                                    </p>
                                </motion.div>
                            )}
                        </div>

                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                NIF / Tax ID (Opcional)
                            </label>
                            <input
                                type="text"
                                value={taxId}
                                onChange={(e) => {
                                    setTaxId(e.target.value);
                                    setDuplicate(null); // editing NIF → the previous match is stale; re-check on next submit
                                    // Clear NIF duplicate error when user types
                                    if (fieldErrors.TaxId) {
                                        setFieldErrors(prev => {
                                            const next = { ...prev };
                                            delete next.TaxId;
                                            return next;
                                        });
                                        setFeedback({ type: 'error', message: null });
                                    }
                                }}
                                placeholder="Ex: 501234567"
                                style={{
                                    ...inputStyle,
                                    borderColor: fieldErrors.TaxId ? '#F59E0B' : 'var(--color-border)'
                                }}
                            />
                            {fieldErrors.TaxId && (
                                <motion.div
                                    initial={{ opacity: 0, height: 0 }}
                                    animate={{ opacity: 1, height: 'auto' }}
                                    style={{
                                        marginTop: '8px',
                                        padding: '10px 12px',
                                        backgroundColor: '#FFFBEB',
                                        border: '1px solid #F59E0B',
                                        borderRadius: 'var(--radius-sm)',
                                        display: 'flex',
                                        alignItems: 'flex-start',
                                        gap: '8px'
                                    }}
                                >
                                    <AlertCircle size={16} style={{ color: '#D97706', flexShrink: 0, marginTop: '1px' }} />
                                    <p style={{ color: '#92400E', fontSize: '0.75rem', fontWeight: 600, margin: 0, lineHeight: '1.4' }}>
                                        {fieldErrors.TaxId[0]}
                                    </p>
                                </motion.div>
                            )}
                        </div>

                        <div style={{ 
                            backgroundColor: 'rgba(52, 152, 219, 0.1)', 
                            border: '2px dashed var(--color-primary)', 
                            borderRadius: 'var(--radius-sm)', 
                            padding: '16px' 
                        }}>
                            <p style={{ fontSize: '0.75rem', color: 'var(--color-text-main)', margin: 0, fontWeight: 600, lineHeight: 1.5 }}>
                                <AlertCircle style={{ width: '14px', height: '14px', display: 'inline', marginRight: '4px', verticalAlign: 'text-bottom' }} />
                                O código do portal será gerado automaticamente. O código Primavera poderá ser preenchido posteriormente.
                            </p>
                        </div>

                        {/* Ficha guidance info box */}
                        <div style={{
                            backgroundColor: '#EFF6FF',
                            border: '1px solid #BFDBFE',
                            borderRadius: 'var(--radius-sm)',
                            padding: '12px 14px',
                            display: 'flex',
                            alignItems: 'flex-start',
                            gap: '10px'
                        }}>
                            <FileText size={16} style={{ color: '#2563EB', flexShrink: 0, marginTop: '1px' }} />
                            <p style={{ fontSize: '0.72rem', color: '#1E40AF', margin: 0, fontWeight: 600, lineHeight: 1.5 }}>
                                O fornecedor será criado como <strong>rascunho</strong>. Complete a ficha de registo em <strong>Contratos → Fichas de Fornecedor</strong>.
                            </p>
                        </div>

                        <div style={{ display: 'flex', gap: '16px', marginTop: '12px' }}>
                            <button
                                type="button"
                                onClick={onClose}
                                style={{
                                    flex: 1, height: '48px', padding: '0 24px', background: 'none', border: '1px solid var(--color-border)',
                                    cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                    fontFamily: 'var(--font-family-display)', fontSize: '0.875rem'
                                }}
                            >
                                CANCELAR
                            </button>
                            <button
                                type="submit"
                                disabled={isSaving}
                                style={{
                                    flex: 1, height: '48px', padding: '0 24px', backgroundColor: 'var(--color-primary)', color: '#fff',
                                    border: 'none', cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)',
                                    boxShadow: 'var(--shadow-md)', fontFamily: 'var(--font-family-display)',
                                    fontSize: '0.875rem', opacity: isSaving ? 0.7 : 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px'
                                }}
                            >
                                {isSaving ? (
                                    <RefreshCcw size={16} style={{ animation: 'spin 1s linear infinite' }} />
                                ) : (
                                    <Save size={16} />
                                )}
                                {duplicate && !duplicate.hard ? 'CRIAR MESMO ASSIM' : 'SALVAR'}
                            </button>
                        </div>
                        </>)}
                    </form>

                    {/* Success toast overlay — shown briefly after creation */}
                    <AnimatePresence>
                        {fichaToast && (
                            <motion.div
                                initial={{ opacity: 0, y: 20 }}
                                animate={{ opacity: 1, y: 0 }}
                                exit={{ opacity: 0, y: -10 }}
                                style={{
                                    position: 'absolute',
                                    bottom: '16px',
                                    left: '16px',
                                    right: '16px',
                                    padding: '14px 16px',
                                    backgroundColor: '#F0FDF4',
                                    border: '1px solid #22C55E',
                                    borderRadius: 'var(--radius-sm)',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '10px',
                                    boxShadow: 'var(--shadow-md)',
                                    zIndex: 10
                                }}
                            >
                                <Info size={18} style={{ color: '#15803D', flexShrink: 0 }} />
                                <span style={{ fontSize: '0.8rem', fontWeight: 600, color: '#15803D', lineHeight: 1.4 }}>
                                    Fornecedor criado como rascunho. Complete a ficha de registo em Contratos → Fichas de Fornecedor.
                                </span>
                            </motion.div>
                        )}
                    </AnimatePresence>
                    </motion.div>
                </motion.div>
            </AnimatePresence>
        </DropdownPortal>
    );
}
