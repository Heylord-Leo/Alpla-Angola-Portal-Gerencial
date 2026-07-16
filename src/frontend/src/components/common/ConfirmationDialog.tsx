
import { AlertTriangle } from 'lucide-react';
import { cancelBtnStyle } from '../it/EquipmentFormModal';
import { ModalWrapper } from './ModalWrapper';

interface ConfirmationDialogProps {
    title: string;
    message: React.ReactNode;
    confirmText?: string;
    cancelText?: string;
    onConfirm: () => void;
    onCancel: () => void;
    variant?: 'primary' | 'destructive' | 'warning';
    isOpen?: boolean;
}

export function ConfirmationDialog({
    title,
    message,
    confirmText = 'Confirmar',
    cancelText = 'Cancelar',
    onConfirm,
    onCancel,
    variant = 'primary',
    isOpen = true
}: ConfirmationDialogProps) {
    if (!isOpen) return null;

    const isDestructive = variant === 'destructive';
    const isWarning = variant === 'warning';

    return (
        <ModalWrapper title={title} onClose={onCancel} width={400}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                    {(isDestructive || isWarning) && (
                        <div style={{ color: isDestructive ? '#ef4444' : '#ea580c', flexShrink: 0, marginTop: '2px' }}>
                            <AlertTriangle size={20} />
                        </div>
                    )}
                    <div style={{ margin: 0, fontSize: '0.9rem', color: 'var(--color-text)', lineHeight: 1.5, whiteSpace: 'pre-line', overflowWrap: 'anywhere', wordBreak: 'break-word', maxWidth: '100%' }}>
                        {message}
                    </div>
                </div>
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '8px' }}>
                    <button type="button" onClick={onCancel} style={cancelBtnStyle}>
                        {cancelText}
                    </button>
                    <button
                        type="button"
                        onClick={onConfirm}
                        style={{
                            padding: '8px 16px',
                            backgroundColor: isDestructive ? '#ef4444' : isWarning ? '#ea580c' : '#3b82f6',
                            color: 'white',
                            border: 'none',
                            borderRadius: '8px',
                            fontWeight: 600,
                            cursor: 'pointer',
                            fontSize: '0.85rem'
                        }}
                    >
                        {confirmText}
                    </button>
                </div>
            </div>
        </ModalWrapper>
    );
}
