import React, { useState } from 'react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { ModalWrapper, SubmitBtn, ErrorBox, Field, TextArea, cancelBtnStyle } from './EquipmentFormModal';

interface Props { equipmentId: string; onClose: () => void; onSuccess: () => void; }

export function RetireEquipmentModal({ equipmentId, onClose, onSuccess }: Props) {
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [reason, setReason] = useState('');
    const [notes, setNotes] = useState('');

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            setSaving(true); setError('');
            await itEquipmentApi.retire(equipmentId, { reason, notes });
            onSuccess();
        } catch (err: any) { setError(err.message); } finally { setSaving(false); }
    };

    return (
        <ModalWrapper title="Baixar Equipamento" onClose={onClose}>
            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {error && <ErrorBox msg={error} />}
                <div style={{ padding: '8px 12px', backgroundColor: '#f9fafb', borderRadius: 8, fontSize: '0.82rem', color: '#6b7280', border: '1px solid var(--color-border)' }}>
                    📦 O equipamento será marcado como inativo e removido do inventário ativo.
                </div>
                <Field label="Motivo da Baixa" value={reason} onChange={setReason} placeholder="Ex: Fim de vida útil, danificado irreparável" />
                <TextArea label="Observações" value={notes} onChange={setNotes} rows={3} />
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12, marginTop: 16, paddingTop: 16, borderTop: '1px solid var(--color-border)' }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle}>Cancelar</button>
                    <SubmitBtn label="Baixar Equipamento" loading={saving} />
                </div>
            </form>
        </ModalWrapper>
    );
}
