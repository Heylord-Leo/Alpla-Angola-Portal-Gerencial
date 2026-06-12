import { useState } from 'react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';

interface Props {
    equipmentId: string;
    onClose: () => void;
    onSuccess: () => void;
}

export function ReactivateEquipmentModal({ equipmentId, onClose, onSuccess }: Props) {
    const [newStatus, setNewStatus] = useState('AVAILABLE');
    const [reason, setReason] = useState('');
    const [notes, setNotes] = useState('');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');

    const handleSubmit = async () => {
        if (!reason.trim()) {
            setError('O motivo da reativação é obrigatório.');
            return;
        }
        setLoading(true);
        setError('');
        try {
            await itEquipmentApi.reactivate(equipmentId, { newStatus, reason, notes });
            onSuccess();
        } catch (err: any) {
            setError(err.message || 'Falha ao reativar equipamento.');
        } finally {
            setLoading(false);
        }
    };

    const statusOptions = [
        { value: 'AVAILABLE', label: 'Disponível' },
        { value: 'RESERVED', label: 'Reservado' },
        { value: 'IN_REPAIR', label: 'Em Conserto' }
    ];

    return (
        <div style={{
            position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)',
            zIndex: 2000, display: 'flex', alignItems: 'center', justifyContent: 'center'
        }}>
            <div style={{
                background: 'var(--color-bg-surface)', borderRadius: 12, padding: 24,
                width: 440, maxHeight: '90vh', overflowY: 'auto',
                boxShadow: '0 10px 40px rgba(0,0,0,0.3)', border: '1px solid var(--color-border)'
            }}>
                <h3 style={{ margin: '0 0 4px', fontSize: '1.05rem', fontWeight: 700, color: 'var(--color-text)' }}>
                    Reativar Equipamento
                </h3>
                <p style={{ margin: '0 0 16px', fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>
                    Este equipamento foi baixado. Ao reativar, ele voltará ao inventário ativo com o status selecionado.
                </p>

                {error && (
                    <div style={{
                        padding: '8px 12px', marginBottom: 12, borderRadius: 6,
                        background: '#fef2f2', border: '1px solid #fecaca', color: '#dc2626', fontSize: '0.82rem'
                    }}>
                        {error}
                    </div>
                )}

                <label style={{ display: 'block', marginBottom: 12 }}>
                    <span style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text)', display: 'block', marginBottom: 4 }}>
                        Novo Status *
                    </span>
                    <select
                        value={newStatus}
                        onChange={e => setNewStatus(e.target.value)}
                        style={{
                            width: '100%', padding: '8px 10px', border: '1px solid var(--color-border)',
                            borderRadius: 6, fontSize: '0.85rem', background: 'var(--color-bg)',
                            color: 'var(--color-text)'
                        }}
                    >
                        {statusOptions.map(s => (
                            <option key={s.value} value={s.value}>{s.label}</option>
                        ))}
                    </select>
                </label>

                <label style={{ display: 'block', marginBottom: 12 }}>
                    <span style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text)', display: 'block', marginBottom: 4 }}>
                        Motivo da Reativação *
                    </span>
                    <input
                        type="text"
                        value={reason}
                        onChange={e => setReason(e.target.value)}
                        placeholder="Ex: Equipamento foi baixado por engano"
                        style={{
                            width: '100%', padding: '8px 10px', border: '1px solid var(--color-border)',
                            borderRadius: 6, fontSize: '0.85rem', background: 'var(--color-bg)',
                            color: 'var(--color-text)', boxSizing: 'border-box'
                        }}
                    />
                </label>

                <label style={{ display: 'block', marginBottom: 16 }}>
                    <span style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text)', display: 'block', marginBottom: 4 }}>
                        Observações
                    </span>
                    <textarea
                        value={notes}
                        onChange={e => setNotes(e.target.value)}
                        rows={3}
                        placeholder="Observações adicionais (opcional)"
                        style={{
                            width: '100%', padding: '8px 10px', border: '1px solid var(--color-border)',
                            borderRadius: 6, fontSize: '0.85rem', resize: 'vertical',
                            background: 'var(--color-bg)', color: 'var(--color-text)',
                            boxSizing: 'border-box'
                        }}
                    />
                </label>

                <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                    <button
                        onClick={onClose}
                        disabled={loading}
                        style={{
                            padding: '8px 16px', border: '1px solid var(--color-border)',
                            borderRadius: 6, background: 'transparent', cursor: 'pointer',
                            fontSize: '0.85rem', color: 'var(--color-text)'
                        }}
                    >
                        Cancelar
                    </button>
                    <button
                        onClick={handleSubmit}
                        disabled={loading}
                        style={{
                            padding: '8px 16px', border: 'none', borderRadius: 6,
                            background: '#22c55e', color: '#fff', cursor: 'pointer',
                            fontSize: '0.85rem', fontWeight: 600, opacity: loading ? 0.7 : 1
                        }}
                    >
                        {loading ? 'Processando...' : 'Reativar Equipamento'}
                    </button>
                </div>
            </div>
        </div>
    );
}
