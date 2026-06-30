import { useState, useEffect } from 'react';
import { X, Save, AlertCircle } from 'lucide-react';
import { FormInput } from '../common/form/FormInput';
import { ConfirmationDialog } from '../common/ConfirmationDialog';
import { useUnsavedChangesWarning } from '../../hooks/useUnsavedChangesWarning';

export type SimpleCatalogType = 'manufacturers' | 'processors' | 'memory';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    catalogType: SimpleCatalogType;
    editingItem?: any;
    onSave: (data: any) => Promise<void>;
}

const TYPE_CONFIG = {
    manufacturers: { title: 'Fabricante', nameLabel: 'Nome do Fabricante' },
    processors: { title: 'Processador', nameLabel: 'Nome do Processador' },
    memory: { title: 'Memória RAM', nameLabel: 'Descrição da Memória' }
};

export function CatalogDrawer({ isOpen, onClose, catalogType, editingItem, onSave }: Props) {
    const [name, setName] = useState('');
    const [valueInGb, setValueInGb] = useState<number | ''>('');
    const [isActive, setIsActive] = useState(true);
    
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [isDirty, setIsDirty] = useState(false);
    const [showCloseConfirm, setShowCloseConfirm] = useState(false);

    const isEdit = !!editingItem;
    const config = TYPE_CONFIG[catalogType];

    useEffect(() => {
        if (isOpen) {
            if (editingItem) {
                setName(catalogType === 'memory' ? editingItem.displayName : editingItem.name);
                setValueInGb(editingItem.valueInGb ?? '');
                setIsActive(editingItem.isActive ?? true);
            } else {
                setName('');
                setValueInGb('');
                setIsActive(true);
            }
            setIsDirty(false);
            setError('');
        }
    }, [isOpen, editingItem, catalogType]);

    // Setup dirty protection hook
    useUnsavedChangesWarning({ isDirty, isSubmitted: false });

    if (!isOpen) return null;

    const handleCloseClick = () => {
        if (isDirty) {
            setShowCloseConfirm(true);
        } else {
            onClose();
        }
    };

    const handleSave = async () => {
        if (!name.trim()) {
            setError(`${config.nameLabel} é obrigatório.`);
            return;
        }

        const data: any = {};
        if (catalogType === 'memory') {
            data.displayName = name.trim();
            if (valueInGb !== '') data.valueInGb = Number(valueInGb);
        } else {
            data.name = name.trim();
        }
        
        if (isEdit) {
            data.isActive = isActive;
        }

        try {
            setSaving(true);
            setError('');
            await onSave(data);
            setIsDirty(false);
            // Parent is responsible for closing, but we can close it too. Actually we let parent close on success or we close it.
            // But if we close it here, we don't need parent to do it. Let's call onClose.
            onClose();
        } catch (err: any) {
            setError(err.message || 'Ocorreu um erro ao guardar.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <>
            {/* Backdrop */}
            <div
                onClick={handleCloseClick}
                style={{
                    position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.4)',
                    zIndex: 'var(--z-drawer)' as any, transition: 'opacity 0.3s'
                }}
            />
            
            {/* Drawer */}
            <div style={{
                position: 'fixed', top: 0, right: 0, bottom: 0, width: 400,
                backgroundColor: 'var(--color-bg-surface)', borderLeft: '1px solid var(--color-border)',
                zIndex: 'calc(var(--z-drawer) + 1)' as any, display: 'flex', flexDirection: 'column',
                boxShadow: '-8px 0 30px rgba(0,0,0,0.15)',
                animation: 'slideIn 0.25s ease-out'
            }}>
                <style>{`@keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }`}</style>

                {/* Header */}
                <div style={{
                    padding: '20px 24px', borderBottom: '1px solid var(--color-border)',
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center'
                }}>
                    <div>
                        <h2 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 600, color: 'var(--color-text)' }}>
                            {isEdit ? `Editar ${config.title}` : `Novo ${config.title}`}
                        </h2>
                        <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)', marginTop: 4 }}>
                            {isEdit ? 'Atualize as informações do registo.' : 'Crie um novo registo no catálogo.'}
                        </div>
                    </div>
                    <button onClick={handleCloseClick} style={{
                        background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)',
                        padding: 6, borderRadius: 6
                    }}>
                        <X size={20} />
                    </button>
                </div>

                {/* Content */}
                <div style={{ flex: 1, overflowY: 'auto', padding: '24px' }}>
                    {error && (
                        <div style={{
                            display: 'flex', alignItems: 'flex-start', gap: 8, padding: '12px 16px',
                            backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8,
                            color: '#991b1b', fontSize: '0.85rem', marginBottom: 20
                        }}>
                            <AlertCircle size={16} style={{ marginTop: 2, flexShrink: 0 }} />
                            <span>{error}</span>
                        </div>
                    )}

                    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
                        <FormInput
                            label={`${config.nameLabel} *`}
                            value={name}
                            onChange={(v) => { setName(v); setIsDirty(true); }}
                            placeholder={`Ex: ${catalogType === 'memory' ? '16GB DDR4' : 'Dell'}`}
                        />

                        {catalogType === 'memory' && (
                            <FormInput
                                label="Capacidade em GB (Opcional)"
                                type="number"
                                value={valueInGb === '' ? '' : valueInGb.toString()}
                                onChange={(v) => { setValueInGb(v ? Number(v) : ''); setIsDirty(true); }}
                                placeholder="Ex: 16"
                            />
                        )}

                        {isEdit && (
                            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 8 }}>
                                <input 
                                    type="checkbox" 
                                    id="drawer-active"
                                    checked={isActive}
                                    onChange={(e) => { setIsActive(e.target.checked); setIsDirty(true); }}
                                    style={{ width: 16, height: 16, cursor: 'pointer' }}
                                />
                                <label htmlFor="drawer-active" style={{ fontSize: '0.9rem', cursor: 'pointer', fontWeight: 500 }}>
                                    Registo Ativo
                                </label>
                            </div>
                        )}
                    </div>
                </div>

                {/* Footer */}
                <div style={{
                    padding: '16px 24px', borderTop: '1px solid var(--color-border)',
                    display: 'flex', justifyContent: 'flex-end', gap: 12,
                    backgroundColor: 'var(--color-bg-subtle)'
                }}>
                    <button 
                        onClick={handleCloseClick}
                        style={{
                            padding: '8px 16px', border: '1px solid var(--color-border)', borderRadius: 6,
                            background: 'white', color: 'var(--color-text)', fontWeight: 500,
                            cursor: 'pointer', fontSize: '0.9rem'
                        }}
                    >
                        Cancelar
                    </button>
                    <button 
                        onClick={handleSave}
                        disabled={saving}
                        style={{
                            padding: '8px 16px', border: 'none', borderRadius: 6,
                            background: 'var(--color-primary)', color: 'white', fontWeight: 500,
                            cursor: saving ? 'not-allowed' : 'pointer', fontSize: '0.9rem',
                            display: 'flex', alignItems: 'center', gap: 6,
                            opacity: saving ? 0.7 : 1
                        }}
                    >
                        <Save size={16} />
                        {saving ? 'A guardar...' : 'Guardar'}
                    </button>
                </div>
            </div>

            {/* Confirm Close Dialog */}
            {showCloseConfirm && (
                <ConfirmationDialog
                    title="Descartar alterações?"
                    message="Existem alterações não guardadas. Tem a certeza que deseja fechar?"
                    confirmText="Sim, fechar e descartar"
                    cancelText="Cancelar"
                    variant="destructive"
                    onConfirm={() => {
                        setShowCloseConfirm(false);
                        setIsDirty(false);
                        onClose();
                    }}
                    onCancel={() => setShowCloseConfirm(false)}
                />
            )}
        </>
    );
}
