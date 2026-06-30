
import { AlertTriangle } from 'lucide-react';
import { ModalWrapper, cancelBtnStyle } from '../it/EquipmentFormModal';

interface ConfirmationDialogProps {
    title: string;
    message: string;
    confirmText?: string;
    cancelText?: string;
    onConfirm: () => void;
    onCancel: () => void;
    variant?: 'primary' | 'destructive';
}

export function ConfirmationDialog({
    title,
    message,
    confirmText = 'Confirmar',
    cancelText = 'Cancelar',
    onConfirm,
    onCancel,
    variant = 'primary'
}: ConfirmationDialogProps) {
    const isDestructive = variant === 'destructive';

    return (
        <ModalWrapper title={title} onClose={onCancel} width={400}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                    {isDestructive && (
                        <div style={{ color: '#ef4444', flexShrink: 0, marginTop: '2px' }}>
                            <AlertTriangle size={20} />
                        </div>
                    )}
                    <p style={{ margin: 0, fontSize: '0.9rem', color: 'var(--color-text)', lineHeight: 1.5 }}>
                        {message}
                    </p>
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
                            backgroundColor: isDestructive ? '#ef4444' : '#3b82f6',
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
