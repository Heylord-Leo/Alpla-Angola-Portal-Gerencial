import React from 'react';
import { AlertTriangle, X } from 'lucide-react';

interface ConfirmDiscardModalProps {
    isOpen: boolean;
    title?: string;
    message?: string;
    confirmLabel?: string;
    cancelLabel?: string;
    onConfirm: () => void;
    onCancel: () => void;
}

/**
 * System-styled confirmation modal for discard/close actions.
 * Replaces native window.confirm() calls.
 */
export const ConfirmDiscardModal: React.FC<ConfirmDiscardModalProps> = ({
    isOpen,
    title = 'Descartar alterações?',
    message = 'Existem alterações não salvas neste processo de aprovação. Se fechar agora, as alterações feitas no wizard serão perdidas.',
    confirmLabel = 'Descartar e fechar',
    cancelLabel = 'Continuar editando',
    onConfirm,
    onCancel
}) => {
    if (!isOpen) return null;

    return (
        <div
            style={{
                position: 'fixed',
                top: 0, left: 0, right: 0, bottom: 0,
                backgroundColor: 'rgba(17, 24, 39, 0.7)',
                backdropFilter: 'blur(4px)',
                zIndex: 10000,
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'center',
                padding: '24px'
            }}
            /* Clicking outside does NOT close — prevents accidental discard */
        >
            <div
                onClick={(e) => e.stopPropagation()}
                style={{
                    backgroundColor: '#FFFFFF',
                    borderRadius: '12px',
                    width: '100%',
                    maxWidth: '440px',
                    boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
                    overflow: 'hidden',
                    animation: 'fadeInScale 0.15s ease-out'
                }}
            >
                {/* Header */}
                <div style={{
                    padding: '20px 24px 16px',
                    display: 'flex',
                    alignItems: 'flex-start',
                    gap: '14px'
                }}>
                    <div style={{
                        width: '40px', height: '40px', borderRadius: '50%',
                        backgroundColor: '#FEF3C7',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        flexShrink: 0
                    }}>
                        <AlertTriangle size={20} color="#D97706" />
                    </div>
                    <div style={{ flex: 1 }}>
                        <h3 style={{
                            fontSize: '1rem', fontWeight: 700,
                            color: '#111827', margin: 0, lineHeight: 1.4
                        }}>
                            {title}
                        </h3>
                        <p style={{
                            fontSize: '0.875rem', color: '#6B7280',
                            margin: '6px 0 0', lineHeight: 1.5
                        }}>
                            {message}
                        </p>
                    </div>
                    <button
                        onClick={onCancel}
                        style={{
                            background: 'none', border: 'none',
                            cursor: 'pointer', padding: '4px',
                            color: '#9CA3AF', borderRadius: '4px',
                            display: 'flex', alignItems: 'center',
                            flexShrink: 0
                        }}
                    >
                        <X size={18} />
                    </button>
                </div>

                {/* Footer */}
                <div style={{
                    padding: '12px 24px 20px',
                    display: 'flex',
                    justifyContent: 'flex-end',
                    gap: '10px'
                }}>
                    <button
                        onClick={onCancel}
                        style={{
                            padding: '9px 18px', borderRadius: '8px',
                            backgroundColor: '#FFFFFF', border: '1px solid #D1D5DB',
                            color: '#374151', fontWeight: 600, fontSize: '0.8125rem',
                            cursor: 'pointer', transition: 'all 0.15s'
                        }}
                    >
                        {cancelLabel}
                    </button>
                    <button
                        onClick={onConfirm}
                        style={{
                            padding: '9px 18px', borderRadius: '8px',
                            backgroundColor: '#DC2626', border: 'none',
                            color: '#FFFFFF', fontWeight: 600, fontSize: '0.8125rem',
                            cursor: 'pointer', transition: 'all 0.15s',
                            boxShadow: '0 1px 3px rgba(220, 38, 38, 0.25)'
                        }}
                    >
                        {confirmLabel}
                    </button>
                </div>
            </div>
        </div>
    );
};
