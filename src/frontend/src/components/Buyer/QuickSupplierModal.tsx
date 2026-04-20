import React, { useState, useEffect } from 'react';
import { X, Save, Building2, AlertCircle, RefreshCcw } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { Feedback, FeedbackType } from '../ui/Feedback';
import { Z_INDEX } from '../../constants/ui';
import { DropdownPortal } from '../ui/DropdownPortal';
import { motion, AnimatePresence } from 'framer-motion';

interface QuickSupplierModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: (supplier: { id: number; name: string; portalCode?: string }) => void;
    initialName?: string;
    initialTaxId?: string;
}

export function QuickSupplierModal({ isOpen, onClose, onSuccess, initialName = '', initialTaxId = '' }: QuickSupplierModalProps) {
    const [name, setName] = useState(initialName);
    const [taxId, setTaxId] = useState(initialTaxId);
    const [isSaving, setIsSaving] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
    
    // Sync name and taxId with initial props when modal opens or props change
    useEffect(() => {
        if (isOpen) {
            setName(initialName);
            setTaxId(initialTaxId);
            setFeedback({ type: 'error', message: null });
            setFieldErrors({});
        }
    }, [isOpen, initialName, initialTaxId]);

    if (!isOpen) return null;

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        setIsSaving(true);
        setFeedback({ type: 'error', message: null });
        setFieldErrors({});

        try {
            // Minimal requirement: Name
            if (!name.trim()) {
                setFieldErrors({ Name: ['O nome do fornecedor é obrigatório.'] });
                setIsSaving(false);
                return;
            }

            const newSupplier = await api.lookups.createSupplier({
                name: name.trim(),
                taxId: taxId.trim() || undefined
            });

            onSuccess({ id: newSupplier.id, name: newSupplier.name, portalCode: newSupplier.portalCode });
            onClose();
        } catch (err: any) {
            if (err instanceof ApiError && err.fieldErrors) {
                setFieldErrors(err.fieldErrors);
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
                            Novo Fornecedor
                        </h2>
                    </div>

                    <form onSubmit={handleSave} style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                        {feedback.message && (
                            <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback({ ...feedback, message: null })} />
                        )}

                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                Nome do Fornecedor <span style={{ color: 'var(--color-status-red)' }}>*</span>
                            </label>
                            <input
                                type="text"
                                value={name}
                                onChange={(e) => setName(e.target.value)}
                                placeholder="Ex: Alpla Portugal, Lda"
                                style={{
                                    ...inputStyle,
                                    borderColor: fieldErrors.Name ? 'var(--color-status-red)' : 'var(--color-border)'
                                }}
                                autoFocus
                            />
                            {fieldErrors.Name && (
                                <p style={{ color: 'var(--color-status-red)', fontSize: '0.75rem', marginTop: '4px', fontWeight: 600 }}>
                                    {fieldErrors.Name[0]}
                                </p>
                            )}
                        </div>

                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 800, fontSize: '0.75rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                NIF / Tax ID (Opcional)
                            </label>
                            <input
                                type="text"
                                value={taxId}
                                onChange={(e) => setTaxId(e.target.value)}
                                placeholder="Ex: 501234567"
                                style={inputStyle}
                            />
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
                                SALVAR
                            </button>
                        </div>
                    </form>
                    </motion.div>
                </motion.div>
            </AnimatePresence>
        </DropdownPortal>
    );
}
