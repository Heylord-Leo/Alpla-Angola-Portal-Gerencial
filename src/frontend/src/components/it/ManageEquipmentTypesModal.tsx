import { useState, useEffect } from 'react';
import { X, Plus, Edit3, ToggleLeft, ToggleRight, Loader2, GripVertical, Check, AlertCircle } from 'lucide-react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';

interface EquipmentType {
    id: string;
    code: string;
    displayName: string;
    isActive: boolean;
    sortOrder: number;
}

interface Props {
    onClose: () => void;
}

export function ManageEquipmentTypesModal({ onClose }: Props) {
    const [types, setTypes] = useState<EquipmentType[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [success, setSuccess] = useState('');

    // Create form
    const [showCreate, setShowCreate] = useState(false);
    const [newCode, setNewCode] = useState('');
    const [newDisplayName, setNewDisplayName] = useState('');
    const [creating, setCreating] = useState(false);

    // Inline edit
    const [editingId, setEditingId] = useState<string | null>(null);
    const [editDisplayName, setEditDisplayName] = useState('');
    const [editSortOrder, setEditSortOrder] = useState('');
    const [saving, setSaving] = useState(false);

    const loadTypes = async () => {
        try {
            setLoading(true);
            setError('');
            const data = await itEquipmentApi.types.list(false);
            setTypes(data.sort((a, b) => a.sortOrder - b.sortOrder));
        } catch (err: any) {
            setError(err.message || 'Falha ao carregar tipos.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { loadTypes(); }, []);

    const showMessage = (msg: string) => {
        setSuccess(msg);
        setTimeout(() => setSuccess(''), 3000);
    };

    const handleCreate = async () => {
        if (!newCode.trim() || !newDisplayName.trim()) {
            setError('Código e nome são obrigatórios.');
            return;
        }

        // Validate code format (uppercase, underscores, no spaces)
        const codeUpper = newCode.trim().toUpperCase().replace(/\s+/g, '_').replace(/[^A-Z0-9_]/g, '');
        if (codeUpper.length < 2) {
            setError('Código deve ter pelo menos 2 caracteres.');
            return;
        }

        // Check for duplicates locally
        if (types.some(t => t.code === codeUpper)) {
            setError(`Já existe um tipo com o código "${codeUpper}".`);
            return;
        }

        try {
            setCreating(true);
            setError('');
            await itEquipmentApi.types.create({
                code: codeUpper,
                displayName: newDisplayName.trim(),
                sortOrder: types.length + 1
            });
            setNewCode('');
            setNewDisplayName('');
            setShowCreate(false);
            showMessage(`Tipo "${newDisplayName.trim()}" criado com sucesso.`);
            await loadTypes();
        } catch (err: any) {
            setError(err.message || 'Falha ao criar tipo.');
        } finally {
            setCreating(false);
        }
    };

    const handleToggle = async (type: EquipmentType) => {
        try {
            setError('');
            await itEquipmentApi.types.toggle(type.id);
            showMessage(`Tipo "${type.displayName}" ${type.isActive ? 'desativado' : 'ativado'}.`);
            await loadTypes();
        } catch (err: any) {
            setError(err.message || 'Falha ao alternar tipo.');
        }
    };

    const startEdit = (type: EquipmentType) => {
        setEditingId(type.id);
        setEditDisplayName(type.displayName);
        setEditSortOrder(type.sortOrder.toString());
    };

    const cancelEdit = () => {
        setEditingId(null);
        setEditDisplayName('');
        setEditSortOrder('');
    };

    const handleSaveEdit = async (id: string) => {
        if (!editDisplayName.trim()) {
            setError('Nome de exibição é obrigatório.');
            return;
        }
        try {
            setSaving(true);
            setError('');
            await itEquipmentApi.types.update(id, {
                displayName: editDisplayName.trim(),
                sortOrder: parseInt(editSortOrder) || 0
            });
            setEditingId(null);
            showMessage('Tipo atualizado com sucesso.');
            await loadTypes();
        } catch (err: any) {
            setError(err.message || 'Falha ao atualizar tipo.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <div style={{
            position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)',
            zIndex: 2000, display: 'flex', alignItems: 'center', justifyContent: 'center'
        }}>
            <div style={{
                background: 'var(--color-bg-surface)', borderRadius: 14, width: 620,
                maxHeight: '85vh', display: 'flex', flexDirection: 'column',
                boxShadow: '0 12px 50px rgba(0,0,0,0.3)', border: '1px solid var(--color-border)',
                animation: 'fadeIn 0.2s ease-out'
            }}>
                <style>{`@keyframes fadeIn { from { opacity: 0; transform: scale(0.96); } to { opacity: 1; transform: scale(1); } }`}</style>

                {/* Header */}
                <div style={{
                    padding: '16px 20px', borderBottom: '1px solid var(--color-border)',
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center'
                }}>
                    <div>
                        <h3 style={{ margin: 0, fontSize: '1.05rem', fontWeight: 700, color: 'var(--color-text)' }}>
                            Gerir Tipos de Equipamento
                        </h3>
                        <p style={{ margin: '2px 0 0', fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                            {types.length} tipos registados · {types.filter(t => t.isActive).length} ativos
                        </p>
                    </div>
                    <button onClick={onClose} style={{
                        background: 'none', border: 'none', cursor: 'pointer',
                        color: 'var(--color-text-muted)', padding: 6, borderRadius: 6
                    }}>
                        <X size={20} />
                    </button>
                </div>

                {/* Messages */}
                {error && (
                    <div style={{
                        margin: '12px 20px 0', padding: '8px 12px', borderRadius: 6,
                        background: '#fef2f2', border: '1px solid #fecaca', color: '#dc2626',
                        fontSize: '0.82rem', display: 'flex', alignItems: 'center', gap: 6
                    }}>
                        <AlertCircle size={14} /> {error}
                    </div>
                )}
                {success && (
                    <div style={{
                        margin: '12px 20px 0', padding: '8px 12px', borderRadius: 6,
                        background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#059669',
                        fontSize: '0.82rem', display: 'flex', alignItems: 'center', gap: 6
                    }}>
                        <Check size={14} /> {success}
                    </div>
                )}

                {/* Actions bar */}
                <div style={{
                    padding: '10px 20px', display: 'flex', justifyContent: 'flex-end'
                }}>
                    <button
                        onClick={() => { setShowCreate(!showCreate); setError(''); }}
                        style={{
                            display: 'flex', alignItems: 'center', gap: 4, padding: '6px 12px',
                            border: '1px solid rgba(59,130,246,0.3)', borderRadius: 6,
                            background: showCreate ? 'rgba(59,130,246,0.1)' : 'transparent',
                            color: '#3b82f6', cursor: 'pointer', fontSize: '0.82rem', fontWeight: 600
                        }}
                    >
                        <Plus size={14} /> Novo Tipo
                    </button>
                </div>

                {/* Create form */}
                {showCreate && (
                    <div style={{
                        margin: '0 20px 12px', padding: 14, borderRadius: 10,
                        background: 'rgba(59,130,246,0.04)', border: '1px solid rgba(59,130,246,0.15)'
                    }}>
                        <div style={{ display: 'flex', gap: 8, marginBottom: 8 }}>
                            <div style={{ flex: 1 }}>
                                <label style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)', display: 'block', marginBottom: 3 }}>
                                    Código *
                                </label>
                                <input
                                    type="text"
                                    value={newCode}
                                    onChange={e => setNewCode(e.target.value.toUpperCase().replace(/\s+/g, '_'))}
                                    placeholder="Ex: WEBCAM"
                                    style={{
                                        width: '100%', padding: '6px 8px', border: '1px solid var(--color-border)',
                                        borderRadius: 6, fontSize: '0.85rem', background: 'var(--color-bg)',
                                        color: 'var(--color-text)', fontFamily: 'monospace', boxSizing: 'border-box'
                                    }}
                                />
                            </div>
                            <div style={{ flex: 2 }}>
                                <label style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)', display: 'block', marginBottom: 3 }}>
                                    Nome de Exibição *
                                </label>
                                <input
                                    type="text"
                                    value={newDisplayName}
                                    onChange={e => setNewDisplayName(e.target.value)}
                                    placeholder="Ex: Webcam"
                                    style={{
                                        width: '100%', padding: '6px 8px', border: '1px solid var(--color-border)',
                                        borderRadius: 6, fontSize: '0.85rem', background: 'var(--color-bg)',
                                        color: 'var(--color-text)', boxSizing: 'border-box'
                                    }}
                                />
                            </div>
                        </div>
                        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                            <button
                                onClick={() => { setShowCreate(false); setNewCode(''); setNewDisplayName(''); }}
                                style={{
                                    padding: '5px 12px', border: '1px solid var(--color-border)',
                                    borderRadius: 6, background: 'transparent', cursor: 'pointer',
                                    fontSize: '0.8rem', color: 'var(--color-text)'
                                }}
                            >
                                Cancelar
                            </button>
                            <button
                                onClick={handleCreate}
                                disabled={creating}
                                style={{
                                    padding: '5px 12px', border: 'none', borderRadius: 6,
                                    background: '#3b82f6', color: '#fff', cursor: 'pointer',
                                    fontSize: '0.8rem', fontWeight: 600, opacity: creating ? 0.6 : 1
                                }}
                            >
                                {creating ? 'Criando...' : 'Criar Tipo'}
                            </button>
                        </div>
                    </div>
                )}

                {/* Types list */}
                <div style={{ flex: 1, overflowY: 'auto', padding: '0 20px 16px' }}>
                    {loading ? (
                        <div style={{ display: 'flex', justifyContent: 'center', padding: 40 }}>
                            <Loader2 size={24} style={{ animation: 'spin 1s linear infinite', color: 'var(--color-primary)' }} />
                            <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
                        </div>
                    ) : types.length === 0 ? (
                        <p style={{ textAlign: 'center', color: 'var(--color-text-muted)', padding: 30 }}>
                            Nenhum tipo registado.
                        </p>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                            {/* Table header */}
                            <div style={{
                                display: 'grid', gridTemplateColumns: '40px 120px 1fr 70px 100px',
                                gap: 8, padding: '8px 10px', fontSize: '0.72rem', fontWeight: 600,
                                color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em',
                                borderBottom: '1px solid var(--color-border)'
                            }}>
                                <span>#</span>
                                <span>Código</span>
                                <span>Nome</span>
                                <span>Ordem</span>
                                <span style={{ textAlign: 'right' }}>Ações</span>
                            </div>

                            {types.map((type) => (
                                <div
                                    key={type.id}
                                    style={{
                                        display: 'grid', gridTemplateColumns: '40px 120px 1fr 70px 100px',
                                        gap: 8, padding: '8px 10px', alignItems: 'center',
                                        borderBottom: '1px solid var(--color-border)',
                                        opacity: type.isActive ? 1 : 0.5,
                                        background: editingId === type.id ? 'rgba(59,130,246,0.04)' : 'transparent',
                                        transition: 'all 0.15s'
                                    }}
                                >
                                    {/* Drag handle placeholder */}
                                    <GripVertical size={14} style={{ color: 'var(--color-text-muted)', opacity: 0.4 }} />

                                    {/* Code */}
                                    <span style={{
                                        fontFamily: 'monospace', fontSize: '0.78rem', fontWeight: 600,
                                        color: 'var(--color-text)', overflow: 'hidden', textOverflow: 'ellipsis'
                                    }}>
                                        {type.code}
                                    </span>

                                    {/* Display Name */}
                                    {editingId === type.id ? (
                                        <input
                                            type="text"
                                            value={editDisplayName}
                                            onChange={e => setEditDisplayName(e.target.value)}
                                            style={{
                                                padding: '4px 6px', border: '1px solid #3b82f6',
                                                borderRadius: 4, fontSize: '0.82rem', background: 'var(--color-bg)',
                                                color: 'var(--color-text)', outline: 'none'
                                            }}
                                            autoFocus
                                        />
                                    ) : (
                                        <span style={{ fontSize: '0.85rem', color: 'var(--color-text)' }}>
                                            {type.displayName}
                                        </span>
                                    )}

                                    {/* Sort Order */}
                                    {editingId === type.id ? (
                                        <input
                                            type="number"
                                            value={editSortOrder}
                                            onChange={e => setEditSortOrder(e.target.value)}
                                            style={{
                                                padding: '4px 6px', border: '1px solid #3b82f6',
                                                borderRadius: 4, fontSize: '0.82rem', background: 'var(--color-bg)',
                                                color: 'var(--color-text)', width: 50, outline: 'none'
                                            }}
                                        />
                                    ) : (
                                        <span style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>
                                            {type.sortOrder}
                                        </span>
                                    )}

                                    {/* Actions */}
                                    <div style={{ display: 'flex', gap: 4, justifyContent: 'flex-end' }}>
                                        {editingId === type.id ? (
                                            <>
                                                <button
                                                    onClick={() => handleSaveEdit(type.id)}
                                                    disabled={saving}
                                                    title="Salvar"
                                                    style={{
                                                        padding: '3px 6px', border: 'none', borderRadius: 4,
                                                        background: '#22c55e', color: '#fff', cursor: 'pointer',
                                                        fontSize: '0.72rem', fontWeight: 600
                                                    }}
                                                >
                                                    {saving ? '...' : '✓'}
                                                </button>
                                                <button
                                                    onClick={cancelEdit}
                                                    title="Cancelar"
                                                    style={{
                                                        padding: '3px 6px', border: '1px solid var(--color-border)',
                                                        borderRadius: 4, background: 'transparent', cursor: 'pointer',
                                                        fontSize: '0.72rem', color: 'var(--color-text)'
                                                    }}
                                                >
                                                    ✕
                                                </button>
                                            </>
                                        ) : (
                                            <>
                                                <button
                                                    onClick={() => startEdit(type)}
                                                    title="Editar"
                                                    style={{
                                                        padding: '3px 6px', border: '1px solid var(--color-border)',
                                                        borderRadius: 4, background: 'transparent', cursor: 'pointer',
                                                        color: 'var(--color-text-muted)'
                                                    }}
                                                >
                                                    <Edit3 size={12} />
                                                </button>
                                                <button
                                                    onClick={() => handleToggle(type)}
                                                    title={type.isActive ? 'Desativar' : 'Ativar'}
                                                    style={{
                                                        padding: '3px 6px', border: 'none', borderRadius: 4,
                                                        background: type.isActive ? 'rgba(239,68,68,0.1)' : 'rgba(34,197,94,0.1)',
                                                        cursor: 'pointer',
                                                        color: type.isActive ? '#ef4444' : '#22c55e'
                                                    }}
                                                >
                                                    {type.isActive ? <ToggleRight size={12} /> : <ToggleLeft size={12} />}
                                                </button>
                                            </>
                                        )}
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>

                {/* Footer */}
                <div style={{
                    padding: '12px 20px', borderTop: '1px solid var(--color-border)',
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center'
                }}>
                    <p style={{ margin: 0, fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                        Tipos inativos não aparecem nos formulários, mas equipamentos existentes continuam visíveis.
                    </p>
                    <button
                        onClick={onClose}
                        style={{
                            padding: '7px 16px', border: '1px solid var(--color-border)',
                            borderRadius: 6, background: 'transparent', cursor: 'pointer',
                            fontSize: '0.85rem', color: 'var(--color-text)'
                        }}
                    >
                        Fechar
                    </button>
                </div>
            </div>
        </div>
    );
}
