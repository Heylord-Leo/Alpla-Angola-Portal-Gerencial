import React, { useState } from 'react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { ModalWrapper, SubmitBtn, ErrorBox, Field, TextArea, cancelBtnStyle } from './EquipmentFormModal';

interface Props { equipmentId: string; onClose: () => void; onSuccess: () => void; }

export function ReserveEquipmentModal({ equipmentId, onClose, onSuccess }: Props) {
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [reservedFor, setReservedFor] = useState('');
    const [reason, setReason] = useState('');
    const [notes, setNotes] = useState('');

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            setSaving(true); setError('');
            await itEquipmentApi.reserve(equipmentId, { reservedFor, reason, notes });
            onSuccess();
        } catch (err: any) { setError(err.message); } finally { setSaving(false); }
    };

    return (
        <ModalWrapper title="Reservar Equipamento" onClose={onClose}>
            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {error && <ErrorBox msg={error} />}
                <Field label="Reservado Para" value={reservedFor} onChange={setReservedFor} placeholder="Nome ou departamento" />
                <Field label="Motivo" value={reason} onChange={setReason} placeholder="Ex: Novo colaborador chegando em..." />
                <TextArea label="Notas" value={notes} onChange={setNotes} rows={2} />
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 8 }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle}>Cancelar</button>
                    <SubmitBtn label="Reservar" loading={saving} />
                </div>
            </form>
        </ModalWrapper>
    );
}
