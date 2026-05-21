import React, { useState } from 'react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { ModalWrapper, SubmitBtn, ErrorBox, Field, TextArea, cancelBtnStyle } from './EquipmentFormModal';

interface Props { equipmentId: string; onClose: () => void; onSuccess: () => void; }

export function LostEquipmentModal({ equipmentId, onClose, onSuccess }: Props) {
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [responsiblePerson, setResponsiblePerson] = useState('');
    const [notes, setNotes] = useState('');

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            setSaving(true); setError('');
            await itEquipmentApi.markLost(equipmentId, { responsiblePerson, notes });
            onSuccess();
        } catch (err: any) { setError(err.message); } finally { setSaving(false); }
    };

    return (
        <ModalWrapper title="Marcar como Perdido" onClose={onClose}>
            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {error && <ErrorBox msg={error} />}
                <div style={{ padding: '8px 12px', backgroundColor: '#fef2f2', borderRadius: 8, fontSize: '0.82rem', color: '#dc2626', border: '1px solid #fecaca' }}>
                    ⚠️ Esta ação marcará o equipamento como perdido e encerrará qualquer atribuição ativa.
                </div>
                <Field label="Responsável" value={responsiblePerson} onChange={setResponsiblePerson} placeholder="Nome da pessoa responsável" />
                <TextArea label="Detalhes" value={notes} onChange={setNotes} rows={3} />
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 8 }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle}>Cancelar</button>
                    <SubmitBtn label="Marcar como Perdido" loading={saving} />
                </div>
            </form>
        </ModalWrapper>
    );
}
