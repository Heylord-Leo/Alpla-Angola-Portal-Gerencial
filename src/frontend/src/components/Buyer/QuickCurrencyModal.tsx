import { useState, useEffect } from 'react';
import { X, Loader2 } from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../ui/Feedback';

interface QuickCurrencyModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: (currency: any) => void;
    initialCode: string;
}

export function QuickCurrencyModal({ isOpen, onClose, onSuccess, initialCode }: QuickCurrencyModalProps) {
    const [code, setCode] = useState('');
    const [symbol, setSymbol] = useState('');
    const [isSaving, setIsSaving] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    useEffect(() => {
        if (isOpen) {
            const normalized = initialCode.trim().toUpperCase();
            setCode(normalized.length === 3 ? normalized : '');
            setSymbol('');
            setFeedback({ type: 'success', message: null });
        }
    }, [isOpen, initialCode]);

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        const normalizedCode = code.trim().toUpperCase();

        if (!normalizedCode || normalizedCode.length !== 3) {
            setFeedback({ type: 'error', message: 'O código deve ter exatamente 3 caracteres (ex: USD).' });
            return;
        }

        setIsSaving(true);
        setFeedback({ type: 'success', message: null });

        try {
            const newCurrency = await api.lookups.createCurrency({
                code: normalizedCode,
                symbol: symbol.trim()
            });
            setFeedback({ type: 'success', message: 'Moeda criada com sucesso!' });
            setTimeout(() => {
                onSuccess(newCurrency);
                onClose();
            }, 500);
        } catch (err: any) {
            setFeedback({
                type: 'error',
                message: err.message || 'Falha ao criar moeda. Verifique se o código já existe.'
            });
        } finally {
            setIsSaving(false);
        }
    };

    if (!isOpen) return null;

    // ── Shared input style ────────────────────────────────────────────────────
    const inputStyle: React.CSSProperties = {
        width: '100%',
        padding: '9px 14px',
        border: '1px solid var(--color-border)',
        borderRadius: '8px',
        backgroundColor: 'var(--color-bg-page)',
        color: 'var(--color-text)',
        fontSize: '14px',
        fontWeight: 500,
        outline: 'none',
        transition: 'border-color 0.15s',
        boxSizing: 'border-box',
    };

    return (
        /* Backdrop */
        <div style={{
            position: 'fixed', inset: 0, zIndex: 100,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            padding: '16px',
            backgroundColor: 'rgba(0,0,0,0.5)',
            backdropFilter: 'blur(4px)',
        }}>
            {/* Modal card */}
            <div style={{
                backgroundColor: 'var(--color-bg-surface)',
                borderRadius: '12px',
                boxShadow: '0 20px 60px rgba(0,0,0,0.2)',
                width: '100%',
                maxWidth: '420px',
                overflow: 'hidden',
                border: '1px solid var(--color-border)',
            }}>
                {/* Header */}
                <div style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                    padding: '16px 24px',
                    borderBottom: '1px solid var(--color-border)',
                    backgroundColor: 'var(--color-bg-page)',
                }}>
                    <h3 style={{ fontSize: '16px', fontWeight: 700, color: 'var(--color-text)', margin: 0 }}>
                        Nova Moeda
                    </h3>
                    <button
                        onClick={onClose}
                        style={{
                            width: 32, height: 32, borderRadius: '50%',
                            border: 'none', backgroundColor: 'transparent',
                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                            color: 'var(--color-text-muted)', cursor: 'pointer',
                            transition: 'background-color 0.15s',
                        }}
                        onMouseEnter={e => e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'}
                        onMouseLeave={e => e.currentTarget.style.backgroundColor = 'transparent'}
                    >
                        <X size={18} />
                    </button>
                </div>

                {/* Body */}
                <form onSubmit={handleSave} style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '18px' }}>
                    {feedback.message && (
                        <Feedback type={feedback.type} message={feedback.message} />
                    )}

                    {/* Code field */}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        <label style={{ fontSize: '13px', fontWeight: 700, color: 'var(--color-text)' }}>
                            Código (ISO) <span style={{ color: '#ef4444' }}>*</span>
                        </label>
                        <input
                            type="text"
                            value={code}
                            onChange={e => setCode(e.target.value.toUpperCase())}
                            placeholder="Ex: USD"
                            maxLength={3}
                            required
                            style={{ ...inputStyle, textTransform: 'uppercase' }}
                            onFocus={e => e.target.style.borderColor = 'var(--color-primary)'}
                            onBlur={e => e.target.style.borderColor = 'var(--color-border)'}
                        />
                        <p style={{ fontSize: '12px', color: 'var(--color-text-muted)', margin: 0 }}>
                            Deve conter exatamente 3 letras.
                        </p>
                    </div>

                    {/* Symbol field */}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        <label style={{ fontSize: '13px', fontWeight: 700, color: 'var(--color-text)' }}>
                            Símbolo
                        </label>
                        <input
                            type="text"
                            value={symbol}
                            onChange={e => setSymbol(e.target.value)}
                            placeholder="Ex: $"
                            style={inputStyle}
                            onFocus={e => e.target.style.borderColor = 'var(--color-primary)'}
                            onBlur={e => e.target.style.borderColor = 'var(--color-border)'}
                        />
                    </div>

                    {/* Actions */}
                    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', paddingTop: '8px' }}>
                        <button
                            type="button"
                            onClick={onClose}
                            disabled={isSaving}
                            style={{
                                padding: '9px 18px', fontSize: '13px', fontWeight: 600,
                                color: 'var(--color-text-muted)',
                                background: 'none', border: 'none', cursor: 'pointer',
                                transition: 'color 0.15s',
                            }}
                            onMouseEnter={e => e.currentTarget.style.color = 'var(--color-text)'}
                            onMouseLeave={e => e.currentTarget.style.color = 'var(--color-text-muted)'}
                        >
                            Cancelar
                        </button>
                        <button
                            type="submit"
                            disabled={isSaving}
                            style={{
                                padding: '9px 22px', fontSize: '13px', fontWeight: 700,
                                backgroundColor: isSaving ? '#93c5fd' : 'var(--color-primary)',
                                color: '#fff',
                                border: 'none', borderRadius: '8px',
                                cursor: isSaving ? 'not-allowed' : 'pointer',
                                display: 'flex', alignItems: 'center', gap: '8px',
                                transition: 'background-color 0.2s',
                                boxShadow: '0 1px 3px rgba(0,0,0,0.1)',
                            }}
                            onMouseEnter={e => { if (!isSaving) e.currentTarget.style.filter = 'brightness(0.9)'; }}
                            onMouseLeave={e => { e.currentTarget.style.filter = 'none'; }}
                        >
                            {isSaving ? (
                                <>
                                    <Loader2 style={{ width: 14, height: 14, animation: 'spin 1s linear infinite' }} />
                                    Gravando...
                                </>
                            ) : 'Criar Moeda'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
